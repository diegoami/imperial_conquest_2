using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace IC2.Data;

/// <summary>
/// The 12-byte mercenary-offer records at the start of the region between the end of the nation table and
/// the 55-byte turn trailer. That region is exactly 3,042 bytes in every save sampled so far, but only its
/// first 600 bytes (50 records) hold plausible data (map-range coordinates, type 0-4, quality 0/5-9); record
/// 50 onward is implausible in every save checked (e.g. y-coordinates and type/quality codes far outside any
/// valid range), so the mercenary table's fixed capacity is 50 slots, not the whole 3,042-byte region. The
/// remaining ~2,442 bytes are a distinct, still-unidentified structure — see docs/roadmap.md.
///
/// Record layout and the troops-become-0xFFFF-on-hire behavior are confirmed by a controlled pair: in
/// `1_rome_270_winter_1.sav`, record 33 read (x=98, y=31, label=11, type=0, troops=6438, quality=8) —
/// exactly Felsina's coordinates, and exactly the troop count and quality ("very good") the user reported
/// hiring as mercenaries there. In `1_rome_270_winter_3.sav`, taken immediately after that hire, the same
/// record is unchanged except troops = 0xFFFF (65535), a sentinel marking the offer consumed/empty.
/// See docs/reports/mercenary-pool-record.md. `Label` does not match the hiring nation's own code (Gaul is
/// nation code 6, not 11) and is otherwise unidentified — likely a separate, larger ethnicity/flavor catalog,
/// consistent with the older Alexandria case (docs/reports/city-units-army-transfer-and-mercenaries.md) where
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
