using IC2.Engine.Model;

namespace IC2.Engine.Calendar;

/// <summary>
/// The city-unit <c>StateCode</c> step: a per-week readiness counter that climbs by a fixed amount and
/// holds at a cap once reached — never resetting or wrapping
/// (<c>docs/build-orchestration-plan.md</c> "T06 Calendar and turn sequencing", Scope: "City-unit
/// <c>StateCode</c> +2/week capped at 24"; T04 fixtures corpus ids
/// <c>calendar.stateCodeIncrementPerWeek</c> / <c>calendar.stateCodeCap</c>, both
/// <c>[confirmed: decompiled-turn-and-calendar-sequencing.md]</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is the step rule, not the storage.</strong> The corpus entry
/// <c>mobilization.stateCodeObservedSequence</c> places this counter on a city's <em>active
/// recruitment slot</em> ("Garrison unit's StateCode observed incrementing by 2 per turn"), and the
/// standing-recruitment-slot model belongs to T13
/// (<c>docs/build-orchestration-plan.md</c> "T13 Recruitment and mercenaries") — it does not exist on
/// <see cref="CityState"/> yet, and T06's Owns list does not cover <c>src/IC2.Engine/Model/**</c> to
/// add it. T06 owns the same thing here it owns for the attrition phase: <em>when</em> and <em>how
/// much</em>, not <em>what</em> holds the value. T13 calls <see cref="Advance"/> once per active slot,
/// per week, when it wires the model this belongs on.
/// </para>
/// <para>Every number here is read from <see cref="CalendarRules"/>, never a C# literal.</para>
/// </remarks>
public static class CityUnitStateCode
{
    /// <summary>
    /// Advances one <c>StateCode</c> value by one week's step, clamped so it holds at the ruleset's cap
    /// rather than exceeding it. A value already at or above the cap stays exactly at the cap — the cap
    /// is a ceiling a value reaches and holds at, never a value that resets it back down.
    /// </summary>
    /// <param name="current">The value before this week's step.</param>
    /// <param name="calendar">The ruleset's calendar rules, supplying the step and the cap.</param>
    public static int Advance(int current, CalendarRules calendar)
    {
        ArgumentNullException.ThrowIfNull(calendar);
        var stepped = current + calendar.CityUnitStateCodeStep;
        return Math.Min(stepped, calendar.CityUnitStateCodeCap);
    }
}
