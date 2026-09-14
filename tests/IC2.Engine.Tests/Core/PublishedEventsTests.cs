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
        // DoD item 4 (strengthened per review round 1's N2): "The caller's own sink still receives every
        // event exactly as before, and TurnResult.Events is unchanged." Compared against an explicit
        // expected sequence, by full record value (not merely by Kind, which could hide a changed payload
        // reaching one stream and not the other), and with the caller's own sink itself wired as a
        // CompositeEventSink over two sinks -- the exact shape PR #77's R7 finding broke on -- to show
        // tagging does not depend on the caller passing a single plain sink.
        var callerPrimary = new RecordingEventSink();
        var callerExtra = new RecordingEventSink();
        var callerSink = new CompositeEventSink(callerPrimary, callerExtra);
        var coordinator = CoreTestbed.CoordinatorWithCommandsFor(PublishedEventsFixtures.SeatGroup, callerSink);

        var result = coordinator.RunTurn(CoreTestbed.InitialState());

        var expectedKinds = new[]
        {
            "test.published.snapshot",
            "test.published.marker",
            "test.published.marker",
            "test.published.marker",
            "test.published.marker",
            "test.published.snapshot",
        };

        Assert.Equal(expectedKinds, callerPrimary.Events.Select(e => e.Kind).ToArray());

        // Every sink involved -- both halves of the caller's own composite, and TurnResult's own copy --
        // carry the exact same sequence of events, by full record equality, not merely by matching Kind.
        Assert.Equal(callerPrimary.Events, callerExtra.Events);
        Assert.Equal(callerPrimary.Events.ToArray(), result.Events.ToArray());
    }

    [Fact]
    public void A_second_run_on_the_same_coordinator_does_not_see_the_first_runs_events()
    {
        // B1 (review round 1): DoD item 3's "a fresh run starts empty, so nothing carries over between
        // turns" had no test that could fail against a coordinator that kept its published-event list
        // across runs instead of starting a fresh one each time -- every other test here builds a brand
        // new coordinator and runs it once. Reusing ONE coordinator for two RunTurn calls is what catches
        // that: a leaky implementation shows the second run's SeatEnd reader seeing ten entries (both
        // runs' events) instead of five, and its SeatStart reader seeing the first run's five instead of
        // none.
        var coordinator = CoreTestbed.CoordinatorWithCommandsFor(PublishedEventsFixtures.SeatGroup);
        var initial = CoreTestbed.InitialState();

        var first = coordinator.RunTurn(initial);
        var second = coordinator.RunTurn(initial);

        var firstSnapshots = first.Events.OfType<PublishedEventsSnapshot>().ToArray();
        var secondSnapshots = second.Events.OfType<PublishedEventsSnapshot>().ToArray();

        Assert.Equal(5, firstSnapshots[1].Seen.Count);

        // The second run's very first system still starts from nothing, exactly like the first run's did.
        Assert.Empty(secondSnapshots[0].Seen);

        // The second run's SeatEnd reader sees exactly what the first run's did: the same five entries
        // from THIS run, not the first run's five plus its own on top.
        Assert.Equal(firstSnapshots[1].Seen.ToArray(), secondSnapshots[1].Seen.ToArray());
        Assert.Equal(5, secondSnapshots[1].Seen.Count);
    }

    [Fact]
    public void A_round_tick_run_on_the_same_coordinator_after_a_turn_does_not_see_the_earlier_runs_events()
    {
        // B1 (review round 1), the second shape the review named explicitly: "RunTurn with a follow-on ->
        // RunRoundTick: the round tick's RoundEnd reader sees no seat-scoped entries from the previous
        // run." A leaky published-event list would show the second call's RoundEnd reader still holding
        // the first run's SeatStart/SeatEnd markers alongside its own CityTick one.
        var coordinator = CoreTestbed.CoordinatorFor(PublishedEventsFixtures.FollowOnGroup);
        var initial = CoreTestbed.InitialState();

        var followOn = coordinator.RunTurn(initial);
        Assert.True(followOn.RoundTickRan);

        var direct = coordinator.RunRoundTick(initial);

        var followOnView = followOn.Events.OfType<PublishedEventsSnapshot>().Single();
        var directView = direct.Events.OfType<PublishedEventsSnapshot>().Single();

        // Sanity check: the follow-on run's own RoundEnd reader really did see both scopes, as already
        // asserted end to end by When_a_seats_turn_follows_on_into_the_round_tick_round_end_also_sees_the_seat_scoped_events.
        Assert.Equal(3, followOnView.Seen.Count);

        // A direct RunRoundTick call on the SAME coordinator, run afterwards, sees only its OWN
        // round-scoped events -- nothing from the earlier turn's seat-scoped phases, and its CityTick
        // entry appears once, not twice.
        Assert.Equal(
            new[] { "CityTick/test.published.fo-city-tick:fo-city-tick" },
            directView.Seen.ToArray());
    }
}
