using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IC2.Data;

/// <summary>The fixed-size army table observed after the city table in known SAV and DAT files.</summary>
public sealed class SaveArmyTable
{
    public const int RecordLength = 656;
    /// <summary>16 bytes: X(+0) Y(+2) Owner(+4) Moves(+6) CoveredCell(+8) Supplies(+10) Money(+12) Morale(+14).
    /// Every field is now decompiled; no header byte is padding. See
    /// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-unit-map-orders-and-record-fields.md.</summary>
    public const int HeaderLength = 16;
    public const int UnitSlotLength = 32;
    public const int UnitSlotsPerArmy = 20;

    private SaveArmyTable(ArmyRecord[] armies, SkippedArmyRecord[] skippedRecords)
    {
        Armies = armies;
        SkippedRecords = skippedRecords;
    }

    public IReadOnlyList<ArmyRecord> Armies { get; }

    /// <summary>Records whose owner word was the <see cref="ArmyRecord.TombstoneOwnerSentinel"/>
    /// (0xFFFF) — an army slot merged or eliminated during the turn and not yet compacted. These are
    /// deliberately excluded from <see cref="Armies"/> rather than returned as degenerate
    /// owner-65535 entries; a caller can tell "clean parse" from "parse with tombstones" from this
    /// list without re-reading bytes. See docs/investigations/thracia-supply-morale.md and
    /// docs/investigations/dat-file-layout.md.</summary>
    public IReadOnlyList<SkippedArmyRecord> SkippedRecords { get; }

    public static SaveArmyTable Parse(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));

        int tableStart;
        int count;
        if (SaveFormat.Detect(data) == SaveFileFormat.Dat)
        {
            tableStart = DatLayout.ArmyTableStart;
            count = DatLayout.ArmyRecordCount;
            if (tableStart + count * RecordLength > data.Length)
                throw new InvalidDataException("DAT ends before the complete fixed-size army table.");
        }
        else
        {
            if (data.Length < WorldPrefix.SharedPrefixLength + 2)
                throw new InvalidDataException("Save ends before the army count.");
            count = ReadWord(data, WorldPrefix.SharedPrefixLength);
            tableStart = WorldPrefix.SharedPrefixLength + 2;
            if (count > (data.Length - tableStart) / RecordLength)
                throw new InvalidDataException($"Army count {count} exceeds the available fixed-size records.");
        }

        var armies = new List<ArmyRecord>(count);
        var skipped = new List<SkippedArmyRecord>();
        for (var i = 0; i < count; i++)
        {
            var offset = tableStart + i * RecordLength;
            var x = ReadWord(data, offset);
            var y = ReadWord(data, offset + 2);
            var owner = ReadWord(data, offset + 4);

            // 0xFFFF is the established no-owner tombstone sentinel (also special-cased for the
            // *capital* field in SaveNationTable): an army slot merged or eliminated during the turn
            // and not yet compacted. Confirmed on 3 of 51 saves, always with otherwise-valid
            // coordinates, in docs/investigations/thracia-supply-morale.md and counted properly
            // across the whole corpus in docs/investigations/dat-file-layout.md. Skip it — do not
            // validate its other fields, since they describe a slot that is not really an army — and
            // keep parsing the rest of the file. Every OTHER out-of-range owner is still a genuine
            // parse failure; this is a specific sentinel, not a widened range.
            if (owner == ArmyRecord.TombstoneOwnerSentinel)
            {
                skipped.Add(new SkippedArmyRecord(i, x, y));
                continue;
            }

            if (x >= WorldPrefix.MapWidth)
                throw new InvalidDataException($"Army {i} has out-of-range X coordinate {x}.");
            if (y >= WorldPrefix.MapHeight)
                throw new InvalidDataException($"Army {i} has out-of-range Y coordinate {y}.");
            if (owner > 15)
                throw new InvalidDataException($"Army {i} has out-of-range owner code {owner}.");

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
                if (nameEnd <= nameStart)
                    throw new InvalidDataException($"Army {i}, unit slot {slot} is missing a nonempty unit name.");
                if (type > 4)
                    throw new InvalidDataException($"Army {i}, unit slot {slot} has out-of-range type code {type}.");
                if (quality is < 5 or > 9)
                    throw new InvalidDataException($"Army {i}, unit slot {slot} has out-of-range quality code {quality}.");
                for (var p = nameStart; p < nameEnd; p++)
                    if (data[p] < 0x20 || data[p] > 0x7e)
                        throw new InvalidDataException($"Army {i}, unit slot {slot} has a non-ASCII name.");
                var name = Encoding.ASCII.GetString(data, nameStart, nameEnd - nameStart).Trim();
                units.Add(new ArmyUnit(slot, name, type, troops, quality, ReadWord(data, unitOffset)));
            }
            armies.Add(new ArmyRecord(i, x, y, owner,
                moves: ReadWord(data, offset + 6),
                coveredCell: ReadWord(data, offset + 8),
                supplies: ReadWord(data, offset + 10),
                money: ReadWord(data, offset + 12),
                morale: ReadWord(data, offset + 14),
                units: units.ToArray()));
        }
        return new SaveArmyTable(armies.ToArray(), skipped.ToArray());
    }

    private static ushort ReadWord(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
}

