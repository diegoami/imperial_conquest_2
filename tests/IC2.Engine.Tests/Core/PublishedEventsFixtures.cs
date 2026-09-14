using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Tests.Core;

/// <summary>
/// Fixtures exercising T40's seam: <see cref="SystemContext.PublishedEvents"/>.
/// </summary>
/// <remarks>
/// Three separate fixture groups, so each test's pipeline is exactly the shape its Definition of Done
/// line needs, and no group's systems can leak into another's assertions. A reader system never has a
/// return channel of its own — a system speaks only through the returned state and
/// <see cref="SystemContext.Events"/> — so every reader below publishes what it saw as a
/// <see cref="PublishedEventsSnapshot"/> for the test to assert against.
/// </remarks>
public static class PublishedEventsFixtures
{
    /// <summary>Seat-scoped phases only: nothing here ever requests the round tick.</summary>
    public const string SeatGroup = "published-events-seat";

    /// <summary>Round-scoped phases only, driven directly by <see cref="TurnCoordinator.RunRoundTick"/>.</summary>
    public const string RoundGroup = "published-events-round";

    /// <summary>A seat's turn that always asks for the round tick, so <c>RunTurn</c> follows on into it.</summary>
    public const string FollowOnGroup = "published-events-follow-on";

    /// <summary>
    /// Renders one <see cref="PublishedEvent"/> for assertion: its phase, its publisher, and a tag telling
    /// two events of the same kind apart. Deliberately not <c>ToString()</c> on the event itself — a
    /// record's synthesised <c>ToString</c> also prints <see cref="DomainEvent.Kind"/> and
    /// <see cref="DomainEvent.IsNewsWorthy"/>, which would make this brittle against unrelated changes.
    /// </summary>
    public static string Describe(PublishedEvent entry) => entry.Event switch
    {
        PublishedMarker marker => $"{entry.Phase}/{entry.SystemId}:{marker.Origin}",
        PublishedEventsSnapshot => $"{entry.Phase}/{entry.SystemId}:snapshot",
        _ => $"{entry.Phase}/{entry.SystemId}:{entry.Event.Kind}",
    };
}

/// <summary>A marker a fixture published, naming where it came from.</summary>
[DomainEvent("test.published.marker")]
public sealed record PublishedMarker(string Origin) : DomainEvent;

/// <summary>What a reader system saw through <see cref="SystemContext.PublishedEvents"/> at the moment it ran.</summary>
[DomainEvent("test.published.snapshot")]
public sealed record PublishedEventsSnapshot(ValueList<string> Seen) : DomainEvent;

// --- SeatGroup: SeatStart -> Orders (direct publish + a command) -> SeatEnd (an earlier system, then the
// reader). No system requests the round tick, so only the seat-scoped phases ever run.

/// <summary>Publishes directly, and is also where the "a fresh run starts empty" assertion is taken.</summary>
[TestFixtureGroup(PublishedEventsFixtures.SeatGroup)]
[GameSystem(TurnPhase.SeatStart, "test.published.seat-start")]
public sealed class SeatGroupSeatStartSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        context.Events.Publish(new PublishedEventsSnapshot(
            ValueList.From(context.PublishedEvents.Select(PublishedEventsFixtures.Describe))));
        context.Events.Publish(new PublishedMarker("seat-start"));
        return context.State;
    }
}

/// <summary>Publishes directly, and issues a command whose handler publishes too — the second source.</summary>
[TestFixtureGroup(PublishedEventsFixtures.SeatGroup)]
[GameSystem(TurnPhase.Orders, "test.published.orders")]
public sealed class SeatGroupOrdersSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        context.Events.Publish(new PublishedMarker("orders-direct"));

        var result = context.Commands.Dispatch(
            context.State, new PublishedEventsCommand(context.ActiveNationId));

        return result.State;
    }
}

/// <summary>An earlier system within SeatEnd, so the reader after it can be shown to see it.</summary>
[TestFixtureGroup(PublishedEventsFixtures.SeatGroup)]
[GameSystem(TurnPhase.SeatEnd, "test.published.seat-end-early", Order = 10)]
public sealed class SeatGroupSeatEndEarlySystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        context.Events.Publish(new PublishedMarker("seat-end-early"));
        return context.State;
    }
}

/// <summary>Reads <see cref="SystemContext.PublishedEvents"/> and publishes what it saw.</summary>
[TestFixtureGroup(PublishedEventsFixtures.SeatGroup)]
[GameSystem(TurnPhase.SeatEnd, "test.published.seat-end-reader", Order = 20)]
public sealed class SeatGroupSeatEndReaderSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        context.Events.Publish(new PublishedEventsSnapshot(
            ValueList.From(context.PublishedEvents.Select(PublishedEventsFixtures.Describe))));
        return context.State;
    }
}

