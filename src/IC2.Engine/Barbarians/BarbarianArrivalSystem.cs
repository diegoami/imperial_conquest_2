using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;

namespace IC2.Engine.Barbarians;

/// <summary>Designed reinforcement entry, narrated through the existing news event pipeline.</summary>
[DomainEvent("barbarians.arrived", NewsWorthy = true)]
public sealed record BarbarianArrivalEntered(string Approach, int X, int Y) : DomainEvent;

/// <summary>One deterministic reinforcement attempt at the existing seasonal boundary.</summary>
[QuarterBoundaryHandler("barbarians.arrivals", Order = 1000)]
public sealed class BarbarianArrivalSystem : IQuarterBoundaryHandler
{
    /// <inheritdoc/>
    public GameState OnQuarterBoundary(QuarterBoundaryContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Scenario?.Barbarians is not { } config) return context.State;
        return Apply(context.State, context.World, context.Ruleset, context.Scenario, context.Rng, context.Events);
    }

    /// <summary>Persist the attempt before rolling; cap/no-entry/chance outcomes cannot reroll on reload.</summary>
    public static GameState Apply(GameState state, World world, Ruleset ruleset, Scenario scenario, IRng rng, IEventSink events)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(events);
        if (scenario.Barbarians is not { } config) return state;
        BarbarianFaction.ValidateScenario(world, ruleset, scenario);
        var nation = state.NationById(config.NationId);
        if (nation is not { IsBarbarian: true, Eliminated: false, Control: SeatControl.Ai })
            throw new ArgumentException("The scenario's barbarian faction is missing or invalid in the saved state.", nameof(state));
        var turn = state.Calendar.TurnIndex;
        if (nation.LastBarbarianArrivalTurn == turn) return state;
        if (nation.LastBarbarianArrivalTurn > turn)
            throw new ArgumentException("Barbarian arrival marker is ahead of the calendar.", nameof(state));

        var next = state with
        {
            Nations = new(state.Nations.Select(n => n.Id == nation.Id ? n with { LastBarbarianArrivalTurn = turn } : n)),
        };
        var policy = new BarbarianArrivalPolicy(config.NationId, config.ChancePercent, config.ArmyCap, config.FleetCap);
        if (BarbarianArrivalPlanner.Plan(world, next, policy, rng) is not { } proposal) return next;

        // Ids are campaign-local and deterministic. Never overwrite an existing force.
        var armyId = $"{nation.Id}-arrival-{turn}-army";
        var fleetId = $"{nation.Id}-arrival-{turn}-fleet";
        if (state.ArmyById(armyId) is not null || state.FleetById(fleetId) is not null)
            throw new InvalidOperationException("A barbarian arrival id is already present; no force was created.");

        var sea = proposal.Kind == BarbarianArrivalKind.Sea;
        var point = proposal.Entry;
        var covered = world.Terrain.Decode(world.Width, world.Height)[point.Y * world.Width + point.X];
        var troops = checked((int)config.ArrivalUnits.Sum(u => (long)u.Troops));
        var army = new ArmyState(armyId, nation.Id, point.X, point.Y, Moves: 0,
            config.ArmyMorale, Money: 0, SupplyCapacity.ArmyCapacityTons(troops, ruleset),
            sea ? null : covered, sea ? fleetId : null, config.ArrivalUnits);
        next = next with { Armies = new(next.Armies.Append(army)) };
        if (sea)
        {
            var fleet = new FleetState(fleetId, nation.Id, point.X, point.Y, Moves: 0,
                config.FleetShips, ConditionPercent: 100, Money: 0,
                SupplyCapacity.FleetCapacityTons(config.FleetShips, ruleset),
                ConstructionTicksRemaining: null, BuildCityId: null, CarriedArmyId: armyId, CoveredTileCode: covered);
            next = next with { Fleets = new(next.Fleets.Append(fleet)) };
        }
        next = Armies.ArmyTable.Record(state, next, ruleset);
        events.Publish(new BarbarianArrivalEntered(sea ? "by sea" : "by land", point.X, point.Y));
        return next;
    }
}
