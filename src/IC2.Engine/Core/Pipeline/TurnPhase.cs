namespace IC2.Engine.Core;

/// <summary>
/// The engine's phase pipeline, in the order phases run.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This list is closed.</strong> It is declared here once and <em>read</em> by every later task,
/// never edited by one — that is the whole point of
/// <c>docs/build-orchestration-plan.md</c> §2.3: a phase list that later tasks append to would be
/// exactly the shared registry file the parallel plan exists to avoid. Every phase below already has a
/// named consumer in the task catalogue (see each member's remarks). A task that believes it needs a
/// tenth phase should escalate rather than add one.
/// </para>
/// <para>
/// <strong>Where the order comes from.</strong> It follows the original's own turn structure as
/// decompiled in
/// <see href="https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-turn-and-calendar-sequencing.md">
/// decompiled-turn-and-calendar-sequencing.md</see>, not from invention:
/// </para>
/// <code>
/// TPremierForm_StartTurn   -> announces pending trade/alliance proposals   -> SeatStart
/// &lt;the player's or AI's interactive turn&gt;                             -> Orders
/// TPremierForm_EndTurn     -> FUN_0045af00 end-turn validity check,
///                             then nationTurnIndex = (index + 1) mod 16    -> SeatEnd
/// if nationTurnIndex == 0: FUN_004514ec, the global weekly tick:
///     loop all cities: supply production and famine unrest, StateCode += 2
///                      capped at 24                                        -> CityTick
///     loop all armies: seasonal supply consumption, moves recomputed       -> ArmyTick
///     loop all fleets: construction countdown, storms and losses at sea    -> FleetTick
///     FUN_00451304: the seasonal weather-event system                      -> WeatherEvents
///     week/season/year advance                                             -> CalendarAdvance
/// </code>
/// <para>
/// Two departures from that trace, both deliberate and both stated so a reviewer can check them:
/// <see cref="Orders"/> is a phase here where the original has an interactive window rather than a
/// code step (an AI seat needs somewhere to run, and a headless harness needs somewhere to inject
/// commands); and <see cref="RoundEnd"/> has no counterpart in <c>FUN_004514ec</c> at all — the
/// original evaluates its victory condition in <c>THumanFalls_InitializeForm</c>, on a path that report
/// does not place in the tick, so the phase is <strong>[designed]</strong>. What was searched and came
/// up empty: <c>decompiled-turn-and-calendar-sequencing.md</c> (the only report that traces the tick end
/// to end) names the five steps above and stops; no report in the research repo states where the
/// win check is evaluated relative to them.
/// </para>
/// <para>
/// <strong>The calendar advance runs last within the tick, not first — corrected from T03's original
/// order by T06 (see <c>docs/build-orchestration-plan.md</c> §"T06 Calendar and turn sequencing" DoD
/// 6).</strong> T03 originally placed <see cref="CalendarAdvance"/> before <see cref="CityTick"/>,
/// <see cref="ArmyTick"/> and <see cref="FleetTick"/>, and said so explicitly as an unconfirmed guess:
/// "It does not state outright that the calendar advance precedes the three loops... Flagged here
/// rather than asserted silently." <c>docs/investigations/thracia-supply-morale.md</c> — a pass
/// commissioned after T03 merged, specifically to trace the supply→morale rule — settles it the other
/// way, from the same decompiled dump T03 could not fully resolve: "The calendar update at the end of
/// the tick is <c>week = (week + 2) % 12</c>" (line offsets 54663-54677, versus the army loop at
/// 54501-54529), and "<c>FUN_00451304</c> [the weather system]... the only other function the tick
/// calls <em>between the army loop and the calendar update</em>." <c>docs/design-audit.md</c> §2.9a
/// states the same order in prose: "the original's tick <c>FUN_004514ec</c> runs its army loop, then
/// its fleet loop, then the weather system, and only then updates the calendar." Getting this backwards
/// is not cosmetic: seasonal population growth, seasonal army supply consumption and the weather system
/// all read the season value, and T08's per-season consumption figures are wrong by 7× if they read the
/// season <em>after</em> it has already advanced rather than the one the turn actually ran under. The
/// <see cref="TurnPhases.Ordered"/> array below reflects the corrected order; the enum's underlying
/// integer values were renumbered alongside it so the numbers do not keep telling the old, wrong story.
/// </para>
/// </remarks>
public enum TurnPhase
{
    /// <summary>
    /// The active seat's turn begins. The original announces pending trade and alliance proposals here
    /// (<c>TPremierForm_StartTurn</c>). Consumer: T19 diplomacy.
    /// </summary>
    SeatStart = 10,

    /// <summary>
    /// The active seat acts: this is where commands are issued and where an AI seat decides.
    /// Consumers: T09 movement, T13 recruitment, T14 naval, T16 battle, T17 capture, T18 city orders,
    /// T22 AI, T23's headless harness.
    /// </summary>
    Orders = 20,

