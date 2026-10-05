using IC2.Engine.Battle.Tactical;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical;

/// <summary>
/// Movement, <c>FUN_00438D24</c> with its step <c>FUN_00438A6C</c>, and <c>MoveHumanUnit</c>: report
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §3 ("Movement", "The human second call");
/// <c>docs/game-design.md</c>, "Movement".
/// </summary>
public class MovementTests
{
    private static (int X, int Y)[] Path(TacticalBattleState state, int slot) =>
        state.Log.OfType<TacticalMovedEvent>().Where(m => m.Slot == slot).Select(m => (m.ToX, m.ToY)).ToArray();

    private static TacticalBattleState Walk(TacticalBattleState state, int slot, int x, int y) =>
        TacticalMovement.MoveUnit(state, slot, x, y, Context, NoDraws.Instance);

    [Fact]
    public void An_x_major_line_is_the_Bresenham_walk()
    {
        // (0,0) -> (5,2): major 5, minor 2, err = 4 - 5 = -1. Steps: x (err 3); y+x (err -3); x (err 1);
        // y+x (err -5); x. One move per step: light cavalry 6 -> 1.
        var after = Walk(Arena((0, Unit(LC, 0, 0, 5000)), (D0, Unit(LI, 13, 11, 5000))), 0, 5, 2);

        Assert.Equal(new[] { (1, 0), (2, 1), (3, 1), (4, 2), (5, 2) }, Path(after, 0));
        Assert.Equal(1, after.Slots[0].Moves);
        Assert.Equal(TacticalIcon.Empty, GridAt(after, 0, 0));
        AssertGridMatchesSlots(after);
    }

    [Fact]
    public void A_y_major_line_steps_on_y()
    {
        var after = Walk(Arena((0, Unit(LC, 0, 0, 5000)), (D0, Unit(LI, 13, 11, 5000))), 0, 2, 5);

        Assert.Equal(new[] { (0, 1), (1, 2), (1, 3), (2, 4), (2, 5) }, Path(after, 0));
    }

    [Fact]
    public void The_walk_stops_when_the_moves_fall_below_1()
    {
        var after = Walk(Arena((0, Unit(LI, 0, 0, 5000)), (D0, Unit(LI, 13, 11, 5000))), 0, 5, 2);

        Assert.Equal(new[] { (1, 0), (2, 1), (3, 1), (4, 2) }, Path(after, 0));
        Assert.Equal(0, after.Slots[0].Moves);
    }

    [Fact]
    public void The_walk_stops_when_the_path_reaches_an_occupied_destination()
    {
        var after = Walk(Arena((0, Unit(LC, 0, 5, 5000)), (D0, Unit(LI, 4, 5, 5000))), 0, 4, 5);

        Assert.Equal(new[] { (1, 5), (2, 5), (3, 5) }, Path(after, 0));
        Assert.Equal(3, after.Slots[0].Moves);
    }

    [Fact]
    public void A_blocked_step_along_a_row_tries_y_plus_1_then_y_minus_1_ties_to_the_first()
    {
        // (2,5) -> (6,5); (3,5) is held by a friend. (3,6) and (3,4) both cost 1: the first, (3,6), wins.
        var state = Arena((0, Unit(LC, 2, 5, 5000)), (1, Unit(HI, 3, 5, 5000)), (D0, Unit(LI, 13, 11, 5000)));
        var after = Walk(state, 0, 6, 5);
        Assert.Equal(new[] { (3, 6), (4, 5), (5, 5), (6, 5) }, Path(after, 0));

        // With (3,6) held too, the second alternative is cheaper.
        var blocked = Arena((0, Unit(LC, 2, 5, 5000)), (1, Unit(HI, 3, 5, 5000)), (2, Unit(HI, 3, 6, 5000)), (D0, Unit(LI, 13, 11, 5000)));
        Assert.Equal((3, 4), Path(Walk(blocked, 0, 6, 5), 0)[0]);
    }

