using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IC2.Data;

/// <summary>The fixed-size army table observed after the city table in known SAV files.</summary>
public sealed class SaveArmyTable
{
    public const int RecordLength = 656;
    public const int HeaderLength = 16;
    public const int UnitSlotLength = 32;
    public const int UnitSlotsPerArmy = 20;

    private SaveArmyTable(ArmyRecord[] armies) => Armies = armies;

    public IReadOnlyList<ArmyRecord> Armies { get; }

    public static SaveArmyTable Parse(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (data.Length < WorldPrefix.SharedPrefixLength + 2)
            throw new InvalidDataException("Save ends before the army count.");

        var count = ReadWord(data, WorldPrefix.SharedPrefixLength);
        var tableStart = WorldPrefix.SharedPrefixLength + 2;
        if (count > (data.Length - tableStart) / RecordLength)
            throw new InvalidDataException($"Army count {count} exceeds the available fixed-size records.");

        var armies = new ArmyRecord[count];
        for (var i = 0; i < count; i++)
        {
            var offset = tableStart + i * RecordLength;
            var x = ReadWord(data, offset);
            var y = ReadWord(data, offset + 2);
            var owner = ReadWord(data, offset + 4);
            if (x >= WorldPrefix.MapWidth || y >= WorldPrefix.MapHeight || owner > 15)
                throw new InvalidDataException($"Army {i} has invalid coordinates or owner code.");

            var units = new List<ArmyUnit>();
            for (var slot = 0; slot < UnitSlotsPerArmy; slot++)
            {
                var unitOffset = offset + HeaderLength + slot * UnitSlotLength;
                var troops = ReadWord(data, unitOffset + 4);
                if (troops == 0) continue;
                var type = ReadWord(data, unitOffset + 2);
                var quality = ReadWord(data, unitOffset + 6);
                var nameStart = unitOffset + 8;
                var nameEnd = Array.IndexOf(data, (byte)0, nameStart, 24);
                if (nameEnd <= nameStart || type > 4 || quality is < 5 or > 9)
                    throw new InvalidDataException($"Army {i}, unit slot {slot} has an unsupported active-unit layout.");
                for (var p = nameStart; p < nameEnd; p++)
                    if (data[p] < 0x20 || data[p] > 0x7e)
                        throw new InvalidDataException($"Army {i}, unit slot {slot} has a non-ASCII name.");
                var name = Encoding.ASCII.GetString(data, nameStart, nameEnd - nameStart).Trim();
                units.Add(new ArmyUnit(slot, name, type, troops, quality));
            }
            armies[i] = new ArmyRecord(i, x, y, owner, ReadWord(data, offset + 6),
                ReadWord(data, offset + 8), ReadWord(data, offset + 10),
                ReadWord(data, offset + 12), units.ToArray());
        }
        return new SaveArmyTable(armies);
    }

    private static ushort ReadWord(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
}

public sealed class ArmyRecord
{
    internal ArmyRecord(int index, ushort x, ushort y, ushort ownerCode, ushort moves,
        ushort moraleValue, ushort supplies, ushort money, ArmyUnit[] units)
    {
        Index = index;
        X = x;
        Y = y;
        OwnerCode = ownerCode;
        Moves = moves;
        MoraleValue = moraleValue;
        Supplies = supplies;
        Money = money;
        Units = units;
        foreach (var unit in units) TotalTroops += unit.Troops;
    }

    public int Index { get; }
    public ushort X { get; }
    public ushort Y { get; }
    public ushort OwnerCode { get; }
    public ushort Moves { get; }
    public ushort MoraleValue { get; }
    public ushort Supplies { get; }
    public ushort Money { get; }
    public IReadOnlyList<ArmyUnit> Units { get; }
    public int TotalTroops { get; }
}

public sealed class ArmyUnit
{
    internal ArmyUnit(int slot, string name, ushort typeCode, ushort troops, ushort qualityCode)
    {
        Slot = slot;
        Name = name;
        TypeCode = typeCode;
        Troops = troops;
        QualityCode = qualityCode;
    }

    public int Slot { get; }
    public string Name { get; }
    public ushort TypeCode { get; }
    public ushort Troops { get; }
    public ushort QualityCode { get; }
}
