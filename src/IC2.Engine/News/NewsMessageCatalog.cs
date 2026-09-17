using System.Collections.Frozen;

namespace IC2.Engine.News;

/// <summary>
/// Maps a news-worthy domain event kind to its message template. A template's placeholders are
/// substituted by <see cref="NewsLogWriter"/> from the published event's own properties.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Every template below is transcribed from the T04 fixtures corpus</strong>
/// (<c>tests/fixtures/corpus.json</c>, ids <c>newsMessage.*</c>) — never re-typed from a report by
/// hand a second time. <c>NewsMessageCatalogTests</c> asserts each one against
/// <c>FixtureCorpus.Get(id).AsString()</c> directly, so a transcription slip here fails CI rather than
/// silently diverging from the confirmed text.
/// </para>
/// <para>
/// <strong>Placeholder syntax is whatever the source report used, not a syntax this task invented.</strong>
/// Two entries (from <c>decompiled-city-capture-resolution.md</c>) use <c>{PascalCase}</c>; most of the
/// rest use <c>&lt;lowercase&gt;</c>, some with a <c>[field[+offset]]</c> provenance annotation the
/// source report itself included in its quoted string (for example <c>&lt;cityName[fleet[+20]]&gt;</c>).
/// <see cref="NewsLogWriter"/>'s renderer recognises both delimiters, matches the token inside —
/// ignoring any bracket annotation — against the published event's own properties (any inherited from
/// an intermediate base record between it and <see cref="Core.DomainEvent"/> still count; only
/// <see cref="Core.DomainEvent"/>'s own two, <c>Kind</c> and <c>IsNewsWorthy</c>, are excluded), matching
/// case-insensitively for the angle-bracket form, and substitutes the property's value; the annotation
/// itself is never printed.
/// </para>
/// <para>
/// <strong>Several entries are <c>[designed]</c> by adding only a delimiter, never by inventing new
/// text.</strong> Their corpus source (<c>newsMessage.paysReparations</c>, <c>newsMessage.formsAlliance</c>,
/// <c>newsMessage.declaresWar</c>, <c>newsMessage.movedCapital</c>, <c>newsMessage.deposesLeader</c>,
/// <c>newsMessage.fleetDamagedInStorm</c>, <c>newsMessage.weekHeader</c> and
/// <c>dialog.pendingDiplomaticOfferParaphrase</c>) writes its operand as a bare, undelimited letter or
/// word — <c>N</c>, <c>A</c>/<c>B</c>, <c>C</c>, <c>L</c>, <c>X</c>/<c>Y</c>, <c>week</c>/<c>season</c>/
/// <c>year</c> — which no renderer can distinguish from surrounding prose. What was searched and came
/// up empty for each: <c>decompiled-diplomacy-peace-terms-and-instant-battles.md</c> (reparations),
/// <c>news-log-format-and-messages.md</c> itself (alliance, war, capital move, deposition, storm
/// damage, the week header — its own Q4 table and Q3 pseudocode give the call sites and the exact
/// spacing, never a delimited template string) and <c>decompiled-turn-and-calendar-sequencing.md</c>
/// (the pending-offer paraphrase); none quotes a delimited format string for any of them. This
/// catalog's designed templates wrap each bare operand in the corpus's own established delimiter
/// convention, changing no other character: nation letters that already had an established
/// <c>&lt;A&gt;</c>/<c>&lt;B&gt;</c> precedent (<c>newsMessage.allyPeaceAgreement</c>, quoted with those
/// exact brackets in its own source report) keep that casing for <c>alliance.formed</c> and
/// <c>war.declared</c>; a single amount letter follows the established <c>N</c> → <c>&lt;n&gt;</c>
/// lowercase-wrap precedent; the week header's <c>week</c>/<c>season</c>/<c>year</c> pseudocode
/// variables wrap the same way, exactly as <c>docs/task-catalogue.md</c>'s T42 entry already gives this
/// string; and the remaining new nation/city/leader operands (capital move, deposition) follow
/// <c>newsMessage.fleetFinished</c>'s own lowercase full-word convention (<c>&lt;nation&gt;</c>,
/// <c>&lt;cityName&gt;</c>) since they share its shape (one nation, one place). <c>NewsMessageCatalogTests</c>
/// asserts that replacing each designed delimiter with its bare source text reproduces
/// <c>FixtureCorpus.Get(id).AsString()</c> exactly, and separately renders each with real operands.
/// </para>
/// <para>
/// <strong>Two entries are <c>[designed]</c> by generalising a single observed example.</strong>
/// <c>city.defects-to</c> and <c>nation.conquered</c> had no confirmed format string of their own when
/// T10 first wrote this catalog — only one observed in-game instance (a screenshot) for each, already
/// substituted. What was searched and came up empty: <c>galatia-elimination-and-city-resupply-confirmed.md</c>
/// (the source report for both) and every other report in <c>tests/fixtures/known-reports.json</c>
/// whose filename mentions city capture, defection or nation elimination; none quoted the underlying
/// pseudocode's literal format string. <c>news-log-format-and-messages.md</c> Q4 has since confirmed
/// both templates directly in code (single spaces throughout, letter-substitution reproduces each
/// observed example exactly), which is why they are no longer tagged <c>[designed]</c> in the corpus —
/// but the template <em>shape</em> chosen here (bracketed <c>{PascalCase}</c> operands, matching the
/// two confirmed <c>decompiled-city-capture-resolution.md</c> entries' own convention) predates that
/// confirmation and is unchanged by it. <c>nation.conquered</c> never renders alone: Q4 #10-12 confirms
/// an elimination always writes it between two <see cref="DashLineKind"/> entries — see
/// <see cref="IsWrappedInDashLines"/> and <see cref="NewsLogWriter.Append"/>.
/// </para>
/// <para>
/// <strong>Three entries are structural, not tied to any <see cref="Core.DomainEvent"/> kind.</strong>
/// <see cref="DashLineKind"/>, <see cref="BlankLineKind"/> and <see cref="WeekHeaderKind"/> are the round
/// tick's own bookkeeping lines (the elimination banner's dash lines, and the blank-line-then-header pair
/// every round tick ends with) — <see cref="NewsLogWriter.Append"/> and
/// <see cref="NewsLogWriter.AppendRoundHeader"/> fetch them by these synthetic kind strings directly,
/// never through <see cref="Core.DomainEventCatalog.Discover"/>. <c>NewsMessageCatalogTests</c>'s
/// corpus/catalog set-equality check (#91 N16) still covers all three, matched against
/// <c>newsMessage.dashLine</c>, <c>newsMessage.blankLine</c> and <c>newsMessage.weekHeader</c>.
/// </para>
/// <para>
/// <strong>Three entries are test-only pipeline scaffolding, never corpus-backed.</strong>
/// <c>test.news-log.pipeline-probe.city-falls</c> and <c>test.news-log.pipeline-probe.fleet-lost</c>
/// exist solely so <c>NewsLogWriterFixtures</c> (<c>tests/IC2.Engine.Tests/News</c>) can exercise the
/// real, registry-instantiated <see cref="NewsLogWriterSeatEnd"/>/<see cref="NewsLogWriterRoundEnd"/> —
/// which always resolve through this static catalog, with no injection seam — under a namespace-unique
/// kind rather than reusing a production kind (#91 N10, N23: two of T10's own fixtures used to declare
/// <c>city.falls-to</c>/<c>fleet.lost-at-sea</c> directly, which collides once a real production event
/// declares either). <c>test.news-log.pipeline-probe.nation-conquered</c> exists for the same reason,
/// one level deeper: <see cref="NewsLogWriter.Append"/>'s dash-wrap decision
/// (<see cref="IsWrappedInDashLines"/>) keys off the published event's own <c>Kind</c>, so exercising it
/// through the *default* catalog resolution (rather than a test's injected <c>templateFor</c> override,
/// which decouples the probe's kind from the template under test) needs a kind that is both wrapped and
/// namespace-unique — it is the only entry that also appears in <see cref="DashWrappedKinds"/>. All
/// three copy an already-confirmed template verbatim under a <c>test.</c>-prefixed kind, so no text
/// here is invented — only the kind string is test-scoped. Excluded from the corpus/catalog
/// set-equality check by that same prefix.
/// </para>
/// <para>
/// Every news-worthy domain event kind the engine declares must have an entry here, asserted by
/// <c>NewsCoverageTests</c>: a later task that declares one without adding its own message literal fails
/// CI (<c>docs/build-process.md</c> §2.5 — emission, and the literal, stay with the task that owns the
/// mechanic). That test also proves every placeholder in a news-worthy event's template resolves to one
/// of that event's own properties (#91 N17/N28), using the same lookup <see cref="NewsLogWriter.Append"/>
/// itself uses to render — a check that must be in place before T14, T16 or T17 declare their first
/// production news events.
/// </para>
/// </remarks>
public static class NewsMessageCatalog
{
    /// <summary>
    /// The synthetic "kind" under which the elimination banner's 59-byte dash line
    /// (<c>newsMessage.dashLine</c>) is stored — not a real <see cref="Core.DomainEvent"/> kind. See this
    /// type's remarks.
    /// </summary>
    public const string DashLineKind = "news.dash-line";

