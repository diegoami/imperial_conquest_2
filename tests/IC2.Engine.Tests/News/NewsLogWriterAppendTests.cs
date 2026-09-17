using System.Reflection;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.News;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.News;

/// <summary>
/// Direct, pure-function tests of <see cref="NewsLogWriter.Append"/> — the public entry point the task's
/// design calls for so a later task (T23) can call it directly with a <c>CommandResult</c>'s own events —
/// decoupled from <see cref="TurnCoordinator"/> and from the production <see cref="NewsMessageCatalog"/>
/// (every test here injects its own template resolver and a namespace-unique fixture kind, N10). The
/// pipeline-level behaviour — phase scoping, ordering, and rendering through the real
/// registry-instantiated systems — is <c>NewsLogWriterTests</c>, not this file.
/// </summary>
public class NewsLogWriterAppendTests
{
    private static readonly NewsLogRules GenerousRules =
        new(RingBufferSlots: 40, MessageByteLength: 4096, SeasonNames: ValueList<string>.Empty);

    /// <summary>Both placeholder syntaxes the corpus uses are recognised and substituted in one template.</summary>
    [Fact]
    public void Append_Substitutes_CurlyAndAngleOperands()
    {
        var probeEvent = new MixedPlaceholderProbeEvent(Winner: "Rome", CityName: "Corinth");
        var state = NewsLogWriter.Append(
            CoreTestbed.InitialState(),
            new DomainEvent[] { probeEvent },
            GenerousRules,
            _ => "<winner> takes {CityName}.");

        Assert.Equal("Rome takes Corinth.", Assert.Single(state.NewsLog.Slots).Text);
    }

    /// <summary>
    /// An angle-bracket token's <c>[field[+offset]]</c> provenance annotation (as in the corpus's own
    /// <c>fleet.finished</c> entry) is consumed when locating the property, and never printed in the
    /// rendered message.
    /// </summary>
    [Fact]
    public void Append_StripsBracketAnnotation_FromAngleToken()
    {
        var probeEvent = new BracketAnnotationProbeEvent(CityName: "Byzantium");
        var state = NewsLogWriter.Append(
            CoreTestbed.InitialState(),
            new DomainEvent[] { probeEvent },
            GenerousRules,
            _ => "A new fleet at <cityName[fleet[+20]]>.");

        Assert.Equal("A new fleet at Byzantium.", Assert.Single(state.NewsLog.Slots).Text);
    }