    /// <summary>
    /// The active seat's turn ends: the original's end-turn validity check (<c>FUN_0045af00</c>, which
    /// refuses to end a turn while units can still act) and the seat rotation that follows it.
    /// Consumer: T06 calendar and turn sequencing, which owns the rotation rule itself.
    /// </summary>
    SeatEnd = 30,

    /// <summary>
    /// Round scope. Every city: weekly supply production and famine unrest, and the city-unit
    /// <c>StateCode</c> step. Consumers: T37 economy, T13 recruitment, T18 city orders.
    /// </summary>
    CityTick = 40,

    /// <summary>
    /// Round scope. Every army: seasonal supply consumption (different at a city and in the field) and
    /// the weekly recomputation of available moves — the attrition phase T08's supply/morale rule
    /// registers into. Runs against the season that is ending, before <see cref="CalendarAdvance"/>
    /// moves it on; see this enum's remarks. Consumers: T08 economy, T09 movement.
    /// </summary>
    ArmyTick = 50,

    /// <summary>
    /// Round scope. Every fleet: the construction countdown and its completion, and the storm/lost-at-sea
    /// rolls for fleets already at sea — the attrition phase T14's fleet attrition registers into. Also
    /// runs before <see cref="CalendarAdvance"/>; see this enum's remarks. Consumer: T14 naval.
    /// </summary>
    FleetTick = 60,

    /// <summary>
    /// Round scope. The seasonal weather-event system (<c>FUN_00451304</c>). Consumer: T08 economy.
    /// </summary>
    WeatherEvents = 70,

    /// <summary>
    /// Round scope. The week/season/year advance: <c>week = (week + 2) mod 12</c>, season on the 11→1
    /// wrap, year decrement on the Winter→Spring wrap. Runs <em>last</em> in the global tick — after
    /// <see cref="CityTick"/>, <see cref="ArmyTick"/>, <see cref="FleetTick"/> and
    /// <see cref="WeatherEvents"/>, not before them; see this enum's remarks. The quarterly hook fires
    /// from inside this phase — see <see cref="IQuarterBoundaryHandler"/>. Consumer: T06.
    /// </summary>
    CalendarAdvance = 80,

    /// <summary>
    /// Round scope, <strong>[designed]</strong> — see this enum's remarks. Everything that must be
    /// evaluated once a full round has been played out: victory conditions and nation elimination.
    /// Consumers: T12 victory, T17 capture's elimination cascade.
    /// </summary>
    RoundEnd = 90,
}

/// <summary>Whether a phase runs for one seat's turn or once per full round of seats.</summary>
public enum TurnPhaseScope
{
    /// <summary>Runs once per seat, every turn, for the seat whose turn it is.</summary>
    Seat,

    /// <summary>
    /// Runs once after every seat has taken its turn — the original's global weekly tick, which
    /// <c>decompiled-turn-and-calendar-sequencing.md</c> confirms loops every city, army and fleet in the
    /// game rather than only the ending nation's.
    /// </summary>
    Round,
}

/// <summary>
/// The one place the phase order is stated. Later tasks read <see cref="InOrder"/>; nothing outside
/// this file decides what runs when.
/// </summary>
public static class TurnPhases
{
    private static readonly TurnPhase[] Ordered =
    {
        TurnPhase.SeatStart,
        TurnPhase.Orders,
        TurnPhase.SeatEnd,
        TurnPhase.CityTick,
        TurnPhase.ArmyTick,
        TurnPhase.FleetTick,
        TurnPhase.WeatherEvents,
        TurnPhase.CalendarAdvance,
        TurnPhase.RoundEnd,
    };

    /// <summary>Every phase, in execution order.</summary>
    public static IReadOnlyList<TurnPhase> InOrder => Ordered;

    /// <summary>The phases that run for a single seat's turn, in execution order.</summary>
    public static IReadOnlyList<TurnPhase> SeatScoped { get; } =
        Ordered.Where(phase => ScopeOf(phase) == TurnPhaseScope.Seat).ToArray();

    /// <summary>The phases that run once per completed round of seats, in execution order.</summary>
    public static IReadOnlyList<TurnPhase> RoundScoped { get; } =
        Ordered.Where(phase => ScopeOf(phase) == TurnPhaseScope.Round).ToArray();

    /// <summary>Whether a phase runs per seat or per round.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a declared phase.</exception>
    public static TurnPhaseScope ScopeOf(TurnPhase phase) => phase switch
    {
        TurnPhase.SeatStart or TurnPhase.Orders or TurnPhase.SeatEnd => TurnPhaseScope.Seat,
        TurnPhase.CalendarAdvance
            or TurnPhase.CityTick
            or TurnPhase.ArmyTick
            or TurnPhase.FleetTick
            or TurnPhase.WeatherEvents
            or TurnPhase.RoundEnd => TurnPhaseScope.Round,
        _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Not a declared turn phase."),
    };

    /// <summary>Where a phase sits in the declared order, counting from zero.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a declared phase.</exception>
    public static int PositionOf(TurnPhase phase)
    {
        for (var i = 0; i < Ordered.Length; i++)
        {
            if (Ordered[i] == phase)
            {
                return i;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(phase), phase, "Not a declared turn phase.");
    }
}
