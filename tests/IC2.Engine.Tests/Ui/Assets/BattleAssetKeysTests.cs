using IC2.Engine.Assets;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Fixtures;
using IC2.Slice.Assets;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui.Assets;

/// <summary>
/// T128: the tactical battle screen's 25 asset keys (<see cref="BattleAssetKeys"/>) exist in both packs'
/// manifests and resolve to files of the stated sizes, each unit icon carries the three marker colours
/// <see cref="BattleIconTint"/> replaces, and the tint maps them to the nation's three colours in the
/// original's order (purple to the first, white to the second, blue to the third).
/// </summary>
/// <remarks>
/// The authored pack's battle files come from a billed generation run the implementer never makes
/// (the main session runs it and commits the files), so its half is its own test: until that run lands
/// the authored-pack facts below fail with a missing-file message, and only they.
/// </remarks>
public sealed class BattleAssetKeysTests
{
    private static readonly string PlaceholderPackDirectory =
        Path.Combine(FixturePaths.RepositoryRoot, "assets", "packs", "placeholder");

    private static readonly string AuthoredPackDirectory =
        Path.Combine(FixturePaths.RepositoryRoot, "assets", "packs", "authored");

    private static readonly GameDataRepository Repository = GameDataRepository.Load(ModelTestPaths.DataRoot);

    // ---- the key set -------------------------------------------------------------------------------------

