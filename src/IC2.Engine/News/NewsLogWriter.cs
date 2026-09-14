using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.News;

/// <summary>
/// The pure function that turns news-worthy domain events into rendered messages appended to
/// <see cref="GameState.NewsLog"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the one path into <see cref="GameState.NewsLog"/></strong>
/// (<c>docs/task-catalogue.md</c> "News log ring buffer and message catalog"): both
/// <see cref="NewsLogWriterSeatEnd"/> and <see cref="NewsLogWriterRoundEnd"/> call
/// <see cref="Append"/>, and nothing else writes to the news log. Commands dispatched outside a run (for
/// example T23's human orders, via the two-argument <c>Dispatch</c>) never reach
/// <see cref="SystemContext.PublishedEvents"/>, so T23 calls <see cref="Append"/> directly with a
/// <c>CommandResult</c>'s own events — which is exactly why this is a public static method taking plain
/// data, not something reachable only through <see cref="IGameSystem.Execute"/>.
/// </para>
/// <para>
/// <see cref="Append"/> is a pure function: every input arrives as a parameter, nothing is held between
/// calls, and the same inputs always produce the same output. That is what lets both registered systems
/// below stay genuinely stateless (<c>docs/task-catalogue.md</c>'s hazard for this task, and
/// <see cref="IGameSystem"/>'s own contract) while still sharing one rendering implementation.
/// </para>
/// </remarks>
public static class NewsLogWriter
{
    private static readonly Regex PlaceholderPattern = new(
        @"\{(?<curly>[A-Za-z][A-Za-z0-9]*)\}|<(?<angle>[A-Za-z][A-Za-z0-9]*)(?:\[[^<>]*\])?>",
        RegexOptions.Compiled);

    /// <summary>
    /// Renders every news-worthy event in <paramref name="events"/> and appends each rendered message to
    /// <paramref name="state"/>'s news log, oldest-eviction included.
    /// </summary>
    /// <param name="state">The state to append to.</param>
    /// <param name="events">
    /// The events to consider, in order. A non-news-worthy event (<see cref="DomainEvent.IsNewsWorthy"/>
    /// false) is skipped.
    /// </param>
    /// <param name="rules">The ring-buffer geometry and message length, from the loaded ruleset.</param>
    /// <param name="templateFor">
    /// Resolves an event kind to its message template. Defaults to
    /// <see cref="NewsMessageCatalog.GetTemplate"/>; a caller (a test, most often) may substitute its own
    /// so it never has to add a fixture-only kind to the production catalog just to exercise this method.
    /// </param>
    /// <returns><paramref name="state"/> with every rendered message appended, in order.</returns>
    public static GameState Append(
        GameState state,
        IEnumerable<DomainEvent> events,
        NewsLogRules rules,
        Func<string, string>? templateFor = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(rules);

        var resolveTemplate = templateFor ?? NewsMessageCatalog.GetTemplate;
        var newsLog = state.NewsLog;

        foreach (var domainEvent in events)
        {
            if (!domainEvent.IsNewsWorthy)
            {
                continue;
            }

            var template = resolveTemplate(domainEvent.Kind);
            var rendered = RenderMessage(domainEvent, template);
            var truncated = TruncateToByteLength(rendered, rules.MessageByteLength);
            newsLog = newsLog.Append(new NewsEntry(truncated), rules);
        }

        return state with { NewsLog = newsLog };
    }