    [Fact]
    public void A_blocked_step_along_a_column_tries_x_plus_1_then_x_minus_1()
    {
        var state = Arena((0, Unit(LC, 5, 2, 5000)), (1, Unit(HI, 5, 3, 5000)), (D0, Unit(LI, 13, 11, 5000)));
        Assert.Equal(new[] { (6, 3), (5, 4), (5, 5), (5, 6) }, Path(Walk(state, 0, 5, 6), 0));

        var blocked = Arena((0, Unit(LC, 5, 2, 5000)), (1, Unit(HI, 5, 3, 5000)), (2, Unit(HI, 6, 3, 5000)), (D0, Unit(LI, 13, 11, 5000)));
        Assert.Equal((4, 3), Path(Walk(blocked, 0, 5, 6), 0)[0]);
    }

    [Fact]
    public void A_blocked_diagonal_step_tries_the_units_x_neighbour_first_on_an_x_major_line()
    {
        // (2,2) -> (5,4): major 3, minor 2, err = 1, so the first step is the diagonal (3,3), held.
        // From the unit: (2 + 1, 2) = (3,2) first, then (2, 2 + 1) = (2,3).
        var state = Arena((0, Unit(LC, 2, 2, 5000)), (1, Unit(HI, 3, 3, 5000)), (D0, Unit(LI, 13, 11, 5000)));
        Assert.Equal((3, 2), Path(Walk(state, 0, 5, 4), 0)[0]);
    }

    [Fact]
    public void On_a_y_major_line_the_diagonal_pair_is_swapped()
    {
        // (2,2) -> (4,5): major 3 on y, minor 2, err = 1: the diagonal (3,3), held. Swapped: (2,3) first.
        var state = Arena((0, Unit(LC, 2, 2, 5000)), (1, Unit(HI, 3, 3, 5000)), (D0, Unit(LI, 13, 11, 5000)));
        Assert.Equal((2, 3), Path(Walk(state, 0, 4, 5), 0)[0]);
    }

    [Fact]
    public void An_alternative_off_the_board_costs_10_so_the_second_is_taken()
    {
        // At the board's right edge: (13,2) -> (13,6) blocked at (13,3); (14,3) is off the board.
        var state = Arena((0, Unit(LC, 13, 2, 5000)), (1, Unit(HI, 13, 3, 5000)), (D0, Unit(LI, 0, 11, 5000)));
        Assert.Equal((12, 3), Path(Walk(state, 0, 13, 6), 0)[0]);
    }

    [Fact]
    public void When_every_choice_costs_10_the_walk_stops_and_spends_nothing()
    {
        var state = Arena(
            (0, Unit(LC, 5, 2, 5000)),
            (1, Unit(HI, 5, 3, 5000)),
            (2, Unit(HI, 6, 3, 5000)),
            (3, Unit(HI, 4, 3, 5000)),
            (D0, Unit(LI, 13, 11, 5000)));

        var after = Walk(state, 0, 5, 6);

        Assert.Empty(Path(after, 0));
        Assert.Equal(6, after.Slots[0].Moves);
    }

    [Fact]
    public void A_cell_more_than_1_from_the_unit_costs_10_so_a_detour_can_strand_the_walk_and_the_human_walks_again()
    {
        // (0,0) -> (4,4), the diagonal, with (1,1) held. The detour takes (1,0). The cursor's next cell
        // (2,2) is empty but 2 from the unit, so it costs 10; the diagonal pair from the unit, (2,0) (2
        // from the cursor) and (1,1) (held), cost 10 too: the walk stops, not arrived.
        var state = Arena((0, Unit(LC, 0, 0, 5000)), (1, Unit(HI, 1, 1, 5000)), (D0, Unit(LI, 13, 11, 5000)));

        var once = Walk(state, 0, 4, 4);
        Assert.Equal(new[] { (1, 0) }, Path(once, 0));
        Assert.Equal(5, once.Slots[0].Moves);

        // MoveHumanUnit walks again from (1,0): (1,0) -> (4,4) is y-major, major 4, minor 3, err = 2:
        // (2,1), (3,2), then err -2 gives the straight (3,3), then (4,4).
        var human = TacticalMovement.MoveHumanUnit(state, 0, 4, 4, Context, NoDraws.Instance);
        Assert.Equal(new[] { (1, 0), (2, 1), (3, 2), (3, 3), (4, 4) }, Path(human, 0));
        Assert.Equal(1, human.Slots[0].Moves);
    }

