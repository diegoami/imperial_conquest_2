using IC2.Engine.Battle.Tactical;
using IC2.Engine.Model;
using Xunit;
using static IC2.Engine.Tests.Battle.Tactical.TacticalTestbed;

namespace IC2.Engine.Tests.Battle.Tactical;

// Source: research500e521, 2026-10-16-computer-general-predicate-and-reload.md.
// FUN_00438378 (OR), 00439968 (setup), 00438d24/00438a6c (Walk/step).
public class GeneralControlTests
{
    private static TacticalBattleState Controls(TacticalBattleState state, int side, bool nation, bool general,
        bool otherGeneral = false) => state with
    {
        SideToMove = side,
        ComputerControlled = ValueList.Of(side == 0 && nation, side == 1 && nation),
        ComputerGeneral = ValueList.Of(side == 0 ? general : otherGeneral, side == 1 ? general : otherGeneral),
    };

    public static IEnumerable<object[]> SetupCases()
    {
        foreach (var side in new[] { 0, 1 })
        foreach (var placed in new[] { false, true })
        foreach (var nation in new[] { false, true })
        foreach (var general in new[] { false, true })
        foreach (var distance in new[] { 2, 3 })
        foreach (var counter in new[] { 9, 10 })
            yield return new object[] { side, placed, nation, general, distance, counter };
    }

    [Theory]
    [MemberData(nameof(SetupCases))]
    public void Setup_uses_only_the_active_sides_OR_at_every_slow_advance_boundary(
        int side, bool placed, bool nation, bool general, int distance, int counter)
    {
        var own = side * D0;
        var enemy = (1 - side) * D0;
        var state = Controls(Arena(new[]
        {
            (own, Unit(LI, 0, 0, 3000, moves: 0, target: enemy)),
            (own + 1, Unit(HI, 1, 0, 3000, moves: 0)),
            (own + 2, Unit(AR, 2, 0, 3000, moves: 0)),
            (enemy, Unit(HI, 0, distance, 3000, moves: 0, target: own)),
        }, placed: placed, counter: counter), side, nation, general, otherGeneral: true);
        var set = TacticalHalfRound.Setup(state, Context);
        var slow = (nation || general) && distance > 2 && counter < 10;
        Assert.Equal(slow ? 1 : 4, set.Slots[own].Moves);
        Assert.Equal(slow ? 1 : 4, set.Slots[own + 2].Moves);
        Assert.Equal(2, set.Slots[own + 1].Moves);
        Assert.Equal(TacticalSlot.NoTarget, set.Slots[own].Target);
        Assert.Equal(state.Slots[enemy], set.Slots[enemy]);
        Assert.Equal(counter + 1, set.Counter);
        Assert.Equal(placed, set.Placed);
        Assert.Empty(set.Log.OfType<TacticalDrawEvent>());

        // The improved switch remains authoritative for both kinds of computer-driven side.
        var improved = TacticalContext.From(ToyRuleset with
        {
            Combat = ToyRuleset.Combat with { Tactical = Rules with { SlowAdvance = false } },
        });
        var full = TacticalHalfRound.Setup(state, improved);
        Assert.Equal(4, full.Slots[own].Moves);
        Assert.Equal(4, full.Slots[own + 2].Moves);
    }

    public static IEnumerable<object[]> ControlCases()
    {
        foreach (var side in new[] { 0, 1 })
        foreach (var nation in new[] { false, true })
        foreach (var general in new[] { false, true })
            yield return new object[] { side, nation, general };
    }

