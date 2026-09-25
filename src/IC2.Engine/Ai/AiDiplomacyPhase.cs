using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;
using static IC2.Engine.Ai.AiFormat;

namespace IC2.Engine.Ai;

/// <summary>
/// <c>docs/game-design.md</c> §AI phase 3: "<em>seek peace if losing and strength ratio is poor; consider
/// alliance offers from nations with a shared enemy.</em>"
/// </summary>
/// <remarks>
/// <para>
/// <strong>Peace is proposed only to a human seat, and that is a rule of the engine, not a preference.</strong>
/// <c>MakePeaceCommandHandler</c> refuses every proposal whose target is
/// <see cref="SeatControl.Ai"/>, unconditionally: "<em>A human-controlled nation always accepts: the
/// refusal applies only to an AI target</em>". So in an all-AI game this candidate can never be placed,
/// and proposing one anyway would be a guaranteed rejection — the exact thing
/// <c>docs/task-catalogue.md</c> T22 Done-when 1's "zero rejected commands" forbids. It is still built,
/// and still tested, because a mixed scenario (the shipped <c>toy-3city.json</c> is one) does reach it.
/// </para>
/// <para>
/// <strong>T82 (#359, bug #357): an AI never writes trade or an alliance to a human seat.</strong> Before
/// this task, this phase proposed <see cref="ProposeAllianceCommand"/> and <see cref="ProposeTradeCommand"/>
/// toward <em>any</em> nation, human seats included, and <c>ProposeAllianceCommandHandler</c>'s "always
/// accepted from a human seat" branch — correct for genuine hotseat human-to-human play — then wrote the
/// alliance immediately. But <c>decompiled-ai-offers-to-human-seats.md</c> §1a/§2 is explicit that the
/// original's AI never calls the human Politics-screen handlers at all: toward a human it can only
/// declare war or raise a turn-start notice (<see cref="PendingOfferSystem"/>), and the notice never
/// writes a relation. So this phase's own alliance and trade methods
/// (<see cref="ProposeOwnAlliance"/>, <see cref="ProposeOwnTrade"/>, <see cref="ProposeOwnTradeSwap"/>)
/// now target only <see cref="SeatControl.Ai"/> nations, through <see cref="AiFormAllianceCommand"/> and
/// <see cref="AiFormTradeCommand"/> — new commands, not <see cref="ProposeAllianceCommand"/>/
/// <see cref="ProposeTradeCommand"/>, because those two are <c>TPolitics_Make*</c>'s own gates
/// (human-initiated), the wrong rule for the AI's own direct writes (<c>FUN_0044FB7C</c>, report §1a).
/// </para>
/// <para>
/// <strong>Direct writes, no consent step — and busier gates than the old heuristic.</strong>
/// <see cref="AiOwnDiplomacyRule"/> carries the deterministic half of <c>FUN_0044FB7C</c>: the busy gate,
/// <c>protected</c>, the alliance-partner search (a partner already at war with a shared, unprotected
/// neighbour — never simply "any nation at peace", which is what produced bug #357's ~38-alliance
/// cascade in round one of the classical scenario) and the trade/swap searches. See that type's own
/// remarks for why the two chance rolls (<c>Random(10)</c> for war, <c>Random(20)</c> for alliance) are
/// decided where they are rather than inside a handler.
/// </para>
/// <para>
/// <strong>Accepting a pending offer is deliberately absent.</strong>
/// <see cref="PendingOfferSystem.Apply"/> returns before it writes anything unless the active seat is
/// <see cref="SeatControl.Human"/> — "<em>the clear-and-reroll only ever runs on a human seat's turn, an
/// AI seat's own turn start never touches the block at all</em>". An AI seat therefore never has a
/// <see cref="GameState.PendingOffer"/> addressed to it, so an
/// <see cref="AcceptPendingOfferCommand"/> candidate would be a branch that can never be taken. Dead code
/// is a review finding in this project (<c>docs/build-process.md</c> §4.2 gate 5), so there is none.
/// </para>
/// </remarks>
public static class AiDiplomacyPhase
{
    /// <summary>Adds every diplomacy candidate this state offers to <paramref name="into"/>.</summary>
    /// <param name="view">The shared reads.</param>
    /// <param name="personality">The acting nation's personality.</param>
    /// <param name="rng">
    /// T82 Owns amendment (PR #378): the seat-turn's own <see cref="IRng"/>, the same instance
    /// <see cref="AiTurn.Run"/> passes into <see cref="AiMilitaryPhase.Propose"/> — see
    /// <see cref="ProposeOwnAlliance"/>'s own remarks for why sharing it (rather than seeding a second
    /// generator) is what makes the alliance roll turn-stable.
    /// </param>
    /// <param name="into">The collecting list.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static void Propose(AiView view, AiPersonalityProfile personality, IRng rng, List<AiCandidate> into)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(personality);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(into);

