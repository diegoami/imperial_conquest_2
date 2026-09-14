using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.News;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.News;

/// <summary>
/// DoD 2: "Every message literal in the T04 corpus's news section is present in the catalog and renders
/// with its operands substituted (one test per literal, table-driven)."
/// </summary>
/// <remarks>
/// Every expected value below comes from <see cref="FixtureCorpus"/>, never a second hand-typed copy of
/// the corpus text — the first attempt's own catalog and its test both retyped the corpus's literals with
/// invented <c>{PascalCase}</c> names, and silently drifted from the actual confirmed text for eleven of
/// the seventeen (N1). Asserting directly against <c>FixtureCorpus.Get(id).AsString()</c> makes that class
/// of drift a single-source-of-truth check instead of a second hand transcription.
/// </remarks>
public class NewsMessageCatalogTests
{
    /// <summary>
    /// DoD 2, confirmed format strings: fourteen of the seventeen corpus entries are quoted (or, for
    /// <c>victory.all-cities</c>, static) source text, transcribed into the catalog verbatim. The catalog's
    /// stored template equals the corpus literal exactly, character for character.
    /// </summary>
    [Theory]
    [MemberData(nameof(VerbatimCorpusIds))]
    public void Catalog_Template_Equals_CorpusLiteral_Verbatim(string eventKind, string corpusId)
    {
        var expected = FixtureCorpus.Get(corpusId).AsString();

        Assert.Equal(expected, NewsMessageCatalog.GetTemplate(eventKind));
    }

    public static TheoryData<string, string> VerbatimCorpusIds => new()
    {
        { "city.falls-to", "newsMessage.fallsTo" },
        { "city.fails-to-capture", "newsMessage.failsToCapture" },
        { "battle.army-destroyed", "newsMessage.destroysArmy" },
        { "battle.fleet-sunk", "newsMessage.sinksFleet" },
        { "peace.honourable", "newsMessage.honourablePeace" },
        { "peace.sues-for", "newsMessage.suesForPeace" },
        { "peace.ends-trading-agreements", "newsMessage.endsTradingAgreements" },
        { "peace.ends-alliances", "newsMessage.endsAlliances" },
        { "peace.pays-reparations", "newsMessage.paysReparations" },
        { "peace.ally-agreement", "newsMessage.allyPeaceAgreement" },
        { "fleet.finished", "newsMessage.fleetFinished" },
        { "victory.all-cities", "newsMessage.victoryAllCities" },
        { "victory.conquered-by-nation", "newsMessage.conqueredByNation" },
        { "diplomacy.pending-offer", "newsMessage.pendingDiplomaticOfferParaphrase" },
    };

    /// <summary>
    /// DoD 2, the three <c>[designed]</c> entries generalised from a single observed example each
    /// (<see cref="NewsMessageCatalog"/>'s remarks): rendering the designed template — through the real,
    /// public <see cref="NewsLogWriter.Append"/> entry point — with that example's own operand values
    /// reproduces the corpus's literal exactly. Each probe event carries a namespace-unique kind (N10) and
    /// is resolved through an injected template resolver rather than a production catalog entry, so this
    /// never depends on — and can never collide with — any corpus- or production-shaped kind.
    /// </summary>
    [Fact]
    public void CityDefectsTo_RenderedWithObservedOperands_ReproducesCorpusLiteral()
    {
        var expected = FixtureCorpus.Get("newsMessage.defectsFromObservedExample").AsString();
        var template = NewsMessageCatalog.GetTemplate("city.defects-to");
        var probeEvent = new DefectsToProbeEvent("Synnada", "Galatia", "Seleucid");

        var rendered = RenderOnly(probeEvent, template);

        Assert.Equal(expected, rendered);
    }

    [Fact]
    public void NationConquered_RenderedWithObservedOperands_ReproducesCorpusLiteral()
    {
        var expected = FixtureCorpus.Get("newsMessage.conquersNationObservedExample").AsString();
        var template = NewsMessageCatalog.GetTemplate("nation.conquered");
        var probeEvent = new ConquersProbeEvent("Seleucid", "Galatia");

        var rendered = RenderOnly(probeEvent, template);

        Assert.Equal(expected, rendered);
    }

    [Fact]
    public void FleetLostAtSea_RenderedWithObservedOperand_ReproducesCorpusLiteral()
    {
        var expected = FixtureCorpus.Get("newsMessage.fleetLostAtSeaObservedExample").AsString();
        var template = NewsMessageCatalog.GetTemplate("fleet.lost-at-sea");
        var probeEvent = new FleetLostProbeEvent("Carthage");

        var rendered = RenderOnly(probeEvent, template);

        Assert.Equal(expected, rendered);
    }

    /// <summary>
    /// Renders exactly one event through the real, public <see cref="NewsLogWriter.Append"/>, with a
    /// generously sized <c>MessageByteLength</c> so nothing here is ever truncated, and reads back the one
    /// slot it produced.
    /// </summary>
    private static string RenderOnly(DomainEvent probeEvent, string template)
    {
        var rules = new NewsLogRules(RingBufferSlots: 1, MessageByteLength: 4096);
        var result = NewsLogWriter.Append(CoreTestbed.InitialState(), new[] { probeEvent }, rules, _ => template);

        return Assert.Single(result.NewsLog.Slots).Text;
    }

    /// <summary>The catalog carries exactly the corpus's seventeen <c>newsMessage.*</c> entries — no more, no fewer.</summary>
    [Fact]
    public void Catalog_HasExactlyTheCorpusNewsMessageCount()
    {
        var corpusCount = FixtureCorpus.All.Count(e => e.Id.StartsWith("newsMessage.", StringComparison.Ordinal));

        Assert.Equal(corpusCount, NewsMessageCatalog.RegisteredKinds.Count());
    }

    /// <summary>The catalog rejects unknown event kinds.</summary>
    [Fact]
    public void Catalog_Throws_For_UnknownKind()
    {
        var ex = Assert.Throws<KeyNotFoundException>(() => NewsMessageCatalog.GetTemplate("unknown.event.kind"));
        Assert.Contains("unknown.event.kind", ex.Message);
    }

    /// <summary>The catalog's kind list is deterministic (sorted).</summary>
    [Fact]
    public void RegisteredKinds_AreSorted()
    {
        var kinds = NewsMessageCatalog.RegisteredKinds.ToList();
        Assert.Equal(kinds.OrderBy(k => k, StringComparer.Ordinal).ToList(), kinds);
    }
}

// --- Probe events for the three [designed] catalog entries. Namespace-unique kinds (N10): never resolved
// through the production catalog (RenderOnly always injects the template directly), so these can never
// collide with a later task's own test fixture or production event.

[DomainEvent("test.news-log.catalog-probe.defects-to", NewsWorthy = true)]
public sealed record DefectsToProbeEvent(string CityName, string OldOwner, string NewOwner) : DomainEvent;

[DomainEvent("test.news-log.catalog-probe.conquers", NewsWorthy = true)]
public sealed record ConquersProbeEvent(string ConqueringNation, string ConqueredNation) : DomainEvent;

[DomainEvent("test.news-log.catalog-probe.fleet-lost", NewsWorthy = true)]
public sealed record FleetLostProbeEvent(string Nation) : DomainEvent;
