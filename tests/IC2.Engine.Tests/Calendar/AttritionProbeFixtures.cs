using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Core;

namespace IC2.Engine.Tests.Calendar;

/// <summary>
/// No-op stand-ins for T08's supply/morale rule and T14's fleet attrition — DoD 6/7's "two no-op probe
/// systems registered into the attrition phase". Each publishes what it observed and changes no state,
/// so a test can prove <em>where</em> the attrition phase sits in the turn order (before
/// <c>calendar.advance-week</c>, reading the season that has not yet advanced) with neither T08 nor T14
/// present in the codebase at all.
/// </summary>
public static class AttritionProbeFixtures
{
    /// <summary>The fixture group these two probes belong to.</summary>
    public const string Group = "calendar.attrition-probes";
}

/// <summary>Stands in for T08's supply/morale rule, which registers into <see cref="TurnPhase.ArmyTick"/>.</summary>
[TestFixtureGroup(AttritionProbeFixtures.Group)]
[GameSystem(TurnPhase.ArmyTick, "test.calendar.attrition-probe.army")]
public sealed class ArmyAttritionProbeSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        var calendar = context.State.Calendar;
        context.Events.Publish(new ArmyAttritionProbeFired(
            "army", calendar.Week, calendar.SeasonIndex, calendar.YearBc));
        return context.State;
    }
}

/// <summary>The army probe's own event kind, distinct from the fleet probe's.</summary>
[DomainEvent("test.calendar.attrition-probe.army-fired")]
public sealed record ArmyAttritionProbeFired(
    string ProbeId,
    int ObservedWeek,
    int ObservedSeasonIndex,
    int ObservedYearBc) : DomainEvent;

/// <summary>Stands in for T14's fleet attrition, which registers into <see cref="TurnPhase.FleetTick"/>.</summary>
[TestFixtureGroup(AttritionProbeFixtures.Group)]
[GameSystem(TurnPhase.FleetTick, "test.calendar.attrition-probe.fleet")]
public sealed class FleetAttritionProbeSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        var calendar = context.State.Calendar;
        context.Events.Publish(new FleetAttritionProbeFired(
            "fleet", calendar.Week, calendar.SeasonIndex, calendar.YearBc));
        return context.State;
    }
}

/// <summary>The fleet probe's own event kind, distinct from the army probe's.</summary>
[DomainEvent("test.calendar.attrition-probe.fleet-fired")]
public sealed record FleetAttritionProbeFired(
    string ProbeId,
    int ObservedWeek,
    int ObservedSeasonIndex,
    int ObservedYearBc) : DomainEvent;
