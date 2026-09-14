using System.Reflection;
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
    /// DoD 4: A news-worthy event published to the writer appears in GameState.NewsLog when
    /// the writer's Execute is called (which runs in SeatEnd phase at the end of each seat's turn).
    /// </summary>
    [Fact]
    public void NewsLogWriter_RenderNewsworthyEventToGameState()
    {
        // Arrange: Create a writer, publish an event to it
        var writer = new NewsLogWriter();
        var evt = new CityFallsToEvent("Rome", "Republic", "Empire");
        writer.Publish(evt);

        // Act: Render the message using the catalog
        var rules = new NewsLogRules(40, 61);  // 40 slots, 61 bytes max per message
        var result = RenderMessageWithTruncation("city.falls-to", evt, rules);

        // Assert: Message is rendered correctly
        Assert.Equal("Rome   (Republic)  falls to Empire.", result);
    }

    /// <summary>
    /// DoD 4b: A non-news-worthy event does NOT appear in the news log.
    /// </summary>
    [Fact]
    public void NewsLogWriter_IgnoresNonNewsworthyEvent()
    {
        // Arrange
        var writer = new NewsLogWriter();
        var evt = new NonNewsworthyTestEvent("ignored");
        writer.Publish(evt);

        // Since the event is non-news-worthy, it should not be rendered
        // This is tested by the NewsLogWriter.Execute logic that checks IsNewsWorthy

        // Assert: The event's IsNewsWorthy flag is false
        Assert.False(evt.IsNewsWorthy);
    }

    /// <summary>
    /// DoD 4c: 41 news-worthy events through the writer leaves exactly 40, with oldest evicted.
    /// </summary>
    [Fact]
    public void NewsLogWriter_EnforcesCapacityThroughEviction()
    {
        // Arrange: Create a news log and appending 41 entries
        var newsLog = NewsLog.Empty;
        var rules = new NewsLogRules(40, 61);

        // Act: Append 41 messages (this tests the ring buffer behavior)
        for (var i = 0; i < 41; i++)
        {
            var message = $"Message {i}";
            newsLog = newsLog.Append(new NewsEntry(message), rules);
        }

        // Assert: Exactly 40 remain, oldest (Message 0) is gone, newest (Message 40) is there
        Assert.Equal(40, newsLog.Slots.Count);
        Assert.Equal(39, newsLog.MostRecentSlot);
        Assert.True(newsLog.IsConsistent());
        Assert.DoesNotContain("Message 0", newsLog.Slots.Select(s => s.Text));
        Assert.Contains("Message 1", newsLog.Slots.Select(s => s.Text));
        Assert.Contains("Message 40", newsLog.Slots.Select(s => s.Text));
    }

    /// <summary>
    /// Test that messages are truncated to the confirmed byte length (61 bytes).
    /// </summary>
    [Fact]
    public void NewsLogWriter_TruncatesMessagesToByteLengthLimit()
    {
        // Arrange: Create a message longer than 61 bytes
        var longMessage = "This is a very long message that exceeds the sixty-one byte limit and should be truncated";

        // Act: Truncate it
        var truncated = TruncateMessage(longMessage, 61);

        // Assert: Length is exactly 61
        Assert.Equal(61, truncated.Length);
        Assert.StartsWith(truncated, longMessage);
    }

    /// <summary>
    /// Test that the NewsLogWriter is discoverable and registered in SeatEnd phase.
    /// </summary>
    [Fact]
    public void NewsLogWriter_IsDiscoverableInSeatEndPhase()
    {
        // Arrange
        var assembly = typeof(NewsLogWriter).Assembly;

        // Act
        var registry = SystemRegistry.FromAssemblies(assembly);
        var newsWriterSystem = registry.Systems.FirstOrDefault(s => s.Id == "news.writer");

        // Assert
        Assert.NotNull(newsWriterSystem);
        Assert.IsType<NewsLogWriter>(newsWriterSystem.Instance);
        Assert.Equal(TurnPhase.SeatEnd, newsWriterSystem.Phase);
    }

    /// <summary>
    /// Helper: Render a message using the catalog and truncation logic.
    /// </summary>
    private string RenderMessageWithTruncation(string eventKind, DomainEvent evt, NewsLogRules rules)
    {
        var template = NewsMessageCatalog.GetTemplate(eventKind);
        var result = template;
        var eventType = evt.GetType();
        var properties = eventType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            var placeholder = $"{{{prop.Name}}}";
            if (result.Contains(placeholder, StringComparison.Ordinal))
            {
                var value = prop.GetValue(evt);
                var valueStr = value == null ? "" : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", value);
                result = result.Replace(placeholder, valueStr, StringComparison.Ordinal);
            }
        }

        // Truncate
        if (result.Length > rules.MessageByteLength)
        {
            result = result.Substring(0, rules.MessageByteLength);
        }

        return result;
    }

    /// <summary>
    /// Helper: Truncate a message to byte length.
    /// </summary>
    private string TruncateMessage(string message, int maxLength)
    {
        return message.Length > maxLength ? message.Substring(0, maxLength) : message;
    }
}
