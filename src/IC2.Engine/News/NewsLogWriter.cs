using System.Collections.Frozen;
using System.Reflection;
using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.News;

/// <summary>
/// Implements the domain-event sink that renders news-worthy events into GameState.NewsLog.
/// Also implements IGameSystem to commit the buffered messages to the state at the end of each seat's turn.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The writer implements both IEventSink and IGameSystem, and is intentionally stateful.</strong>
/// This is the only system in the engine that violates the "stateless shared instance" rule documented in
/// <see cref="SystemRegistry"/>. It works <em>only</em> if the exact same instance is wired into both roles:
/// the <c>IGameSystem</c> instance from the assembly scan and the <c>IEventSink</c> in the engine's event sink
/// chain must be the same object. If a future wiring task constructs separate instances for each role, every
/// event will be silently dropped. The <see cref="SeatEnd"/> phase ensures events published during a seat's
/// <see cref="TurnPhase.Orders"/> run are rendered into <c>GameState.NewsLog</c> "at the end of that turn"
/// as DoD 4 requires — not at the end of a full 16-seat round.
/// </para>
/// <para>
/// Every system that publishes a news-worthy event does so by calling
/// <c>context.Events.Publish(theEvent)</c>. T10's news sink is one implementation of IEventSink; the
/// Godot UI will be another.
/// </para>
/// <para>
/// Message rendering is deterministic and requires no RNG. A test can construct a GameState with
/// events already published, call this writer's system directly, and verify the news log is updated
/// correctly without mocking or complex setup.
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
    /// Renders all pending news-worthy events into GameState.NewsLog and clears the buffer.
    /// Called once per seat's turn, in the <see cref="TurnPhase.SeatEnd"/> phase, so events published
    /// during that seat's <see cref="TurnPhase.Orders"/> appear "at the end of that turn" as DoD 4 requires.
    /// </summary>
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var newsLog = context.State.NewsLog;
        var rules = context.Ruleset.NewsLog;

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

        return context.State with { NewsLog = newsLog };
    }

    /// <summary>
    /// Renders a news-worthy event into a message by substituting operands into the catalog template,
    /// then truncates to the confirmed message byte length (61 bytes per <c>decompiled-news-log-identified.md</c>
    /// and <c>decompiled-sav-file-layout.md</c>). Rendering is deterministic: operand substitution uses
    /// invariant culture and occurs in property-declaration order (not hash-based).
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
                var valueStr = value == null ? "" : string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0}", value);
                result = result.Replace(placeholder, valueStr, StringComparison.Ordinal);
            }
        }

        // Truncate to the confirmed message buffer size (61 bytes)
        if (result.Length > rules.MessageByteLength)
        {
            result = result.Substring(0, rules.MessageByteLength);
        }

        return result;
    }
}
