using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

    /// <summary>The relation-row lower bound: the most negative cooldown any documented writer
    /// produces. <c>FUN_00449B40</c>'s "setting state = 0 is translated into a cooldown instead, by
    /// the previous state" table writes trade(1)→−8, alliance(2)→−24, war(3)→−18 — so −24 is the
    /// floor, never the corpus minimum (99 corpus saves + the DAT bottom out at −18). See
    /// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-diplomacy-peace-terms-and-instant-battles.md
    /// §"The relation matrix".</summary>
    public const short MinRelationValue = -24;

    /// <summary>The relation-row upper bound: 3 = war, the highest of the four documented states
    /// (0 peace, 1 trade, 2 alliance, 3 war). Same report as <see cref="MinRelationValue"/>.</summary>
    public const short MaxRelationValue = 3;

    public static SaveNationTable Parse(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        var table = SaveFormat.Detect(data) == SaveFileFormat.Dat ? ParseDat(data) : ParseSav(data);
        ValidateRelations(table.Nations);
        return table;
    }

    private static SaveNationTable ParseSav(byte[] data)
    {
        var start = SaveNationLayout.Locate(data);
        var nations = new NationRecord[SaveNationLayout.NationCount];
        for (var i = 0; i < nations.Length; i++)
        {
            var offset = start + i * SaveNationLayout.NationRecordLength;
            var name = ReadName(data, offset, 11);
            // 27 bytes (+0x0B .. +0x25 inclusive), not 34: the relation row below starts at +0x26,
            // and reading past it (the old 34-byte read) let leader text run into relation bytes.
            // Confirmed on 1_rome_270_summer_7.sav — see DatLayout's relation-row remarks and
            // https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-diplomacy-peace-terms-and-instant-battles.md
            // §"The relation matrix", 2026-09-24 addition. Unlike ReadName, a 27-byte leader with no
            // NUL at all is not treated as an error here: this is a defensive read of a field this
            // parser does not otherwise bound-check byte for byte (see ReadLeader's own remarks) —
            // not a DAT property. Measured directly on the DAT's leader-pool bytes at 0x2089A (T73
            // review round 1, B2): every one of the 192 candidates is in fact NUL-terminated well
            // within 27 bytes (longest: 21 characters).
            var leader = ReadLeader(data, offset + 11, 27);
            var relations = ReadRelationRow(data, offset + 0x26);
            // T85: the 16-bit neighbour mask, runtime/SAV nation-record offset +0x46 -- the byte
            // immediately after the 32-byte relation row at +0x26, the same offset the DAT loader
            // (FUN_004481A0) and the SAV loader (FUN_004487C4) both write it to. See
            // DatLayout.NationNeighbourOffset's own remarks for the DAT-side derivation.
            var neighbourMask = ReadWord(data, offset + 0x46);
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
            // Wealth (+0x430, 4 bytes) and tax base (+0x44c, signed 16-bit) — confirmed in
            // https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/nation-tax-base-and-city-economy-fields.md.
            var wealth = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 0x430, 4));
            var taxBase = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset + 0x44C, 2));
            nations[i] = new NationRecord((ushort)i, name, leader,
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + 0x438, 4)),
                ReadWord(data, offset + 0x440), ReadWord(data, offset + 0x442),
                capitalCity, cities, ReadWord(data, offset + 0x44A), wealth, taxBase,
                humanPlayer: human == 1, source: SaveFileFormat.Sav, relations: relations,
                neighbourMask: neighbourMask);
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
            var wealth = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + DatLayout.NationWealthOffset, 4));
            var taxBase = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset + DatLayout.NationTaxBaseOffset, 2));
            var relations = ReadRelationRow(data, offset + DatLayout.NationRelationOffset);
            // T85: the 16-bit neighbour mask, DatLayout.NationNeighbourOffset (+0x2B).
            var neighbourMask = ReadWord(data, offset + DatLayout.NationNeighbourOffset);
            nations[i] = new NationRecord((ushort)i, name, leader: null,
                BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset + DatLayout.NationTreasuryOffset, 4)),
                ReadWord(data, offset + DatLayout.NationUnityOffset),
                ReadWord(data, offset + DatLayout.NationMobilizedOffset),
                capitalCity, cities, ReadWord(data, offset + DatLayout.NationTaxOffset), wealth, taxBase,
                humanPlayer: null, source: SaveFileFormat.Dat, relations: relations,
                neighbourMask: neighbourMask);
        }
        return new SaveNationTable(nations);
    }

    /// <summary>Validates the relation matrix across every nation in <paramref name="nations"/>:
    /// each nation's own diagonal entry is 0, every entry is within
    /// [<see cref="MinRelationValue"/>, <see cref="MaxRelationValue"/>], and the matrix is symmetric
    /// — <c>FUN_00449B40</c> is the single setter and writes both <c>[a][b]</c> and <c>[b][a]</c>, so
    /// it is symmetric by construction. See
    /// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-diplomacy-peace-terms-and-instant-battles.md
    /// §"The relation matrix".</summary>
    private static void ValidateRelations(IReadOnlyList<NationRecord> nations)
    {
        for (var a = 0; a < nations.Count; a++)
        {
            var row = nations[a].Relations;
            if (row[a] != 0)
                throw new InvalidDataException($"Nation {a}'s relation diagonal entry is {row[a]}, not 0.");
            for (var b = 0; b < row.Count; b++)
                if (row[b] < MinRelationValue || row[b] > MaxRelationValue)
                    throw new InvalidDataException(
                        $"Nation {a}'s relation toward nation {b} is {row[b]}, outside " +
                        $"[{MinRelationValue}, {MaxRelationValue}].");
        }
        for (var a = 0; a < nations.Count; a++)
            for (var b = a + 1; b < nations.Count; b++)
                if (nations[a].Relations[b] != nations[b].Relations[a])
                    throw new InvalidDataException(
                        $"Relation matrix is not symmetric: [{a}][{b}] = {nations[a].Relations[b]} but " +
                        $"[{b}][{a}] = {nations[b].Relations[a]}.");
    }

    private static short[] ReadRelationRow(byte[] data, int offset)
    {
        var row = new short[16];
        for (var i = 0; i < row.Length; i++)
            row[i] = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(offset + i * 2, 2));
        return row;
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

    /// <summary>Reads the leader field: up to <paramref name="length"/> bytes, NUL-terminated if a
    /// NUL appears within it, otherwise the whole <paramref name="length"/> bytes verbatim — unlike
    /// <see cref="ReadName"/>, a leader that fills its field exactly with no NUL is not treated as an
    /// error (Done-when line 2). This is a defensive allowance in this parser, not a documented DAT
    /// property. The pool itself (16 nations × 12 candidates, 26 bytes each, <c>strcpy(record +
    /// 0x0b, leaderPool + i * 0x1a)</c>) is documented in docs/investigations/dat-file-layout.md;
    /// that a 26-byte slot cannot itself produce 27 non-NUL bytes, and that every one of the 192
    /// candidates is in fact NUL-terminated (longest: 21 characters), was measured directly on the
    /// DAT's leader-pool bytes at 0x2089A (T73 review round 1, B2/N7), not read from that document.
    /// An empty leader (a NUL at <paramref name="offset"/> itself) is still rejected, exactly as
    /// <see cref="ReadName"/> rejects an empty name — see
    /// <c>NationRelationTests.An_empty_leader_is_rejected</c>.</summary>
    private static string ReadLeader(byte[] data, int offset, int length)
    {
        var nul = Array.IndexOf(data, (byte)0, offset, length);
        var end = nul >= 0 ? nul : offset + length;
        if (end <= offset) throw new InvalidDataException($"Missing nation text at {offset:X}.");
        for (var p = offset; p < end; p++)
            if (data[p] < 0x20 || data[p] > 0x7e)
                throw new InvalidDataException($"Non-ASCII leader text at {p:X}.");
        return Encoding.ASCII.GetString(data, offset, end - offset).Trim();
    }
}

