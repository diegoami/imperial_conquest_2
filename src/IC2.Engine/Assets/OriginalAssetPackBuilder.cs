using System.Text.Json;

namespace IC2.Engine.Assets;

/// <summary>One category line of a cache build: how many of its keys the installation could supply.</summary>
public sealed record OriginalCategoryResult(string Category, int Found, int Total, string? Note);

/// <summary>What a cache build did, for the Settings status line. Holds counts and notes, never original bytes.</summary>
public sealed record OriginalCacheReport(string SourceDirectory, IReadOnlyList<OriginalCategoryResult> Categories)
{
    public int KeysWritten => Categories.Sum(c => c.Found);
    public int KeysWanted => Categories.Sum(c => c.Total);
    public bool Usable => KeysWritten > 0;
}

/// <summary>
/// T292b: builds the local <c>original</c> asset pack from the user's own installation into a per-user cache
/// directory (never the repository, a release or Pages). Keys it cannot produce are simply absent from the
/// manifest, so the loader's per-key fallback to <c>authored</c> (T292c) covers them.
/// </summary>
/// <remarks>
/// This first slice covers the ten sounds, which are plain PCM WAV files in the installation's <c>WAVS</c> folder
/// and whose event mapping is recorded in docs/asset-specification.md section 4.6. Images live inside the
/// executable's form streams and wait for their key mapping (docs/design/original-assets-measurement.md).
/// </remarks>
public static class OriginalAssetPackBuilder
{
    public const string PackId = "original";
    private const long MaxWavBytes = 8L * 1024 * 1024;

    /// <summary>Sound file number (SOUNDn.WAV) to the clone's sound key; the original's own order.</summary>
    public static readonly IReadOnlyList<(int Number, string Key)> Sounds =
    [
        (1, AssetKeys.SfxUnitMove), (2, AssetKeys.SfxFleetMove), (3, AssetKeys.SfxBattleArrows),
        (4, AssetKeys.SfxBattleJavelin), (5, AssetKeys.SfxBattleMelee), (6, AssetKeys.SfxSiegeFailed),
        (7, AssetKeys.SfxCityCaptured), (8, AssetKeys.SfxFleetSunk), (9, AssetKeys.SfxBattle),
        (10, AssetKeys.SfxNationConquered),
    ];

