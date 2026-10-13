using IC2.Data;
using IC2.Engine.Import;
using IC2.Engine.Model;
using IC2.Engine.Tests.Import;

namespace IC2.Engine.Tests.Battle.Tactical.GoldenMaster;

/// <summary>
/// Where the golden master's recorded battles live: the <c>battles/</c> folder of the private fixtures repository
/// (<c>IC2_FIXTURES_DIR</c>), never this repository. Every test sits behind <see cref="SkipReason"/>, so a machine
/// without the fixtures reports an explicit skip.
/// </summary>
internal static class BattleFixtures
{
    /// <summary>The <c>battles/</c> folder, or <see langword="null"/> when the fixtures are not configured.</summary>
    public static string? Root { get; } = LocalOriginalAssets.Settings is { } s
        ? Path.Combine(s.DirectoryPath, "battles")
        : null;

    /// <summary>Whether the battles are present.</summary>
    public static bool IsAvailable => Root is not null && Directory.Exists(Root);

    /// <summary>The reason to skip, or the empty string.</summary>
    public static string SkipReason =>
        !LocalOriginalAssets.IsConfigured ? LocalOriginalAssets.SkipReason
        : !IsAvailable ? $"the fixtures repository has no battles/ folder under '{LocalOriginalAssets.Settings?.DirectoryPath}'."
        : string.Empty;

    /// <summary>The full path of <paramref name="relative"/> under <c>battles/</c>.</summary>
    public static string Path_(string relative) => Path.Combine(Root!, relative.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Whether the file exists.</summary>
    public static bool Exists(string relative) => File.Exists(Path_(relative));

    /// <summary>Reads the file's bytes.</summary>
    public static byte[] Read(string relative) => File.ReadAllBytes(Path_(relative));

    /// <summary>The block of a block save.</summary>
    public static SaveBattleBlock Block(string relative) =>
        SaveTurnState.Parse(Read(relative)).BattleBlock
        ?? throw new InvalidDataException($"'{relative}' carries no battle block.");

    /// <summary>Imports a save through T21's importer.</summary>
    public static OriginalSaveImportResult Import(string relative)
    {
        var name = System.IO.Path.GetFileName(relative);
        return OriginalSaveImporter.Import(
            Read(relative), name, RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario,
            "golden-" + name, "Golden " + name);
    }
}
