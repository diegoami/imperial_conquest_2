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
/// <see cref="Append"/>, and nothing else writes to the news log. A command dispatched outside a run
/// (T23's human orders, via <see cref="CommandDispatcher"/>'s two-argument <c>Dispatch</c>) never
/// reaches <see cref="SystemContext.PublishedEvents"/>, and neither does
/// <see cref="TurnCoordinator.FireQuarterBoundary"/> called directly — both bypass it the same way, so
/// a caller of either routes its own <see cref="CommandResult.Events"/> (or its own published events)
/// through this public static method itself, which is exactly why it takes plain data rather than
/// something reachable only through <see cref="IGameSystem.Execute"/>.
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
    /// <remarks>
    /// An event whose kind is <see cref="NewsMessageCatalog.IsWrappedInDashLines"/> renders as three
    /// entries instead of one — a <see cref="NewsMessageCatalog.DashLineKind"/> line, the message, then
    /// the dash line again — matching the original's elimination banner
    /// (news-log-format-and-messages.md Q4 #10-12, confirmed 2 of 2).
    /// </remarks>
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

            if (NewsMessageCatalog.IsWrappedInDashLines(domainEvent.Kind))
            {
                var dash = TruncateToByteLength(
                    NewsMessageCatalog.GetTemplate(NewsMessageCatalog.DashLineKind), rules.MessageByteLength);
                newsLog = newsLog.Append(new NewsEntry(dash), rules);
                newsLog = newsLog.Append(new NewsEntry(truncated), rules);
                newsLog = newsLog.Append(new NewsEntry(dash), rules);
            }
            else
            {
                newsLog = newsLog.Append(new NewsEntry(truncated), rules);
            }
        }

        return state with { NewsLog = newsLog };
    }

    /// <summary>
    /// Appends the round tick's mandatory closing pair — a single blank entry, then the week header —
    /// after every other message the round produced. Unconditional every round, whether or not any other
    /// event fired that round: news-log-format-and-messages.md Q3 confirms "a quiet round costs 2 of the
    /// 40 slots".
    /// </summary>
    /// <param name="state">
    /// The state to append to. Its own <see cref="GameState.Calendar"/> supplies the header's week,
    /// season and year, so a caller must call this only after the calendar's own advance for that round
    /// — <see cref="TurnPhase.CalendarAdvance"/> precedes <see cref="TurnPhase.RoundEnd"/> in the declared
    /// phase order, and <see cref="NewsLogWriterRoundEnd"/> relies on exactly that ordering. This matches
    /// the original's own step order: the header names the week that is <em>starting</em>, not the one
    /// that just ended (Q3: storm, fleet-completion and deposition lines all come first in the tick, and
    /// "everything a seat does in the new round follows the new header").
    /// </param>
    /// <param name="rules">The ring-buffer geometry, message length and season-name table.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="state"/>'s season index has no matching entry in
    /// <see cref="NewsLogRules.SeasonNames"/>.
    /// </exception>
    public static GameState AppendRoundHeader(GameState state, NewsLogRules rules)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rules);

        var calendar = state.Calendar;
        var seasonName = ResolveSeasonName(rules.SeasonNames, calendar.SeasonIndex);
        var header = RenderWeekHeader(calendar.Week, seasonName, calendar.YearBc);
        var blank = NewsMessageCatalog.GetTemplate(NewsMessageCatalog.BlankLineKind);

        var newsLog = state.NewsLog
            .Append(new NewsEntry(TruncateToByteLength(blank, rules.MessageByteLength)), rules)
            .Append(new NewsEntry(TruncateToByteLength(header, rules.MessageByteLength)), rules);

        return state with { NewsLog = newsLog };
    }

    /// <summary>
    /// Renders the round tick's week header from <see cref="NewsMessageCatalog.WeekHeaderKind"/>'s
    /// stored template (<c>"Week  &lt;w&gt;      &lt;Season&gt;      &lt;year&gt;BC"</c>,
    /// news-log-format-and-messages.md Q3), substituting its three tokens directly rather than through
    /// <see cref="RenderMessage"/>'s reflection-based lookup — this line has no backing
    /// <see cref="DomainEvent"/> for that lookup to reflect over.
    /// </summary>
    /// <param name="week">The calendar week, after that round's own advance.</param>
    /// <param name="seasonName">The season name, after that round's own advance.</param>
    /// <param name="yearBc">The calendar year BC, after that round's own advance.</param>
    /// <returns>
    /// The rendered header, e.g. <c>"Week  3      Spring      270BC"</c>. Week and year are plain
    /// <see cref="int.ToString()"/>, never comma-grouped — only <see cref="FormatGroupedAmount"/>'s
    /// reparations amount is (Q2).
    /// </returns>
    public static string RenderWeekHeader(int week, string seasonName, int yearBc)
    {
        ArgumentNullException.ThrowIfNull(seasonName);

        var template = NewsMessageCatalog.GetTemplate(NewsMessageCatalog.WeekHeaderKind);
        return template
            .Replace("<w>", week.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("<Season>", seasonName, StringComparison.Ordinal)
            .Replace("<year>", yearBc.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    /// <summary>
    /// Formats <paramref name="amount"/> the way the original's own number formatter renders the
    /// reparations line — a comma every three digits, no locale, no decimals
    /// (news-log-format-and-messages.md Q2: <c>FormStripNum1</c>/<c>FUN_00448F3C</c>, confirmed against
    /// five observed settlements, e.g. "... pays reparations of 2,269 talents."). No other operand in
    /// the log is ever grouped ("Only reparations are grouped. Week and year go through plain Str()").
    /// </summary>
    /// <remarks>
    /// The emitting task (T19, for the reparations event) calls this when building its own event's
    /// amount property, so the value substituted into <c>peace.pays-reparations</c>'s <c>&lt;n&gt;</c>
    /// placeholder already carries the comma — <see cref="RenderMessage"/>'s generic placeholder
    /// substitution stays a plain <c>ToString()</c> call, never a special case keyed to one property
    /// name. This mirrors how <see cref="ApplyWarDeclarationShouting"/> is a separate call the emitter
    /// makes with its own human flag, rather than a rule baked into the renderer.
    /// </remarks>
    public static string FormatGroupedAmount(int amount) => amount.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>
    /// Applies the original's war-declaration shouting rule: the whole message is uppercased, ASCII
    /// <c>a-z</c> only, when either nation in the declaration is human; alliance lines are never
    /// uppercased (news-log-format-and-messages.md Q2: <c>FUN_00449A44</c>'s <c>StrUpper</c> call,
    /// confirmed against 7 of 7 observed war declarations — all lowercase, because all are between AI
    /// nations; the uppercase form has never been observed in a save).
    /// </summary>
    /// <param name="renderedWarDeclaration">An already-rendered <c>war.declared</c> message.</param>
    /// <param name="involvesHuman">Whether either nation in the declaration is human-controlled.</param>
    /// <remarks>
    /// Uppercases only ASCII <c>a-z</c>, matching the original's own <c>StrUpper</c> exactly, rather
    /// than <see cref="string.ToUpperInvariant"/> — which would also uppercase non-ASCII letters the
    /// original's single-byte routine never touches. In practice every shipped name is ASCII, so the
    /// two never differ for real data; this keeps the two identical by construction rather than by luck.
    /// </remarks>
    public static string ApplyWarDeclarationShouting(string renderedWarDeclaration, bool involvesHuman)
    {
        ArgumentNullException.ThrowIfNull(renderedWarDeclaration);

        if (!involvesHuman)
        {
            return renderedWarDeclaration;
        }

        var chars = renderedWarDeclaration.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] >= 'a' && chars[i] <= 'z')
            {
                chars[i] = (char)(chars[i] - ('a' - 'A'));
            }
        }

        return new string(chars);
    }

    /// <summary>
    /// Every placeholder in <paramref name="template"/> that has no matching property on
    /// <paramref name="eventType"/> — empty if the template and the type agree.
    /// </summary>
    /// <remarks>
    /// Lets a coverage test (<c>NewsCoverageTests</c>, #91 N17/N28) prove a news-worthy event's template
    /// will actually render — before any production event of that kind is ever constructed — by
    /// checking the exact same lookup <see cref="RenderMessage"/> uses at rendering time, never a second,
    /// independently written check that could itself drift from the real lookup rule.
    /// </remarks>
    /// <param name="eventType">The event type the template will render against.</param>
    /// <param name="template">The message template.</param>
    /// <exception cref="AmbiguousMatchException">
    /// A placeholder matches two of <paramref name="eventType"/>'s properties that differ only by case
    /// (see <see cref="RenderMessage"/>'s remarks on <see cref="FindOperandProperty"/>).
    /// </exception>
    public static IReadOnlyList<string> UnresolvedPlaceholders(Type eventType, string template)
    {
        ArgumentNullException.ThrowIfNull(eventType);
        ArgumentNullException.ThrowIfNull(template);

        var missing = new List<string>();
        foreach (Match match in PlaceholderPattern.Matches(template))
        {
            var curlyGroup = match.Groups["curly"];
            var isCurly = curlyGroup.Success;
            var propertyName = isCurly ? curlyGroup.Value : match.Groups["angle"].Value;

            if (FindOperandProperty(eventType, propertyName, ignoreCase: !isCurly) is null)
            {
                missing.Add(match.Value);
            }
        }

        return missing;
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
    /// <para>
    /// <strong>An angle-bracket token that matches two properties differing only by case throws too</strong>
    /// (<see cref="FindOperandProperty"/>, #91 N26) — <see cref="System.Reflection.PropertyInfo.GetProperties()"/>'s
    /// order among such a pair is unspecified, so silently picking one would be an order-dependent result
    /// masquerading as a deterministic one.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A placeholder in <paramref name="template"/> matches no property of
    /// <paramref name="domainEvent"/>'s own declaring type (excluding <see cref="DomainEvent"/> itself).
    /// </exception>
    /// <exception cref="AmbiguousMatchException">
    /// An angle-bracket placeholder matches two properties that differ only by case.
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
    /// <exception cref="AmbiguousMatchException">
    /// <paramref name="ignoreCase"/> is <see langword="true"/> and two properties differing only by case
    /// both match <paramref name="propertyName"/> (#91 N26). Never possible when
    /// <paramref name="ignoreCase"/> is <see langword="false"/>: two properties on the same type cannot
    /// share an exactly-equal name.
    /// </exception>
    private static PropertyInfo? FindOperandProperty(Type eventType, string propertyName, bool ignoreCase)
    {
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        PropertyInfo? match = null;

        foreach (var property in eventType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.DeclaringType == typeof(DomainEvent))
            {
                continue;
            }

            if (!string.Equals(property.Name, propertyName, comparison))
            {
                continue;
            }

            if (match is not null)
            {
                throw new AmbiguousMatchException(
                    $"Placeholder token for '{propertyName}' matches more than one property on "
                    + $"'{eventType.FullName}' that differ only by case ('{match.Name}' and "
                    + $"'{property.Name}'). Rename one of them so the match is unambiguous.");
            }

            match = property;
        }

        return match;
    }

    /// <summary>
    /// Looks up <paramref name="seasonIndex"/> in <paramref name="seasonNames"/>, throwing rather than
    /// defaulting when the calendar's season index has no matching entry.
    /// </summary>
    private static string ResolveSeasonName(IReadOnlyList<string> seasonNames, int seasonIndex)
    {
        if (seasonIndex < 0 || seasonIndex >= seasonNames.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seasonIndex), seasonIndex,
                "The calendar's season index has no matching entry in NewsLogRules.SeasonNames.");
        }

        return seasonNames[seasonIndex];
    }

    /// <summary>
    /// Truncates <paramref name="text"/> to at most <c>maxBytes - 1</c> single-byte ASCII characters —
    /// the slot's own byte budget, minus the byte the writer always reserves for the NUL terminator.
    /// </summary>
    /// <remarks>
    /// The original stores each news slot as a NUL-terminated, single-byte string exactly
    /// <c>Ruleset.NewsLog.MessageByteLength</c> (61) bytes wide, with no truncation and no encoding
    /// conversion of its own — every message the game can build already fits
    /// (news-log-format-and-messages.md Q1: "confirmed: 2,101 slots in 54 saves, every one
    /// NUL-terminated, longest 59, every byte 0x20-0x7E"). A byte outside that printable-ASCII range
    /// signals an upstream bug — every shipped nation, city and leader name is pure ASCII — rather than
    /// something to silently transcode, so this <strong>rejects</strong> it by throwing instead of
    /// falling back to UTF-8 or a best-fit replacement, either of which could write a different byte
    /// sequence than the original's single-byte code page ever would have (#91 N15). Searched: the
    /// report's own "Encoding" note (Q1) and its "What an implementation needs" summary; neither
    /// describes a fallback for a byte outside 0x20-0x7E, both describe the shipped data as never
    /// needing one.
    /// </remarks>
    private static string TruncateToByteLength(string text, int maxBytes)
    {
        if (maxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxBytes), maxBytes,
                "A news slot must be at least one byte, to hold the writer's own NUL terminator.");
        }

        foreach (var ch in text)
        {
            if (ch < 0x20 || ch > 0x7E)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(text), ch,
                    "News message text must be printable single-byte ASCII (0x20-0x7E) -- "
                    + "news-log-format-and-messages.md Q1's confirmed byte range. Every shipped nation, "
                    + "city and leader name is ASCII; a non-ASCII operand signals an upstream bug rather "
                    + "than something to silently transcode.");
            }
        }

        // Every character above is confirmed single-byte ASCII, so string.Length is exactly the byte
        // count -- no separate byte-counting pass needed. The slot's last byte is always the writer's
        // own NUL (Q1), so at most maxBytes - 1 bytes of text ever reach the log.
        var maxTextBytes = maxBytes - 1;
        return text.Length <= maxTextBytes ? text : text[..maxTextBytes];
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
/// <para>
/// <strong>The tie-break itself is ordinal id comparison</strong> (<c>SystemRegistry.FromAssemblies</c>'s
/// sort: phase, then <c>Order</c>, then <c>string.CompareOrdinal(id, id)</c>) — so a <em>different</em>
/// future <c>SeatEnd</c> system also declared at <c>Order = int.MaxValue</c>, with an id that sorts after
/// <c>"news.writer"</c> ordinally, would run <em>after</em> this writer, and any event it published that
/// same tick would never be rendered (#91 N18). <c>NewsLogWriterTests.NewsWriter_IsLast_InSeatEnd_AcrossTheWholeEngineAssembly</c>
/// pins "runs last" against the real, whole engine assembly (not a test fixture) so such a collision
/// fails CI the moment it is introduced, rather than being discovered later as a missing news line.
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
/// Flushes round-scoped news at the end of each completed round of seats, then closes the round with
/// its mandatory blank-line-and-week-header pair.
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
/// <strong>Then, unconditionally, <see cref="NewsLogWriter.AppendRoundHeader"/></strong> — the blank
/// entry and week header every round tick ends with, whether or not any round-scoped event fired this
/// round (news-log-format-and-messages.md Q3). This runs in <see cref="TurnPhase.RoundEnd"/>, which
/// follows <see cref="TurnPhase.CalendarAdvance"/> in the declared phase order, so the calendar this
/// system reads off <see cref="SystemContext.State"/> already reflects that round's own advance.
/// </para>
/// <para>
/// <strong>Must run last within <see cref="TurnPhase.RoundEnd"/></strong>, for the same reason and by the
/// same mechanism as <see cref="NewsLogWriterSeatEnd"/> — see its remarks, including the ordinal
/// tie-break's own risk (#91 N18) and the whole-engine-assembly pin,
/// <c>NewsLogWriterTests.NewsWriterRound_IsLast_InRoundEnd_AcrossTheWholeEngineAssembly</c>.
/// <c>NewsLogWriterTests.NewsWriter_RoundEnd_RunsAfterAnEarlierSameNamedSystem</c> proves it against a
/// fixture.
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

        var afterEvents = NewsLogWriter.Append(context.State, roundScopedEvents, context.Ruleset.NewsLog);
        return NewsLogWriter.AppendRoundHeader(afterEvents, context.Ruleset.NewsLog);
    }
}