    /// <summary>
    /// Reads <paramref name="installDirectory"/> and writes a complete pack into <paramref name="outputDirectory"/>.
    /// The pack is assembled beside the target and swapped in only when finished, so an interrupted build keeps
    /// the previous cache. Never throws for a missing or partial installation; it reports what was found.
    /// </summary>
    public static OriginalCacheReport Build(string installDirectory, string outputDirectory)
    {
        var results = new List<OriginalCategoryResult>();
        var assets = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var staging = outputDirectory.TrimEnd('\\', '/') + ".building";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        Directory.CreateDirectory(Path.Combine(staging, "sfx"));

        try
        {
            results.Add(BuildSounds(installDirectory, staging, assets));
            results.Add(BuildTerrainTiles(installDirectory, staging, assets));
            var manifest = new
            {
                schemaVersion = 1,
                name = "Original assets (local cache)",
                description = "Built on this machine from the user's own installation. Never committed, released or uploaded; keys it lacks fall back to the authored pack.",
                assets,
            };
            File.WriteAllText(Path.Combine(staging, "manifest.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n");

            if (Directory.Exists(outputDirectory)) Directory.Delete(outputDirectory, recursive: true);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputDirectory))!);
            Directory.Move(staging, outputDirectory);
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            throw;
        }

        return new OriginalCacheReport(installDirectory, results);
    }

    private static OriginalCategoryResult BuildSounds(string install, string staging, SortedDictionary<string, string> assets)
    {
        var folder = FindDirectory(install, "WAVS");
        if (folder is null)
            return new OriginalCategoryResult("sounds", 0, Sounds.Count, "no WAVS folder in the installation");

        var found = 0;
        var rejected = new List<string>();
        foreach (var (number, key) in Sounds)
        {
            var source = Directory.EnumerateFiles(folder)
                .FirstOrDefault(f => string.Equals(Path.GetFileName(f), $"SOUND{number}.WAV", StringComparison.OrdinalIgnoreCase));
            if (source is null) continue;
            var length = new FileInfo(source).Length;
            if (length > MaxWavBytes) { rejected.Add($"SOUND{number}.WAV is too large"); continue; }
            var bytes = File.ReadAllBytes(source);
            if (!IsPcmWave(bytes)) { rejected.Add($"SOUND{number}.WAV is not 16-bit PCM"); continue; }
            var relative = "sfx/" + key["sfx.".Length..] + ".wav";
            File.WriteAllBytes(Path.Combine(staging, relative.Replace('/', Path.DirectorySeparatorChar)), bytes);
            assets[key] = relative;
            found++;
        }

        return new OriginalCategoryResult("sounds", found, Sounds.Count, rejected.Count == 0 ? null : string.Join("; ", rejected));
    }

    /// <summary>Executable's TUnitMap.TerrainList cells 0 to 5 to the six tile keys (research extraction spec, crosswalk).</summary>
    public static readonly IReadOnlyList<(int Cell, string Key, string File)> TerrainTiles =
    [
        (0, AssetKeys.TerrainSeaCalmTile, "terrain/sea_calm.png"), (1, AssetKeys.TerrainSeaRoughTile, "terrain/sea_rough.png"),
        (2, AssetKeys.TerrainPlainTile, "terrain/plain.png"), (3, AssetKeys.TerrainDesertTile, "terrain/desert.png"),
        (4, AssetKeys.TerrainForestTile, "terrain/forest.png"), (5, AssetKeys.TerrainMountainTile, "terrain/mountain.png"),
    ];

    /// <summary>Every key this builder can supply today (sounds and terrain tiles); the status counts against these.</summary>
    public static IReadOnlyList<string> SupportedKeys { get; } =
        [.. Sounds.Select(s => s.Key), .. TerrainTiles.Select(t => t.Key)];

    private const long MaxExecutableBytes = 64L * 1024 * 1024;

    /// <summary>
    /// The six terrain tiles, all or none: the map draws them from one texture array and needs equal sizes, so
    /// a partial set would be worse than the authored one. Rivers (six shapes behind one key), markers and
    /// battle pieces need further mapping and are not written.
    /// </summary>
    private static OriginalCategoryResult BuildTerrainTiles(string install, string staging, SortedDictionary<string, string> assets)
    {
        const string category = "terrain tiles";
        var total = TerrainTiles.Count;
        var exe = Directory.Exists(install)
            ? Directory.EnumerateFiles(install).FirstOrDefault(f => string.Equals(Path.GetFileName(f), "Imperial Conquest 2.exe", StringComparison.OrdinalIgnoreCase))
            : null;
        if (exe is null) return new OriginalCategoryResult(category, 0, total, "no 'Imperial Conquest 2.exe' in the installation");
        if (new FileInfo(exe).Length > MaxExecutableBytes) return new OriginalCategoryResult(category, 0, total, "the executable is too large to read");

        try
        {
            var list = ReadUnitMapImageList(File.ReadAllBytes(exe), "TerrainList");
            if (list is null || list.Count < TerrainTiles.Count)
                return new OriginalCategoryResult(category, 0, total, "this build of the game has no recognised terrain image list");

            var files = new List<(string Key, string File, byte[] Png)>();
            foreach (var (cell, key, file) in TerrainTiles)
                files.Add((key, file, PngEncoder.EncodeRgb(list.CellWidth, list.CellHeight, list.CellRgb(cell))));
            foreach (var (key, file, png) in files)
            {
                var path = Path.Combine(staging, file.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, png);
                assets[key] = file;
            }
            return new OriginalCategoryResult(category, total, total, null);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new OriginalCategoryResult(category, 0, total, "this build of the game could not be read (" + ex.GetType().Name + ")");
        }
    }

    private static OriginalImageList? ReadUnitMapImageList(byte[] exe, string componentName)
    {
        foreach (var (_, data) in PeResources.ReadRcData(exe))
        {
            if (data.Length < 4 || data[0] != 'T' || data[1] != 'P' || data[2] != 'F' || data[3] != '0') continue;
            var form = DelphiComponent.Parse(data);
            if (form.ClassName != "TUnitMap") continue;
            var component = form.Find("TImageList", componentName);
            if (component is null || !component.BinaryProperties.TryGetValue("Bitmap", out var bitmap)) return null;
            var width = (int)component.IntegerProperties.GetValueOrDefault("Width", 0);
            var height = (int)component.IntegerProperties.GetValueOrDefault("Height", 0);
            return OriginalImageList.Parse(bitmap, width, height);
        }
        return null;
    }

    private static string? FindDirectory(string parent, string name)
    {
        if (!Directory.Exists(parent)) return null;
        return Directory.EnumerateDirectories(parent)
            .FirstOrDefault(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>RIFF/WAVE with a PCM 'fmt ' chunk (format 1), 1 or 2 channels, 8 or 16 bit, and a 'data' chunk.</summary>
    public static bool IsPcmWave(ReadOnlySpan<byte> b)
    {
        if (b.Length < 44 || !b[..4].SequenceEqual("RIFF"u8) || !b[8..12].SequenceEqual("WAVE"u8)) return false;
        var position = 12;
        var pcm = false;
        var data = false;
        while (position + 8 <= b.Length)
        {
            var id = b.Slice(position, 4);
            var size = BitConverter.ToUInt32(b.Slice(position + 4, 4));
            var body = position + 8;
            if (id.SequenceEqual("fmt "u8))
            {
                if (size < 16 || body + 16 > b.Length) return false;
                var format = BitConverter.ToUInt16(b.Slice(body, 2));
                var channels = BitConverter.ToUInt16(b.Slice(body + 2, 2));
                var bits = BitConverter.ToUInt16(b.Slice(body + 14, 2));
                pcm = format == 1 && channels is 1 or 2 && bits is 8 or 16;
            }
            else if (id.SequenceEqual("data"u8))
            {
                data = size > 0;
            }
            var next = (long)body + size + (size & 1);
            if (next > b.Length) break;
            position = (int)next;
        }
        return pcm && data;
    }
}
