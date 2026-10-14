using System.Text.Json;
using System.Runtime.CompilerServices;
using Xunit;

namespace IC2.Engine.Tests.Battle.Tactical.GoldenMaster;

/// <summary>
/// Strict expectations for comparison discrepancies, never for fixture or runtime exceptions.
/// A changed discrepancy or an unexpected pass requires investigation; fixes delete entries.
/// </summary>
internal sealed class KnownFailureRegistry
{
    internal sealed record Entry(string Case, string FirstDifference, int Issue);
    private readonly IReadOnlyDictionary<string, Entry> _entries;

    public KnownFailureRegistry(string json)
    {
        using var document = ParseDocument(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("KnownFailures must be an array.");
        var entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("KnownFailures entries must be objects.");
            var properties = row.EnumerateObject().Select(p => p.Name).ToArray();
            if (properties.Length != 3 || !properties.ToHashSet(StringComparer.Ordinal).SetEquals(new[] { "Case", "FirstDifference", "Issue" }))
                throw new InvalidDataException("KnownFailures entries require exactly Case, FirstDifference and Issue.");
            if (row.GetProperty("Case").ValueKind != JsonValueKind.String ||
                row.GetProperty("FirstDifference").ValueKind != JsonValueKind.String ||
                row.GetProperty("Issue").ValueKind != JsonValueKind.Number ||
                !row.GetProperty("Issue").TryGetInt32(out var issue))
                throw new InvalidDataException("KnownFailures requires string Case/FirstDifference and integer Issue.");
            var id = row.GetProperty("Case").GetString();
            var difference = row.GetProperty("FirstDifference").GetString();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(difference) || issue is < 1103 or > 1107)
                throw new InvalidDataException("KnownFailures has an empty id/discrepancy or an issue outside bugs 1103–1107.");
            if (!entries.TryAdd(id, new Entry(id, difference, issue)))
                throw new InvalidDataException($"Duplicate known failure: {id}.");
        }
        _entries = entries;
    }

    private static JsonDocument ParseDocument(string json)
    {
        try { return JsonDocument.Parse(json); }
        catch (JsonException error) { throw new InvalidDataException("KnownFailures contains invalid JSON.", error); }
    }

    public IReadOnlyCollection<Entry> Entries => _entries.Values.ToArray();

    public void ValidateCases(IEnumerable<string> cases)
    {
        var valid = cases.ToHashSet(StringComparer.Ordinal);
        foreach (var id in _entries.Keys)
            Assert.True(valid.Contains(id), $"Unused known failure: {id}.");
    }

    public void Check(string id, Action compare)
    {
        ComparisonDiscrepancy? discrepancy = null;
        try { compare(); }
        catch (ComparisonDiscrepancy failure) { discrepancy = failure; }
        if (_entries.TryGetValue(id, out var expected))
        {
            Assert.True(discrepancy is not null, $"Known failure {id} now passes: delete its entry (bug #{expected.Issue}).");
            Assert.Equal(expected.FirstDifference, discrepancy!.Message);
        }
        else if (discrepancy is not null)
        {
            Assert.Fail("UNLISTED " + JsonSerializer.Serialize(new Entry(id, discrepancy.Message, 1107)));
        }
    }
}

internal sealed class ComparisonDiscrepancy(string message) : Exception(message);

internal static class KnownFailures
{
    private static readonly Lazy<KnownFailureRegistry> Registry = new(() => new(File.ReadAllText(SourceFile())));

    // Read checked-in JSON beside this source, without changing project wiring or copying game assets.
    internal static string SourceFile([CallerFilePath] string source = "") => Path.Combine(Path.GetDirectoryName(source)!, "KnownFailures.json");
    internal static KnownFailureRegistry Loaded => Registry.Value;

    public static void Require(bool matches, string discrepancy)
    {
        if (!matches) throw new ComparisonDiscrepancy(discrepancy);
    }

    public static void Check(string id, Action compare) => Loaded.Check(id, compare);

    internal static IEnumerable<string> Cases()
    {
        foreach (var name in GoldenData.FixedSeries)
        {
            yield return nameof(SeriesReplayTests.The_recorded_series_replays_exactly) + ":" + name;
            yield return nameof(PeaceDrawTests.The_treaty_is_offered_where_the_reports_observed_it) + ":" + name;
        }
        foreach (var trial in SeriesCatalogue.HookedTrials())
        {
            var name = trial.Series.Name;
            yield return nameof(SeriesReplayTests.The_hooked_series_replays_exactly) + ":" + name;
            yield return nameof(DrawLogTests.The_port_draws_what_the_original_drew) + ":" + name;
            yield return nameof(PeaceDrawTests.The_port_draws_the_peace_roll_when_the_original_did) + ":" + name;
        }
        foreach (var name in SeriesCatalogue.CraftedPlayed)
            yield return nameof(CraftedSaveTests.The_crafted_battle_replays_from_its_slots) + ":CRAFT_" + name;
        foreach (var name in SeriesCatalogue.CraftedGrid)
            yield return nameof(CraftedSaveTests.Stale_or_doubled_cells_are_not_repaired) + ":CRAFT_" + name;
        yield return nameof(CraftedSaveTests.C2_and_c4_give_the_same_battle);
        yield return nameof(ResumeTests.The_resume_gives_the_ten_files_the_original_wrote);
        yield return nameof(ResumeTests.No_resumed_file_equals_the_uninterrupted_tail);
        yield return nameof(ResumeTests.The_resume_through_a_clone_save_gives_the_same_ten_files);
    }
}