    /// <summary>
    /// The synthetic "kind" under which the round tick's single-space blank entry
    /// (<c>newsMessage.blankLine</c>) is stored — not a real <see cref="Core.DomainEvent"/> kind. See this
    /// type's remarks.
    /// </summary>
    public const string BlankLineKind = "news.blank-line";

    /// <summary>
    /// The synthetic "kind" under which the round tick's week-header template
    /// (<c>newsMessage.weekHeader</c>) is stored — not a real <see cref="Core.DomainEvent"/> kind. See
    /// this type's remarks and <see cref="NewsLogWriter.RenderWeekHeader"/>.
    /// </summary>
    public const string WeekHeaderKind = "news.week-header";

    /// <summary>
    /// The event kinds an elimination wraps between two <see cref="DashLineKind"/> entries. The second
    /// entry is test-only scaffolding (see this type's remarks on the two pipeline-probe kinds) that lets
    /// a test exercise <see cref="NewsLogWriter.Append"/>'s dash-wrap mechanism through the *default*
    /// production catalog resolution — which is what makes <see cref="NewsLogWriter.Append"/> key this
    /// decision off <c>domainEvent.Kind</c> in the first place — without reusing the real
    /// <c>nation.conquered</c> kind (#91 N23).
    /// </summary>
    private static readonly FrozenSet<string> DashWrappedKinds =
        new[] { "nation.conquered", "test.news-log.pipeline-probe.nation-conquered" }
            .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Event kind → message template. See this type's remarks for where each value comes from.
    /// </summary>
    private static readonly FrozenDictionary<string, string> Templates = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        // ---- Confirmed format strings, transcribed verbatim from the corpus. ----

