using System.Buffers.Binary;
using System.IO.Compression;

namespace IC2.Tools.TerrainImport;

/// <summary>
/// T201: the small PNG subset the terrain importer needs, with no image library. Reads non-interlaced
/// 8-bit RGB and RGBA images and 1/2/4/8-bit indexed images (as RGB through their palette); writes an
/// 8-bit indexed image. Every image editor can save one of these.
/// </summary>
public static class PngCodec
{
    private static readonly byte[] Signature = { 137, 80, 78, 71, 13, 10, 26, 10 };

    /// <summary>A decoded image: its size and one <c>0xRRGGBB</c> value per pixel, row by row.</summary>
    public sealed record Image(int Width, int Height, int[] Rgb);

    /// <summary>Decodes <paramref name="png"/>; a format outside the subset is a <see cref="TerrainImportException"/>.</summary>
    public static Image Read(byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        if (png.Length < Signature.Length || !png.AsSpan(0, Signature.Length).SequenceEqual(Signature))
        {
            throw new TerrainImportException("The file is not a PNG image.");
        }

        int width = 0, height = 0, bitDepth = 0, colorType = -1;
        int[]? palette = null;
        using var idat = new MemoryStream();
        var offset = Signature.Length;
        while (offset + 8 <= png.Length)
        {
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4));
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            if (length < 0 || offset + 12 + length > png.Length)
            {
                throw new TerrainImportException($"The PNG's '{type}' chunk runs past the end of the file.");
            }

            var data = png.AsSpan(offset + 8, length);
            switch (type)
            {
                case "IHDR":
                    width = (int)BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
                    height = (int)BinaryPrimitives.ReadUInt32BigEndian(data[4..8]);
                    bitDepth = data[8];
                    colorType = data[9];
                    if (data[12] != 0)
                    {
                        throw new TerrainImportException("Interlaced PNGs are not supported; save the map without interlacing.");
                    }

                    break;
                case "PLTE":
                    palette = new int[length / 3];
                    for (var i = 0; i < palette.Length; i++)
                    {
                        palette[i] = (data[i * 3] << 16) | (data[(i * 3) + 1] << 8) | data[(i * 3) + 2];
                    }

                    break;
                case "IDAT":
                    idat.Write(data);
                    break;
            }

