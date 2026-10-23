using System.Reflection;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Battle.Tactical.General;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical.General;

/// <summary>Original FUN_0043A544/FUN_0043ABB4 visit live slots, unlike pass one's type order.</summary>
public sealed class GeneralIterationOrderTests
{
    private const BindingFlags Internal = BindingFlags.Static | BindingFlags.Instance
        | BindingFlags.Public | BindingFlags.NonPublic;

    private static object Board(TacticalBattleState state) => typeof(ComputerGeneral).Assembly
        .GetType("IC2.Engine.Battle.Tactical.TacticalBoard", throwOnError: true)!
        .GetMethod("Thaw", Internal)!
        .Invoke(null, [state, Context, new DelphiBattleDraws(7)])!;

    private static IEnumerable<int> Order(object board, string method) =>
        (IEnumerable<int>)typeof(ComputerGeneral).Assembly
            .GetType("IC2.Engine.Battle.Tactical.General.GeneralOrders", throwOnError: true)!
            .GetMethod(method, Internal)!.Invoke(null, [board])!;

    [Fact]
    public void First_pass_keeps_type_then_slot_order()
    {
        var state = Arena(Enumerable.Range(0, 5)
            .Select(i => (i, Unit(new[] { LI, HI, AR, HC, LC }[i], i + 1, 1, 1_000))));

        Assert.Equal(new[] { 1, 3, 4, 0, 2 }, Order(Board(state), "InOrder"));
    }

    [Fact]
    public void Later_passes_stay_within_defender_slots_and_recheck_live_state()
    {
        var defender = Arena(Enumerable.Range(0, 5)
            .Select(i => (D0 + i, Unit(new[] { LI, HI, AR, HC, LC }[i], i + 1, 9, 1_000))),
            sideToMove: TacticalBattleState.DefenderSide);
        var board = Board(defender);
        using var order = Order(board, "InSlotOrder").GetEnumerator();

        Assert.True(order.MoveNext());
        Assert.Equal(D0, order.Current);
        var slots = (TacticalSlot[])board.GetType().GetProperty("Slots", Internal)!.GetValue(board)!;
        slots[D0 + 1] = TacticalSlot.Empty;
        Assert.True(order.MoveNext());
        Assert.Equal(D0 + 2, order.Current);
        board.GetType().GetProperty("IsOver", Internal)!.SetValue(board, true);
        Assert.False(order.MoveNext());
    }

    [Fact]
    public void Threat_pass_visits_light_infantry_slot_zero_before_heavy_infantry_slot_one()
    {
        var state = Arena(new[]
        {
            (0, Unit(LI, 3, 1, 15_000, moves: 1, shots: 7)),
            (1, Unit(HI, 7, 1, 6_000, moves: 2, shots: 0)),
            (D0, Unit(LI, 5, 10, 15_000, moves: 1, shots: 7)),
        }, attackerGeneral: true);
        var journal = new List<GeneralDecision>();

        GeneralPassThree.ThreatMove(state, Context, new DelphiBattleDraws(7), journal);

        Assert.Equal(new[] { 0, 1 }, journal.Select(decision => decision.Slot).Distinct());
    }

    [Fact]
    public void Second_pass_fires_archer_slot_zero_before_light_infantry_slot_one()
    {
        var state = Arena(new[]
        {
            (0, Unit(AR, 4, 5, 3_500, moves: 1, shots: 25)),
            (1, Unit(LI, 5, 4, 15_000, moves: 1, shots: 7)),
            (D0, Unit(HI, 5, 5, 6_000, moves: 2, shots: 0)),
        }, attackerGeneral: true);

        var after = GeneralPassTwo.Run(state, Context, new DelphiBattleDraws(7));

        Assert.Equal(new[] { 0, 1 }, after.Log.OfType<TacticalShotEvent>().Select(shot => shot.Shooter).Take(2));
    }
}
