using IC2.Engine.Battle.Tactical;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical;

/// <summary>
/// A general that records which half-rounds it was asked to play, and can be told to do something.
/// </summary>
internal sealed class RecordingGeneral : ITacticalGeneral
{
    private readonly Func<TacticalBattleState, IBattleDraws, TacticalBattleState>? _onMove;

    public RecordingGeneral(Func<TacticalBattleState, IBattleDraws, TacticalBattleState>? onMove = null) => _onMove = onMove;

    public List<(string Kind, int Side, int Counter)> Calls { get; } = new();

    public TacticalBattleState Place(TacticalBattleState state, TacticalContext context, IBattleDraws draws)
    {
        Calls.Add(("place", state.SideToMove, state.Counter));
        return state;
    }

    public TacticalBattleState Move(TacticalBattleState state, TacticalContext context, IBattleDraws draws)
    {
        Calls.Add(("move", state.SideToMove, state.Counter));
        return _onMove is null ? state : _onMove(state, draws);
    }
}

/// <summary>
/// Who plays a half-round, <c>FUN_00439C84</c>: report <c>2026-10-04-decompiled-tactical-battle-rules.md</c>
/// §3 ("Who drives a half-round"); <c>docs/game-design.md</c>, "The half-round", "Who plays it".
/// </summary>
public class DriverTests
{
    [Fact]
    public void A_computer_defender_places_first_then_the_driver_waits_for_the_human_attacker()
    {
        var state = Arena(
            new[] { (0, Unit(LI, 0, 0, 5000)), (D0, Unit(LI, 0, 11, 5000)) },
            sideToMove: 1,
            placed: false,
            counter: 1,
            defenderComputer: true);
        var general = new RecordingGeneral();

        var after = TacticalDriver.Run(state, Context, NoDraws.Instance, general);

        Assert.Equal(new[] { ("place", 1, 1) }, general.Calls);
        Assert.Equal((0, false, 2), (after.SideToMove, after.Placed, after.Counter));
    }

    [Fact]
    public void A_computer_attacker_places_second_and_moves_first_then_hands_to_the_human_defender()
    {
        var state = Arena(
            new[] { (0, Unit(LI, 0, 0, 5000)), (D0, Unit(LI, 0, 11, 5000)) },
            sideToMove: 0,
            placed: false,
            counter: 2,
            attackerComputer: true);
        var general = new RecordingGeneral();

        var after = TacticalDriver.Run(state, Context, NoDraws.Instance, general);

        Assert.Equal(new[] { ("place", 0, 2), ("move", 0, 3) }, general.Calls);
        Assert.Equal((1, true, 4), (after.SideToMove, after.Placed, after.Counter));
    }

    [Fact]
    public void A_human_side_waits_and_the_driver_does_nothing()
    {
        var state = Arena((0, Unit(LI, 0, 0, 5000)), (D0, Unit(LI, 0, 11, 5000)));
        var general = new RecordingGeneral();

        Assert.Equal(state, TacticalDriver.Run(state, Context, NoDraws.Instance, general));
        Assert.Empty(general.Calls);
    }

    [Fact]
    public void Computer_general_drives_a_human_side_and_the_loop_stops_when_the_battle_is_over()
    {
        // Both sides computer-driven (attacker by Computer general). The attacker's LI shoots the
        // defender's last unit, 100 troops of LI, below the floor 600: removed, battle over.
        var state = Arena(
            new[] { (0, Unit(LI, 5, 5, 5000)), (D0, Unit(LI, 5, 6, 100)) },
            defenderComputer: true,
            attackerGeneral: true);
        var general = new RecordingGeneral((s, d) => TacticalShooting.Shoot(s, 0, D0, Context, d));

        var after = TacticalDriver.Run(state, Context, new ScriptedDraws(1, 1), general);

        Assert.True(after.IsOver);
        Assert.Single(general.Calls);
    }
}

/// <summary>
/// A human's orders, <c>OnMapClick</c>, <c>TBattleMap_PlaceUnit</c> and the toolbar: report
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §2 ("Human placement"), §3 ("Human actions") and
/// §10 ("The toolbar"); <c>docs/game-design.md</c>, "Placement" and "A human's orders".
/// </summary>
public class OrdersTests
{
    private static TacticalBattleState Placing(int side) =>
        Arena(
            new[] { (0, Unit(LI, 0, 0, 5000)), (1, Unit(HI, 1, 0, 5000)), (D0, Unit(LI, 0, 11, 5000)) },
            sideToMove: side,
            placed: false,
            counter: side == 1 ? 1 : 2);