public sealed class NationRecord
{
    private readonly bool? _humanPlayer;

    internal NationRecord(ushort code, string name, string? leader, int treasury, ushort unityValue,
        ushort mobilizedPercent, ushort capitalCityIndex, ushort cityCount, ushort taxRatePercent,
        int wealth, short taxBase, bool? humanPlayer, SaveFileFormat source, short[] relations,
        ushort neighbourMask)
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
        Wealth = wealth;
        TaxBase = taxBase;
        _humanPlayer = humanPlayer;
        Source = source;
        // Array.AsReadOnly, not the array itself cast to IReadOnlyList<short>: a caller that casts
        // the interface back to short[] must not be able to reach (and mutate) the backing array —
        // N6, T73 review round 1.
        Relations = Array.AsReadOnly(relations);
        NeighbourMask = neighbourMask;
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

    /// <summary>The nation's 16-entry signed relation row toward every nation, indexed by nation
    /// code (SAV <c>+0x26</c>; DAT <see cref="DatLayout.NationRelationOffset"/>, <c>+0x0B</c>):
    /// 0 peace, 1 trade, 2 alliance, 3 war, and a negative value is peace with a cooldown counting
    /// up toward 0. Present on both a SAV and a DAT record — the DAT loader reads it straight into
    /// the same runtime offset, and new-game init never overwrites it, so the DAT's own matrix is
    /// the original's starting relations. <c>Relations[Code]</c> is always 0 (a nation's own
    /// diagonal entry), and the full table's matrix is symmetric by construction. See
    /// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-diplomacy-peace-terms-and-instant-battles.md
    /// §"The relation matrix" and its 2026-09-24 addition.</summary>
    public IReadOnlyList<short> Relations { get; }