        // newsMessage.fallsTo (decompiled-city-capture-resolution.md) — FUN_0044b27c's format string,
        // including its original irregular spacing.
        { "city.falls-to", "{CityName}   ({OldOwner})  falls to {NewOwner}." },

        // newsMessage.failsToCapture (decompiled-city-capture-resolution.md)
        { "city.fails-to-capture", "{AttackerNation} fails to capture {CityName}   ({DefenderNation})." },

        // newsMessage.destroysArmy (decompiled-diplomacy-peace-terms-and-instant-battles.md)
        { "battle.army-destroyed", "<winner> destroys army of <loser>." },

        // newsMessage.sinksFleet (decompiled-diplomacy-peace-terms-and-instant-battles.md)
        { "battle.fleet-sunk", "<winner> sinks fleet of <loser>." },

        // newsMessage.honourablePeace (decompiled-diplomacy-peace-terms-and-instant-battles.md) — fires
        // when the winner scores lower on population*unity or total army strength: no reparations paid.
        { "peace.honourable", "<winner> and <loser> have agreed to end their war." },

        // newsMessage.suesForPeace (decompiled-diplomacy-peace-terms-and-instant-battles.md)
        { "peace.sues-for", "<loser> sues <winner> for peace and;" },

        // newsMessage.endsTradingAgreements (decompiled-diplomacy-peace-terms-and-instant-battles.md) —
        // leading whitespace transcribed exactly as the report shows it.
        { "peace.ends-trading-agreements", "    <loser> ends all current trading agreements." },

