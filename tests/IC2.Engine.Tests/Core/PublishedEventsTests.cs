using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Core;

/// <summary>
/// T40's Definition of Done: "Expose the run's published events to systems (a T03 seam)" —
/// <c>docs/task-catalogue.md</c>. Every reader fixture speaks only through an event it publishes (a
/// system's only outward channel), so each test reads the rendered <see cref="PublishedEventsSnapshot"/>
/// off the run's own event stream rather than reaching into <see cref="SystemContext"/> directly.
/// </summary>
public class PublishedEventsTests
{
    [Fact]
    public void PublishedEvents_is_exposed_as_a_read_only_immutable_list()
    {
        // DoD item 1: "The list is read-only; a system cannot modify it." ValueList<T> exposes no
        // mutating member (Add/Remove/indexer setter) and the property has no setter of its own, so this
        // is a property of the type, asserted here rather than left implicit.
        var property = typeof(SystemContext).GetProperty(nameof(SystemContext.PublishedEvents));

        Assert.NotNull(property);
        Assert.Equal(typeof(ValueList<PublishedEvent>), property!.PropertyType);
        Assert.Null(property.SetMethod);
    }

    [Fact]
    public void A_fresh_run_starts_with_no_published_events()
    {
        // DoD item 3: "A fresh run starts empty, so nothing carries over between turns." Taken at the
        // very first system of a fresh RunTurn call, before anything in this run has published at all.
        var sink = new RecordingEventSink();
        var coordinator = CoreTestbed.CoordinatorWithCommandsFor(PublishedEventsFixtures.SeatGroup, sink);

        var result = coordinator.RunTurn(CoreTestbed.InitialState());

        var firstSnapshot = result.Events.OfType<PublishedEventsSnapshot>().First();
        Assert.Empty(firstSnapshot.Seen);
    }

    [Fact]
    public void A_system_in_seat_end_sees_seat_start_orders_and_earlier_seat_end_systems()
    {
        // DoD item 3: "A system in SeatEnd sees the events of SeatStart, Orders and the systems before it
        // in SeatEnd." Also covers DoD item 2's first source directly: the reader's own view includes the
        // event published by the command SeatGroupOrdersSystem issued in Orders.
        var sink = new RecordingEventSink();
        var coordinator = CoreTestbed.CoordinatorWithCommandsFor(PublishedEventsFixtures.SeatGroup, sink);

        var result = coordinator.RunTurn(CoreTestbed.InitialState());

        var snapshots = result.Events.OfType<PublishedEventsSnapshot>().ToArray();
        Assert.Equal(2, snapshots.Length);
        var seatEndView = snapshots[1];

        Assert.Equal(
            new[]
            {
                "SeatStart/test.published.seat-start:snapshot",
                "SeatStart/test.published.seat-start:seat-start",
                "Orders/test.published.orders:orders-direct",
                "Orders/test.published.orders:orders-command",
                "SeatEnd/test.published.seat-end-early:seat-end-early",
            },
            seatEndView.Seen.ToArray());
    }

    [Fact]
    public void Events_published_through_a_command_appear_in_the_published_events_view()
    {
        // DoD item 2, first source: "events published through command dispatch ... since they go through
        // the same sink." SeatGroupOrdersSystem issues PublishedEventsCommand; its handler publishes from
        // inside CommandContext.Events, not SystemContext.Events, so this is genuinely the command path.
        var sink = new RecordingEventSink();
        var coordinator = CoreTestbed.CoordinatorWithCommandsFor(PublishedEventsFixtures.SeatGroup, sink);

        var result = coordinator.RunTurn(CoreTestbed.InitialState());

        var seatEndView = result.Events.OfType<PublishedEventsSnapshot>().ToArray()[1];
        Assert.Contains("Orders/test.published.orders:orders-command", seatEndView.Seen);
    }

    [Fact]
    public void Events_published_through_a_quarter_boundary_handler_appear_in_the_published_events_view()
    {
        // DoD item 2, second source: "... and quarter-boundary handlers during the run". The handler
        // publishes from inside QuarterBoundaryContext.Events, reached only through
        // SystemContext.QuarterBoundary.Fire, never through SystemContext.Events directly.
        var sink = new RecordingEventSink();
        var coordinator = CoreTestbed.CoordinatorFor(PublishedEventsFixtures.RoundGroup, sink);

        var result = coordinator.RunRoundTick(CoreTestbed.InitialState());

        var roundEndView = result.Events.OfType<PublishedEventsSnapshot>().Single();
        Assert.Contains("CalendarAdvance/test.published.calendar:quarter-handler", roundEndView.Seen);
    }

    [Fact]
    public void A_system_in_round_end_sees_every_round_scoped_phases_events()
    {
        // DoD item 3: "A system in RoundEnd sees the round-scoped phases' events." Run directly with
        // RunRoundTick, so no seat-scoped phase has run at all -- everything visible is round-scoped.
        var coordinator = CoreTestbed.CoordinatorFor(PublishedEventsFixtures.RoundGroup);

        var result = coordinator.RunRoundTick(CoreTestbed.InitialState());

        var roundEndView = result.Events.OfType<PublishedEventsSnapshot>().Single();
        Assert.Equal(
            new[]
            {
                "CityTick/test.published.city-tick:city-tick",
                "CalendarAdvance/test.published.calendar:quarter-handler",
            },
            roundEndView.Seen.ToArray());
    }

    [Fact]
    public void When_a_seats_turn_follows_on_into_the_round_tick_round_end_also_sees_the_seat_scoped_events()
    {
        // DoD item 3: "When RunTurn follows on into the round tick, it also sees the seat-scoped events of
        // the same run, told apart by their phase tag." FollowOnGroup's SeatEnd system always signals the
        // round tick, so one RunTurn call carries both scopes through the same published-events list.
        var coordinator = CoreTestbed.CoordinatorFor(PublishedEventsFixtures.FollowOnGroup);

        var result = coordinator.RunTurn(CoreTestbed.InitialState());

        Assert.True(result.RoundTickRan);
        var roundEndView = result.Events.OfType<PublishedEventsSnapshot>().Single();
        Assert.Equal(
            new[]
            {
                "SeatStart/test.published.fo-seat-start:fo-seat-start",
                "SeatEnd/test.published.fo-seat-end:fo-seat-end",
                "CityTick/test.published.fo-city-tick:fo-city-tick",
            },
            roundEndView.Seen.ToArray());
    }

    [Fact]
    public void The_callers_own_sink_and_TurnResult_Events_are_unaffected_by_the_new_seam()
    {
        // DoD item 4: "The caller's own sink still receives every event exactly as before, and
        // TurnResult.Events is unchanged." Both streams must carry every event this run published --
        // direct, via the command, and the two snapshots -- in the same order.
        var sink = new RecordingEventSink();
        var coordinator = CoreTestbed.CoordinatorWithCommandsFor(PublishedEventsFixtures.SeatGroup, sink);

        var result = coordinator.RunTurn(CoreTestbed.InitialState());

        Assert.Equal(6, sink.Events.Count);
        Assert.Equal(
            sink.Events.Select(e => e.Kind).ToArray(),
            result.Events.Select(e => e.Kind).ToArray());
    }
}
