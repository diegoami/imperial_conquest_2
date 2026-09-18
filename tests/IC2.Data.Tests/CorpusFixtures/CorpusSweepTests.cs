using IC2.Inspect;
using Xunit;

namespace IC2.Data.Tests.CorpusFixtures;

/// <summary>
/// docs/build-orchestration-plan.md, T30 Done-when line 4: every file in the configured assets
/// directory (every .sav file across saves/, saves-processed/ and saves-processed/processed/, plus
/// the DAT) runs through every one of IC2.Data's seven parsers and is checked against the committed
/// expected-outcome table in <c>expected-corpus-outcomes.json</c>.
///
/// T34 (bug #57) makes the fixture path-independent: each row is found by file NAME across the three
/// save folders, so moving a save between them — what <c>/process-evidence</c> does once a report
/// cites it — changes no test outcome. See <see cref="IC2.Inspect.CorpusFileLocator"/>.
///
/// Done-when line 8: every test here reports an explicit Skipped result, not a silent pass or a hard
/// failure, when <c>assets.local.ini</c> is absent.
/// </summary>
public class CorpusSweepTests
{
    private const string RegenerateCommand =
        "dotnet run --project src/IC2.Inspect -- --corpus-outcomes " +
        "tests/IC2.Data.Tests/CorpusFixtures/expected-corpus-outcomes.json";

    public static IEnumerable<object[]> Cases() => CorpusFixture.Entries.Select(e => new object[] { e });

    [SkippableTheory]
    [MemberData(nameof(Cases))]
    public void File_parses_clean_through_all_seven_parsers_as_expected(CorpusOutcome expected)
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;

        // T34 #40 item 4 / Done-when line 2: a fixture entry whose file is absent on this machine
        // skips just this one case, naming the file and the regeneration command, rather than failing
        // the whole sweep.
        var path = CorpusFileLocator.TryResolve(settings, expected.FileName);
        Skip.If(path is null,
            $"'{expected.FileName}' is not present in any of the three save folders (or, for the " +
            $"DAT, is not configured) on this machine. Regenerate the fixture with: {RegenerateCommand}");
        var data = File.ReadAllBytes(path!);

        var format = SaveFormat.Detect(data);
        Assert.Equal(expected.Format, format.ToString());
        Assert.Equal(expected.Bytes, data.Length);

        var world = WorldPrefix.Parse(data);
        Assert.Equal(expected.Cities, world.Cities.Count);

        var armyTable = SaveArmyTable.Parse(data);
        Assert.Equal(expected.Armies, armyTable.Armies.Count);
        Assert.Equal(expected.SkippedArmies, armyTable.SkippedRecords.Count);

        var fleetTable = SaveFleetTable.Parse(data);
        Assert.Equal(expected.Fleets, fleetTable.Fleets.Count);

        var nationTable = SaveNationTable.Parse(data);
        Assert.Equal(expected.Nations, nationTable.Nations.Count);
        Assert.Equal(expected.NationCitiesSum, nationTable.Nations.Sum(n => (int)n.CityCount));
        for (var i = 0; i < nationTable.Nations.Count; i++)
            Assert.Equal(NationCatalog.Name((ushort)i), nationTable.Nations[i].Name);

        var recruitment = SaveRecruitmentTable.Parse(data);
        Assert.Equal(expected.RecruitmentEntries, recruitment.Entries.Count);

        // Mercenary pool and calendar trailer: present (and must parse to the recorded count) on
        // every SAV, and genuinely absent (and must throw the typed, expected exception) on the DAT.
        // "Zero unexpected exceptions" means these ARE expected for the DAT row, not "unexpected".
        if (expected.MercenaryRecords is { } mercenaryRecords)
        {
            var mercenaries = SaveMercenaryTable.Parse(data);
            Assert.Equal(mercenaryRecords, mercenaries.Records.Count);
        }
        else
        {
            Assert.Throws<DatDataNotPresentException>(() => SaveMercenaryTable.Parse(data));
        }

        if (expected.TurnWeek is { } week)
        {
            var turn = SaveTurnState.Parse(data);
            Assert.Equal(week, turn.Week);
        }
        else
        {
            Assert.Throws<DatDataNotPresentException>(() => SaveTurnState.Parse(data));
        }
    }

    /// <summary>
    /// Cross-checks that the committed table actually enumerates every file name currently present
    /// under the configured directory's three save folders, plus the DAT — so the fixture cannot go
    /// silently stale the way the earlier directory-relative-path check did (bug #57). A file on disk
    /// the fixture does not list is a SKIP (Done-when line 2), naming the files and the regeneration
    /// command — not a failure, since /process-evidence adding or moving saves is routine, not a
    /// defect. A file name present in more than one save folder is a hard failure: a fixture keyed by
    /// name alone cannot represent that (Done-when line 1).
    /// </summary>
    [SkippableFact]
    public void Fixture_covers_every_file_currently_in_the_configured_corpus()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;

        var actual = CorpusFileLocator.DiscoverFileNames(settings);

        var duplicated = actual
            .Where(kv => kv.Value.Count > 1)
            .OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => $"{kv.Key} (in {string.Join(" and ", kv.Value)})")
            .ToList();
        Assert.True(duplicated.Count == 0,
            "File name(s) present in more than one save folder — a fixture keyed by name alone " +
            $"cannot represent this: {string.Join("; ", duplicated)}.");

        var fixtureNames = CorpusFixture.Entries.Select(e => e.FileName).ToHashSet(StringComparer.Ordinal);
        var uncovered = actual.Keys
            .Where(name => !fixtureNames.Contains(name))
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        Skip.If(uncovered.Count > 0,
            $"The fixture does not list: {string.Join(", ", uncovered)}. Regenerate it with: {RegenerateCommand}");

        // Structural invariants that hold regardless of how many files the corpus currently has —
        // deliberately NOT a hard-coded total file/tombstone count, which is exactly the kind of
        // snapshot-in-time assertion bug #57 is about (the corpus grows as /process-evidence adds and
        // moves saves). There is always exactly one DAT row; no file name is listed twice in the
        // fixture itself; and no save carries more tombstones than the confirmed corpus has ever
        // shown for one save (docs/investigations/dat-file-layout.md / T34 #40 item 1: "the confirmed
        // corpus has at most one per save") — a file that broke this would be new information worth
        // surfacing, not drift to silently accept.
        Assert.Equal(1, CorpusFixture.Entries.Count(e => e.Format == "Dat"));
        Assert.Equal(CorpusFixture.Entries.Count,
            CorpusFixture.Entries.Select(e => e.FileName).Distinct(StringComparer.Ordinal).Count());
        Assert.All(CorpusFixture.Entries, e =>
            Assert.True(e.SkippedArmies <= 1, $"{e.FileName} has {e.SkippedArmies} skipped army records."));
    }
}
