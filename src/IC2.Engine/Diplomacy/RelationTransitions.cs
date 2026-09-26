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
/// <strong>Contagion is one step, not recursive (T88, bug #383).</strong> Forming an alliance with
/// <c>B</c> declares war on every nation <c>B</c> is already at war with (<see cref="FormAlliance"/>);
/// declaring war on <c>B</c> declares war on every nation allied to <c>B</c> (<see cref="DeclareWar"/>).
/// Each dragged-in nation is written directly — <see cref="DeclareWarWithoutFurtherCascade"/> sets the
/// relation and its own news line and starts <em>no</em> cascade of its own —
/// <strong>[confirmed: decompiled-war-cascade-and-peace-paths.md §1]</strong>: a whole-program call-graph
/// scan of the original's setter, <c>FUN_00449B40</c>, finds 22 callers and zero calls to itself; its two
/// propagation loops are plain double stores (<c>rel[a][k]</c> and <c>rel[k][a]</c>) plus a call to the
/// nested news procedure, never a re-entry into the setter. In a chain of allies A–B, B–C, C–D, a
/// declaration on A reaches only A and B — C and D stay at peace, exactly as the report's worked example
/// (§1.4) reads a war onto A and B and no further. The doc comment this replaces claimed the original's
/// own setter "is one function calling itself"; the byte scan says otherwise, so the fix is to match the
/// original's one-step write, not to keep the deeper reimplementation.
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

        var current = state.Relations.Get(a, b);
        var cooldown = CooldownForBrokenRelation(current, ruleset) ?? current;

        return cooldown == current
            ? state
            : state with { Relations = state.Relations.WithRelation(a, b, cooldown) };
    }

    /// <summary>
    /// Resets every relation <paramref name="eliminatedId"/> holds, in both directions, the way the
    /// original resets them on elimination (rework round 1, B1) — <strong>[confirmed]</strong>. Called
    /// only from <c>CityCaptureResolver</c>'s two elimination sites, only when a capture or defection has
    /// just eliminated the nation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both decompiled elimination paths call the relation setter with state 0 against all 16 nation
    /// slots (local dump <c>%LOCALAPPDATA%\ReTools\all_app_functions.txt</c>): <c>FUN_0044bed8</c>, the
    /// "last city lost" branch (:50381–50391) — <c>TPremierForm_DisableNation(...)</c>, then
    /// <c>do { FUN_00449b40(sVar1, sVar11, 0); sVar11++; } while (sVar11 != 0x10);</c> — and
    /// <c>FUN_0044c528</c>, the "X conquers Y" cascade (:50674–50677) — <c>FUN_00449b40(param_1, uVar4,
    /// 0)</c> in a loop to <c>0x10</c>. Research: <c>decompiled-ai-offers-to-human-seats.md:43</c> —
    /// "defection and elimination <c>FUN_0044BED8</c>/<c>FUN_0044C360</c>/<c>FUN_0044C528</c>/<c>FUN_0044C8F0</c>
    /// … write only peace or cooldowns" <strong>[confirmed: all 22 decompiled FUN_00449B40( call
    /// sites]</strong>; and <c>decompiled-diplomacy-peace-terms-and-instant-battles.md:28–34</c> — the
    /// setter's own cooldown mapping for a state-0 call against a positive relation.
    /// </para>
    /// <para>
    /// <strong>The setter's own code, read directly</strong> (<c>all_app_functions.txt</c>
    /// :48488–48504): given state 0, it reads the pair's current relation; a positive state
    /// (trade/alliance/war) maps to its cooldown exactly as <see cref="BreakToPeace"/> does (the shared
    /// <c>CooldownForBrokenRelation</c> below), but <strong>any other current value — already peace (0),
    /// or already a cooldown (negative) — is written back to state 0 anyway</strong>, unconditionally, at
    /// :48502/:48504. There is no early-return and no "already there" check anywhere in the function. So,
    /// unlike <see cref="BreakToPeace"/> (a no-op once a pair is already at peace or on a cooldown — the
    /// behaviour every one of its other callers, <c>MakePeaceCommand</c>, <c>PeaceTreatySystem</c> and
    /// <c>TradePartnerCap</c>, wants, since none of them ever calls it starting from peace or a cooldown),
    /// an elimination reset <strong>clears an existing cooldown back to full peace immediately</strong>,
    /// rather than leaving it to finish cooling down. <see cref="BreakToPeace"/> itself is unchanged; this
    /// method exists because its callers' no-op is the wrong behaviour for this one caller.
    /// </para>
    /// <para>
    /// No news line: neither decompiled elimination path calls the news writer for any of these
    /// per-nation relation writes (only the elimination banner itself, owned by
    /// <c>CityCaptureResolver</c>/<c>NationElimination</c>).
    /// </para>
    /// </remarks>
    public static GameState ResetAllOnElimination(GameState state, Ruleset ruleset, string eliminatedId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(eliminatedId);

        // Defensive: every nation a real GameState carries is a row in its own relation matrix (built
        // that way by GameStateFactory.CreateInitial and OriginalSaveImporter alike), so this never fires
        // in production. It guards a fixture that models cities/armies/nations without modelling
        // diplomacy at all -- "reset every relation this nation holds" is vacuously true when the matrix
        // does not track it.
        if (state.Relations.IndexOf(eliminatedId) < 0)
        {
            return state;
        }

        var codes = ruleset.Diplomacy.StateCodes;
        foreach (var other in state.Relations.NationIds)
        {
            if (string.Equals(other, eliminatedId, StringComparison.Ordinal))
            {
                continue;
            }

            var current = state.Relations.Get(eliminatedId, other);
            var next = CooldownForBrokenRelation(current, ruleset) ?? codes.Peace;
            state = state with { Relations = state.Relations.WithRelation(eliminatedId, other, next) };
        }

        return state;
    }

    /// <summary>
    /// The setter's own mapping from a broken positive relation to its cooldown (DoD 2): trade →
    /// <c>-8</c>, alliance → <c>-24</c>, war → <c>-18</c>, all from <see cref="DiplomacyRules"/>, never a
    /// C# literal. <see langword="null"/> when <paramref name="current"/> is not one of the three positive
    /// states — <see cref="BreakToPeace"/> (a no-op then) and <see cref="ResetAllOnElimination"/> (peace
    /// then) each decide what that means for their own caller, matching the setter's own two call shapes.
    /// </summary>
    private static int? CooldownForBrokenRelation(int current, Ruleset ruleset)
    {
        var codes = ruleset.Diplomacy.StateCodes;
        if (current == codes.Trade)
        {
            return ruleset.Diplomacy.CooldownAfterBrokenTrade;
        }

        if (current == codes.Alliance)
        {
            return ruleset.Diplomacy.CooldownAfterBrokenAlliance;
        }

        if (current == codes.War)
        {
            return ruleset.Diplomacy.CooldownAfterEndedWar;
        }

        return null;
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
        // T88 (bug #383): one step only -- DeclareWarWithoutFurtherCascade writes the dragged-in war
        // directly and starts no cascade of its own, matching the setter's own loop 1 (§1.1) exactly --
        // the alliance-branch loop at 0x00449BEF, textually first in the setter (ContagionTests calls it
        // loop 1 too), ahead of the war-branch loop (loop 2, 0x00449C71) that DeclareWar's own cascade
        // below matches.
        foreach (var other in state.Relations.NationIds)
        {
            if (string.Equals(other, proposer, StringComparison.Ordinal)
                || string.Equals(other, partner, StringComparison.Ordinal))
            {
                continue;
            }

            if (state.Relations.Get(partner, other) == codes.War && state.Relations.Get(proposer, other) != codes.War)
            {
                state = DeclareWarWithoutFurtherCascade(state, ruleset, proposer, other);
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
    /// declare) and the guard the setter's own cascade loops read live (§1.1: "the guard stays the same
    /// (<c>rel[a][k] != 3</c>), evaluated live"), so a nation already dragged in by one hop of this
    /// method's own cascade below is skipped rather than re-declared against. There is no recursion here
    /// to guard against (T88, bug #383): the cascade is one step, not a re-entry into this method.
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
        // declare war on each of B's allies"). T88 (bug #383): one step only -- the dragged-in war is
        // written directly, with its own news line, and starts no cascade of its own (an ally of an ally
        // is not reached). This is the direct declaration's own hop; a caller such as FormAlliance reaches
        // this method only for its own single hop too, via DeclareWarWithoutFurtherCascade below.
        foreach (var other in state.Relations.NationIds)
        {
            if (string.Equals(other, decreeing, StringComparison.Ordinal)
                || string.Equals(other, target, StringComparison.Ordinal))
            {
                continue;
            }

            if (state.Relations.Get(target, other) == codes.Alliance && state.Relations.Get(decreeing, other) != codes.War)
            {
                state = DeclareWarWithoutFurtherCascade(state, ruleset, decreeing, other);
            }
        }

        return state;
    }

    /// <summary>
    /// One dragged-in war, written directly with its own news line and no cascade of its own — the
    /// setter's own two propagation loops (report §1.1: "two direct stores of 3", "then the news line"),
    /// never a re-entry into the setter itself (T88, bug #383). Both <see cref="DeclareWar"/>'s own
    /// cascade loop and <see cref="FormAlliance"/>'s call this for exactly the nations their loops select,
    /// so a nation dragged in by one hop never drags a further nation in — the one-step behaviour DoD 1
    /// pins with the report's own chain-of-four-allies example. Never called for the transition's own
    /// direct declaration (that is <see cref="DeclareWar"/>'s own body), only for a cascade hop.
    /// </summary>
    private static GameState DeclareWarWithoutFurtherCascade(
        GameState state, Ruleset ruleset, string decreeing, string target)
    {
        var codes = ruleset.Diplomacy.StateCodes;
        if (state.Relations.Get(decreeing, target) == codes.War)
        {
            return state;
        }

        state = state with { Relations = state.Relations.WithRelation(decreeing, target, codes.War) };

        var decreeingNation = state.NationById(decreeing);
        var targetNation = state.NationById(target);
        var involvesHuman = decreeingNation?.Control == SeatControl.Human || targetNation?.Control == SeatControl.Human;

        return AppendWarDeclarationNews(
            state, ruleset, decreeingNation?.Name ?? decreeing, targetNation?.Name ?? target, involvesHuman);
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
    /// Whether <paramref name="nationId"/> is currently at war with any other nation — the first of
    /// <c>TPolitics_MakeAlliance</c>'s human-to-AI refusal conditions against an AI target (T82 rework
    /// round 1, B2 corrects an earlier "either side" reading: <c>decompiled-ai-offers-to-human-seats.md</c>
    /// §3 is explicit that <strong>only the human's own side is checked</strong> — "the AI target's own
    /// wars are not checked" — so this is applied to the human/accepting/proposing party only, never the
    /// AI counterparty), shared by <c>ProposeAllianceCommandHandler</c> and
    /// <c>AcceptPendingOfferCommandHandler</c> rather than duplicated in each.
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

    /// <summary>
    /// Whether any nation <paramref name="nationId"/> is currently allied with is itself at war with
    /// anyone (T82 rework round 1, B2) — the second of <c>TPolitics_MakeAlliance</c>'s human-to-AI
    /// refusal conditions <strong>[confirmed: decompiled-ai-offers-to-human-seats.md §3,
    /// <c>FUN_00449CD8</c>]</strong>: "any nation the working row marks allied is at war with anyone".
    /// Shared by <c>ProposeAllianceCommandHandler</c> and <c>AcceptPendingOfferCommandHandler</c> rather
    /// than duplicated in each — the same reasoning as <see cref="IsAtWarWithAnyone"/>'s own sharing, one
    /// member up.
    /// </summary>
    public static bool HasAnAllyAtWarWithAnyone(GameState state, Ruleset ruleset, string nationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var allianceCode = ruleset.Diplomacy.StateCodes.Alliance;
        foreach (var ally in state.Relations.NationIds)
        {
            if (!string.Equals(ally, nationId, StringComparison.Ordinal)
                && state.Relations.Get(nationId, ally) == allianceCode
                && IsAtWarWithAnyone(state, ruleset, ally))
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// The rejection shared by every diplomacy handler that refuses new diplomacy with an eliminated
/// counterparty (bug #199) — defined once and reused, the same reasoning as
/// <see cref="RelationTransitions.IsAtWarWithAnyone"/> (T19). Used by propose-trade, propose-alliance,
/// accept-pending-offer, declare-war and make-peace (rework round 1, N2 — the review found the first two
/// commands had the identical gap).
/// </summary>
/// <remarks>
/// <para>
/// <strong>[confirmed].</strong> Re-review round 2 (PR #364, finding R2) located the original's own
/// rejection, in <c>RE-imperial-conquest-2/docs/reports/decompiled-elimination-cleanup.md</c> §5
/// (:211–214): "Every diplomatic path requires the target's unity &gt; 0 [confirmed]", naming
/// <c>TPolitics_ChangeIR</c> (:55103), the AI's own picks (<c>FUN_0044FB7C</c>), the offer roll
/// (<c>FUN_00452034</c>) and the menu-disabling <c>TPolitics_InitialiseForm</c> (:55042). Read directly
/// from the local dump (<c>%LOCALAPPDATA%\ReTools\all_app_functions.txt</c> :55120–55122),
/// <c>TPolitics_ChangeIR</c> gates the calls to <c>TPolitics_MakePeace</c>/<c>MakeTrade</c>/
/// <c>MakeAlliance</c> and the war selection on <c>(0 &lt; (short)(&amp;DAT_00474ab0)[sVar3 * 0x24a])</c> —
/// the target's own unity. Both decompiled elimination paths zero it: <c>(&amp;DAT_00474ab0)[iVar2 *
/// 0x24a] = 0</c> at :50383 (<c>FUN_0044bed8</c>) and :50743 (<c>FUN_0044c528</c>). So the original does
/// not merely grey out a menu entry: <c>TPolitics_ChangeIR</c> is a decompiled function that refuses every
/// one of these five diplomatic actions against any nation whose unity has reached 0 — which every
/// elimination causes, by construction (<see cref="IC2.Engine.Cities.Capture.NationElimination"/>'s own
/// use of <see cref="Model.CaptureRules.EliminationUnityReset"/>). This rejection is therefore confirmed,
/// not designed.
/// </para>
/// <para>
/// <strong>One tracked difference, not this task's to close.</strong> The original's own gate is the
/// target's <em>unity ≤ 0</em>, a value that could in principle reach 0 by some other route than
/// elimination and still be a legal target on the original's own terms; this reimplementation keys the
/// rejection on <see cref="Model.NationState.Eliminated"/> instead, which is exactly right at elimination
/// (both decompiled elimination paths always zero unity in the same call that eliminates the nation) but
/// is not a byte-for-byte reproduction of the original's own broader unity gate. That divergence is
/// tracked as bug #368, out of T69's own Owns list.
/// </para>
/// </remarks>
public static class DiplomacyRejections
{
    /// <summary>The other nation named in the command has already been eliminated.</summary>
    public static readonly RejectionCode CounterpartyEliminated = new("diplomacy.counterparty-eliminated");
}
