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

    public static byte[] Form(string cls, string imageList, byte[] bitmap, int cell = 32)
    {
        using var ms = new MemoryStream();
        void PStr(string s) { ms.WriteByte((byte)s.Length); ms.Write(Encoding.Latin1.GetBytes(s)); }
        ms.Write("TPF0"u8);
        PStr(cls); PStr("UnitMap"); ms.WriteByte(0);              // no properties
        PStr("TImageList"); PStr(imageList);
        PStr("Height"); ms.WriteByte(2); ms.WriteByte((byte)cell);
        PStr("Width"); ms.WriteByte(2); ms.WriteByte((byte)cell);
        PStr("Bitmap"); ms.WriteByte(10); ms.Write(BitConverter.GetBytes(bitmap.Length)); ms.Write(bitmap);
        ms.WriteByte(0); ms.WriteByte(0);                          // end of image list properties and children
        ms.WriteByte(0);                                           // end of root children
        return ms.ToArray();
    }

    public static byte[] Pe(string resourceName, byte[] data)
    {
        const int sectionRaw = 0x200, sectionVa = 0x1000;
        var file = new byte[sectionRaw + 0x400 + data.Length];
        void U16(int o, int v) => BitConverter.GetBytes((ushort)v).CopyTo(file, o);
        void U32(int o, uint v) => BitConverter.GetBytes(v).CopyTo(file, o);
        file[0] = (byte)'M'; file[1] = (byte)'Z'; U32(0x3C, 0x40);
        U32(0x40, 0x00004550); U16(0x46, 1); U16(0x54, 224);
        var optional = 0x58; U16(optional, 0x10B);
        U32(optional + 96 + 16, sectionVa); U32(optional + 96 + 20, 0x400);
        var section = optional + 224;
        U32(section + 8, (uint)(0x400 + data.Length)); U32(section + 12, sectionVa); U32(section + 16, (uint)(file.Length - sectionRaw)); U32(section + 20, sectionRaw);
        var r = sectionRaw;
        // root: one id entry, type 10 (RCDATA) -> directory at 0x18
        U16(r + 14, 1); U32(r + 16, 10); U32(r + 20, 0x80000000 | 0x18);
        // name level at 0x18: one named entry -> directory at 0x30; the name string sits at 0x60
        U16(r + 0x18 + 12, 1); U32(r + 0x18 + 16, 0x80000000 | 0x60); U32(r + 0x18 + 20, 0x80000000 | 0x30);
        // language level at 0x30: one id entry -> data entry at 0x48
        U16(r + 0x30 + 14, 1); U32(r + 0x30 + 16, 0x409); U32(r + 0x30 + 20, 0x48);
        // data entry at 0x48: RVA and size of the data at 0x100
        U32(r + 0x48, (uint)(sectionVa + 0x100)); U32(r + 0x4C, (uint)data.Length);
        U16(r + 0x60, resourceName.Length); Encoding.Unicode.GetBytes(resourceName).CopyTo(file, r + 0x62);
        data.CopyTo(file, r + 0x100);
        return file;
    }

    /// <summary>A whole installation folder with this executable under its real name.</summary>
    public static void WriteInstall(string folder, byte[] exe)
    {
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "Imperial Conquest 2.exe"), exe);
    }
}
