using System.Text.Json;
using Xunit;

namespace IC2.Engine.Tests.Fixtures;

/// <summary>
/// The four "Done when" checks for T04 (docs/build-orchestration-plan.md, "T04 Fixtures corpus").
/// Each test method here is exactly one Done-when line, in the same order the task entry lists
/// them, so a reviewer re-running the DoD can match method to line directly.
/// </summary>
public class FixturesCorpusTests
{
    // ---- Done when 1: a test loads the corpus and fails if any entry has an empty value,
    // source, or tag. ----

    [Fact]
    public void NoEntryHasAnEmptyValueSourceOrTag()
    {
        var entries = Fixtures.All;
        Assert.NotEmpty(entries);

        var offenders = entries
            .Where(e => string.IsNullOrWhiteSpace(e.Source)
                        || string.IsNullOrWhiteSpace(e.Tag)
                        || IsEmptyValue(e.Value))
            .Select(e => e.Id)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Entries with an empty value/source/tag: {string.Join(", ", offenders)}");
    }

    private static bool IsEmptyValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Undefined => true,
        JsonValueKind.Null => true,
        JsonValueKind.String => string.IsNullOrEmpty(value.GetString()),
        JsonValueKind.Array => value.GetArrayLength() == 0,
        JsonValueKind.Object => !value.EnumerateObject().Any(),
        // Numbers and booleans (including 0 and false) are valid, non-empty values -- a
        // transcribed "0" or "false" is real data, not a missing entry.
        JsonValueKind.Number => false,
        JsonValueKind.True => false,
        JsonValueKind.False => false,
        _ => true,
    };

    // ---- Done when 2: a test asserts every `source` names a file present in a committed
    // manifest of the research repo's real report filenames. Fully offline: reads the committed
    // manifest only, never the network or a cloned research repo. ----

    [Fact]
    public void EverySourceNamesAFileInTheKnownReportsManifest()
    {
        var known = new HashSet<string>(Fixtures.KnownReportFilenames, StringComparer.Ordinal);
        Assert.NotEmpty(known);

        var unknownSources = Fixtures.All
            .Select(e => e.Source)
            .Distinct(StringComparer.Ordinal)
            .Where(source => !known.Contains(source))
            .ToList();

        Assert.True(unknownSources.Count == 0,
            $"Sources not present in known-reports.json: {string.Join(", ", unknownSources)}");
    }

    // ---- Done when 3: a test asserts the corpus contains every id in a committed required-ids
    // list, so a later task cannot silently find its fixture missing. ----

    [Fact]
    public void CorpusContainsEveryRequiredId()
    {
        var required = Fixtures.RequiredIds;
        Assert.NotEmpty(required);

        var present = new HashSet<string>(Fixtures.All.Select(e => e.Id), StringComparer.Ordinal);
        var missing = required.Where(id => !present.Contains(id)).ToList();

        Assert.True(missing.Count == 0,
            $"Required ids missing from the corpus: {string.Join(", ", missing)}");
    }

    // ---- Done when 4: a test asserts no two entries share an id. ----

    [Fact]
    public void NoTwoEntriesShareAnId()
    {
        var duplicates = Fixtures.All
            .GroupBy(e => e.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        Assert.True(duplicates.Count == 0,
            $"Duplicate fixture ids: {string.Join(", ", duplicates)}");
    }

    // ---- Supporting checks: not a Done-when line, but cheap and directly relevant to the
    // hazard this task exists to prevent (silently wrong transcription). ----

    [Fact]
    public void EveryTagIsOneOfTheThreeConventionValues()
    {
        var validTags = new HashSet<string>(
            Enum.GetNames<FixtureTag>().Select(n => n.ToLowerInvariant()),
            StringComparer.Ordinal);

        var invalid = Fixtures.All
            .Where(e => !validTags.Contains(e.Tag.ToLowerInvariant()))
            .Select(e => $"{e.Id}={e.Tag}")
            .ToList();

        Assert.True(invalid.Count == 0,
            $"Entries with a tag outside confirmed/derived/designed: {string.Join(", ", invalid)}");
    }

    [Fact]
    public void ASampleOfEntriesResolveToTheExpectedTranscribedValues()
    {
        // A handful of spot checks that the loader round-trips the corpus correctly -- not a
        // Done-when line, but cheap insurance that Fixtures.Get actually works the way later
        // tasks (T06-T19) will rely on it working.
        Assert.Equal(2440, Fixtures.Get("tax.nationTaxBaseRome").AsInt());
        Assert.Equal(442, Fixtures.Get("roman13.regularUpkeepQuarterly").AsInt());
        Assert.Equal(6438, Fixtures.Get("mercenary.felsina.troops").AsInt());
        Assert.Equal(-8, Fixtures.Get("diplomacy.cooldown.brokenTrade").AsInt());
        Assert.Equal(40, Fixtures.Get("loyalty.floor.forcedCapture").AsInt());
        Assert.Equal("confirmed", Fixtures.Get("tax.nationTaxBaseRome").Tag);
        Assert.Equal(FixtureTag.Confirmed, Fixtures.Get("tax.nationTaxBaseRome").ParsedTag());
        Assert.Equal(4, Fixtures.Get("terrain.moveCost.code5").AsInt()); // Mountains
        Assert.Throws<KeyNotFoundException>(() => Fixtures.Get("no.such.fixture.id"));
    }
}
