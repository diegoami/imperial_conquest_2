using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace IC2.Data.Tests;

/// <summary>
/// T129 Done-when 1: the original's tactical-battle block saves parse. Local-only: the saves are in the private
/// fixtures repository's <c>battles/</c> folder (<c>IC2_FIXTURES_DIR</c>), and every test skips with an explicit result
/// when it is absent, as the rest of this project does. Layout:
/// https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-04-decompiled-tactical-battle-rules.md §1
/// and §10; the research repository's <c>scripts/battle-block-check.py</c> reads it independently.
/// </summary>
public class SaveBattleBlockTests
{
    /// <summary>134,254 bytes: the 132,149-byte base save plus the 2,105-byte block (battle-probe.md item 2).</summary>
    private const int BlockSaveLength = 134_254;

    private const int BaseSaveLength = 132_149;

    /// <summary>The standard battalion size by type (LI, HI, archers, LC, HC; decompile report §10), the icon size's divisor.</summary>
    private static readonly int[] StandardBattalion = { 15000, 6000, 3500, 7000, 2500 };

    /// <summary>The start shots by type (stat <c>+0x1C</c>: 7/0/25/9/0, the task's Done-when 1).</summary>
    private static readonly int[] StartShots = { 7, 0, 25, 9, 0 };

    /// <summary>The four crafted series whose stale or doubled cells break the icon rule on purpose (sweep B3).</summary>
    private static readonly string[] CraftedToBreakTheGrid = { "c5_stale_grid", "c6_same_cell", "d5_stale_grid", "d6_same_cell" };

    private static string? BattlesDirectory =>
        LocalAssets.Settings is { } settings && Directory.Exists(Path.Combine(settings.DirectoryPath, "battles"))
            ? Path.Combine(settings.DirectoryPath, "battles")
            : null;

    private static string SkipReason =>
        !LocalAssets.IsConfigured ? LocalAssets.SkipReason : "the fixtures repository has no battles/ folder.";

    private static bool Available => LocalAssets.IsConfigured && BattlesDirectory is not null;

