using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.News;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.News;

/// <summary>
/// Tests the news log writer system via real TurnCoordinator integration.
/// DoD 4 — A news-worthy event published to the sink during a turn appears as a rendered message
/// in GameState.NewsLog at the end of that turn; a non-news-worthy event does not. The 40-slot eviction
/// is asserted end-to-end through the writer, not only against the buffer in isolation.
/// </summary>
[Collection("news-log-writer-integration")]
public class NewsLogWriterTests
{

    /// <summary>
    /// DoD 4(a): A news-worthy event published during a seat-scoped phase (Orders) appears in
    /// GameState.NewsLog at the end of that seat's turn (after SeatEnd phase).
    /// </summary>
    [Fact]
    public void NewsLogWriter_SeatScopedEventAppearsInLogAtSeatEnd()
    {
        // Arrange: Get the writer instance from the registry (the exact same instance that runs as a system)
        var registry = SystemRegistry.FromAssemblies(typeof(NewsLogWriter).Assembly);
        var writerSystem = registry.Systems.First(s => s.Id == "news.writer");
        var writer = (NewsLogWriter)writerSystem.Instance;

        var coordinator = new TurnCoordinator(registry, CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, sink: writer);
        var state = CoreTestbed.InitialState();

        // Act: Publish an event BEFORE running the turn, simulating publication during Orders phase
        writer.Publish(new CityFallsToEvent("Rome", "Republic", "Empire"));

        // Execute the turn (all phases including SeatEnd where the writer system runs)
        var result = coordinator.RunTurn(state);

        // Assert: Event appears in the log with correct rendering
        var newsLog = result.State.NewsLog;
        Assert.NotEmpty(newsLog.Slots);
        Assert.Single(newsLog.Slots, s => s.Text.Contains("Rome"));
    }

    /// <summary>
    /// DoD 4(b): A non-news-worthy event published to the sink does NOT appear in GameState.NewsLog.
    /// </summary>
    [Fact]
    public void NewsLogWriter_NonNewsworthyEventDoesNotAppear()
    {
        // Arrange: Get the writer instance from the registry
        var registry = SystemRegistry.FromAssemblies(typeof(NewsLogWriter).Assembly);
        var writerSystem = registry.Systems.First(s => s.Id == "news.writer");
        var writer = (NewsLogWriter)writerSystem.Instance;

        var coordinator = new TurnCoordinator(registry, CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, sink: writer);
        var state = CoreTestbed.InitialState();
        var initialLogCount = state.NewsLog.Slots.Count;

        // Act: Publish a non-newsworthy event
        writer.Publish(new NonNewsworthyTestEvent("should be ignored"));

        // Execute the turn
        var result = coordinator.RunTurn(state);

        // Assert: Log size unchanged, event not rendered
        Assert.Equal(initialLogCount, result.State.NewsLog.Slots.Count);
    }

    /// <summary>
    /// DoD 4(c): A 40-slot ring buffer enforces capacity: 41 events through the writer results in
    /// exactly 40 remaining, with the oldest evicted. Asserted end-to-end through real turns.
    /// </summary>
    [Fact]
    public void NewsLogWriter_EnforcesCapacityThroughEvictionAcrossMultipleTurns()
    {
        // Arrange: Get the writer instance from the registry
        var registry = SystemRegistry.FromAssemblies(typeof(NewsLogWriter).Assembly);
        var writerSystem = registry.Systems.First(s => s.Id == "news.writer");
        var writer = (NewsLogWriter)writerSystem.Instance;

        var coordinator = new TurnCoordinator(registry, CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, sink: writer);
        var state = CoreTestbed.InitialState();

        // Act: Publish 41 events across multiple turns to test eviction
        for (var i = 0; i < 41; i++)
        {
            // Reuse existing events with varying parameters to create distinct messages
            writer.Publish(new CityFallsToEvent($"City{i}", "Old", "New"));
            // Run one turn per event to flush through the system
            var result = coordinator.RunTurn(state);
            state = result.State;
        }

        // Assert: Exactly 40 entries, oldest (City0) evicted, newest (City40) present
        var newsLog = state.NewsLog;
        Assert.Equal(40, newsLog.Slots.Count);
        var texts = newsLog.Slots.Select(s => s.Text).ToList();
        Assert.DoesNotContain(texts, t => t.Contains("City0"));
        Assert.Contains(texts, t => t.Contains("City40"));
    }

    /// <summary>
    /// DoD 4(d): Round-scoped events (published in WeatherEvents phase) appear at the round boundary
    /// in RoundEnd phase, not deferred to a later seat's turn.
    /// </summary>
    [Fact]
    public void NewsLogWriter_RoundScopedEventAppearsAtRoundBoundary()
    {
        // Arrange: Get the writer instance from the registry
        var registry = SystemRegistry.FromAssemblies(typeof(NewsLogWriter).Assembly);
        var writerSystem = registry.Systems.First(s => s.Id == "news.writer");
        var writer = (NewsLogWriter)writerSystem.Instance;

        var coordinator = new TurnCoordinator(registry, CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, sink: writer);
        var state = CoreTestbed.InitialState();

        // Act: Publish a round-scoped event and run a complete round
        writer.Publish(new FleetLostAtSeaEvent("NavalNation"));
        var result = coordinator.RunRoundTick(state);

        // Assert: Event appears in the log (round boundary has been processed)
        var newsLog = result.State.NewsLog;
        Assert.NotEmpty(newsLog.Slots);
        Assert.True(
            newsLog.Slots.Any(s => s.Text.Contains("NavalNation")),
            "Round-scoped event should appear after RoundEnd phase"
        );
    }

    /// <summary>
    /// Verification: The main writer system and its round-end helper are both discoverable and
    /// correctly registered with the engine's system registry. The main system (IGameSystem) is
    /// the exact same instance as wired to the coordinator as IEventSink.
    /// </summary>
    [Fact]
    public void NewsLogWriter_SystemsAreDiscoverableInRegistry()
    {
        // Arrange
        var assembly = typeof(NewsLogWriter).Assembly;
        var registry = SystemRegistry.FromAssemblies(assembly);

        // Act: Find both systems
        var seatEndSystem = registry.Systems.FirstOrDefault(s => s.Id == "news.writer");
        var roundEndSystem = registry.Systems.FirstOrDefault(s => s.Id == "news.writer.round");

        // Assert: Both exist and are in correct phases, and the main system is a NewsLogWriter instance
        Assert.NotNull(seatEndSystem);
        Assert.NotNull(roundEndSystem);
        Assert.Equal(TurnPhase.SeatEnd, seatEndSystem.Phase);
        Assert.Equal(TurnPhase.RoundEnd, roundEndSystem.Phase);
        Assert.IsType<NewsLogWriter>(seatEndSystem.Instance);
        Assert.IsType<NewsLogWriterRoundEnd>(roundEndSystem.Instance);
    }
}