    [Fact]
    public void AllKeys_AreThe25InTheInventory()
    {
        var keys = BattleAssetKeys.AllKeys.ToList();

        Assert.Equal(25, keys.Count);
        Assert.Equal(25, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(15, keys.Count(BattleAssetKeys.IsUnitIcon));
        Assert.Equal(8, keys.Count(BattleAssetKeys.IsToolbarGlyph));
        Assert.Contains("battle.ground", keys);
        Assert.Contains("battle.cursor", keys);
        Assert.Contains("battle.unit.light_infantry.small", keys);
        Assert.Contains("battle.unit.heavy_cavalry.large", keys);
        Assert.Contains("battle.toolbar.computer_general", keys);

        // None of them is an engine key: they would otherwise be listed twice in the drift test.
        Assert.Empty(keys.Intersect(AssetKeys.AllKeys));
    }

    [Fact]
    public void ToolbarButtons_AreInTheOriginalsOrder_ComputerGeneralSeventh()
    {
        Assert.Equal(
            new[]
            {
                "unit_moves", "friendly_units", "enemy_units", "cancel_selection",
                "end_turn", "change_pauses", "computer_general", "surrender",
            },
            BattleAssetKeys.ToolbarButtons);
        Assert.Equal("computer_general", BattleAssetKeys.ToolbarButtons[6]);
    }

    /// <summary>
    /// <c>min(2, troops div (standardBattalionSize div 3))</c>: the icon changes size at exactly
    /// <c>std div 3</c> and <c>2 x (std div 3)</c> troops (the tactical-battle sweep, B8: light infantry
    /// 5,000 / 10,000 with a 15,000 battalion, heavy infantry 2,000 / 4,000 with 6,000).
    /// </summary>
    [Theory]
    [InlineData(0, 15000, 0)]
    [InlineData(4999, 15000, 0)]
    [InlineData(5000, 15000, 1)]
    [InlineData(9999, 15000, 1)]
    [InlineData(10000, 15000, 2)]
    [InlineData(45000, 15000, 2)]
    [InlineData(1999, 6000, 0)]
    [InlineData(2000, 6000, 1)]
    [InlineData(4000, 6000, 2)]
    public void SizeIndex_ChangesAtOneThirdAndTwoThirdsOfTheStandardBattalion(int troops, int standard, int expected)
    {
        Assert.Equal(expected, BattleAssetKeys.SizeIndex(troops, standard));
    }

    [Fact]
    public void UnitIcon_NamesTheKeyBySize()
    {
        Assert.Equal("battle.unit.archers.small", BattleAssetKeys.UnitIcon("archers", 0));
        Assert.Equal("battle.unit.archers.medium", BattleAssetKeys.UnitIcon("archers", 1166, 3498));
        Assert.Equal("battle.unit.archers.large", BattleAssetKeys.UnitIcon("archers", 2332, 3498));
        Assert.Equal("battle.toolbar.end_turn", BattleAssetKeys.ToolbarGlyph("end_turn"));
    }

    // ---- the packs ---------------------------------------------------------------------------------------

    [Fact]
    public void PlaceholderPack_ResolvesAll25Keys_ToFilesOfTheStatedSizes()
    {
        AssertPackHoldsTheBattleKeys(PlaceholderPackDirectory);
    }

    [Fact]
    public void AuthoredPack_ResolvesAll25Keys_ToFilesOfTheStatedSizes()
    {
        AssertPackHoldsTheBattleKeys(AuthoredPackDirectory);
    }

    [Fact]
    public void PlaceholderPack_UnitIcons_ContainTheThreeMarkerColours()
    {
        AssertEveryUnitIconCarriesTheMarkers(PlaceholderPackDirectory);
    }

    [Fact]
    public void AuthoredPack_UnitIcons_ContainTheThreeMarkerColours()
    {
        AssertEveryUnitIconCarriesTheMarkers(AuthoredPackDirectory);
    }

    [Fact]
    public void PlaceholderPack_UnitIcons_AreAll15Distinct()
    {
        var pack = AssetLoader.LoadManifest(Path.Combine(PlaceholderPackDirectory, "manifest.json"));
        var distinct = BattleAssetKeys.AllKeys
            .Where(BattleAssetKeys.IsUnitIcon)
            .Select(key => Convert.ToHexString(File.ReadAllBytes(Path.Combine(PlaceholderPackDirectory, pack.ResolveAsset(key)))))
            .Distinct(StringComparer.Ordinal)
            .Count();

        Assert.Equal(15, distinct);
    }

    // ---- the recolour ------------------------------------------------------------------------------------

    [Fact]
    public void ForNation_UsesBattleColorsHex_InTheOriginalsRoleOrder()
    {
        var world = Repository.Resolve("classical-mediterranean").World;

        var rome = BattleIconTint.ForNation(world, "rome");
        Assert.Equal(new BattleIconColors((0x80, 0x00, 0x80), (0xFF, 0xFF, 0xFF), (0x00, 0x00, 0xFF)), rome);

        // Numidia: a lime background, a black fill and a grey outline (not the teal of its city markers).
        var numidia = BattleIconTint.ForNation(world, "numidia");
        Assert.Equal(new BattleIconColors((0x00, 0xFF, 0x00), (0x00, 0x00, 0x00), (0x80, 0x80, 0x80)), numidia);
    }

    [Fact]
    public void ForNation_WithoutTheField_FallsBackToColorWhiteAndGlyphColor()
    {
        var toy = Repository.Resolve("toy-3city").World;
        var custom = new NationDefinition("custom", "Custom", "#102030", "Leader", null, 0, 0, 0, 0, 0, 0, 0,
            GlyphColorHex: "#A0B0C0");
        var noGlyph = new NationDefinition("plain", "Plain", "#FFFFFF", "Leader", null, 0, 0, 0, 0, 0, 0, 0);
        var world = toy with { Nations = ValueList.Of(custom, noGlyph) };

        Assert.Equal(
            new BattleIconColors((0x10, 0x20, 0x30), (0xFF, 0xFF, 0xFF), (0xA0, 0xB0, 0xC0)),
            BattleIconTint.ForNation(world, "custom"));

        // No glyph colour either: the luminance foreground (black on a white background).
        Assert.Equal(
            new BattleIconColors((0xFF, 0xFF, 0xFF), (0xFF, 0xFF, 0xFF), (0x00, 0x00, 0x00)),
            BattleIconTint.ForNation(world, "plain"));

        Assert.Null(BattleIconTint.ForNation(world, "nobody"));
        Assert.Null(BattleIconTint.ForNation(null, "custom"));
    }

    [Fact]
    public void Map_ReplacesPurpleWithFirst_WhiteWithSecond_BlueWithThird_AndKeepsTheRest()
    {
        var colors = new BattleIconColors((1, 2, 3), (4, 5, 6), (7, 8, 9));

        Assert.Equal((1, 2, 3), BattleIconTint.Map(colors, 0x80, 0x00, 0x80));
        Assert.Equal((4, 5, 6), BattleIconTint.Map(colors, 0xFF, 0xFF, 0xFF));
        Assert.Equal((7, 8, 9), BattleIconTint.Map(colors, 0x00, 0x00, 0xFF));
        Assert.Equal((0, 0, 0), BattleIconTint.Map(colors, 0, 0, 0));
        Assert.Equal((0, 255, 0), BattleIconTint.Map(colors, 0, 255, 0));
        Assert.Equal((0x80, 0x00, 0x81), BattleIconTint.Map(colors, 0x80, 0x00, 0x81));
    }

    [Fact]
    public void Recolour_RewritesOnlyTheMarkerPixels_AndLeavesAlpha()
    {
        var numidia = BattleIconTint.ForNation(Repository.Resolve("classical-mediterranean").World, "numidia")!.Value;
        byte[] pixels =
        {
            0x80, 0x00, 0x80, 255, // purple -> lime
            0xFF, 0xFF, 0xFF, 128, // white -> black, alpha kept
            0x00, 0x00, 0xFF, 0,   // blue -> grey, alpha kept
            0x12, 0x34, 0x56, 255, // anything else is kept
        };

        BattleIconTint.Recolour(numidia, pixels, 4);

        Assert.Equal(
            new byte[]
            {
                0x00, 0xFF, 0x00, 255,
                0x00, 0x00, 0x00, 128,
                0x80, 0x80, 0x80, 0,
                0x12, 0x34, 0x56, 255,
            },
            pixels);
    }

    // ---- helpers -----------------------------------------------------------------------------------------

    private static void AssertPackHoldsTheBattleKeys(string packDirectory)
    {
        var pack = AssetLoader.LoadManifest(Path.Combine(packDirectory, "manifest.json"));

        foreach (var key in BattleAssetKeys.AllKeys)
        {
            Assert.True(pack.Assets.ContainsKey(key), $"{key}: not in {packDirectory}'s manifest");

            var path = Path.Combine(packDirectory, pack.ResolveAsset(key));
            Assert.True(File.Exists(path), $"{key}: file not found at {path}");

            var bmp = ReadBmp(path, key);
            var size = BattleAssetKeys.ExpectedSize(key);
            Assert.True(bmp.Width == size && bmp.Height == size,
                $"{key}: {bmp.Width} x {bmp.Height}, expected {size} x {size}");
            Assert.True(bmp.BitCount == BattleAssetKeys.ExpectedBitCount(key),
                $"{key}: {bmp.BitCount}-bit, expected {BattleAssetKeys.ExpectedBitCount(key)}-bit");
        }
    }

    private static void AssertEveryUnitIconCarriesTheMarkers(string packDirectory)
    {
        var pack = AssetLoader.LoadManifest(Path.Combine(packDirectory, "manifest.json"));
        var unitKeys = BattleAssetKeys.AllKeys.Where(BattleAssetKeys.IsUnitIcon).ToList();
        Assert.Equal(15, unitKeys.Count);

        foreach (var key in unitKeys)
        {
            var path = Path.Combine(packDirectory, pack.ResolveAsset(key));
            Assert.True(File.Exists(path), $"{key}: file not found at {path}");

            var colours = ReadBmp(path, key).Colours;
            Assert.True(colours.Contains(BattleIconTint.PurpleMarker), $"{key}: no purple 0x800080 pixel");
            Assert.True(colours.Contains(BattleIconTint.WhiteMarker), $"{key}: no white pixel");
            Assert.True(colours.Contains(BattleIconTint.BlueMarker), $"{key}: no blue 0x0000FF pixel");
        }
    }

    private sealed record Bmp(int Width, int Height, int BitCount, HashSet<(byte R, byte G, byte B)> Colours);

    private static Bmp ReadBmp(string path, string key)
    {
        var bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length >= 54 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M', $"{key}: not a BMP");

        var width = BitConverter.ToInt32(bytes, 18);
        var height = BitConverter.ToInt32(bytes, 22);
        var bitCount = BitConverter.ToUInt16(bytes, 28);
        Assert.Contains(bitCount, new[] { 24, 32 });

        var bytesPerPixel = bitCount / 8;
        var rowSize = ((width * bytesPerPixel) + 3) / 4 * 4;
        var colours = new HashSet<(byte R, byte G, byte B)>();
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = 54 + (y * rowSize) + (x * bytesPerPixel);
                if (bitCount == 32 && bytes[i + 3] == 0)
                {
                    continue;
                }

                colours.Add((bytes[i + 2], bytes[i + 1], bytes[i]));
            }
        }

        return new Bmp(width, height, bitCount, colours);
    }
}
