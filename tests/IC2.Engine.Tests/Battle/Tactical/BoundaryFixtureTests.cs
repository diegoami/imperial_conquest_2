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

        // The recorded human nation uses Computer general. Issue 1104 separately concerns that
        // control distinction. Keep the imported path and also isolate the existing computer gate
        // with a test-only control flag; no gameplay control predicate is changed by T174.
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
}

