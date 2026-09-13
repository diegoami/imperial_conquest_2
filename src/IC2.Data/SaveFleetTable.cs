using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace IC2.Data;

/// <summary>The 26-byte fleet records observed between the army table and the nation table in known SAV files.</summary>
public sealed class SaveFleetTable
{
    public const int RecordLength = 26;

    private SaveFleetTable(FleetRecord[] fleets) => Fleets = fleets;

    public IReadOnlyList<FleetRecord> Fleets { get; }

    public static SaveFleetTable Parse(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (data.Length < WorldPrefix.SharedPrefixLength + 2)
            throw new InvalidDataException("Save ends before the army count.");

        var armyCount = ReadWord(data, WorldPrefix.SharedPrefixLength);
        var fleetCountOffset = WorldPrefix.SharedPrefixLength + 2 + armyCount * SaveArmyTable.RecordLength;
        if (fleetCountOffset + 2 > data.Length)
            throw new InvalidDataException("Save ends before the fleet count.");

        var fleetCount = ReadWord(data, fleetCountOffset);
        if (fleetCount > WorldPrefix.CityCount)
            throw new InvalidDataException($"Implausible fleet count {fleetCount}.");

        var tableStart = fleetCountOffset + 2;
        if (tableStart + fleetCount * RecordLength > data.Length)
            throw new InvalidDataException("Save ends before the complete fleet table.");

        var fleets = new FleetRecord[fleetCount];
        for (var i = 0; i < fleetCount; i++)
        {
            var offset = tableStart + i * RecordLength;
            var raw = new byte[RecordLength];
            Array.Copy(data, offset, raw, 0, RecordLength);
            fleets[i] = new FleetRecord(i, raw);
        }
        return new SaveFleetTable(fleets);
    }

    private static ushort ReadWord(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
}

/// <summary>
/// A 26-byte fleet record, now decompiled field by field from the fleet order/launch/repair/combat code —
/// see docs/reports/decompiled-unit-map-orders-and-record-fields.md. Layout: X(+0) Y(+2) ?(+4) ?(+6)
/// Owner(+8) ConstructionCountdown(+10) Moves(+12) Supplies(+14) Money(+16) ShipCount(+18)
/// BuildCityOrCondition(+20) CarriedArmyIndex(+22) CoveredCell(+24).
/// The original controlled evidence still stands: a 10-ship order placed at Caere (city 82) with no turn
/// advance added exactly one record with ShipCount 10, word +20 = 82, and OwnerCode matching the ordering
/// nation, changing nothing else but that nation's treasury (docs/reports/fleet-order-at-caere.md,
/// docs/reports/fleet-owner-field-confirmed.md) — but word +20 is reused once the fleet launches, which is
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
    /// docs/reports/fleet-owner-field-confirmed.md.</summary>
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
