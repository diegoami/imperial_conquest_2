using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Calendar;

/// <summary>
/// Advances the calendar once per completed round: week <c>+2 mod 12</c>, season on the 11→1 wrap,
/// year on the Winter→Spring wrap, and the quarterly hook fired from inside this system, before the
/// season index itself moves — <c>docs/game-design.md</c> §"Calendar and turns" and
/// <c>docs/build-orchestration-plan.md</c> "T06 Calendar and turn sequencing".
/// </summary>
/// <remarks>
/// <para>
/// This is the real implementation of the stand-in T03's own tests built in its place —
/// <c>QuarterFiringCalendarSystem</c> in
/// <c>tests/IC2.Engine.Tests/Core/QuarterBoundaryFixtures.cs</c>, whose doc comment says exactly that:
/// "Stands in for T06's calendar". The wrap check and the hook-then-advance ordering here match that
/// stand-in deliberately, so the two paths agree on when the boundary fires relative to the season
/// counter.
/// </para>
/// <para>
/// Runs in <see cref="TurnPhase.CalendarAdvance"/>, which this task's own fix to
/// <c>src/IC2.Engine/Core/Pipeline/TurnPhase.cs</c> moved to the <em>end</em> of the round-scoped
/// phases (see that file's remarks for the evidence): after <see cref="TurnPhase.CityTick"/>,
/// <see cref="TurnPhase.ArmyTick"/>, <see cref="TurnPhase.FleetTick"/> and
/// <see cref="TurnPhase.WeatherEvents"/> have all read the season that is <em>ending</em>, not the one
/// this system is about to advance to.
/// </para>
/// <para>
/// Every constant this system reads comes from <see cref="CalendarRules"/>, never a C# literal.
/// </para>
/// </remarks>
[GameSystem(TurnPhase.CalendarAdvance, "calendar.advance-week")]
public sealed class CalendarSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var rules = context.Ruleset.Calendar;
        var before = context.State.Calendar;

        // The wrap check reads the week as it stood before this round's step: "week == 11" means this
        // round's advance is the one that wraps 11 -> 1, not that it already has. Same convention as
        // T03's QuarterFiringCalendarSystem stand-in.
        var wrapsThisRound = before.Week == rules.SeasonAdvanceFromWeek;
        var week = (before.Week + rules.WeekStep) % rules.WeekModulus;

        if (!wrapsThisRound)
        {
            return context.State with
            {
                Calendar = before with { Week = week, TurnIndex = before.TurnIndex + 1 },
            };
        }

        // The hook fires while the season counter still reads the season that is ending, and strictly
        // before the season index itself advances -- exactly what IQuarterBoundaryHandler's remarks
        // require ("right before the season counter itself advances").
        var endingSeasonIndex = before.SeasonIndex;
        var afterBoundary = context.QuarterBoundary.Fire(context.State, endingSeasonIndex);

        var nextSeasonIndex = (endingSeasonIndex + 1) % rules.SeasonsPerYear;
        var wrapsToNewYear = nextSeasonIndex == 0;
        var yearBc = afterBoundary.Calendar.YearBc - (wrapsToNewYear ? 1 : 0);

        return afterBoundary with
        {
            Calendar = afterBoundary.Calendar with
            {
                Week = week,
                SeasonIndex = nextSeasonIndex,
                YearBc = yearBc,
                TurnIndex = afterBoundary.Calendar.TurnIndex + 1,
            },
        };
    }
}