    /// <summary>
    /// Substitutes a template's placeholders from <paramref name="domainEvent"/>'s own declared
    /// properties, in a single left-to-right pass over the template.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Single pass, not repeated <c>Replace</c> calls.</strong> Placeholder occurrences are found
    /// by scanning <paramref name="domainEvent"/>'s <em>template</em> once; each match is resolved and
    /// appended immediately, and the cursor only ever moves forward. An operand value that happens to
    /// contain literal placeholder-looking text (for example a city named <c>{OldOwner}</c>) is therefore
    /// never re-scanned and can never be substituted a second time.
    /// </para>
    /// <para>
    /// <strong>Only the event's own properties are candidates, never <see cref="DomainEvent"/>'s own.</strong>
    /// A property is excluded exactly when <see cref="PropertyInfo.DeclaringType"/> is
    /// <see cref="DomainEvent"/> itself — <see cref="DomainEvent.Kind"/> and
    /// <see cref="DomainEvent.IsNewsWorthy"/>, and only those, are never treated as operands even if a
    /// template happened to contain <c>{Kind}</c>. This is deliberately narrower than excluding every
    /// inherited property (<see cref="BindingFlags.DeclaredOnly"/> would also have hidden a property
    /// declared on an intermediate base record between <see cref="DomainEvent"/> and the concrete event
    /// type, silently under-rendering it).
    /// </para>
    /// <para>
    /// Recognises two placeholder syntaxes, because the corpus's own source reports used both: a
    /// curly-brace <c>{PascalCase}</c> token is matched case-sensitively against a property of that exact
    /// name; an angle-bracket <c>&lt;lowercase&gt;</c> token — optionally followed by a
    /// <c>[field[+offset]]</c> provenance annotation the source report included in its own quoted string —
    /// is matched case-insensitively, and the annotation is consumed but never printed.
    /// </para>
    /// <para>
    /// <strong>A placeholder that matches no eligible property throws</strong> rather than being printed
    /// literally: a template/property mismatch (a typo, a renamed property the catalog entry was not
    /// updated for) would otherwise put the raw placeholder text into <see cref="GameState.NewsLog"/> — and
    /// into a save — with no signal to anyone.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A placeholder in <paramref name="template"/> matches no property of
    /// <paramref name="domainEvent"/>'s own declaring type (excluding <see cref="DomainEvent"/> itself).
    /// </exception>
    internal static string RenderMessage(DomainEvent domainEvent, string template)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        ArgumentNullException.ThrowIfNull(template);

        var eventType = domainEvent.GetType();
        var builder = new StringBuilder(template.Length);
        var cursor = 0;

        foreach (Match match in PlaceholderPattern.Matches(template))
        {
            builder.Append(template, cursor, match.Index - cursor);

            var curlyGroup = match.Groups["curly"];
            var isCurly = curlyGroup.Success;
            var propertyName = isCurly ? curlyGroup.Value : match.Groups["angle"].Value;

            // The corpus's angle-bracket tokens are lowercase (<winner>, <nation>); the event's own
            // property is PascalCase (Winner, Nation). The curly-brace tokens already match property
            // casing exactly, so only the angle-bracket form needs a case-insensitive lookup.
            var property = FindOperandProperty(eventType, propertyName, ignoreCase: !isCurly);
            if (property is null)
            {
                throw new InvalidOperationException(
                    $"Event kind '{domainEvent.Kind}' ({eventType.FullName}) has no property matching "
                    + $"template placeholder '{match.Value}'. Add the property to the event, or correct "
                    + "the template in NewsMessageCatalog.");
            }

            var value = property.GetValue(domainEvent);
            builder.Append(value is null
                ? string.Empty
                : string.Format(CultureInfo.InvariantCulture, "{0}", value));

            cursor = match.Index + match.Length;
        }

        builder.Append(template, cursor, template.Length - cursor);
        return builder.ToString();
    }

    /// <summary>
    /// Finds <paramref name="eventType"/>'s public instance property named <paramref name="propertyName"/>,
    /// excluding any property declared directly on <see cref="DomainEvent"/> — see
    /// <see cref="RenderMessage"/>'s remarks for why that exclusion is narrower than
    /// <see cref="BindingFlags.DeclaredOnly"/>.
    /// </summary>
    private static PropertyInfo? FindOperandProperty(Type eventType, string propertyName, bool ignoreCase)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        foreach (var property in eventType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.DeclaringType == typeof(DomainEvent))
            {
                continue;
            }

            if (string.Equals(property.Name, propertyName, comparison))
            {
                return property;
            }
        }

        return null;
    }

    /// <summary>
    /// Truncates <paramref name="text"/> to at most <paramref name="maxBytes"/> UTF-8 bytes, never
    /// splitting a multi-byte character.
    /// </summary>
    /// <remarks>
    /// The original stores each news slot in a fixed 61-byte buffer
    /// (<c>Ruleset.NewsLog.MessageByteLength</c>, confirmed: <c>decompiled-news-log-identified.md</c> and
    /// <c>decompiled-sav-file-layout.md</c>). Truncating by <see cref="string.Length"/> instead of UTF-8
    /// byte count is indistinguishable for the ASCII corpus text this task ships, but would silently
    /// overrun the buffer's actual byte budget for any non-ASCII operand a later task substitutes in
    /// (a nation or city name), so truncation counts bytes here rather than characters.
    /// </remarks>
    private static string TruncateToByteLength(string text, int maxBytes)
    {
        if (maxBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "Message byte length must be non-negative.");
        }

        if (Encoding.UTF8.GetByteCount(text) <= maxBytes)
        {
            return text;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        var length = maxBytes;

        // A UTF-8 continuation byte has the high bits 10xxxxxx; back off until the cut lands on a
        // sequence boundary rather than inside one.
        while (length > 0 && (bytes[length] & 0xC0) == 0x80)
        {
            length--;
        }

        return Encoding.UTF8.GetString(bytes, 0, length);
    }
}

