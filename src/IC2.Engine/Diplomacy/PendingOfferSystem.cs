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
/// <strong>The candidate draw, the peace/alive gate and the 1-in-3 roll are confirmed, mechanical, and
/// this task's to implement.</strong> The report gives the exact short-circuit order of the
/// <c>&amp;&amp;</c> chain, which this reproduces draw-for-draw: <c>r</c> is drawn unconditionally, the
/// relation and alive checks are evaluated with no further draw, the <c>Random(3)</c> draw only happens
/// once both pass, and the "<c>r</c> is AI" check — which also excludes the active human seat itself,
/// since it is never AI-controlled — is evaluated last, only once the chance roll passes too.
/// </para>
/// <para>
/// <strong>Trade versus alliance is not this task's to decide.</strong> The report's own words: "possibly
/// offer = (r, 1)... possibly offer = (r, 2), overrides trade... from a tax-base comparison" — then, in
/// its own "Still open" list, "<c>FUN_00452034</c>'s trade/alliance decision rule was read only as far as
/// needed here. It is AI decision code." That is exactly the willingness judgement
/// <c>docs/task-catalogue.md</c> T19's Note reserves for T22's opinion-score layer. This task's confirmed
/// candidate-selection shell is the read surface that note describes; the trade/alliance choice defaults
/// to trade — <c>[designed]</c>, not invented blind: every one of the 6 observed pending offers in the
/// corpus is a trade offer, and the alliance code is itself tagged <c>[derived]</c> (never observed) by
/// <c>pending-offer-block-army-split-and-naupactus.md</c>. <c>improved</c>'s opinion-score layer replaces
/// this one default, not the candidate legality gates above it.
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

        var (state, announcement) = Apply(context.State, context.Ruleset, context.Rng);
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
        GameState state, Ruleset ruleset, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
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

        if (!rng.NextChance(1, 3))
        {
            return (state, null);
        }

        if (candidate.Control != SeatControl.Ai)
        {
            return (state, null);
        }

        // [designed]: trade, always -- see this type's remarks.
        var proposedRelation = ruleset.Diplomacy.StateCodes.Trade;
        var offer = new PendingDiplomaticOffer(candidateId, proposedRelation);
        state = state with { PendingOffer = offer };

        var dialogText = DialogText(candidate.Name, active.Name, proposedRelation, ruleset);
        var announcement = new PendingDiplomaticOfferAnnounced(
            candidateId, candidate.Name, active.Id, active.Name, proposedRelation, dialogText);

        return (state, announcement);
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
