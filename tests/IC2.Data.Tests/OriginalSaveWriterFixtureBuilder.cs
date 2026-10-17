using System.Buffers.Binary;
using System.Text;

namespace IC2.Data.Tests;

/// <summary>Independent framing oracle. Does not use writer offsets or the existing synthetic builder.</summary>
internal static class OriginalSaveWriterFixtureBuilder
{
    internal static byte[] Build(int armies, int fleets, int newsIndex)
    {
        var data = new byte[100956 + 2 + armies * 656 + 2 + fleets * 26 + 18752 + 600 + 2
            + (newsIndex + 1) * 61 + 55];
        // Nonzero, position-dependent residue over the ENTIRE file, including opaque regions.
        for (var i = 0; i < data.Length; i++) data[i] = (byte)(2 + (i * 37L + i / 251) % 253);
        for (var i = 0; i < 334; i++)
        {
            var city = 89600 + i * 34;
            Text(data, city, $"C{i}"); Word(data, city + 14, i % 320); Word(data, city + 16, i % 140);
        }
        Word(data, 100956, armies);
        for (var i = 0; i < armies; i++)
        {
            var army = 100958 + i * 656;
            Array.Clear(data, army, 16);
            Word(data, army + 6, -1);
            for (var u = 0; u < 20; u++) Word(data, army + 16 + u * 32 + 4, 0);
            // One live unit exercises strict 23-character storage plus NUL and negative moves.
            if (i == 0)
            {
                Word(data, army + 18, 4); Word(data, army + 20, 1); Word(data, army + 22, 9);
                Text(data, army + 24, new string('U', 23));
            }
        }
        var fleetCount = 100958 + armies * 656;
        Word(data, fleetCount, fleets);
        for (var i = 0; i < fleets; i++) Array.Clear(data, fleetCount + 2 + i * 26, 26);
        var nations = fleetCount + 2 + fleets * 26;
        for (var i = 0; i < 16; i++)
        {
            var nation = nations + i * 1172;
            Text(data, nation, NationCatalog.Name((ushort)i)); Text(data, nation + 11, $"Leader{i}");
            Array.Clear(data, nation + 0x26, 32);
            for (var u = 0; u < 40; u++) Word(data, nation + 0x2e4 + u * 8 + 4, 0);
            Word(data, nation + 0x444, 65535); Word(data, nation + 0x446, 0); Word(data, nation + 0x490, 0);
        }
        var news = nations + 18752 + 600;
        Word(data, news, newsIndex);
        for (var i = 0; i <= newsIndex; i++) Text(data, news + 2 + i * 61, i == 0 ? new string('N', 60) : $"News{i}");
        var tail = news + 2 + (newsIndex + 1) * 61;
        for (var i = 0; i < 16; i++) Word(data, tail + 2 * i, i);
        Word(data, tail + 32, 65535); Word(data, tail + 34, 1);
        Word(data, tail + 36, 0); Word(data, tail + 38, 0); Word(data, tail + 40, 13);
        Word(data, tail + 42, 1000); Word(data, tail + 44, 3); data[tail + 54] = 0;
        return data;
    }

    internal static void Word(byte[] data, int offset, int value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), unchecked((ushort)value));
    private static void Text(byte[] data, int offset, string text)
    {
        Encoding.ASCII.GetBytes(text).CopyTo(data, offset); data[offset + text.Length] = 0;
    }
}
