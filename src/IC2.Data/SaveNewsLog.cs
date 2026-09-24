using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IC2.Data;

/// <summary>
/// The 40-slot news/event ring buffer that sits between the mercenary table and the 55-byte
/// calendar trailer in a SAV, and is seeded from the DAT's own last 2,440 bytes. <c>FUN_00449240</c>
/// is the game's single writer: <c>newsIndex</c> (−1 for an empty log, 39 once full) names the newest
/// slot, and there is no head pointer or wrap-around — display order is slot order, oldest first.
/// Each 61-byte slot is a NUL-terminated single-byte ASCII string of at most 60 bytes; bytes after
/// the NUL are whatever the slot held before its last write (stale residue, not corruption — 1,117
/// of 2,101 corpus slots carry it), and are not part of the parsed text. See
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/news-log-format-and-messages.md
/// §Q1 and
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-sav-file-layout.md's
/// 2026-09-14 correction.
/// </summary>
public sealed class SaveNewsLog
{
    /// <summary>The fixed size of one news slot: up to 60 bytes of ASCII text, NUL-terminated.</summary>
    public const int SlotLength = 61;

    /// <summary>The SAV's ring-buffer capacity — 40 slots, indices 0–39. A DAT-sourced log always has
    /// exactly this many slots (see <see cref="DatLayout.NewsSeedSlotCount"/>); a SAV-sourced log has
    /// <c>NewestIndex + 1</c> of them, since the SAV never stores more than it has used.</summary>
    public const int MaxSlotCount = 40;

    private readonly int? _newestIndex;

    private SaveNewsLog(int? newestIndex, IReadOnlyList<string> slots, SaveFileFormat source)
    {
        _newestIndex = newestIndex;
        Slots = slots;
        Source = source;
    }

    /// <summary>The <c>NewestIndex + 1</c> slot texts, oldest first (slot order — the writer never
    /// reorders on append, only on the full-log shift). For a DAT-sourced log this is always all 40
    /// seed slots (see <see cref="Source"/>).</summary>
    public IReadOnlyList<string> Slots { get; }

    /// <summary>Which file shape this log was parsed from.</summary>
    public SaveFileFormat Source { get; }

    /// <summary>The index of the newest slot, −1 … 39. Only a SAV stores this field — the DAT does
    /// not, since <c>FUN_00448AA4</c> (not the DAT loader) is what sets it to 26 once a New Game
    /// actually starts. Throws <see cref="DatDataNotPresentException"/> when <see cref="Source"/> is
    /// <see cref="SaveFileFormat.Dat"/>. See
    /// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/news-log-format-and-messages.md
    /// §Q1, "The DAT seeds the log, and a new game starts at index 26".</summary>
    public int NewestIndex => _newestIndex ?? throw new DatDataNotPresentException(
        "NewestIndex is not stored in the DAT. FUN_00448AA4 sets it to 26 only once a New Game " +
        "actually starts; see " +
        "https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/news-log-format-and-messages.md.");

    public static SaveNewsLog Parse(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        return SaveFormat.Detect(data) == SaveFileFormat.Dat ? ParseDat(data) : ParseSav(data);
    }

    private static SaveNewsLog ParseSav(byte[] data)
    {
        var newsIndexOffset = SaveMercenaryTable.TableEnd(data);
        if (newsIndexOffset + 2 > data.Length)
            throw new InvalidDataException("Save ends before the news log's index field.");

        var newsIndex = BinaryPrimitives.ReadInt16LittleEndian(data.AsSpan(newsIndexOffset, 2));
        if (newsIndex is < -1 or > MaxSlotCount - 1)
            throw new InvalidDataException($"News log index {newsIndex} is outside -1..{MaxSlotCount - 1}.");

        var slotsStart = newsIndexOffset + 2;
        var slotCount = newsIndex + 1;
        var slotsEnd = slotsStart + slotCount * SlotLength;
        var trailerStart = data.Length - 55;
        // "Layout 100,956 + 2 + A×656 + 2 + F×26 + 18,752 + 600 + 2 + (newsIndex+1)×61 + 55 = file
        // length" — checked on 54 of 54 saves; here, checked the equivalent way: the news slots must
        // run exactly up to where the 55-byte trailer starts, with nothing left over and nothing
        // missing.
        if (slotsEnd != trailerStart)
            throw new InvalidDataException(
                $"News log layout does not account for the file length exactly: {slotCount} slots " +
                $"from {slotsStart:X} would end at {slotsEnd:X}, but the 55-byte trailer starts at " +
                $"{trailerStart:X}.");

        var slots = new string[slotCount];
        for (var i = 0; i < slotCount; i++)
            slots[i] = ReadSlotText(data, slotsStart + i * SlotLength);

        return new SaveNewsLog(newsIndex, slots, SaveFileFormat.Sav);
    }

    /// <summary>The DAT stores no index — only the 40-slot seed table, the last
    /// <see cref="DatLayout.NewsSeedLength"/> bytes of the file (checked by
    /// <see cref="SaveFormat.Detect"/>'s fixed-length DAT recognition before this runs).</summary>
    private static SaveNewsLog ParseDat(byte[] data)
    {
        var seedStart = data.Length - DatLayout.NewsSeedLength;
        var slots = new string[DatLayout.NewsSeedSlotCount];
        for (var i = 0; i < slots.Length; i++)
            slots[i] = ReadSlotText(data, seedStart + i * SlotLength);
        return new SaveNewsLog(newestIndex: null, slots, SaveFileFormat.Dat);
    }

    /// <summary>Reads one 61-byte slot's text: bytes up to (not including) the first NUL, validated
    /// to be within 0x20–0x7E. Deliberately does NOT trim: a <c>" "</c> entry (the blank line the
    /// round tick writes before every date header) must survive as a single space, not become an
    /// empty string — unlike <see cref="SaveNationTable"/>'s name reader, which trims because no
    /// nation or city name is ever meaningfully space-padded.</summary>
    private static string ReadSlotText(byte[] data, int offset)
    {
        var nul = Array.IndexOf(data, (byte)0, offset, SlotLength);
        if (nul < 0)
            throw new InvalidDataException($"News slot at {offset:X} has no NUL within {SlotLength} bytes.");
        for (var p = offset; p < nul; p++)
            if (data[p] < 0x20 || data[p] > 0x7e)
                throw new InvalidDataException($"News slot at {offset:X} has a byte outside 0x20-0x7E at {p:X}.");
        return Encoding.ASCII.GetString(data, offset, nul - offset);
    }
}
