using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace IC2.Data;

/// <summary>The 26-byte fleet records observed between the army table and the nation table in known
/// SAV files, and at their own fixed offset in the DAT.</summary>
public sealed class SaveFleetTable
{
    public const int RecordLength = 26;

    /// <summary>Value of the owner word (+8) that marks a tombstoned record rather than a real fleet
    /// — see <see cref="SkippedRecords"/>. The same 0xFFFF bit pattern as
    /// <see cref="FleetRecord.LaunchedSentinel"/> and <see cref="FleetRecord.NoCarriedArmySentinel"/>,
    /// but a different field with a different meaning; kept as its own constant, mirroring
    /// <see cref="ArmyRecord.TombstoneOwnerSentinel"/>, so the three are never conflated.</summary>
    public const ushort TombstoneOwnerSentinel = 0xFFFF;

    private SaveFleetTable(FleetRecord[] fleets, SkippedFleetRecord[] skippedRecords)
    {
        Fleets = fleets;
        SkippedRecords = skippedRecords;
    }

    public IReadOnlyList<FleetRecord> Fleets { get; }

    /// <summary>Records whose owner word was <see cref="TombstoneOwnerSentinel"/> (0xFFFF) — a fleet
    /// absorbed into another during the turn and not yet compacted (bug #276): the absorbing fleet's
    /// ship count and money grow by exactly the absorbed fleet's own, and the absorbed record survives
    /// with every other field intact. Mirrors <see cref="SaveArmyTable.SkippedRecords"/>: deliberately
    /// excluded from <see cref="Fleets"/> rather than returned as a degenerate owner-65535 entry, and
    /// the rest of a skipped record is not validated — its fields describe a slot that is not really a
    /// fleet.</summary>
    public IReadOnlyList<SkippedFleetRecord> SkippedRecords { get; }

    public static SaveFleetTable Parse(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));

        int tableStart;
        int fleetCount;
        if (SaveFormat.Detect(data) == SaveFileFormat.Dat)
        {
            // DAT offset 0x1B0CC, 2 fixed records, no count word — see docs/investigations/dat-file-layout.md.
            tableStart = DatLayout.FleetTableStart;
            fleetCount = DatLayout.FleetRecordCount;
            if (tableStart + fleetCount * RecordLength > data.Length)
                throw new InvalidDataException("DAT ends before the complete fixed-size fleet table.");
        }
        else
        {
            // No structural guards here: SaveFormat.Detect already ran SaveNationLayout.Locate to
            // classify this file as SAV-shaped, and Locate re-derives this exact armyCount /
            // fleetCountOffset / fleetCount / tableStart chain with strictly tighter bounds checks
            // (it additionally requires the whole nation table to fit) — so these re-checks could
            // never fire and were unreachable dead code (T34 #40 item 2). A file that fails any of
            // them never reaches this branch: SaveFormat.Detect raises
            // UnrecognizedSaveFormatException for it first.
            var armyCount = ReadWord(data, WorldPrefix.SharedPrefixLength);
            var fleetCountOffset = WorldPrefix.SharedPrefixLength + 2 + armyCount * SaveArmyTable.RecordLength;

            fleetCount = ReadWord(data, fleetCountOffset);

            tableStart = fleetCountOffset + 2;
        }

        var fleets = new List<FleetRecord>(fleetCount);
        var skipped = new List<SkippedFleetRecord>();
        for (var i = 0; i < fleetCount; i++)
        {
            var offset = tableStart + i * RecordLength;

            // 0xFFFF is the fleet table's own no-owner tombstone sentinel — see SkippedRecords'
            // remarks and bug #276. Skip it — do not validate its other fields — and keep parsing the
            // rest of the table. Every fleet slot keeps its own original index; nothing is
            // renumbered, since T21's import maps these records by slot.
            var owner = ReadWord(data, offset + 8);
            if (owner == TombstoneOwnerSentinel)
            {
                var x = ReadWord(data, offset);
                var y = ReadWord(data, offset + 2);
                // #340 N1: +22 is CarriedArmyIndex on a live record (FleetRecord.CarriedArmyIndex); read
                // it here too, under the same NoCarriedArmySentinel rule, so the import can unlink a
                // surviving army this tombstoned fleet still claims (EmbarkationLinker).
                var carriedArmyWord = ReadWord(data, offset + 22);
                var carriedArmyIndex = carriedArmyWord == FleetRecord.NoCarriedArmySentinel
                    ? (ushort?)null
                    : carriedArmyWord;
                skipped.Add(new SkippedFleetRecord(i, x, y, carriedArmyIndex));
                continue;
            }

            var raw = new byte[RecordLength];
            Array.Copy(data, offset, raw, 0, RecordLength);
            fleets.Add(new FleetRecord(i, raw));
        }

        // Unlike SaveArmyTable's AllArmyRecordsTombstonedException, the fleet table has no
        // all-tombstoned guard. A fleet count of zero (or every fleet slot being empty) is the
        // ordinary case for most saves — plenty of nations simply have no fleets afloat on a given
        // turn — so "every present record is a tombstone" carries none of the army table's "this
        // table is suspiciously empty" signal: the corpus evidence backing that guard
        // (docs/investigations/dat-file-layout.md: a small minority of army records are ever
        // tombstoned in one save — bug #313 found a second case, IP012B.sav with two, out of hundreds
        // of records — so 100% tombstoned is still unambiguously abnormal) has no fleet-table
        // analogue, and a scan of the whole configured corpus after this fix finds no save whose
        // fleet table is 100% tombstoned while non-empty — see the PR body for the count. A table
        // that later turns up with only tombstones parses to zero fleets and zero surprises, exactly
        // like a table that legitimately has none.
        return new SaveFleetTable(fleets.ToArray(), skipped.ToArray());
    }

    private static ushort ReadWord(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
}

