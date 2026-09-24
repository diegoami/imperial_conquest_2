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

    // ---- T73 (bug #321/#322): relation-row, trailer and news-log builders ----------------------

    /// <summary>Leader offset within one nation record (+0x0B) and its narrowed length (27 bytes,
    /// ending exactly where the relation row starts at +0x26) — mirrors
    /// <c>SaveNationTable</c>'s own (private) offsets, not directly reusable here.</summary>
    private const int NationLeaderOffset = 0x0B;
    private const int NationLeaderLength = 27;

    /// <summary>Relation-row offset within one nation record (+0x26, immediately after the 27-byte
    /// leader field) — mirrors <c>DatLayout.NationRelationOffset</c>'s SAV-side counterpart, which is
    /// the same offset the runtime record itself uses (SAV nation-record +0x26 == runtime +0x26).</summary>
    private const int NationRelationOffset = 0x26;

    /// <summary>Builds a SAV-shaped byte array with zero armies/fleets and a complete, correctly-named
    /// 16-record nation table — the minimum <see cref="SaveNationTable.Parse"/> needs, since it
    /// checks every name against <see cref="NationCatalog"/> and (T73 review round 1, N1) rejects an
    /// empty leader field just as it rejects an empty name. Every record gets a placeholder leader
    /// ("Leader &lt;code&gt;") alongside its name, and starts with an all-peace relation row
    /// (diagonal 0, every other entry 0, a valid — if uninteresting — matrix), so a test only needs
    /// to write the specific bytes its scenario cares about via <see cref="NationRecordOffset"/> and
    /// the writers below.</summary>
    public static byte[] MinimalSavWithNations()
    {
        var data = MinimalSavWithFleets(0, 0);
        for (var i = 0; i < NationCount; i++)
        {
            var offset = NationRecordOffset(data, i);
            var nameBytes = System.Text.Encoding.ASCII.GetBytes(NationCatalog.Name((ushort)i));
            Array.Copy(nameBytes, 0, data, offset, nameBytes.Length);
            // Byte after the name stays 0 (NUL) — the array starts zero-filled.
            var leaderBytes = System.Text.Encoding.ASCII.GetBytes($"Leader {i}");
            Array.Copy(leaderBytes, 0, data, offset + NationLeaderOffset, leaderBytes.Length);
        }
        return data;
    }

    /// <summary>The byte offset of nation record <paramref name="nationIndex"/> within
    /// <paramref name="data"/>, built by <see cref="MinimalSavWithNations"/> (zero armies/fleets, so
    /// the nation table is the last <c>NationCount * SavNationRecordLength</c> bytes).</summary>
    public static int NationRecordOffset(byte[] data, int nationIndex) =>
        data.Length - NationCount * SavNationRecordLength + nationIndex * SavNationRecordLength;

    /// <summary>Writes <paramref name="leaderBytes"/> (at most 27 bytes) at nation
    /// <paramref name="nationIndex"/>'s leader field. Unlike a name field, 27 non-NUL bytes are
    /// valid: <see cref="SaveNationTable"/>'s leader reader does not require a terminating NUL when
    /// the field is filled exactly (see its own remarks) — this is exactly Done-when line 2's "leader
    /// fills all 27 bytes with no NUL" scenario.</summary>
    public static void WriteNationLeaderBytes(byte[] data, int nationIndex, byte[] leaderBytes)
    {
        if (leaderBytes.Length > NationLeaderLength)
            throw new ArgumentException($"Leader must be at most {NationLeaderLength} bytes.", nameof(leaderBytes));
        var offset = NationRecordOffset(data, nationIndex) + NationLeaderOffset;
        Array.Copy(leaderBytes, 0, data, offset, leaderBytes.Length);
    }

    /// <summary>Writes one relation-matrix entry — nation <paramref name="a"/>'s row, toward nation
    /// <paramref name="b"/> — without touching the reciprocal <c>[b][a]</c> entry. Used to build an
    /// intentionally asymmetric matrix for <see cref="SaveNationTable"/>'s rejection tests; a
    /// well-formed matrix is built with <see cref="SetSymmetricRelation"/> instead, which writes
    /// both sides the way the original's single setter (<c>FUN_00449B40</c>) does.</summary>
    public static void WriteRelationEntry(byte[] data, int a, int b, short value) =>
        WriteInt16(data, NationRecordOffset(data, a) + NationRelationOffset + b * 2, value);

    /// <summary>Writes a symmetric relation-matrix entry: both <c>[a][b]</c> and <c>[b][a]</c> to
    /// <paramref name="value"/>, as <c>FUN_00449B40</c> — the game's single setter — always does.</summary>
    public static void SetSymmetricRelation(byte[] data, int a, int b, short value)
    {
        WriteRelationEntry(data, a, b, value);
        WriteRelationEntry(data, b, a, value);
    }

    /// <summary>Appends the 55-byte calendar/turn-order trailer
    /// (<c>+0</c> turn order, 16 x uint16; <c>+32</c> pending offer, left zero; <c>+36</c> current
    /// nation; <c>+38</c> turn-order index; <c>+40</c> week; <c>+42</c> year BC; <c>+44</c> season;
    /// <c>+46</c> window geometry, left zero; <c>+54</c> battle flag) to the end of
    /// <paramref name="data"/>. See
    /// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-sav-file-layout.md,
    /// the 2026-09-14 correction.</summary>
    public static byte[] AppendTrailer(byte[] data, ushort[] turnOrder, ushort turnOrderIndex,
        ushort currentNation, ushort week, ushort yearBc, ushort season, byte battleFlag = 0)
    {
        if (turnOrder.Length != 16)
            throw new ArgumentException("Turn order must have 16 entries.", nameof(turnOrder));

        var trailer = new byte[55];
        for (var i = 0; i < 16; i++)
            WriteUInt16(trailer, i * 2, turnOrder[i]);
        WriteUInt16(trailer, 36, currentNation);
        WriteUInt16(trailer, 38, turnOrderIndex);
        WriteUInt16(trailer, 40, week);
        WriteUInt16(trailer, 42, yearBc);
        WriteUInt16(trailer, 44, season);
        trailer[54] = battleFlag;

        var result = new byte[data.Length + trailer.Length];
        Array.Copy(data, result, data.Length);
        Array.Copy(trailer, 0, result, data.Length, trailer.Length);
        return result;
    }

    /// <summary>Appends a zero-filled 600-byte mercenary table, then a news log — <c>int16
    /// newsIndex</c> followed by <c>newsIndex + 1</c> 61-byte NUL-terminated slots, one per entry in
    /// <paramref name="slotTexts"/> — to the end of <paramref name="data"/>. Does not append the
    /// 55-byte trailer; combine with <see cref="AppendTrailer"/> for a complete SAV. Mirrors
    /// <see cref="SaveMercenaryTable"/>'s own 50 x 12-byte layout and <see cref="SaveNewsLog"/>'s own
    /// 61-byte slot length — both public constants, reused directly rather than duplicated.</summary>
    public static byte[] AppendMercenaryTableAndNews(byte[] data, short newsIndex, params string[] slotTexts)
    {
        if (slotTexts.Length != newsIndex + 1)
            throw new ArgumentException("slotTexts.Length must equal newsIndex + 1.", nameof(slotTexts));

        var mercenaryTable = new byte[SaveMercenaryTable.RecordCount * SaveMercenaryTable.RecordLength];
        var newsIndexBytes = new byte[2];
        WriteInt16(newsIndexBytes, 0, newsIndex);
        var slotsBytes = new byte[slotTexts.Length * SaveNewsLog.SlotLength];
        for (var i = 0; i < slotTexts.Length; i++)
        {
            var bytes = System.Text.Encoding.ASCII.GetBytes(slotTexts[i]);
            if (bytes.Length >= SaveNewsLog.SlotLength)
                throw new ArgumentException($"Slot {i} text must be under {SaveNewsLog.SlotLength} bytes.", nameof(slotTexts));
            Array.Copy(bytes, 0, slotsBytes, i * SaveNewsLog.SlotLength, bytes.Length);
            // Byte at bytes.Length stays 0 (the NUL terminator); the rest of the slot stays 0 too —
            // a test that needs stale non-zero residue after the NUL writes it explicitly.
        }

        var result = new byte[data.Length + mercenaryTable.Length + newsIndexBytes.Length + slotsBytes.Length];
        var pos = 0;
        Array.Copy(data, 0, result, pos, data.Length); pos += data.Length;
        Array.Copy(mercenaryTable, 0, result, pos, mercenaryTable.Length); pos += mercenaryTable.Length;
        Array.Copy(newsIndexBytes, 0, result, pos, newsIndexBytes.Length); pos += newsIndexBytes.Length;
        Array.Copy(slotsBytes, 0, result, pos, slotsBytes.Length);
        return result;
    }
}
