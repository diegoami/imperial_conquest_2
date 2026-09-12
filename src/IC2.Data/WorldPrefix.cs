using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IC2.Data;

/// <summary>The portion whose layout is shared by the known DAT and SAV samples.</summary>
public sealed class WorldPrefix
{
    public const int MapWidth = 320;
    public const int MapHeight = 140;
    public const int MapCellCount = MapWidth * MapHeight;
    public const int MapByteLength = MapCellCount * 2;
    public const int CityCount = 334;
    public const int CityRecordLength = 34;
    public const int CityNamesByteLength = 14;
    public const int CityStart = MapByteLength;
    public const int SharedPrefixLength = CityStart + CityCount * CityRecordLength;

    private readonly ushort[] _cells;
    private readonly CityRecord[] _cities;

    private WorldPrefix(ushort[] cells, CityRecord[] cities)
    {
        _cells = cells;
        _cities = cities;
    }

    public IReadOnlyList<ushort> Cells => _cells;
    public IReadOnlyList<CityRecord> Cities => _cities;

    public ushort CellAt(int x, int y)
    {
        if ((uint)x >= MapWidth || (uint)y >= MapHeight)
            throw new ArgumentOutOfRangeException(nameof(x), "Cell coordinates must be within the candidate 320 × 140 grid.");
        return _cells[y * MapWidth + x];
    }

    public static WorldPrefix Parse(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (data.Length < SharedPrefixLength)
            throw new InvalidDataException($"Expected at least {SharedPrefixLength} bytes for the shared world prefix; got {data.Length}.");

        var cells = new ushort[MapCellCount];
        for (var i = 0; i < cells.Length; i++)
            cells[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i * 2, 2));

        var cities = new CityRecord[CityCount];
        for (var i = 0; i < cities.Length; i++)
        {
            var offset = CityStart + i * CityRecordLength;
            var nameEnd = Array.IndexOf(data, (byte)0, offset, CityNamesByteLength);
            if (nameEnd < 0 || nameEnd == offset)
                throw new InvalidDataException($"City record {i} has no nonempty NUL-terminated name in its first 14 bytes.");
            for (var p = offset; p < nameEnd; p++)
                if (data[p] < 0x20 || data[p] > 0x7e)
                    throw new InvalidDataException($"City record {i} has a non-ASCII name byte at offset {p}.");

            var name = Encoding.ASCII.GetString(data, offset, nameEnd - offset);
            var x = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 14, 2));
            var y = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset + 16, 2));
            if (x >= MapWidth || y >= MapHeight)
                throw new InvalidDataException($"City {i} ({name}) has coordinates ({x}, {y}) outside the candidate map.");

            var raw = new byte[CityRecordLength];
            Array.Copy(data, offset, raw, 0, raw.Length);
            cities[i] = new CityRecord(i, name, x, y, raw);
        }
        return new WorldPrefix(cells, cities);
    }
}

public sealed class CityRecord
{
    private readonly byte[] _raw;

    internal CityRecord(int index, string name, ushort x, ushort y, byte[] raw)
    {
        Index = index;
        Name = name;
        X = x;
        Y = y;
        _raw = raw;
    }

    public int Index { get; }
    public string Name { get; }
    public ushort X { get; }
    public ushort Y { get; }

    /// <summary>Returns an unlabelled raw byte from this 34-byte record.</summary>
    public byte RawByteAt(int offset)
    {
        if ((uint)offset >= WorldPrefix.CityRecordLength)
            throw new ArgumentOutOfRangeException(nameof(offset));
        return _raw[offset];
    }
}

