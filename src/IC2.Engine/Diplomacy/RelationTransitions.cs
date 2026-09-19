using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.News;

namespace IC2.Engine.Diplomacy;

/// <summary>
/// The confirmed relation-matrix setter — <c>FUN_00449B40</c>
/// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong> — and the two
/// contagion rules that ride along with it (DoD 4). Every transition keeps
/// <see cref="DiplomaticRelations"/> symmetric (DoD 1): it always goes through
/// <see cref="DiplomaticRelations.WithRelation"/>, which writes both cells together, never one alone.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Peace is a cooldown, not a reset.</strong> Setting a pair back to peace does not write
/// <see cref="RelationStateCodes.Peace"/> (<c>0</c>) directly; it writes the cooldown the <em>previous</em>
/// state maps to (DoD 2): <c>-8</c> from trade, <c>-24</c> from alliance, <c>-18</c> from war. All three
/// come from <see cref="DiplomacyRules"/>, never a C# literal.
/// </para>
/// <para>
/// <strong>Contagion is recursive by construction, not by a separate "further cascade" step.</strong>
/// Forming an alliance with <c>B</c> declares war on every nation <c>B</c> is already at war with
/// (<see cref="FormAlliance"/>); declaring war on <c>B</c> declares war on every nation allied to <c>B</c>
/// (<see cref="DeclareWar"/>). Both go through the very same <see cref="DeclareWar"/> call for each
/// dragged-in nation, so a nation dragged in by one hop that is itself allied to yet another nation drags
/// that nation in too — the original's own <c>FUN_00449B40</c> is one function calling itself for every
/// state it writes, so the reimplementation's own recursion matches it rather than truncating at one hop.
/// <see cref="DeclareWar"/>'s own early-exit (already at war ⇒ no-op) is what keeps this from looping: a
/// nation can only ever be dragged in once, because the second attempt to declare war on an
/// already-at-war pair does nothing.
/// </para>
/// <para>
/// <strong>War declarations are written directly, not through the standard per-event news pipeline.</strong>
/// See <see cref="WarDeclared"/>'s own remarks for why: the shouting rule needs the whole rendered line
/// transformed, which <see cref="NewsLogWriter.Append"/>'s per-kind-template pipeline cannot do, so
/// <see cref="DeclareWar"/> renders the line itself and calls <see cref="NewsLogWriter.Append"/> with a
/// literal, placeholder-free override. Alliance formation has no such rule and uses the standard path.
/// </para>
/// </remarks>
public static class RelationTransitions
{
    /// <summary>
    /// Moves the relation between <paramref name="a"/> and <paramref name="b"/> back to peace, writing the
    /// cooldown the relation being broken maps to (DoD 2). A no-op when the pair is already at peace or
    /// already on a cooldown (there is nothing to "break").
    /// </summary>
    public static GameState BreakToPeace(GameState state, Ruleset ruleset, string a, string b)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var codes = ruleset.Diplomacy.StateCodes;
        var current = state.Relations.Get(a, b);

        var cooldown = current switch
        {
            _ when current == codes.Trade => ruleset.Diplomacy.CooldownAfterBrokenTrade,
            _ when current == codes.Alliance => ruleset.Diplomacy.CooldownAfterBrokenAlliance,
            _ when current == codes.War => ruleset.Diplomacy.CooldownAfterEndedWar,
            _ => current,
        };

