using System.Text;

namespace IC2.Engine.Tests.Assets;

/// <summary>Builds a tiny PE32 file with one RCDATA resource, and a TPF0 form with a TerrainList image list, from scratch. No original bytes.</summary>
internal static class SyntheticExecutable
{
    public static byte[] ImageListProperty(int width = 128, int height = 128, int cell = 32, int count = 13, byte bitsPerPixel = 8)
    {
        const int paletteBytes = 1024;
        var stride = (width + 3) & ~3;
        var pixels = new byte[stride * height];
        // Image-list cell k is filled with palette index k + 1; the bitmap is stored bottom-up like the real ones.
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                pixels[(height - 1 - y) * stride + x] = (byte)(y / cell * (width / cell) + x / cell + 1);
        using var bmp = new MemoryStream();
        using var w = new BinaryWriter(bmp);
        w.Write("BM"u8); w.Write(54 + paletteBytes + pixels.Length); w.Write(0); w.Write(54 + paletteBytes);
        w.Write(40); w.Write(width); w.Write(height); w.Write((ushort)1); w.Write((ushort)bitsPerPixel);
        w.Write(0); w.Write(pixels.Length); w.Write(0); w.Write(0); w.Write(256); w.Write(0);
        for (var i = 0; i < 256; i++) { w.Write((byte)(i * 3)); w.Write((byte)(i * 2)); w.Write((byte)i); w.Write((byte)0); } // B, G, R
        w.Write(pixels);
        var colour = bmp.ToArray();
        using var prop = new MemoryStream();
        using var p = new BinaryWriter(prop);
        p.Write(colour.Length); p.Write(count); p.Write(colour); p.Write("BM"u8); p.Write(new byte[64]); // mask: ignored by the tile slice
        return prop.ToArray();
    }

    /// <summary>A form whose root holds one TImageList per entry: (component name, stored bitmap, cell size).</summary>
    public static byte[] Form(string cls, params (string Name, byte[] Bitmap, int Cell)[] lists)
    {
        using var ms = new MemoryStream();
        void PStr(string s) { ms.WriteByte((byte)s.Length); ms.Write(Encoding.Latin1.GetBytes(s)); }
        ms.Write("TPF0"u8);
        PStr(cls); PStr("UnitMap"); ms.WriteByte(0);              // no properties
        foreach (var (name, bitmap, cell) in lists)
        {
            PStr("TImageList"); PStr(name);
            PStr("Height"); ms.WriteByte(2); ms.WriteByte((byte)cell);
            PStr("Width"); ms.WriteByte(2); ms.WriteByte((byte)cell);
            PStr("Bitmap"); ms.WriteByte(10); ms.Write(BitConverter.GetBytes(bitmap.Length)); ms.Write(bitmap);
            ms.WriteByte(0); ms.WriteByte(0);                      // end of image list properties and children
        }
        ms.WriteByte(0);                                           // end of root children
        return ms.ToArray();
    }

    public static byte[] Form(string cls, string imageList, byte[] bitmap, int cell = 32) => Form(cls, (imageList, bitmap, cell));

    public static byte[] Pe(string resourceName, byte[] data) => Pe([(resourceName, data)]);

    /// <summary>A PE32 file whose .rsrc holds one RCDATA resource per entry.</summary>
    public static byte[] Pe(IReadOnlyList<(string Name, byte[] Data)> resources)
    {
        const int sectionRaw = 0x200, sectionVa = 0x1000;
        var n = resources.Count;
        var nameDir = 0x18;
        var langDirs = nameDir + 16 + 8 * n;
        var dataEntries = langDirs + 24 * n;
        var names = dataEntries + 16 * n;
        var nameOffsets = new int[n];
        var cursor = names;
        for (var i = 0; i < n; i++) { nameOffsets[i] = cursor; cursor += 2 + resources[i].Name.Length * 2; }
        cursor = (cursor + 15) & ~15;
        var dataOffsets = new int[n];
        for (var i = 0; i < n; i++) { dataOffsets[i] = cursor; cursor += (resources[i].Data.Length + 15) & ~15; }
        var resourceSize = cursor;

        var file = new byte[sectionRaw + resourceSize];
        void U16(int o, int v) => BitConverter.GetBytes((ushort)v).CopyTo(file, o);
        void U32(int o, uint v) => BitConverter.GetBytes(v).CopyTo(file, o);
        file[0] = (byte)'M'; file[1] = (byte)'Z'; U32(0x3C, 0x40);
        U32(0x40, 0x00004550); U16(0x46, 1); U16(0x54, 224);
        var optional = 0x58; U16(optional, 0x10B);
        U32(optional + 96 + 16, sectionVa); U32(optional + 96 + 20, (uint)resourceSize);
        var section = optional + 224;
        U32(section + 8, (uint)resourceSize); U32(section + 12, sectionVa); U32(section + 16, (uint)resourceSize); U32(section + 20, sectionRaw);
        var r = sectionRaw;
        // root: one id entry, type 10 (RCDATA) -> the name directory
        U16(r + 14, 1); U32(r + 16, 10); U32(r + 20, (uint)(0x80000000 | (uint)nameDir));
        // name level: one named entry per resource -> its language directory
        U16(r + nameDir + 12, n);
        for (var i = 0; i < n; i++)
        {
            U32(r + nameDir + 16 + i * 8, (uint)(0x80000000 | (uint)nameOffsets[i]));
            U32(r + nameDir + 20 + i * 8, (uint)(0x80000000 | (uint)(langDirs + 24 * i)));
            // language level: one id entry -> its data entry
            U16(r + langDirs + 24 * i + 14, 1); U32(r + langDirs + 24 * i + 16, 0x409); U32(r + langDirs + 24 * i + 20, (uint)(dataEntries + 16 * i));
            U32(r + dataEntries + 16 * i, (uint)(sectionVa + dataOffsets[i])); U32(r + dataEntries + 16 * i + 4, (uint)resources[i].Data.Length);
            U16(r + nameOffsets[i], resources[i].Name.Length);
            Encoding.Unicode.GetBytes(resources[i].Name).CopyTo(file, r + nameOffsets[i] + 2);
            resources[i].Data.CopyTo(file, r + dataOffsets[i]);
        }
        return file;
    }

    /// <summary>A whole installation folder with this executable under its real name.</summary>
    public static void WriteInstall(string folder, byte[] exe)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "Imperial Conquest 2.exe"), exe);
    }
}
