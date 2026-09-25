using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Diplomacy;

/// <summary>
/// The pending trade/alliance offer's clear-and-reroll step (DoD 10) — <c>FUN_00452034</c>
/// <strong>[confirmed: news-log-format-and-messages.md Q5]</strong>, run at the start of every
/// <strong>human</strong> seat's turn:
/// <code>
/// offer = (-1, ·)                                    // cleared every human turn start, accepted or not
/// r = Random(16)
/// if relation[human][r] == 0 and r alive and Random(3) == 0 and r is AI:
///     possibly offer = (r, 1)                        // trade, from a tax-base comparison
///     possibly offer = (r, 2)                        // alliance, overrides trade
/// </code>
/// </summary>
/// <remarks>
/// <para>
/// <strong>The candidate draw, the peace/alive gate and the roll are confirmed, mechanical.</strong> The
/// report gives the exact short-circuit order of the <c>&amp;&amp;</c> chain, which this reproduces
/// draw-for-draw: <c>r</c> is drawn unconditionally, the relation and alive checks are evaluated with no
/// further draw, the <c>Random(<see cref="DiplomacyRules.OfferRollDenominator"/>)</c> draw only happens
/// once both pass, and the "<c>r</c> is AI" check — which also excludes the active human seat itself,
/// since it is never AI-controlled — is evaluated last, only once the chance roll passes too.
/// </para>
/// <para>
/// <strong>T82 (#359, bug #357): trade versus alliance, from the report's §1b pseudocode</strong>
/// <strong>[confirmed: decompiled-ai-offers-to-human-seats.md]</strong> — the willingness judgement an
/// earlier task's own Note deferred is exactly this: <c>floor = trades(h) &lt; cap ? 0 : min taxBase over
/// h's own partners; offer = TRADE if taxBase[r] &gt; floor and r has some partner k poorer than h (the
/// same "swap a poorer partner for a richer one" shape <see cref="AiOwnDiplomacyRule.FindTradeSwap"/>
/// uses for the AI's own trades); offer = ALLIANCE instead — overriding a trade already set this same
/// roll — if r is a <see cref="NeighbourGeography"/> neighbour of h and h is not at war with anyone.</c>
/// Every one of the 6 observed pending offers in the corpus is a trade offer, consistent with this rule.
/// Rework round 1, N7 correction: this used to claim none of those six human seats had a neighbouring AI
/// at all; the report's own worked example (§1b, Rome → Thracia) gives a narrower reason instead —
/// Rome's own offers stayed trade-only because Rome was at war with Gaul, which alone forces the
/// alliance override's "h is not at war with anyone" clause to fail regardless of any proposer's
/// neighbour status. The broader "no neighbouring AI" reading is not the report's own claim and is
/// retracted here rather than left implied.
/// </para>
/// <para>
/// Registered at <see cref="TurnPhase.SeatStart"/>, matching <c>TPremierForm_StartTurn</c>'s own place in
/// the turn ("The original announces pending trade and alliance proposals here. Consumer: T19
/// diplomacy" — <see cref="TurnPhase.SeatStart"/>'s own remarks).
/// </para>
/// </remarks>
[GameSystem(TurnPhase.SeatStart, "diplomacy.pending-offer-reroll")]
public sealed class PendingOfferSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var (state, announcement) = Apply(context.State, context.Ruleset, context.World, context.Rng);
        if (announcement is not null)
        {
            context.Events.Publish(announcement);
        }

        return state;
    }

    /// <summary>
    /// The pure clear-and-reroll pass, directly callable so a test can pin an exact result under a fixed
    /// seed without building a full <see cref="SystemContext"/>.
    /// </summary>
    public static (GameState State, PendingDiplomaticOfferAnnounced? Announcement) Apply(
        GameState state, Ruleset ruleset, World world, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(rng);

        // FUN_00451FDC "runs the AI seats and calls FUN_00452034 when it reaches a human" -- the
        // clear-and-reroll only ever runs on a human seat's turn, an AI seat's own turn start never
        // touches the block at all.
        var active = state.NationById(state.ActiveNationId);
        if (active is null || active.Control != SeatControl.Human)
        {
            return (state, null);
        }

        // "offer = (-1, ·) -- cleared every human turn start, accepted or not" -- unconditional from here.
        state = state with { PendingOffer = null };

        var ids = state.Relations.NationIds;
        var candidateIndex = rng.NextInt(ids.Count);
        var candidateId = ids[candidateIndex];
        var candidate = state.NationById(candidateId);
        if (candidate is null)
        {
            return (state, null);
        }

        var peaceCode = ruleset.Diplomacy.StateCodes.Peace;
        var relationOk = state.Relations.Get(active.Id, candidateId) == peaceCode;
        var aliveOk = !candidate.Eliminated;
        if (!(relationOk && aliveOk))
        {
            return (state, null);
        }

        if (!rng.NextChance(1, ruleset.Diplomacy.OfferRollDenominator))
        {
            return (state, null);
        }

        if (candidate.Control != SeatControl.Ai)
        {
            return (state, null);
        }

        var proposedRelation = DecideOfferType(state, ruleset, world, active, candidate);
        if (proposedRelation is null)
        {
            return (state, null);
        }

        var offer = new PendingDiplomaticOffer(candidateId, proposedRelation.Value);
        state = state with { PendingOffer = offer };

        var dialogText = DialogText(candidate.Name, active.Name, proposedRelation.Value, ruleset);
        var announcement = new PendingDiplomaticOfferAnnounced(
            candidateId, candidate.Name, active.Id, active.Name, proposedRelation.Value, dialogText);

        return (state, announcement);
    }

    /// <summary>
    /// Report §1b's trade/alliance decision, once the candidate has cleared every gate above
    /// <strong>[confirmed: decompiled-ai-offers-to-human-seats.md]</strong>:
    /// <code>
    /// floor = (trades(h) &lt; cap) ? 0 : min taxBase over h's own trade partners
    /// if taxBase[r] &gt; floor and r has a partner k with taxBase[k] &lt; taxBase[h]: offer = TRADE
    /// if r in neighbours(h) and not atWar(h): offer = ALLIANCE                      // overrides trade
    /// </code>
    /// <see langword="null"/> if neither condition holds -- the roll passed, but the candidate offers
    /// nothing after all, exactly as a human turn start with no visible offer looks today.
    /// </summary>
    private static int? DecideOfferType(
        GameState state, Ruleset ruleset, World world, NationState human, NationState candidate)
    {
        var codes = ruleset.Diplomacy.StateCodes;
        int? proposedRelation = null;

        var humanPartners = TradePartnerCap.CurrentPartners(state, ruleset, human.Id, excluding: candidate.Id);
        var floor = humanPartners.Count < ruleset.Diplomacy.MaxTradePartners
            ? 0
            : humanPartners.Min(partnerId => state.NationById(partnerId)?.TaxBase ?? 0);

        var candidateHasAPartnerPoorerThanHuman = TradePartnerCap
            .CurrentPartners(state, ruleset, candidate.Id, excluding: human.Id)
            .Any(partnerId => (state.NationById(partnerId)?.TaxBase ?? int.MaxValue) < human.TaxBase);

        if (candidate.TaxBase > floor && candidateHasAPartnerPoorerThanHuman)
        {
            proposedRelation = codes.Trade;
        }

        if (NeighbourGeography.AreNeighbours(world, human.Id, candidate.Id)
            && !RelationTransitions.IsAtWarWithAnyone(state, ruleset, human.Id))
        {
            proposedRelation = codes.Alliance;
        }

        return proposedRelation;
    }

    /// <summary>
    /// The exact literal the original shows — <c>news-log-format-and-messages.md</c> Q5:
    /// <c>"X wants to trade with Y."</c> / <c>"X wants to form an alliance with Y."</c>, period-terminated,
    /// word-for-word (distinct from the corpus's own paraphrase entry, tagged as a paraphrase for exactly
    /// this reason).
    /// </summary>
    private static string DialogText(string proposingName, string targetName, int proposedRelationCode, Ruleset ruleset)
    {
        var codes = ruleset.Diplomacy.StateCodes;
        var verb = proposedRelationCode == codes.Alliance ? "form an alliance" : "trade";
        return $"{proposingName} wants to {verb} with {targetName}.";
    }
}
