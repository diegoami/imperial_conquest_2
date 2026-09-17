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
    /// DoD 2, template presence: twelve of the seventeen corpus entries are quoted (or, for
    /// <c>victory.all-cities</c>, static) source text, transcribed into the catalog verbatim. The catalog's
    /// stored template equals the corpus literal exactly, character for character.
    /// </summary>
    /// <remarks>
    /// <c>peace.pays-reparations</c> and <c>diplomacy.pending-offer</c> are not in this table — their
    /// corpus source has no placeholder delimiter at all (a bare "N" / "X"/"Y"), so this catalog's designed
    /// templates differ from the corpus text by exactly one delimiter each; see
    /// <see cref="PeacePaysReparations_DesignedDelimiter_MapsBackToCorpusLiteral"/> and
    /// <see cref="DiplomacyPendingOffer_DesignedDelimiters_MapBackToCorpusLiteral"/>.
    /// </remarks>
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
        { "peace.ally-agreement", "newsMessage.allyPeaceAgreement" },
        { "fleet.finished", "newsMessage.fleetFinished" },
        { "victory.all-cities", "newsMessage.victoryAllCities" },
        { "victory.conquered-by-nation", "newsMessage.conqueredByNation" },
    };

    /// <summary>
    /// B2(i): the two designed, delimiter-added templates differ from their corpus source by exactly one
    /// added delimiter each — mapping the delimiter back to its bare letter reproduces the corpus literal
    /// exactly. See <see cref="NewsMessageCatalog"/>'s remarks for why the delimiter was added and what was
    /// searched for an alternative.
    /// </summary>
    [Fact]
    public void PeacePaysReparations_DesignedDelimiter_MapsBackToCorpusLiteral()
    {
        var corpusLiteral = FixtureCorpus.Get("newsMessage.paysReparations").AsString();
        var designed = NewsMessageCatalog.GetTemplate("peace.pays-reparations");

        Assert.Equal(corpusLiteral, designed.Replace("<n>", "N", StringComparison.Ordinal));
    }

    [Fact]
    public void DiplomacyPendingOffer_DesignedDelimiters_MapBackToCorpusLiteral()
    {
        var corpusLiteral = FixtureCorpus.Get("newsMessage.pendingDiplomaticOfferParaphrase").AsString();
        var designed = NewsMessageCatalog.GetTemplate("diplomacy.pending-offer");

        var mappedBack = designed
            .Replace("<x>", "X", StringComparison.Ordinal)
            .Replace("<y>", "Y", StringComparison.Ordinal);

        Assert.Equal(corpusLiteral, mappedBack);
    }

    /// <summary>
    /// DoD 2, B1: every one of the seventeen catalog entries — fetched from the real production
    /// <see cref="NewsMessageCatalog"/>, never a copy — renders with its operands substituted, and no
    /// placeholder delimiter (<c>{</c>/<c>}</c>/<c>&lt;</c>/<c>&gt;</c>) survives into the output. Each
    /// probe event carries a namespace-unique kind (N10); the injected resolver ignores it and always
    /// returns <see cref="NewsMessageCatalog.GetTemplate"/>'s own value for the real kind under test, so
    /// this exercises the production catalog directly without needing the probe's own kind to match it.
    /// Three rows (<c>city.defects-to</c>, <c>nation.conquered</c>, <c>fleet.lost-at-sea</c>) use the exact
    /// operand values from the corpus's own observed example, so their expected text is read from
    /// <see cref="FixtureCorpus"/> rather than hand-typed a second time.
    /// </summary>
    [Theory]
    [MemberData(nameof(RenderCases))]
    public void Catalog_Renders_EachLiteral_WithOperandsSubstituted(
        string eventKind, DomainEvent probeEvent, string expectedRendered)
    {
        var rendered = RenderThroughProductionCatalog(eventKind, probeEvent);

        Assert.Equal(expectedRendered, rendered);
        Assert.DoesNotMatch(@"[{}<>]", rendered);
    }

    public static TheoryData<string, DomainEvent, string> RenderCases => new()
    {
        {
            "city.falls-to",
            new RenderProbeEvent(CityName: "Corinth", OldOwner: "Sparta", NewOwner: "Athens"),
            "Corinth   (Sparta)  falls to Athens."
        },
        {
            "city.fails-to-capture",
            new RenderProbeEvent(AttackerNation: "Rome", CityName: "Carthage", DefenderNation: "Numidia"),
            "Rome fails to capture Carthage   (Numidia)."
        },
        {
            "battle.army-destroyed",
            new RenderProbeEvent(Winner: "Rome", Loser: "Carthage"),
            "Rome destroys army of Carthage."
        },
        {
            "battle.fleet-sunk",
            new RenderProbeEvent(Winner: "Rome", Loser: "Carthage"),
            "Rome sinks fleet of Carthage."
        },
        {
            "peace.honourable",
            new RenderProbeEvent(Winner: "Rome", Loser: "Carthage"),
            "Rome and Carthage have agreed to end their war."
        },
        {
            "peace.sues-for",
            new RenderProbeEvent(Winner: "Rome", Loser: "Carthage"),
            "Carthage sues Rome for peace and;"
        },
        {
            "peace.ends-trading-agreements",
            new RenderProbeEvent(Loser: "Carthage"),
            "    Carthage ends all current trading agreements."
        },
        {
            "peace.ends-alliances",
            new RenderProbeEvent(Loser: "Carthage"),
            "    Carthage  ends all current alliances."
        },
        {
            // B2(ii): a real reparations amount, in the format the original prints it in
            // (saves-processed/1_rome_270_autumn_11.sav's news log: "... pays reparations of 2,269 talents.").
            "peace.pays-reparations",
            new RenderProbeEvent(Loser: "Carthage", N: "2,269"),
            "    Carthage pays reparations of 2,269 talents."
        },
        {
            "peace.ally-agreement",
            new RenderProbeEvent(A: "Rome", B: "Carthage"),
            "Rome and Carthage have agreed to end their war."
        },
        {
            "fleet.finished",
            new RenderProbeEvent(Nation: "Rome", CityName: "Ostia"),
            "Rome finishes a new fleet at Ostia"
        },
        {
            "victory.all-cities",
            new RenderProbeEvent(),
            "You have conquerred the Mediterranean, a unique achievement."
        },
        {
            "victory.conquered-by-nation",
            new RenderProbeEvent(Nation: "Carthage"),
            "Your nation has been conquerred by Carthage."
        },
        {
            "diplomacy.pending-offer",
            new RenderProbeEvent(X: "Rome", Y: "Carthage"),
            "Rome wants to trade/form an alliance with Carthage"
        },
        {
            // Matches newsMessage.defectsFromObservedExample's own operand values.
            "city.defects-to",
            new RenderProbeEvent(CityName: "Synnada", OldOwner: "Galatia", NewOwner: "Seleucid"),
            ObservedExampleText("newsMessage.defectsFromObservedExample")
        },
        {
            // Matches newsMessage.conquersNationObservedExample's own operand values.
            "nation.conquered",
            new RenderProbeEvent(ConqueringNation: "Seleucid", ConqueredNation: "Galatia"),
            ObservedExampleText("newsMessage.conquersNationObservedExample")
        },
        {
            // Matches newsMessage.fleetLostAtSeaObservedExample's own operand value.
            "fleet.lost-at-sea",
            new RenderProbeEvent(Nation: "Carthage"),
            ObservedExampleText("newsMessage.fleetLostAtSeaObservedExample")
        },
    };

    /// <summary>Reads an observed-example literal straight from the corpus, for a <see cref="RenderCases"/> row.</summary>
    private static string ObservedExampleText(string corpusId) => FixtureCorpus.Get(corpusId).AsString();

    /// <summary>
    /// Renders exactly one event through the real, public <see cref="NewsLogWriter.Append"/>, resolving the
    /// template through the real production <see cref="NewsMessageCatalog"/> for <paramref name="eventKind"/>
    /// — never the probe event's own (namespace-unique, N10) kind — and reads back the one slot it produced.
    /// </summary>
    private static string RenderThroughProductionCatalog(string eventKind, DomainEvent probeEvent)
    {
        var rules = new NewsLogRules(RingBufferSlots: 1, MessageByteLength: 4096, SeasonNames: ValueList<string>.Empty);
        var result = NewsLogWriter.Append(
            CoreTestbed.InitialState(),
            new[] { probeEvent },
            rules,
            _ => NewsMessageCatalog.GetTemplate(eventKind));

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

/// <summary>
/// A single flexible probe event covering every operand name any of the seventeen catalog templates uses.
/// Namespace-unique kind (N10) — never resolved through the production catalog, since
/// <see cref="NewsMessageCatalogTests.RenderThroughProductionCatalog"/> always injects the real template
/// for the specific kind under test, ignoring this event's own <see cref="DomainEvent.Kind"/> entirely.
/// </summary>
[DomainEvent("test.news-log.catalog-probe.render-all", NewsWorthy = true)]
public sealed record RenderProbeEvent(
    string? CityName = null,
    string? OldOwner = null,
    string? NewOwner = null,
    string? AttackerNation = null,
    string? DefenderNation = null,
    string? Winner = null,
    string? Loser = null,
    string? A = null,
    string? B = null,
    string? Nation = null,
    string? N = null,
    string? ConqueringNation = null,
    string? ConqueredNation = null,
    string? X = null,
    string? Y = null) : DomainEvent;
