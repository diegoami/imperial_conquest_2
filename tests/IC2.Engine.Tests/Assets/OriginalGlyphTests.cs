using System.Text;
using IC2.Engine.Assets;
using Xunit;

namespace IC2.Engine.Tests.Assets;

/// <summary>Toolbar glyphs: 4-bit speed-button bitmaps, bottom-left colour key to alpha, integer scaling, and the 44-key group.</summary>
public sealed class OriginalGlyphTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ic2-glyph-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    /// <summary>A 20x20 4-bit bottom-up BMP, length-prefixed like Glyph.Data. Index 3 everywhere, the key index in the bottom-left corner and next to it, 5 top-left.</summary>
    private static byte[] Glyph(int keyIndex, bool prefix = true)
    {
        const int w = 20, h = 20, stride = 12;
        var pixels = new byte[stride * h];
        void Set(int x, int y, int index) // x, y top-left based
        {
            var o = (h - 1 - y) * stride + x / 2;
            pixels[o] = (x & 1) == 0 ? (byte)((pixels[o] & 0x0F) | (index << 4)) : (byte)((pixels[o] & 0xF0) | index);
        }
        for (var y = 0; y < h; y++) for (var x = 0; x < w; x++) Set(x, y, 3);
        Set(0, 0, 5);
        Set(0, 19, keyIndex); Set(1, 19, keyIndex); Set(5, 10, keyIndex);
        using var bmp = new MemoryStream();
        using var b = new BinaryWriter(bmp);
        b.Write("BM"u8); b.Write(54 + 64 + pixels.Length); b.Write(0); b.Write(54 + 64);
        b.Write(40); b.Write(w); b.Write(h); b.Write((ushort)1); b.Write((ushort)4);
        b.Write(0); b.Write(pixels.Length); b.Write(0); b.Write(0); b.Write(16); b.Write(0);
        for (var i = 0; i < 16; i++) { b.Write((byte)(i * 4)); b.Write((byte)(i * 8)); b.Write((byte)(i * 16)); b.Write((byte)0); } // B, G, R
        b.Write(pixels);
        var bytes = bmp.ToArray();
        return prefix ? [.. BitConverter.GetBytes(bytes.Length), .. bytes] : bytes;
    }

    private static byte[] ButtonForm(string cls, IEnumerable<string> names, int keyIndex)
    {
        using var ms = new MemoryStream();
        void PStr(string s) { ms.WriteByte((byte)s.Length); ms.Write(Encoding.Latin1.GetBytes(s)); }
        ms.Write("TPF0"u8);
        PStr(cls); PStr("Form"); ms.WriteByte(0);
        PStr("TPanel"); PStr("Panel1"); ms.WriteByte(0);
        foreach (var name in names)
        {
            PStr("TSpeedButton"); PStr(name);
            var glyph = Glyph(keyIndex);
            PStr("Glyph.Data"); ms.WriteByte(10); ms.Write(BitConverter.GetBytes(glyph.Length)); ms.Write(glyph);
            ms.WriteByte(0); ms.WriteByte(0);
        }
        ms.WriteByte(0); // end of panel children
        ms.WriteByte(0); // end of form children
        return ms.ToArray();
    }

    [Fact]
    public void The_bottom_left_colour_becomes_transparent_and_the_rest_stays_opaque_at_integer_scale()
    {
        var g = GlyphDecoder.Decode(Glyph(keyIndex: 11), scale: 2);

        Assert.Equal((40, 40), (g.Width, g.Height));
        byte Alpha(int x, int y) => g.Rgba[(y * g.Width + x) * 4 + 3];
        Assert.Equal(0, Alpha(0, 38));      // bottom-left pixel, scaled 2x
        Assert.Equal(0, Alpha(3, 39));      // its neighbour with the same index
        Assert.Equal(0, Alpha(10, 20));     // the key colour inside the glyph is transparent too
        Assert.Equal(255, Alpha(20, 20));   // ordinary pixel
        Assert.Equal(255, Alpha(0, 0));     // top-left uses another index, so it is opaque
        // index 3 is (B,G,R) = (12, 24, 48), so RGB = (48, 24, 12)
        Assert.Equal((byte[])[48, 24, 12], g.Rgba.AsSpan((20 * g.Width + 20) * 4, 3).ToArray());
    }

    [Fact]
    public void The_key_index_is_read_from_the_bitmap_so_white_keyed_glyphs_work_too()
    {
        var g = GlyphDecoder.Decode(Glyph(keyIndex: 15, prefix: false), scale: 1);
        Assert.Equal((20, 20), (g.Width, g.Height));
        Assert.Equal(0, g.Rgba[(19 * 20 + 0) * 4 + 3]);
        Assert.Equal(255, g.Rgba[(0 * 20 + 0) * 4 + 3]);
    }

    [Fact]
    public void An_unsupported_glyph_is_rejected()
    {
        var eightBit = SyntheticExecutable.ImageListProperty(); // 8-bit data is not a glyph
        Assert.Throws<InvalidDataException>(() => GlyphDecoder.Decode(eightBit, 2));
        Assert.Throws<InvalidDataException>(() => GlyphDecoder.Decode(Glyph(11)[..60], 2));
    }

    [Fact]
    public void The_forty_four_glyphs_are_built_all_or_none_as_rgba_PNGs_under_their_command_keys()
    {
        var premier = new[] { "sb_Open", "sb_Save", "sb_Endturn", "sb_News", "sb_Pols", "sb_Tax", "sb_Balance", "sb_Newunit", "sb_Newfleet" };
        var area = new[] { "sb_areacits", "sb_areacap", "sb_areaarms", "sb_areaflts", "sb_areaalln", "sb_areali", "sb_areahi", "sb_areaarch", "sb_arealc", "sb_areahc", "sb_areaallm", "sb_findcity" };
        var unit = new[] { "sb_supplyarmy", "sb_recruitmercs", "sb_transferunit", "sb_splitarmy", "sb_joinarmies", "sb_changeunit", "sb_disbandarmy", "sb_supplyfleet", "sb_repairfleet", "sb_transferships", "sb_splitfleet", "sb_joinfleets", "sb_scuttle", "sb_fortifycity", "sb_cancel" };
        var battle = new[] { "sb_unitmoves", "sb_friendlyunits", "sb_enemyunits", "sb_cancel", "sb_endturn", "sb_pauses", "sb_compgen", "sb_surrender" };
        var install = Path.Combine(_root, "install");
        SyntheticExecutable.WriteInstall(install, SyntheticExecutable.Pe(
        [
            ("TPREMIERFORM", ButtonForm("TPremierForm", premier, 15)), ("TAREAMAP", ButtonForm("TAreaMap", area, 11)),
            ("TUNITMAP", ButtonForm("TUnitMap", unit, 11)), ("TBATTLEMAP", ButtonForm("TBattleMap", battle, 11)),
        ]));
        var output = Path.Combine(_root, "cache", "original");

        var report = OriginalAssetPackBuilder.Build(install, output);

        var glyphs = report.Categories.Single(c => c.Category == "toolbar glyphs");
        Assert.Equal((44, 44), (glyphs.Found, glyphs.Total));
        var pack = AssetLoader.LoadManifest(Path.Combine(output, "manifest.json"));
        foreach (var key in new[] { "ui.command.open.icon", "ui.command.army_split.icon", "ui.command.find_city.icon", "battle.toolbar.surrender" })
        {
            Assert.True(pack.TryResolveAsset(key, out var path), key);
            var png = File.ReadAllBytes(Path.Combine(output, path));
            Assert.Equal((40, 40), (System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16)), System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20))));
            Assert.Equal(6, png[25]); // colour type RGBA
        }
    }

    [Fact]
    public void A_missing_component_drops_the_whole_glyph_group()
    {
        var install = Path.Combine(_root, "install");
        SyntheticExecutable.WriteInstall(install, SyntheticExecutable.Pe("TPREMIERFORM", ButtonForm("TPremierForm", ["sb_Open"], 15)));
        var glyphs = OriginalAssetPackBuilder.Build(install, Path.Combine(_root, "cache", "original")).Categories.Single(c => c.Category == "toolbar glyphs");
        Assert.Equal(0, glyphs.Found);
        Assert.Contains("recognised", glyphs.Note);
    }
}