    /// <summary>Every save in <c>battles/</c> that carries a block: all but the strategic start saves and the post-battle saves.</summary>
    private static List<string> BlockSaves() =>
        Directory.GetFiles(BattlesDirectory!, "*.SAV", SearchOption.AllDirectories)
            .Where(f => !Path.GetRelativePath(BattlesDirectory!, f).StartsWith("starts", StringComparison.Ordinal)
                        && !f.EndsWith("_post.SAV", StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

    private static bool CraftedToBreakGrid(string path) =>
        CraftedToBreakTheGrid.Any(c => Path.GetFileName(path).Contains(c, StringComparison.Ordinal));

    [SkippableFact]
    public void Every_block_save_parses_and_is_134254_bytes_with_a_2105_byte_block()
    {
        Skip.IfNot(Available, SkipReason);

        var saves = BlockSaves();
        Assert.NotEmpty(saves);
        foreach (var path in saves)
        {
            var data = File.ReadAllBytes(path);
            Assert.True(data.Length == BlockSaveLength, $"{Path.GetFileName(path)}: {data.Length} bytes, expected {BlockSaveLength}");

            var block = SaveTurnState.Parse(data).GetBattleBlock();

            Assert.True(block is not null, $"{Path.GetFileName(path)}: no battle block parsed");
            Assert.Equal(SaveBattleBlock.Length, block!.Raw.Count);
            Assert.Equal(SaveBattleBlock.SlotCount, block.Slots.Count);
            Assert.Equal(SaveBattleBlock.Width * SaveBattleBlock.Height, block.Grid.Count);
            Assert.True(
                data.AsSpan(data.Length - SaveBattleBlock.Length).SequenceEqual(block.Raw.ToArray()),
                $"{Path.GetFileName(path)}: the parsed block is not the file's last 2,105 bytes");
        }
    }

    [SkippableFact]
    public void A_save_without_a_battle_flag_exposes_no_block()
    {
        Skip.IfNot(Available, SkipReason);

        var blockSaves = BlockSaves().ToHashSet(StringComparer.Ordinal);
        var withoutBlock = Directory.GetFiles(BattlesDirectory!, "*.SAV", SearchOption.AllDirectories)
            .Where(f => !blockSaves.Contains(f))
            .ToList();
        Assert.NotEmpty(withoutBlock);
        foreach (var path in withoutBlock)
        {
            var data = File.ReadAllBytes(path);
            Assert.True(data.Length == BaseSaveLength, $"{Path.GetFileName(path)}: {data.Length} bytes, expected {BaseSaveLength}");
            Assert.Null(SaveTurnState.Parse(data).GetBattleBlock());
        }
    }

    [SkippableFact]
    public void The_icon_grid_follows_the_rule_in_every_save_not_crafted_to_break_it()
    {
        Skip.IfNot(Available, SkipReason);

        var checkedSaves = 0;
        var broken = new List<string>();
        foreach (var path in BlockSaves())
        {
            var block = SaveTurnState.Parse(File.ReadAllBytes(path)).GetBattleBlock()!;
            var follows = IconGridFollowsRule(block);
            if (CraftedToBreakGrid(path))
            {
                if (!follows)
                {
                    broken.Add(Path.GetFileName(path));
                }

                continue;
            }

            Assert.True(follows, $"{Path.GetFileName(path)}: the icon grid is not type x 3 + size (+20) computed from the slots");
            checkedSaves++;
        }

        Assert.True(checkedSaves > 0);

        // The crafted four really do break the rule (the report's 18 crafted saves: the 4 start saves and 14 series files).
        Assert.Equal(18, broken.Count);
    }

    [SkippableFact]
    public void The_start_shots_read_7_0_25_9_0_by_type()
    {
        Skip.IfNot(Available, SkipReason);

        // The first file of a fresh battle: copy-in has just set every slot's shots from its type's stat.
        var firstFiles = BlockSaves()
            .Where(f => Path.GetFileName(f).EndsWith("_BATTLE01.SAV", StringComparison.Ordinal)
                        && !Path.GetFileName(f).StartsWith("resume", StringComparison.Ordinal)
                        && !Path.GetRelativePath(BattlesDirectory!, f).StartsWith("crafted", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(firstFiles);
        foreach (var path in firstFiles)
        {
            var block = SaveTurnState.Parse(File.ReadAllBytes(path)).GetBattleBlock()!;
            foreach (var slot in block.Slots.Where(s => s.Troops > 0))
            {
                Assert.True(
                    slot.Shots == StartShots[slot.Type],
                    $"{Path.GetFileName(path)}: a type {slot.Type} unit has {slot.Shots} shots, expected {StartShots[slot.Type]}");
            }
        }
    }

    /// <summary>Whether every cell is 50 (empty) or <c>side x 20 + type x 3 + min(2, troops div (std div 3))</c> of the last live slot at it.</summary>
    private static bool IconGridFollowsRule(SaveBattleBlock block)
    {
        var expected = Enumerable.Repeat(50, SaveBattleBlock.Width * SaveBattleBlock.Height).ToArray();
        for (var i = 0; i < SaveBattleBlock.SlotCount; i++)
        {
            var s = block.Slots[i];
            if (s.Troops > 0 && s.X is >= 0 and < SaveBattleBlock.Width && s.Y is >= 0 and < SaveBattleBlock.Height)
            {
                expected[(s.X * SaveBattleBlock.Height) + s.Y] =
                    (i < SaveBattleBlock.SlotCount / 2 ? 0 : 20)
                    + (s.Type * 3)
                    + Math.Min(2, s.Troops / (StandardBattalion[s.Type] / 3));
            }
        }

        return expected.SequenceEqual(block.Grid.Select(c => (int)c));
    }
}