    [Theory]
    [MemberData(nameof(ControlCases))]
    public void Walk_existing_target_and_last_move_danger_follow_the_same_OR(int side, bool nation, bool general)
    {
        var own = side * D0;
        var enemy = (1 - side) * D0;
        var driven = nation || general;
        var targeted = Controls(Arena((own, Unit(LC, 0, 0, 5000, target: enemy)),
            (enemy, Unit(HI, 13, 11, 5000))), side, nation, general, otherGeneral: true);
        var stop = TacticalMovement.MoveUnit(targeted, own, 3, 0, Context, NoDraws.Instance);
        Assert.Equal(driven ? 0 : 3, stop.Slots[own].X);
        Assert.Equal(driven ? 6 : 3, stop.Slots[own].Moves);
        Assert.Equal(enemy, stop.Slots[own].Target);

        // Same threat-strength control as MovementTests: strong LI beats this weak HI.
        var danger = Controls(Arena((own, Unit(HI, 5, 5, 1000, moves: 1)),
            (enemy, Unit(LI, 6, 7, 15000, quality: 9, morale: 99))), side, nation, general, otherGeneral: true);
        var last = TacticalMovement.MoveUnit(danger, own, 5, 8, Context, NoDraws.Instance);
        Assert.Equal(driven ? 5 : 6, last.Slots[own].Y);
        Assert.Equal(0, last.Slots[own].Moves);
        Assert.Equal(driven, last.Log.OfType<TacticalMoveForfeitedEvent>().Any());
        AssertGridMatchesSlots(last);
    }

