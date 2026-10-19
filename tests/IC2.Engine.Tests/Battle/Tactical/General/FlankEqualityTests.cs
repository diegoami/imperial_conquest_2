using IC2.Engine.Battle.Tactical;
using IC2.Engine.Battle.Tactical.General;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical.General;

/// <summary>FUN_0043AA60 report §D: synthetic rule probes, not original runtime histories.</summary>
public class FlankEqualityTests
{
    [Fact]
    public void Issue1167_equal_rows_cancel_below_and_walk_up_with_seed1()
    {
        var state = Arena((0, Unit(HI, 5, 5, 3000, moves: 2)),
            (20, Unit(HI, 8, 6, 3000)), (21, Unit(HI, 9, 6, 3000)),
            (22, Unit(HI, 8, 5, 3000)), (23, Unit(HI, 9, 5, 3000)));
        var journal = new List<GeneralDecision>();
        var after = GeneralPassThree.Flank(state, Context, new DelphiBattleDraws(1), journal);
        // Seed1 flips captured h=+1. Two below and two equal yield balance0 -> v=-1.
        Assert.Equal(new GeneralDecision(0, GeneralStep.Flank, -1, 3, 3), Assert.Single(journal));
        Assert.Equal(new[] { (4, 4), (3, 3) }, GeneralTestbed.Path(after, 0));
        Assert.Equal((3, 3, 0), (after.Slots[0].X, after.Slots[0].Y, after.Slots[0].Moves));
        Assert.Equal(new[] { (3, 0) }, DrawsOn(after));
        var repeatedJournal = new List<GeneralDecision>();
        var repeated = GeneralPassThree.Flank(state, Context, new DelphiBattleDraws(1), repeatedJournal);
        Assert.Equal(after, repeated);
        Assert.Equal(journal, repeatedJournal);
        Assert.Equal(DrawsOn(after), DrawsOn(repeated));
    }

    [Theory]
    [InlineData(new[] { 2, 3 }, 3)] // strictly above, negative
    [InlineData(new[] { 6, 7 }, 7)] // strictly below, positive
    [InlineData(new[] { 5, 5 }, 3)] // all equal, negative
    [InlineData(new[] { 6, 5 }, 3)] // equality creates zero, formerly positive
    [InlineData(new[] { 6, 6, 5 }, 7)] // equality still permits positive
    [InlineData(new[] { 6, 5, 5 }, 3)] // mixed negative
    [InlineData(new[] { 2, 6 }, 3)] // zero without equality
    [InlineData(new int[] { }, 3)] // empty table, zero
    public void Vertical_balance_counts_equal_rows_with_above(int[] rows, int aimY)
    {
        var units = rows.Select((y, i) => (20 + i, Unit(HI, 8 + i, y, 3000))).ToList();
        units.Add((0, Unit(HI, 5, 5, 3000)));
        var draws = new ScriptedDraws(0);
        var journal = new List<GeneralDecision>();
        var after = GeneralPassThree.Flank(Arena(units), Context, draws, journal);
        Assert.Equal(aimY, Assert.Single(journal).Y);
        Assert.Equal(aimY, after.Slots[0].Y);
        Assert.Equal(2, GeneralTestbed.Path(after, 0).Length);
        Assert.Equal(new[] { (3, 0) }, draws.Calls);
        Assert.Equal(0, draws.Remaining);
    }

    [Fact]
    public void Dead_equal_row_enemies_do_not_cancel_live_below()
    {
        var state = Arena((0, Unit(HI, 5, 5, 3000)), (20, Unit(HI, 8, 6, 3000)),
            (21, Unit(HI, 9, 5, 0)), (22, Unit(HI, 10, 5, 0)));
        var journal = new List<GeneralDecision>();
        var after = GeneralPassThree.Flank(state, Context, new ScriptedDraws(0), journal);
        Assert.Equal(new GeneralDecision(0, GeneralStep.Flank, -1, 3, 7), Assert.Single(journal));
        Assert.Equal(new[] { (4, 6), (3, 7) }, GeneralTestbed.Path(after, 0));
    }

    [Theory]
    [InlineData(0, -1)]
    [InlineData(1, -1)]
    [InlineData(2, 20)]
    public void Exhausted_ineligible_or_targeted_units_do_not_draw(int moves, int target)
    {
        var state = Arena((0, Unit(HI, 5, 5, 3000, moves: moves, target: target)),
            (20, Unit(HI, 8, 5, 3000)));
        var journal = new List<GeneralDecision>();
        Assert.Equal(state, GeneralPassThree.Flank(state, Context, NoDraws.Instance, journal));
        Assert.Empty(journal);
    }
}

