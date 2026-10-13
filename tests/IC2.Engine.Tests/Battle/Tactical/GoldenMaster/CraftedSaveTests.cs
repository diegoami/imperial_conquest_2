using IC2.Data;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Tests.Import;
using Xunit;

namespace IC2.Engine.Tests.Battle.Tactical.GoldenMaster;

/// <summary>
/// T129 Done-when 4, the crafted saves. <c>c2</c> and <c>c4</c> replay to the same battle (the port plays from the slots,
/// not from the grid), and <c>c5</c>, <c>c6</c>, <c>d5</c> and <c>d6</c> keep their stale or doubled cells where the
/// original's files do (the grid is not repaired). Each crafted series is resumed from its own block under seed 1.
/// </summary>
public sealed class CraftedSaveTests
{
    [SkippableTheory]
    [MemberData(nameof(PlayedRows))]
    public void The_crafted_battle_replays_from_its_slots(string name)
    {
        Skip.IfNot(BattleFixtures.SkipReason.Length == 0, BattleFixtures.SkipReason);

        var spec = GoldenData.Named(name);
        var outcome = GoldenReplay.Run(spec);

        Assert.True(outcome.Passed, outcome.Describe(spec));
    }

    [SkippableFact]
    public void C2_and_c4_give_the_same_battle()
    {
        Skip.IfNot(BattleFixtures.SkipReason.Length == 0, BattleFixtures.SkipReason);

        var c2 = GoldenData.Named("CRAFT_c2_troops");
        var c4 = GoldenData.Named("CRAFT_c4_slot_only");
        var first = GoldenReplay.Run(c2);
        var second = GoldenReplay.Run(c4);

        Assert.True(first.BlocksPassed, first.Describe(c2));
        Assert.True(second.BlocksPassed, second.Describe(c4));
        Assert.Equal(first.Snapshots.Count, second.Snapshots.Count);
        for (var i = 0; i < first.Snapshots.Count; i++)
        {
            var a = first.Snapshots[i];
            var b = second.Snapshots[i];
            Assert.True(
                a.Slots.Equals(b.Slots) && a.Grid.Equals(b.Grid) && a.Counter == b.Counter && a.SideToMove == b.SideToMove && a.Placed == b.Placed,
                $"c2 and c4 differ at half-round {i + 1}");
        }
    }

    [SkippableTheory]
    [MemberData(nameof(GridRows))]
    public void Stale_or_doubled_cells_are_not_repaired(string name)
    {
        Skip.IfNot(BattleFixtures.SkipReason.Length == 0, BattleFixtures.SkipReason);

        var spec = GoldenData.Named(name);
        var context = TacticalContext.From(RealGameData.Ruleset);

        // The fixture really is crafted: some original file's grid differs from the icon rule over its own slots
        // (otherwise the replay below would not show that the port keeps the original's cells).
        var staleFiles = spec.Files.Count(f => !GridFollowsRule(BattleFixtures.Block(f), context));
        Assert.True(staleFiles > 0, $"{name}: every original file's grid follows the icon rule; the series is not crafted");

        var outcome = GoldenReplay.Run(spec);

        Assert.True(outcome.BlocksPassed, outcome.Describe(spec));
    }

    private static bool GridFollowsRule(SaveBattleBlock block, TacticalContext context)
    {
        var expected = Enumerable.Repeat((int)TacticalIcon.Empty, SaveBattleBlock.Width * SaveBattleBlock.Height).ToArray();
        for (var i = 0; i < SaveBattleBlock.SlotCount; i++)
        {
            var s = block.Slots[i];
            if (s.Troops > 0 && s.X is >= 0 and < SaveBattleBlock.Width && s.Y is >= 0 and < SaveBattleBlock.Height)
            {
                expected[(s.X * SaveBattleBlock.Height) + s.Y] =
                    TacticalIcon.Code(i < SaveBattleBlock.SlotCount / 2 ? 0 : 1, s.Type, s.Troops, context.StandardOf(s.Type));
            }
        }

        return expected.SequenceEqual(block.Grid.Select(c => (int)c));
    }

    public static IEnumerable<object[]> PlayedRows() => GoldenData.CraftedPlayedRows();

    public static IEnumerable<object[]> GridRows() => GoldenData.CraftedGridRows();
}
