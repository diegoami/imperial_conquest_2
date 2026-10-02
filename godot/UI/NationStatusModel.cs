using IC2.Engine.Model;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// One rendered line of a nation's status panel, keyed so a test can ask for a fact without matching the
/// human wording. <see cref="Text"/> is what the context panel draws.
/// </summary>
public sealed record NationStatusLine(string Key, string Text);

/// <summary>
/// The context panel's nation status panel — the Godot-free lines for <em>any</em> nation, own or
/// foreign, so the panel and its tests share one rule.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Evidence.</strong> The original's nation panel lists "leader, capital, cities, population,
/// unity <strong>as a word</strong>, tax %, mobilized %, treasury, and a per-nation
/// international-relations list" (<c>ptolemy-run-ui-inventory-and-leader-draw.md</c> §1). The foreign
/// panel's withheld half is the user's decision of 2026-10-01 (the task entry's Scope): a foreign
/// nation shows public facts only — its cities, capital and leader, and its relation with the viewer's
/// nation — and "no treasury, tax rate, population, unity, mobilization or units in training", in line
/// with the confirmed fog on foreign armies (that report §1 and §5: composition and terrain are public,
/// moves, supply, morale and money are withheld).
/// </para>
/// <para>
/// <strong>Unity is shown as the number, not a word.</strong> The report names unity as a word but also
/// records that "none of the three ladders is mapped beyond the points observed so far" (§5). There is
/// no word table to copy, and the task entry's hazard says to show the number and say so when the report
/// gives none. This is that case; the PR body records it.
/// </para>
/// <para>
/// The "viewer" is the <em>active seat's</em> nation, not the viewed one: selecting Carthage while Rome
/// is the active seat shows Carthage's public facts relative to Rome, and the relation line is
/// Carthage–Rome. That is why callers pass <see cref="GameState.ActiveNationId"/> as
/// <c>viewerNationId</c>.
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

    /// <summary><see cref="NationState.Population"/> — own nation only.</summary>
    public const string PopulationKey = "population";

    /// <summary><see cref="NationState.Unity"/> — own nation only.</summary>
    public const string UnityKey = "unity";

    /// <summary><see cref="NationState.TaxRatePercent"/> — own nation only.</summary>
    public const string TaxRateKey = "tax_rate";

    /// <summary><see cref="NationState.MobilizedPercent"/> — own nation only.</summary>
    public const string MobilizedKey = "mobilized";

    /// <summary><see cref="NationState.Treasury"/> — own nation only.</summary>
    public const string TreasuryKey = "treasury";

    /// <summary>Prefix of the per-nation relation lines, then the other nation's id.</summary>
    public const string RelationKeyPrefix = "relation.";

    /// <summary>The "Regiments in training" section header — own nation only.</summary>
    public const string TrainingHeaderKey = "training_header";

    /// <summary>The "None." line under the training header — own nation only.</summary>
    public const string TrainingNoneKey = "training_none";

    /// <summary>Prefix of a training line, then the slot index — own nation only.</summary>
    public const string TrainingKeyPrefix = "training.";

    /// <summary>
    /// Builds the status panel's lines for <paramref name="nationId"/>. <paramref name="viewerNationId"/>
    /// is the active seat's nation; when it equals <paramref name="nationId"/> the panel is the own
    /// nation's and carries the full list, otherwise it carries public facts only.
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

        var lines = new List<NationStatusLine>
        {
            new(LeaderKey, $"Leader: {nation.LeaderName}"),
            new(CapitalKey, $"Capital: {CapitalName(state, nation)}"),
            new(CitiesKey, $"Cities: {ownedCities.Count}"),
            new(CityNamesKey, ownedCities.Count == 0
                ? "No cities."
                : string.Join(", ", ownedCities.Select(city => city.Name))),
        };

        if (isOwnNation)
        {
            lines.Add(new NationStatusLine(PopulationKey, $"Population: {nation.Population}"));
            lines.Add(new NationStatusLine(UnityKey, $"Unity: {nation.Unity}"));
            lines.Add(new NationStatusLine(TaxRateKey, $"Tax rate: {nation.TaxRatePercent}%"));
            lines.Add(new NationStatusLine(MobilizedKey, $"Mobilized: {nation.MobilizedPercent}%"));
            lines.Add(new NationStatusLine(TreasuryKey, $"Treasury: {nation.Treasury}"));
            AddRelations(state, ruleset, nationId, otherNationId: null, lines);
            AddTraining(state, ruleset, nationId, lines);
        }
        else if (viewerNationId is not null)
        {
            AddRelations(state, ruleset, nationId, otherNationId: viewerNationId, lines);
        }

        return lines;
    }

    private static string CapitalName(GameState state, NationState nation) =>
        nation.CapitalCityId is { } capitalId && state.CityById(capitalId) is { } capital
            ? capital.Name
            : "none";

    /// <summary>
    /// Adds one line per other nation — the full matrix for the own nation, or only the viewer's cell
    /// for a foreign one. A nation absent from the matrix is skipped rather than given a fabricated
    /// relation.
    /// </summary>
    private static void AddRelations(
        GameState state,
        Ruleset ruleset,
        string nationId,
        string? otherNationId,
        List<NationStatusLine> lines)
    {
        if (state.Relations.IndexOf(nationId) < 0)
        {
            return;
        }

        foreach (var other in state.Nations)
        {
            if (otherNationId is not null && !string.Equals(other.Id, otherNationId, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(other.Id, nationId, StringComparison.Ordinal)
                || state.Relations.IndexOf(other.Id) < 0)
            {
                continue;
            }

            var value = state.Relations.Get(nationId, other.Id);
            lines.Add(new NationStatusLine(
                RelationKeyPrefix + other.Id,
                $"{other.Name}: {RelationLabel(value, ruleset.Diplomacy.StateCodes)}"));
        }
    }

    /// <summary>
    /// Bug #513's fix, kept: the own nation's panel lists every regiment in training at every city. A
    /// foreign nation gets no line at all (public facts only).
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

    /// <summary>
    /// Words a raw relation cell for display, the same rule (and wording) as
    /// <c>DiplomacyGridViewModel</c>'s private <c>RelationLabel</c>: a negative value is a cooldown
    /// counter, not a fourth state (<c>DiplomaticRelations</c>'s own remarks), so it is worded as one.
    /// </summary>
    private static string RelationLabel(int value, RelationStateCodes codes)
    {
        if (value < 0)
        {
            return $"Cooldown ({-value})";
        }

        if (value == codes.War)
        {
            return "War";
        }

        if (value == codes.Alliance)
        {
            return "Alliance";
        }

        if (value == codes.Trade)
        {
            return "Trade";
        }

        if (value == codes.Peace)
        {
            return "Peace";
        }

        return $"Unknown ({value})";
    }
}
