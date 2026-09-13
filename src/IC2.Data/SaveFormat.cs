using System;
using System.IO;

namespace IC2.Data;

/// <summary>The two known Imperial Conquest 2 world-file shapes. See
/// docs/investigations/dat-file-layout.md: the DAT is not a SAV with a different extension — it
/// shares the SAV's 100,956-byte map+city prefix (<see cref="WorldPrefix.SharedPrefixLength"/>) and
/// diverges immediately after it.</summary>
public enum SaveFileFormat
{
    /// <summary>A player save: count-word-prefixed army/fleet tables, a 1,172-byte nation record,
    /// a 50-slot mercenary pool, and a 55-byte calendar trailer.</summary>
    Sav,

    /// <summary>The shipped world template (`Imperial Conquest 2.dat`): fixed-size army (15) and
    /// fleet (2) tables with no count words at all, a 1,055-byte nation record, and no mercenary pool
    /// or calendar trailer — that state does not exist until a New Game actually starts.</summary>
    Dat
}

/// <summary>
/// Distinguishes the DAT's own fixed layout from a SAV's count-word-driven layout, so the SAV table
/// walk (<see cref="SaveNationLayout.Locate"/>) is never asked to "try harder" on a file it cannot
/// parse — see docs/build-orchestration-plan.md's T30 entry ("do not teach the SAV locator to 'try
/// harder'") and docs/investigations/dat-file-layout.md.
/// </summary>
public static class SaveFormat
{
    /// <summary>The DAT's fixed total size in bytes — the sum of every read the New Game loader
    /// <c>FUN_004481a0</c> issues, exactly: 89,600 (map) + 11,356 (city table) + 15×656 (armies) +
    /// 2×26 (fleets) + 16×1,055 (nations) + 1,494 + 1,040 + 3,012 + 4,992 + 2,440 (static tables) =
    /// 140,706. See docs/investigations/dat-file-layout.md's read-order table, which accounts for
    /// the file exactly.</summary>
    public const int DatFileLength = 140706;

    /// <summary>Detects whether <paramref name="data"/> is DAT-shaped or SAV-shaped. A DAT is
    /// recognised by its fixed total length; a SAV is recognised by the same count-word-driven
    /// army/fleet/nation-table walk <see cref="SaveNationLayout.Locate"/> already performs for every
    /// SAV-consuming parser. A file that is neither throws
    /// <see cref="UnrecognizedSaveFormatException"/> naming both checks that failed.</summary>
    public static SaveFileFormat Detect(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (data.Length == DatFileLength) return SaveFileFormat.Dat;

        try
        {
            SaveNationLayout.Locate(data);
            return SaveFileFormat.Sav;
        }
        catch (InvalidDataException ex)
        {
            throw new UnrecognizedSaveFormatException(
                $"File is {data.Length} bytes: it does not match the DAT's fixed {DatFileLength} " +
                $"bytes, and it does not check out as a SAV either — the army/fleet/nation-table " +
                $"walk failed with: {ex.Message}", ex);
        }
    }
}

/// <summary>Thrown by <see cref="SaveFormat.Detect"/> when a file matches neither the DAT's fixed
/// length nor a SAV's count-word-driven table structure — i.e. neither discriminator recognised it.
/// This must never be papered over by falling through to one layout or the other.
/// <see cref="System.IO.InvalidDataException"/> is sealed, so this derives from <see cref="Exception"/>
/// directly rather than from it.</summary>
public sealed class UnrecognizedSaveFormatException : Exception
{
    public UnrecognizedSaveFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
