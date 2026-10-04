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
/// not parsed here), <c>+54</c> a 1-byte battle-in-progress flag (also not parsed here). See
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
    public const int BattleBlockLength = 2105;

    private const int TrailerLength = 55;
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
    /// <para>Shared by <see cref="SaveNewsLog"/> and <see cref="SavePendingOffer"/> so none of the
    /// three counts back from <c>length</c> alone (bug #675). Rejects a file that fits neither shape —
    /// its last byte is not the no-block flag 0, and either it is too short to hold a block or the
    /// byte one block before the end is not the mid-battle flag 1 — rather than silently guessing.</para></summary>
    public static int LocateTrailerStart(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (data.Length < TrailerLength)
            throw new InvalidDataException("Save ends before the calendar trailer.");

        var noBlockStart = data.Length - TrailerLength;
        if (data[^1] == 0) return noBlockStart;

        if (data.Length >= TrailerLength + BattleBlockLength
            && data[data.Length - BattleBlockLength - 1] == 1)
            return noBlockStart - BattleBlockLength;

        throw new InvalidDataException(
            $"Save's trailer cannot be located: the file's last byte is {data[^1]}, not the battle " +
            "flag 0 of a save without a battle block, and the byte " +
            $"{BattleBlockLength} bytes before its end is not the mid-battle flag 1 either.");
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
