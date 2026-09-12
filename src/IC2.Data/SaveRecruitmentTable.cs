using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace IC2.Data;

/// <summary>The 40-slot recruitment section observed after the army table in known SAV files.</summary>
public sealed class SaveRecruitmentTable
{
    // The 794 intervening bytes include a two-fleet count, two 26-byte fleet records,
    // and other structures that are not yet mapped. All available saves have two fleets.
    public const int OffsetAfterArmyTable = 794;
    public const int SlotCount = 40;
    public const int SlotLength = 8;

    private SaveRecruitmentTable(RecruitmentEntry[] entries) => Entries = entries;

    public IReadOnlyList<RecruitmentEntry> Entries { get; }

    public static SaveRecruitmentTable Parse(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (data.Length < WorldPrefix.SharedPrefixLength + 2)
            throw new InvalidDataException("Save ends before the army count.");
        var armyCount = ReadWord(data, WorldPrefix.SharedPrefixLength);
        var postArmyStart = WorldPrefix.SharedPrefixLength + 2 + armyCount * SaveArmyTable.RecordLength;
        if (postArmyStart + OffsetAfterArmyTable + SlotCount * SlotLength > data.Length)
            throw new InvalidDataException("Save ends before the complete recruitment section.");
        if (postArmyStart + 2 > data.Length)
            throw new InvalidDataException("Save ends before the fleet count.");
        var fleetCount = ReadWord(data, postArmyStart);
        if (fleetCount != 2)
            throw new InvalidDataException($"Recruitment offset is validated only for saves with two fleets; got {fleetCount}.");
        var start = postArmyStart + OffsetAfterArmyTable;

        var entries = new List<RecruitmentEntry>();
        for (var slot = 0; slot < SlotCount; slot++)
        {
            var offset = start + slot * SlotLength;
            var amount = ReadWord(data, offset + 4);
            if (amount == 0) continue;
            var state = ReadWord(data, offset);
            var type = ReadWord(data, offset + 2);
            var cityIndex = ReadWord(data, offset + 6);
            if (type > 4 || cityIndex >= WorldPrefix.CityCount)
                throw new InvalidDataException($"Recruitment slot {slot} has an unsupported type or city index.");
            entries.Add(new RecruitmentEntry(slot, state, type, amount, cityIndex));
        }
        return new SaveRecruitmentTable(entries.ToArray());
    }

    private static ushort ReadWord(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
}

public sealed class RecruitmentEntry
{
    internal RecruitmentEntry(int slot, ushort stateCode, ushort typeCode, ushort troops, ushort cityIndex)
    {
        Slot = slot;
        StateCode = stateCode;
        TypeCode = typeCode;
        Troops = troops;
        CityIndex = cityIndex;
    }

    public int Slot { get; }
    public ushort StateCode { get; }
    public ushort TypeCode { get; }
    public ushort Troops { get; }
    public ushort CityIndex { get; }
}
