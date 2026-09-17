using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.News;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.News;

/// <summary>
/// DoD 2 and DoD 5: every message literal in the T04 corpus's news section is present in the catalog
/// and renders with its operands substituted (one test per literal, table-driven), including T42's
/// corrections and additions from <c>news-log-format-and-messages.md</c>.
/// </summary>
/// <remarks>
/// Every expected value below comes from <see cref="FixtureCorpus"/>, never a second hand-typed copy of
/// the corpus text — the first attempt's own catalog and its test both retyped the corpus's literals with
/// invented <c>{PascalCase}</c> names, and silently drifted from the actual confirmed text for eleven of
/// the seventeen it then held (N1). Asserting directly against <c>FixtureCorpus.Get(id).AsString()</c>
/// makes that class of drift a single-source-of-truth check instead of a second hand transcription.
/// </remarks>
public class NewsMessageCatalogTests
{
    /// <summary>
    /// DoD 2/5, template presence: corpus entries whose value has no bare, undelimited operand are
    /// quoted (or, for the two structural literals with no placeholder at all, literal) source text,
    /// transcribed into the catalog verbatim. The catalog's stored template equals the corpus literal
    /// exactly, character for character.
    /// </summary>
    /// <remarks>
    /// Entries whose corpus source has a bare, undelimited operand (a plain "N", "A"/"B", "C", "L") are
    /// not in this table — this catalog's designed templates differ from the corpus text by a delimiter
    /// (or, for three of them, a full word) each; see <see cref="DesignedMapsBackToCorpusLiteral"/>.
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
        { NewsMessageCatalog.DashLineKind, "newsMessage.dashLine" },
        { NewsMessageCatalog.BlankLineKind, "newsMessage.blankLine" },
        { NewsMessageCatalog.WeekHeaderKind, "newsMessage.weekHeader" },
    };

    /// <summary>
    /// B2(i) and T42's own additions: every designed, delimiter-or-word-added template differs from its
    /// corpus source by exactly the documented substitution — mapping it back reproduces the corpus
    /// literal exactly. See <see cref="NewsMessageCatalog"/>'s remarks for why each was added and what
    /// was searched for an alternative.
    /// </summary>
    [Theory]
    [MemberData(nameof(DesignedMappings))]
    public void DesignedMapsBackToCorpusLiteral(string eventKind, string corpusId, string designedToken, string bareText)
    {
        var corpusLiteral = FixtureCorpus.Get(corpusId).AsString();
        var designed = NewsMessageCatalog.GetTemplate(eventKind);

        Assert.Equal(corpusLiteral, designed.Replace(designedToken, bareText, StringComparison.Ordinal));
    }

    public static TheoryData<string, string, string, string> DesignedMappings => new()
    {
        { "peace.pays-reparations", "newsMessage.paysReparations", "<n>", "N" },
    };

    /// <summary>
    /// <c>war.declared</c> and <c>alliance.formed</c> each have two designed tokens (<c>&lt;A&gt;</c>
    /// and <c>&lt;B&gt;</c>); <see cref="DesignedMappings"/> only maps back the first, so this covers the
    /// second for both in one assertion each.
    /// </summary>
    [Theory]
    [InlineData("alliance.formed", "newsMessage.formsAlliance")]
    [InlineData("war.declared", "newsMessage.declaresWar")]
    public void DesignedMapsBackToCorpusLiteral_BothTokens(string eventKind, string corpusId)
    {
        var corpusLiteral = FixtureCorpus.Get(corpusId).AsString();
        var designed = NewsMessageCatalog.GetTemplate(eventKind)
            .Replace("<A>", "A", StringComparison.Ordinal)
            .Replace("<B>", "B", StringComparison.Ordinal);

        Assert.Equal(corpusLiteral, designed);
    }

    /// <summary>
    /// <c>nation.capital-moved</c>, <c>nation.leader-deposed</c> and <c>fleet.damaged-in-storm</c> map a
    /// bare corpus letter to a full descriptive word (not merely a delimiter around the same letter) —
    /// mapping each word back to its bare source letter still reproduces the corpus literal exactly.
    /// </summary>
    [Fact]
    public void MovedCapital_DesignedWordMapsBackToCorpusLiteral()
    {
        var corpusLiteral = FixtureCorpus.Get("newsMessage.movedCapital").AsString();
        var designed = NewsMessageCatalog.GetTemplate("nation.capital-moved")
            .Replace("<nation>", "N", StringComparison.Ordinal)
            .Replace("<cityName>", "C", StringComparison.Ordinal);

        Assert.Equal(corpusLiteral, designed);
    }

    [Fact]
    public void DeposesLeader_DesignedWordMapsBackToCorpusLiteral()
    {
        var corpusLiteral = FixtureCorpus.Get("newsMessage.deposesLeader").AsString();
        var designed = NewsMessageCatalog.GetTemplate("nation.leader-deposed")
            .Replace("<nation>", "N", StringComparison.Ordinal)
            .Replace("<leaderName>", "L", StringComparison.Ordinal);

        Assert.Equal(corpusLiteral, designed);
    }

    [Fact]
    public void FleetDamagedInStorm_DesignedWordMapsBackToCorpusLiteral()
    {
        var corpusLiteral = FixtureCorpus.Get("newsMessage.fleetDamagedInStorm").AsString();
        var designed = NewsMessageCatalog.GetTemplate("fleet.damaged-in-storm")
            .Replace("{Nation}", "N", StringComparison.Ordinal);

        Assert.Equal(corpusLiteral, designed);
    }

    /// <summary>
    /// DoD 5, B1: every production catalog entry that renders through a real
    /// <see cref="Core.DomainEvent"/> — fetched from the real production <see cref="NewsMessageCatalog"/>,
    /// never a copy — renders with its operands substituted, and no placeholder delimiter
    /// (<c>{</c>/<c>}</c>/<c>&lt;</c>/<c>&gt;</c>) survives into the output. Each probe event carries a
    /// namespace-unique kind (N10); the injected resolver ignores it and always returns
    /// <see cref="NewsMessageCatalog.GetTemplate"/>'s own value for the real kind under test, so this
    /// exercises the production catalog directly without needing the probe's own kind to match it.
    /// Some rows use the exact operand values from the corpus's own observed example, so their expected
    /// text is read from <see cref="FixtureCorpus"/> rather than hand-typed a second time. The three
    /// structural entries (<see cref="NewsMessageCatalog.DashLineKind"/>,
    /// <see cref="NewsMessageCatalog.BlankLineKind"/>, <see cref="NewsMessageCatalog.WeekHeaderKind"/>)
    /// have no backing event and are covered separately, by
    /// <see cref="StructuralEntries_RenderAsTheirOwnLiteral"/> and
    /// <see cref="RenderWeekHeader_SubstitutesTheThreeTokens"/>.
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
            // (saves-processed/1_rome_270_autumn_11.sav's news log: "... pays reparations of 2,269 talents."),
            // via NewsLogWriter.FormatGroupedAmount.
            "peace.pays-reparations",
            new RenderProbeEvent(Loser: "Carthage", N: NewsLogWriter.FormatGroupedAmount(2269)),
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
            "Rome finishes a new fleet at Ostia."
        },
        {
            // Matches newsMessage.defectsFromObservedExample's own operand values.
            "city.defects-to",
            new RenderProbeEvent(CityName: "Synnada", OldOwner: "Galatia", NewOwner: "Seleucid"),
            ObservedExampleText("newsMessage.defectsFromObservedExample")
        },
        {
            // Matches newsMessage.conquersNationObservedExample's own operand values. This row exercises
            // the catalog's stored text only, via the injected-resolver pattern every other row here uses
            // (the probe's own Kind never matches "nation.conquered", so the dash-wrap in
            // NewsLogWriterAppendTests.NationConquered_RendersAsThreeEntries_BetweenDashLines does not
            // trigger here -- that is a separate, dedicated test for DoD 4).
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
        {
            "alliance.formed",
            new RenderProbeEvent(A: "Dacia", B: "Rome"),
            "Dacia forms an alliance with Rome."
        },
        {
            "war.declared",
            new RenderProbeEvent(A: "Dacia", B: "Gaul"),
            "Dacia declares war on Gaul."
        },
        {
            "nation.capital-moved",
            new RenderProbeEvent(Nation: "Rome", CityName: "Ravenna"),
            "Rome have moved their capital to Ravenna."
        },
        {
            "nation.leader-deposed",
            new RenderProbeEvent(Nation: "Rome", LeaderName: "Romulus"),
            "Rome depose their leader Romulus."
        },
        {
            "fleet.damaged-in-storm",
            new RenderProbeEvent(Nation: "Carthage"),
            "A fleet belonging to Carthage is damaged in a storm."
        },
    };

    /// <summary>The three structural entries render as their own stored literal, with no substitution.</summary>
    [Theory]
    [InlineData(NewsMessageCatalog.DashLineKind, "newsMessage.dashLine")]
    [InlineData(NewsMessageCatalog.BlankLineKind, "newsMessage.blankLine")]
    public void StructuralEntries_RenderAsTheirOwnLiteral(string kind, string corpusId)
    {
        Assert.Equal(FixtureCorpus.Get(corpusId).AsString(), NewsMessageCatalog.GetTemplate(kind));
    }

    /// <summary>
    /// <see cref="NewsLogWriter.RenderWeekHeader"/> substitutes <see cref="NewsMessageCatalog.WeekHeaderKind"/>'s
    /// three tokens directly (DoD 3's exact spacing), never comma-grouping the week or year (contrast
    /// <see cref="NewsLogWriter.FormatGroupedAmount"/>).
    /// </summary>
    [Fact]
    public void RenderWeekHeader_SubstitutesTheThreeTokens()
    {
        var rendered = NewsLogWriter.RenderWeekHeader(week: 11, seasonName: "Summer", yearBc: 270);

        Assert.Equal("Week  11      Summer      270BC", rendered);
    }

    /// <summary>Reads an observed-example literal straight from the corpus, for a <see cref="RenderCases"/> row.</summary>
    private static string ObservedExampleText(string corpusId) => FixtureCorpus.Get(corpusId).AsString();

    /// <summary>
    /// Renders exactly one event through the real, public <see cref="NewsLogWriter.Append"/>, resolving the
    /// template through the real production <see cref="NewsMessageCatalog"/> for <paramref name="eventKind"/>
    /// — never the probe event's own (namespace-unique, N10) kind — and reads back the LAST slot it
    /// produced (the message itself, for a dash-wrapped kind like <c>nation.conquered</c>, which also
    /// writes a dash line before and after it).
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

    /// <summary>
    /// DoD 5/N16: the catalog's production entries (excluding the two <c>test.</c>-prefixed pipeline-probe
    /// entries, #91 N23) and the corpus's <c>newsMessage.*</c> entries are in true set equality via this
    /// explicit, exhaustive kind-to-corpus-id map — not merely equal in count (the count-only check this
    /// replaces, #91 N16, would still pass with one entry missing and a different one added).
    /// </summary>
    [Fact]
    public void Catalog_And_CorpusNewsMessages_AreInSetEquality()
    {
        var corpusIds = FixtureCorpus.All
            .Where(e => e.Id.StartsWith("newsMessage.", StringComparison.Ordinal))
            .Select(e => e.Id)
            .ToHashSet(StringComparer.Ordinal);

        var productionKinds = NewsMessageCatalog.RegisteredKinds
            .Where(k => !k.StartsWith("test.", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(corpusIds, KindToCorpusId.Values.ToHashSet(StringComparer.Ordinal));
        Assert.Equal(productionKinds, KindToCorpusId.Keys.ToHashSet(StringComparer.Ordinal));
    }

    /// <summary>The explicit kind ↔ corpus-id correspondence <see cref="Catalog_And_CorpusNewsMessages_AreInSetEquality"/> checks.</summary>
    private static readonly IReadOnlyDictionary<string, string> KindToCorpusId = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["city.falls-to"] = "newsMessage.fallsTo",
        ["city.fails-to-capture"] = "newsMessage.failsToCapture",
        ["city.defects-to"] = "newsMessage.defectsFromObservedExample",
        ["battle.army-destroyed"] = "newsMessage.destroysArmy",
        ["battle.fleet-sunk"] = "newsMessage.sinksFleet",
        ["peace.honourable"] = "newsMessage.honourablePeace",
        ["peace.sues-for"] = "newsMessage.suesForPeace",
        ["peace.ends-trading-agreements"] = "newsMessage.endsTradingAgreements",
        ["peace.ends-alliances"] = "newsMessage.endsAlliances",
        ["peace.pays-reparations"] = "newsMessage.paysReparations",
        ["peace.ally-agreement"] = "newsMessage.allyPeaceAgreement",
        ["fleet.finished"] = "newsMessage.fleetFinished",
        ["fleet.lost-at-sea"] = "newsMessage.fleetLostAtSeaObservedExample",
        ["nation.conquered"] = "newsMessage.conquersNationObservedExample",
        ["alliance.formed"] = "newsMessage.formsAlliance",
        ["war.declared"] = "newsMessage.declaresWar",
        ["nation.capital-moved"] = "newsMessage.movedCapital",
        ["nation.leader-deposed"] = "newsMessage.deposesLeader",
        ["fleet.damaged-in-storm"] = "newsMessage.fleetDamagedInStorm",
        [NewsMessageCatalog.DashLineKind] = "newsMessage.dashLine",
        [NewsMessageCatalog.BlankLineKind] = "newsMessage.blankLine",
        [NewsMessageCatalog.WeekHeaderKind] = "newsMessage.weekHeader",
    };

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
/// A single flexible probe event covering every operand name any catalog template uses.
/// Namespace-unique kind (N10) — never resolved through the production catalog, since
/// <see cref="NewsMessageCatalogTests"/>'s render helper always injects the real template for the
/// specific kind under test, ignoring this event's own <see cref="DomainEvent.Kind"/> entirely.
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
    string? Y = null,
    string? LeaderName = null) : DomainEvent;
