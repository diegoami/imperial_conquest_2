using System.Text.Json;
using IC2.Data;

namespace IC2.Inspect;

/// <summary>
/// One row of the corpus expected-outcome table: what a clean parse of one file in the configured
/// original-assets corpus actually returns, across all seven <c>IC2.Data</c> parsers. Emitted by
/// <c>IC2.Inspect --corpus-outcomes</c> and consumed by <c>IC2.Data.Tests</c>' <c>CorpusSweepTests</c>
/// as the committed regression fixture
/// (<c>tests/IC2.Data.Tests/CorpusFixtures/expected-corpus-outcomes.json</c>).
/// <see cref="MercenaryRecords"/> and <see cref="TurnWeek"/> are null exactly when that table has no
/// DAT-shaped equivalent at all (<see cref="DatDataNotPresentException"/> is the expected outcome for
/// that row), never a fabricated zero. Keyed by <see cref="FileName"/> alone (T34, bug #57) — not by
/// which of the three save folders currently holds the file — so moving a save between folders
/// changes no test outcome.
/// </summary>
public sealed record CorpusOutcome(
    string FileName,
    string Format,
    int Bytes,
    int Cities,
    int Armies,
    int SkippedArmies,
    int Fleets,
    int SkippedFleets,
    int Nations,
    int NationCitiesSum,
    int RecruitmentEntries,
    int? MercenaryRecords,
    int? TurnWeek)
{
    public override string ToString() => FileName;
}

/// <summary>Generates <see cref="CorpusOutcome"/> rows by running every <c>IC2.Data</c> parser over
/// every file in the configured corpus, and (de)serialises the resulting table in the same shape the
/// committed fixture uses.</summary>
public static class CorpusOutcomeGenerator
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Runs every parser over one already-located file and records what a clean parse
    /// returns.</summary>
    public static CorpusOutcome Generate(string path, string fileName)
    {
        var data = File.ReadAllBytes(path);

        var format = SaveFormat.Detect(data);
        var world = WorldPrefix.Parse(data);
        var armyTable = SaveArmyTable.Parse(data);
        var fleetTable = SaveFleetTable.Parse(data);
        var nationTable = SaveNationTable.Parse(data);
        var recruitment = SaveRecruitmentTable.Parse(data);

        int? mercenaryRecords;
        try
        {
            mercenaryRecords = SaveMercenaryTable.Parse(data).Records.Count;
        }
        catch (DatDataNotPresentException)
        {
            mercenaryRecords = null;
        }

        int? turnWeek;
        try
        {
            turnWeek = SaveTurnState.Parse(data).Week;
        }
        catch (DatDataNotPresentException)
        {
            turnWeek = null;
        }

        return new CorpusOutcome(
            fileName,
            format.ToString(),
            data.Length,
            world.Cities.Count,
            armyTable.Armies.Count,
            armyTable.SkippedRecords.Count,
            fleetTable.Fleets.Count,
            fleetTable.SkippedRecords.Count,
            nationTable.Nations.Count,
            nationTable.Nations.Sum(n => (int)n.CityCount),
            recruitment.Entries.Count,
            mercenaryRecords,
            turnWeek);
    }

    /// <summary>Scans every <c>.sav</c> file under the three save folders, plus the DAT, and generates
    /// one <see cref="CorpusOutcome"/> per file found — in the same folder-scan order regardless of
    /// how many files any one folder holds, then sorted by file name for a stable, diffable table.
    /// Each on-disk file is processed once, wherever it is; a name that happens to occur in more than
    /// one folder still produces one row per occurrence (the fixture's own coverage test is what
    /// flags that as an error, not this generator — regeneration records what the parsers produce).
    /// </summary>
    public static IReadOnlyList<CorpusOutcome> GenerateAll(AssetSettings settings)
    {
        var outcomes = new List<CorpusOutcome>();
        foreach (var dir in CorpusFileLocator.SaveDirs)
        {
            var full = Path.Combine(settings.DirectoryPath, dir);
            if (!Directory.Exists(full)) continue;
            foreach (var file in Directory.GetFiles(full, "*.sav").OrderBy(f => f, StringComparer.Ordinal))
                outcomes.Add(Generate(file, Path.GetFileName(file)));
        }
        if (File.Exists(settings.DatPath))
            outcomes.Add(Generate(settings.DatPath, CorpusFileLocator.DatFileName));

        return outcomes.OrderBy(o => o.FileName, StringComparer.Ordinal).ToList();
    }

    public static string ToJson(IReadOnlyList<CorpusOutcome> outcomes) =>
        JsonSerializer.Serialize(outcomes, WriteOptions);

    public static IReadOnlyList<CorpusOutcome> FromJson(string json)
    {
        var entries = JsonSerializer.Deserialize<List<CorpusOutcome>>(json, ReadOptions);
        if (entries is null || entries.Count == 0)
            throw new InvalidOperationException("Corpus outcomes JSON did not deserialize to a nonempty table.");
        return entries;
    }
}
