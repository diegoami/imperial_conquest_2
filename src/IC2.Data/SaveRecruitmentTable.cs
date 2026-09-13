using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace IC2.Data;

/// <summary>The 40 city-unit slots embedded in each nation record in known SAV files; shown in the recruitment dialog.</summary>
public sealed class SaveRecruitmentTable
{
    public const int SlotCount = 40;
    public const int SlotLength = 8;

    private SaveRecruitmentTable(RecruitmentEntry[] entries) => Entries = entries;

    public IReadOnlyList<RecruitmentEntry> Entries { get; }

    /// <summary>Sum of unit troop counts assigned to a city, matching the number beside fortification in sampled city panels.</summary>
    public int TroopsAtCity(int cityIndex)
    {
        if ((uint)cityIndex >= WorldPrefix.CityCount) throw new ArgumentOutOfRangeException(nameof(cityIndex));
        var total = 0;
        foreach (var entry in Entries)
            if (entry.CityIndex == cityIndex) total += entry.Troops;
        return total;
    }

    public static SaveRecruitmentTable Parse(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));

        int nationTableStart, nationRecordLength, recruitmentOffset;
        if (SaveFormat.Detect(data) == SaveFileFormat.Dat)
        {
            // The recruitment queue is embedded in each nation record in the DAT too, just at that
            // format's own record length and offset. See DatLayout and docs/investigations/dat-file-layout.md.
            nationTableStart = DatLayout.NationTableStart;
            nationRecordLength = DatLayout.NationRecordLength;
            recruitmentOffset = DatLayout.NationRecruitmentOffset;
        }
        else
        {
            nationTableStart = SaveNationLayout.Locate(data);
            nationRecordLength = SaveNationLayout.NationRecordLength;
            recruitmentOffset = SaveNationLayout.RecruitmentOffset;
        }

        var entries = new List<RecruitmentEntry>();
        for (ushort nation = 0; nation < SaveNationLayout.NationCount; nation++)
        {
            var nationStart = nationTableStart + nation * nationRecordLength;
            for (var slot = 0; slot < SlotCount; slot++)
            {
                var offset = nationStart + recruitmentOffset + slot * SlotLength;
                var amount = ReadWord(data, offset + 4);
                if (amount == 0) continue;
                var state = ReadWord(data, offset);
                var type = ReadWord(data, offset + 2);
                var cityIndex = ReadWord(data, offset + 6);
                if (type > 4 || cityIndex >= WorldPrefix.CityCount)
                    throw new InvalidDataException($"Nation {nation}, recruitment slot {slot} has an unsupported type or city index.");
                entries.Add(new RecruitmentEntry(nation, slot, state, type, amount, cityIndex));
            }
        }
        return new SaveRecruitmentTable(entries.ToArray());
    }

    private static ushort ReadWord(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
}

public sealed class RecruitmentEntry
{
    internal RecruitmentEntry(ushort nationCode, int slot, ushort stateCode, ushort typeCode, ushort troops, ushort cityIndex)
    {
        NationCode = nationCode;
        Slot = slot;
        StateCode = stateCode;
        TypeCode = typeCode;
        Troops = troops;
        CityIndex = cityIndex;
    }

    public ushort NationCode { get; }
    public int Slot { get; }
    public ushort StateCode { get; }
    public ushort TypeCode { get; }
    public ushort Troops { get; }
    public ushort CityIndex { get; }
}
