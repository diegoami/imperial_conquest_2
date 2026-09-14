using System.Linq;
using System.Reflection;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.News;
using IC2.Engine.Tests.Core;

namespace IC2.Engine.Tests.News;

/// <summary>
/// Test-only events and systems for <c>NewsLogWriterTests</c> and <c>NewsLogWriterAppendTests</c> — DoD 4:
/// "A news-worthy event published to the sink during a turn appears as a rendered message in
/// <c>GameState.NewsLog</c> at the end of that turn... The 40-slot eviction is asserted end-to-end
/// through the writer."
/// </summary>
/// <remarks>
/// <para>
/// <strong>Event kinds live in a namespace-unique fixture group</strong> (<c>docs/task-catalogue.md</c>'s
/// hazard for this task, N10): <see cref="NonNewsworthyFixtureEvent"/>'s kind is
/// <c>test.news-log.non-newsworthy-fixture</c>, never a corpus- or production-shaped name, so it can never
/// collide with a later task's own test fixture in the shared test assembly.
/// </para>
/// <para>
/// <see cref="CityFallsToFixtureEvent"/> and <see cref="FleetLostAtSeaFixtureEvent"/> are the two
/// exceptions, and deliberately so: DoD 4 requires proving the news log is populated through the
/// <em>real, registry-instantiated</em> <see cref="NewsLogWriterSeatEnd"/> and
/// <see cref="NewsLogWriterRoundEnd"/> — which resolve every template through the real
/// <see cref="NewsMessageCatalog"/>, with no way to substitute a test-only template without adding a
/// fixture-only entry to that production catalog (which would then need its own justification, and
/// would grow instead of shrink the catalog's public surface for a reason unrelated to any report or
/// corpus). Reusing the two real, corpus-confirmed kinds <c>city.falls-to</c> and <c>fleet.lost-at-sea</c>
/// — which this task's own catalog owns permanently — keeps the DoD-4 pipeline tests genuinely
/// end-to-end without that. This is a smaller, understood instance of the same risk N10 flagged (a later
/// task's <em>own</em> test fixture could independently pick the same kind for the same reason T10 did),
/// reduced from the twelve production-shaped kinds the first attempt declared down to these two. The
/// direct <see cref="NewsLogWriter.Append"/> unit tests in <c>NewsLogWriterAppendTests</c> need no catalog
/// entry at all — they inject their own template resolver — and use fully unique kinds throughout.
/// </para>
/// </remarks>
public static class NewsLogWriterFixtures
{
    /// <summary>Orders publishes a seat-scoped event; SeatEnd's writer should render it. DoD 4(a), 4(d).</summary>
    public const string SeatScopedGroup = "news-log.seat-scoped";

    /// <summary>Orders publishes a non-news-worthy event; it must never appear. DoD 4(b).</summary>
    public const string NonNewsworthyGroup = "news-log.non-newsworthy";

    /// <summary>WeatherEvents publishes a round-scoped event, driven by <c>RunRoundTick</c> alone. DoD 4(c).</summary>
    public const string RoundScopedGroup = "news-log.round-scoped";

    /// <summary>
    /// A seat's turn that always requests the round tick, so <c>RunTurn</c> follows straight on into it —
    /// proving a seat-scoped and a round-scoped event in the very same run are each rendered exactly once,
    /// by the writer whose scope they belong to.
    /// </summary>
    public const string FollowOnGroup = "news-log.follow-on";

    /// <summary>
    /// A fixture registered in <c>SeatEnd</c> whose id sorts <em>after</em> <c>news.writer</c>
    /// alphabetically, at the default <c>Order</c>. Proves the writer's <c>Order = int.MaxValue</c> is
    /// doing real work: without it, ties within the phase break by id, and <c>news.writer</c> would run
    /// (and see nothing) before this fixture ever publishes.
    /// </summary>
    public const string SeatOrderGroup = "news-log.seat-order";

