using IC2.Engine.Battle.Tactical;
using IC2.Engine.Battle.Tactical.General;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.General.GeneralTestbed;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical.General;

/// <summary>
/// T124 Done-when 3, engage: <c>FUN_0043A160</c> (<c>2026-10-04-decompiled-tactical-battle-rules.md</c> §7,
/// "Engage"; <c>docs/game-design.md</c>, "The computer general", "Pass 1"), and the clear-line test
/// <c>FUN_00439F00</c>.
/// </summary>
/// <remarks>
/// The side to move is a human nation with <em>Computer general</em> on, so T123's movement runs without
/// its computer-only branches (they key on the nation, T123's choice 5) and each test sees the general's
/// own decision. Shots draw through <see cref="ZeroDraws"/>: every loss is 0, so no morale moves and no
/// rout draws, and the shot count is the number of <c>Random(n)</c> pairs.
/// </remarks>
public class EngageTests
{
    private static TacticalBattleState General(params (int Slot, TacticalSlot Unit)[] units) =>
        Arena(units, attackerGeneral: true);

    private static (TacticalBattleState State, List<GeneralDecision> Journal, ZeroDraws Draws) Engage(
        TacticalBattleState state, int slot, int target)
    {
        var journal = new List<GeneralDecision>();
        var draws = new ZeroDraws();
        return (GeneralEngage.Engage(state, slot, target, Context, draws, journal), journal, draws);
    }

    [Fact]
    public void Archers_at_distance_below_3_fire_until_their_moves_run_out()
    {
        // Archers 1,500 (q 5, m 70) at distance 2 from a light infantry of 5,000: the shot bound is
        // 1500 × 5 × 70 × 18 div (1500 × 5 + 150000) = 9,450,000 div 157,500 = 60, not doubled (2 is not
        // below range 2), capped by min(500, 2500): n = 61. Four moves: four shots, eight Random(61).
        var (after, journal, draws) = Engage(General((0, Unit(AR, 0, 0, 1500)), (D0, Unit(LI, 2, 0, 5000))), 0, D0);

        Assert.Equal(Enumerable.Repeat(61, 8), draws.Bounds);
        Assert.Equal(0, after.Slots[0].Moves);
        Assert.Equal(25 - 4, after.Slots[0].Shots);
        Assert.Equal(GeneralStep.ArcherVolley, Assert.Single(journal).Step);
        Assert.Equal(TacticalSlot.NoTarget, after.Slots[0].Target);
    }

    [Fact]
    public void Archers_stop_when_their_shots_run_out()
    {
        var (after, _, draws) = Engage(General((0, Unit(AR, 0, 0, 1500, shots: 2)), (D0, Unit(LI, 1, 1, 5000))), 0, D0);

        Assert.Equal(4, draws.Bounds.Count);
        Assert.Equal(0, after.Slots[0].Shots);
        Assert.Equal(2, after.Slots[0].Moves);
    }

    [Fact]
    public void Archers_stop_when_the_target_is_removed()
    {
        // A light infantry of 100 is below its rout floor (standard 15,000 div 25 = 600): the first shot's
        // rout removes it, with no rout draw. Another enemy keeps the battle going.
        var state = General((0, Unit(AR, 0, 0, 1500)), (D0, Unit(LI, 2, 0, 100)), (D0 + 1, Unit(HI, 13, 11, 4000)));

        var (after, _, draws) = Engage(state, 0, D0);

        Assert.Equal(2, draws.Bounds.Count);
        Assert.False(after.Slots[D0].IsLive);
        Assert.Equal(3, after.Slots[0].Moves);
        Assert.False(after.IsOver);
    }

    [Fact]
    public void Archers_at_distance_3_do_not_fire_they_approach()
    {
        // Distance 3 is not below 3: the line is clear, so the archers walk toward the target and stop
        // when the path reaches its occupied cell: (1,0), (2,0).
        var (after, journal, draws) = Engage(General((0, Unit(AR, 0, 0, 1500)), (D0, Unit(LI, 3, 0, 5000))), 0, D0);

        Assert.Empty(draws.Bounds);
        Assert.Equal(new[] { (1, 0), (2, 0) }, Path(after, 0));
        Assert.Equal(new GeneralDecision(0, GeneralStep.ApproachLine, D0, 3, 0), Assert.Single(journal));
    }

