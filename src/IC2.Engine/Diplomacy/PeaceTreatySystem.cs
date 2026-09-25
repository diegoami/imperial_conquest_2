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
                state = Apply(state, context.Ruleset, context.World, treaty, context.Rng);
            }
        }

        return state;
    }

    /// <summary>
    /// The pure treaty reaction, directly callable so a test can drive it from a fabricated
    /// <see cref="PeaceTreatyTriggered"/> with no battle and no full pipeline run.
    /// </summary>
    /// <param name="world">
    /// T88: the ally-peace cascade's border gate reads <see cref="NeighbourGeography.AreNeighbours"/>,
    /// which needs the world the battle happened on (report §3: "an ally makes peace alongside its
    /// partner only if it does not border the enemy").
    /// </param>
    public static GameState Apply(GameState state, Ruleset ruleset, World world, PeaceTreatyTriggered treaty, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(world);
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

        state = ApplyAllyPeaceCascade(state, ruleset, world, winnerId, loserId);
        return state;
    }

    /// <summary>
    /// The human-consent treaty's Yes branch (T88, DoD 3): always the honourable line, never reparations
    /// — <c>[derived]</c> per the report (§2.3: the dialog's own gate already requires
    /// <c>armies(winner) &lt; armies(loser)</c>, and nothing can move between that gate and the human's
    /// Yes because the dialog is modal, so <c>HonourablePeaceGate</c>'s score/armies comparison would
    /// always agree anyway). Called only from <c>AcceptPeaceTreatyCommand</c>'s handler, once the human
    /// has answered Yes to a <see cref="Battle.PeaceTreatyOffered"/> offer <see cref="Presentation.GameSession"/>
    /// captured; a No calls neither this nor anything else ("No writes nothing").
    /// </summary>
    public static GameState ApplyHumanConsentedPeace(
        GameState state, Ruleset ruleset, World world, string winnerId, string loserId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(winnerId);
        ArgumentNullException.ThrowIfNull(loserId);

        state = RelationTransitions.BreakToPeace(state, ruleset, winnerId, loserId);

        var winnerName = RelationTransitions.NameOf(state, winnerId);
        var loserName = RelationTransitions.NameOf(state, loserId);
        state = NewsLogWriter.Append(
            state, new DomainEvent[] { new PeaceHonourableAgreed(winnerName, loserName) }, ruleset.NewsLog);

        return ApplyAllyPeaceCascade(state, ruleset, world, winnerId, loserId);
    }

    /// <summary>
    /// The treaty's ally loop, re-checked against the decompile (T88, report §3 —
    /// <c>0x00450F79</c>–<c>0x004510F8</c>). For each nation <c>k</c>, in
    /// <see cref="DiplomaticRelations.NationIds"/> order (the setter's own <c>k = 0..15</c>):
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A single pass over k, both sides checked per k — not two full passes.</strong> The
    /// decompile is one loop that tests the winner's-ally condition and then the loser's-ally condition
    /// for the same <c>k</c> before moving to <c>k + 1</c>; a nation can never satisfy both conditions at
    /// once (it cannot be simultaneously allied with and at war with the same side), so the two checks
    /// never collide, but the order their news lines land in does depend on walking them together. The
    /// version this replaced ran two complete passes — every winner-side line before any loser-side one —
    /// which only matches the original when every affected ally happens to sort onto one side.
    /// </para>
    /// <para>
    /// <strong>Four corrections against the version this replaces</strong> (report §3, all four
    /// <c>[confirmed: listing]</c>):
    /// </para>
    /// <list type="number">
    /// <item>
    /// <strong>The partner's own alliance with the ally is reset to -8 first, with no gate.</strong> Every
    /// ally of the winner still at war with the loser loses its alliance with the winner — bordering or
    /// human or not — before anything else is tested. The setter maps only state 0 to a cooldown; a
    /// non-zero argument (here, the literal -8) is stored as given, not -24 the way breaking an alliance
    /// normally would.
    /// </item>
    /// <item>
    /// <strong>The ally only joins the peace if it does not border the enemy, and is not human.</strong>
    /// Both conditions gate the SECOND write (the ally's own war going to -8) and the news line; neither
    /// gates the first, ungated reset. The border check is <see cref="NeighbourGeography.AreNeighbours"/>
    /// — T85 made this exact against the DAT's own mask on the classical world.
    /// </item>
    /// <item>
    /// <strong>The enemy is named first.</strong> "&lt;loser&gt; and &lt;k&gt;" for the winner's allies,
    /// "&lt;winner&gt; and &lt;k&gt;" for the loser's allies — never the ally first.
    /// </item>
    /// <item><strong>The two halves are interleaved per k</strong>, per the single-pass note above.</item>
    /// </list>
    private static GameState ApplyAllyPeaceCascade(
        GameState state, Ruleset ruleset, World world, string winnerId, string loserId)
    {
        var codes = ruleset.Diplomacy.StateCodes;
        var cooldown = ruleset.Diplomacy.CooldownAfterAllyPeace;

        foreach (var allyId in state.Relations.NationIds)
        {
            if (string.Equals(allyId, winnerId, StringComparison.Ordinal)
                || string.Equals(allyId, loserId, StringComparison.Ordinal))
            {
                continue;
            }

            // The winner's ally k, still at war with the loser: loser named first.
            if (state.Relations.Get(allyId, winnerId) == codes.Alliance
                && state.Relations.Get(allyId, loserId) == codes.War)
            {
                state = ApplyOneAllyHalf(state, ruleset, world, cooldown, side: winnerId, enemy: loserId, allyId);
            }

            // The loser's ally k, still at war with the winner: winner named first.
            if (state.Relations.Get(allyId, loserId) == codes.Alliance
                && state.Relations.Get(allyId, winnerId) == codes.War)
            {
                state = ApplyOneAllyHalf(state, ruleset, world, cooldown, side: loserId, enemy: winnerId, allyId);
            }
        }

        return state;
    }

    /// <summary>
    /// One ally's own half of <see cref="ApplyAllyPeaceCascade"/>, for the side (<paramref name="side"/>)
    /// it is allied with and the enemy (<paramref name="enemy"/>) it is at war with — shared by both the
    /// winner's-ally and loser's-ally checks so the ungated reset, the border/human gate and the
    /// enemy-first news line are written exactly once each, not duplicated per call site.
    /// </summary>
    private static GameState ApplyOneAllyHalf(
        GameState state, Ruleset ruleset, World world, int cooldown, string side, string enemy, string allyId)
    {
        // "setRelation(W, k, -8)" -- ungated: every ally still at war with the enemy loses its alliance
        // with its own partner, bordering or human or not (report §3, point 1).
        state = state with { Relations = state.Relations.WithRelation(allyId, side, cooldown) };

        var allyControl = state.NationById(allyId)?.Control;
        if (NeighbourGeography.AreNeighbours(world, allyId, enemy) || allyControl == SeatControl.Human)
        {
            return state;
        }

        state = state with { Relations = state.Relations.WithRelation(allyId, enemy, cooldown) };

        var enemyName = RelationTransitions.NameOf(state, enemy);
        var allyName = RelationTransitions.NameOf(state, allyId);
        return NewsLogWriter.Append(
            state, new DomainEvent[] { new PeaceAllyAgreement(enemyName, allyName) }, ruleset.NewsLog);
    }

    private static int LoserTaxBase(GameState state, string loserId) => state.NationById(loserId)?.TaxBase ?? 0;
}
