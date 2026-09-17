using IC2.Engine.Core;
using IC2.Engine.News;
using Xunit;

namespace IC2.Engine.Tests.News;

/// <summary>
/// Tests coverage: DoD 6 — a coverage test asserts every domain event kind returned by
/// T03's DomainEventCatalog.Discover that is marked news-worthy has a catalog entry — so a later
/// task adding an event without a message fails CI — and that every placeholder in that entry's
/// template resolves to one of the event's own properties (#91 N17/N28), before T14, T16 or T17
/// declare the first production news events.
/// </summary>
public class NewsCoverageTests
{
    /// <summary>
    /// DoD 6, first half: every news-worthy event kind has a message catalog entry.
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
    /// DoD 6, second half (#91 N17/N28): every news-worthy event kind's template placeholders all
    /// resolve to one of that event type's own properties — the same lookup
    /// <see cref="NewsLogWriter.RenderMessage"/> uses at rendering time
    /// (<see cref="NewsLogWriter.UnresolvedPlaceholders"/>), checked here for every declared event
    /// instead of only when a production system happens to publish one. Before this existed, a
    /// template/property mismatch (a typo, a renamed property the catalog entry was not updated for)
    /// aborted <c>RunTurn</c> at runtime instead of failing CI.
    /// </summary>
    [Fact]
    public void AllNewsworthyEvents_HaveAPropertyForEveryTemplatePlaceholder()
    {
        var assembly = typeof(DomainEvent).Assembly;
        var eventDescriptors = DomainEventCatalog.Discover([assembly]);

        var failures = new List<string>();
        foreach (var descriptor in eventDescriptors)
        {
            if (!descriptor.NewsWorthy || !NewsMessageCatalog.HasTemplate(descriptor.Kind))
            {
                // A missing catalog entry is AllNewsworthyEvents_HaveCatalogEntries's finding, not this
                // one's -- this test only checks placeholder/property agreement for entries that exist.
                continue;
            }

            var template = NewsMessageCatalog.GetTemplate(descriptor.Kind);
            var unresolved = NewsLogWriter.UnresolvedPlaceholders(descriptor.EventType, template);
            if (unresolved.Count > 0)
            {
                failures.Add($"{descriptor.Kind} ({descriptor.EventType.Name}): {string.Join(", ", unresolved)}");
            }
        }

        if (failures.Count > 0)
        {
            throw new InvalidOperationException(
                "The following news-worthy events have a template placeholder that matches no property "
                + "on the event itself:\n" + string.Join("\n", failures)
                + "\n\nAdd the missing property to the event, or correct the template in NewsMessageCatalog.");
        }
    }

    /// <summary>
    /// #91 N17/N28's own regression case: proves
    /// <see cref="AllNewsworthyEvents_HaveAPropertyForEveryTemplatePlaceholder"/>'s check actually fails
    /// on a genuine mismatch, using <see cref="NewsLogWriter.UnresolvedPlaceholders"/> directly against a
    /// template/type pair that does not agree (this file's own probe events all agree with their
    /// templates, so nothing in this assembly's real coverage exercises the failure path otherwise).
    /// </summary>
    [Fact]
    public void UnresolvedPlaceholders_Detects_AGenuineMismatch()
    {
        var unresolved = NewsLogWriter.UnresolvedPlaceholders(
            typeof(NewsCoverageMismatchProbeEvent), "{CityName} did {NotADeclaredProperty}.");

        Assert.Equal(new[] { "{NotADeclaredProperty}" }, unresolved);
    }

    /// <summary>The companion case: a template that DOES agree with its type reports nothing missing.</summary>
    [Fact]
    public void UnresolvedPlaceholders_ReportsNothing_WhenTheTemplateAgrees()
    {
        var unresolved = NewsLogWriter.UnresolvedPlaceholders(
            typeof(NewsCoverageMismatchProbeEvent), "{CityName} did something.");

        Assert.Empty(unresolved);
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

/// <summary>
/// A plain property holder for <see cref="NewsCoverageTests.UnresolvedPlaceholders_Detects_AGenuineMismatch"/>
/// and its companion -- not a real <see cref="DomainEvent"/>, and never registered under any kind, since
/// <see cref="NewsLogWriter.UnresolvedPlaceholders"/> only needs a <see cref="Type"/> to reflect over.
/// </summary>
public sealed record NewsCoverageMismatchProbeEvent(string CityName);
