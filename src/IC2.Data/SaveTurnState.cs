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
/// the 2026-09-14 correction, checked on 54 saves.</summary>
public sealed class SaveTurnState
{
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

    public static SaveTurnState Parse(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (SaveFormat.Detect(data) == SaveFileFormat.Dat)
            throw new DatDataNotPresentException(
                "The DAT has no calendar/current-turn trailer at all — no code assigns starting " +
                "week/season/year/active-nation values when the DAT is loaded, so there is no " +
                "DAT-derived value that would not be an invented default. See " +
                "docs/investigations/dat-file-layout.md.");
        if (data.Length < 55) throw new InvalidDataException("Save ends before the calendar trailer.");
        var start = data.Length - 55;

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
