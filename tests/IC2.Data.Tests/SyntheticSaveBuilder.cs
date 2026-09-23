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
    public static byte[] MinimalSav(int armyCount, params (int Index, Action<byte[], int> Write)[] writers) =>
        MinimalSavWithFleets(armyCount, 0, armyWriters: writers);

    /// <summary>As <see cref="MinimalSav"/>, but with <paramref name="fleetCount"/> 26-byte fleet
    /// records (zero-filled by default — an all-zero fleet record is a structurally valid fleet with
    /// owner 0 (Rome) and every other field 0) following the army table, for tests that target
    /// <see cref="SaveFleetTable.Parse"/>. Apply <paramref name="fleetWriters"/> to fill in specific
    /// fleet records before returning; <paramref name="armyWriters"/> does the same for army
    /// records.</summary>
    public static byte[] MinimalSavWithFleets(int armyCount, int fleetCount,
        (int Index, Action<byte[], int> Write)[]? armyWriters = null,
        (int Index, Action<byte[], int> Write)[]? fleetWriters = null)
    {
        var mapAndCityLength = WorldPrefix.SharedPrefixLength;
        var armyTableLength = armyCount * SaveArmyTable.RecordLength;
        var fleetTableLength = fleetCount * SaveFleetTable.RecordLength;
        var totalLength = mapAndCityLength + 2 + armyTableLength + 2 + fleetTableLength
            + NationCount * SavNationRecordLength;

        var data = new byte[totalLength];
        WriteUInt16(data, mapAndCityLength, (ushort)armyCount);

        var armyTableStart = mapAndCityLength + 2;
        foreach (var (index, write) in armyWriters ?? Array.Empty<(int, Action<byte[], int>)>())
            write(data, armyTableStart + index * SaveArmyTable.RecordLength);

        var fleetCountOffset = armyTableStart + armyTableLength;
        WriteUInt16(data, fleetCountOffset, (ushort)fleetCount);

        var fleetTableStart = fleetCountOffset + 2;
        foreach (var (index, write) in fleetWriters ?? Array.Empty<(int, Action<byte[], int>)>())
            write(data, fleetTableStart + index * SaveFleetTable.RecordLength);

        return data;
    }

    /// <summary>Writes a fleet record's owner word (+8) at <paramref name="offset"/>, leaving every
    /// other field at 0. Enough to target <see cref="SaveFleetTable.Parse"/>'s owner-sentinel
    /// handling without needing every other field populated.</summary>
    public static void WriteFleetOwner(byte[] data, int offset, ushort owner) =>
        WriteUInt16(data, offset + 8, owner);

    /// <summary>Writes an army record header (X, Y, Owner, Moves; CoveredCell/Supplies/Money/Morale
    /// left at 0) at <paramref name="offset"/>. All 20 unit slots are left zeroed (troops = 0), which
    /// <see cref="SaveArmyTable.Parse"/> treats as "no unit in this slot" and skips without validating
    /// name/type/quality — so a caller only needs to fill the header to get a structurally valid army.
    /// <paramref name="moves"/> is <see cref="short"/>, not <see cref="ushort"/>, because word +6 is
    /// the one signed field on this record (see <see cref="ArmyRecord.Moves"/>'s own doc comment) —
    /// callers write the value they mean, including negative ones, without a bit-pattern cast.</summary>
    public static void WriteArmyHeader(byte[] data, int offset, ushort x = 0, ushort y = 0, ushort owner = 0,
        short moves = 0)
    {
        WriteUInt16(data, offset + 0, x);
        WriteUInt16(data, offset + 2, y);
        WriteUInt16(data, offset + 4, owner);
        WriteInt16(data, offset + 6, moves);
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

    /// <summary>Returns a copy of <paramref name="data"/> resized to exactly <paramref name="totalLength"/>
    /// bytes, truncating or zero-padding at the end. The SAV structural walk
    /// <see cref="SaveFormat.Detect"/> uses only ever requires the file to be AT LEAST long enough to
    /// hold the nation table, so padding a structurally valid SAV out to an arbitrary total length — even
    /// exactly <see cref="SaveFormat.DatFileLength"/> — must not change how it is classified or
    /// parsed. Used to reproduce the coincidental-length misclassification the DAT-vs-SAV
    /// discriminator must not make.</summary>
    public static byte[] PadTo(byte[] data, int totalLength)
    {
        var padded = new byte[totalLength];
        Array.Copy(data, padded, Math.Min(data.Length, totalLength));
        return padded;
    }

    private static void WriteUInt16(byte[] data, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(offset, 2), value);

    private static void WriteInt16(byte[] data, int offset, short value) =>
        BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(offset, 2), value);
}
