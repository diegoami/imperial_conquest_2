using System.Buffers.Binary;

namespace IC2.Data.Tests;

/// <summary>
/// Builds minimal, structurally-valid synthetic SAV byte arrays for testing IC2.Data's own
/// malformed-record error paths (docs/build-orchestration-plan.md, T30 Done-when line 3) without
/// needing the user's original save files — this is deliberately the one class in this task's test
/// suite that does NOT gate on <see cref="LocalAssets.IsConfigured"/>: it tests IC2.Data's own logic
/// against bytes this test suite constructs itself, so there is nothing to skip.
///
/// Only the byte ranges the parsers under test actually read are given meaningful values. Neither
/// <see cref="SaveArmyTable.Parse"/> nor the SAV-shape probe <see cref="SaveFormat.Detect"/> uses
/// (internally, the army/fleet/nation-table walk) inspects the map, city-table, or nation-table
/// *content* (only their byte lengths, to place the army/fleet/nation tables), so those regions are
/// left zeroed.
/// </summary>
internal static class SyntheticSaveBuilder
{
    // Mirrors IC2.Data.SaveNationLayout's own (internal, so not directly reusable here) constants.
    // 1,172 bytes is the in-memory SAV nation record length cited throughout
    // docs/investigations/dat-file-layout.md ("the 1,172-byte in-memory record"); 16 is the nation count.
    private const int SavNationRecordLength = 1172;
    private const int NationCount = 16;

    /// <summary>
    /// Builds a SAV-shaped byte array with <paramref name="armyCount"/> army records (656 bytes each,
    /// zero-filled by default — an all-zero record is a structurally valid army with owner 0 (Rome),
    /// coordinates (0, 0), and 20 empty unit slots) and zero fleets. The array is long enough for
    /// <see cref="SaveFormat.Detect"/> to recognise it as SAV-shaped. Apply <paramref name="writers"/>
    /// to fill in specific records before returning.
    /// </summary>
    public static byte[] MinimalSav(int armyCount, params (int Index, Action<byte[], int> Write)[] writers)
    {
        var mapAndCityLength = WorldPrefix.SharedPrefixLength;
        var armyTableLength = armyCount * SaveArmyTable.RecordLength;
        var totalLength = mapAndCityLength + 2 + armyTableLength + 2 + NationCount * SavNationRecordLength;

        var data = new byte[totalLength];
        WriteUInt16(data, mapAndCityLength, (ushort)armyCount);

        var armyTableStart = mapAndCityLength + 2;
        foreach (var (index, write) in writers)
            write(data, armyTableStart + index * SaveArmyTable.RecordLength);

        var fleetCountOffset = armyTableStart + armyTableLength;
        WriteUInt16(data, fleetCountOffset, 0); // zero fleets

        return data;
    }

    /// <summary>Writes an army record header (X, Y, Owner; Moves/CoveredCell/Supplies/Money/Morale
    /// left at 0) at <paramref name="offset"/>. All 20 unit slots are left zeroed (troops = 0), which
    /// <see cref="SaveArmyTable.Parse"/> treats as "no unit in this slot" and skips without validating
    /// name/type/quality — so a caller only needs to fill the header to get a structurally valid army.</summary>
    public static void WriteArmyHeader(byte[] data, int offset, ushort x = 0, ushort y = 0, ushort owner = 0)
    {
        WriteUInt16(data, offset + 0, x);
        WriteUInt16(data, offset + 2, y);
        WriteUInt16(data, offset + 4, owner);
    }

    /// <summary>Writes an army record whose header is valid but whose unit slot 0 holds the given
    /// (possibly invalid) type/quality/name, so a test can target <see cref="SaveArmyTable.Parse"/>'s
    /// unit-slot validation specifically.</summary>
    public static void WriteArmyWithUnit(byte[] data, int offset, ushort type, ushort quality, string name,
        ushort troops = 1)
    {
        WriteArmyHeader(data, offset, owner: 0);
        const int unitOffset = 16; // HeaderLength
        WriteUInt16(data, offset + unitOffset + 2, type);
        WriteUInt16(data, offset + unitOffset + 4, troops);
        WriteUInt16(data, offset + unitOffset + 6, quality);
        var nameBytes = System.Text.Encoding.ASCII.GetBytes(name);
        Array.Copy(nameBytes, 0, data, offset + unitOffset + 8, Math.Min(nameBytes.Length, 23));
        // Byte after the name is already 0 (NUL terminator) since the array starts zero-filled.
    }

    private static void WriteUInt16(byte[] data, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), value);
}
