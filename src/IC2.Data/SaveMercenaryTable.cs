using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace IC2.Data;

/// <summary>
/// The 12-byte mercenary-offer records at the start of the region between the end of the nation table and
/// the 55-byte turn trailer. The fixed 50-slot capacity, first found empirically (only the first 600 of the
/// region's 3,042 bytes hold plausible data in every save checked), is now also confirmed directly from the
/// game's own save/load code: https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-sav-file-layout.md decompiled the exact read/write
/// function pair, which loops a hard-coded 50 times over 12-byte records at this position. The remaining
/// ~2,442 bytes hold a distinct, now partially-identified structure (a count-prefixed run of 61-byte
/// records, still unidentified) — see that report and docs/roadmap.md.
///
/// Record layout and the troops-become-0xFFFF-on-hire behavior are confirmed by a controlled pair: in
/// `1_rome_270_winter_1.sav`, record 33 read (x=98, y=31, label=11, type=0, troops=6438, quality=8) —
/// exactly Felsina's coordinates, and exactly the troop count and quality ("very good") the user reported
/// hiring as mercenaries there. In `1_rome_270_winter_3.sav`, taken immediately after that hire, the same
/// record is unchanged except troops = 0xFFFF (65535), a sentinel marking the offer consumed/empty.
/// See https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/mercenary-pool-record.md. `Label` does not match the hiring nation's own code (Gaul is
/// nation code 6, not 11) and is otherwise unidentified — likely a separate, larger ethnicity/flavor catalog,
/// consistent with the older Alexandria case (https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/city-units-army-transfer-and-mercenaries.md) where
/// label 35 similarly did not match Ptolemaic's nation code 3.
/// </summary>
public sealed class SaveMercenaryTable
{
    public const int RecordLength = 12;
    public const int RecordCount = 50;
    public const ushort EmptySentinel = 0xFFFF;

    private SaveMercenaryTable(MercenaryRecord[] records) => Records = records;

    public IReadOnlyList<MercenaryRecord> Records { get; }

    public static SaveMercenaryTable Parse(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (SaveFormat.Detect(data) == SaveFileFormat.Dat)
            throw new DatDataNotPresentException(
                "The DAT has no mercenary-offer pool. It is New Game state the original has not " +
                "created yet when the DAT is loaded (offers are generated turn by turn); there is no " +
                "DAT-derived value that would not be a fabricated 'empty pool'. See " +
                "docs/investigations/dat-file-layout.md.");

        var nationStart = SaveNationLayout.Locate(data);
        var start = nationStart + SaveNationLayout.NationCount * SaveNationLayout.NationRecordLength;
        var end = start + RecordCount * RecordLength;
        if (end + 55 > data.Length) throw new InvalidDataException("Save ends before the complete mercenary table and trailer.");

        var records = new MercenaryRecord[RecordCount];
        for (var i = 0; i < RecordCount; i++)
        {
            var offset = start + i * RecordLength;
            records[i] = new MercenaryRecord(
                i,
                ReadWord(data, offset),
                ReadWord(data, offset + 2),
                ReadWord(data, offset + 4),
                ReadWord(data, offset + 6),
                ReadWord(data, offset + 8),
                ReadWord(data, offset + 10));
        }
        return new SaveMercenaryTable(records);
    }

    private static ushort ReadWord(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
}

public sealed class MercenaryRecord
{
    internal MercenaryRecord(int index, ushort x, ushort y, ushort label, ushort typeCode, ushort troops, ushort qualityCode)
    {
        Index = index;
        X = x;
        Y = y;
        Label = label;
        TypeCode = typeCode;
        Troops = troops;
        QualityCode = qualityCode;
    }

    public int Index { get; }
    public ushort X { get; }
    public ushort Y { get; }

    /// <summary>Unidentified selector at +4; does not match the hiring/source nation's own code. Candidate ethnicity/flavor label.</summary>
    public ushort Label { get; }

    public ushort TypeCode { get; }

    /// <summary>Available troop count, or <see cref="SaveMercenaryTable.EmptySentinel"/> (0xFFFF) once hired/consumed.</summary>
    public ushort Troops { get; }

    public ushort QualityCode { get; }

    public bool IsEmpty => Troops == SaveMercenaryTable.EmptySentinel || Troops == 0;
}
