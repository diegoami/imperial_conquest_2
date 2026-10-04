using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace IC2.Data;

/// <summary>Calendar, active-nation and turn-order fields in the 55-byte trailer of known SAV files:
/// <c>+0</c> turn order (16 x int16), <c>+32</c> pending offer (parsed separately by
/// <see cref="SavePendingOffer"/>), <c>+36</c> current nation, <c>+38</c> turn-order index, <c>+40</c>
/// week, <c>+42</c> year BC, <c>+44</c> season, <c>+46</c> 8 bytes of main-window geometry (UI state,
/// not parsed here), <c>+54</c> a 1-byte battle-in-progress flag, read only by
/// <see cref="LocateTrailerStart"/> to pick the trailer's position. See
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-sav-file-layout.md,
/// the 2026-09-14 correction, checked on 54 saves.
/// <para>A save written during a tactical battle sets that flag to 1 and writes the
/// <see cref="BattleBlockLength"/>-byte battle block <b>after</b> the trailer, so its trailer is not
/// the file's last 55 bytes. <see cref="LocateTrailerStart"/> is the one place that knows this;
/// this class, <see cref="SaveNewsLog"/> and <see cref="SavePendingOffer"/> all use it (bug #675).
/// The block's contents are not parsed here — that is T129's <c>SaveBattleBlock</c>.</para></summary>
public sealed class SaveTurnState
{
    /// <summary>The length of the optional battle block an original save carries after the 55-byte
    /// trailer when it was written during a tactical battle: 9 header bytes, 40 × 44 slot bytes and a
    /// 14 × 12 grid of 2-byte entries. Only its length is used here, to locate the trailer; the
    /// block's contents are T129's. See
    /// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-04-battle-probe.md
    /// item 2 (134,254 bytes = 132,149 + 2,105, "block 12 present, flag 1").</summary>
    internal const int BattleBlockLength = 2105;

    private const int TrailerLength = 55;

    /// <summary>Offset of the 1-byte battle-in-progress flag within the 55-byte trailer.</summary>
    private const int BattleFlagOffset = 54;
    private SaveTurnState(ushort currentNationCode, ushort week, ushort yearBc, ushort seasonCode,
        ushort[] turnOrder, ushort turnOrderIndex)
    {
        CurrentNationCode = currentNationCode;
        Week = week;
        YearBc = yearBc;
        SeasonCode = seasonCode;
        // Array.AsReadOnly, not the array itself cast to IReadOnlyList<ushort>: a caller that casts
        // the interface back to ushort[] must not be able to reach (and mutate) the backing array —
        // N6, T73 review round 1.
        TurnOrder = Array.AsReadOnly(turnOrder);
        TurnOrderIndex = turnOrderIndex;
    }

    public ushort CurrentNationCode { get; }
    public ushort Week { get; }
    public ushort YearBc { get; }
    public ushort SeasonCode { get; }

    /// <summary>The saved turn order: 16 nation codes, one per seat, a permutation of 0..15. Trailer
    /// offset <c>+0</c>.</summary>
    public IReadOnlyList<ushort> TurnOrder { get; }

    /// <summary>The index into <see cref="TurnOrder"/> of the nation whose turn it is —
    /// <c>TurnOrder[TurnOrderIndex] == CurrentNationCode</c> always holds (checked on 54 of 54
    /// saves). Trailer offset <c>+38</c>.</summary>
    public ushort TurnOrderIndex { get; }

    public string SeasonName => SeasonCode switch
    {
        0 => "Spring",
        1 => "Summer",
        2 => "Autumn",
        3 => "Winter",
        _ => $"season {SeasonCode}"
    };

