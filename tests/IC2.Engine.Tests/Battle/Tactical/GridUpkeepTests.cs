using IC2.Engine.Battle.Tactical;
using IC2.Engine.Battle.Tactical.General;
using IC2.Engine.Model;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical;

/// <summary>
/// The stored grid's upkeep on stale and doubled cells, #1106 and #1324:
/// <c>2026-10-21-crafted-battle-grid-movement-and-line.md</c> (research <c>1c203b7</c>), "Operation matrix".
/// A move carries the code stored in its old cell, slot lookups that test the grid first ignore a cell the
/// grid shows empty, and a cell and itself is no clear line.
/// </summary>
public class GridUpkeepTests
{
    /// <summary>A stale code, never produced by the icon rule for any slot of these tests.</summary>
    private const int Stale = 7;

    private static TacticalBattleState WithCell(TacticalBattleState state, int x, int y, int code)
    {
        var grid = state.Grid.ToArray();
        grid[Cell(x, y)] = code;
        return state with { Grid = ValueList.From(grid) };
    }

    private static TacticalBattleState Walk(TacticalBattleState state, int slot, int x, int y) =>
        TacticalMovement.MoveUnit(state, slot, x, y, Context, NoDraws.Instance);

    [Fact]
    public void A_move_carries_a_stale_code_with_the_unit_and_leaves_the_old_cell_empty()
    {
        var state = WithCell(Arena((0, Unit(LC, 0, 0, 5000)), (D0, Unit(LI, 13, 11, 5000))), 0, 0, Stale);
        Assert.NotEqual(Stale, IconOf(0, state.Slots[0]));

        var after = Walk(state, 0, 2, 0);

        Assert.Equal((2, 0), (after.Slots[0].X, after.Slots[0].Y));
        Assert.Equal(Stale, GridAt(after, 2, 0));
        Assert.Equal(TacticalIcon.Empty, GridAt(after, 0, 0));
        Assert.Equal(TacticalIcon.Empty, GridAt(after, 1, 0));
    }

    [Fact]
    public void Moving_one_of_two_stacked_units_leaves_the_cell_empty_under_the_other()
    {
        // Slots 0 and 1 share (0,0); the grid holds slot 1's code (painted last).
        var state = Arena((0, Unit(LC, 0, 0, 5000)), (1, Unit(LC, 0, 0, 5000)), (D0, Unit(LI, 13, 11, 5000)));
        var stacked = GridAt(state, 0, 0);

        var after = Walk(state, 0, 1, 0);

        Assert.Equal(stacked, GridAt(after, 1, 0));
        Assert.Equal(TacticalIcon.Empty, GridAt(after, 0, 0));
        Assert.True(after.Slots[1].IsLive);
        Assert.Equal((0, 0), (after.Slots[1].X, after.Slots[1].Y));
    }

    [Fact]
    public void The_last_move_danger_test_skips_a_live_enemy_whose_cell_the_grid_shows_empty()
    {
        // As MovementTests' forfeit case: an equal HI beside the next cell makes a computer unit forfeit its
        // last move. With that enemy's stored cell reading empty, FUN_004389AC never looks it up.
        var units = new[]
        {
            (0, Unit(HI, 5, 5, 1000, quality: 5, morale: 70, moves: 1)),
            (D0, Unit(HI, 6, 7, 1000, quality: 5, morale: 70)),
        };
        var seen = Arena(units, attackerComputer: true);
        Assert.Empty(Walk(seen, 0, 5, 8).Log.OfType<TacticalMovedEvent>());

        var hidden = WithCell(Arena(units, attackerComputer: true), 6, 7, TacticalIcon.Empty);
        var after = Walk(hidden, 0, 5, 8);
        Assert.Equal(new[] { (5, 6) }, after.Log.OfType<TacticalMovedEvent>().Select(m => (m.ToX, m.ToY)));
        Assert.DoesNotContain(new TacticalMoveForfeitedEvent(0), after.Log);
    }

    private sealed class ZeroDraws : IBattleDraws
    {
        public int Random(int n) => 0;
    }

    [Fact]
    public void A_melee_repaints_its_live_defender_after_the_routs_so_a_cascade_on_its_cell_does_not_erase_it()
    {
        // Crafted: attacker friend 1 shares the defender's cell (4,2); the grid shows the defender (painted last).
        // The attacker (morale 5) routs at once; the cascade's -6 takes friend 1 below 30 and removes it, which
        // writes empty into (4,2). FUN_004393EC repaints the defender only after both routs, so its code is back.
        var state = Arena(
            (0, Unit(HI, 3, 2, 3000, morale: 5, target: D0)),
            (1, Unit(HI, 4, 2, 3000, morale: 30)),
            (D0, Unit(HI, 4, 2, 3000, morale: 99)));

        var after = TacticalMelee.Resolve(state, TacticalBattleState.AttackerSide, Context, new ZeroDraws());

        Assert.False(after.Slots[0].IsLive);
        Assert.False(after.Slots[1].IsLive);
        Assert.True(after.Slots[D0].IsLive);
        Assert.Equal(IconOf(D0, after.Slots[D0]), GridAt(after, 4, 2));
        Assert.Equal(TacticalIcon.Empty, GridAt(after, 3, 2));
    }

    [Fact]
    public void A_cell_and_itself_is_not_a_clear_line_but_an_adjacent_cell_is()
    {
        var state = Arena((0, Unit(HI, 3, 1, 3000)), (D0, Unit(HI, 6, 2, 4000)));

        Assert.False(GeneralLine.IsClear(state, 3, 1, 3, 1, Context));
        Assert.False(GeneralLine.IsClear(state, 0, 0, 0, 0, Context));
        Assert.True(GeneralLine.IsClear(state, 3, 1, 4, 1, Context));
    }

    [Fact]
    public void A_normal_board_still_matches_its_slots_after_moves()
    {
        var state = Arena((0, Unit(LC, 0, 0, 5000)), (1, Unit(LI, 2, 3, 3000)), (D0, Unit(LI, 13, 11, 5000)));

        var after = Walk(Walk(state, 0, 4, 2), 1, 3, 5);

        AssertGridMatchesSlots(after);
    }
}
