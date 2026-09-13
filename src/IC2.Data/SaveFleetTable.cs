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
/// A 26-byte fleet record. <see cref="OwnerCode"/>, <see cref="ShipCount"/>, and <see cref="CityIndex"/> have
/// controlled-action or cross-referenced evidence (see docs/reports/fleet-order-at-caere.md and
/// docs/reports/fleet-owner-field-confirmed.md): a 10-ship order placed at Caere (city 82) with no turn
/// advance added exactly one such record with ShipCount 10, CityIndex 82, and OwnerCode matching the
/// ordering nation, and no other field in the save changed except the ordering nation's treasury. All other
/// fields are unlabelled.
/// </summary>
public sealed class FleetRecord
{
    private readonly byte[] _raw;

    internal FleetRecord(int index, byte[] raw)
    {
        Index = index;
        _raw = raw;
    }

    public int Index { get; }

    /// <summary>Candidate map X at +0. The controlled under-construction order and one pre-existing
    /// fleet were both (0, 0); two other pre-existing fleets had plausible map coordinates here and at +2,
    /// suggesting (0, 0) may mark a fleet still under construction rather than a real map position.</summary>
    public ushort X => BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan(0, 2));

    /// <summary>Candidate map Y at +2. See <see cref="X"/>.</summary>
    public ushort Y => BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan(2, 2));

    /// <summary>Nation code at +8, confirmed by comparing two independently-identified fleets in the same
    /// save: the Caere fleet (owner known from the controlled order below) reads 0 (Rome), and the
    /// Andematunum fleet (owner known because the next save's news log reports "A fleet belonging to
    /// Carthage is lost at sea" for this exact record) reads 1 (Carthage). All 4 remaining in-port fleets in
    /// that same save also match their port city's current owner exactly. See
    /// docs/reports/fleet-owner-field-confirmed.md.</summary>
    public ushort OwnerCode => BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan(8, 2));

    /// <summary>Ship count at +18, confirmed by a controlled 10-ship order at Caere.</summary>
    public ushort ShipCount => BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan(18, 2));

    /// <summary>City-table index at +20. A controlled order at Caere confirmed this field, but a later turn
    /// showed two already-deployed fleets' CityIndex change (one of them without moving at all), so this is
    /// NOT a fixed home port/construction origin — read it as "some city this fleet is currently associated
    /// with," candidate meaning "most recently resupplied at." See docs/reports/fleet-order-at-caere.md and
    /// docs/reports/field-recruitment-uniform-attrition-and-fleet-drift.md.</summary>
    public ushort CityIndex => BinaryPrimitives.ReadUInt16LittleEndian(_raw.AsSpan(20, 2));

    /// <summary>Returns an unlabelled raw byte from this 26-byte record.</summary>
    public byte RawByteAt(int offset)
    {
        if ((uint)offset >= SaveFleetTable.RecordLength) throw new ArgumentOutOfRangeException(nameof(offset));
        return _raw[offset];
    }
}
