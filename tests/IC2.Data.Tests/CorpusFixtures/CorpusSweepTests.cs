using Xunit;

namespace IC2.Data.Tests.CorpusFixtures;

/// <summary>
/// docs/build-orchestration-plan.md, T30 Done-when line 4: every file in the configured assets
/// directory (all 51 .sav files across saves/, saves-processed/ and saves-processed/processed/, plus
/// the DAT) runs through every one of IC2.Data's seven parsers and is checked against the committed
/// expected-outcome table in <c>expected-corpus-outcomes.json</c> — replacing a sampled check that had
/// under-counted the tombstone class at 2 saves because only the Thracia series was sampled.
/// Done-when line 8: every test here reports an explicit Skipped result, not a silent pass or a hard
/// failure, when <c>assets.local.ini</c> is absent.
/// </summary>
public class CorpusSweepTests
{
    public static IEnumerable<object[]> Cases() => CorpusFixture.Entries.Select(e => new object[] { e });

    [SkippableTheory]
    [MemberData(nameof(Cases))]
    public void File_parses_clean_through_all_seven_parsers_as_expected(CorpusEntry expected)
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;
        var path = settings.ResolveSavePath(expected.RelativePath);
        var data = File.ReadAllBytes(path);

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
    /// Cross-checks that the committed table actually enumerates every .sav file currently present
    /// under the configured directory's three save folders, plus the DAT — so the fixture cannot go
    /// silently stale the way the earlier "sampled" check did. Also pins the two headline facts from
    /// docs/investigations/dat-file-layout.md: exactly 52 files (51 saves + the DAT), and exactly 3
    /// saves carrying an army tombstone.
    /// </summary>
    [SkippableFact]
    public void Fixture_covers_every_file_currently_in_the_configured_corpus()
    {
        Skip.IfNot(LocalAssets.IsConfigured, LocalAssets.SkipReason);
        var settings = LocalAssets.Settings!;

        var saveDirs = new[] { "saves", "saves-processed", Path.Combine("saves-processed", "processed") };
        var actualPaths = saveDirs
            .SelectMany(dir => Directory.GetFiles(Path.Combine(settings.DirectoryPath, dir), "*.sav"))
            .Select(file => Path.GetRelativePath(settings.DirectoryPath, file).Replace('\\', '/'))
            .Append("Imperial Conquest 2.dat")
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        var fixturePaths = CorpusFixture.Entries
            .Select(e => e.RelativePath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(actualPaths, fixturePaths);
        Assert.Equal(52, CorpusFixture.Entries.Count);
        Assert.Equal(51, CorpusFixture.Entries.Count(e => e.Format == "Sav"));
        Assert.Equal(1, CorpusFixture.Entries.Count(e => e.Format == "Dat"));
        Assert.Equal(3, CorpusFixture.Entries.Count(e => e.SkippedArmies > 0));
        Assert.All(CorpusFixture.Entries, e => Assert.True(e.SkippedArmies <= 1));
    }
}
