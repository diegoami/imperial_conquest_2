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
/// <remarks>
/// T06's review follow-up #43 flagged the army and fleet probes below as near-duplicate classes with
/// near-duplicate event records. Both now share <see cref="AttritionProbeSystem"/> and the single
/// <see cref="AttritionProbeFired"/> event kind, distinguished only by
/// <see cref="AttritionProbeFired.ProbeId"/>. The two concrete classes still exist separately because
/// attribute-based registration (<see cref="GameSystemAttribute"/>, <c>AllowMultiple = false</c>) is
/// one attribute per class, and each stands in for a different phase.
/// </remarks>
public static class AttritionProbeFixtures
{
    /// <summary>The fixture group these probes -- and <see cref="LaterTaskStandIns"/> -- belong to.</summary>
    public const string Group = "calendar.attrition-probes";
}

/// <summary>
/// Publishes one <see cref="AttritionProbeFired"/> naming the concrete probe's id and the calendar it
/// observed, and changes no state. Shared by <see cref="ArmyAttritionProbeSystem"/> and
/// <see cref="FleetAttritionProbeSystem"/> below.
/// </summary>
public abstract class AttritionProbeSystem : IGameSystem
{
    private readonly string _probeId;

    /// <param name="probeId">The value published as <see cref="AttritionProbeFired.ProbeId"/>.</param>
    protected AttritionProbeSystem(string probeId) => _probeId = probeId;

    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        var calendar = context.State.Calendar;
        context.Events.Publish(new AttritionProbeFired(
            _probeId, calendar.Week, calendar.SeasonIndex, calendar.YearBc));
        return context.State;
    }
}

/// <summary>Stands in for T08's supply/morale rule, which registers into <see cref="TurnPhase.ArmyTick"/>.</summary>
[TestFixtureGroup(AttritionProbeFixtures.Group)]
[GameSystem(TurnPhase.ArmyTick, "test.calendar.attrition-probe.army")]
public sealed class ArmyAttritionProbeSystem : AttritionProbeSystem
{
    /// <summary>Registered types need a public parameterless constructor (<c>AssemblyScan.Instantiate</c>).</summary>
    public ArmyAttritionProbeSystem() : base("army")
    {
    }
}

/// <summary>Stands in for T14's fleet attrition, which registers into <see cref="TurnPhase.FleetTick"/>.</summary>
[TestFixtureGroup(AttritionProbeFixtures.Group)]
[GameSystem(TurnPhase.FleetTick, "test.calendar.attrition-probe.fleet")]
public sealed class FleetAttritionProbeSystem : AttritionProbeSystem
{
    /// <summary>Registered types need a public parameterless constructor (<c>AssemblyScan.Instantiate</c>).</summary>
    public FleetAttritionProbeSystem() : base("fleet")
    {
    }
}

/// <summary>One attrition probe firing: which probe (<c>"army"</c> or <c>"fleet"</c>) and the calendar
/// it observed, before that round's calendar advance.</summary>
[DomainEvent("test.calendar.attrition-probe.fired")]
public sealed record AttritionProbeFired(
    string ProbeId,
    int ObservedWeek,
    int ObservedSeasonIndex,
    int ObservedYearBc) : DomainEvent;

/// <summary>
/// Bug #50 / T32 DoD 1: truly inert test-local stand-ins for a later task's real system landing in the
/// same phase as <see cref="AttritionProbeFixtures"/>'s probes -- exactly what broke
/// <c>AttritionPhasesAcceptRegistrationWithNeitherT08NorT14Present</c> once T08's real
/// <c>economy.supply-consumption-and-morale</c> system registered into <see cref="TurnPhase.ArmyTick"/>.
/// These publish nothing and change no state, so they cannot affect any assertion except the one they
/// exist to prove: that looking a probe up by id in <see cref="AttritionPhaseOrderingTests"/> still works
/// when the phase holds more than one system.
/// </summary>
public static class LaterTaskStandIns
{
    /// <summary>The army stand-in's id.</summary>
    public const string ArmyId = "test.calendar.later-task-stand-in.army";

    /// <summary>The fleet stand-in's id.</summary>
    public const string FleetId = "test.calendar.later-task-stand-in.fleet";
}

/// <inheritdoc cref="LaterTaskStandIns"/>
[TestFixtureGroup(AttritionProbeFixtures.Group)]
[GameSystem(TurnPhase.ArmyTick, LaterTaskStandIns.ArmyId, Order = 100)]
public sealed class LaterTaskStandInArmySystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context) => context.State;
}

/// <inheritdoc cref="LaterTaskStandIns"/>
[TestFixtureGroup(AttritionProbeFixtures.Group)]
[GameSystem(TurnPhase.FleetTick, LaterTaskStandIns.FleetId, Order = 100)]
public sealed class LaterTaskStandInFleetSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context) => context.State;
}