    [Fact]
    public void The_attacker_places_in_rows_y_below_3_on_empty_cells_as_often_as_it_likes()
    {
        var first = TacticalOrders.Place(Placing(0), 0, 5, 2, Context);
        Assert.True(first.Accepted);
        Assert.Equal((5, 2), (first.State.Slots[0].X, first.State.Slots[0].Y));
        Assert.Equal(TacticalIcon.Empty, GridAt(first.State, 0, 0));
        AssertGridMatchesSlots(first.State);

        var again = TacticalOrders.Place(first.State, 0, 6, 1, Context);
        Assert.True(again.Accepted);

        Assert.False(TacticalOrders.Place(Placing(0), 0, 5, 3, Context).Accepted);
        Assert.False(TacticalOrders.Place(Placing(0), 0, 1, 0, Context).Accepted); // occupied
        Assert.False(TacticalOrders.Place(Placing(0), D0, 5, 2, Context).Accepted); // not its unit
        Assert.False(TacticalOrders.Place(Placing(0), 0, -1, 2, Context).Accepted); // off the board
    }

    [Fact]
    public void The_defender_places_in_rows_y_above_8()
    {
        Assert.True(TacticalOrders.Place(Placing(1), D0, 5, 9, Context).Accepted);
        Assert.False(TacticalOrders.Place(Placing(1), D0, 5, 8, Context).Accepted);
    }

    [Fact]
    public void Placement_orders_are_refused_after_placement_and_movement_orders_during_it()
    {
        var placed = Arena((0, Unit(LI, 0, 0, 5000)), (D0, Unit(LI, 0, 1, 5000)));
        Assert.False(TacticalOrders.Place(placed, 0, 5, 2, Context).Accepted);
        Assert.False(TacticalOrders.Move(Placing(0), 0, 5, 2, Context, NoDraws.Instance).Accepted);
        Assert.False(TacticalOrders.SetTarget(Placing(0), 0, D0, Context).Accepted);
    }

    [Fact]
    public void A_target_is_set_on_an_adjacent_live_enemy_by_a_unit_with_a_move_at_no_cost()
    {
        var state = Arena((0, Unit(HI, 3, 3, 5000)), (D0, Unit(LI, 4, 4, 5000)), (D0 + 1, Unit(LI, 3, 5, 5000)));

        var set = TacticalOrders.SetTarget(state, 0, D0, Context);
        Assert.True(set.Accepted);
        Assert.Equal((D0, 2), (set.State.Slots[0].Target, set.State.Slots[0].Moves));

        Assert.False(TacticalOrders.SetTarget(state, 0, D0 + 1, Context).Accepted); // distance 2
        var noMoves = state with { Slots = ValueListWith(state, 0, state.Slots[0] with { Moves = 0 }) };
        Assert.False(TacticalOrders.SetTarget(noMoves, 0, D0, Context).Accepted);
    }

    [Fact]
    public void A_shot_needs_shots_moves_and_the_enemy_within_range()
    {
        var state = Arena(
            (0, Unit(AR, 3, 3, 3000)),
            (1, Unit(HI, 6, 3, 5000)),
            (D0, Unit(LI, 3, 5, 5000, morale: 99)),
            (D0 + 1, Unit(LI, 3, 6, 5000, morale: 99)),
            (D0 + 2, Unit(LI, 6, 4, 5000, morale: 99)));

        Assert.True(TacticalOrders.Shoot(state, 0, D0, Context, new ScriptedDraws(0, 0)).Accepted); // distance 2
        Assert.False(TacticalOrders.Shoot(state, 0, D0 + 1, Context, NoDraws.Instance).Accepted); // distance 3
        Assert.False(TacticalOrders.Shoot(state, 1, D0 + 2, Context, NoDraws.Instance).Accepted); // HI has no shots

        var noShots = state with { Slots = ValueListWith(state, 0, state.Slots[0] with { Shots = 0 }) };
        Assert.False(TacticalOrders.Shoot(noShots, 0, D0, Context, NoDraws.Instance).Accepted);
        var noMoves = state with { Slots = ValueListWith(state, 0, state.Slots[0] with { Moves = 0 }) };
        Assert.False(TacticalOrders.Shoot(noMoves, 0, D0, Context, NoDraws.Instance).Accepted);
    }