        return cooldown == current
            ? state
            : state with { Relations = state.Relations.WithRelation(a, b, cooldown) };
    }

    /// <summary>
    /// Forms an alliance between <paramref name="proposer"/> and <paramref name="partner"/> (DoD 1, DoD 4):
    /// writes the alliance directly (no cooldown mapping — alliance is a state, not a peace variant),
    /// writes the confirmed <c>alliance.formed</c> news line, then drags <paramref name="proposer"/> into
    /// war with every nation <paramref name="partner"/> is already at war with that
    /// <paramref name="proposer"/> is not already at war with.
    /// </summary>
    public static GameState FormAlliance(GameState state, Ruleset ruleset, string proposer, string partner)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var codes = ruleset.Diplomacy.StateCodes;
        state = state with { Relations = state.Relations.WithRelation(proposer, partner, codes.Alliance) };

        var proposerName = NameOf(state, proposer);
        var partnerName = NameOf(state, partner);
        state = NewsLogWriter.Append(
            state, new DomainEvent[] { new AllianceFormed(proposerName, partnerName) }, ruleset.NewsLog);

        // The cascade: every nation the new ally is at war with, that the proposer is not already at war
        // with, becomes an enemy of the proposer too (news-log-format-and-messages.md Q4's own confirmed
        // note: "An alliance between A and B also makes A declare war on each nation at war with B").
        foreach (var other in state.Relations.NationIds)
        {
            if (string.Equals(other, proposer, StringComparison.Ordinal)
                || string.Equals(other, partner, StringComparison.Ordinal))
            {
                continue;
            }

            if (state.Relations.Get(partner, other) == codes.War && state.Relations.Get(proposer, other) != codes.War)
            {
                state = DeclareWar(state, ruleset, proposer, other);
            }
        }

        return state;
    }

    /// <summary>
    /// Declares war from <paramref name="decreeing"/> on <paramref name="target"/> (DoD 1, DoD 4, DoD 5):
    /// writes the war state directly ("War: set directly, no check" — the report's own words), writes the
    /// confirmed <c>war.declared</c> news line, uppercased in full when either nation is human
    /// (<c>news-log-format-and-messages.md</c> Q2), then drags <paramref name="decreeing"/> into war with
    /// every nation already allied to <paramref name="target"/>.
    /// </summary>
    /// <remarks>
    /// A no-op when the two are already at war: this is both the confirmed behaviour (there is nothing to
    /// declare) and the guard that stops the contagion recursion from looping.
    /// </remarks>
    public static GameState DeclareWar(GameState state, Ruleset ruleset, string decreeing, string target)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var codes = ruleset.Diplomacy.StateCodes;
        if (state.Relations.Get(decreeing, target) == codes.War)
        {
            return state;
        }

        state = state with { Relations = state.Relations.WithRelation(decreeing, target, codes.War) };

        var decreeingNation = state.NationById(decreeing);
        var targetNation = state.NationById(target);
        var involvesHuman = decreeingNation?.Control == SeatControl.Human || targetNation?.Control == SeatControl.Human;

        state = AppendWarDeclarationNews(
            state, ruleset, decreeingNation?.Name ?? decreeing, targetNation?.Name ?? target, involvesHuman);

        // The cascade: every nation already allied to the target, that the decreeing nation is not already
        // at war with, becomes an enemy of the decreeing nation too ("A declaration on B also makes A
        // declare war on each of B's allies").
        foreach (var other in state.Relations.NationIds)
        {
            if (string.Equals(other, decreeing, StringComparison.Ordinal)
                || string.Equals(other, target, StringComparison.Ordinal))
            {
                continue;
            }

            if (state.Relations.Get(target, other) == codes.Alliance && state.Relations.Get(decreeing, other) != codes.War)
            {
                state = DeclareWar(state, ruleset, decreeing, other);
            }
        }

        return state;
    }

    /// <summary>
    /// Renders and appends one <c>war.declared</c> line, applying the shouting rule — see
    /// <see cref="WarDeclared"/>'s own remarks for why this bypasses the standard per-event pipeline.
    /// </summary>
    internal static GameState AppendWarDeclarationNews(
        GameState state, Ruleset ruleset, string decreeingName, string targetName, bool involvesHuman)
    {
        var template = NewsMessageCatalog.GetTemplate("war.declared");
        var rendered = template
            .Replace("<A>", decreeingName, StringComparison.Ordinal)
            .Replace("<B>", targetName, StringComparison.Ordinal);
        var shouted = NewsLogWriter.ApplyWarDeclarationShouting(rendered, involvesHuman);

        var vehicle = new WarDeclared(decreeingName, targetName);
        return NewsLogWriter.Append(state, new DomainEvent[] { vehicle }, ruleset.NewsLog, templateFor: _ => shouted);
    }

    /// <summary>The nation's display name, or its id if it cannot be resolved (defensive; never expected).</summary>
    internal static string NameOf(GameState state, string nationId) => state.NationById(nationId)?.Name ?? nationId;

    /// <summary>
    /// Whether <paramref name="nationId"/> is currently at war with any other nation — the "either side
    /// is currently at war with anyone" gate <c>TPolitics_MakeAlliance</c> applies against an AI target,
    /// shared by <c>ProposeAllianceCommandHandler</c> and <c>AcceptPendingOfferCommandHandler</c> (rework
    /// round 1, B2) rather than duplicated in each.
    /// </summary>
    public static bool IsAtWarWithAnyone(GameState state, Ruleset ruleset, string nationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var warCode = ruleset.Diplomacy.StateCodes.War;
        foreach (var other in state.Relations.NationIds)
        {
            if (!string.Equals(other, nationId, StringComparison.Ordinal)
                && state.Relations.Get(nationId, other) == warCode)
            {
                return true;
            }
        }

        return false;
    }
}
