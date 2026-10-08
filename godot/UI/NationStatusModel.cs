using IC2.Engine.Model;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// One rendered line of a nation's status panel, keyed so a test can ask for a fact without matching the
/// human wording. <see cref="Text"/> is what the context panel draws; <see cref="IsRed"/> flags the
/// conquered-nation row the original draws red (T140, row N12).
/// </summary>
public sealed record NationStatusLine(string Key, string Text, bool IsRed = false);

/// <summary>
/// The context panel's nation status panel — the Godot-free lines for <em>any</em> nation, own or
/// foreign, so the panel and its tests share one rule. T140 (bug #718) rewrote this on top of the
/// research-read rules (rows N01–N12 of
/// <c>2026-10-05-information-window-fields-and-bands.md</c>), so the original's headers and the
/// foreign panel's added public-facts lines all live here together.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Unity is now a word,</strong> read from <see cref="InformationWords.Unity"/>. The previous
/// version printed the number; the research read pins a six-word band table the original draws, so
/// this is the rewrite's central evidence-driven change.
/// </para>
/// <para>
/// <strong>The foreign panel now matches the original.</strong> The 2026-10-01 default ("public facts
/// only" — Cities, Capital, Leader, the one viewer relation) is replaced by the 2026-10-05 decision:
/// the foreign panel adds <em>Population</em> (= <see cref="NationState.Wealth"/>, row N05),
/// <em>Unity</em> (a word, row N06) and <em>Tax rate</em> (row N07), and every other nation's relation
/// (row N11) in place of the viewer's cell alone; <em>Mobilized</em> and <em>Treasury</em> stay blank
/// (rows N08, N09). The viewer's relation is the relation the shown nation has <em>toward</em> the
/// viewer: <c>state.Relations.Get(shownNationId, viewerNationId)</c>.
/// </para>
/// <para>
/// <strong>A conquered-nation row</strong> (row N12) replaces the row that would name the eliminated
/// nation with <c>"   ( X conquerred by Y )"</c> and flags <see cref="NationStatusLine.IsRed"/>
/// — the original's <c>TInformation_PaintForm</c> draws a line that starts with <c>"   ("</c> in red,
/// and <c>NationState.Eliminated</c> is the gate the clone tests
/// (<c>diplomacy-elimination-and-peace-terms.md</c>). The misspelling <c>"conquerred"</c> matches the
/// original verbatim.
/// </para>
/// <para>
/// <strong>The "viewer" is the active seat's nation</strong>, not the viewed one: with Rome the active
/// seat, selecting Carthage shows Carthage's facts relative to Rome. Callers pass
/// <see cref="GameState.ActiveNationId"/> as <c>viewerNationId</c>.
/// </para>
/// </remarks>
public static class NationStatusModel
{
    /// <summary>The nation's leader name (per-game state, not world data).</summary>
    public const string LeaderKey = "leader";

    /// <summary>The nation's live capital city, by name.</summary>
    public const string CapitalKey = "capital";

    /// <summary>The count of cities the nation owns.</summary>
    public const string CitiesKey = "cities";

    /// <summary>The owned cities' names, in <see cref="GameState.Cities"/> order.</summary>
    public const string CityNamesKey = "city_names";

    /// <summary><see cref="NationState.Wealth"/> — both own and foreign. Bug #742's fix:
    /// <see cref="NationState.Population"/> is a different field whose value disagrees with the sum of
    /// city panels by a factor of 3.</summary>
    public const string PopulationKey = "population";

    /// <summary>The unity band word (see <see cref="InformationWords.Unity"/>) — both own and foreign.</summary>
    public const string UnityKey = "unity";

    /// <summary><see cref="NationState.TaxRatePercent"/> — both own and foreign.</summary>
    public const string TaxRateKey = "tax_rate";

    /// <summary><see cref="NationState.MobilizedPercent"/> — own nation only.</summary>
    public const string MobilizedKey = "mobilized";

    /// <summary><see cref="NationState.Treasury"/> — own nation only.</summary>
    public const string TreasuryKey = "treasury";

    /// <summary>Prefix of the per-nation relation lines, then the other nation's id.</summary>
    public const string RelationKeyPrefix = "relation.";

    /// <summary>N10's blank line above the INTERNATIONAL RELATIONS heading, on both panels.</summary>
    public const string RelationsSpacerKey = "relations_spacer";

    /// <summary>The "Regiments in training" section header — own nation only.</summary>
    public const string TrainingHeaderKey = "training_header";

    /// <summary>The "None." line under the training header — own nation only.</summary>
    public const string TrainingNoneKey = "training_none";

    /// <summary>Prefix of a training line, then the slot index — own nation only.</summary>
    public const string TrainingKeyPrefix = "training.";

