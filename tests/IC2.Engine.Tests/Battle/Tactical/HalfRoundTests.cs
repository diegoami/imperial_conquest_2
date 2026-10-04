using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical;

/// <summary>
/// The half-round setup, <c>FUN_00439968</c> (also the resume path): report
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §3 ("Half-round setup"), golden-master check 4;
/// <c>docs/game-design.md</c>, "The half-round", "Setup".
/// </summary>
public class HalfRoundSetupTests
{
    private static TacticalBattleState Board(int enemyY, int counter, bool computer, bool general = false) =>
        Arena(
            new[]
            {
                (0, Unit(LI, 0, 0, 5000, moves: 0, target: D0)),
                (1, Unit(HI, 1, 0, 5000, moves: 0, target: D0)),
                (2, Unit(LC, 2, 0, 5000, moves: 0)),
                (D0, Unit(LI, 0, enemyY, 5000, moves: 0, target: 0)),
            },
            counter: counter,
            attackerComputer: computer,
            attackerGeneral: general);

    [Fact]
    public void Moves_are_reset_to_the_stat_and_targets_cleared_for_the_side_to_move_only()
    {
        var state = Board(enemyY: 5, counter: 3, computer: false);
        var set = TacticalHalfRound.Setup(state, Context);

        Assert.Equal(Context.MovesOf(LI), set.Slots[0].Moves);
        Assert.Equal(4, set.Slots[0].Moves);
        Assert.Equal(2, set.Slots[1].Moves);
        Assert.Equal(6, set.Slots[2].Moves);
        Assert.All(new[] { 0, 1, 2 }, s => Assert.Equal(TacticalSlot.NoTarget, set.Slots[s].Target));

        // The enemy's slot is not touched.
        Assert.Equal(0, set.Slots[D0].Moves);
        Assert.Equal(0, set.Slots[D0].Target);
        Assert.Equal(4, set.Counter);
    }

    [Theory]
    [InlineData(5, 3, 1)] // dmin 5 > 2, counter 3 < 10: the slow advance
    [InlineData(3, 3, 1)] // dmin 3 > 2
    [InlineData(2, 3, 4)] // dmin 2 is not > 2: full moves
    [InlineData(5, 9, 1)] // counter 9, read before its increment, is below 10
    [InlineData(5, 10, 4)] // counter 10 is not below 10
    public void A_computer_sides_non_HI_units_get_1_move_while_dmin_is_above_2_and_the_counter_below_10(
        int enemyY, int counter, int expectedLiMoves)
    {
        var set = TacticalHalfRound.Setup(Board(enemyY, counter, computer: true), Context);

        Assert.Equal(expectedLiMoves, set.Slots[0].Moves);
        Assert.Equal(expectedLiMoves == 1 ? 1 : 6, set.Slots[2].Moves);

        // Heavy infantry is exempt: always its stat.
        Assert.Equal(2, set.Slots[1].Moves);
        Assert.Equal(counter + 1, set.Counter);
    }

    [Fact]
    public void A_human_side_never_gets_the_slow_advance_even_with_Computer_general_on()
    {
        Assert.Equal(4, TacticalHalfRound.Setup(Board(5, 3, computer: false), Context).Slots[0].Moves);
        Assert.Equal(4, TacticalHalfRound.Setup(Board(5, 3, computer: false, general: true), Context).Slots[0].Moves);
    }

    [Fact]
    public void The_general_claims_are_cleared_and_the_counter_incremented()
    {
        var state = Board(5, 3, computer: true) with { GeneralClaims = ValueList.From(Enumerable.Repeat(2, Slots)) };
        var set = TacticalHalfRound.Setup(state, Context);

        Assert.All(set.GeneralClaims, c => Assert.Equal(0, c));
        Assert.Equal(4, set.Counter);
        Assert.Equal(new TacticalSetUpEvent(0, 4, 5), set.Log[^1]);
    }

