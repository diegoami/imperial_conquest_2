using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IC2.Data;

/// <summary>The 16 fixed-size nation records observed in known SAV files, and at their own fixed
/// offset and shorter record length in the DAT.</summary>
public sealed class SaveNationTable
{
    private SaveNationTable(NationRecord[] nations) => Nations = nations;

    public IReadOnlyList<NationRecord> Nations { get; }

    public const ushort NoCapitalSentinel = 0xFFFF;

    public static SaveNationTable Parse(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        return SaveFormat.Detect(data) == SaveFileFormat.Dat ? ParseDat(data) : ParseSav(data);
    }

    private static SaveNationTable ParseSav(byte[] data)
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
                capitalCity, cities, ReadWord(data, offset + 0x44A), humanPlayer: human == 1,
                source: SaveFileFormat.Sav);
        }
        return new SaveNationTable(nations);
    }

    /// <summary>Reads the DAT's own 1,055-byte nation record shape: everything <see cref="ParseSav"/>
    /// reads except <c>Leader</c> and <c>HumanPlayer</c>, which the DAT genuinely does not store — see
    /// <see cref="NationRecord.Leader"/> and <see cref="NationRecord.HumanPlayer"/>. Offsets are cited
    /// in <see cref="DatLayout"/>. See docs/investigations/dat-file-layout.md.</summary>
    private static SaveNationTable ParseDat(byte[] data)
    {
        var nations = new NationRecord[SaveNationLayout.NationCount];
        for (var i = 0; i < nations.Length; i++)
        {
            var offset = DatLayout.NationTableStart + i * DatLayout.NationRecordLength;
            var name = ReadName(data, offset + DatLayout.NationNameOffset, DatLayout.NationNameLength);
            if (name != NationCatalog.Name((ushort)i))
                throw new InvalidDataException($"DAT nation record {i} has unexpected name {name}.");
            var capitalCity = ReadWord(data, offset + DatLayout.NationCapitalOffset);
            var cities = ReadWord(data, offset + DatLayout.NationCitiesOffset);
            if ((capitalCity >= WorldPrefix.CityCount && capitalCity != NoCapitalSentinel) ||
                cities > WorldPrefix.CityCount)
                throw new InvalidDataException($"DAT nation record {i} has invalid capital or city count.");
            nations[i] = new NationRecord((ushort)i, name, leader: null,
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + DatLayout.NationTreasuryOffset, 4)),
                ReadWord(data, offset + DatLayout.NationUnityOffset),
                ReadWord(data, offset + DatLayout.NationMobilizedOffset),
                capitalCity, cities, ReadWord(data, offset + DatLayout.NationTaxOffset),
                humanPlayer: null, source: SaveFileFormat.Dat);
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
    private readonly bool? _humanPlayer;

    internal NationRecord(ushort code, string name, string? leader, int treasury, ushort unityValue,
        ushort mobilizedPercent, ushort capitalCityIndex, ushort cityCount, ushort taxRatePercent,
        bool? humanPlayer, SaveFileFormat source)
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
        _humanPlayer = humanPlayer;
        Source = source;
    }

    public ushort Code { get; }
    public string Name { get; }

    /// <summary>The nation's leader name, or <c>null</c> when <see cref="Source"/> is
    /// <see cref="SaveFileFormat.Dat"/> — the DAT genuinely does not store it. In the original,
    /// <c>TPremierForm_NewGame</c>'s second helper (<c>FUN_00448aa4</c>) draws it at random from a
    /// 12-candidate-per-nation pool only once a New Game actually starts; it is not world data. See
    /// docs/investigations/dat-file-layout.md. Modelled as an explicit absence rather than an empty
    /// string, precisely so a caller cannot mistake "not stored" for "stored and blank".</summary>
    public string? Leader { get; }

    public int Treasury { get; }
    public ushort UnityValue { get; }
    public ushort MobilizedPercent { get; }
    public ushort CapitalCityIndex { get; }
    public ushort CityCount { get; }
    public ushort TaxRatePercent { get; }

    /// <summary>True if this is a human-controlled seat, false if AI-controlled. Throws
    /// <see cref="DatDataNotPresentException"/> when <see cref="Source"/> is
    /// <see cref="SaveFileFormat.Dat"/>: the DAT does not store this field either (the loader never
    /// reads record offset +0x490 at all), and <c>FUN_00448aa4</c> only ever sets it to 0 ("nobody is
    /// human yet") once a New Game starts — so there is no DAT-derived value that would not be a
    /// fabricated, plausible-looking default. See docs/investigations/dat-file-layout.md.</summary>
    public bool HumanPlayer => _humanPlayer ?? throw new DatDataNotPresentException(
        "HumanPlayer is not stored in the DAT. TPremierForm_NewGame's FUN_00448aa4 sets it only once " +
        "a New Game actually starts; see docs/investigations/dat-file-layout.md.");

    /// <summary>Which file shape this record was parsed from — an explicit marker so a caller can
    /// check before touching <see cref="Leader"/> or <see cref="HumanPlayer"/>, both of which are
    /// absent by construction on a <see cref="SaveFileFormat.Dat"/> record.</summary>
    public SaveFileFormat Source { get; }

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
