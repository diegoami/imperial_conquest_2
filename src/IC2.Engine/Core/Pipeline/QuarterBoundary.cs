using IC2.Engine.Model;

namespace IC2.Engine.Core;

/// <summary>
/// The <c>OnQuarterBoundary</c> hook: everything that happens once per season, in one place, fired by
/// the calendar and subscribed to by whoever needs it.
/// </summary>
/// <remarks>
/// <para>
/// <strong>When it fires, exactly.</strong>
/// <see href="https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-quarterly-billing-and-economy.md">
/// decompiled-quarterly-billing-and-economy.md</see> pins this down: <c>FUN_00451b40</c> runs "exactly
/// once per season (at the week-11-to-1 wrap, <em>right before the season counter itself advances</em>)".
/// So the hook fires <em>inside</em> <see cref="TurnPhase.CalendarAdvance"/>, on the turn whose week
/// wraps, and a handler sees <see cref="QuarterBoundaryContext.EndingSeasonIndex"/> — the season that is
/// finishing, not the one about to start. Anything seasonal (the season-indexed rate tables, the winter
/// weather weighting) must read that value and not <c>state.Calendar.SeasonIndex</c>, which may already
/// have moved by the time the handler runs in a future refactor.
/// </para>
/// <para>
/// <strong>Why a hook rather than another phase.</strong> Three separate tasks subscribe (T08's quarterly
/// billing, T19's diplomatic thaw, and T13's mercenary-pool refresh) while exactly one task fires it
/// (T06's calendar), and none of them merge at the same time. A phase would have made the firing
/// condition — the week wrap — a rule living outside the calendar; a hook keeps the condition with the
/// calendar and the reactions with their own systems, and lets a test fire it with no calendar
/// implementation present at all. That last property is this task's Definition of Done item 5:
/// <see cref="TurnCoordinator.FireQuarterBoundary"/> is callable directly.
/// </para>
/// </remarks>
public interface IQuarterBoundaryHandler
{
    /// <summary>Handles one quarter boundary, returning the resulting state.</summary>
    /// <param name="context">The state, the rules, the ending season, and this handler's random stream.</param>
    GameState OnQuarterBoundary(QuarterBoundaryContext context);
}

/// <summary>
/// Marks an <see cref="IQuarterBoundaryHandler"/> for assembly-scanned registration. Same mechanism, and
/// same reasoning, as <see cref="GameSystemAttribute"/>: subscribing to the quarter boundary must not
/// require editing a file another task also edits.
/// </summary>
/// <remarks>
/// A class may carry both this and <see cref="GameSystemAttribute"/> when it both runs in a phase and
/// reacts to the boundary; the two registrations are independent, and each gets its own random stream.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class QuarterBoundaryHandlerAttribute : Attribute
{
    /// <summary>Declares a quarter-boundary subscriber.</summary>
    /// <param name="id">A stable, globally unique id in <c>area.name</c> form.</param>
    public QuarterBoundaryHandlerAttribute(string id) => Id = SystemId.Validated(id, nameof(id));

    /// <summary>The subscriber's stable id, which is also its random stream name.</summary>
    public string Id { get; }

    /// <summary>Position among subscribers; lower runs first, ties broken by <see cref="Id"/>.</summary>
    public int Order { get; init; }
}

/// <summary>What a quarter-boundary handler is given.</summary>
/// <param name="State">The state as the previous handler in the chain left it.</param>
/// <param name="Ruleset">The loaded ruleset — the source of every number a handler uses.</param>
/// <param name="World">The loaded world, for terrain and static city/nation data.</param>
/// <param name="EndingSeasonIndex">
/// The zero-based index of the season that is <em>finishing</em>. See
/// <see cref="IQuarterBoundaryHandler"/> for why this is passed rather than read from the calendar.
/// </param>
/// <param name="Rng">This handler's own random stream, independent of every other handler's.</param>
/// <param name="Events">Where the handler publishes anything the news log or the UI should see.</param>
public sealed record QuarterBoundaryContext(
    GameState State,
    Ruleset Ruleset,
    World World,
    int EndingSeasonIndex,
    IRng Rng,
    IEventSink Events);

/// <summary>
/// The firing end of the hook, handed to a system through <see cref="SystemContext.QuarterBoundary"/>
/// so that the calendar can fire it from inside its own phase, at the exact moment the original does.
/// </summary>
public interface IQuarterBoundaryHook
{
    /// <summary>
    /// Runs every registered subscriber, in declared order, threading the state through them.
    /// </summary>
    /// <param name="state">The state at the moment of the boundary.</param>
    /// <param name="endingSeasonIndex">The zero-based index of the season that is finishing.</param>
    /// <returns>The state after every subscriber has run. With no subscribers, <paramref name="state"/>.</returns>
    GameState Fire(GameState state, int endingSeasonIndex);
}