/// <summary>
/// Flushes seat-scoped news at the end of each seat's turn.
/// </summary>
/// <remarks>
/// <para>
/// Reads <see cref="SystemContext.PublishedEvents"/> — T40's seam — filtered to the events published in a
/// seat-scoped phase (<see cref="TurnPhase.SeatStart"/>, <see cref="TurnPhase.Orders"/> or an earlier
/// system in <see cref="TurnPhase.SeatEnd"/> itself), and renders exactly those through
/// <see cref="NewsLogWriter.Append"/>. No reflection over the sink chain, no static field, no buffering
/// sink of its own: everything this system needs arrives on <see cref="SystemContext"/>, so it stays a
/// genuinely stateless, attribute-discovered <see cref="IGameSystem"/> like any other.
/// </para>
/// <para>
/// <strong>Must run last within <see cref="TurnPhase.SeatEnd"/>.</strong> <see cref="SystemContext.PublishedEvents"/>
/// is a snapshot taken before this system runs, so an earlier system's ordering is what makes its events
/// visible here at all. <see cref="GameSystemAttribute.Order"/> is set to <see cref="int.MaxValue"/> —
/// not left at the default and relied on to sort after other <c>SeatEnd</c> systems by id, which is not
/// guaranteed for every id a later task might choose — so this system runs after every other
/// <c>SeatEnd</c> registration regardless of what it is named.
/// <c>NewsLogWriterTests.NewsLogWriter_SeatEnd_RunsAfterAnEarlierSameNamedSystem</c> proves this against a
/// fixture system whose id would otherwise sort before this one's.
/// </para>
/// </remarks>
[GameSystem(TurnPhase.SeatEnd, "news.writer", Order = int.MaxValue)]
public sealed class NewsLogWriterSeatEnd : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var seatScopedEvents = context.PublishedEvents
            .Where(published => TurnPhases.ScopeOf(published.Phase) == TurnPhaseScope.Seat)
            .Select(published => published.Event);

        return NewsLogWriter.Append(context.State, seatScopedEvents, context.Ruleset.NewsLog);
    }
}

/// <summary>
/// Flushes round-scoped news at the end of each completed round of seats.
/// </summary>
/// <remarks>
/// <para>
/// The seat-scoped writer (<see cref="NewsLogWriterSeatEnd"/>) alone would defer a round-scoped event
/// (published in, for example, <see cref="TurnPhase.WeatherEvents"/>) to whichever seat's next turn
/// happens to reach <see cref="TurnPhase.SeatEnd"/> — this system exists so it appears at the round
/// boundary instead, never later.
/// </para>
/// <para>
/// Filters <see cref="SystemContext.PublishedEvents"/> to round-scoped phases only. That filter matters
/// even when <see cref="TurnCoordinator.RunTurn"/> follows a seat's turn straight on into the round tick:
/// in that case this system's own view also includes the seat-scoped events from earlier in the same run
/// (<see cref="SystemContext.PublishedEvents"/>'s own remarks) — which <see cref="NewsLogWriterSeatEnd"/>
/// already rendered a moment earlier in that same run. Without the filter this system would render them a
/// second time; with it, each event is rendered by exactly the writer whose scope it belongs to.
/// </para>
/// <para>
/// <strong>Must run last within <see cref="TurnPhase.RoundEnd"/></strong>, for the same reason and by the
/// same mechanism as <see cref="NewsLogWriterSeatEnd"/> — see its remarks.
/// <c>NewsLogWriterTests.NewsLogWriter_RoundEnd_RunsAfterAnEarlierSameNamedSystem</c> proves it.
/// </para>
/// </remarks>
[GameSystem(TurnPhase.RoundEnd, "news.writer.round", Order = int.MaxValue)]
public sealed class NewsLogWriterRoundEnd : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var roundScopedEvents = context.PublishedEvents
            .Where(published => TurnPhases.ScopeOf(published.Phase) == TurnPhaseScope.Round)
            .Select(published => published.Event);

        return NewsLogWriter.Append(context.State, roundScopedEvents, context.Ruleset.NewsLog);
    }
}
