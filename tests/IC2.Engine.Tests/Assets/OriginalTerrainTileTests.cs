using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using IC2.Engine.Assets;
using Xunit;

namespace IC2.Engine.Tests.Assets;

/// <summary>T292b images slice: PE resources, Delphi forms, image lists and the six terrain tiles, from synthetic files.</summary>
public sealed class OriginalTerrainTileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ic2-tiles-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string Install(byte[]? bitmap = null, string cls = "TUnitMap", string list = "TerrainList")
    {
        var install = Path.Combine(_root, "install");
        SyntheticExecutable.WriteInstall(install, SyntheticExecutable.Pe("TUNITMAP", SyntheticExecutable.Form(cls, list, bitmap ?? SyntheticExecutable.ImageListProperty())));
        return install;
    }

    private static OriginalCategoryResult Tiles(OriginalCacheReport report) => report.Categories.Single(c => c.Category == "terrain tiles");

    [Fact]
    public void The_resource_reader_finds_a_named_RCDATA_stream()
    {
        var form = SyntheticExecutable.Form("TUnitMap", "TerrainList", SyntheticExecutable.ImageListProperty());
        var single = Assert.Single(PeResources.ReadRcData(SyntheticExecutable.Pe("TUNITMAP", form)));
        Assert.Equal("TUNITMAP", single.Name);
        Assert.Equal(form, single.Data);
    }

    [Fact]
    public void The_form_parser_exposes_components_and_binary_properties()
    {
        var form = DelphiComponent.Parse(SyntheticExecutable.Form("TUnitMap", "TerrainList", SyntheticExecutable.ImageListProperty()));
        var list = form.Find("TImageList", "TerrainList");
        Assert.NotNull(list);
        Assert.Equal(32, list!.IntegerProperties["Width"]);
        Assert.True(list.BinaryProperties["Bitmap"].Length > 1000);
    }

    [Fact]
    public void Each_cell_is_cut_from_the_sheet_in_row_major_order_with_palette_colours()
    {
        var list = OriginalImageList.Parse(SyntheticExecutable.ImageListProperty(), 32, 32);
        Assert.Equal(13, list.Count);
        foreach (var cell in new[] { 0, 3, 4, 5, 12 })
        {
            var rgb = list.CellRgb(cell);
            var index = cell + 1; // the synthetic sheet paints cell k with palette index k + 1; palette entry i is (i, 2i, 3i) as R, G, B
            for (var i = 0; i < 32 * 32; i++)
                Assert.Equal((byte[])[(byte)index, (byte)(index * 2), (byte)(index * 3)], rgb.AsSpan(i * 3, 3).ToArray());
        }
    }

    [Fact]
    public void A_full_build_writes_six_PNG_tiles_the_normal_loader_resolves()
    {
        var output = Path.Combine(_root, "cache", "original");
        var tiles = Tiles(OriginalAssetPackBuilder.Build(Install(), output));

        Assert.Equal((6, 6), (tiles.Found, tiles.Total));
        var pack = AssetLoader.LoadManifest(Path.Combine(output, "manifest.json"));
        foreach (var (_, key, file) in OriginalAssetPackBuilder.TerrainTiles)
        {
            Assert.True(pack.TryResolveAsset(key, out var path), key);
            Assert.Equal(file, path);
            var png = File.ReadAllBytes(Path.Combine(output, path));
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
            Assert.Equal((32, 32), (BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20))));
        }
    }

    [Fact]
    public void The_PNG_round_trips_to_the_same_pixels()
    {
        var rgb = Enumerable.Range(0, 4 * 4 * 3).Select(i => (byte)(i * 5)).ToArray();
        var png = PngEncoder.EncodeRgb(4, 4, rgb);
        var position = 8;
        using var idat = new MemoryStream();
        while (position < png.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(position));
            if (Encoding.ASCII.GetString(png, position + 4, 4) == "IDAT") idat.Write(png, position + 8, length);
            position += 12 + length;
        }
        idat.Position = 0;
        using var raw = new MemoryStream();
        using (var z = new ZLibStream(idat, CompressionMode.Decompress)) z.CopyTo(raw);
        var bytes = raw.ToArray();
        Assert.Equal(4 * (1 + 12), bytes.Length);
        for (var y = 0; y < 4; y++)
        {
            Assert.Equal(0, bytes[y * 13]);
            Assert.Equal(rgb.AsSpan(y * 12, 12).ToArray(), bytes.AsSpan(y * 13 + 1, 12).ToArray());
        }
    }

    [Theory]
    [InlineData("TOtherForm", "TerrainList")]
    [InlineData("TUnitMap", "SomethingElse")]
    public void An_unrecognised_form_is_reported_and_writes_no_tiles(string cls, string list)
    {
        var output = Path.Combine(_root, "cache", "original");
        var tiles = Tiles(OriginalAssetPackBuilder.Build(Install(cls: cls, list: list), output));
        Assert.Equal(0, tiles.Found);
        Assert.Contains("recognised", tiles.Note);
        Assert.False(Directory.Exists(Path.Combine(output, "terrain")));
    }

    [Fact]
    public void A_truncated_or_unsupported_image_list_is_reported_not_thrown()
    {
        var good = SyntheticExecutable.ImageListProperty();
        foreach (var bad in new[] { good[..200], SyntheticExecutable.ImageListProperty(bitsPerPixel: 4), SyntheticExecutable.ImageListProperty(count: 3) })
        {
            var output = Path.Combine(_root, "cache", Guid.NewGuid().ToString("N"));
            var tiles = Tiles(OriginalAssetPackBuilder.Build(Install(bad), output));
            Assert.Equal(0, tiles.Found);
            Assert.NotNull(tiles.Note);
            Assert.DoesNotContain(AssetLoader.LoadManifest(Path.Combine(output, "manifest.json")).Assets.Keys, k => k.StartsWith("terrain.", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void A_file_that_is_not_an_executable_is_reported()
    {
        var install = Path.Combine(_root, "install");
        Directory.CreateDirectory(install);
        File.WriteAllBytes(Path.Combine(install, "IMPERIAL CONQUEST 2.EXE"), [1, 2, 3, 4]);
        var tiles = Tiles(OriginalAssetPackBuilder.Build(install, Path.Combine(_root, "cache", "original")));
        Assert.Equal(0, tiles.Found);
        Assert.Contains("could not be read", tiles.Note);
    }

    [Fact]
    public void Status_counts_against_what_the_builder_can_supply()
    {
        var cache = Path.Combine(_root, "cache", "original");
        OriginalAssetPackBuilder.Build(Install(), cache);
        var status = OriginalCacheStatus.Read(Path.Combine(_root, "install"), cache);
        Assert.Equal(OriginalCacheState.Partial, status.State); // tiles present, no WAVS folder
        Assert.Equal((6, 16), (status.Keys, status.Wanted));
    }
}