    /// <summary>
    /// Builds the status panel's lines for <paramref name="nationId"/>. <paramref name="viewerNationId"/>
    /// is the active seat's nation; the panel is <em>always</em> the shown nation's facts, with the
    /// own-only fields (Mobilized, Treasury, Training) on when viewer equals shown.
    /// </summary>
    public static IReadOnlyList<NationStatusLine> Build(
        GameState state,
        Ruleset ruleset,
        string nationId,
        string? viewerNationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var nation = state.NationById(nationId)
            ?? throw new ArgumentException($"No nation '{nationId}' in the state.", nameof(nationId));

        var isOwnNation = viewerNationId is not null
            && string.Equals(nationId, viewerNationId, StringComparison.Ordinal);

        var ownedCities = state.Cities
            .Where(city => string.Equals(city.Owner, nationId, StringComparison.Ordinal))
            .ToList();

        // N01–N04: every nation's panel carries leader, capital, cities, city names.
        var lines = new List<NationStatusLine>
        {
            new(LeaderKey, $"Leader: {nation.LeaderName}"),
            new(CapitalKey, $"Capital: {CapitalName(state, nation)}"),
            new(CitiesKey, $"Cities: {ownedCities.Count}"),
            new(CityNamesKey, ownedCities.Count == 0
                ? "No cities."
                : string.Join(", ", ownedCities.Select(city => city.Name))),
        };

        // N05–N07: Population (Wealth), Unity word, Tax rate — both own and foreign.
        lines.Add(new NationStatusLine(PopulationKey, $"Population: {nation.Wealth}"));
        lines.Add(new NationStatusLine(UnityKey, $"Unity: {InformationWords.Unity(nation.Unity)}"));
        lines.Add(new NationStatusLine(TaxRateKey, $"Tax rate: {nation.TaxRatePercent}%"));

        // N08, N09: Mobilized and Treasury. The own nation shows the values; a foreign nation
        // (including one selected for viewing from the Nations menu while another's turn runs)
        // still gets the two rows, in their position, with the values blank — the original
        // keeps the rows but withholds the figures (R3 of the R1 review: a foreign panel that
        // omits the rows entirely hides the field's place in the original's order).
        if (isOwnNation)
        {
            lines.Add(new NationStatusLine(MobilizedKey, $"Mobilized: {nation.MobilizedPercent}%"));
            // N09: the own Treasury line carries the same ' talents' suffix the engine's other
            // money lines use (R4 of the R1 review: the omission broke the panel's text
            // consistency with city tribute and army money lines).
            lines.Add(new NationStatusLine(TreasuryKey, $"Treasury: {nation.Treasury} talents"));
        }
        else
        {
            lines.Add(new NationStatusLine(MobilizedKey, "Mobilized:"));
            lines.Add(new NationStatusLine(TreasuryKey, "Treasury:"));
        }

        // N10: spacer and heading.
        lines.Add(new NationStatusLine(RelationsSpacerKey, string.Empty));
        lines.Add(new NationStatusLine("international_relations_header", "INTERNATIONAL RELATIONS"));

        // N11, N12: one row per other nation. Relation 0 and below print just the name; relations 1, 2, 3
        // (trade, ally, war) tag the row with the word; an eliminated nation X becomes the red
        // "conquerred by Y" row.
        AddRelations(state, ruleset, nation, isOwnNation, lines);

        if (isOwnNation)
        {
            AddTraining(state, ruleset, nationId, lines);
        }

        return lines;
    }

    private static string CapitalName(GameState state, NationState nation) =>
        nation.CapitalCityId is { } capitalId && state.CityById(capitalId) is { } capital
            ? capital.Name
            : "none";

    /// <summary>
    /// Adds one line per other nation (N11 / N12): an eliminated nation replaces the row with
    /// <c>"   ( X conquerred by Y )"</c> flagged red; relations at or below 0 print only the name;
    /// relations 1–3 print the name and the corresponding word.
    /// </summary>
    private static void AddRelations(
        GameState state,
        Ruleset ruleset,
        NationState shown,
        bool isOwnNation,
        List<NationStatusLine> lines)
    {
        if (state.Relations.IndexOf(shown.Id) < 0)
        {
            return;
        }

        foreach (var other in state.Nations)
        {
            if (string.Equals(other.Id, shown.Id, StringComparison.Ordinal)
                || state.Relations.IndexOf(other.Id) < 0)
            {
                continue;
            }

            if (other.Eliminated)
            {
                // N12: conquered-nation row. The "conquered by" nation is read from other.ConqueredBy;
                // the format and the spelling match the original verbatim.
                var conqueror = other.ConqueredBy is { } conquerorId
                    && state.NationById(conquerorId) is { } conquerorNation
                        ? conquerorNation.Name
                        : string.Empty;
                var line = $"   ( {other.Name} conquerred by {conqueror} )";
                lines.Add(new NationStatusLine(RelationKeyPrefix + other.Id, line, IsRed: true));
                continue;
            }

            var value = state.Relations.Get(shown.Id, other.Id);
            var word = InformationWords.Relation(value);
            var lineText = word.Length == 0 ? other.Name : $"{other.Name}: {word}";
            lines.Add(new NationStatusLine(RelationKeyPrefix + other.Id, lineText));
        }
    }

    /// <summary>
    /// Bug #513's fix, kept: the own nation's panel lists every regiment in training at every city. A
    /// foreign nation gets no line at all (the panel replaces nothing with the original's None / list
    /// in the foreign branch).
    /// </summary>
    private static void AddTraining(
        GameState state,
        Ruleset ruleset,
        string nationId,
        List<NationStatusLine> lines)
    {
        lines.Add(new NationStatusLine(TrainingHeaderKey, "Regiments in training"));
        var inTraining = RecruitmentPanelViewModel.TrainingForNation(state, ruleset, nationId);
        if (inTraining.Count == 0)
        {
            lines.Add(new NationStatusLine(TrainingNoneKey, "None."));
            return;
        }

        for (var i = 0; i < inTraining.Count; i++)
        {
            var regiment = inTraining[i];
            lines.Add(new NationStatusLine(
                TrainingKeyPrefix + i,
                $"{regiment.UnitTypeId} — {regiment.Troops} troops — {regiment.ReadinessText} — at "
                + $"{CityName(state, regiment.TargetCityId)}"));
        }
    }

    private static string CityName(GameState state, string cityId) =>
        state.CityById(cityId)?.Name ?? cityId;
}
