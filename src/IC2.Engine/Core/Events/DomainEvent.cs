using System.Collections.Concurrent;
using System.Reflection;

namespace IC2.Engine.Core;

/// <summary>
/// Something the game did that anyone outside the system that did it might care about: a battle
/// resolved, a city changed hands, a fleet was lost at sea.
/// </summary>
/// <remarks>
/// <para>
/// Events are the engine's only outward narration. A system never calls the news log, never touches the
/// UI, and never calls another system directly — it publishes an event to
/// <see cref="SystemContext.Events"/>, and T10's news log and the Godot UI each consume the same stream.
/// T16's Definition of Done depends on exactly this: its <c>PeaceTreatyTriggered</c> must be observable
/// "with no diplomacy system registered".
/// </para>
/// <para>
/// <strong>Why subtypes and an attribute rather than one big enum.</strong> An enum listing every event
/// in the game would be a shared file that a dozen tasks each append to — precisely the conflict
/// <c>docs/build-orchestration-plan.md</c> §2.3 removes everywhere else. Instead each task declares its
/// own event records in its own directory, marked with <see cref="DomainEventAttribute"/>, and
/// <see cref="DomainEventCatalog"/> recovers the full set by the same assembly scan that finds systems.
/// A coverage test over "every declared news-worthy event kind" is therefore still writable — which is
/// what T10's Definition of Done item 3 actually needs.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// [DomainEvent("battle.resolved", NewsWorthy = true)]
/// public sealed record BattleResolved(string WinnerNationId, string LoserNationId) : DomainEvent;
/// </code>
/// </example>
public abstract record DomainEvent
{
    /// <summary>
    /// The event's stable kind string, read from its <see cref="DomainEventAttribute"/>. Stable across
    /// C# renames, which is what makes it safe for a news catalog key or a saved log.
    /// </summary>
    /// <exception cref="InvalidOperationException">The concrete type carries no attribute.</exception>
    public string Kind => DomainEventCatalog.KindOf(GetType());

    /// <summary>Whether this kind of event is one the news log should render.</summary>
    /// <exception cref="InvalidOperationException">The concrete type carries no attribute.</exception>
    public bool IsNewsWorthy => DomainEventCatalog.Describe(GetType()).NewsWorthy;
}

/// <summary>Declares a <see cref="DomainEvent"/> subtype's stable kind and whether it reaches the news log.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class DomainEventAttribute : Attribute
{
    /// <summary>Declares an event kind.</summary>
    /// <param name="kind">A stable, globally unique id in <c>area.name</c> form.</param>
    public DomainEventAttribute(string kind) => Kind = SystemId.Validated(kind, nameof(kind));

    /// <summary>The event's stable kind string.</summary>
    public string Kind { get; }

    /// <summary>
    /// Whether the news log is expected to carry a message for this kind. T10's coverage test fails when
    /// a later task declares a news-worthy event without adding a catalog entry for it.
    /// </summary>
    public bool NewsWorthy { get; init; }
}

/// <summary>One declared event kind: its attribute data plus the type that declared it.</summary>
public sealed record DomainEventDescriptor(string Kind, bool NewsWorthy, Type EventType);

/// <summary>
/// Every <see cref="DomainEvent"/> kind an assembly declares, discovered by the same attribute scan that
/// finds systems and command handlers.
/// </summary>
public static class DomainEventCatalog
{
    private static readonly ConcurrentDictionary<Type, DomainEventDescriptor> DescriptorCache = new();

    /// <summary>
    /// Describes one event type from its attribute.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The type carries no <see cref="DomainEventAttribute"/>. Every concrete event must declare one:
    /// a kind that defaulted to the C# type name would change silently on a rename and break the news
    /// catalog key it is used as.
    /// </exception>
    public static DomainEventDescriptor Describe(Type eventType)
    {
        ArgumentNullException.ThrowIfNull(eventType);

        // Keyed lookup only; the cache is never enumerated, so no hash-ordered iteration reaches a
        // gameplay path. The determinism guard checks exactly that distinction.
        return DescriptorCache.GetOrAdd(eventType, static type =>
        {
            var attribute = type.GetCustomAttribute<DomainEventAttribute>(inherit: false)
                            ?? throw new InvalidOperationException(
                                $"'{type.FullName}' is a DomainEvent but carries no [DomainEvent(\"...\")] "
                                + "attribute. Every concrete event declares its own stable kind string.");

            return new DomainEventDescriptor(attribute.Kind, attribute.NewsWorthy, type);
        });
    }

    /// <summary>The stable kind string of an event type.</summary>
    /// <exception cref="InvalidOperationException">The type carries no attribute.</exception>
    public static string KindOf(Type eventType) => Describe(eventType).Kind;

    /// <summary>
    /// Every event kind declared in <paramref name="assemblies"/>, ordered by kind so that two runs, two
    /// machines and two scan orders produce the same list.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Two declared events share a kind string, or a concrete event type declares none.
    /// </exception>
    public static IReadOnlyList<DomainEventDescriptor> Discover(IEnumerable<Assembly> assemblies)
    {
        ArgumentNullException.ThrowIfNull(assemblies);

        var found = new List<DomainEventDescriptor>();
        foreach (var type in AssemblyScan.CandidateTypes(assemblies))
        {
            if (!typeof(DomainEvent).IsAssignableFrom(type) || type.IsAbstract)
            {
                continue;
            }

            found.Add(Describe(type));
        }

        found.Sort(static (left, right) => string.CompareOrdinal(left.Kind, right.Kind));

        for (var i = 1; i < found.Count; i++)
        {
            if (string.Equals(found[i].Kind, found[i - 1].Kind, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Two domain events declare the kind '{found[i].Kind}': "
                    + $"'{found[i - 1].EventType.FullName}' and '{found[i].EventType.FullName}'.");
            }
        }

        return found;
    }
}
