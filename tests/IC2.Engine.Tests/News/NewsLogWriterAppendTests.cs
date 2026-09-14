using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.News;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.News;

/// <summary>
/// Direct, pure-function tests of <see cref="NewsLogWriter.Append"/> — the public entry point
/// <c>docs/task-catalogue.md</c>'s hazard asks to be "test[ed] directly too", decoupled from
/// <see cref="TurnCoordinator"/> and from the production <see cref="NewsMessageCatalog"/> (every test here
/// injects its own template resolver and a namespace-unique fixture kind, N10). The pipeline-level
/// behaviour — phase scoping, ordering, and rendering through the real registry-instantiated systems — is
/// <c>NewsLogWriterTests</c>, not this file.
/// </summary>
public class NewsLogWriterAppendTests
{
    private static readonly NewsLogRules GenerousRules = new(RingBufferSlots: 40, MessageByteLength: 4096);

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
    /// <c>fleet.finished</c> and <c>victory.conquered-by-nation</c> entries) is consumed when locating the
    /// property, and never printed in the rendered message.
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

    /// <summary>A placeholder matching no declared property is left exactly as the template wrote it.</summary>
    [Fact]
    public void Append_Leaves_UnmatchedPlaceholder_Unchanged()
    {
        var probeEvent = new UnknownPlaceholderProbeEvent(CityName: "Sparta");
        var state = NewsLogWriter.Append(
            CoreTestbed.InitialState(),
            new DomainEvent[] { probeEvent },
            GenerousRules,
            _ => "{CityName} did {NotADeclaredProperty}.");

        Assert.Equal("Sparta did {NotADeclaredProperty}.", Assert.Single(state.NewsLog.Slots).Text);
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
    /// N11: only the event's own <em>declared</em> properties are substitution candidates —
    /// <see cref="DomainEvent.Kind"/>, declared on the abstract base, is never treated as an operand even
    /// when a template happens to contain the literal text <c>{Kind}</c>.
    /// </summary>
    [Fact]
    public void Append_Ignores_BaseDomainEventProperties()
    {
        var probeEvent = new BasePropertyProbeEvent(Detail: "irrelevant");
        var state = NewsLogWriter.Append(
            CoreTestbed.InitialState(),
            new DomainEvent[] { probeEvent },
            GenerousRules,
            _ => "kind={Kind}");

        // If {Kind} resolved to the base property, this would read "kind=test.news-log.base-property-probe".
        Assert.Equal("kind={Kind}", Assert.Single(state.NewsLog.Slots).Text);
    }

    /// <summary>
    /// Truncation counts UTF-8 bytes, not <see cref="string.Length"/> (N6), and never splits a multi-byte
    /// character. "é" is <c>0xC3 0xA9</c> in UTF-8 (2 bytes); five of them is 10 bytes. A 7-byte limit lands
    /// inside the fourth character's 2-byte sequence, so the correct result keeps only the first three.
    /// </summary>
    [Fact]
    public void Append_Truncates_ByUtf8ByteLength_WithoutSplittingACharacter()
    {
        var rules = new NewsLogRules(RingBufferSlots: 1, MessageByteLength: 7);
        var probeEvent = new MultiByteProbeEvent(Text: "ééééé");

        var state = NewsLogWriter.Append(
            CoreTestbed.InitialState(),
            new DomainEvent[] { probeEvent },
            rules,
            _ => "{Text}");

        var text = Assert.Single(state.NewsLog.Slots).Text;
        Assert.Equal("ééé", text);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(text) <= rules.MessageByteLength);
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
        var rules = new NewsLogRules(RingBufferSlots: 3, MessageByteLength: 4096);
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

[DomainEvent("test.news-log.append-probe.multi-byte", NewsWorthy = true)]
public sealed record MultiByteProbeEvent(string Text) : DomainEvent;