        // newsMessage.endsAlliances (decompiled-diplomacy-peace-terms-and-instant-battles.md) — double
        // space before "ends" transcribed exactly as the report shows it.
        { "peace.ends-alliances", "    <loser>  ends all current alliances." },

        // newsMessage.paysReparations — designed: the source text's own placeholder is the bare,
        // undelimited letter "N"; this template wraps it as "<n>", changing no other character. The
        // amount itself is comma-grouped (news-log-format-and-messages.md Q2); see
        // NewsLogWriter.FormatGroupedAmount. See this type's remarks.
        { "peace.pays-reparations", "    <loser> pays reparations of <n> talents." },

        // newsMessage.allyPeaceAgreement (decompiled-diplomacy-peace-terms-and-instant-battles.md) — a
        // distinct line from peace.honourable in the source pseudocode (different placeholder names, same
        // text pattern), fired when an ally of either treaty side is still at war with the other. This
        // report's own quoted string already uses these exact <A>/<B> brackets.
        { "peace.ally-agreement", "<A> and <B> have agreed to end their war." },

        // newsMessage.fleetFinished (decompiled-unit-map-orders-and-record-fields.md) — the report's own
        // quoted string keeps the "[fleet[+20]]" field-provenance annotation on the city-name placeholder.
        // Corrected by T42 (bug #88): the EXE does StrCat(".") after this line (line 48785), so it ends
        // with a period; the report's own quote had dropped it.
        { "fleet.finished", "<nation> finishes a new fleet at <cityName[fleet[+20]]>." },

        // ---- [designed]: generalised from a single observed example, now independently confirmed in
        // code by news-log-format-and-messages.md Q4 (#9, #11). See this type's remarks. ----

        // Generalises newsMessage.defectsFromObservedExample ("Synnada defects from Galatia to
        // Seleucid."); rendering with CityName=Synnada, OldOwner=Galatia, NewOwner=Seleucid reproduces it.
        { "city.defects-to", "{CityName} defects from {OldOwner} to {NewOwner}." },

        // Generalises newsMessage.conquersNationObservedExample ("Seleucid conquers Galatia."); rendering
        // with ConqueringNation=Seleucid, ConqueredNation=Galatia reproduces it. Always wrapped between
        // two DashLineKind entries by NewsLogWriter.Append — see IsWrappedInDashLines.
        { "nation.conquered", "{ConqueringNation} conquers {ConqueredNation}." },

        // ---- Confirmed format string; the corpus's own transcription of the observed example lacked its
        // trailing period, corrected by T42 (bug #88). See this type's remarks. ----

        // Matches newsMessage.fleetLostAtSeaObservedExample ("A fleet belonging to Carthage is lost at
        // sea.") verbatim but for the {Nation} operand. Confirmed with the period in both
        // supply-driven-morale-and-fleet-attrition.md (pseudocode near line 162, and the save
        // transcription near line 206) and news-log-format-and-messages.md's own corpus-comparison table.
        { "fleet.lost-at-sea", "A fleet belonging to {Nation} is lost at sea." },

        // ---- New templates from news-log-format-and-messages.md Q4, added by T42 (T10's follow-up #91
        // and the report's own "missing templates" list). All [designed] by wrapping a bare source letter
        // in a delimiter; see this type's remarks. ----

        // newsMessage.formsAlliance (Q4 #1, FUN_00449A44 via FUN_00449B40): confirmed, 4 of 4 events.
        { "alliance.formed", "<A> forms an alliance with <B>." },

        // newsMessage.declaresWar (Q4 #2, same call site): confirmed, 7 of 7 events, all lowercase (all
        // between AI nations). A human-involved declaration is uppercased in full (ASCII a-z only) —
        // that rendering rule is code-only, never observed in a save, and lives on NewsLogWriter for T19
        // to call with its own human flag; it is not baked into this template.
        { "war.declared", "<A> declares war on <B>." },