    /// <summary>The nation's 16-bit neighbour mask toward every nation, indexed by bit position ==
    /// nation code (SAV runtime <c>+0x46</c>; DAT <see cref="DatLayout.NationNeighbourOffset"/>,
    /// <c>+0x2B</c>): bit <c>j</c> set means this nation and nation <c>j</c> border each other.
    /// Present on both a SAV and a DAT record -- the DAT loader reads it straight into the same
    /// runtime offset, and (T85's own scope) nothing but the conquest merge <c>FUN_0044C528</c>
    /// rewrites it during play, so the DAT's own mask is the original's starting neighbour set. This
    /// parser reads the raw word only: symmetry, the no-self-bit rule and the 24-pair shape are
    /// checked where the DAT-backed tests exercise them
    /// (<c>tests/IC2.Data.Tests/NationNeighbourMaskTests.cs</c>), not enforced here, mirroring how
    /// this same type reads <see cref="Relations"/> without validating it beyond <c>Parse</c>'s own
    /// <see cref="MinRelationValue"/>/<see cref="MaxRelationValue"/>/diagonal/symmetry checks -- this
    /// task's Owns list is "only reading that field". See
    /// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/dat-neighbour-mask.md
    /// §1-§2.</summary>
    public ushort NeighbourMask { get; }

    public int Treasury { get; }
    public ushort UnityValue { get; }
    public ushort MobilizedPercent { get; }
    public ushort CapitalCityIndex { get; }
    public ushort CityCount { get; }
    public ushort TaxRatePercent { get; }

    /// <summary>The nation's wealth pool — <c>Σ (owned city population thousands) × 3000</c>, rebuilt
    /// to zero and re-summed every quarterly tick and adjusted by city captures in between (word +4
    /// at SAV/DAT nation offset <c>+0x430</c> / <c>+0x409</c>). Subtracted (<c>÷ 20000</c>) from the
    /// quarterly treasury credit. See
    /// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/nation-tax-base-and-city-economy-fields.md.</summary>
    public int Wealth { get; }

    /// <summary>The nation's tax base — <c>Σ over owned cities (tribute × population / maxPopulation) &lt;&lt; 2</c>,
    /// rebuilt every quarterly tick and adjusted by city captures in between (a signed 16-bit word at
    /// SAV/DAT nation offset <c>+0x44c</c> / <c>+0x41b</c>). Drives the quarterly treasury credit
    /// (<c>taxBase × taxRate / 100 + taxBase / 4</c>), trade/alliance income, and the reparations
    /// formula. See
    /// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/nation-tax-base-and-city-economy-fields.md.</summary>
    public short TaxBase { get; }

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