    /// <summary>The <see cref="SeatOrderGroup"/> scenario, for <c>RoundEnd</c> and <c>news.writer.round</c>.</summary>
    public const string RoundOrderGroup = "news-log.round-order";

    /// <summary>The production writer systems, always included alongside whichever fixture group is scanned.</summary>
    private static readonly Type[] ProductionSystemTypes =
    {
        typeof(NewsLogWriterSeatEnd),
        typeof(NewsLogWriterRoundEnd),
    };

    /// <summary>
    /// A registry over the real writer systems plus one fixture group's test-only systems — never the
    /// whole engine assembly (irrelevant production systems would only add noise) and never the whole test
    /// assembly (every other task's fixtures would leak in).
    /// </summary>
    public static SystemRegistry RegistryFor(string group) =>
        SystemRegistry.FromAssemblies(
            new[] { typeof(NewsLogWriterSeatEnd).Assembly, typeof(NewsLogWriterFixtures).Assembly },
            type => ProductionSystemTypes.Contains(type)
                || string.Equals(
                    type.GetCustomAttribute<TestFixtureGroupAttribute>(inherit: false)?.Group,
                    group,
                    StringComparison.Ordinal));

    /// <summary>A coordinator over one fixture group's registry, publishing to <paramref name="sink"/>.</summary>
    public static TurnCoordinator CoordinatorFor(string group, IEventSink sink) =>
        new(RegistryFor(group), CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, sink);
}

/// <summary>
/// A seat-scoped, news-worthy fixture reusing the real, corpus-confirmed <c>city.falls-to</c> kind. See
/// <see cref="NewsLogWriterFixtures"/>'s remarks for why.
/// </summary>
[DomainEvent("city.falls-to", NewsWorthy = true)]
public sealed record CityFallsToFixtureEvent(string CityName, string OldOwner, string NewOwner) : DomainEvent;

/// <summary>
/// A round-scoped, news-worthy fixture reusing the real, corpus-confirmed <c>fleet.lost-at-sea</c> kind.
/// See <see cref="NewsLogWriterFixtures"/>'s remarks for why.
/// </summary>
[DomainEvent("fleet.lost-at-sea", NewsWorthy = true)]
public sealed record FleetLostAtSeaFixtureEvent(string Nation) : DomainEvent;

/// <summary>A non-news-worthy fixture, under a namespace-unique kind — no catalog entry is needed or exists.</summary>
[DomainEvent("test.news-log.non-newsworthy-fixture", NewsWorthy = false)]
public sealed record NonNewsworthyFixtureEvent(string Detail) : DomainEvent;

// --- SeatScopedGroup: Orders publishes; SeatEnd's real writer renders it. -------------------------------

/// <summary>
/// Publishes a distinct city name derived from the news log's own current size, so a run of several turns
/// produces distinguishable messages without any state of its own (<see cref="IGameSystem"/>'s own
/// contract) — turn 1 sees an empty log and publishes "City0", turn 2 sees one entry and publishes
/// "City1", and so on; once the ring buffer saturates at capacity the count plateaus there, which is
/// exactly the point DoD 4(d)'s eviction test needs.
/// </summary>
[TestFixtureGroup(NewsLogWriterFixtures.SeatScopedGroup)]
[GameSystem(TurnPhase.Orders, "test.news-log.seat-scoped-publisher")]
public sealed class SeatScopedPublisherSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        var index = context.State.NewsLog.Slots.Count;
        context.Events.Publish(new CityFallsToFixtureEvent($"City{index}", "Old", "New"));
        return context.State;
    }
}

// --- NonNewsworthyGroup: isolated so nothing else can add to the log during this scenario. ---------------

[TestFixtureGroup(NewsLogWriterFixtures.NonNewsworthyGroup)]
[GameSystem(TurnPhase.Orders, "test.news-log.non-newsworthy-publisher")]
public sealed class NonNewsworthyPublisherSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        context.Events.Publish(new NonNewsworthyFixtureEvent("should never be rendered"));
        return context.State;
    }
}

