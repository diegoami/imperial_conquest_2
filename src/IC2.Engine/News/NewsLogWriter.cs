using System.Globalization;
using System.Reflection;
using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.News;

/// <summary>
/// Implements the domain-event sink that renders news-worthy events into GameState.NewsLog.
/// Also implements IGameSystem to flush the buffer at SeatEnd and cooperates with
/// <see cref="NewsLogWriterRoundEnd"/> to also flush at RoundEnd.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The writer implements both IEventSink and IGameSystem, and is intentionally stateful.</strong>
/// This is the only system in the engine that violates the "stateless shared instance" rule documented in
/// <see cref="SystemRegistry"/>. It works <em>only</em> if the exact same instance is wired into both roles:
/// the <c>IGameSystem</c> instance from the assembly scan and the <c>IEventSink</c> in the engine's event
/// sink chain must be the same object. If a different instance is constructed for each role, every event
/// will be silently dropped.
/// </para>
/// <para>
/// The dual-phase design ensures:
/// (1) Events published during a seat-scoped phase (such as <see cref="TurnPhase.Orders"/>) appear in
///     the news log "at the end of that turn" via this system at <see cref="TurnPhase.SeatEnd"/>,
///     satisfying DoD 4(a).
/// (2) Events published during a round-scoped phase (such as <see cref="TurnPhase.WeatherEvents"/>)
///     appear at the round boundary via <see cref="NewsLogWriterRoundEnd"/> at <see cref="TurnPhase.RoundEnd"/>,
///     not deferred to a later seat's turn, satisfying DoD 4(b).
/// </para>
/// </remarks>
[GameSystem(TurnPhase.SeatEnd, "news.writer")]
public sealed class NewsLogWriter : IEventSink, IGameSystem
{
    private readonly List<DomainEvent> _pending = new();

    /// <summary>Publishes an event for later rendering into the news log.</summary>
    public void Publish(DomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _pending.Add(domainEvent);
    }

    /// <summary>
    /// Executes at the end of each seat's turn to flush pending events to the news log.
    /// </summary>
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return FlushToNewsLog(context.State, context.Ruleset);
    }

    /// <summary>
    /// Renders all pending news-worthy events into GameState.NewsLog and clears the buffer.
    /// Called by both this system at <see cref="TurnPhase.SeatEnd"/> and by
    /// <see cref="NewsLogWriterRoundEnd"/> at <see cref="TurnPhase.RoundEnd"/>.
    /// </summary>
    internal GameState FlushToNewsLog(GameState state, Ruleset ruleset)
    {
        var newsLog = state.NewsLog;
        var rules = ruleset.NewsLog;

        foreach (var evt in _pending)
        {
            if (!evt.IsNewsWorthy)
            {
                continue;
            }

            var rendered = RenderMessage(evt, rules);
            newsLog = newsLog.Append(new NewsEntry(rendered), rules);
        }

        _pending.Clear();

        return state with { NewsLog = newsLog };
    }

    /// <summary>
    /// Renders a news-worthy event into a message by substituting operands into the catalog
    /// template, then truncates to the confirmed message byte length (61 bytes per
    /// <c>decompiled-news-log-identified.md</c> and <c>decompiled-sav-file-layout.md</c>).
    /// Rendering is deterministic: operand substitution uses invariant culture and occurs in
    /// property-declaration order (not hash-based).
    /// </summary>
    private string RenderMessage(DomainEvent evt, NewsLogRules rules)
    {
        var kind = evt.Kind;
        var template = NewsMessageCatalog.GetTemplate(kind);

        // Find all placeholders in the template (e.g., {CityName}, {OldOwner})
        var result = template;
        var eventType = evt.GetType();
        var properties = eventType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            var placeholder = $"{{{prop.Name}}}";
            if (result.Contains(placeholder, StringComparison.Ordinal))
            {
                var value = prop.GetValue(evt);
                var valueStr = value == null ? "" : string.Format(CultureInfo.InvariantCulture, "{0}", value);
                result = result.Replace(placeholder, valueStr, StringComparison.Ordinal);
            }
        }

        // Truncate to the confirmed message buffer size (61 bytes)
        // Note: Currently truncates by character count, not byte count (indistinguishable for ASCII)
        if (result.Length > rules.MessageByteLength)
        {
            result = result.Substring(0, rules.MessageByteLength);
        }

        return result;
    }
}

/// <summary>
/// System that flushes pending news events to GameState.NewsLog at the end of each round.
/// Runs in <see cref="TurnPhase.RoundEnd"/> so events published during a round-scoped phase
/// (such as <see cref="TurnPhase.WeatherEvents"/>) are rendered at the round boundary, not
/// deferred to a later seat's turn. Accesses the <see cref="NewsLogWriter"/> instance from
/// the context's event sink via reflection on the <see cref="CompositeEventSink"/> wrapper.
/// </summary>
[GameSystem(TurnPhase.RoundEnd, "news.writer.round")]
public sealed class NewsLogWriterRoundEnd : IGameSystem
{
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // The context.Events is a CompositeEventSink wrapping [RecordingEventSink, NewsLogWriter].
        // We extract the writer via reflection to call FlushToNewsLog.
        var writer = ExtractNewsLogWriter(context.Events);
        if (writer is not null)
        {
            return writer.FlushToNewsLog(context.State, context.Ruleset);
        }

        // If extraction fails, this is silent — the seat-scoped flusher at SeatEnd already ran.
        return context.State;
    }

    /// <summary>
    /// Extracts a NewsLogWriter instance from a CompositeEventSink by reflection.
    /// The composite sink wraps [RecordingEventSink, NewsLogWriter] in its _sinks field.
    /// </summary>
    private static NewsLogWriter? ExtractNewsLogWriter(IEventSink sink)
    {
        if (sink is CompositeEventSink composite)
        {
            var sinksField = typeof(CompositeEventSink).GetField(
                "_sinks",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (sinksField?.GetValue(composite) is IEventSink[] sinks && sinks.Length > 1)
            {
                return sinks[1] as NewsLogWriter;
            }
        }

        return null;
    }
}
