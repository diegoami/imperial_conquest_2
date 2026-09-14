using System.Text.Json;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.News;

/// <summary>
/// Tests round-trip serialization: DoD 5 — The rendered log survives a GameState round-trip
/// through T02's serialization — so T20's save/load inherits a news log that is actually populated.
/// </summary>
public class NewsRoundTripTests
{
    /// <summary>
    /// DoD 5: A populated news log survives round-trip serialization (JSON -> NewsLog -> JSON).
    /// </summary>
    [Fact]
    public void NewsLog_SurvivesRoundTrip()
    {
        // Arrange: Create a news log with several entries
        var entries = new List<NewsEntry>
        {
            new("Rome   (Republic)  falls to Empire."),
            new("Caesar conquers Gauls."),
            new("Octavian and Marcus Antonius have agreed to end their war."),
        };
        var newsLog = new NewsLog(MostRecentSlot: 2, Slots: ValueList.From(entries));

        // Verify the log is consistent
        Assert.True(newsLog.IsConsistent());

        // Act: Serialize and deserialize the news log
        var json = GameJson.Serialize(newsLog);
        var deserialized = JsonSerializer.Deserialize<NewsLog>(json, GameJson.Options);

        // Assert: The news log survived round-trip intact
        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized.Slots.Count);
        Assert.Equal(2, deserialized.MostRecentSlot);
        Assert.Equal("Rome   (Republic)  falls to Empire.", deserialized.Slots[0].Text);
        Assert.Equal("Caesar conquers Gauls.", deserialized.Slots[1].Text);
        Assert.Equal("Octavian and Marcus Antonius have agreed to end their war.", deserialized.Slots[2].Text);
    }

    /// <summary>
    /// Test that an empty news log round-trips correctly.
    /// </summary>
    [Fact]
    public void EmptyNewsLog_RoundTrips()
    {
        // Arrange
        var newsLog = NewsLog.Empty;

        // Act
        var json = GameJson.Serialize(newsLog);
        var deserialized = JsonSerializer.Deserialize<NewsLog>(json, GameJson.Options);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.Slots);
        Assert.Equal(-1, deserialized.MostRecentSlot);
        Assert.True(deserialized.IsConsistent());
    }

    /// <summary>
    /// Test that a full 40-slot buffer round-trips correctly.
    /// </summary>
    [Fact]
    public void FullNewsLog_RoundTrips()
    {
        // Arrange: Create a full 40-slot buffer
        var entries = Enumerable.Range(0, 40)
            .Select(i => new NewsEntry($"Message {i}"))
            .ToList();
        var newsLog = new NewsLog(MostRecentSlot: 39, Slots: ValueList.From(entries));

        // Act
        var json = GameJson.Serialize(newsLog);
        var deserialized = JsonSerializer.Deserialize<NewsLog>(json, GameJson.Options);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(40, deserialized.Slots.Count);
        Assert.Equal(39, deserialized.MostRecentSlot);
        for (var i = 0; i < 40; i++)
        {
            Assert.Equal($"Message {i}", deserialized.Slots[i].Text);
        }
    }

    /// <summary>
    /// Test that news log round-trips maintain message text exactly (including whitespace).
    /// </summary>
    [Fact]
    public void NewsLog_PreservesExactText()
    {
        // Arrange: Messages with exact whitespace from the corpus
        var entries = new List<NewsEntry>
        {
            new("    Loser ends all current trading agreements."),  // Leading spaces
            new("    Loser  ends all current alliances."),           // Double space
            new("Rome   (Republic)  falls to Empire."),              // Irregular spacing
        };
        var newsLog = new NewsLog(MostRecentSlot: 2, Slots: ValueList.From(entries));

        // Act
        var json = GameJson.Serialize(newsLog);
        var deserialized = JsonSerializer.Deserialize<NewsLog>(json, GameJson.Options);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal("    Loser ends all current trading agreements.", deserialized.Slots[0].Text);
        Assert.Equal("    Loser  ends all current alliances.", deserialized.Slots[1].Text);
        Assert.Equal("Rome   (Republic)  falls to Empire.", deserialized.Slots[2].Text);
    }
}
