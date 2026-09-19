using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.News;

namespace IC2.Engine.Diplomacy;

/// <summary>
/// Reacts to T16's <see cref="PeaceTreatyTriggered"/> — the post-battle treaty, <c>FUN_00450C68</c>
/// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong> — with the
/// honourable-peace branch (DoD 8) or the full reparations sequence (DoD 7), and finally the
/// ally-of-either-side peace cascade both branches share.
/// </summary>
/// <remarks>
/// <para>
/// <strong>An event subscriber, not a direct call.</strong> T16 publishes <see cref="PeaceTreatyTriggered"/>
/// rather than calling into this task, so the two do not depend on each other's internals
/// (<c>docs/task-catalogue.md</c> T16 and T19 Scope). This system reads
/// <see cref="SystemContext.PublishedEvents"/> — T40's seam — for every trigger published earlier in the
/// same run, which is what lets it react regardless of which phase or command eventually publishes one;
/// nothing in this build wires a command to <see cref="InstantBattleResolver.ResolveField"/> yet, so this
/// task's own tests fire the trigger directly rather than through a real attack.
/// </para>
/// <para>
/// Registered at <see cref="TurnPhase.SeatEnd"/>, default order (0) — well before
/// <c>NewsLogWriterSeatEnd</c>'s <c>int.MaxValue</c>, so every peace-related news line this system
/// writes directly (see below) has already landed in <see cref="GameState.NewsLog"/> before the round's
/// own bookkeeping runs, and every line an earlier system in the same phase published through
/// <see cref="SystemContext.Events"/> is visible on <see cref="SystemContext.PublishedEvents"/> here too.
/// </para>
/// <para>
/// <strong>Every peace-sequence line here is written directly through <see cref="NewsLogWriter.Append"/></strong>,
/// the same way <see cref="RelationTransitions.DeclareWar"/> does, rather than published through
/// <see cref="SystemContext.Events"/> for the standard per-event pipeline to render later: this system
/// runs inside <see cref="TurnPhase.SeatEnd"/> itself, and publishing through <see cref="SystemContext.Events"/>
/// would only be picked up by that phase's own <c>NewsLogWriterSeatEnd</c> — which this system already
/// runs before, by design — so publishing and rendering directly here keeps the whole treaty's news in one
/// place and one order, exactly as <c>FUN_00450C68</c> writes it.
/// </para>
/// </remarks>
[GameSystem(TurnPhase.SeatEnd, "diplomacy.peace-treaty")]
public sealed class PeaceTreatySystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        foreach (var published in context.PublishedEvents)
        {
            if (published.Event is PeaceTreatyTriggered treaty)
            {
                state = Apply(state, context.Ruleset, treaty, context.Rng);
            }
        }

        return state;
    }

    /// <summary>
    /// The pure treaty reaction, directly callable so a test can drive it from a fabricated
    /// <see cref="PeaceTreatyTriggered"/> with no battle and no full pipeline run.
    /// </summary>
    public static GameState Apply(GameState state, Ruleset ruleset, PeaceTreatyTriggered treaty, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(treaty);
        ArgumentNullException.ThrowIfNull(rng);

        var codes = ruleset.Diplomacy.StateCodes;
        var winnerId = treaty.WinnerNationId;
        var loserId = treaty.LoserNationId;

        // score(n) and armies(n) are read BEFORE the relation write below -- the original's own treaty
        // routine computes both from the state exactly as the battle left it (which already reflects the
        // battle's own casualties and the loser's own deleteArmy/scatter), then only afterwards moves the
        // relation to its post-war cooldown.
        var honourable = HonourablePeaceGate.Fires(state, ruleset, winnerId, loserId);

        // "setRelation(winner, loser, 0)" -- the setter's own peace-cooldown translation, from whatever the
        // pair's relation was going into the treaty (war, for every treaty this build produces).
        state = RelationTransitions.BreakToPeace(state, ruleset, winnerId, loserId);

        var winnerName = RelationTransitions.NameOf(state, winnerId);
        var loserName = RelationTransitions.NameOf(state, loserId);

        if (honourable)
        {
            state = NewsLogWriter.Append(
                state, new DomainEvent[] { new PeaceHonourableAgreed(winnerName, loserName) }, ruleset.NewsLog);
        }
        else
        {
            var reparations = ReparationsFormula.Compute(LoserTaxBase(state, loserId), treaty.LoserCityCount, ruleset, rng);

            var loser = state.NationById(loserId);
            var winner = state.NationById(winnerId);
            if (loser is not null && winner is not null)
            {
                state = state with
                {
                    Nations = ValueList.From(state.Nations.Select(n =>
                        string.Equals(n.Id, loserId, StringComparison.Ordinal) ? n with { Treasury = n.Treasury - reparations }
                        : string.Equals(n.Id, winnerId, StringComparison.Ordinal) ? n with { Treasury = n.Treasury + reparations }
                        : n)),
                };
            }

            // The loser's other trade/alliance partners are set to the CooldownAfterPeaceTerms cooldown
            // ("for each n: if relation[loser][n] is trade or alliance: setRelation(loser, n, -10)") --
            // straight writes, no news line of their own (the report attaches no literal to this step).
            foreach (var otherId in state.Relations.NationIds)
            {
                if (string.Equals(otherId, loserId, StringComparison.Ordinal))
                {
                    continue;
                }

                var relation = state.Relations.Get(loserId, otherId);
                if (relation == codes.Trade || relation == codes.Alliance)
                {
                    state = state with
                    {
                        Relations = state.Relations.WithRelation(
                            loserId, otherId, ruleset.Diplomacy.CooldownAfterPeaceTerms),
                    };
                }
            }

            state = NewsLogWriter.Append(
                state,
                new DomainEvent[]
                {
                    new PeaceSuedFor(loserName, winnerName),
                    new PeaceEndsTradingAgreements(loserName),
                    new PeaceEndsAlliances(loserName),
                    new PeacePaysReparations(loserName, NewsLogWriter.FormatGroupedAmount(reparations)),
                },
                ruleset.NewsLog);
        }

        state = ApplyAllyPeaceCascade(state, ruleset, winnerId, loserId);
        return state;
    }

    /// <summary>
    /// "Finally: any ally of either side still at war with the other gets setRelation(..., -8), with news
    /// '&lt;A&gt; and &lt;B&gt; have agreed to end their war.'" — checked for allies of both the winner and
    /// the loser, in <see cref="DiplomaticRelations.NationIds"/> order for a deterministic result.
    /// </summary>
    private static GameState ApplyAllyPeaceCascade(GameState state, Ruleset ruleset, string winnerId, string loserId)
    {
        var codes = ruleset.Diplomacy.StateCodes;

        foreach (var (side, other) in new[] { (winnerId, loserId), (loserId, winnerId) })
        {
            foreach (var allyId in state.Relations.NationIds)
            {
                if (string.Equals(allyId, winnerId, StringComparison.Ordinal)
                    || string.Equals(allyId, loserId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (state.Relations.Get(allyId, side) != codes.Alliance || state.Relations.Get(allyId, other) != codes.War)
                {
                    continue;
                }

                state = state with
                {
                    Relations = state.Relations.WithRelation(allyId, other, ruleset.Diplomacy.CooldownAfterAllyPeace),
                };

                var allyName = RelationTransitions.NameOf(state, allyId);
                var otherName = RelationTransitions.NameOf(state, other);
                state = NewsLogWriter.Append(
                    state, new DomainEvent[] { new PeaceAllyAgreement(allyName, otherName) }, ruleset.NewsLog);
            }
        }

        return state;
    }

    private static int LoserTaxBase(GameState state, string loserId) => state.NationById(loserId)?.TaxBase ?? 0;
}
