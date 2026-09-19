using System.Linq;
using IC2.Engine.Calendar;
using IC2.Engine.Model;
using IC2.Engine.Tests.Calendar;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// <c>docs/task-catalogue.md</c> T17, Done-when 8: elimination-aware seat rotation
/// (<see cref="SeatRotationSystem"/>, T06 follow-up
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/43">#43</see>). Lives here rather
/// than under <c>tests/IC2.Engine.Tests/Calendar/**</c> because that directory is not in this task's
/// Owns list; <see cref="SeatRotationSystem"/> itself is, and its own public entry point (through
/// <see cref="CalendarTestbed"/>, T06's public test helper, used read-only here exactly as
/// <c>Battle</c>'s tests use <c>Strength</c>'s public API) is fully testable from here.
/// </summary>
public sealed class SeatRotationEliminationTests
{
    private static GameState ThreeSeatState(bool bEliminated)
    {
        var initial = CalendarTestbed.InitialState();
        var a = initial.NationById("north")! with { Id = "a", Name = "a", Eliminated = false };
        var b = initial.NationById("south")! with { Id = "b", Name = "b", Control = SeatControl.Ai, Eliminated = bEliminated };
        var c = initial.NationById("south")! with { Id = "c", Name = "c", Control = SeatControl.Ai, Eliminated = false };

        return initial with
        {
            Nations = ValueList.From(new[] { a, b, c }),
            TurnOrder = ValueList.From(new[] { "a", "b", "c" }),
            ActiveSeatIndex = 0,
        };
    }

    /// <summary>
    /// Baseline: with nobody eliminated, rotation still visits every seat once, in order — proving this
    /// task's change is a no-op for the ordinary case (backward compatible with T06's own
    /// <c>SeatRotationTests</c>, which this fixture deliberately mirrors at three seats instead of two).
    /// </summary>
    [Fact]
    public void NoEliminatedSeats_RotatesThroughEveryOneInOrder()
    {
        var coordinator = CalendarTestbed.CoordinatorFor("t17-seat-rotation-elimination");
        var state = ThreeSeatState(bEliminated: false);

        var toB = coordinator.RunTurn(state);
        Assert.Equal("b", toB.State.ActiveNationId);
        Assert.False(toB.RoundTickRan);

        var toC = coordinator.RunTurn(toB.State);
        Assert.Equal("c", toC.State.ActiveNationId);
        Assert.False(toC.RoundTickRan);

        var toA = coordinator.RunTurn(toC.State);
        Assert.Equal("a", toA.State.ActiveNationId);
        Assert.True(toA.RoundTickRan);
    }

    /// <summary>
    /// The core DoD 8 behaviour: seat "b" is eliminated, so ending "a"'s turn skips straight to "c" —
    /// "b" gets no <see cref="SeatHandoffRequested"/> and never becomes the active seat.
    /// </summary>
    [Fact]
    public void EliminatedSeat_IsSkipped_NoHandoffAndNeverBecomesActive()
    {
        var coordinator = CalendarTestbed.CoordinatorFor("t17-seat-rotation-elimination");
        var state = ThreeSeatState(bEliminated: true);

        var afterA = coordinator.RunTurn(state);

        Assert.Equal("c", afterA.State.ActiveNationId);
        Assert.False(afterA.RoundTickRan); // Only one seat (a) has gone so far in this round.
        Assert.DoesNotContain(afterA.Events, e => e is SeatHandoffRequested handoff && handoff.NationId == "b");
    }

    /// <summary>
    /// "Including one eliminated earlier in the same round": once "b" is skipped and "c" finishes its
    /// turn, rotation wraps back to "a" and the round completes — "b" is skipped again on the way, not
    /// just the first time.
    /// </summary>
    [Fact]
    public void EliminatedSeat_IsSkippedOnEveryLap_AndTheRoundStillCompletesOnWrap()
    {
        var coordinator = CalendarTestbed.CoordinatorFor("t17-seat-rotation-elimination");
        var state = ThreeSeatState(bEliminated: true);

        var afterA = coordinator.RunTurn(state);
        Assert.Equal("c", afterA.State.ActiveNationId);

        var afterC = coordinator.RunTurn(afterA.State);
        Assert.Equal("a", afterC.State.ActiveNationId);
        Assert.True(afterC.RoundTickRan);
        Assert.DoesNotContain(afterC.Events, e => e is SeatHandoffRequested handoff && handoff.NationId == "b");
    }

