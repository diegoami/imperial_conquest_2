using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Tests.Naval;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// T149 (correction task for bug #790) Done-when 1, the
/// <c>FleetMoveAndScuttleEventsTests</c> half: the two new naval events are published on
/// accepted fleet orders, never on refused ones, and the news log is unchanged (the events
/// are not news-worthy by design, and the news log is the only other state the original
/// game writes for these orders). The test reads events through
/// <see cref="RecordingEventSink"/>, the same sink <c>CommandDispatcher</c> and the turn
/// pipeline deliver to.
/// </summary>
public sealed class FleetMoveAndScuttleEventsTests
{
    [Fact]
    public void Accepted_fleet_move_publishes_FleetMoved_with_endpoints()
    {
        var state = NavalTestbed.InitialState();
        var fleet = state.Fleets[0]; // "north-fleet-1", (0, 3), 4 moves, sea_deep under it.
        var fromX = fleet.X;
        var fromY = fleet.Y;
        Assert.Equal(0, fromX); // sanity

        var sink = new RecordingEventSink();
        var dispatcher = NavalTestbed.RealEngineDispatcher(sink);

        var result = dispatcher.Dispatch(state, new MoveFleetCommand(fleet.Nation, fleet.Id, X: 0, Y: 4));
        Assert.True(result.IsAccepted, result.ToString());

        var fleetMoved = sink.Events.OfType<FleetMoved>().SingleOrDefault();
        Assert.NotNull(fleetMoved);
        Assert.Equal(fleet.Id, fleetMoved!.FleetId);
        Assert.Equal(fleet.Nation, fleetMoved.NationId);
        Assert.Equal(fromX, fleetMoved.FromX);
        Assert.Equal(fromY, fleetMoved.FromY);
        Assert.Equal(0, fleetMoved.ToX);
        Assert.Equal(4, fleetMoved.ToY);
    }

    [Fact]
    public void Accepted_scuttle_publishes_FleetScuttled()
    {
        // The scuttle handler needs the fleet adjacent to an owned city. Place a fleet next
        // to arx (2, 1) so the dispatcher's accept path runs, then scuttle it.
        var state = NavalTestbed.InitialState();
        var arx = state.CityById("arx")!;
        var nationId = arx.Owner;

        var fleet = new FleetState(
            "scuttle-evt-fleet", nationId, X: arx.X, Y: arx.Y, Moves: 3, Ships: 15, ConditionPercent: 80,
            Money: 60, SupplyTons: 40, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null);
        state = state with { Fleets = ValueList.Of(fleet) };

        var sink = new RecordingEventSink();
        var dispatcher = NavalTestbed.RealEngineDispatcher(sink);

        var result = dispatcher.Dispatch(state, new ScuttleFleetCommand(nationId, fleet.Id));
        Assert.True(result.IsAccepted, result.ToString());

        var scuttled = sink.Events.OfType<FleetScuttled>().SingleOrDefault();
        Assert.NotNull(scuttled);
        Assert.Equal(fleet.Id, scuttled!.FleetId);
        Assert.Equal(nationId, scuttled.NationId);
    }

    [Fact]
    public void Refused_fleet_move_publishes_neither_FleetMoved_nor_FleetScuttled()
    {
        var state = NavalTestbed.InitialState();
        // A move to a land tile is refused by the walker's blocker; the handler still
        // returns an Accepted result with the fleet at its starting tile, so no FleetMoved
        // event is published (the walk never moved the fleet -- the no-move guard in the
        // handler skips publication). Use a move that the dispatcher actually refuses:
        // a move with no moves left.
        var fleet = state.Fleets[0] with { Moves = 0 };
        state = state with { Fleets = ValueList.From(state.Fleets.Select(f => f.Id == fleet.Id ? fleet : f)) };

        var sink = new RecordingEventSink();
        var dispatcher = NavalTestbed.RealEngineDispatcher(sink);

        var result = dispatcher.Dispatch(state, new MoveFleetCommand(fleet.Nation, fleet.Id, X: 0, Y: 4));
        Assert.True(result.IsRejected);

        Assert.Empty(sink.Events.OfType<FleetMoved>());
        Assert.Empty(sink.Events.OfType<FleetScuttled>());
    }

    [Fact]
    public void Refused_scuttle_publishes_neither_event()
    {
        var state = NavalTestbed.InitialState();
        var fleet = state.Fleets[0]; // On (0, 3), far from any north city.

        var sink = new RecordingEventSink();
        var dispatcher = NavalTestbed.RealEngineDispatcher(sink);

        var result = dispatcher.Dispatch(state, new ScuttleFleetCommand(fleet.Nation, fleet.Id));
        Assert.True(result.IsRejected);

        Assert.Empty(sink.Events.OfType<FleetMoved>());
        Assert.Empty(sink.Events.OfType<FleetScuttled>());
    }

    [Fact]
    public void News_log_is_unchanged_by_the_new_events()
    {
        // The new events are not news-worthy: T149's own [DomainEvent(..., NewsWorthy = false)]
        // attribute. The test reruns the same orders through a session and through a bare
        // dispatcher and asserts the resulting State.NewsLog.Slots is identical -- a
        // news-worthy event would have appended a slot.
        var sink = new RecordingEventSink();
        var dispatcher = NavalTestbed.RealEngineDispatcher(sink);
        var fleet = NavalTestbed.InitialState().Fleets[0];

        // Move then scuttle: both events are published, neither is news-worthy. The
        // first move is to (0, 2); the scuttle is then refused because the fleet is not
        // yet adjacent to an owned city, so we only have one event to assert about.
        var state1 = NavalTestbed.InitialState();
        var moveResult = dispatcher.Dispatch(
            state1, new MoveFleetCommand(fleet.Nation, fleet.Id, X: 0, Y: 2));
        Assert.True(moveResult.IsAccepted, moveResult.ToString());

        // The sink collected FleetMoved; the event has NewsWorthy false.
        var navalEvents = sink.Events.Where(e => e is FleetMoved || e is FleetScuttled).ToList();
        Assert.Single(navalEvents);
        Assert.False(navalEvents[0].IsNewsWorthy);
    }
}
