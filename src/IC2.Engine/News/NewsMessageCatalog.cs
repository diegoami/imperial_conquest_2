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
/// Two entries (from <c>decompiled-city-capture-resolution.md</c>) use <c>{PascalCase}</c>; the rest
/// (mostly from <c>decompiled-diplomacy-peace-terms-and-instant-battles.md</c>) use
/// <c>&lt;lowercase&gt;</c>, some with a <c>[field[+offset]]</c> provenance annotation the source report
/// itself included in its quoted string (for example <c>&lt;cityName[fleet[+20]]&gt;</c>). T10's
/// renderer (<see cref="NewsLogWriter"/>) recognises both delimiters, matches the token inside — ignoring
/// any bracket annotation — against the published event's own declared properties (case-insensitively for
/// the angle-bracket form), and substitutes the property's value; the annotation itself is never printed.
/// Two entries (<c>peace.pays-reparations</c>'s "N" and <c>diplomacy.pending-offer</c>'s "X"/"Y") carry no
/// bracket delimiter at all in the source text — the corpus transcribes them exactly as written, and this
/// catalog does the same rather than inventing a delimiter no report shows. Substitution is therefore not
/// available for those two until a later task finds or confirms an exact format string; until then they
/// render as literal, static text.
/// </para>
/// <para>
/// <strong>Three entries are <c>[designed]</c>.</strong> <c>city.defects-to</c>, <c>nation.conquered</c>
/// and <c>fleet.lost-at-sea</c> have no confirmed format string in any report — the corpus only has one
/// observed in-game instance (a screenshot) for each, already substituted. What was searched and came up
/// empty: <c>galatia-elimination-and-city-resupply-confirmed.md</c> and <c>fleet-owner-field-confirmed.md</c>
/// (the two source reports) and every other report in <c>tests/fixtures/known-reports.json</c> whose
/// filename mentions the mechanic; none quotes the underlying pseudocode's literal format string. The
/// template shape chosen here — bracketed <c>{PascalCase}</c> operands, matching the two confirmed
/// <c>decompiled-city-capture-resolution.md</c> entries' own convention — is this task's designed choice,
/// and <c>NewsMessageCatalogTests</c> verifies it: rendering the designed template with the observed
/// example's own operand values reproduces the corpus's literal exactly.
/// </para>
/// <para>
/// Every news-worthy domain event kind the engine declares must have an entry here, asserted by
/// <c>NewsCoverageTests</c>: a later task that declares one without adding its own message literal fails
/// CI (<c>docs/build-process.md</c> §2.5 — emission, and the literal, stay with the task that owns the
/// mechanic).
/// </para>
/// </remarks>
public static class NewsMessageCatalog
{
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

        // newsMessage.paysReparations (decompiled-diplomacy-peace-terms-and-instant-battles.md) — the
        // source text's own placeholder is the bare letter "N", with no bracket delimiter. Substitution is
        // not available for it; see this type's remarks.
        { "peace.pays-reparations", "    <loser> pays reparations of N talents." },

        // newsMessage.allyPeaceAgreement (decompiled-diplomacy-peace-terms-and-instant-battles.md) — a
        // distinct line from peace.honourable in the source pseudocode (different placeholder names, same
        // text pattern), fired when an ally of either treaty side is still at war with the other.
        { "peace.ally-agreement", "<A> and <B> have agreed to end their war." },

        // newsMessage.fleetFinished (decompiled-unit-map-orders-and-record-fields.md) — the report's own
        // quoted string keeps the "[fleet[+20]]" field-provenance annotation on the city-name placeholder.
        { "fleet.finished", "<nation> finishes a new fleet at <cityName[fleet[+20]]>" },

        // newsMessage.victoryAllCities (decompiled-diplomacy-peace-terms-and-instant-battles.md) — static
        // text, no placeholder; the original's spelling "conquerred" is preserved verbatim, not corrected.
        { "victory.all-cities", "You have conquerred the Mediterranean, a unique achievement." },

        // newsMessage.conqueredByNation (decompiled-diplomacy-peace-terms-and-instant-battles.md) —
        // "+0x44E" is the report's own field-provenance annotation on the placeholder; the field offset
        // itself is "inferred from its only use, not otherwise confirmed" per that report, which does not
        // affect this task (only the message text is T10's concern).
        { "victory.conquered-by-nation", "Your nation has been conquerred by <nation[+0x44E]>." },

        // newsMessage.pendingDiplomaticOfferParaphrase (decompiled-turn-and-calendar-sequencing.md) —
        // tagged "derived" in the corpus, not "confirmed": the report presents this as an italicised
        // paraphrase of TPremierForm_StartTurn's announcement, not a quoted literal, and its placeholders
        // are the bare letters "X"/"Y" with no bracket delimiter. Substitution is not available for it;
        // see this type's remarks.
        { "diplomacy.pending-offer", "X wants to trade/form an alliance with Y" },

        // ---- [designed]: generalised from a single observed example. See this type's remarks. ----

        // Generalises newsMessage.defectsFromObservedExample ("Synnada defects from Galatia to
        // Seleucid."); rendering with CityName=Synnada, OldOwner=Galatia, NewOwner=Seleucid reproduces it.
        { "city.defects-to", "{CityName} defects from {OldOwner} to {NewOwner}." },

        // Generalises newsMessage.conquersNationObservedExample ("Seleucid conquers Galatia."); rendering
        // with ConqueringNation=Seleucid, ConqueredNation=Galatia reproduces it.
        { "nation.conquered", "{ConqueringNation} conquers {ConqueredNation}." },

        // Generalises newsMessage.fleetLostAtSeaObservedExample ("A fleet belonging to Carthage is lost
        // at sea"); rendering with Nation=Carthage reproduces it.
        { "fleet.lost-at-sea", "A fleet belonging to {Nation} is lost at sea" },
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
}
