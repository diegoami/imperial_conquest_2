using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace IC2.Engine.Assets;

/// <summary>
/// T292b: bounded readers for the containers the original executable keeps its images in. Every read is range
/// checked and throws <see cref="InvalidDataException"/> for anything unexpected, so an unsupported build is
/// reported instead of guessed at. Nothing here is original data; the format knowledge comes from the research
/// extraction spec (docs/reports/2026-11-07-original-assets218-extraction-spec.md).
/// </summary>
public static class PeResources
{
    /// <summary>The RCDATA resources of a 32-bit PE file as (name, bytes). Names are the resource names.</summary>
    public static IReadOnlyList<(string Name, byte[] Data)> ReadRcData(byte[] file)
    {
        const int RcData = 10;
        var pe = (int)U32(file, 0x3C);
        if (pe < 0 || pe + 24 > file.Length || U32(file, pe) != 0x00004550) throw new InvalidDataException("not a PE file");
        var sections = U16(file, pe + 6);
        var optional = pe + 24;
        if (U16(file, optional) != 0x10B) throw new InvalidDataException("not a 32-bit PE file");
        var resourceRva = U32(file, optional + 96 + 16);
        if (resourceRva == 0) return [];
        var sectionTable = optional + U16(file, pe + 20);
        var map = new List<(uint Va, uint Size, uint Raw)>();
        for (var i = 0; i < sections; i++)
        {
            var s = sectionTable + i * 40;
            Need(file, s, 40);
            map.Add((U32(file, s + 12), Math.Max(U32(file, s + 8), U32(file, s + 16)), U32(file, s + 20)));
        }

        int Offset(uint rva)
        {
            foreach (var (va, size, raw) in map)
                if (rva >= va && rva < va + size) return checked((int)(raw + rva - va));
            throw new InvalidDataException("resource address outside every section");
        }

        var baseOffset = Offset(resourceRva);
        List<(uint Name, uint Target)> Entries(int offset)
        {
            Need(file, offset, 16);
            var count = U16(file, offset + 12) + U16(file, offset + 14);
            var list = new List<(uint, uint)>();
            for (var i = 0; i < count; i++)
            {
                Need(file, offset + 16 + i * 8, 8);
                list.Add((U32(file, offset + 16 + i * 8), U32(file, offset + 20 + i * 8)));
            }
            return list;
        }

        string Name(uint raw)
        {
            if ((raw & 0x80000000) == 0) return "#" + raw;
            var o = baseOffset + (int)(raw & 0x7FFFFFFF);
            var length = U16(file, o);
            Need(file, o + 2, length * 2);
            return Encoding.Unicode.GetString(file, o + 2, length * 2);
        }

        var result = new List<(string, byte[])>();
        foreach (var (typeName, typeTarget) in Entries(baseOffset))
        {
            if (typeName != RcData || (typeTarget & 0x80000000) == 0) continue;
            foreach (var (name, nameTarget) in Entries(baseOffset + (int)(typeTarget & 0x7FFFFFFF)))
            {
                if ((nameTarget & 0x80000000) == 0) continue;
                foreach (var (_, languageTarget) in Entries(baseOffset + (int)(nameTarget & 0x7FFFFFFF)))
                {
                    var entry = baseOffset + (int)languageTarget;
                    Need(file, entry, 16);
                    var start = Offset(U32(file, entry));
                    var size = checked((int)U32(file, entry + 4));
                    Need(file, start, size);
                    result.Add((Name(name), file.AsSpan(start, size).ToArray()));
                }
            }
        }
        return result;
    }

    internal static ushort U16(byte[] b, int o) { Need(b, o, 2); return BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(o)); }
    internal static uint U32(byte[] b, int o) { Need(b, o, 4); return BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(o)); }
    internal static void Need(byte[] b, int offset, int length)
    {
        if (offset < 0 || length < 0 || offset > b.Length - length) throw new InvalidDataException("read past the end of the data");
    }
}

