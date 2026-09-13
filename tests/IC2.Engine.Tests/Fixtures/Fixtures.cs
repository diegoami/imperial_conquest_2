using System.Text.Json;

namespace IC2.Engine.Tests.Fixtures;

/// <summary>
/// Loads and exposes the T04 fixtures corpus: every exact number transcribed from the research
/// repo's <c>docs/reports/</c> (docs/build-orchestration-plan.md, "T04 Fixtures corpus"). Later
/// tasks assert against <see cref="Get"/> instead of each re-reading 46 reports (§2.4 of that plan).
/// </summary>
/// <remarks>
/// Loading is lazy and cached (a static field, initialized once per test run) rather than
/// re-parsed per call — deterministic, since the corpus file itself is fixed content and parsing
/// order follows the JSON array's own file order, never a hash-based enumeration.
/// </remarks>
public static class Fixtures
{
    private static readonly Lazy<IReadOnlyList<FixtureEntry>> LazyAll = new(LoadCorpus);
    private static readonly Lazy<IReadOnlyDictionary<string, FixtureEntry>> LazyById = new(BuildIndex);
    private static readonly Lazy<IReadOnlyList<string>> LazyKnownReportFilenames = new(LoadKnownReportFilenames);
    private static readonly Lazy<IReadOnlyList<string>> LazyRequiredIds = new(LoadRequiredIds);

    /// <summary>Every entry in the corpus, in the order they appear in <c>corpus.json</c>.</summary>
    public static IReadOnlyList<FixtureEntry> All => LazyAll.Value;

    /// <summary>
    /// The committed manifest of the research repo's real <c>docs/reports/</c> filenames
    /// (<c>tests/fixtures/known-reports.json</c>), generated from
    /// <c>gh api repos/diegoami/imperial-conquest-2-research/contents/docs/reports</c>.
    /// </summary>
    public static IReadOnlyList<string> KnownReportFilenames => LazyKnownReportFilenames.Value;

    /// <summary>
    /// The committed list of fixture ids the T04 task's own "minimum contents" scope requires
    /// (<c>tests/fixtures/required-ids.json</c>).
    /// </summary>
    public static IReadOnlyList<string> RequiredIds => LazyRequiredIds.Value;

    /// <summary>Looks up one fixture entry by id. Throws <see cref="KeyNotFoundException"/> if absent.</summary>
    public static FixtureEntry Get(string id)
    {
        if (LazyById.Value.TryGetValue(id, out var entry))
        {
            return entry;
        }

        throw new KeyNotFoundException($"No fixture with id '{id}' in {FixturePaths.CorpusFile}.");
    }

    /// <summary>Looks up one fixture entry by id, or <c>null</c> if absent.</summary>
    public static FixtureEntry? TryGet(string id) =>
        LazyById.Value.TryGetValue(id, out var entry) ? entry : null;

    private static IReadOnlyList<FixtureEntry> LoadCorpus()
    {
        using var stream = File.OpenRead(FixturePaths.CorpusFile);
        using var document = JsonDocument.Parse(stream);

        var entriesElement = document.RootElement.GetProperty("entries");
        var entries = new List<FixtureEntry>(entriesElement.GetArrayLength());

        foreach (var element in entriesElement.EnumerateArray())
        {
            var id = element.GetProperty("id").GetString()
                ?? throw new InvalidDataException("Fixture entry has a null 'id'.");
            var value = element.GetProperty("value").Clone();
            var source = element.GetProperty("source").GetString()
                ?? throw new InvalidDataException($"Fixture '{id}' has a null 'source'.");
            var tag = element.GetProperty("tag").GetString()
                ?? throw new InvalidDataException($"Fixture '{id}' has a null 'tag'.");
            var note = element.TryGetProperty("note", out var noteElement)
                ? noteElement.GetString()
                : null;

            entries.Add(new FixtureEntry(id, value, source, tag, note));
        }

        return entries;
    }

    private static IReadOnlyDictionary<string, FixtureEntry> BuildIndex()
    {
        // Built with an explicit loop (not entries.ToDictionary, which throws on the first
        // duplicate with a message that names neither id) so a duplicate id in the corpus fails
        // clearly and points at FixturesCorpusTests' own uniqueness assertion (Done-when line 4)
        // instead of surfacing as an ArgumentException from deep inside LINQ.
        var index = new Dictionary<string, FixtureEntry>(StringComparer.Ordinal);
        foreach (var entry in All)
        {
            index[entry.Id] = entry;
        }

        return index;
    }

    private static IReadOnlyList<string> LoadKnownReportFilenames()
    {
        using var stream = File.OpenRead(FixturePaths.KnownReportsFile);
        using var document = JsonDocument.Parse(stream);

        return document.RootElement.GetProperty("reportFilenames")
            .EnumerateArray()
            .Select(e => e.GetString() ?? throw new InvalidDataException("Null entry in reportFilenames."))
            .ToList();
    }

    private static IReadOnlyList<string> LoadRequiredIds()
    {
        using var stream = File.OpenRead(FixturePaths.RequiredIdsFile);
        using var document = JsonDocument.Parse(stream);

        return document.RootElement.GetProperty("requiredIds")
            .EnumerateArray()
            .Select(e => e.GetString() ?? throw new InvalidDataException("Null entry in requiredIds."))
            .ToList();
    }
}