/// <summary>A skipped fleet-table record: the tombstone sentinel was found in the owner word, so the
/// rest of the 26-byte record was never interpreted. See <see cref="SaveFleetTable.SkippedRecords"/>.
/// Mirrors <see cref="SkippedArmyRecord"/>.</summary>
public sealed class SkippedFleetRecord
{
    internal SkippedFleetRecord(int index, ushort x, ushort y, ushort? carriedArmyIndex)
    {
        Index = index;
        X = x;
        Y = y;
        CarriedArmyIndex = carriedArmyIndex;
    }

    /// <summary>The record's position (0-based) in the fleet table — its own original slot; skipping
    /// a record never renumbers the ones that follow it.</summary>
    public int Index { get; }

    /// <summary>Map X at +0 — read for diagnostics even though the record is a tombstone; the one
    /// confirmed instance (IP012B.sav fleet 4) has otherwise-valid coordinates.</summary>
    public ushort X { get; }

    /// <summary>Map Y at +2. See <see cref="X"/>.</summary>
    public ushort Y { get; }

    /// <summary>Index of the army this tombstoned fleet was carrying at +22, or null when it carried
    /// none — the same field and the same <see cref="FleetRecord.NoCarriedArmySentinel"/> rule as
    /// <see cref="FleetRecord.CarriedArmyIndex"/>. Follow-up #340 N1: a fleet absorbed into another
    /// (bug #276) can still be tombstoned while carrying a surviving army; without this, the import had
    /// no way to know that army needs unlinking (<see cref="Engine.Import.EmbarkationLinker"/>).</summary>
    public ushort? CarriedArmyIndex { get; }
}

/// <summary>
/// A 26-byte fleet record, now decompiled field by field from the fleet order/launch/repair/combat code —
/// see https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-unit-map-orders-and-record-fields.md. Layout: X(+0) Y(+2) ?(+4) ?(+6)
/// Owner(+8) ConstructionCountdown(+10) Moves(+12) Supplies(+14) Money(+16) ShipCount(+18)
/// BuildCityOrCondition(+20) CarriedArmyIndex(+22) CoveredCell(+24).
/// The original controlled evidence still stands: a 10-ship order placed at Caere (city 82) with no turn
/// advance added exactly one record with ShipCount 10, word +20 = 82, and OwnerCode matching the ordering
/// nation, changing nothing else but that nation's treasury (https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/fleet-order-at-caere.md,
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/fleet-owner-field-confirmed.md) — but word +20 is reused once the fleet launches, which is
/// why it was mislabelled a permanent CityIndex.
/// </summary>
public sealed class FleetRecord
{
    /// <summary>Value of <see cref="ConstructionCountdown"/> once the fleet has launched.</summary>
    public const ushort LaunchedSentinel = 0xFFFF;

    /// <summary>Value of word +22 when the fleet is not carrying an army.</summary>
    public const ushort NoCarriedArmySentinel = 0xFFFF;

    private readonly byte[] _raw;

