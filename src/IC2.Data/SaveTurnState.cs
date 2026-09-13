using System;
using System.Buffers.Binary;
using System.IO;

namespace IC2.Data;

/// <summary>Calendar and active-nation fields in the 55-byte trailer of known SAV files.</summary>
public sealed class SaveTurnState
{
    private SaveTurnState(ushort currentNationCode, ushort week, ushort yearBc, ushort seasonCode)
    {
        CurrentNationCode = currentNationCode;
        Week = week;
        YearBc = yearBc;
        SeasonCode = seasonCode;
    }

    public ushort CurrentNationCode { get; }
    public ushort Week { get; }
    public ushort YearBc { get; }
    public ushort SeasonCode { get; }

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
        var currentNation = ReadWord(data, start + 36);
        var week = ReadWord(data, start + 40);
        var year = ReadWord(data, start + 42);
        var season = ReadWord(data, start + 44);
        if (currentNation > 15 || week is 0 or > 13 || year is 0 or > 1000 || season > 3)
            throw new InvalidDataException("Save trailer has unsupported calendar or active-nation values.");
        return new SaveTurnState(currentNation, week, year, season);
    }

    private static ushort ReadWord(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset, 2));
}
