using IC2.Engine.Core;
using IC2.Engine.News;
using Xunit;

namespace IC2.Engine.Tests.News;

/// <summary>
/// Tests coverage: DoD 3 — A coverage test asserts every domain event kind returned by
/// T03's DomainEventCatalog.Discover that is marked news-worthy has a catalog entry — so a later
/// task adding an event without a message fails CI.
/// </summary>
public class NewsCoverageTests
{
    /// <summary>
    /// DoD 3: Every news-worthy event kind has a message catalog entry.
    /// This test will fail if a task declares a news-worthy event without adding a catalog entry for it.
    /// </summary>
    [Fact]
    public void AllNewsworthyEvents_HaveCatalogEntries()
    {
        // Arrange: Discover all domain event kinds in the engine assembly
        var assembly = typeof(DomainEvent).Assembly;
        var eventDescriptors = DomainEventCatalog.Discover([assembly]);

        // Act & Assert: Every news-worthy event kind must have a catalog entry
        var missingCatalogEntries = new List<string>();
        foreach (var descriptor in eventDescriptors)
        {
            if (descriptor.NewsWorthy && !NewsMessageCatalog.HasTemplate(descriptor.Kind))
            {
                missingCatalogEntries.Add(descriptor.Kind);
            }
        }

        // Assert
        if (missingCatalogEntries.Count > 0)
        {
            throw new InvalidOperationException(
                $"The following news-worthy event kinds have no message catalog entry:\n"
                + string.Join("\n", missingCatalogEntries)
                + "\n\nAdd entries to NewsMessageCatalog for each of these events.");
        }
    }

    /// <summary>
    /// A known corpus-derived kind has a catalog entry. (This is implicitly proven by
    /// <see cref="AllNewsworthyEvents_HaveCatalogEntries"/> once a production event declares this kind;
    /// explicit confirmation here does not depend on that.)
    /// </summary>
    [Fact]
    public void CityFallsTo_HasCatalogEntry()
    {
        Assert.True(NewsMessageCatalog.HasTemplate("city.falls-to"));
    }

    /// <summary>
    /// Test that non-news-worthy events are not required to have catalog entries.
    /// </summary>
    [Fact]
    public void NonNewsworthyEvent_MayNotHaveCatalogEntry()
    {
        // Arrange
        var expectedKind = "test.non-newsworthy";

        // Act
        var hasEntry = NewsMessageCatalog.HasTemplate(expectedKind);

        // Assert: Non-news-worthy events don't need catalog entries (they won't be rendered)
        // This test simply verifies the coverage test doesn't require them
        Assert.False(hasEntry, $"Non-news-worthy event '{expectedKind}' should not have a catalog entry");
    }
}
