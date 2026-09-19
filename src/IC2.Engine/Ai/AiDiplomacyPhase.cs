using System.Globalization;
using IC2.Engine.Diplomacy;
using IC2.Engine.Diplomacy.Commands;
using IC2.Engine.Model;

namespace IC2.Engine.Ai;

/// <summary>
/// <c>docs/game-design.md</c> §AI phase 3: "<em>seek peace if losing and strength ratio is poor; consider
/// alliance offers from nations with a shared enemy.</em>"
/// </summary>
/// <remarks>
/// <para>
/// <strong>This phase proposes; it never decides for the other side.</strong> Whether an offer is
/// accepted is T19's confirmed state machine — <c>ProposeTradeCommandHandler</c>,
/// <c>ProposeAllianceCommandHandler</c> and <c>MakePeaceCommandHandler</c> — and none of it is
/// reimplemented here. What this phase does is read the same gates those handlers apply, so that an offer
/// is only ever placed when it will be accepted.
/// </para>
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
    /// <param name="into">The collecting list.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static void Propose(AiView view, AiPersonalityProfile personality, List<AiCandidate> into)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(personality);
        ArgumentNullException.ThrowIfNull(into);

        var ownPower = view.TotalArmyPower(view.NationId);

        foreach (var other in view.OtherLivingNations())
        {
            if (view.RelationBetween(view.NationId, other.Id) is not { } relation)
            {
                // Not both in the relation matrix: every command in this phase would throw or refuse.
                continue;
            }

            ProposePeace(view, other, relation, ownPower, into);
            ProposeAlliance(view, personality, other, relation, into);
            ProposeTrade(view, personality, other, relation, into);
        }
    }

    private static void ProposePeace(
        AiView view, NationState other, int relation, long ownPower, List<AiCandidate> into)
    {
        if (relation != view.WarCode || other.Control != SeatControl.Human)
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

    private static void ProposeAlliance(
        AiView view, AiPersonalityProfile personality, NationState other, int relation, List<AiCandidate> into)
    {
        // The relation must already be peace or trade. That is narrower than what the handler would
        // accept, and deliberately so, in both directions:
        //
        //  - Below peace is a cooldown. FormAlliance would overwrite it outright and the handler would
        //    accept from a human target, but allying out of a cooldown the nation itself imposed is not
        //    behaviour worth having; declining keeps the confirmed cooldown meaningful.
        //  - Above trade is alliance (already allied) or WAR. ProposeAllianceCommandHandler refuses an
        //    AI target while either side is at war, but a human target "always accepts" -- so without
        //    this gate the AI could declare war on a human seat and ally with it in the same turn,
        //    undoing its own declaration. The CLI demo printed exactly that ("SOUTHERN LEAGUE DECLARES
        //    WAR ON NORTHERN LEAGUE." followed by "Southern League forms an alliance with Northern
        //    League.") before this gate existed. Nothing refused it; it was simply nonsense.
        if (relation != view.PeaceCode && relation != view.Ruleset.Diplomacy.StateCodes.Trade)
        {
            return;
        }

        // ProposeAllianceCommandHandler's remaining gate for an AI target: neither side at war with
        // anyone at all, not merely with each other.
        if (other.Control == SeatControl.Ai
            && (RelationTransitions.IsAtWarWithAnyone(view.State, view.Ruleset, view.NationId)
                || RelationTransitions.IsAtWarWithAnyone(view.State, view.Ruleset, other.Id)))
        {
            return;
        }

        var shared = SharedEnemyCount(view, other.Id);
        var score = (AiWeights.ProposeAllianceBaseScore + (shared * AiWeights.SharedEnemyBonus))
                    * personality.LoyaltyToAlliancesPermille
                    / AiWeights.PermilleScale;
        if (score < AiWeights.MinimumActionScore)
        {
            return;
        }

        into.Add(AiCandidate.Single(
            AiPhase.Diplomacy,
            "propose-alliance",
            new ProposeAllianceCommand(view.NationId, other.Id),
            score,
            Inv(
                "propose alliance to {0}: {1} shared enemies, loyalty {2} permille",
                other.Id, shared, personality.LoyaltyToAlliancesPermille)));
    }

    private static void ProposeTrade(
        AiView view, AiPersonalityProfile personality, NationState other, int relation, List<AiCandidate> into)
    {
        // ProposeTradeCommandHandler accepts exactly one relation value: peace. Below it is a cooldown,
        // at trade it is already trading, above it is alliance or war.
        if (relation != view.PeaceCode)
        {
            return;
        }

        var score = AiWeights.ProposeTradeBaseScore
                    * personality.LoyaltyToAlliancesPermille
                    / AiWeights.PermilleScale;
        if (score < AiWeights.MinimumActionScore)
        {
            return;
        }

        into.Add(AiCandidate.Single(
            AiPhase.Diplomacy,
            "propose-trade",
            new ProposeTradeCommand(view.NationId, other.Id),
            score,
            Inv(
                "propose trade to {0}: relation {1} is peace, loyalty {2} permille",
                other.Id, relation, personality.LoyaltyToAlliancesPermille)));
    }

    /// <summary>
    /// How many nations both the acting nation and <paramref name="otherId"/> are at war with —
    /// <c>docs/game-design.md</c> §AI's "nations with a shared enemy", counted over
    /// <see cref="GameState.Nations"/> in its own stable order.
    /// </summary>
    private static int SharedEnemyCount(AiView view, string otherId)
    {
        var count = 0;
        foreach (var third in view.State.Nations)
        {
            if (third.Eliminated
                || string.Equals(third.Id, view.NationId, StringComparison.Ordinal)
                || string.Equals(third.Id, otherId, StringComparison.Ordinal))
            {
                continue;
            }

            if (view.IsAtWar(view.NationId, third.Id) && view.IsAtWar(otherId, third.Id))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>See <c>AiMilitaryPhase.Inv</c>: the per-seed log has to be locale-independent.</summary>
    private static string Inv(string format, params object?[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, format, arguments);
}