    [Theory]
    [MemberData(nameof(ControlCases))]
    public void Walk_adjacency_shooting_and_exhaustion_follow_the_same_OR(int side, bool nation, bool general)
    {
        var own = side * D0;
        var enemy = (1 - side) * D0;
        var driven = nation || general;
        var state = Controls(Arena((own, Unit(LI, 5, 5, 3000)),
            (enemy, Unit(HI, 5, 6, 6000, morale: 99))), side, nation, general, otherGeneral: true);
        var draws = new ScriptedDraws(0, 0, 0, 0, 0, 0);
        var shot = TacticalMovement.MoveUnit(state, own, 5, 6, Context, draws);
        Assert.Equal(driven ? new[] { 13, 13, 13, 13, 13, 13 } : Array.Empty<int>(), draws.Bounds);
        Assert.Equal(driven ? 3 : 0, shot.Log.OfType<TacticalShotEvent>().Count());
        Assert.Equal(driven ? 1 : 4, shot.Slots[own].Moves);
        Assert.Equal(driven ? 4 : 7, shot.Slots[own].Shots);
        Assert.Equal(driven ? enemy : TacticalSlot.NoTarget, shot.Slots[own].Target);
        Assert.Equal((5, 5), (shot.Slots[own].X, shot.Slots[own].Y));

        foreach (var moves in new[] { 0, 1 })
        {
            var slots = state.Slots.ToArray();
            slots[own] = slots[own] with { Moves = moves, Shots = 0 };
            var adjacent = TacticalMovement.MoveUnit(state with { Slots = ValueList.From(slots) }, own, 5, 6,
                Context, NoDraws.Instance);
            Assert.Equal(driven && moves > 0 ? enemy : TacticalSlot.NoTarget, adjacent.Slots[own].Target);
            Assert.Equal(moves, adjacent.Slots[own].Moves);
            Assert.Empty(adjacent.Log.OfType<TacticalShotEvent>());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Enabling_preserves_assigned_moves_and_the_next_setup_rechecks_control(bool placed)
    {
        var state = Arena(new[] { (0, Unit(LI, 0, 0, 3000)), (1, Unit(HI, 1, 0, 3000)),
            (D0, Unit(HI, 13, 11, 3000)) }, placed: placed, counter: placed ? 2 : 1);
        state = TacticalHalfRound.Setup(state, Context);
        var general = new BudgetGeneral();
        var on = TacticalOrders.ComputerGeneralOn(state, Context, NoDraws.Instance, general);
        Assert.True(on.Accepted);
        Assert.Equal(4, general.Seen[0].Slots[0].Moves);
        Assert.Equal(2, general.Seen[0].Slots[1].Moves);
        if (!placed)
        {
            Assert.Equal(2, general.Seen.Count);
            Assert.Equal(1, general.Seen[1].Slots[0].Moves);
            Assert.Equal(2, general.Seen[1].Slots[1].Moves);
        }

        var slow = TacticalHalfRound.Setup(state with { ComputerGeneral = ValueList.Of(true, false) }, Context);
        var off = TacticalOrders.ComputerGeneralOff(slow, 0, Context).State;
        Assert.Equal(1, off.Slots[0].Moves);
        Assert.Equal(4, TacticalHalfRound.Setup(off, Context).Slots[0].Moves);
        var enabledAgain = new BudgetGeneral();
        TacticalOrders.ComputerGeneralOn(off, Context, NoDraws.Instance, enabledAgain);
        Assert.Equal(1, enabledAgain.Seen[0].Slots[0].Moves);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Resume_setup_consumes_explicit_process_history_before_general_flags_are_cleared(bool priorGeneral)
    {
        // This exercises the setup primitive and documented ordering, not an entire original loader.
        // Original saves do not store either general flag; the input is explicitly supplied here.
        var saved = Arena((0, Unit(LI, 0, 0, 3000)), (1, Unit(HI, 1, 0, 3000)), (D0, Unit(HI, 13, 11, 3000)))
            with { ComputerGeneral = ValueList.Of(priorGeneral, true), Counter = 2 };
        var setup = TacticalHalfRound.Resume(saved, Context);
        Assert.Equal(priorGeneral ? 1 : 4, setup.Slots[0].Moves);
        Assert.Equal(2, setup.Slots[1].Moves);
        Assert.Equal(3, setup.Counter);
        var cleared = TacticalOrders.ComputerGeneralOff(setup, 0, Context).State;
        cleared = TacticalOrders.ComputerGeneralOff(cleared, 1, Context).State;
        Assert.All(cleared.ComputerGeneral, g => Assert.False(g));
        Assert.Equal(setup.Slots, cleared.Slots);
        var next = TacticalHalfRound.Setup(cleared, Context);
        Assert.Equal(4, next.Slots[0].Moves);
        Assert.Equal(4, next.Counter);
    }

    [Theory]
    [InlineData(1, false, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    [InlineData(1, true, true)]
    [InlineData(2, false, false)]
    [InlineData(2, false, true)]
    [InlineData(2, true, false)]
    [InlineData(2, true, true)]
    public void Shooting_the_last_shot_rechecks_moves_before_any_step(int moves, bool removesTarget, bool nation)
    {
        // Quality-zero target isolates exhaustion from the separate last-move danger forfeiture.
        var state = Controls(Arena((0, Unit(AR, 0, 0, 1500, moves: moves, shots: 1)),
            (D0, Unit(LI, 2, 0, removesTarget ? 100 : 5000, quality: 0)),
            (D0 + 1, Unit(HI, 13, 11, 3000))), 0, nation, !nation);
        var draws = new ScriptedDraws(0, 0);
        var after = TacticalMovement.MoveUnit(state, 0, 2, 0, Context, draws);
        Assert.Equal(moves - 1, after.Slots[0].X);
        Assert.Equal(0, after.Slots[0].Y);
        Assert.Equal(0, after.Slots[0].Moves);
        Assert.Equal(0, after.Slots[0].Shots);
        Assert.Equal(TacticalSlot.NoTarget, after.Slots[0].Target);
        Assert.Equal(2, draws.Calls.Count);
        Assert.Single(after.Log.OfType<TacticalShotEvent>());
        Assert.Equal(!removesTarget, after.Slots[D0].IsLive);
        Assert.False(after.IsOver);
        AssertGridMatchesSlots(after);
    }

    private sealed class BudgetGeneral : ITacticalGeneral
    {
        public List<TacticalBattleState> Seen { get; } = new();
        public TacticalBattleState Place(TacticalBattleState s, TacticalContext c, IBattleDraws d)
        { Seen.Add(s); return s; }
        public TacticalBattleState Move(TacticalBattleState s, TacticalContext c, IBattleDraws d)
        { Seen.Add(s); return s; }
    }
}