    /// <summary>
    /// N14: a placeholder matching no eligible property throws, rather than being printed literally — a
    /// template/property mismatch must fail loudly, not put the raw placeholder text into the news log (and
    /// into a save) with no signal to anyone.
    /// </summary>
    [Fact]
    public void Append_Throws_WhenPlaceholderHasNoMatchingProperty()
    {
        var probeEvent = new UnknownPlaceholderProbeEvent(CityName: "Sparta");

        var ex = Assert.Throws<InvalidOperationException>(() => NewsLogWriter.Append(
            CoreTestbed.InitialState(),
            new DomainEvent[] { probeEvent },
            GenerousRules,
            _ => "{CityName} did {NotADeclaredProperty}."));

        Assert.Contains("NotADeclaredProperty", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// N13: excluding only properties declared on <see cref="DomainEvent"/> itself (not
    /// <see cref="System.Reflection.BindingFlags.DeclaredOnly"/>) means a property declared on an
    /// intermediate base record between <see cref="DomainEvent"/> and the concrete event type is still a
    /// valid substitution candidate.
    /// </summary>
    [Fact]
    public void Append_Substitutes_PropertyDeclaredOnIntermediateBaseRecord()
    {
        var probeEvent = new DerivedProbeEvent(NewOwner: "Rome");

        var state = NewsLogWriter.Append(
            CoreTestbed.InitialState(),
            new DomainEvent[] { probeEvent },
            GenerousRules,
            _ => "{CityName} falls to {NewOwner}.");

        Assert.Equal("Corinth falls to Rome.", Assert.Single(state.NewsLog.Slots).Text);
    }

    /// <summary>
    /// N12: rendering is a single left-to-right pass over the <em>template</em>, not repeated
    /// <c>Replace</c> calls over a growing result — an operand value that happens to look exactly like
    /// another placeholder token is never itself re-scanned and substituted a second time.
    /// </summary>
    [Fact]
    public void Append_DoesNotDoubleSubstitute_WhenAnOperandValueLooksLikeAPlaceholder()
    {
        // CityName's own value is literally the text of the OTHER placeholder token. A naive
        // sequential-Replace implementation would substitute {OldOwner} first, write "{CityName}" into the
        // result, and then — scanning for {CityName} next — corrupt what it just wrote.
        var probeEvent = new SelfReferentialProbeEvent(CityName: "{OldOwner}", OldOwner: "Athens");
        var state = NewsLogWriter.Append(
            CoreTestbed.InitialState(),
            new DomainEvent[] { probeEvent },
            GenerousRules,
            _ => "{CityName} / {OldOwner}");

        Assert.Equal("{OldOwner} / Athens", Assert.Single(state.NewsLog.Slots).Text);
    }

    /// <summary>
    /// N11/N13: <see cref="DomainEvent.Kind"/> and <see cref="DomainEvent.IsNewsWorthy"/>, declared on
    /// <see cref="DomainEvent"/> itself, are never substitution candidates — a template containing
    /// <c>{Kind}</c> throws exactly as it would for any other unmatched placeholder (N14), rather than
    /// silently resolving to the base property.
    /// </summary>
    [Fact]
    public void Append_Throws_ForBaseDomainEventProperty()
    {
        var probeEvent = new BasePropertyProbeEvent(Detail: "irrelevant");

        var ex = Assert.Throws<InvalidOperationException>(() => NewsLogWriter.Append(
            CoreTestbed.InitialState(),
            new DomainEvent[] { probeEvent },
            GenerousRules,
            _ => "kind={Kind}"));

        Assert.Contains("Kind", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// DoD 1 (#91 N15): the slot is a NUL-terminated single-byte string, so at most
    /// <c>MessageByteLength - 1</c> bytes of text ever reach the log — the 61st byte is always the
    /// writer's own NUL (news-log-format-and-messages.md Q1). At the real 61-byte slot size that is 60
    /// bytes: a 59-byte message is untouched, a 60-byte message is untouched (exactly at the cap), and a
    /// 61-byte message is cut to 60.
    /// </summary>
    [Theory]
    [InlineData(59)]
    [InlineData(60)]
    [InlineData(61)]
    public void Append_Truncates_AtSixtyBytes_ReservingTheSlotsFinalByteForTheNul(int messageLength)
    {
        var rules = new NewsLogRules(RingBufferSlots: 1, MessageByteLength: 61, SeasonNames: ValueList<string>.Empty);
        var probeEvent = new MultiByteProbeEvent(Text: new string('x', messageLength));

        var state = NewsLogWriter.Append(
            CoreTestbed.InitialState(),
            new DomainEvent[] { probeEvent },
            rules,
            _ => "{Text}");

        var text = Assert.Single(state.NewsLog.Slots).Text;
        Assert.Equal(new string('x', Math.Min(messageLength, 60)), text);
        Assert.True(text.Length <= 60);
    }

    /// <summary>
    /// DoD 1 (#91 N15): a non-ASCII operand is rejected rather than silently counted as UTF-8 or
    /// transcoded through a best-fit fallback — every shipped name is ASCII
    /// (news-log-format-and-messages.md Q1), so a non-ASCII byte signals an upstream bug. See
    /// <c>NewsLogWriter.TruncateToByteLength</c>'s remarks for what was searched.
    /// </summary>
    [Fact]
    public void Append_Rejects_ANonAsciiOperand()
    {
        var rules = new NewsLogRules(RingBufferSlots: 1, MessageByteLength: 61, SeasonNames: ValueList<string>.Empty);
        var probeEvent = new MultiByteProbeEvent(Text: "Ostiaé"); // trailing é (U+00E9)

        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => NewsLogWriter.Append(
            CoreTestbed.InitialState(),
            new DomainEvent[] { probeEvent },
            rules,
            _ => "{Text}"));

        Assert.Contains("ASCII", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// DoD 4: an event whose kind is <c>NewsMessageCatalog.IsWrappedInDashLines</c> renders as three
    /// entries — a dash line, the message, then the dash line again — matching the original's
    /// elimination banner (news-log-format-and-messages.md Q4 #10-12, confirmed 2 of 2). Uses the
    /// test-only <c>test.news-log.pipeline-probe.nation-conquered</c> kind through the *default*
    /// production catalog (no <c>templateFor</c> override), because the dash-wrap decision keys off the
    /// published event's own <c>Kind</c> — see <c>NewsMessageCatalog</c>'s remarks on why this needs its
    /// own namespace-unique wrapped kind rather than reusing the real <c>nation.conquered</c> (#91 N23).
    /// </summary>
    [Fact]
    public void NationConquered_RendersAsThreeEntries_BetweenDashLines()
    {
        var probeEvent = new PipelineProbeNationConqueredEvent(
            ConqueringNation: "Seleucid", ConqueredNation: "Galatia");

        var state = NewsLogWriter.Append(CoreTestbed.InitialState(), new DomainEvent[] { probeEvent }, GenerousRules);

        var texts = state.NewsLog.Slots.Select(s => s.Text).ToList();
        var dash = NewsMessageCatalog.GetTemplate(NewsMessageCatalog.DashLineKind);
        Assert.Equal(new[] { dash, "Seleucid conquers Galatia.", dash }, texts);
    }

    /// <summary>A non-news-worthy event is skipped entirely: no template lookup, no ring-buffer write.</summary>
    [Fact]
    public void Append_Skips_NonNewsworthyEvents()
    {
        var probeEvent = new NonNewsworthyFixtureEvent("ignored");

        var state = NewsLogWriter.Append(
            CoreTestbed.InitialState(),
            new DomainEvent[] { probeEvent },
            GenerousRules,
            _ => throw new InvalidOperationException("A non-news-worthy event must never reach the template resolver."));

        Assert.Empty(state.NewsLog.Slots);
    }

    /// <summary>Several events in one <see cref="NewsLogWriter.Append"/> call still evict beyond capacity.</summary>
    [Fact]
    public void Append_Evicts_WhenManyEventsInOneCallExceedCapacity()
    {
        var rules = new NewsLogRules(RingBufferSlots: 3, MessageByteLength: 4096, SeasonNames: ValueList<string>.Empty);
        var events = Enumerable.Range(0, 5)
            .Select(i => (DomainEvent)new MultiByteProbeEvent($"m{i}"))
            .ToArray();

        var state = NewsLogWriter.Append(CoreTestbed.InitialState(), events, rules, _ => "{Text}");

        var texts = state.NewsLog.Slots.Select(s => s.Text).ToList();
        Assert.Equal(new[] { "m2", "m3", "m4" }, texts);
    }

    /// <summary>With no resolver supplied, <see cref="Append"/> uses the real production catalog.</summary>
    [Fact]
    public void Append_UsesProductionCatalog_WhenNoResolverIsGiven()
    {
        var probeEvent = new CityFallsToFixtureEvent("Rome", "Republic", "Empire");

        var state = NewsLogWriter.Append(CoreTestbed.InitialState(), new DomainEvent[] { probeEvent }, GenerousRules);

        Assert.Equal("Rome   (Republic)  falls to Empire.", Assert.Single(state.NewsLog.Slots).Text);
    }

    /// <summary>
    /// #91 N26: an angle-bracket token that matches two properties differing only by case throws, rather
    /// than silently picking whichever <see cref="System.Reflection.PropertyInfo.GetProperties()"/>
    /// happens to return first (an order-dependent result <c>PropertyInfo.GetProperties()</c>'s own
    /// contract does not promise).
    /// </summary>
    [Fact]
    public void Append_Throws_WhenAnAngleTokenMatchesTwoPropertiesDifferingOnlyByCase()
    {
        var probeEvent = new CaseAmbiguousProbeEvent(CityName: "Rome", Cityname: "Carthage");

        Assert.Throws<AmbiguousMatchException>(() => NewsLogWriter.Append(
            CoreTestbed.InitialState(),
            new DomainEvent[] { probeEvent },
            GenerousRules,
            _ => "<cityname> falls."));
    }

    /// <summary>
    /// DoD 2 (news-log-format-and-messages.md Q2): the reparations amount is comma-grouped, no locale,
    /// no decimals; a week or year operand is never grouped (contrast <see cref="RenderWeekHeader"/>).
    /// </summary>
    [Theory]
    [InlineData(2269, "2,269")]
    [InlineData(12345678, "12,345,678")]
    [InlineData(334, "334")]
    public void FormatGroupedAmount_GroupsEveryThreeDigits(int amount, string expected)
    {
        Assert.Equal(expected, NewsLogWriter.FormatGroupedAmount(amount));
    }

    /// <summary>
    /// DoD 2, end to end: a reparations event whose amount property is pre-formatted through
    /// <see cref="NewsLogWriter.FormatGroupedAmount"/> (the emitting task's own responsibility, matching
    /// the existing <c>peace.pays-reparations</c> render test) renders with the comma intact.
    /// </summary>
    [Fact]
    public void Append_Renders_TheGroupedReparationsAmount()
    {
        var probeEvent = new GroupedAmountProbeEvent(Loser: "Carthage", Amount: NewsLogWriter.FormatGroupedAmount(2269));

        var state = NewsLogWriter.Append(
            CoreTestbed.InitialState(),
            new DomainEvent[] { probeEvent },
            GenerousRules,
            _ => "<loser> pays reparations of <amount> talents.");

        Assert.Equal(
            "Carthage pays reparations of 2,269 talents.",
            Assert.Single(state.NewsLog.Slots).Text);
    }

    /// <summary>
    /// news-log-format-and-messages.md Q2: a war declaration is uppercased in full (ASCII a-z only) when
    /// either nation is human; an alliance line is never uppercased regardless.
    /// </summary>
    [Theory]
    [InlineData("Dacia declares war on Gaul.", false, "Dacia declares war on Gaul.")]
    [InlineData("Dacia declares war on Gaul.", true, "DACIA DECLARES WAR ON GAUL.")]
    public void ApplyWarDeclarationShouting_UppercasesOnlyWhenAHumanIsInvolved(
        string rendered, bool involvesHuman, string expected)
    {
        Assert.Equal(expected, NewsLogWriter.ApplyWarDeclarationShouting(rendered, involvesHuman));
    }

    /// <summary>
    /// #91 N20: the out-of-run entry point is exercised with a real <see cref="CommandResult"/> from a
    /// real <see cref="CommandDispatcher.Dispatch"/> call, outside a <see cref="TurnCoordinator"/> run --
    /// not a plain array standing in for its <see cref="CommandResult.Events"/>. Mirrors how T23's
    /// human-order commands and <c>GameSession</c> (T41) both route a dispatched command's own events
    /// through this public method directly (<see cref="NewsLogWriter"/>'s own remarks: a command
    /// dispatched outside a run, and <see cref="TurnCoordinator.FireQuarterBoundary"/> called directly,
    /// both bypass <see cref="SystemContext.PublishedEvents"/> the same way).
    /// </summary>
    [Fact]
    public void Append_Renders_EventsFromARealCommandResult()
    {
        var registry = SystemRegistry.FromAssemblies(
            new[] { typeof(NewsLogWriterSeatEnd).Assembly, typeof(NewsLogWriterFixtures).Assembly },
            type => type == typeof(CommandResultProbeCommandHandler));
        var dispatcher = new CommandDispatcher(
            registry, CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, NullEventSink.Instance);

        var result = dispatcher.Dispatch(CoreTestbed.InitialState(), new CommandResultProbeCommand("north"));
        Assert.True(result.IsAccepted);

        var updated = NewsLogWriter.Append(result.State, result.Events, CoreTestbed.Toy.Ruleset.NewsLog);

        Assert.Equal(
            "CommandProbeCity   (Old)  falls to New.",
            Assert.Single(updated.NewsLog.Slots).Text);
    }
}

// --- Probe events. Namespace-unique kinds (N10): none of these are ever resolved through the production
// catalog (every test above injects its own resolver), so none can collide with a later task's own test
// fixture or production event.

[DomainEvent("test.news-log.append-probe.mixed-placeholder", NewsWorthy = true)]
public sealed record MixedPlaceholderProbeEvent(string Winner, string CityName) : DomainEvent;

[DomainEvent("test.news-log.append-probe.bracket-annotation", NewsWorthy = true)]
public sealed record BracketAnnotationProbeEvent(string CityName) : DomainEvent;

[DomainEvent("test.news-log.append-probe.unknown-placeholder", NewsWorthy = true)]
public sealed record UnknownPlaceholderProbeEvent(string CityName) : DomainEvent;

[DomainEvent("test.news-log.append-probe.self-referential", NewsWorthy = true)]
public sealed record SelfReferentialProbeEvent(string CityName, string OldOwner) : DomainEvent;

[DomainEvent("test.news-log.base-property-probe", NewsWorthy = true)]
public sealed record BasePropertyProbeEvent(string Detail) : DomainEvent;

/// <summary>The test-only, dash-wrapped kind <c>NewsMessageCatalog.DashWrappedKinds</c> also carries.</summary>
[DomainEvent("test.news-log.pipeline-probe.nation-conquered", NewsWorthy = true)]
public sealed record PipelineProbeNationConqueredEvent(string ConqueringNation, string ConqueredNation) : DomainEvent;

/// <summary>Two properties differing only by case — #91 N26's regression case.</summary>
[DomainEvent("test.news-log.append-probe.case-ambiguous", NewsWorthy = true)]
public sealed record CaseAmbiguousProbeEvent(string CityName, string Cityname) : DomainEvent;

[DomainEvent("test.news-log.append-probe.grouped-amount", NewsWorthy = true)]
public sealed record GroupedAmountProbeEvent(string Loser, string Amount) : DomainEvent;

[DomainEvent("test.news-log.append-probe.multi-byte", NewsWorthy = true)]
public sealed record MultiByteProbeEvent(string Text) : DomainEvent;

/// <summary>An intermediate base record, one level below <see cref="DomainEvent"/> — not itself concrete.</summary>
public abstract record BaseWithCityNameProbeEvent(string CityName) : DomainEvent;

/// <summary>
/// Declares no <c>CityName</c> property of its own — it inherits one from
/// <see cref="BaseWithCityNameProbeEvent"/>, an intermediate base record, not from <see cref="DomainEvent"/>
/// itself. N13's regression case.
/// </summary>
[DomainEvent("test.news-log.append-probe.derived", NewsWorthy = true)]
public sealed record DerivedProbeEvent(string NewOwner) : BaseWithCityNameProbeEvent("Corinth");