    [Fact]
    public void A_human_move_clears_the_target_and_draws_nothing()
    {
        var state = Arena((0, Unit(LC, 0, 0, 5000, target: D0)), (D0, Unit(LI, 13, 11, 5000)));

        var after = TacticalMovement.MoveHumanUnit(state, 0, 2, 0, Context, NoDraws.Instance);

        Assert.Equal(TacticalSlot.NoTarget, after.Slots[0].Target);
        Assert.Equal((2, 0), (after.Slots[0].X, after.Slots[0].Y));
        Assert.Empty(after.Log.OfType<TacticalDrawEvent>());
    }

    [Fact]
    public void A_human_unit_with_a_target_still_moves_and_a_human_unit_never_shoots_on_the_way()
    {
        // Light infantry with shots walks up to an enemy at its destination: no shot, no target.
        var state = Arena((0, Unit(LI, 0, 5, 5000, target: D0 + 1)), (D0, Unit(LI, 3, 5, 5000)), (D0 + 1, Unit(LI, 9, 9, 5000)));

        var after = Walk(state, 0, 3, 5);

        Assert.Equal(new[] { (1, 5), (2, 5) }, Path(after, 0));
        Assert.Empty(after.Log.OfType<TacticalShotEvent>());
        Assert.Equal(D0 + 1, after.Slots[0].Target);
    }

    [Fact]
    public void A_computer_unit_with_a_target_does_not_move()
    {
        var state = Arena(new[] { (0, Unit(LC, 0, 0, 5000, target: D0)), (D0, Unit(LI, 13, 11, 5000)) }, attackerComputer: true);

        var after = Walk(state, 0, 3, 0);

        Assert.Empty(Path(after, 0));
        Assert.Equal(6, after.Slots[0].Moves);
    }

    [Fact]
    public void A_computer_unit_gives_up_its_last_move_next_to_an_enemy_at_least_as_strong()
    {
        // HI 1,000 q5 m70 with 1 move, heading (5,5) -> (5,8); the next cell (5,6) has the enemy LI at
        // (6,7) in its 3 x 3. S(enemy) = (15,000 x 9 div 100) x 99 x M[LI][HI] = 1,350 x 99 x 4 = 534,600;
        // S(unit) = (1,000 x 5 div 100) x 70 x M[HI][LI] = 50 x 70 x 60 = 210,000. 534,600 >= 210,000.
        var state = Arena(
            new[] { (0, Unit(HI, 5, 5, 1000, quality: 5, morale: 70, moves: 1)), (D0, Unit(LI, 6, 7, 15000, quality: 9, morale: 99)) },
            attackerComputer: true);

        var after = Walk(state, 0, 5, 8);

        Assert.Empty(Path(after, 0));
        Assert.Equal(0, after.Slots[0].Moves);
        Assert.Contains(new TacticalMoveForfeitedEvent(0), after.Log);
    }

    [Fact]
    public void The_forfeit_counts_ties_as_at_least_as_strong_and_ignores_a_weaker_enemy()
    {
        // A weaker enemy: LI 1,000 q5 m70: S = 50 x 70 x 4 = 14,000 < 210,000. The unit steps.
        var weaker = Arena(
            new[] { (0, Unit(HI, 5, 5, 1000, quality: 5, morale: 70, moves: 1)), (D0, Unit(LI, 6, 7, 1000, quality: 5, morale: 70)) },
            attackerComputer: true);
        Assert.Equal(new[] { (5, 6) }, Path(Walk(weaker, 0, 5, 8), 0));

        // An equal one: HI against HI with the same troops, quality and morale: S equal, so it forfeits.
        var equal = Arena(
            new[] { (0, Unit(HI, 5, 5, 1000, quality: 5, morale: 70, moves: 1)), (D0, Unit(HI, 6, 7, 1000, quality: 5, morale: 70)) },
            attackerComputer: true);
        Assert.Empty(Path(Walk(equal, 0, 5, 8), 0));
    }