/// <summary>A Delphi form stream (<c>TPF0</c>): components, their class names and their binary properties.</summary>
public sealed record DelphiComponent(string ClassName, string Name, IReadOnlyDictionary<string, byte[]> BinaryProperties,
    IReadOnlyDictionary<string, long> IntegerProperties, IReadOnlyList<DelphiComponent> Children)
{
    public static DelphiComponent Parse(byte[] stream)
    {
        if (stream.Length < 6 || stream[0] != 'T' || stream[1] != 'P' || stream[2] != 'F' || stream[3] != '0')
            throw new InvalidDataException("not a TPF0 form stream");
        var position = 4;
        return ReadObject(stream, ref position, 0);
    }

    /// <summary>The first component (depth first) matching class and name, or null.</summary>
    public DelphiComponent? Find(string className, string name)
    {
        if (ClassName == className && Name == name) return this;
        foreach (var child in Children)
            if (child.Find(className, name) is { } found) return found;
        return null;
    }

    private static DelphiComponent ReadObject(byte[] b, ref int p, int depth)
    {
        if (depth > 32) throw new InvalidDataException("form nesting is too deep");
        if ((Byte(b, p) & 0xF0) == 0xF0)
        {
            var flags = b[p++] & 0x0F;
            if ((flags & 2) != 0) SkipValue(b, ref p, b[p++]); // child position
        }
        var className = PString(b, ref p);
        var name = PString(b, ref p);
        var binary = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var integers = new Dictionary<string, long>(StringComparer.Ordinal);
        while (Byte(b, p) != 0)
        {
            var property = PString(b, ref p);
            var type = Byte(b, p++);
            switch (type)
            {
                case 10:
                    var length = checked((int)PeResources.U32(b, p)); p += 4;
                    PeResources.Need(b, p, length);
                    binary[property] = b.AsSpan(p, length).ToArray(); p += length;
                    break;
                case 2: integers[property] = (sbyte)Byte(b, p); p += 1; break;
                case 3: integers[property] = (short)PeResources.U16(b, p); p += 2; break;
                case 4: integers[property] = (int)PeResources.U32(b, p); p += 4; break;
                default: SkipValue(b, ref p, type); break;
            }
        }
        p++;
        var children = new List<DelphiComponent>();
        while (Byte(b, p) != 0) children.Add(ReadObject(b, ref p, depth + 1));
        p++;
        return new DelphiComponent(className, name, binary, integers, children);
    }

    private static byte Byte(byte[] b, int p) { PeResources.Need(b, p, 1); return b[p]; }

    private static string PString(byte[] b, ref int p)
    {
        int n = Byte(b, p++);
        PeResources.Need(b, p, n);
        var s = Encoding.Latin1.GetString(b, p, n);
        p += n;
        return s;
    }

    /// <summary>Steps over a property value whose content the reader does not need.</summary>
    private static void SkipValue(byte[] b, ref int p, int type)
    {
        switch (type)
        {
            case 2: p += 1; break;
            case 3: p += 2; break;
            case 4: case 15: p += 4; break;
            case 5: p += 10; break;
            case 6: case 7: { int n = Byte(b, p++); p += n; break; }
            case 8: case 9: case 13: break;
            case 10: { var n = checked((int)PeResources.U32(b, p)); p += 4 + n; break; }
            case 11: while (PString(b, ref p).Length > 0) { } break;
            case 12: case 20: { var n = checked((int)PeResources.U32(b, p)); p += 4 + n; break; }
            case 16: case 17: case 19: p += 8; break;
            case 18: { var n = checked((int)PeResources.U32(b, p)); p += 4 + n * 2; break; }
            case 1: while (Byte(b, p) != 0) { var t = b[p++]; SkipValue(b, ref p, t); } p++; break;
            default: throw new InvalidDataException($"unsupported property value type {type}");
        }
        if (p > b.Length) throw new InvalidDataException("property runs past the end of the stream");
    }
}

/// <summary>One decoded bitmap: RGB bytes, top row first, and an optional 1-bit mask expanded to one byte per pixel.</summary>
public sealed record DecodedBitmap(int Width, int Height, byte[] Rgb);

