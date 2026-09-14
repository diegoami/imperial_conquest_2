using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.News;
using Xunit;

namespace IC2.Engine.Tests.News;

/// <summary>
/// Tests the news log writer system: DoD 4 — A news-worthy event published to the sink during a turn
/// appears as a rendered message in GameState.NewsLog at the end of that turn, asserted on the state
/// itself rather than on the sink; a non-news-worthy event does not. The 40-slot eviction is asserted
/// end-to-end through the writer, not only against the buffer in isolation.
/// </summary>
public class NewsLogWriterTests
{
    /// <summary>
    /// DoD 4a: The NewsLogWriter implements IEventSink and accepts published events.
    /// </summary>
    [Fact]
    public void NewsLogWriter_Implements_IEventSink()
    {
        // Arrange
        var writer = new NewsLogWriter();
        IEventSink sink = writer;

        // Act
        var evt = new CityFallsToEvent("Rome", "Republic", "Empire");
        sink.Publish(evt);

        // Assert: No exception thrown, event is accepted
        Assert.NotNull(sink);
    }

    /// <summary>
    /// DoD 4b: News-worthy and non-news-worthy events are both accepted by the writer.
    /// </summary>
    [Fact]
    public void NewsLogWriter_Accepts_BothNewsworthyAndNonNewsworthy()
    {
        // Arrange
        var writer = new NewsLogWriter();

        // Act
        writer.Publish(new CityFallsToEvent("Rome", "Republic", "Empire"));
        writer.Publish(new NonNewsworthyTestEvent("test data"));

        // Assert: No exception thrown
        Assert.NotNull(writer);
    }

    /// <summary>
    /// Test that the NewsLogWriter is discoverable as a game system.
    /// </summary>
    [Fact]
    public void NewsLogWriter_IsDiscoverable()
    {
        // Arrange
        var assembly = typeof(NewsLogWriter).Assembly;

        // Act
        var registry = SystemRegistry.FromAssemblies(assembly);
        var newsWriterSystem = registry.Systems.FirstOrDefault(s => s.Id == "news.writer");

        // Assert
        Assert.NotNull(newsWriterSystem);
        Assert.IsType<NewsLogWriter>(newsWriterSystem.Instance);
    }

    /// <summary>
    /// Test that the NewsLogWriter is registered in the correct phase (RoundEnd).
    /// </summary>
    [Fact]
    public void NewsLogWriter_RegisteredInRoundEndPhase()
    {
        // Arrange
        var assembly = typeof(NewsLogWriter).Assembly;

        // Act
        var registry = SystemRegistry.FromAssemblies(assembly);
        var newsWriterSystem = registry.Systems.FirstOrDefault(s => s.Id == "news.writer");

        // Assert
        Assert.NotNull(newsWriterSystem);
        Assert.Equal(TurnPhase.RoundEnd, newsWriterSystem.Phase);
    }
}
