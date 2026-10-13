using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace IC2.Data;

/// <summary>One of the 40 unit slots of a <see cref="SaveBattleBlock"/>: 22 little-endian words, the
/// first ten numeric fields and a 24-byte name (decompile report §1, "Unit slot"). Every field is read
/// exactly as stored; nothing is validated or repaired, because the golden master compares the port
/// against what the original wrote, stale cells and out-of-range values included.</summary>
/// <param name="X">Word 0, column.</param>
/// <param name="Y">Word 1, row.</param>
/// <param name="Origin">Word 2, origin label: 0 national, non-zero mercenary.</param>
/// <param name="Type">Word 3, unit type: 0 LI, 1 HI, 2 archers, 3 LC, 4 HC.</param>
/// <param name="Troops">Word 4, troops; 0 means not in the battle.</param>
/// <param name="Quality">Word 5.</param>
/// <param name="Morale">Word 6, battle morale.</param>
/// <param name="Moves">Word 7, moves left this half-round.</param>
/// <param name="Shots">Word 8, shots left for the battle (signed: the general can drive it negative).</param>
/// <param name="Target">Word 9, melee target slot, -1 for none.</param>
/// <param name="NameBytes">Words 10-21, the 24 raw name bytes.</param>
public sealed record SaveBattleSlot(
    short X,
    short Y,
    short Origin,
    short Type,
    short Troops,
    short Quality,
    short Morale,
    short Moves,
    short Shots,
    short Target,
    IReadOnlyList<byte> NameBytes)
{
    /// <summary>The name: the raw bytes up to the first 0 (or all 24), read as ASCII.</summary>
    public string Name
    {
        get
        {
            var length = 0;
            while (length < NameBytes.Count && NameBytes[length] != 0)
            {
                length++;
            }

            var bytes = new byte[length];
            for (var i = 0; i < length; i++)
            {
                bytes[i] = NameBytes[i];
            }

            return Encoding.ASCII.GetString(bytes);
        }
    }
}

/// <summary>The tactical-battle block an original save carries after its 55-byte trailer when it was
/// written during a battle (trailer flag <c>+54</c> = 1): 9 header bytes, 40 slots of 44 bytes and a
/// 14 x 12 grid of 16-bit icon codes, 2,105 bytes in all. Layout:
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-04-decompiled-tactical-battle-rules.md §1
/// and https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-04-battle-probe.md item 2;
/// the research repository's <c>scripts/battle-block-check.py</c> reads it independently.
/// <para>The grid is indexed <c>x * Height + y</c>. Slots 0-19 are the attacker's, 20-39 the
/// defender's.</para></summary>
public sealed class SaveBattleBlock
{
    /// <summary>The block's length in bytes.</summary>
    public const int Length = 2105;

    /// <summary>Header bytes: attacker army (2), defender army (2), side (2), placed (1), counter (2).</summary>
    public const int HeaderLength = 9;

    /// <summary>Unit slots in the block.</summary>
    public const int SlotCount = 40;

    /// <summary>Bytes per unit slot.</summary>
    public const int SlotLength = 44;

    /// <summary>Grid columns.</summary>
    public const int Width = 14;

    /// <summary>Grid rows.</summary>
    public const int Height = 12;

    /// <summary>Bytes in a slot's name field.</summary>
    public const int NameLength = 24;

    private SaveBattleBlock(
        short attackerArmy, short defenderArmy, short side, byte placed, short counter,
        SaveBattleSlot[] slots, short[] grid, byte[] raw)
    {
        AttackerArmy = attackerArmy;
        DefenderArmy = defenderArmy;
        Side = side;
        Placed = placed;
        Counter = counter;
        Slots = Array.AsReadOnly(slots);
        Grid = Array.AsReadOnly(grid);
        Raw = Array.AsReadOnly(raw);
    }

    /// <summary>Header bytes 0-1: the attacking army's index.</summary>
    public short AttackerArmy { get; }

    /// <summary>Header bytes 2-3: the defending army's index.</summary>
    public short DefenderArmy { get; }

    /// <summary>Header bytes 4-5: the side to move, 0 attacker, 1 defender.</summary>
    public short Side { get; }

    /// <summary>Header byte 6: placement finished, as stored (0 or 1).</summary>
    public byte Placed { get; }

    /// <summary>Header bytes 7-8: the half-round counter.</summary>
    public short Counter { get; }

    /// <summary>The 40 unit slots.</summary>
    public IReadOnlyList<SaveBattleSlot> Slots { get; }

    /// <summary>The 168 icon codes, <c>cell(x, y) = [x * 12 + y]</c>.</summary>
    public IReadOnlyList<short> Grid { get; }

    /// <summary>The block's 2,105 raw bytes.</summary>
    public IReadOnlyList<byte> Raw { get; }

    /// <summary>Parses a block from exactly <see cref="Length"/> bytes.</summary>
    /// <exception cref="InvalidDataException">The span is not <see cref="Length"/> bytes.</exception>
    public static SaveBattleBlock Parse(ReadOnlySpan<byte> block)
    {
        if (block.Length != Length)
        {
            throw new InvalidDataException($"A battle block is {Length} bytes; got {block.Length}.");
        }

        var slots = new SaveBattleSlot[SlotCount];
        for (var i = 0; i < SlotCount; i++)
        {
            var o = HeaderLength + (i * SlotLength);
            slots[i] = new SaveBattleSlot(
                ReadShort(block, o), ReadShort(block, o + 2), ReadShort(block, o + 4), ReadShort(block, o + 6),
                ReadShort(block, o + 8), ReadShort(block, o + 10), ReadShort(block, o + 12), ReadShort(block, o + 14),
                ReadShort(block, o + 16), ReadShort(block, o + 18),
                Array.AsReadOnly(block.Slice(o + 20, NameLength).ToArray()));
        }

        var gridStart = HeaderLength + (SlotCount * SlotLength);
        var grid = new short[Width * Height];
        for (var i = 0; i < grid.Length; i++)
        {
            grid[i] = ReadShort(block, gridStart + (i * 2));
        }

        return new SaveBattleBlock(
            ReadShort(block, 0), ReadShort(block, 2), ReadShort(block, 4), block[6], ReadShort(block, 7),
            slots, grid, block.ToArray());
    }

    private static short ReadShort(ReadOnlySpan<byte> data, int offset) =>
        BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));
}
