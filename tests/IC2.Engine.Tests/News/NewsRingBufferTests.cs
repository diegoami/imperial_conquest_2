using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.News;
using Xunit;

namespace IC2.Engine.Tests.News;

/// <summary>
/// Tests the ring buffer behavior: DoD 1 — 41 appends leave exactly the 40 newest, in order,
/// oldest evicted.
/// </summary>
public class NewsRingBufferTests
{
    [Fact]
    public void RingBuffer_Evicts_Oldest_When_Full()
    {
        // Arrange: Create an empty news log and a ruleset with 40-slot capacity
        var newsLog = NewsLog.Empty;
        var rules = new NewsLogRules(RingBufferSlots: 40, MessageByteLength: 256);

        // Act: Append 41 entries
        for (var i = 0; i < 41; i++)
        {
            newsLog = newsLog.Append(new NewsEntry($"Entry {i}"), rules);
        }

        // Assert: The log contains exactly 40 entries, the oldest (Entry 0) is gone
        Assert.Equal(40, newsLog.Slots.Count);
        Assert.Equal(39, newsLog.MostRecentSlot);
        Assert.Equal("Entry 1", newsLog.Slots[0].Text);  // Entry 0 was evicted
        Assert.Equal("Entry 40", newsLog.Slots[39].Text); // Entry 40 is the newest
    }

    [Fact]
    public void RingBuffer_Maintains_Order()
    {
        // Arrange
        var newsLog = NewsLog.Empty;
        var rules = new NewsLogRules(RingBufferSlots: 40, MessageByteLength: 256);

        // Act: Append 50 entries
        for (var i = 0; i < 50; i++)
        {
            newsLog = newsLog.Append(new NewsEntry($"Message {i}"), rules);
        }

        // Assert: Entries are in ascending order, oldest is 10 (50 - 40)
        Assert.Equal(40, newsLog.Slots.Count);
        for (var i = 0; i < 40; i++)
        {
            Assert.Equal($"Message {i + 10}", newsLog.Slots[i].Text);
        }
    }

    [Fact]
    public void RingBuffer_MostRecentSlot_Correct_After_Eviction()
    {
        // Arrange
        var newsLog = NewsLog.Empty;
        var rules = new NewsLogRules(RingBufferSlots: 5, MessageByteLength: 256);

        // Act: Append entries exceeding capacity
        for (var i = 0; i < 12; i++)
        {
            newsLog = newsLog.Append(new NewsEntry($"Entry {i}"), rules);
        }

        // Assert: MostRecentSlot should always equal Count - 1
        Assert.True(newsLog.IsConsistent());
        Assert.Equal(newsLog.Slots.Count - 1, newsLog.MostRecentSlot);
    }

    [Fact]
    public void RingBuffer_Capacity_From_Rules()
    {
        // Arrange
        var rules = new NewsLogRules(RingBufferSlots: 10, MessageByteLength: 256);
        var newsLog = NewsLog.Empty;

        // Act: Fill beyond capacity
        for (var i = 0; i < 15; i++)
        {
            newsLog = newsLog.Append(new NewsEntry($"Entry {i}"), rules);
        }

        // Assert: Capacity comes from rules, not hardcoded
        Assert.Equal(10, newsLog.Slots.Count);
    }
}