    [Fact]
    public void Only_the_last_move_is_forfeited_and_a_human_never_forfeits()
    {
        // With 2 moves the first step is taken; the forfeit comes before the second, the last.
        var two = Arena(
            new[] { (0, Unit(HI, 5, 4, 1000, quality: 5, morale: 70, moves: 2)), (D0, Unit(LI, 6, 7, 15000, quality: 9, morale: 99)) },
            attackerComputer: true);
        var after = Walk(two, 0, 5, 8);
        Assert.Equal(new[] { (5, 5) }, Path(after, 0));
        Assert.Equal(0, after.Slots[0].Moves);

        var human = Arena((0, Unit(HI, 5, 5, 1000, quality: 5, morale: 70, moves: 1)), (D0, Unit(LI, 6, 7, 15000, quality: 9, morale: 99)));
        Assert.Equal(new[] { (5, 6) }, Path(Walk(human, 0, 5, 8), 0));
    }

    [Fact]
    public void A_computer_unit_adjacent_to_the_enemy_at_its_destination_with_no_shots_takes_it_as_target_and_stops()
    {
        var state = Arena(
            new[] { (0, Unit(LI, 5, 5, 3000, shots: 0)), (D0, Unit(HI, 5, 6, 6000, morale: 99)) },
            attackerComputer: true);

        var after = Walk(state, 0, 5, 6);

        Assert.Equal(D0, after.Slots[0].Target);
        Assert.Equal(4, after.Slots[0].Moves);
        Assert.Empty(Path(after, 0));
    }

    [Fact]
    public void A_computer_unit_in_range_shoots_and_checks_again_until_it_has_fewer_than_2_moves_then_takes_the_target()
    {
        // LI 3,000 q5 m70 adjacent to HI (vuln 2): base = 3,000 x 5 x 70 x 2 div 165,000 = 12; n = 13.
        // Moves 4: shoot (3), shoot (2), shoot (1); then moves < 2 and not archers: the target, stop.
        var state = Arena(
            new[] { (0, Unit(LI, 5, 5, 3000, quality: 5, morale: 70)), (D0, Unit(HI, 5, 6, 6000, morale: 99)) },
            attackerComputer: true);

        var draws = new ScriptedDraws(0, 0, 0, 0, 0, 0);
        var after = TacticalMovement.MoveUnit(state, 0, 5, 6, Context, draws);

        Assert.Equal(new[] { 13, 13, 13, 13, 13, 13 }, draws.Bounds);
        Assert.Equal(3, after.Log.OfType<TacticalShotEvent>().Count());
        Assert.Equal((1, 4, D0), (after.Slots[0].Moves, after.Slots[0].Shots, after.Slots[0].Target));
        Assert.Equal(new TacticalTargetSetEvent(0, D0), after.Log[^1]);
    }

    [Fact]
    public void Computer_archers_with_shots_adjacent_keep_shooting_and_never_take_a_melee_target()
    {
        // Archers (range 2) adjacent with 4 moves: the adjacency branch needs no shots for archers, so the
        // in-range branch fires four times, to 0 moves, and the walk ends with no target.
        var state = Arena(
            new[] { (0, Unit(AR, 5, 5, 1000, quality: 5, morale: 70)), (D0, Unit(HI, 5, 6, 6000, morale: 99)) },
            attackerComputer: true);

        var after = TacticalMovement.MoveUnit(state, 0, 5, 6, Context, new ScriptedDraws(Enumerable.Repeat(0, 8).ToArray()));

        Assert.Equal(4, after.Log.OfType<TacticalShotEvent>().Count());
        Assert.Equal((0, 21, TacticalSlot.NoTarget), (after.Slots[0].Moves, after.Slots[0].Shots, after.Slots[0].Target));
    }

    [Fact]
    public void A_computer_unit_out_of_range_walks_and_the_checks_run_before_each_step()
    {
        // LI (range 1) at (5,2) toward the enemy at (5,6): it walks (5,3), (5,4), (5,5); there, adjacent
        // with 1 move left, it takes the target before the next step.
        var state = Arena(
            new[] { (0, Unit(LI, 5, 2, 3000, quality: 5, morale: 70)), (D0, Unit(HI, 5, 6, 6000, morale: 99)) },
            attackerComputer: true);

        var after = Walk(state, 0, 5, 6);

        Assert.Equal(new[] { (5, 3), (5, 4), (5, 5) }, Path(after, 0));
        Assert.Equal((1, D0), (after.Slots[0].Moves, after.Slots[0].Target));
    }
}
