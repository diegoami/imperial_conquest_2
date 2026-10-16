using IC2.Engine.Battle.Tactical;
using IC2.Engine.Import;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle.Tactical.GoldenMaster;
using IC2.Engine.Tests.Import;
using Xunit;

namespace IC2.Engine.Tests.Battle.Tactical;

public class BoundaryFixtureTests
{
    [SkippableTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hi_hi_three_half_s1_BATTLE05_slot_zero_stops_without_a_target(bool isolateComputerPath)
    {
        Skip.IfNot(BattleFixtures.SkipReason.Length == 0, BattleFixtures.SkipReason);
        const string file = "hook/hi-hi-three-half_s1_r5_hook_BATTLE04.SAV";
        var before = BattleFixtures.Block(file);
        var expected = BattleFixtures.Block("hook/hi-hi-three-half_s1_r5_hook_BATTLE05.SAV");
        var game = BattleFixtures.Import(file).Save.State;
        var context = TacticalContext.From(RealGameData.Ruleset);
        var state = OriginalBattleBlockImport.Import(before, game, new[] { true, true });
        Assert.False(state.ComputerControlled[0]);
        Assert.True(state.ComputerGeneral[0]);
        var draws = new ScriptedDraws();
        state = TacticalHalfRound.End(state, context, draws);
        Assert.Equal((5, 4, 2), (state.Slots[0].X, state.Slots[0].Y, state.Slots[0].Moves));

        // Preserve T174's boundary under either source of the same OR predicate: the recorded
        // human/general path and a test-only AI-nation control. Neither changes fixture bytes.
        if (isolateComputerPath)
        {
            state = state with { ComputerControlled = ValueList.From(new[] { true, state.ComputerControlled[1] }) };
        }

        var enemy = state.Slots[20];
        Assert.Equal((6, 7), (enemy.X, enemy.Y));
        var after = TacticalMovement.MoveUnit(state, 0, enemy.X, enemy.Y, context, draws);
        var actual = after.Slots[0];
        var original = expected.Slots[0];
        Assert.Equal((6, 6, 0, TacticalSlot.NoTarget),
            ((int)original.X, (int)original.Y, (int)original.Moves, (int)original.Target));
        Assert.Equal(((int)original.X, (int)original.Y, (int)original.Moves, (int)original.Target),
            (actual.X, actual.Y, actual.Moves, actual.Target));
        Assert.Equal(new[] { (5, 5), (6, 6) }, after.Log.OfType<TacticalMovedEvent>()
            .Where(e => e.Slot == 0).Select(e => (e.ToX, e.ToY)).ToArray());
        Assert.DoesNotContain(after.Log.OfType<TacticalTargetSetEvent>(), e => e.Slot == 0);
        Assert.Empty(draws.Calls);
        TacticalTestbed.AssertGridMatchesSlots(after);
    }

    [SkippableFact]
    public void Gate2_a_placement_budget_then_general_on_first_movement_uses_the_OR_setup()
    {
        Skip.IfNot(BattleFixtures.SkipReason.Length == 0, BattleFixtures.SkipReason);
        const string file = "probe/gate2_a_BATTLE02.SAV";
        var block = BattleFixtures.Block(file);
        var moved = BattleFixtures.Block("probe/gate2_a_BATTLE03.SAV");
        var game = BattleFixtures.Import(file).Save.State;
        var context = TacticalContext.From(RealGameData.Ruleset);
        var off = OriginalBattleBlockImport.Import(block, game, new[] { false, false });
        Assert.False(off.ComputerControlled[0]);
        Assert.True(off.ComputerControlled[1]);
        Assert.Equal((0, false, 2), (off.SideToMove, off.Placed, off.Counter));
        Assert.Equal(new[] { 2, 4, 5, 4, 4, 4, 6, 4, 4 }, off.Slots.Take(9).Select(s => s.Moves));
        var full = TacticalHalfRound.Setup(off, context);
        Assert.Equal(off.Slots.Take(9).Select(s => s.Moves), full.Slots.Take(9).Select(s => s.Moves));

        // Enabling after the saved placement setup retains those granted moves. This input is
        // supplied explicitly; no general flag or exact original toggle time can be decoded.
        var on = off with { ComputerGeneral = ValueList.Of(true, false) };
        var firstMovement = TacticalHalfRound.End(on, context, NoDraws.Instance);
        Assert.Equal((0, true, 3), (firstMovement.SideToMove, firstMovement.Placed, firstMovement.Counter));
        Assert.Equal(new[] { 2, 1, 1, 1, 1, 1, 1, 1, 1 }, firstMovement.Slots.Take(9).Select(s => s.Moves));
        Assert.Equal(new[] { 2, 1, 1, 1, 1, 1, 1, 1, 1 }, Enumerable.Range(0, 9).Select(i =>
            Math.Max(Math.Abs(block.Slots[i].X - moved.Slots[i].X), Math.Abs(block.Slots[i].Y - moved.Slots[i].Y))));
        Assert.All(moved.Slots.Take(9), s => Assert.Equal(0, s.Moves));
        Assert.Empty(firstMovement.Log.OfType<TacticalDrawEvent>());
    }
}