/// <summary>The command <see cref="SeatGroupOrdersSystem"/> issues.</summary>
public sealed record PublishedEventsCommand(string IssuingNationId) : ICommand
{
    /// <inheritdoc/>
    public string Kind => "test.published.command";
}

/// <summary>Always accepts, and publishes as a real handler would.</summary>
[TestFixtureGroup(PublishedEventsFixtures.SeatGroup)]
[CommandHandler]
public sealed class PublishedEventsCommandHandler : ICommandHandler<PublishedEventsCommand>
{
    /// <inheritdoc/>
    public CommandOutcome Handle(PublishedEventsCommand command, CommandContext context)
    {
        context.Events.Publish(new PublishedMarker("orders-command"));
        return CommandOutcome.Accept(context.State);
    }
}

// --- RoundGroup: CityTick publishes directly; CalendarAdvance fires the quarter boundary, whose handler
// publishes -- the third source; RoundEnd reads. Driven with RunRoundTick, so no seat-scoped phase runs.

/// <summary>Publishes directly in a round-scoped phase.</summary>
[TestFixtureGroup(PublishedEventsFixtures.RoundGroup)]
[GameSystem(TurnPhase.CityTick, "test.published.city-tick")]
public sealed class RoundGroupCityTickSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        context.Events.Publish(new PublishedMarker("city-tick"));
        return context.State;
    }
}

/// <summary>Fires the quarter boundary from inside its own phase, exactly as T06's calendar will.</summary>
[TestFixtureGroup(PublishedEventsFixtures.RoundGroup)]
[GameSystem(TurnPhase.CalendarAdvance, "test.published.calendar")]
public sealed class RoundGroupCalendarSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context) =>
        context.QuarterBoundary.Fire(context.State, context.State.Calendar.SeasonIndex);
}

/// <summary>Publishes from inside a quarter-boundary handler -- the third source DoD item 2 names.</summary>
[TestFixtureGroup(PublishedEventsFixtures.RoundGroup)]
[QuarterBoundaryHandler("test.published.quarter-handler")]
public sealed class RoundGroupQuarterHandler : IQuarterBoundaryHandler
{
    /// <inheritdoc/>
    public GameState OnQuarterBoundary(QuarterBoundaryContext context)
    {
        context.Events.Publish(new PublishedMarker("quarter-handler"));
        return context.State;
    }
}

/// <summary>Reads <see cref="SystemContext.PublishedEvents"/> at the end of the round.</summary>
[TestFixtureGroup(PublishedEventsFixtures.RoundGroup)]
[GameSystem(TurnPhase.RoundEnd, "test.published.round-end-reader")]
public sealed class RoundGroupRoundEndReaderSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        context.Events.Publish(new PublishedEventsSnapshot(
            ValueList.From(context.PublishedEvents.Select(PublishedEventsFixtures.Describe))));
        return context.State;
    }
}

// --- FollowOnGroup: a seat's turn that always signals the round tick, so RunTurn runs the seat-scoped
// phases and then follows straight on into the round-scoped ones, all in the same run.

/// <summary>Publishes in SeatStart.</summary>
[TestFixtureGroup(PublishedEventsFixtures.FollowOnGroup)]
[GameSystem(TurnPhase.SeatStart, "test.published.fo-seat-start")]
public sealed class FollowOnSeatStartSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        context.Events.Publish(new PublishedMarker("fo-seat-start"));
        return context.State;
    }
}

/// <summary>Publishes in SeatEnd, and unconditionally asks for the round tick to follow on.</summary>
[TestFixtureGroup(PublishedEventsFixtures.FollowOnGroup)]
[GameSystem(TurnPhase.SeatEnd, "test.published.fo-seat-end")]
public sealed class FollowOnSeatEndSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        context.Events.Publish(new PublishedMarker("fo-seat-end"));
        context.Signals.RequestRoundTick();
        return context.State;
    }
}

/// <summary>Publishes in a round-scoped phase, once the follow-on carries the run into it.</summary>
[TestFixtureGroup(PublishedEventsFixtures.FollowOnGroup)]
[GameSystem(TurnPhase.CityTick, "test.published.fo-city-tick")]
public sealed class FollowOnCityTickSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        context.Events.Publish(new PublishedMarker("fo-city-tick"));
        return context.State;
    }
}

/// <summary>Reads <see cref="SystemContext.PublishedEvents"/> once the follow-on's round tick reaches RoundEnd.</summary>
[TestFixtureGroup(PublishedEventsFixtures.FollowOnGroup)]
[GameSystem(TurnPhase.RoundEnd, "test.published.fo-round-end-reader")]
public sealed class FollowOnRoundEndReaderSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        context.Events.Publish(new PublishedEventsSnapshot(
            ValueList.From(context.PublishedEvents.Select(PublishedEventsFixtures.Describe))));
        return context.State;
    }
}