    [Fact]
    public void Archers_with_no_shots_do_not_fire()
    {
        var (after, journal, draws) = Engage(General((0, Unit(AR, 0, 0, 1500, shots: 0)), (D0, Unit(LI, 2, 0, 5000))), 0, D0);

        Assert.Empty(draws.Bounds);
        Assert.Equal(GeneralStep.ApproachLine, Assert.Single(journal).Step);
        Assert.Equal(new[] { (1, 0) }, Path(after, 0));
    }

    [Fact]
    public void An_adjacent_unit_shoots_until_1_move_is_left_then_takes_the_target()
    {
        // Light infantry, 4 moves and 7 shots, next to a heavy infantry: three shots (4 → 1 move), then
        // the melee target. Bound: 3000 × 350 × 2 div 165000 = 12, + 1 = 13.
        var (after, journal, draws) = Engage(General((0, Unit(LI, 0, 0, 3000)), (D0, Unit(HI, 1, 0, 4000))), 0, D0);

        Assert.Equal(Enumerable.Repeat(13, 6), draws.Bounds);
        Assert.Equal(1, after.Slots[0].Moves);
        Assert.Equal(4, after.Slots[0].Shots);
        Assert.Equal(D0, after.Slots[0].Target);
        Assert.Equal(GeneralStep.AdjacentEngage, Assert.Single(journal).Step);
        Assert.IsType<TacticalTargetSetEvent>(after.Log[^1]);
    }

    [Fact]
    public void An_adjacent_unit_stops_shooting_when_its_shots_run_out()
    {
        // Light cavalry, 6 moves, 2 shots: two shots, 4 moves left, then the target.
        var (after, _, draws) = Engage(General((0, Unit(LC, 0, 0, 2500, shots: 2)), (D0, Unit(HI, 1, 1, 4000))), 0, D0);

        Assert.Equal(4, draws.Bounds.Count);
        Assert.Equal(4, after.Slots[0].Moves);
        Assert.Equal(D0, after.Slots[0].Target);
    }

    [Fact]
    public void An_adjacent_unit_with_no_shots_takes_the_target_at_once()
    {
        var (after, _, draws) = Engage(General((0, Unit(HI, 0, 0, 3000)), (D0, Unit(HI, 0, 1, 4000))), 0, D0);

        Assert.Empty(draws.Bounds);
        Assert.Equal(2, after.Slots[0].Moves);
        Assert.Equal(D0, after.Slots[0].Target);
    }

    [Fact]
    public void An_adjacent_unit_whose_target_is_removed_takes_no_target()
    {
        var state = General((0, Unit(LI, 0, 0, 3000)), (D0, Unit(LI, 1, 0, 100)), (D0 + 1, Unit(HI, 13, 11, 4000)));

        var (after, _, draws) = Engage(state, 0, D0);

        Assert.Equal(2, draws.Bounds.Count);
        Assert.Equal(TacticalSlot.NoTarget, after.Slots[0].Target);
        Assert.Equal(3, after.Slots[0].Moves);
    }

    [Fact]
    public void With_a_clear_line_the_unit_moves_toward_its_target()
    {
        var (after, journal, _) = Engage(General((0, Unit(HI, 0, 0, 3000)), (D0, Unit(HI, 5, 0, 4000))), 0, D0);

        Assert.Equal(new GeneralDecision(0, GeneralStep.ApproachLine, D0, 5, 0), Assert.Single(journal));
        Assert.Equal(new[] { (1, 0), (2, 0) }, Path(after, 0));
    }

