using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;

namespace IC2.Data;

/// <summary>
/// The DAT's own leader-name pool — 16 nations × 12 candidates × 26 bytes at DAT offset
/// <c>0x2089A</c>, the <c>leader-name pool, 16 × 12 × 26 bytes</c> row of
/// <c>FUN_004481A0</c>'s read order in docs/investigations/dat-file-layout.md. <c>FUN_00448aa4</c>
/// copies one candidate per nation at New Game with
/// <c>strcpy(record + 0x0b, leaderPool + i * 0x1a)</c>, so this is the 12-name table the original draws
/// from; the nation record's own leader field is still New Game state (see
/// <see cref="DatDataNotPresentException"/>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Nations in DAT order.</strong> <see cref="Pools"/> is indexed by the DAT nation-table order
/// (the same order <see cref="NationCatalog"/> and <see cref="SaveNationTable.ParseDat"/> use: Rome 0 …
/// Thracia 15). The pool block is flat, so the nation index is simply the record's position divided by
/// <see cref="DatLayout.LeaderPoolNamesPerNation"/>.
/// </para>
/// <para>
/// <strong>Every record is NUL-terminated within its own 26 bytes.</strong> The pool is a block of
/// fixed 26-byte slots, and <c>strcpy</c> would run past a slot with no terminator into the next
/// candidate's text; T146 review B2 measured all 192 names and found every one terminated well within
/// the slot (longest 21 characters). This parser enforces the same property rather than trusting it:
/// a slot with no NUL, or an empty slot, is a genuinely malformed pool and throws
/// <see cref="InvalidDataException"/> — not a name read out of the following slot.
/// </para>
/// </remarks>
public sealed class DatLeaderPool
{
    /// <summary>The DAT pool's own nation count (the nation-table order's 16).</summary>
    public const int NationCount = DatLayout.LeaderPoolNationCount;

    /// <summary>The DAT pool's own candidates per nation (the report's 12).</summary>
    public const int NamesPerNation = DatLayout.LeaderPoolNamesPerNation;

    private DatLeaderPool(string[][] pools)
    {
        Pools = new ReadOnlyCollection<ReadOnlyCollection<string>>(
            Array.ConvertAll(pools, pool => new ReadOnlyCollection<string>(pool)));
    }

    /// <summary>The 16 nations' 12-name pools, indexed by DAT nation order.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Pools { get; }

    /// <summary>One nation's 12-name pool, by DAT nation index.</summary>
    public IReadOnlyList<string> this[int nationIndex] => Pools[nationIndex];

    /// <summary>Parses the 4,992-byte pool out of a DAT-shaped file.</summary>
    /// <param name="data">The whole DAT file's bytes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    /// <exception cref="InvalidDataException">The file ends before the pool, or a slot is empty or has no NUL within its 26 bytes.</exception>
    public static DatLeaderPool Parse(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));

        var end = DatLayout.LeaderPoolStart
                  + (DatLayout.LeaderPoolNationCount * DatLayout.LeaderPoolNamesPerNation * DatLayout.LeaderPoolNameRecordLength);
        if (data.Length < end)
        {
            throw new InvalidDataException(
                $"The file ends before the leader pool: {DatLayout.LeaderPoolStart:X} + " +
                $"{DatLayout.LeaderPoolNationCount} x {DatLayout.LeaderPoolNamesPerNation} x " +
                $"{DatLayout.LeaderPoolNameRecordLength} needs {end} bytes, got {data.Length}.");
        }

        var pools = new string[DatLayout.LeaderPoolNationCount][];
        for (var nation = 0; nation < DatLayout.LeaderPoolNationCount; nation++)
        {
            var names = new string[DatLayout.LeaderPoolNamesPerNation];
            for (var candidate = 0; candidate < DatLayout.LeaderPoolNamesPerNation; candidate++)
            {
                var offset = DatLayout.LeaderPoolStart
                             + ((nation * DatLayout.LeaderPoolNamesPerNation + candidate) * DatLayout.LeaderPoolNameRecordLength);
                names[candidate] = ReadName(data, offset, DatLayout.LeaderPoolNameRecordLength, nation, candidate);
            }

            pools[nation] = names;
        }

        return new DatLeaderPool(pools);
    }

    private static string ReadName(byte[] data, int offset, int length, int nation, int candidate)
    {
        var nul = Array.IndexOf(data, (byte)0, offset, length);
        if (nul < 0)
        {
            throw new InvalidDataException(
                $"The leader pool's nation {nation} candidate {candidate} at {offset:X} is not " +
                $"NUL-terminated within its {length}-byte record.");
        }

        if (nul == offset)
        {
            throw new InvalidDataException(
                $"The leader pool's nation {nation} candidate {candidate} at {offset:X} is empty.");
        }

        for (var p = offset; p < nul; p++)
        {
            if (data[p] < 0x20 || data[p] > 0x7e)
            {
                throw new InvalidDataException(
                    $"The leader pool has non-ASCII leader text at {p:X} (nation {nation} candidate {candidate}).");
            }
        }

        return Encoding.ASCII.GetString(data, offset, nul - offset).Trim();
    }
}
