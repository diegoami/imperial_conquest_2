using System.Buffers.Binary;

namespace IC2.Tools.TerrainImport;

/// <summary>A terrain map the importer refuses, with a message naming the problem and where it is.</summary>
public sealed class TerrainImportException(string message) : Exception(message);

/// <summary>One terrain code and the colour it is painted in.</summary>
public sealed record TerrainSwatch(int Code, string TileId, int Rgb)
{
    /// <summary>The colour as <c>#RRGGBB</c>.</summary>
    public string Hex => $"#{Rgb:X6}";
}

/// <summary>
/// T201: turns a painted terrain map into a world's terrain grid and back. Pure: no file access, so the tests
/// and the command-line tool share it.
/// </summary>
/// <remarks>
/// The palette is <strong>[designed]</strong> (T201): one colour per shipped terrain code 0–11
/// (<c>data/worlds/classical-mediterranean.json</c>'s <c>tileTypes</c>), chosen to be told apart at a glance,
/// plus code 12, hills (T202), which a world must declare in its own <c>tileTypes</c> to use.
/// It is documented in <c>docs/scenario-authoring.md</c>, "Authoring terrain from a PNG".
/// </remarks>
public static class TerrainImporter
{
    /// <summary>The largest map the importer accepts on either side.</summary>
    public const int MaxSide = 1024;

    /// <summary>The terrain palette, by code.</summary>
    public static readonly IReadOnlyList<TerrainSwatch> Palette = new[]
    {
        new TerrainSwatch(0, "sea_calm", 0x2A5CAA),
        new TerrainSwatch(1, "sea_rough", 0x14306A),
        new TerrainSwatch(2, "plain", 0x9CCB5A),
        new TerrainSwatch(3, "desert", 0xE8D58A),
        new TerrainSwatch(4, "forest", 0x2E7D32),
        new TerrainSwatch(5, "mountains", 0x8D6E63),
        new TerrainSwatch(6, "river_1", 0x80DEEA),
        new TerrainSwatch(7, "river_2", 0x4DD0E1),
        new TerrainSwatch(8, "river_3", 0x26C6DA),
        new TerrainSwatch(9, "river_4", 0x00BCD4),
        new TerrainSwatch(10, "river_5", 0x00ACC1),
        new TerrainSwatch(11, "river_6", 0x0097A7),
        new TerrainSwatch(12, "hills", 0xB5A642),
    };

    /// <summary>Whether <paramref name="code"/> is a sea code (0–1).</summary>
    public static bool IsSea(int code) => code is 0 or 1;

    /// <summary>Whether <paramref name="code"/> is a river code (6–11).</summary>
    public static bool IsRiver(int code) => code is >= 6 and <= 11;

    /// <summary>
    /// The terrain codes of <paramref name="image"/>, row by row, after every check: the size matches
    /// <paramref name="expectedWidth"/>×<paramref name="expectedHeight"/>, every pixel is a palette colour, and every
    /// river stroke reaches the sea or the map edge.
    /// </summary>
    public static int[] CodesFrom(PngCodec.Image image, int expectedWidth, int expectedHeight)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (expectedWidth is <= 0 or > MaxSide || expectedHeight is <= 0 or > MaxSide)
        {
            throw new TerrainImportException($"A world must be 1–{MaxSide} tiles on each side; the world says {expectedWidth}×{expectedHeight}.");
        }

        if (image.Width != expectedWidth || image.Height != expectedHeight)
        {
            throw new TerrainImportException(
                $"The map is {image.Width}×{image.Height} pixels, but the world is {expectedWidth}×{expectedHeight} tiles; they must match, one pixel per tile.");
        }

        var byColour = Palette.ToDictionary(s => s.Rgb, s => s.Code);
        var codes = new int[image.Rgb.Length];
        for (var i = 0; i < codes.Length; i++)
        {
            if (!byColour.TryGetValue(image.Rgb[i], out codes[i]))
            {
                throw new TerrainImportException(
                    $"Pixel ({i % image.Width}, {i / image.Width}) is #{image.Rgb[i]:X6}, which is not a terrain colour. The palette is: "
                    + string.Join(", ", Palette.Select(s => $"{s.Code} {s.TileId} {s.Hex}")) + ".");
            }
        }

        RequireRiversReachSeaOrEdge(image.Width, image.Height, codes);
        return codes;
    }

    /// <summary>
    /// Refuses a river stroke (a 4-connected group of river tiles) that touches neither a sea tile nor the map
    /// edge: water has to flow somewhere.
    /// </summary>
    public static void RequireRiversReachSeaOrEdge(int width, int height, int[] codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var seen = new bool[codes.Length];
        var queue = new Queue<int>();
        for (var start = 0; start < codes.Length; start++)
        {
            if (seen[start] || !IsRiver(codes[start]))
            {
                continue;
            }

            var drains = false;
            var size = 0;
            seen[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var cell = queue.Dequeue();
                size++;
                var (x, y) = (cell % width, cell / width);
                if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                {
                    drains = true;
                }

                foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                {
                    if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                    {
                        continue;
                    }

                    var next = (ny * width) + nx;
                    if (IsSea(codes[next]))
                    {
                        drains = true;
                    }
                    else if (IsRiver(codes[next]) && !seen[next])
                    {
                        seen[next] = true;
                        queue.Enqueue(next);
                    }
                }
            }

            if (!drains)
            {
                throw new TerrainImportException(
                    $"The river through ({start % width}, {start / width}) ({size} tiles) reaches neither the sea nor the map edge; "
                    + "connect it to the sea or the edge, or paint it as land.");
            }
        }
    }

    /// <summary>
    /// The <c>.terrain.b64</c> sidecar for <paramref name="codes"/>: base64 of one little-endian 16-bit code per
    /// tile, row by row, then a newline — the shipped sidecar's own format (<c>TerrainGrid.Decode</c>).
    /// </summary>
    public static string Sidecar(int[] codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var bytes = new byte[codes.Length * 2];
        for (var i = 0; i < codes.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2, 2), (ushort)codes[i]);
        }

        return Convert.ToBase64String(bytes) + "\n";
    }

    /// <summary>A PNG of <paramref name="codes"/> in the palette, to edit and import again.</summary>
    public static byte[] Png(int width, int height, int[] codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var indices = new byte[codes.Length];
        for (var i = 0; i < codes.Length; i++)
        {
            if (codes[i] < 0 || codes[i] >= Palette.Count)
            {
                throw new TerrainImportException($"Tile ({i % width}, {i / width}) has code {codes[i]}, which has no palette colour.");
            }

            indices[i] = (byte)codes[i];
        }

        return PngCodec.WriteIndexed(width, height, indices, Palette.Select(s => s.Rgb).ToArray());
    }
}
