using System.Text.Json;

namespace IC2.Engine.Tests.Fixtures;

/// <summary>
/// Loads and exposes the T04 fixtures corpus: every exact number transcribed from the research
/// repo's <c>docs/reports/</c> (docs/build-orchestration-plan.md, "T04 Fixtures corpus"). Later
/// tasks assert against <see cref="Get"/> instead of each re-reading 46 reports (§2.4 of that plan).
/// </summary>
/// <remarks>
/// <para>
/// Loading is lazy and cached (a static field, initialized once per test run) rather than
/// re-parsed per call — deterministic, since the corpus file itself is fixed content and parsing
/// order follows the JSON array's own file order, never a hash-based enumeration.
/// </para>
/// <para>
/// Named <c>FixtureCorpus</c>, not <c>Fixtures</c> (its name through T04's first review round):
/// a type named identically to the last segment of its own declaring namespace
/// (<c>IC2.Engine.Tests.Fixtures</c>) cannot be referred to by its simple name from any sibling
/// namespace — C# simple-name lookup binds to the enclosing <em>namespace</em> member before it
/// ever consults a <c>using</c> directive, so <c>Fixtures.Get(...)</c> failed to compile
/// (<c>CS0234</c>) from every other task's own test subfolder, exactly the call site
/// docs/build-orchestration-plan.md §2.4 promises every later task will use. See
/// <c>DownstreamNamespaceUsageTests</c> in this folder for a test that exercises that call site
/// directly and would have caught the collision.
/// </para>
/// </remarks>
public static class FixtureCorpus
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

        var index = 0;
        foreach (var element in entriesElement.EnumerateArray())
        {
            entries.Add(ParseEntry(element, index));
            index++;
        }

        return entries;
    }

    /// <summary>
    /// Parses one entry, wrapping any structural failure (a required property missing or JSON
    /// <c>null</c>) with the array index — and the id, if it was itself readable — so a malformed
    /// entry fails with one diagnosable message instead of a bare exception from deep inside
    /// property access. Deliberately does NOT reject an empty-but-present string (<c>""</c>): that
    /// is a different failure mode (a value someone actually wrote, just wrong), left for
    /// <c>FixturesCorpusTests.NoEntryHasAnEmptyValueSourceOrTag</c> to catch as a clean assertion.
    /// </summary>
    private static FixtureEntry ParseEntry(JsonElement element, int index)
    {
        string? id = null;
        try
        {
            id = RequirePresentString(element, "id");
            var value = element.GetProperty("value").Clone();
            var source = RequirePresentString(element, "source");
            var tag = RequirePresentString(element, "tag");
            var note = element.TryGetProperty("note", out var noteElement) ? noteElement.GetString() : null;

            return new FixtureEntry(id, value, source, tag, note);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidDataException)
        {
            var idPart = id is null ? "" : $" (id '{id}')";
            throw new InvalidDataException(
                $"Malformed fixture entry at corpus.json entries[{index}]{idPart}: {ex.Message}", ex);
        }
    }

    private static string RequirePresentString(JsonElement element, string propertyName) =>
        element.GetProperty(propertyName).GetString()
            ?? throw new InvalidDataException($"Property '{propertyName}' is missing or JSON null.");

    private static IReadOnlyDictionary<string, FixtureEntry> BuildIndex()
    {
        // A duplicate id is a hard load-time failure here (not last-wins), so every accessor
        // built on Get/TryGet can never silently observe a stale duplicate. This does NOT
        // replace FixturesCorpusTests.NoTwoEntriesShareAnId (Done-when 4): that test asserts
        // directly over FixtureCorpus.All, which never calls this method, so it still produces
        // a clean assertion failure naming every duplicate rather than the single exception
        // below -- the two paths are complementary, not redundant.
        var index = new Dictionary<string, FixtureEntry>(StringComparer.Ordinal);
        foreach (var entry in All)
        {
            if (!index.TryAdd(entry.Id, entry))
            {
                throw new InvalidDataException(
                    $"Duplicate fixture id '{entry.Id}' in corpus.json -- ids must be unique.");
            }
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
