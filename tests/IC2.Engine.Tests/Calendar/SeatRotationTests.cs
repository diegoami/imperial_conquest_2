using IC2.Engine.Calendar;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Calendar;

/// <summary>
/// DoD 4: seat rotation visits every seat once per calendar tick in turn-order-table order, and a
/// hotseat seat raises a handoff event while an AI seat does not.
/// </summary>
/// <remarks>
/// Uses <see cref="IC2.Engine.Core.TurnCoordinator.RunTurn"/>, one call per seat's turn -- unlike
/// <see cref="CalendarSequenceTests"/>, this test is specifically about the seat-scoped rotation, so it
/// exercises the real per-seat entry point rather than <c>RunRoundTick</c>. The toy scenario's turn
/// order is <c>["north", "south"]</c> (<c>data/worlds/toy-3city.json</c>), with <c>north</c> a human
/// seat and <c>south</c> an AI seat (<c>data/scenarios/toy-3city.json</c>) -- exactly the human/AI pair
/// this DoD line needs.
/// </remarks>
public sealed class SeatRotationTests
{
    [Fact]
    public void RotationVisitsEverySeatOnceInTurnOrderAndSignalsHandoffForHumanSeatsOnly()
    {
        var coordinator = CalendarTestbed.CoordinatorFor("calendar.seat-rotation-only");
        var state = CalendarTestbed.InitialState();
        var turnOrder = CalendarTestbed.Toy.World.TurnOrder;

        Assert.Equal(new[] { "north", "south" }, turnOrder);
        Assert.Equal("north", state.ActiveNationId);
        Assert.Equal(SeatControl.Human, state.NationById("north")!.Control);
        Assert.Equal(SeatControl.Ai, state.NationById("south")!.Control);

        // Two full calendar ticks (two rounds), each visiting north then south, in turn-order-table
        // order -- checked one seat at a time rather than only by the final state.
        for (var round = 0; round < 2; round++)
        {
            // Seat 0 (north, human) is active; its turn ends and rotation hands to seat 1 (south, AI).
            var toSouth = coordinator.RunTurn(state);
            state = toSouth.State;
            Assert.Equal("south", state.ActiveNationId);
            Assert.False(toSouth.RoundTickRan, "Rotating from the first seat to the second must not complete the round.");
            Assert.DoesNotContain(toSouth.Events, e => e is SeatHandoffRequested);

            // Seat 1 (south, AI) is active; its turn ends, rotation wraps back to seat 0 (north, human)
            // and the round completes.
            var toNorth = coordinator.RunTurn(state);
            state = toNorth.State;
            Assert.Equal("north", state.ActiveNationId);
            Assert.True(toNorth.RoundTickRan, "Wrapping back to the first seat must complete the round.");

            var handoffs = toNorth.Events.OfType<SeatHandoffRequested>().ToArray();
            var handoff = Assert.Single(handoffs);
            Assert.Equal("north", handoff.NationId);
        }
    }
}
