using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.News;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.News;

/// <summary>
/// Tests the ring buffer behavior: DoD 1 — 41 appends leave exactly the 40 newest, in order,
/// oldest evicted.
/// </summary>
/// <remarks>
/// Tests <see cref="NewsLog.Append"/> directly (T02's own model, not T10's writer) — the ring buffer's own
/// data-structure correctness, in isolation. DoD 4's separate, stronger requirement — that the eviction
/// also holds end to end, through the real writer and a real <c>TurnCoordinator</c> — is
/// <c>NewsLogWriterTests.FortyOneTurns_LeaveExactlyCapacity_OldestEvicted</c>, not this file.
/// </remarks>
public class NewsRingBufferTests
{
    private static readonly int Capacity = FixtureCorpus.Get("caps.maxNewsSlots").AsInt();

    [Fact]
    public void RingBuffer_Evicts_Oldest_When_Full()
    {
        // Arrange: Create an empty news log and a ruleset at the corpus-confirmed capacity.
        var newsLog = NewsLog.Empty;
        var rules = new NewsLogRules(RingBufferSlots: Capacity, MessageByteLength: 256);

        // Act: Append one more than capacity.
        for (var i = 0; i < Capacity + 1; i++)
        {
            newsLog = newsLog.Append(new NewsEntry($"Entry {i}"), rules);
        }

        // Assert: The log contains exactly `Capacity` entries, the oldest (Entry 0) is gone.
        Assert.Equal(Capacity, newsLog.Slots.Count);
        Assert.Equal(Capacity - 1, newsLog.MostRecentSlot);
        Assert.Equal("Entry 1", newsLog.Slots[0].Text);  // Entry 0 was evicted
        Assert.Equal($"Entry {Capacity}", newsLog.Slots[Capacity - 1].Text); // the newest survives
    }

    [Fact]
    public void RingBuffer_Maintains_Order()
    {
        // Arrange
        var newsLog = NewsLog.Empty;
        var rules = new NewsLogRules(RingBufferSlots: Capacity, MessageByteLength: 256);
        var total = Capacity + 10;

        // Act: Append more than capacity.
        for (var i = 0; i < total; i++)
        {
            newsLog = newsLog.Append(new NewsEntry($"Message {i}"), rules);
        }

        // Assert: Entries are in ascending order, oldest is (total - Capacity).
        Assert.Equal(Capacity, newsLog.Slots.Count);
        for (var i = 0; i < Capacity; i++)
        {
            Assert.Equal($"Message {i + (total - Capacity)}", newsLog.Slots[i].Text);
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