    internal FleetRecord(int index, byte[] raw)
    {
        Index = index;
        _raw = raw;
    }

    public int Index { get; }

    /// <summary>Map X at +0. A fleet still under construction has no position and reads (0, 0); it is placed
    /// on the map only when the construction countdown completes.</summary>
    public ushort X => BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan(0, 2));

    /// <summary>Map Y at +2. See <see cref="X"/>.</summary>
    public ushort Y => BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan(2, 2));

    /// <summary>Nation code at +8, confirmed by comparing two independently-identified fleets in the same
    /// save: the Caere fleet (owner known from the controlled order below) reads 0 (Rome), and the
    /// Andematunum fleet (owner known because the next save's news log reports "A fleet belonging to
    /// Carthage is lost at sea" for this exact record) reads 1 (Carthage). All 4 remaining in-port fleets in
    /// that same save also match their port city's current owner exactly. See
    /// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/fleet-owner-field-confirmed.md.</summary>
    public ushort OwnerCode => BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan(8, 2));

    /// <summary>Construction countdown at +10, set to 24 when the order is placed and overwritten with
    /// <see cref="LaunchedSentinel"/> when the fleet launches — so it doubles as the "is on the map" flag.</summary>
    public ushort ConstructionCountdown => BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan(10, 2));

    /// <summary>True once the fleet has been launched and placed on the map.</summary>
    public bool IsLaunched => ConstructionCountdown == LaunchedSentinel;

    /// <summary>Movement points remaining at +12. Zeroed by ordering a repair, embarking an army, joining
    /// fleets, or attacking.</summary>
    public ushort Moves => BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan(12, 2));

    /// <summary>Supply stock in tons at +14, initialised to 50 on launch. Capacity is
    /// <see cref="SupplyCapacityTons"/>.</summary>
    public ushort Supplies => BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan(14, 2));

    /// <summary>The fleet's own money purse at +16 (capped at 1,000 by the transfer dialog), out of which
    /// supply purchases are paid.</summary>
    public ushort Money => BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan(16, 2));

    /// <summary>Ship count at +18, confirmed by a controlled 10-ship order at Caere.</summary>
    public ushort ShipCount => BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan(18, 2));

    /// <summary>Word +20, which serves two purposes depending on <see cref="IsLaunched"/>: while the fleet is
    /// under construction it is the build city's table index (what the launch message names), and on launch it
    /// is overwritten with 100 and thereafter holds the fleet's condition percentage — displayed as "N %" by
    /// the repair dialog, restored at <c>ships * points / 5</c> talents, reduced by naval combat, and used as
    /// <c>ships * condition / 10</c> in the naval strength formula. Prefer <see cref="BuildCityIndex"/> or
    /// <see cref="ConditionPercent"/>.</summary>
    public ushort BuildCityOrCondition => BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan(20, 2));

    /// <summary>The city this fleet is being built at, or null once it has launched.</summary>
    public ushort? BuildCityIndex => IsLaunched ? null : BuildCityOrCondition;

    /// <summary>The fleet's condition percentage, or null while it is still under construction.</summary>
    public ushort? ConditionPercent => IsLaunched ? BuildCityOrCondition : null;

    /// <summary>Index of the army this fleet is carrying at +22, or null when it carries none. A fleet carries
    /// at most one army, of at most <see cref="TransportCapacityTroops"/> troops.</summary>
    public ushort? CarriedArmyIndex
    {
        get
        {
            var value = BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan(22, 2));
            return value == NoCarriedArmySentinel ? null : value;
        }
    }

    /// <summary>Transport capacity, <c>ships * 500</c> troops — enforced on embarking and on hiring
    /// mercenaries into an embarked army.</summary>
    public int TransportCapacityTroops => ShipCount * 500;

    /// <summary>Supply capacity in tons, <c>ships * 8</c> — the cap the supply-purchase dialog enforces.</summary>
    public int SupplyCapacityTons => ShipCount * 8;

    /// <summary>Quarterly upkeep, <c>ships * 3</c> talents.</summary>
    public int QuarterlyUpkeep => ShipCount * 3;

    /// <summary>Returns an unlabelled raw byte from this 26-byte record.</summary>
    public byte RawByteAt(int offset)
    {
        if ((uint)offset >= SaveFleetTable.RecordLength) throw new ArgumentOutOfRangeException(nameof(offset));
        return _raw[offset];
    }
}
