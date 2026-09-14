using System.Collections.Frozen;
using System.Reflection;
using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.News;

/// <summary>
/// Implements the domain-event sink that renders news-worthy events into GameState.NewsLog.
/// Also implements IGameSystem to commit the buffered messages to the state at the end of each round.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The writer implements both IEventSink and IGameSystem.</strong> Events are published to it
/// as they happen (the IEventSink interface), and they are rendered and flushed to GameState at the
/// end of each round (the IGameSystem interface).
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
[GameSystem(TurnPhase.RoundEnd, "news.writer")]
public sealed class NewsLogWriter : IEventSink, IGameSystem
{
    private readonly List<DomainEvent> _pending = new();
    private readonly FrozenDictionary<string, PropertyInfo[]> _cachedEventProperties = new Dictionary<string, PropertyInfo[]>().ToFrozenDictionary();

    /// <summary>Publishes an event for later rendering into the news log.</summary>
    public void Publish(DomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _pending.Add(domainEvent);
    }

    /// <summary>
    /// Renders all pending news-worthy events into GameState.NewsLog and clears the buffer.
    /// Called once per round by the turn coordinator.
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

            var rendered = RenderMessage(evt);
            newsLog = newsLog.Append(new NewsEntry(rendered), rules);
        }

        _pending.Clear();

        return context.State with { NewsLog = newsLog };
    }

    /// <summary>
    /// Renders a news-worthy event into a message by substituting operands into the catalog template.
    /// </summary>
    private string RenderMessage(DomainEvent evt)
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
                var valueStr = value?.ToString() ?? "";
                result = result.Replace(placeholder, valueStr, StringComparison.Ordinal);
            }
        }

        return result;
    }
}