    /// <summary>
    /// N3: the <c>wrapped</c> refactor's own genuinely new behaviour, isolated -- the wrap can be detected
    /// while landing on the very seat that then turns out to be eliminated and gets skipped, not only
    /// while landing on a seat that is kept. Seat "a" (index 0, the wrap-around target) is eliminated, and
    /// the active seat starts at "c" (index 2, the last one). Ending "c"'s turn must still both skip "a"
    /// (a wraps and is immediately eliminated) and report the round complete, handing off to "b".
    /// </summary>
    [Fact]
    public void EliminatedSeatAtTheWrapAroundIndex_StillCompletesTheRound()
    {
        var coordinator = CalendarTestbed.CoordinatorFor("t17-seat-rotation-elimination");
        var initial = CalendarTestbed.InitialState();
        var a = initial.NationById("north")! with { Id = "a", Name = "a", Eliminated = true };
        var b = initial.NationById("south")! with { Id = "b", Name = "b", Control = SeatControl.Human, Eliminated = false };
        var c = initial.NationById("south")! with { Id = "c", Name = "c", Control = SeatControl.Ai, Eliminated = false };
        var state = initial with
        {
            Nations = ValueList.From(new[] { a, b, c }),
            TurnOrder = ValueList.From(new[] { "a", "b", "c" }),
            ActiveSeatIndex = 2, // "c" is active.
        };

        var afterC = coordinator.RunTurn(state);

        Assert.Equal("b", afterC.State.ActiveNationId);
        Assert.True(afterC.RoundTickRan); // The wrap through index 0 happened, even though "a" itself was skipped.
        Assert.DoesNotContain(afterC.Events, e => e is SeatHandoffRequested handoff && handoff.NationId == "a");
        Assert.Contains(afterC.Events, e => e is SeatHandoffRequested handoff && handoff.NationId == "b");
    }

    /// <summary>
    /// A turn-order entry that names no nation at all (as opposed to one that resolves but is eliminated)
    /// fails with a typed <see cref="SeatResolutionException"/> rather than being silently skipped.
    /// </summary>
    [Fact]
    public void ActiveSeatWithNoResolvingNation_ThrowsTypedInvariantError()
    {
        var coordinator = CalendarTestbed.CoordinatorFor("t17-seat-rotation-elimination");
        var initial = CalendarTestbed.InitialState();
        var a = initial.NationById("north")! with { Id = "a", Eliminated = false };
        var state = initial with
        {
            Nations = ValueList.From(new[] { a }),
            TurnOrder = ValueList.From(new[] { "a", "ghost-nation" }),
            ActiveSeatIndex = 0,
        };

        Assert.Throws<SeatResolutionException>(() => coordinator.RunTurn(state));
    }

    /// <summary>Every seat eliminated: no next active seat exists, and this is reported, not looped forever.</summary>
    [Fact]
    public void EverySeatEliminated_ThrowsTypedInvariantError()
    {
        var coordinator = CalendarTestbed.CoordinatorFor("t17-seat-rotation-elimination");
        var initial = CalendarTestbed.InitialState();
        var a = initial.NationById("north")! with { Id = "a", Eliminated = true };
        var b = initial.NationById("south")! with { Id = "b", Eliminated = true };
        var state = initial with
        {
            Nations = ValueList.From(new[] { a, b }),
            TurnOrder = ValueList.From(new[] { "a", "b" }),
            ActiveSeatIndex = 0,
        };

        Assert.Throws<SeatResolutionException>(() => coordinator.RunTurn(state));
    }
}
