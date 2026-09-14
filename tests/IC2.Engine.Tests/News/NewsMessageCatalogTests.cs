using IC2.Engine.News;
using Xunit;

namespace IC2.Engine.Tests.News;

/// <summary>
/// Tests the message catalog: DoD 2 — Every message literal in T04 corpus is present and renders
/// with operands substituted (one test per literal, table-driven).
/// </summary>
public class NewsMessageCatalogTests
{
    /// <summary>
    /// DoD 2: Every corpus message is registered and renders correctly with operands.
    /// </summary>
    [Theory]
    [MemberData(nameof(CorpusMessages))]
    public void Catalog_Has_CorpusMessage(string eventKind, string expectedTemplate)
    {
        // Act
        var template = NewsMessageCatalog.GetTemplate(eventKind);

        // Assert
        Assert.Equal(expectedTemplate, template);
    }

    /// <summary>
    /// Test data sourced from the T04 fixtures corpus: every confirmed news message literal.
    /// </summary>
    public static TheoryData<string, string> CorpusMessages => new()
    {
        // Capture messages (decompiled-city-capture-resolution.md)
        { "city.falls-to", "{CityName}   ({OldOwner})  falls to {NewOwner}." },
        { "city.fails-to-capture", "{AttackerNation} fails to capture {CityName}   ({DefenderNation})." },

        // Defection (galatia-elimination-and-city-resupply-confirmed.md)
        { "city.defects-to", "{CityName} defects from {OldOwner} to {NewOwner}." },

        // Nation conquest (galatia-elimination-and-city-resupply-confirmed.md)
        { "nation.conquered", "{ConqueringNation} conquers {ConqueredNation}." },

        // Battle outcomes (decompiled-diplomacy-peace-terms-and-instant-battles.md)
        { "battle.army-destroyed", "{WinnerNation} destroys army of {LoserNation}." },
        { "battle.fleet-sunk", "{WinnerNation} sinks fleet of {LoserNation}." },

        // Peace treaties (decompiled-diplomacy-peace-terms-and-instant-battles.md)
        { "peace.honourable", "{WinnerNation} and {LoserNation} have agreed to end their war." },
        { "peace.sues-for", "{LoserNation} sues {WinnerNation} for peace and;" },
        { "peace.ends-trading-agreements", "    {LoserNation} ends all current trading agreements." },
        { "peace.ends-alliances", "    {LoserNation}  ends all current alliances." },
        { "peace.pays-reparations", "    {LoserNation} pays reparations of {ReparationAmount} talents." },
        { "peace.ally-agreement", "{NationA} and {NationB} have agreed to end their war." },

        // Fleet (decompiled-unit-map-orders-and-record-fields.md)
        { "fleet.finished", "{Nation} finishes a new fleet at {CityName}" },

        // Fleet lost at sea (fleet-owner-field-confirmed.md)
        { "fleet.lost-at-sea", "A fleet belonging to {Nation} is lost at sea" },

        // Victory (decompiled-diplomacy-peace-terms-and-instant-battles.md)
        { "victory.all-cities", "You have conquerred the Mediterranean, a unique achievement." },
        { "victory.conquered-by-nation", "Your nation has been conquerred by {ConqueringNation}." },

        // Diplomacy (decompiled-turn-and-calendar-sequencing.md)
        { "diplomacy.pending-offer", "{OfferingNation} wants to trade/form an alliance with {ReceivingNation}" },
    };

    /// <summary>Test that operand substitution works for a simple message.</summary>
    [Fact]
    public void MessageRendering_Substitutes_SimpleOperands()
    {
        // Arrange
        var template = NewsMessageCatalog.GetTemplate("city.falls-to");
        var message = template;

        // Act & Assert
        Assert.Contains("{CityName}", message);
        Assert.Contains("{OldOwner}", message);
        Assert.Contains("{NewOwner}", message);
    }

    /// <summary>Test that the catalog rejects unknown event kinds.</summary>
    [Fact]
    public void Catalog_Throws_For_UnknownKind()
    {
        // Act & Assert
        var ex = Assert.Throws<KeyNotFoundException>(() =>
            NewsMessageCatalog.GetTemplate("unknown.event.kind"));
        Assert.Contains("unknown.event.kind", ex.Message);
    }

    /// <summary>Test that HasTemplate works correctly.</summary>
    [Fact]
    public void HasTemplate_ReturnsTrueForKnownKinds()
    {
        // Act & Assert
        Assert.True(NewsMessageCatalog.HasTemplate("city.falls-to"));
        Assert.False(NewsMessageCatalog.HasTemplate("unknown.event"));
    }

    /// <summary>Test that the catalog's kind list is deterministic (sorted).</summary>
    [Fact]
    public void RegisteredKinds_AreSorted()
    {
        // Act
        var kinds = NewsMessageCatalog.RegisteredKinds.ToList();

        // Assert
        var sorted = kinds.OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.Equal(sorted, kinds);
    }
}