// --- RoundScopedGroup: WeatherEvents publishes; RoundEnd's real writer renders it, driven by RunRoundTick.

[TestFixtureGroup(NewsLogWriterFixtures.RoundScopedGroup)]
[GameSystem(TurnPhase.WeatherEvents, "test.news-log.round-scoped-publisher")]
public sealed class RoundScopedPublisherSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        context.Events.Publish(new FleetLostAtSeaFixtureEvent("NavalNation"));
        return context.State;
    }
}

// --- FollowOnGroup: a seat's turn that always signals the round tick, so RunTurn runs the seat-scoped
// phases and then follows straight on into the round-scoped ones, all in the same run -- proving each of
// the two writers renders only the events in its own scope, exactly once.

[TestFixtureGroup(NewsLogWriterFixtures.FollowOnGroup)]
[GameSystem(TurnPhase.Orders, "test.news-log.fo-seat-publisher")]
public sealed class FollowOnSeatPublisherSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        context.Events.Publish(new CityFallsToFixtureEvent("FollowOnCity", "Old", "New"));
        return context.State;
    }
}

/// <summary>Runs before the seat-scoped writer (default <c>Order</c> against its <c>int.MaxValue</c>) and always asks for the round tick.</summary>
[TestFixtureGroup(NewsLogWriterFixtures.FollowOnGroup)]
[GameSystem(TurnPhase.SeatEnd, "test.news-log.fo-round-requester")]
public sealed class FollowOnRoundRequesterSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        context.Signals.RequestRoundTick();
        return context.State;
    }
}

[TestFixtureGroup(NewsLogWriterFixtures.FollowOnGroup)]
[GameSystem(TurnPhase.WeatherEvents, "test.news-log.fo-weather-publisher")]
public sealed class FollowOnWeatherPublisherSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        context.Events.Publish(new FleetLostAtSeaFixtureEvent("FollowOnFleetNation"));
        return context.State;
    }
}

// --- SeatOrderGroup: a SeatEnd fixture at the default Order, with an id that sorts after "news.writer" --
// proving the writer's Order = int.MaxValue, not id-based luck, is what puts it last.

[TestFixtureGroup(NewsLogWriterFixtures.SeatOrderGroup)]
[GameSystem(TurnPhase.SeatEnd, "zzz-fixture.late-seat-publisher")]
public sealed class LateSeatEndPublisherSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        context.Events.Publish(new CityFallsToFixtureEvent("OrderProofCity", "Old", "New"));
        return context.State;
    }
}

// --- RoundOrderGroup: the same proof for RoundEnd and "news.writer.round". --------------------------------

[TestFixtureGroup(NewsLogWriterFixtures.RoundOrderGroup)]
[GameSystem(TurnPhase.RoundEnd, "zzz-fixture.late-round-publisher")]
public sealed class LateRoundEndPublisherSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        context.Events.Publish(new FleetLostAtSeaFixtureEvent("RoundOrderProofNation"));
        return context.State;
    }
}

/// <summary>A second sink chained after the run's recorder, standing in for the Godot UI's own consumer.</summary>
/// <remarks>
/// Used to build a realistic <see cref="CompositeEventSink"/> for <c>NewsLogWriterTests</c>'s coordinators,
/// rather than a bare sink — the first attempt's writer broke specifically under this composition (it
/// reflected into the sink chain to find itself; see <c>docs/task-catalogue.md</c>'s hazard, R7). This
/// task's design reads <see cref="SystemContext.PublishedEvents"/> instead, which does not care what
/// <see cref="TurnCoordinator"/> was constructed with, but the tests still wire a composed sink to make
/// that robustness explicit rather than assumed.
/// </remarks>
public sealed class RecordingUiStyleSink : IEventSink
{
    private readonly List<DomainEvent> _received = new();

    /// <summary>Everything this stand-in "UI" received, in order.</summary>
    public IReadOnlyList<DomainEvent> Received => _received;

    /// <inheritdoc/>
    public void Publish(DomainEvent domainEvent) => _received.Add(domainEvent);
}
