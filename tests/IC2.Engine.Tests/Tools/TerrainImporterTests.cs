using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json.Nodes;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Tools.TerrainImport;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Tools;

/// <summary>
/// T201 Done-when 1: the terrain importer round-trips a painted map to a sidecar that loads as a valid world,
/// reproducibly, and refuses bad colours, a river that drains nowhere and a wrong size with a clear message.
/// </summary>
public sealed class TerrainImporterTests
{
    /// <summary>
    /// A 12×8 map: a sea frame, plain land, a forest and mountains, and a river (codes 6–11) that runs from the
    /// middle of the land to the sea on the right.
    /// </summary>
    private static (int Width, int Height, int[] Codes) Sample()
    {
        const int width = 12, height = 8;
        var codes = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var edge = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                codes[(y * width) + x] = edge ? (x == 0 ? 1 : 0) : 2;
            }
        }

        codes[(2 * width) + 2] = 4;
        codes[(5 * width) + 3] = 5;
        codes[(3 * width) + 3] = 3;
        for (var x = 5; x <= 10; x++)
        {
            codes[(4 * width) + x] = 6 + (x - 5);
        }

        return (width, height, codes);
    }

    [Fact]
    public void A_painted_map_round_trips_to_a_sidecar_that_loads_as_a_world_and_reruns_byte_identically()
    {
        var (width, height, codes) = Sample();
        var png = TerrainImporter.Png(width, height, codes);

        var imported = TerrainImporter.CodesFrom(PngCodec.Read(png), width, height);
        Assert.Equal(codes, imported);

        var root = Path.Combine(Path.GetTempPath(), "ic2-t201-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var world = JsonNode.Parse(File.ReadAllText(Path.Combine(ModelTestPaths.DataRoot, "worlds", "example-tiny-duel.json")))!.AsObject();
            world["width"] = width;
            world["height"] = height;
            var classical = JsonNode.Parse(File.ReadAllText(Path.Combine(ModelTestPaths.DataRoot, "worlds", "classical-mediterranean.json")))!;
            world["tileTypes"] = classical["tileTypes"]!.DeepClone();
            world["terrain"] = new JsonObject { ["encoding"] = "base64", ["runs"] = null, ["data"] = null, ["dataFile"] = "painted.terrain.b64" };
            var worldPath = Path.Combine(root, "painted.json");
            File.WriteAllText(worldPath, world.ToJsonString());

            var sidecar = TerrainImporter.Sidecar(imported);
            File.WriteAllText(Path.Combine(root, "painted.terrain.b64"), sidecar);
            var loaded = GameDataLoader.LoadFile<World>(worldPath);

            Assert.Equal(codes, loaded.Terrain.Decode(width, height));
            Assert.Equal(sidecar, TerrainImporter.Sidecar(TerrainImporter.CodesFrom(PngCodec.Read(TerrainImporter.Png(width, height, imported)), width, height)));
            Assert.Equal(png, TerrainImporter.Png(width, height, imported));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void The_classical_terrain_exported_and_imported_again_reproduces_the_committed_sidecar()
    {
        var world = GameDataRepository.Load(ModelTestPaths.DataRoot).WorldById("classical-mediterranean")!;
        var codes = world.Terrain.Decode(world.Width, world.Height);

        var imported = TerrainImporter.CodesFrom(PngCodec.Read(TerrainImporter.Png(world.Width, world.Height, codes)), world.Width, world.Height);

        var committed = File.ReadAllText(Path.Combine(ModelTestPaths.DataRoot, "worlds", "classical-mediterranean.terrain.b64"));
        Assert.Equal(committed, TerrainImporter.Sidecar(imported));
    }

    [Fact]
    public void An_unknown_colour_is_refused_naming_the_pixel_and_the_palette()
    {
        var (width, height, codes) = Sample();
        var image = PngCodec.Read(TerrainImporter.Png(width, height, codes));
        image.Rgb[(3 * width) + 7] = 0xFF00FF;

        var ex = Assert.Throws<TerrainImportException>(() => TerrainImporter.CodesFrom(image, width, height));

        Assert.Contains("Pixel (7, 3) is #FF00FF", ex.Message, StringComparison.Ordinal);
        Assert.Contains("2 plain #9CCB5A", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_river_that_reaches_neither_sea_nor_edge_is_refused()
    {
        var (width, height, codes) = Sample();
        codes[(4 * width) + 10] = 2; // cut the river's mouth: its last tile no longer touches the sea

        var ex = Assert.Throws<TerrainImportException>(
            () => TerrainImporter.CodesFrom(PngCodec.Read(TerrainImporter.Png(width, height, codes)), width, height));

        Assert.Contains("The river through (5, 4) (5 tiles) reaches neither the sea nor the map edge", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_river_ending_at_the_map_edge_is_accepted()
    {
        const int width = 4, height = 3;
        var codes = new[] { 2, 2, 2, 2, 6, 7, 2, 2, 2, 2, 2, 2 };

        Assert.Equal(codes, TerrainImporter.CodesFrom(PngCodec.Read(TerrainImporter.Png(width, height, codes)), width, height));
    }

    [Fact]
    public void A_map_whose_size_differs_from_the_world_is_refused()
    {
        var (width, height, codes) = Sample();

        var ex = Assert.Throws<TerrainImportException>(
            () => TerrainImporter.CodesFrom(PngCodec.Read(TerrainImporter.Png(width, height, codes)), width, height + 1));

        Assert.Contains("The map is 12×8 pixels, but the world is 12×9 tiles", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void An_RGB_map_saved_with_any_row_filter_reads_the_same(int filter)
    {
        var (width, height, codes) = Sample();
        var rgb = codes.Select(c => TerrainImporter.Palette[c].Rgb).ToArray();

        var image = PngCodec.Read(RgbPng(width, height, rgb, (byte)filter));

        Assert.Equal(rgb, image.Rgb);
        Assert.Equal(codes, TerrainImporter.CodesFrom(image, width, height));
    }

    [Fact]
    public void A_file_that_is_not_a_PNG_is_refused()
    {
        var ex = Assert.Throws<TerrainImportException>(() => PngCodec.Read(new byte[] { 1, 2, 3 }));

        Assert.Equal("The file is not a PNG image.", ex.Message);
    }

    /// <summary>An 8-bit RGB PNG with every row stored under <paramref name="filter"/>, built the way an editor would.</summary>
    private static byte[] RgbPng(int width, int height, int[] rgb, byte filter)
    {
        var stride = width * 3;
        var rows = new byte[stride * height];
        for (var i = 0; i < rgb.Length; i++)
        {
            rows[i * 3] = (byte)(rgb[i] >> 16);
            rows[(i * 3) + 1] = (byte)(rgb[i] >> 8);
            rows[(i * 3) + 2] = (byte)rgb[i];
        }

        var raw = new byte[(stride + 1) * height];
        for (var y = 0; y < height; y++)
        {
            raw[y * (stride + 1)] = filter;
            for (var i = 0; i < stride; i++)
            {
                int a = i >= 3 ? rows[(y * stride) + i - 3] : 0;
                int b = y > 0 ? rows[((y - 1) * stride) + i] : 0;
                int c = i >= 3 && y > 0 ? rows[((y - 1) * stride) + i - 3] : 0;
                var predictor = filter switch
                {
                    1 => a,
                    2 => b,
                    3 => (a + b) / 2,
                    _ => Paeth(a, b, c),
                };
                raw[(y * (stride + 1)) + 1 + i] = (byte)(rows[(y * stride) + i] - predictor);
            }
        }

        using var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)height);
        header[8] = 8;
        header[9] = 2;
        Chunk(output, "IHDR", header);
        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                zlib.Write(raw);
            }

            Chunk(output, "IDAT", compressed.ToArray());
        }

        Chunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var (pa, pb, pc) = (Math.Abs(p - a), Math.Abs(p - b), Math.Abs(p - c));
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    /// <summary>A chunk with a zero CRC: the reader does not check CRCs, so the test need not compute them.</summary>
    private static void Chunk(Stream output, string type, byte[] data)
    {
        var header = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)data.Length);
        System.Text.Encoding.ASCII.GetBytes(type, header.AsSpan(4, 4));
        output.Write(header);
        output.Write(data);
        output.Write(new byte[4]);
    }
}