            offset += 12 + length;
            if (type == "IEND")
            {
                break;
            }
        }

        if (width <= 0 || height <= 0)
        {
            throw new TerrainImportException("The PNG has no valid size (IHDR).");
        }

        var (channels, bitsPerPixel) = (colorType, bitDepth) switch
        {
            (2, 8) => (3, 24),
            (6, 8) => (4, 32),
            (3, 1 or 2 or 4 or 8) => (1, bitDepth),
            _ => throw new TerrainImportException(
                $"PNG colour type {colorType} at bit depth {bitDepth} is not supported; save as 8-bit RGB, RGBA or an indexed (palette) image."),
        };
        if (colorType == 3 && palette is null)
        {
            throw new TerrainImportException("The indexed PNG has no palette (PLTE).");
        }

        var stride = ((width * bitsPerPixel) + 7) / 8;
        var raw = Inflate(idat.ToArray(), (stride + 1) * height);
        var bytesPerPixel = Math.Max(1, bitsPerPixel / 8);
        var rows = Unfilter(raw, stride, height, bytesPerPixel);

        var rgb = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            var row = rows.AsSpan(y * stride, stride);
            for (var x = 0; x < width; x++)
            {
                rgb[(y * width) + x] = colorType switch
                {
                    2 => (row[x * 3] << 16) | (row[(x * 3) + 1] << 8) | row[(x * 3) + 2],
                    6 => row[(x * 4) + 3] == 255
                        ? (row[x * 4] << 16) | (row[(x * 4) + 1] << 8) | row[(x * 4) + 2]
                        : throw new TerrainImportException($"Pixel ({x}, {y}) is not fully opaque; terrain maps must have no transparency."),
                    _ => PaletteColour(palette!, IndexAt(row, x, bitDepth), x, y),
                };
            }
        }

        return new Image(width, height, rgb);
    }

    /// <summary>Encodes an 8-bit indexed PNG of <paramref name="indices"/> (row by row) over <paramref name="palette"/>.</summary>
    public static byte[] WriteIndexed(int width, int height, byte[] indices, IReadOnlyList<int> palette)
    {
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(palette);
        using var output = new MemoryStream();
        output.Write(Signature);

        var header = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4, 4), (uint)height);
        header[8] = 8;
        header[9] = 3;
        WriteChunk(output, "IHDR", header);

        var plte = new byte[palette.Count * 3];
        for (var i = 0; i < palette.Count; i++)
        {
            plte[i * 3] = (byte)(palette[i] >> 16);
            plte[(i * 3) + 1] = (byte)(palette[i] >> 8);
            plte[(i * 3) + 2] = (byte)palette[i];
        }

        WriteChunk(output, "PLTE", plte);

        var raw = new byte[(width + 1) * height];
        for (var y = 0; y < height; y++)
        {
            Array.Copy(indices, y * width, raw, (y * (width + 1)) + 1, width);
        }

        using (var compressed = new MemoryStream())
        {
            using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            {
                zlib.Write(raw);
            }

            WriteChunk(output, "IDAT", compressed.ToArray());
        }

        WriteChunk(output, "IEND", Array.Empty<byte>());
        return output.ToArray();
    }

    private static int PaletteColour(int[] palette, int index, int x, int y) =>
        index < palette.Length
            ? palette[index]
            : throw new TerrainImportException($"Pixel ({x}, {y}) uses palette entry {index}, which the PNG's palette does not have.");

    private static int IndexAt(ReadOnlySpan<byte> row, int x, int bitDepth)
    {
        if (bitDepth == 8)
        {
            return row[x];
        }

        var bitOffset = x * bitDepth;
        var shift = 8 - bitDepth - (bitOffset % 8);
        return (row[bitOffset / 8] >> shift) & ((1 << bitDepth) - 1);
    }

    private static byte[] Inflate(byte[] compressed, int expectedLength)
    {
        try
        {
            using var zlib = new ZLibStream(new MemoryStream(compressed), CompressionMode.Decompress);
            using var result = new MemoryStream();
            zlib.CopyTo(result);
            if (result.Length != expectedLength)
            {
                throw new TerrainImportException($"The PNG's image data holds {result.Length} bytes; its size needs {expectedLength}.");
            }

            return result.ToArray();
        }
        catch (InvalidDataException ex)
        {
            throw new TerrainImportException($"The PNG's image data is corrupt: {ex.Message}");
        }
    }

    private static byte[] Unfilter(byte[] raw, int stride, int height, int bpp)
    {
        var rows = new byte[stride * height];
        for (var y = 0; y < height; y++)
        {
            var filter = raw[y * (stride + 1)];
            var src = raw.AsSpan((y * (stride + 1)) + 1, stride);
            var dst = rows.AsSpan(y * stride, stride);
            var prior = y > 0 ? rows.AsSpan((y - 1) * stride, stride) : Span<byte>.Empty;
            for (var i = 0; i < stride; i++)
            {
                int a = i >= bpp ? dst[i - bpp] : 0;
                int b = y > 0 ? prior[i] : 0;
                int c = i >= bpp && y > 0 ? prior[i - bpp] : 0;
                dst[i] = filter switch
                {
                    0 => src[i],
                    1 => (byte)(src[i] + a),
                    2 => (byte)(src[i] + b),
                    3 => (byte)(src[i] + ((a + b) / 2)),
                    4 => (byte)(src[i] + Paeth(a, b, c)),
                    _ => throw new TerrainImportException($"Row {y} of the PNG uses unknown filter {filter}."),
                };
            }
        }

        return rows;
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream output, string type, byte[] data)
    {
        var header = new byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), (uint)data.Length);
        System.Text.Encoding.ASCII.GetBytes(type, header.AsSpan(4, 4));
        output.Write(header);
        output.Write(data);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(header.AsSpan(4, 4), data));
        output.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var span in new[] { type.ToArray(), data.ToArray() })
        {
            foreach (var value in span)
            {
                crc ^= value;
                for (var k = 0; k < 8; k++)
                {
                    crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
                }
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