    [Fact]
    public void A_right_click_on_an_own_unit_clears_its_target_and_a_move_clears_it_too()
    {
        var state = Arena((0, Unit(LC, 3, 3, 5000, target: D0)), (D0, Unit(LI, 4, 4, 5000)));

        Assert.Equal(TacticalSlot.NoTarget, TacticalOrders.ClearTarget(state, 0, Context).State.Slots[0].Target);

        var moved = TacticalOrders.Move(state, 0, 3, 1, Context, NoDraws.Instance);
        Assert.True(moved.Accepted);
        Assert.Equal((3, 1, TacticalSlot.NoTarget), (moved.State.Slots[0].X, moved.State.Slots[0].Y, moved.State.Slots[0].Target));
        Assert.False(TacticalOrders.Move(state, 0, 4, 4, Context, NoDraws.Instance).Accepted); // occupied
    }

    [Fact]
    public void Orders_are_refused_while_the_computer_plays_and_once_the_battle_is_over()
    {
        var computer = Arena(new[] { (0, Unit(LC, 3, 3, 5000)), (D0, Unit(LI, 4, 4, 5000)) }, attackerGeneral: true);
        Assert.False(TacticalOrders.Move(computer, 0, 3, 1, Context, NoDraws.Instance).Accepted);
        Assert.False(TacticalOrders.Select(computer, 0, Context).Accepted);

        var over = Arena((0, Unit(LC, 3, 3, 5000)), (D0, Unit(LI, 4, 4, 5000))) with { IsOver = true };
        Assert.False(TacticalOrders.Move(over, 0, 3, 1, Context, NoDraws.Instance).Accepted);
        Assert.False(TacticalOrders.Surrender(over, Context, NoDraws.Instance).Accepted);
    }

    [Fact]
    public void Select_records_the_selected_unit()
    {
        var state = Arena((0, Unit(LC, 3, 3, 5000)), (D0, Unit(LI, 4, 4, 5000)));
        Assert.Equal(D0, TacticalOrders.Select(state, D0, Context).State.Selected);
        Assert.False(TacticalOrders.Select(state, 5, Context).Accepted);
    }

    [Fact]
    public void End_turn_ends_the_half_round_and_hands_to_the_computer_until_the_human_is_back()
    {
        var state = Arena(new[] { (0, Unit(LI, 0, 0, 5000)), (D0, Unit(LI, 13, 11, 5000)) }, defenderComputer: true);
        var general = new RecordingGeneral();

        var result = TacticalOrders.EndHalfRound(state, Context, NoDraws.Instance, general);

        Assert.Equal(new[] { ("move", 1, 4) }, general.Calls);
        Assert.Equal((0, 5), (result.State.SideToMove, result.State.Counter));
    }

    [Fact]
    public void Computer_general_on_plays_the_side_to_move_and_off_hands_it_back()
    {
        var state = Arena((0, Unit(LI, 0, 0, 5000)), (D0, Unit(LI, 13, 11, 5000)));
        var general = new RecordingGeneral();

        var on = TacticalOrders.ComputerGeneralOn(state, Context, NoDraws.Instance, general);

        Assert.True(on.State.ComputerGeneral[0]);
        Assert.Equal(new[] { ("move", 0, 3) }, general.Calls);
        Assert.Equal(1, on.State.SideToMove);

        var off = TacticalOrders.ComputerGeneralOff(on.State, 0, Context);
        Assert.False(off.State.ComputerGeneral[0]);
    }

    [Fact]
    public void Surrender_is_an_order_of_the_side_to_move()
    {
        var state = Arena((0, Unit(LI, 0, 0, 5000)), (D0, Unit(LI, 13, 11, 5000)));
        var result = TacticalOrders.Surrender(state, Context, NoDraws.Instance);

        Assert.True(result.State.IsOver);
        Assert.Equal(TacticalBattleState.DefenderSide, TacticalSurrender.Winner(result.State));
    }

    private static IC2.Engine.Model.ValueList<TacticalSlot> ValueListWith(TacticalBattleState state, int slot, TacticalSlot unit)
    {
        var slots = state.Slots.ToArray();
        slots[slot] = unit;
        return IC2.Engine.Model.ValueList.From(slots);
    }
}
