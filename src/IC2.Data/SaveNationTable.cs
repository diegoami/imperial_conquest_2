using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IC2.Data;

/// <summary>The 16 fixed-size nation records observed in known SAV files.</summary>
public sealed class SaveNationTable
{
    private SaveNationTable(NationRecord[] nations) => Nations = nations;

    public IReadOnlyList<NationRecord> Nations { get; }

    public const ushort NoCapitalSentinel = 0xFFFF;

    public static SaveNationTable Parse(byte[] data)
    {
        var start = SaveNationLayout.Locate(data);
        var nations = new NationRecord[SaveNationLayout.NationCount];
        for (var i = 0; i < nations.Length; i++)
        {
            var offset = start + i * SaveNationLayout.NationRecordLength;
            var name = ReadName(data, offset, 11);
            var leader = ReadName(data, offset + 11, 34);
            if (name != NationCatalog.Name((ushort)i))
                throw new InvalidDataException($"Nation record {i} has unexpected name {name}.");
            var capitalCity = ReadWord(data, offset + 0x444);
            var cities = ReadWord(data, offset + 0x446);
            var human = ReadWord(data, offset + 0x490);
            // 0xFFFF marks an eliminated nation (no capital left); confirmed against Galatia's
            // elimination by Seleucid in https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/galatia-elimination-confirmed.md.
            if ((capitalCity >= WorldPrefix.CityCount && capitalCity != NoCapitalSentinel) ||
                cities > WorldPrefix.CityCount || human > 1)
                throw new InvalidDataException($"Nation record {i} has invalid capital, city count, or player flag.");
            nations[i] = new NationRecord((ushort)i, name, leader,
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 0x438, 4)),
                ReadWord(data, offset + 0x440), ReadWord(data, offset + 0x442),
                capitalCity, cities, ReadWord(data, offset + 0x44A), human == 1);
        }
        return new SaveNationTable(nations);
    }

    private static ushort ReadWord(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));

    private static string ReadName(byte[] data, int offset, int length)
    {
        var end = Array.IndexOf(data, (byte)0, offset, length);
        if (end <= offset) throw new InvalidDataException($"Missing nation text at {offset:X}.");
        for (var p = offset; p < end; p++)
            if (data[p] < 0x20 || data[p] > 0x7e)
                throw new InvalidDataException($"Non-ASCII nation text at {p:X}.");
        return Encoding.ASCII.GetString(data, offset, end - offset).Trim();
    }
}

public sealed class NationRecord
{
    internal NationRecord(ushort code, string name, string leader, int treasury, ushort unityValue,
        ushort mobilizedPercent, ushort capitalCityIndex, ushort cityCount, ushort taxRatePercent, bool humanPlayer)
    {
        Code = code;
        Name = name;
        Leader = leader;
        Treasury = treasury;
        UnityValue = unityValue;
        MobilizedPercent = mobilizedPercent;
        CapitalCityIndex = capitalCityIndex;
        CityCount = cityCount;
        TaxRatePercent = taxRatePercent;
        HumanPlayer = humanPlayer;
    }

    public ushort Code { get; }
    public string Name { get; }
    public string Leader { get; }
    public int Treasury { get; }
    public ushort UnityValue { get; }
    public ushort MobilizedPercent { get; }
    public ushort CapitalCityIndex { get; }
    public ushort CityCount { get; }
    public ushort TaxRatePercent { get; }
    public bool HumanPlayer { get; }

    /// <summary>True once a nation has lost its last city (capital reads the 0xFFFF sentinel).</summary>
    public bool IsEliminated => CapitalCityIndex == SaveNationTable.NoCapitalSentinel;
}

internal static class SaveNationLayout
{
    internal const int NationCount = 16;
    internal const int NationRecordLength = 1172;
    internal const int FleetRecordLength = 26;
    internal const int RecruitmentOffset = 0x2E4;

    internal static int Locate(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (data.Length < WorldPrefix.SharedPrefixLength + 2)
            throw new InvalidDataException("Save ends before the army count.");
        var armyCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(WorldPrefix.SharedPrefixLength, 2));
        var fleetCountOffset = WorldPrefix.SharedPrefixLength + 2 + armyCount * SaveArmyTable.RecordLength;
        if (fleetCountOffset + 2 > data.Length)
            throw new InvalidDataException("Save ends before the fleet count.");
        // Formula confirmed for fleetCount 2 and 3: the nation table's leading name always matched
        // NationCatalog.Name(0) at the computed offset (see https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/rome-tax-increase-and-sidon-capture.md).
        var fleetCount = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(fleetCountOffset, 2));
        if (fleetCount > WorldPrefix.CityCount)
            throw new InvalidDataException($"Implausible fleet count {fleetCount}.");
        var start = fleetCountOffset + 2 + fleetCount * FleetRecordLength;
        if (start + NationCount * NationRecordLength > data.Length)
            throw new InvalidDataException("Save ends before the complete nation table.");
        return start;
    }
}
