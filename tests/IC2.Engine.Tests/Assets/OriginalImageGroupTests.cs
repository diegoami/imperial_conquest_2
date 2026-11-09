using IC2.Engine.Assets;
using Xunit;

namespace IC2.Engine.Tests.Assets;

/// <summary>Army and fleet markers and battle pieces: each group is all or none, and each key comes from the cell the research crosswalk names.</summary>
public sealed class OriginalImageGroupTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ic2-groups-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Install(int battleCells = 17)
    {
        var unitMap = SyntheticExecutable.Form("TUnitMap",
            ("TerrainList", SyntheticExecutable.ImageListProperty(), 32),
            ("ArmiesList", SyntheticExecutable.ImageListProperty(128, 64, 32, 3), 32),
            ("FleetsList", SyntheticExecutable.ImageListProperty(128, 64, 32, 3), 32),
            ("Cities1List", SyntheticExecutable.ImageListProperty(128, 160, 32, 16), 32),
            ("Cities2List", SyntheticExecutable.ImageListProperty(128, 160, 32, 16), 32),
            ("Cities3List", SyntheticExecutable.ImageListProperty(128, 160, 32, 16), 32),
            ("Cities4List", SyntheticExecutable.ImageListProperty(128, 160, 32, 16), 32),
            ("CapitalsList", SyntheticExecutable.ImageListProperty(128, 160, 32, 16), 32));
        var battleMap = SyntheticExecutable.Form("TBattleMap", ("BatMapList", SyntheticExecutable.ImageListProperty(128, 160, 32, battleCells), 32));
        var install = Path.Combine(_root, "install");
        SyntheticExecutable.WriteInstall(install, SyntheticExecutable.Pe([("TUNITMAP", unitMap), ("TBATTLEMAP", battleMap)]));
        return install;
    }

    private static (byte R, byte G, byte B) FirstPixel(string pngPath)
    {
        // The encoder writes filter byte 0 then raw RGB, so the first pixel is the first three bytes after inflating.
        var png = File.ReadAllBytes(pngPath);
        var position = 8;
        using var idat = new MemoryStream();
        while (position < png.Length)
        {
            var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position));
            if (System.Text.Encoding.ASCII.GetString(png, position + 4, 4) == "IDAT") idat.Write(png, position + 8, length);
            position += 12 + length;
        }
        idat.Position = 0;
        using var raw = new MemoryStream();
        using (var z = new System.IO.Compression.ZLibStream(idat, System.IO.Compression.CompressionMode.Decompress)) z.CopyTo(raw);
        var b = raw.ToArray();
        return (b[1], b[2], b[3]);
    }

    private static (byte, byte, byte) Palette(int index) => ((byte)index, (byte)(index * 2), (byte)(index * 3));

    [Fact]
    public void All_three_image_groups_are_written_with_their_keys()
    {
        var output = Path.Combine(_root, "cache", "original");
        var report = OriginalAssetPackBuilder.Build(Install(), output);

        Assert.Equal((6, 6), Found(report, "terrain tiles"));
        Assert.Equal((6, 6), Found(report, "river tiles"));
        Assert.Equal((80, 80), Found(report, "town and capital markers"));
        Assert.Equal((6, 6), Found(report, "army and fleet markers"));
        Assert.Equal((16, 16), Found(report, "battle pieces"));
        var pack = AssetLoader.LoadManifest(Path.Combine(output, "manifest.json"));
        foreach (var key in new[] { "army.tier1.icon", "army.tier3.icon", "fleet.tier2.icon", "battle.ground", "battle.unit.light_infantry.small", "battle.unit.heavy_cavalry.large" })
            Assert.True(pack.TryResolveAsset(key, out var path) && File.Exists(Path.Combine(output, path)), key);
    }

    [Fact]
    public void Each_key_is_cut_from_the_cell_the_crosswalk_names()
    {
        var output = Path.Combine(_root, "cache", "original");
        OriginalAssetPackBuilder.Build(Install(), output);
        var pack = AssetLoader.LoadManifest(Path.Combine(output, "manifest.json"));
        (byte, byte, byte) Pixel(string key) { pack.TryResolveAsset(key, out var path); return FirstPixel(Path.Combine(output, path)); }

        // The synthetic sheets paint cell k with palette index k + 1.
        Assert.Equal(Palette(7), Pixel(IC2.Engine.Assets.AssetKeys.OriginalRiverTiles[0]));  // TerrainList cell 6 (east-west)
        Assert.Equal(Palette(12), Pixel(IC2.Engine.Assets.AssetKeys.OriginalRiverTiles[5])); // cell 11 (north-west)
        Assert.Equal(Palette(1), Pixel(IC2.Engine.Assets.AssetKeys.OriginalTownIcon(1, 0)));   // Cities1List, owner 0
        Assert.Equal(Palette(6), Pixel(IC2.Engine.Assets.AssetKeys.OriginalTownIcon(3, 5)));   // Cities3List, owner 5 (cell 5)
        Assert.Equal(Palette(16), Pixel(IC2.Engine.Assets.AssetKeys.OriginalTownIcon(0, 15))); // CapitalsList, owner 15
        Assert.Equal(Palette(2), Pixel("army.tier2.icon"));                 // ArmiesList cell 1
        Assert.Equal(Palette(3), Pixel("fleet.tier3.icon"));                // FleetsList cell 2
        Assert.Equal(Palette(1), Pixel("battle.unit.light_infantry.small"));   // 3 * 0 + 0
        Assert.Equal(Palette(8), Pixel("battle.unit.archers.medium"));          // 3 * 2 + 1 = 7
        Assert.Equal(Palette(15), Pixel("battle.unit.heavy_cavalry.large"));    // 3 * 4 + 2 = 14
        Assert.Equal(Palette(16), Pixel("battle.ground"));                      // cell 15
    }

    [Fact]
    public void A_group_with_a_short_list_is_dropped_whole_and_the_others_still_build()
    {
        var output = Path.Combine(_root, "cache", "original");
        var report = OriginalAssetPackBuilder.Build(Install(battleCells: 10), output);

        Assert.Equal((0, 16), Found(report, "battle pieces"));
        Assert.Equal((6, 6), Found(report, "terrain tiles"));
        Assert.Equal((6, 6), Found(report, "army and fleet markers"));
        var pack = AssetLoader.LoadManifest(Path.Combine(output, "manifest.json"));
        Assert.False(pack.TryResolveAsset("battle.ground", out _));
        Assert.False(pack.TryResolveAsset("battle.unit.light_infantry.small", out _));
    }

    [Fact]
    public void The_status_counts_every_supported_key()
    {
        Assert.Equal(10 + 6 + 6 + 6 + 80 + 16 + 44, OriginalAssetPackBuilder.SupportedKeys.Count);
        Assert.Equal(OriginalAssetPackBuilder.SupportedKeys.Count, OriginalAssetPackBuilder.SupportedKeys.Distinct().Count());
    }

    private static (int, int) Found(OriginalCacheReport report, string category)
    {
        var c = report.Categories.Single(x => x.Category == category);
        return (c.Found, c.Total);
    }
}