    /// <summary>Returns the file offset where the 55-byte calendar/turn-order trailer starts.
    /// <para>A save without a battle block ends with the trailer, so the trailer starts at
    /// <c>length − 55</c> and its battle flag (<c>+54</c>) is the file's last byte. A save written
    /// during a tactical battle sets that flag to 1 and writes the <see cref="BattleBlockLength"/>-byte
    /// battle block after the trailer, so the trailer starts at <c>length − 55 − 2,105</c> and the
    /// flag sits at <c>length − 2,105 − 1</c>.</para>
    /// <para>The flag byte alone cannot decide which shape a file is: a real battle block ends with
    /// the 14 × 12 icon grid of little-endian 16-bit words (50 empty, otherwise at most 34), so the
    /// block's last byte — the file's last byte — is <b>0</b> on every real mid-battle save, the same
    /// value a no-block save's flag has. When both candidates carry their own flag value, the trailer
    /// is the one that is structurally a trailer (a valid 0..15 turn-order permutation whose
    /// <c>+38</c> entry is the <c>+36</c> current nation, with <see cref="Parse"/>'s calendar
    /// bounds); the no-block case that carries its own flag 0 is returned directly, so a malformed
    /// turn order still reaches <see cref="Parse"/> and its specific error. A file where both fit or
    /// neither fits is rejected rather than silently guessed. Shared by <see cref="SaveNewsLog"/> and
    /// <see cref="SavePendingOffer"/> so none of the three counts back from <c>length</c> alone
    /// (bug #675).</para></summary>
    public static int LocateTrailerStart(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));

        var noBlockStart = data.Length - TrailerLength;
        var blockStart = noBlockStart - BattleBlockLength;

        var noBlockFlag = noBlockStart >= 0 && data[noBlockStart + BattleFlagOffset] == 0;
        var blockFlag = blockStart >= 0 && data[blockStart + BattleFlagOffset] == 1;

        // The common case: exactly one candidate carries its own flag value. Return it directly and
        // let Parse validate the trailer's structure, so a malformed trailer keeps Parse's specific
        // error rather than getting a generic "cannot be located" here.
        if (noBlockFlag && !blockFlag) return noBlockStart;
        if (blockFlag && !noBlockFlag && IsValidTrailerAt(data, blockStart, expectedBattleFlag: 1))
            return blockStart;

        // Both candidates carry their flag value — a mid-battle save's icon grid leaves the last byte
        // 0 too, and a block-free save can coincidentally hold 1 where a block's flag would sit — or
        // neither does. Pick the candidate whose bytes are structurally a trailer; an ambiguous pair
        // or a file that fits neither shape is rejected.
        var noBlockValid = noBlockStart >= 0 && IsValidTrailerAt(data, noBlockStart, expectedBattleFlag: 0);
        var blockValid = blockStart >= 0 && IsValidTrailerAt(data, blockStart, expectedBattleFlag: 1);
        if (noBlockValid && !blockValid) return noBlockStart;
        if (blockValid && !noBlockValid) return blockStart;

        if (noBlockValid && blockValid)
            throw new InvalidDataException(
                $"Save's trailer cannot be located: both {noBlockStart:X} (the last 55 bytes) and " +
                $"{blockStart:X} (55 bytes before a {BattleBlockLength}-byte battle block) are " +
                "structurally valid calendar/turn-order trailers, so the file's shape is ambiguous.");

        throw new InvalidDataException(
            $"Save's trailer cannot be located: neither {noBlockStart:X} (the last 55 bytes) nor " +
            $"{blockStart:X} (55 bytes before a {BattleBlockLength}-byte battle block) is structurally " +
            "a calendar/turn-order trailer, and the file's battle flag does not pick one.");
    }

    /// <summary>True when a structurally valid 55-byte trailer begins at <paramref name="start"/>:
    /// its battle flag (<c>+54</c>) has the <paramref name="expectedBattleFlag"/> value the caller's
    /// candidate shape calls for, and its <c>+0</c> turn order is a permutation of 0..15 whose entry
    /// at <c>+38</c> is the <c>+36</c> current nation, with the same calendar bounds
    /// <see cref="Parse"/> enforces.</summary>
    private static bool IsValidTrailerAt(byte[] data, int start, byte expectedBattleFlag)
    {
        if (start < 0 || start + TrailerLength > data.Length) return false;
        if (data[start + BattleFlagOffset] != expectedBattleFlag) return false;

        var seen = new bool[16];
        for (var i = 0; i < seen.Length; i++)
        {
            var code = ReadWord(data, start + i * 2);
            if (code > 15 || seen[code]) return false;
            seen[code] = true;
        }

        var turnOrderIndex = ReadWord(data, start + 38);
        var currentNation = ReadWord(data, start + 36);
        var week = ReadWord(data, start + 40);
        var year = ReadWord(data, start + 42);
        var season = ReadWord(data, start + 44);
        if (turnOrderIndex > 15) return false;
        if (currentNation > 15 || week is 0 or > 13 || year is 0 or > 1000 || season > 3) return false;
        return ReadWord(data, start + turnOrderIndex * 2) == currentNation;
    }

    public static SaveTurnState Parse(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (SaveFormat.Detect(data) == SaveFileFormat.Dat)
            throw new DatDataNotPresentException(
                "The DAT has no calendar/current-turn trailer at all — no code assigns starting " +
                "week/season/year/active-nation values when the DAT is loaded, so there is no " +
                "DAT-derived value that would not be an invented default. See " +
                "docs/investigations/dat-file-layout.md.");
        var start = LocateTrailerStart(data);

        var turnOrder = new ushort[16];
        for (var i = 0; i < turnOrder.Length; i++)
            turnOrder[i] = ReadWord(data, start + i * 2);
        var turnOrderIndex = ReadWord(data, start + 38);
        var currentNation = ReadWord(data, start + 36);
        var week = ReadWord(data, start + 40);
        var year = ReadWord(data, start + 42);
        var season = ReadWord(data, start + 44);
        if (currentNation > 15 || week is 0 or > 13 || year is 0 or > 1000 || season > 3)
            throw new InvalidDataException("Save trailer has unsupported calendar or active-nation values.");

        // The turn order is a permutation of 0..15: every nation seat appears exactly once. Checked
        // by counting occurrences rather than sorting, so a duplicate is named directly.
        var seen = new bool[16];
        foreach (var code in turnOrder)
        {
            if (code > 15)
                throw new InvalidDataException($"Save trailer's turn order names nation {code}, outside 0..15.");
            if (seen[code])
                throw new InvalidDataException($"Save trailer's turn order is not a permutation: nation {code} appears more than once.");
            seen[code] = true;
        }
        if (turnOrderIndex > 15)
            throw new InvalidDataException($"Save trailer's turn-order index {turnOrderIndex} is outside 0..15.");
        if (turnOrder[turnOrderIndex] != currentNation)
            throw new InvalidDataException(
                $"Save trailer's turn order at index {turnOrderIndex} names nation " +
                $"{turnOrder[turnOrderIndex]}, not the current nation {currentNation}.");

        return new SaveTurnState(currentNation, week, year, season, turnOrder, turnOrderIndex);
    }

    private static ushort ReadWord(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
}