    [Fact]
    public void Resume_is_the_setup_run_again_so_the_counter_moves_and_a_pending_target_is_lost()
    {
        var state = Board(5, 7, computer: false);
        var resumed = TacticalHalfRound.Resume(state, Context);

        Assert.Equal(TacticalHalfRound.Setup(state, Context), resumed);
        Assert.Equal(8, resumed.Counter);
        Assert.Equal(TacticalSlot.NoTarget, resumed.Slots[0].Target);
    }
}

/// <summary>
/// The half-round end, <c>FUN_00439C20</c>: report <c>2026-10-04-decompiled-tactical-battle-rules.md</c>
/// §3 ("Half-round end"); <c>docs/game-design.md</c>, "The half-round", "End".
/// </summary>
public class HalfRoundEndTests
{
    [Fact]
    public void During_placement_the_defender_hands_to_the_attacker_and_the_attacker_sets_placed_without_changing_side()
    {
        var defenderPlacing = Arena(
            new[] { (0, Unit(LI, 0, 0, 5000)), (D0, Unit(LI, 0, 11, 5000)) },
            sideToMove: 1,
            placed: false,
            counter: 1);

        var attackerPlacing = TacticalHalfRound.End(defenderPlacing, Context, NoDraws.Instance);
        Assert.Equal(0, attackerPlacing.SideToMove);
        Assert.False(attackerPlacing.Placed);
        Assert.Equal(2, attackerPlacing.Counter);

        var attackerMoving = TacticalHalfRound.End(attackerPlacing, Context, NoDraws.Instance);
        Assert.Equal(0, attackerMoving.SideToMove);
        Assert.True(attackerMoving.Placed);
        Assert.Equal(3, attackerMoving.Counter);

        var defenderMoving = TacticalHalfRound.End(attackerMoving, Context, NoDraws.Instance);
        Assert.Equal(1, defenderMoving.SideToMove);
        Assert.Equal(4, defenderMoving.Counter);

        var attackerAgain = TacticalHalfRound.End(defenderMoving, Context, NoDraws.Instance);
        Assert.Equal(0, attackerAgain.SideToMove);
        Assert.Equal(5, attackerAgain.Counter);
    }

    [Fact]
    public void The_melee_resolved_is_the_side_that_just_moved_and_the_other_sides_targets_wait()
    {
        // Both sides hold targets; side 0 just moved. Only side 0's exchange is fought (four draws), and
        // the next setup (side 1) clears side 1's target before it could fight.
        var state = Arena(
            (0, Unit(HI, 3, 3, 5000, target: D0)),
            (D0, Unit(HI, 3, 4, 5000, target: 0)));

        var draws = new ScriptedDraws(0, 0, 0, 0);
        var ended = TacticalHalfRound.End(state, Context, draws);

        Assert.Equal(4, draws.Calls.Count);
        var melee = Assert.Single(ended.Log.OfType<TacticalMeleeEvent>());
        Assert.Equal((0, D0), (melee.Attacker, melee.Defender));
        Assert.Equal(1, ended.SideToMove);
        Assert.Equal(TacticalSlot.NoTarget, ended.Slots[D0].Target);
        Assert.Equal(4, ended.Counter);
    }

    [Fact]
    public void No_setup_follows_when_the_melee_ends_the_battle()
    {
        // The defender HI of 100 troops is below its floor 6,000 div 25 = 240 after any loss: removed, and
        // with it the defender's last unit.
        var state = Arena(
            (0, Unit(HI, 3, 3, 5000, target: D0)),
            (D0, Unit(HI, 3, 4, 100)));

        var ended = TacticalHalfRound.End(state, Context, new ScriptedDraws(0, 0, 0, 0));

        Assert.True(ended.IsOver);
        Assert.Equal(3, ended.Counter);
        Assert.DoesNotContain(ended.Log, e => e is TacticalSetUpEvent);
    }
}