public static class BmpDecoder
{
    /// <summary>Uncompressed 8-bit indexed (palette) bitmap to RGB.</summary>
    public static DecodedBitmap DecodeIndexed8(ReadOnlySpan<byte> bmp)
    {
        var b = bmp.ToArray();
        if (b.Length < 54 || b[0] != 'B' || b[1] != 'M') throw new InvalidDataException("not a bitmap");
        var pixelOffset = (int)PeResources.U32(b, 10);
        var header = (int)PeResources.U32(b, 14);
        var width = (int)PeResources.U32(b, 18);
        var rawHeight = (int)PeResources.U32(b, 22);
        var bits = PeResources.U16(b, 28);
        var compression = PeResources.U32(b, 30);
        var colours = (int)PeResources.U32(b, 46);
        if (header < 40 || bits != 8 || compression != 0 || width <= 0 || rawHeight == 0 || width > 4096 || Math.Abs(rawHeight) > 4096)
            throw new InvalidDataException("unsupported bitmap encoding");
        if (colours == 0) colours = 256;
        if (colours > 256) throw new InvalidDataException("palette too large");
        var paletteOffset = 14 + header;
        PeResources.Need(b, paletteOffset, colours * 4);
        var height = Math.Abs(rawHeight);
        var stride = (width + 3) & ~3;
        PeResources.Need(b, pixelOffset, checked(stride * height));
        var rgb = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            var sourceRow = rawHeight > 0 ? height - 1 - y : y; // positive height means bottom-up
            for (var x = 0; x < width; x++)
            {
                var index = b[pixelOffset + sourceRow * stride + x];
                if (index >= colours) throw new InvalidDataException("pixel outside the palette");
                var entry = paletteOffset + index * 4;
                var o = (y * width + x) * 3;
                rgb[o] = b[entry + 2]; rgb[o + 1] = b[entry + 1]; rgb[o + 2] = b[entry];
            }
        }
        return new DecodedBitmap(width, height, rgb);
    }
}

/// <summary>A Delphi TImageList's stored bitmap: <c>[u32 colour-BMP length][u32 image count][colour BMP][mask BMP]</c>.</summary>
public sealed record OriginalImageList(int CellWidth, int CellHeight, int Count, DecodedBitmap Sheet)
{
    public static OriginalImageList Parse(byte[] property, int cellWidth, int cellHeight)
    {
        if (property.Length < 8 + 54) throw new InvalidDataException("image list is too short");
        var length = (int)PeResources.U32(property, 0);
        var count = (int)PeResources.U32(property, 4);
        PeResources.Need(property, 8, length);
        if (count <= 0 || count > 400) throw new InvalidDataException("implausible image count");
        var sheet = BmpDecoder.DecodeIndexed8(property.AsSpan(8, length));
        if (cellWidth <= 0 || cellHeight <= 0 || sheet.Width % cellWidth != 0 || sheet.Height % cellHeight != 0
            || count > (sheet.Width / cellWidth) * (sheet.Height / cellHeight))
            throw new InvalidDataException("image list geometry does not fit its sheet");
        return new OriginalImageList(cellWidth, cellHeight, count, sheet);
    }

    /// <summary>Cell <paramref name="index"/> as RGB, columns = sheet width / cell width, row-major from the top left.</summary>
    public byte[] CellRgb(int index)
    {
        if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        var columns = Sheet.Width / CellWidth;
        var x0 = index % columns * CellWidth;
        var y0 = index / columns * CellHeight;
        var cell = new byte[CellWidth * CellHeight * 3];
        for (var y = 0; y < CellHeight; y++)
            Buffer.BlockCopy(Sheet.Rgb, ((y0 + y) * Sheet.Width + x0) * 3, cell, y * CellWidth * 3, CellWidth * 3);
        return cell;
    }
}

public static class PngEncoder
{
    /// <summary>An opaque 8-bit RGB PNG.</summary>
    public static byte[] EncodeRgb(int width, int height, byte[] rgb)
    {
        if (rgb.Length != width * height * 3) throw new ArgumentException("pixel data does not match the size");
        using var raw = new MemoryStream();
        for (var y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgb, y * width * 3, width * 3);
        }
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true)) z.Write(raw.GetBuffer(), 0, (int)raw.Length);

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var ihdr = new byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.AsSpan(4), (uint)height);
        ihdr[8] = 8; ihdr[9] = 2;
        Chunk(png, "IHDR", ihdr);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return ~crc;
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        s.Write(length);
        var typed = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, typed);
        data.CopyTo(typed, 4);
        s.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(length, Crc32(typed));
        s.Write(length);
    }
}