/// <summary>A skipped army-table record: the tombstone sentinel was found in the owner word, so the
/// rest of the 656-byte record was never interpreted. See <see cref="SaveArmyTable.SkippedRecords"/>.</summary>
public sealed class SkippedArmyRecord
{
    internal SkippedArmyRecord(int index, ushort x, ushort y)
    {
        Index = index;
        X = x;
        Y = y;
    }

    /// <summary>The record's position (0-based) in the army table.</summary>
    public int Index { get; }

    /// <summary>Map X at +0 — read for diagnostics even though the record is a tombstone; every
    /// observed tombstone has otherwise-valid coordinates.</summary>
    public ushort X { get; }

    /// <summary>Map Y at +2. See <see cref="X"/>.</summary>
    public ushort Y { get; }
}

public sealed class ArmyRecord
{
    /// <summary>Value of <see cref="CoveredCell"/> when the army is aboard a fleet and therefore
    /// occupies no map cell of its own.</summary>
    public const ushort AboardFleetSentinel = 0xFFFF;

    /// <summary>Value of the owner word (+4) that marks a tombstoned record rather than a real army —
    /// see <see cref="SaveArmyTable.SkippedRecords"/>. Numerically the same 0xFFFF bit pattern as
    /// <see cref="AboardFleetSentinel"/>, but a different field with a different meaning; kept as a
    /// separate constant so the two are never conflated.</summary>
    public const ushort TombstoneOwnerSentinel = 0xFFFF;

    internal ArmyRecord(int index, ushort x, ushort y, ushort ownerCode, ushort moves,
        ushort coveredCell, ushort supplies, ushort money, ushort morale, ArmyUnit[] units)
    {
        Index = index;
        X = x;
        Y = y;
        OwnerCode = ownerCode;
        Moves = moves;
        CoveredCell = coveredCell;
        Supplies = supplies;
        Money = money;
        Morale = morale;
        Units = units;
        foreach (var unit in units) TotalTroops += unit.Troops;
    }

    public int Index { get; }
    public ushort X { get; }
    public ushort Y { get; }
    public ushort OwnerCode { get; }
    public ushort Moves { get; }

    /// <summary>Word +8: the map cell value this army's marker is covering, saved so it can be restored
    /// when the army moves or is removed — NOT a morale value, as this field was labelled until the
    /// movement/creation/removal code was decompiled. It is also what the army-information panel prints
    /// as "Terrain". <see cref="AboardFleetSentinel"/> means the army is embarked on a fleet.
    /// See https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-unit-map-orders-and-record-fields.md.</summary>
    public ushort CoveredCell { get; }

    /// <summary>True when <see cref="CoveredCell"/> is the aboard-a-fleet sentinel. Such an army has no
    /// marker on the map and its coordinates track the carrying fleet's.</summary>
    public bool IsAboardFleet => CoveredCell == AboardFleetSentinel;

    public ushort Supplies { get; }
    public ushort Money { get; }

    /// <summary>Word +14: the army's morale, printed on the army-information panel as a tier via
    /// <c>moraleNames[(v - 51) &gt;&gt; 2]</c> (falling back to <c>v - 48</c> below 51), used to seed each
    /// unit's starting battle morale, and a direct multiplier in both army-strength formulas. Previously
    /// treated as unused padding, then as an unidentified "army experience" field.</summary>
    public ushort Morale { get; }

    public IReadOnlyList<ArmyUnit> Units { get; }
    public int TotalTroops { get; }

    /// <summary>Supply capacity in tons, <c>troops / 100</c> — the cap the supply-purchase dialog enforces,
    /// and the denominator behind the panel's supply percentage (<c>supplies * 10000 / troops</c>).</summary>
    public int SupplyCapacityTons => TotalTroops / 100;

    /// <summary>Supply as the whole percentage the original's army panel displays, or 0 for an empty army.</summary>
    public int SupplyPercent => TotalTroops == 0 ? 0 : Supplies * 10000 / TotalTroops;
}

public sealed class ArmyUnit
{
    internal ArmyUnit(int slot, string name, ushort typeCode, ushort troops, ushort qualityCode,
        ushort mercenaryLabel)
    {
        Slot = slot;
        Name = name;
        TypeCode = typeCode;
        Troops = troops;
        QualityCode = qualityCode;
        MercenaryLabel = mercenaryLabel;
    }

    public int Slot { get; }
    public string Name { get; }
    public ushort TypeCode { get; }
    public ushort Troops { get; }
    public ushort QualityCode { get; }

    /// <summary>Word +0 of the unit slot: 0 for a regular unit, otherwise the mercenary name-table index
    /// copied from the pool record's Label when the unit was hired. It selects which of the two quarterly
    /// upkeep formulas applies and blocks the unit from being merged with regulars.</summary>
    public ushort MercenaryLabel { get; }

    /// <summary>True when this unit was hired from the mercenary pool rather than recruited.</summary>
    public bool IsMercenary => MercenaryLabel != 0;
}
