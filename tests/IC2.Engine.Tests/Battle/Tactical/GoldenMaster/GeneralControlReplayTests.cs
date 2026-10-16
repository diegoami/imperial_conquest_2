using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle.Tactical;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical.GoldenMaster;

public class GeneralControlReplayTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void Recorded_toolbar_input_follows_setup_and_only_enables_the_current_human(int side, bool nation)
    {
        var off = Arena(new[] { (0, Unit(LI, 0, 0, 3000)), (D0, Unit(LI, 13, 11, 3000)) },
            sideToMove: side, placed: false, counter: 1,
            attackerComputer: side == 0 && nation, defenderComputer: side == 1 && nation);
        var setup = TacticalHalfRound.Setup(off, Context);
        var input = GoldenReplay.EnableRecordedHumanGeneral(setup);
        Assert.Equal(nation ? 1 : 4, input.Slots[side * D0].Moves);
        Assert.Equal(!nation, input.ComputerGeneral[side]);
        Assert.False(input.ComputerGeneral[1 - side]);
        Assert.Equal(setup.Slots, input.Slots);
        Assert.Equal(setup.Counter, input.Counter);
        Assert.Equal(setup.Log, input.Log);
        Assert.Equal(input, GoldenReplay.EnableRecordedHumanGeneral(input));
    }

    [SkippableFact]
    public void Gate2_replay_keeps_full_human_placement_moves_then_sets_up_slow_first_movement()
    {
        Skip.IfNot(BattleFixtures.SkipReason.Length == 0, BattleFixtures.SkipReason);
        var outcome = GoldenReplay.Run(SeriesCatalogue.Gate("gate2_a", 1));
        Assert.True(outcome.Snapshots.Count >= 2, outcome.Describe(SeriesCatalogue.Gate("gate2_a", 1)));
        var defender = outcome.Snapshots[0];
        var human = outcome.Snapshots[1];
        Assert.Equal((1, 1), (defender.SideToMove, defender.Counter));
        Assert.All(defender.ComputerGeneral, flag => Assert.False(flag));
        Assert.Equal((0, 2), (human.SideToMove, human.Counter));
        Assert.True(human.ComputerGeneral[0]);
        Assert.False(human.ComputerGeneral[1]);
        Assert.Equal(new[] { 2, 4, 5, 4, 4, 4, 6, 4, 4 }, human.Slots.Take(9).Select(s => s.Moves));
        var moving = TacticalHalfRound.End(human, outcome.Context, NoDraws.Instance);
        Assert.Equal(new[] { 2, 1, 1, 1, 1, 1, 1, 1, 1 }, moving.Slots.Take(9).Select(s => s.Moves));
        Assert.Equal((0, true, 3), (moving.SideToMove, moving.Placed, moving.Counter));
    }

    [SkippableFact]
    public void Crafted_d0_replay_resumes_with_explicit_fresh_process_flags_before_toolbar_enable()
    {
        Skip.IfNot(BattleFixtures.SkipReason.Length == 0, BattleFixtures.SkipReason);
        var spec = SeriesCatalogue.Crafted("d0_control");
        var outcome = GoldenReplay.Run(spec);
        Assert.NotEmpty(outcome.Snapshots);
        var first = outcome.Snapshots[0];
        Assert.True(first.ComputerGeneral[first.SideToMove]);
        Assert.Equal(4, first.Slots[1].Moves);
        Assert.Null(BlockComparison.First(first, BattleFixtures.Block(spec.Files[0])));
    }
}