        // newsMessage.movedCapital (Q4 #8, FUN_0044BD2C): [derived] -- code-only, never observed in a
        // save (news-log-format-and-messages.md's own "Code-only, never observed" list).
        { "nation.capital-moved", "<nation> have moved their capital to <cityName>." },

        // newsMessage.deposesLeader (Q4 #13, FUN_0044C8F0): confirmed, 2 of 2 events, AI-only (a human's
        // equivalent opens the THumanFalls game-over form instead, which is not news -- see
        // gameOverForm.* in the corpus).
        { "nation.leader-deposed", "<nation> depose their leader <leaderName>." },

        // newsMessage.fleetDamagedInStorm (Q4 #20, FUN_004514EC round tick): confirmed, 2 of 2 events.
        // Closes the other half of bug #88 (the corpus was missing this literal entirely).
        { "fleet.damaged-in-storm", "A fleet belonging to {Nation} is damaged in a storm." },

        // ---- Structural entries: the round tick's own bookkeeping, not tied to any DomainEvent kind.
        // Fetched directly by NewsLogWriter under these synthetic kinds. See this type's remarks. ----

        // newsMessage.dashLine (Q1/Q4 #10, #12): 59 ASCII '-' bytes, the longest text the log ever holds.
        { DashLineKind, "-----------------------------------------------------------" },

        // newsMessage.blankLine (Q3): a single 0x20 space -- not an empty string.
        { BlankLineKind, " " },

        // newsMessage.weekHeader (Q3): docs/task-catalogue.md's T42 entry gives this exact delimited
        // string; see NewsLogWriter.RenderWeekHeader for how <w>/<Season>/<year> are substituted.
        { WeekHeaderKind, "Week  <w>      <Season>      <year>BC" },

        // ---- Test-only pipeline scaffolding (#91 N10, N23) -- never corpus-backed, excluded from the
        // corpus/catalog set-equality check by the "test." prefix. See this type's remarks. ----

        { "test.news-log.pipeline-probe.city-falls", "{CityName}   ({OldOwner})  falls to {NewOwner}." },
        { "test.news-log.pipeline-probe.fleet-lost", "A fleet belonging to {Nation} is lost at sea." },
        { "test.news-log.pipeline-probe.nation-conquered", "{ConqueringNation} conquers {ConqueredNation}." },
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Gets the message template for an event kind, or throws if the kind is not registered.
    /// </summary>
    /// <param name="eventKind">The event's stable kind string.</param>
    /// <returns>The message template, with whichever placeholder syntax its source uses.</returns>
    /// <exception cref="KeyNotFoundException">The event kind has no catalog entry.</exception>
    public static string GetTemplate(string eventKind)
    {
        ArgumentNullException.ThrowIfNull(eventKind);

        if (!Templates.TryGetValue(eventKind, out var template))
        {
            throw new KeyNotFoundException(
                $"Event kind '{eventKind}' has no news message template registered. "
                + "The coverage test should have caught this when the event was declared.");
        }

        return template;
    }

    /// <summary>Whether an event kind has a registered message template.</summary>
    public static bool HasTemplate(string eventKind) => Templates.ContainsKey(eventKind);

    /// <summary>All registered event kinds, sorted for deterministic iteration.</summary>
    public static IEnumerable<string> RegisteredKinds => Templates.Keys.OrderBy(k => k, StringComparer.Ordinal);

    /// <summary>
    /// Whether an elimination-style event's rendered message is wrapped between two
    /// <see cref="DashLineKind"/> entries — confirmed only for <c>nation.conquered</c>
    /// (news-log-format-and-messages.md Q4 #10-12: "An elimination always writes three slots, the banner
    /// between two 59-dash lines [confirmed: 2 of 2]").
    /// </summary>
    public static bool IsWrappedInDashLines(string eventKind) => DashWrappedKinds.Contains(eventKind);
}