    [Fact]
    public void The_box_search_is_dx_outer_dy_inner_with_a_strict_less_than()
    {
        // HI at (0,0), its target at (5,5), an own unit at (4,4) on the diagonal: the line is blocked. The
        // box (r = 1) around (5,5), dx outer and dy inner: (4,4) occupied; (4,5) on a clear line
        // ((1,1), (2,2), (2,3), (3,4)) at Chebyshev distance max(4, 5) = 5 from (0,0); (4,6) at 6;
        // (5,4) at 5, not strictly nearer, so (4,5) stays. dy outer would take (5,4) first, and a "≤"
        // would end on (5,4) as well. The walk: (1,1), (2,2) with heavy infantry's 2 moves.
        var state = General((0, Unit(HI, 0, 0, 3000)), (1, Unit(HI, 4, 4, 3000, moves: 0)), (D0, Unit(HI, 5, 5, 4000)));

        Assert.False(GeneralLine.IsClear(state, 0, 0, 5, 5, Context));
        Assert.Equal((4, 5), GeneralLine.BoxCell(state, 0, D0, Context));

        var (after, journal, _) = Engage(state, 0, D0);

        Assert.Equal(new GeneralDecision(0, GeneralStep.ApproachBox, D0, 4, 5), Assert.Single(journal));
        Assert.Equal(new[] { (1, 1), (2, 2) }, Path(after, 0));
    }

    [Fact]
    public void The_archers_box_has_radius_2()
    {
        // The same board with archers (radius 2): the 5 × 5 box around (5,5) holds (3,3), distance 3 from
        // (0,0) on the clear line (1,1), (2,2), which no radius-1 cell matches. Four moves walk to it.
        var state = General((0, Unit(AR, 0, 0, 1500)), (1, Unit(HI, 4, 4, 3000, moves: 0)), (D0, Unit(HI, 5, 5, 4000)));

        var (after, journal, draws) = Engage(state, 0, D0);

        Assert.Empty(draws.Bounds);
        Assert.Equal(new GeneralDecision(0, GeneralStep.ApproachBox, D0, 3, 3), Assert.Single(journal));
        Assert.Equal(new[] { (1, 1), (2, 2), (3, 3) }, Path(after, 0));
    }

    [Fact]
    public void With_no_usable_box_cell_the_unit_stays()
    {
        // The target in the corner (13,11), its three neighbours occupied by its own side: the line from
        // (13,0) passes (13,10), and the box has no empty cell.
        var state = General(
            (0, Unit(HI, 13, 0, 3000)),
            (D0, Unit(HI, 13, 11, 4000)), (D0 + 1, Unit(HI, 12, 11, 4000)),
            (D0 + 2, Unit(HI, 12, 10, 4000)), (D0 + 3, Unit(HI, 13, 10, 4000)));

        var (after, journal, _) = Engage(state, 0, D0);

        Assert.Empty(journal);
        Assert.Empty(Path(after, 0));
        Assert.Equal(2, after.Slots[0].Moves);
    }

    [Fact]
    public void The_clear_line_needs_every_cell_strictly_between_empty_and_adjacent_is_clear()
    {
        var state = General((0, Unit(HI, 0, 0, 3000)), (1, Unit(HI, 3, 1, 3000)), (D0, Unit(HI, 6, 2, 4000)));

        // (0,0) → (6,2), x-major: (1,0), (2,1), (3,1), (4,1), (5,2) — (3,1) is occupied.
        Assert.False(GeneralLine.IsClear(state, 0, 0, 6, 2, Context));

        // (0,0) → (6,1): err = 2 − 6 = −4; (1,0) err −2, (2,0) err 0, then a minor step: (3,1). Blocked too.
        Assert.False(GeneralLine.IsClear(state, 0, 0, 6, 1, Context));

        // (0,0) → (6,0): along the row, clear; the occupied ends do not count.
        Assert.True(GeneralLine.IsClear(state, 0, 0, 6, 0, Context));

        // Adjacent, and a cell to itself: nothing between.
        Assert.True(GeneralLine.IsClear(state, 3, 1, 4, 2, Context));
        Assert.True(GeneralLine.IsClear(state, 3, 1, 3, 1, Context));
    }
}