        var ownPower = view.TotalArmyPower(view.NationId);

        foreach (var other in view.OtherLivingNations())
        {
            if (other.Control != SeatControl.Human)
            {
                continue;
            }

            if (view.RelationBetween(view.NationId, other.Id) is not { } relation)
            {
                // Not both in the relation matrix: every command in this phase would throw or refuse.
                continue;
            }

            ProposePeace(view, other, relation, ownPower, into);
        }

        ProposeOwnAlliance(view, rng, into);
        ProposeOwnTrade(view, into);
        ProposeOwnTradeSwap(view, into);
    }

    private static void ProposePeace(
        AiView view, NationState other, int relation, long ownPower, List<AiCandidate> into)
    {
        if (relation != view.WarCode)
        {
            return;
        }

        var theirPower = view.TotalArmyPower(other.Id);
        var ratio = AiView.RatioPermille(ownPower, theirPower);
        if (ratio >= AiWeights.SuePeaceStrengthRatioPermille)
        {
            return;
        }

        into.Add(AiCandidate.Single(
            AiPhase.Diplomacy,
            "make-peace",
            new MakePeaceCommand(view.NationId, other.Id),
            AiWeights.MakePeaceBaseScore,
            Inv(
                "sue for peace with {0}: own army power {1} vs {2} (ratio {3} permille, sue below {4})",
                other.Id, ownPower, theirPower, ratio, AiWeights.SuePeaceStrengthRatioPermille)));
    }

    /// <summary>
    /// T82 (#359, bug #357): the alliance half of <c>FUN_0044FB7C</c>
    /// <strong>[confirmed: decompiled-ai-offers-to-human-seats.md §1a]</strong>. The partner search
    /// (<see cref="AiOwnDiplomacyRule.FindAlliancePartner"/>) is deterministic; only the
    /// <c>Random(20) == 0</c> roll below is this method's own.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>T82 Owns amendment (PR #378): the roll's stream is the seat-turn's own, exactly like the
    /// war roll's.</strong> Originally this method had no <see cref="IRng"/> to draw from — <see cref="AiTurn"/>
    /// called this phase's <c>Propose</c> with none, and <see cref="AiTurn"/>/<see cref="AiView"/> were
    /// outside this task's Owns list, so the call site could not be changed to add one. The Owns list has
    /// since been widened to exactly that one change (only passing the seat-turn's stream through), which
    /// is what <paramref name="rng"/> now is: the identical <see cref="IRng"/> instance
    /// <see cref="AiTurn.Run"/> already passes into <see cref="AiMilitaryPhase.Propose"/> for the war
    /// roll, threaded one call further. <see cref="IRng.ForStream"/> derives an independent, deterministic
    /// sequence from <paramref name="rng"/>'s own <see cref="IRng.Seed"/> and the name given it — never
    /// from how many draws anything has made — so keying this roll by the seat and the turn on this same
    /// instance makes it exactly as turn-stable as <see cref="AiMilitaryPhase.ProposeOwnWarDeclaration"/>'s
    /// own: a re-evaluated proposal pass later in the same turn sees the identical draw, not a fresh one.
    /// No second generator is ever seeded from <see cref="Model.GameState.RandomSeed"/> — that was the
    /// earlier, weaker substitute this amendment removes.
    /// </para>
    /// </remarks>
    private static void ProposeOwnAlliance(AiView view, IRng rng, List<AiCandidate> into)
    {
        if (AiOwnDiplomacyRule.IsBusy(view.State, view.Ruleset, view.NationId))
        {
            return;
        }

        var partner = AiOwnDiplomacyRule.FindAlliancePartner(view.State, view.Ruleset, view.World, view.NationId);
        if (partner is null)
        {
            return;
        }

        var denominator = view.Ruleset.Diplomacy.AiOwnDiplomacy.AllianceRollDenominator;
        var roll = rng.ForStream(
            Inv("ai.ownDiplomacy.allianceRoll:{0}:{1}", view.NationId, view.State.Calendar.TurnIndex));
        if (!roll.NextChance(1, denominator))
        {
            return;
        }

        into.Add(AiCandidate.Single(
            AiPhase.Diplomacy,
            "ai-form-alliance",
            new AiFormAllianceCommand(view.NationId, partner),
            OwnAllianceScore,
            Inv(
                "ally with {0}: FUN_0044FB7C's own alliance search picked it (an AI already at war with a "
                + "shared, unprotected neighbour), and the Random({1}) roll hit",
                partner, denominator)));
    }

    /// <summary>
    /// T82 (#359, bug #357): the direct trade half of <c>FUN_0044FB7C</c>
    /// <strong>[confirmed: decompiled-ai-offers-to-human-seats.md §1a]</strong> -- no chance roll, every
    /// currently eligible AI partner is offered as its own candidate (<see cref="AiOwnDiplomacyRule.EligibleTradePartners"/>),
    /// so the greedy loop can accept more than one over successive actions this turn, each time re-
    /// checking both sides' caps against the current state.
    /// </summary>
    /// <remarks>
    /// Rework round 1, N4: the original picks a trade partner in index order -- loop 1 (skipping the
    /// running war candidate), then loop 3, both over the nation table in its own fixed slot order
    /// (report §1a). Scoring every eligible partner identically at <see cref="OwnTradeScore"/> used to
    /// leave that choice to <c>AiTurn.Select</c>'s own exact-tie break, a draw from the seat's stream --
    /// with more than one eligible partner this picked a random one, not the nation-order-first one the
    /// original always does. Each successive partner in
    /// <see cref="AiOwnDiplomacyRule.EligibleTradePartners"/>' own stable
    /// <see cref="GameState.Nations"/> order is now scored one point lower than the last, so the first
    /// one in that order always wins outright -- no tie, so no draw -- reproducing the original's index
    /// order without touching <c>AiTurn.Select</c> itself (outside this task's Owns list). The largest
    /// step this can ever take (one fewer than the world's own nation count) stays comfortably above
    /// <see cref="OwnTradeSwapScore"/>, so the relative ordering against a swap candidate is unaffected.
    /// </remarks>
    private static void ProposeOwnTrade(AiView view, List<AiCandidate> into)
    {
        var rank = 0;
        foreach (var partnerId in AiOwnDiplomacyRule.EligibleTradePartners(view.State, view.Ruleset, view.NationId))
        {
            into.Add(AiCandidate.Single(
                AiPhase.Diplomacy,
                "ai-form-trade",
                new AiFormTradeCommand(view.NationId, partnerId),
                OwnTradeScore - rank,
                Inv(
                    "trade with {0}: at peace, both sides under {1} partners",
                    partnerId, view.Ruleset.Diplomacy.MaxTradePartners)));
            rank++;
        }
    }

    /// <summary>
    /// T82 (#359, bug #357): the closing "swap a poorer partner for a richer one" loop of
    /// <c>FUN_0044FB7C</c> <strong>[confirmed: decompiled-ai-offers-to-human-seats.md §1a]</strong>.
    /// </summary>
    private static void ProposeOwnTradeSwap(AiView view, List<AiCandidate> into)
    {
        if (AiOwnDiplomacyRule.FindTradeSwap(view.State, view.Ruleset, view.NationId) is not { } swap)
        {
            return;
        }

        into.Add(AiCandidate.Single(
            AiPhase.Diplomacy,
            "ai-swap-trade-partner",
            new AiSwapTradePartnerCommand(view.NationId, swap.PoorerPartner, swap.RicherCandidate),
            OwnTradeSwapScore,
            Inv(
                "swap trade partner {0} for richer {1}",
                swap.PoorerPartner, swap.RicherCandidate)));
    }

    /// <summary>
    /// Comfortably above every ordinary military, economy and trade candidate, second only to
    /// <see cref="AiMilitaryPhase.OwnWarDeclarationScore"/> — see <see cref="ProposeOwnAlliance"/>'s own
    /// remarks for why that ordering matters (a war declaration that is going to land this turn always
    /// beats a trade or alliance candidate touching the same target, matching the original's own loop
    /// order). Rework round 1, N7: this no longer stands in for anything the roll itself lacks — the
    /// Owns amendment gave <see cref="ProposeOwnAlliance"/> a genuinely turn-stable <see cref="IRng"/>,
    /// the same one <see cref="AiMilitaryPhase"/>'s war roll uses, so this score's own job is purely the
    /// candidate-ordering one above.
    /// </summary>
    /// <remarks>
    /// Rework round 1, N5: this constant (and the two below) live outside <see cref="AiWeights"/>, so
    /// they are not covered by T79's own Done-when 2 ("nothing reads a C# constant") the way
    /// <c>AiWeights.cs</c>'s own fields are — left here rather than moved (out of T82's Owns list to
    /// relocate), flagged so T79 (#355) finds them when that task widens its own sweep.
    /// </remarks>
    private const long OwnAllianceScore = 9_000_000;

    /// <summary>On <see cref="AiWeights"/>'s own scale, just above the old human-facing propose-trade score.</summary>
    private const long OwnTradeScore = 520;

    /// <summary>Slightly below <see cref="OwnTradeScore"/>: a swap is a smaller net gain than a fresh partner.</summary>
    private const long OwnTradeSwapScore = 480;
}
