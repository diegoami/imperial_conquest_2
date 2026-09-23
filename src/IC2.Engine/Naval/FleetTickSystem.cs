using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Naval;

/// <summary>
/// Registers the fleet construction countdown and <see cref="FleetAttritionRule"/> into
/// <see cref="TurnPhase.FleetTick"/> — the attrition phase T06 declared for exactly this
/// (<c>docs/task-catalogue.md</c> "T14 Naval", Done-when 2, 10, 11, 12, 13, 14).
/// </summary>
/// <remarks>
/// <para>
/// <strong>"In port" versus "at sea" is <see cref="FleetState.IsUnderConstruction"/>, not tile
/// geometry.</strong> <c>docs/task-catalogue.md</c>'s own Scope states the at-sea gate as
/// <c>FleetRecord +10 == 0xFFFF</c> — exactly the sentinel <see cref="FleetState.ConstructionTicksRemaining"/>
/// already models as <see langword="null"/> once a fleet launches. A fleet still under construction is
/// "in port" for this rule: its supply still drains by <see cref="FleetState.Ships"/> every turn (the one
/// unconditional line, run for every fleet before the branch below), but its condition, moves and the
/// death check are untouched, and a docked, already-launched fleet sitting at its own city is not
/// exempted by geography — the original's own gate never tests position, only the launched sentinel.
/// </para>
/// <para>
/// <strong>Two predicates the cited reports leave open</strong> (<c>docs/investigations/thracia-supply-morale.md</c>
/// §"Still open"), implemented as named, ruleset-driven placeholders rather than escalated, per
/// <c>docs/task-catalogue.md</c> T14's own instruction:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <c>FleetRecord +24 == 1</c> (the tripling predicate) reads <see cref="FleetState.CoveredTileCode"/>
/// directly against <see cref="NavalRules.StormTripleConditionTileCode"/> — the same field the record
/// layout already confirms as the fleet's covered map cell, so no new field is invented; only the
/// trigger value's real-world meaning is undecompiled.
/// </description></item>
/// <item><description>
/// <c>FUN_004494e4</c> ("away from friendly coast") is answered by <see cref="IsNearFriendlyCoast"/>: a
/// launched fleet within <see cref="NavalRules.FriendlyCoastRadiusTiles"/> tiles (Chebyshev distance) of
/// a city its own nation owns counts as near friendly coast. <c>[designed]</c> placeholder geometry for
/// an undecompiled predicate, not a resolution of it — see that field's own remarks.
/// </description></item>
/// </list>
/// <para>
/// A fleet the storm pass destroys takes any army it carries down with it
/// (<c>docs/game-design.md</c> §Naval: "lost if the fleet is lost") — removed from
/// <see cref="GameState.Armies"/> in the same pass, never left behind as an orphaned record.
/// </para>
/// <para>
/// Draws are made sequentially off <see cref="SystemContext.Rng"/>, in <see cref="GameState.Fleets"/>'s
/// own order — the same convention <see cref="Economy.ArmySupplyAndMoraleSystem"/> uses for armies, so
/// this system's own phase-scoped stream (<see cref="GameSystemAttribute.Id"/>) is what keeps one fleet's
/// roll from perturbing another system's, not a further per-fleet sub-stream.
/// </para>
/// </remarks>
[GameSystem(TurnPhase.FleetTick, "naval.fleet-tick")]
public sealed class FleetTickSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ruleset = context.Ruleset;
        var rules = ruleset.Naval;
        var state = context.State;
        var isWinter = state.Calendar.SeasonIndex == ruleset.Calendar.SeasonsPerYear - 1;

        var updatedFleets = new List<FleetState>(state.Fleets.Count);
        var destroyedArmyIds = new HashSet<string>(StringComparer.Ordinal);
        var updatedArmies = new Dictionary<string, ArmyState>(StringComparer.Ordinal);

        foreach (var fleet in state.Fleets)
        {
            if (fleet.IsUnderConstruction)
            {
                var supplyAfterConsumption = Math.Max(0, fleet.SupplyTons - fleet.Ships);
                var ticksRemaining = fleet.ConstructionTicksRemaining!.Value - rules.ConstructionTickStep;

                var buildCity = fleet.BuildCityId is { } buildCityId ? state.CityById(buildCityId) : null;
                var launchPoint = buildCity is not null
                    ? CoastalCity.FirstAdjacentSeaTile(buildCity, context.World)
                    : null;

                // N9 (first review): a build city that no longer resolves, or that no longer has an
                // adjacent sea tile, must not launch the fleet at a fabricated "?" city name or at the
                // stored (0, 0) sentinel -- both are unguarded fallbacks masquerading as success. Neither
                // is reachable today (nothing in the shipped engine removes a city or its terrain), but
                // this is the defensible failure mode if it ever became one: stall in construction,
                // retried every turn, rather than complete degraded.
                if (ticksRemaining <= 0 && buildCity is not null && launchPoint is { } point)
                {
                    var launched = fleet with
                    {
                        X = point.X,
                        Y = point.Y,
                        ConditionPercent = rules.LaunchConditionPercent,
                        SupplyTons = rules.LaunchSupplyTons,
                        Money = 0,
                        ConstructionTicksRemaining = null,
                    };
                    updatedFleets.Add(launched);

                    context.Events.Publish(new FleetFinished(fleet.Nation, buildCity.Name));
                }
                else
                {
                    // Stall at 1 (retry next turn) rather than persist a <= 0 value if the launch was
                    // blocked; otherwise carry the ordinary decremented countdown forward unchanged.
                    var carriedTicks = ticksRemaining <= 0 ? 1 : ticksRemaining;
                    updatedFleets.Add(fleet with
                    {
                        SupplyTons = supplyAfterConsumption,
                        ConstructionTicksRemaining = carriedTicks,
                    });
                }

                continue;
            }

            var carriedArmy = fleet.CarriedArmyId is { } armyId ? state.ArmyById(armyId) : null;
            var tripleDamageBranchActive = fleet.CoveredTileCode == rules.StormTripleConditionTileCode;
            var nearFriendlyCoast = IsNearFriendlyCoast(fleet, state, rules);

            // T63 (bug #292, steps 3 and 4; bug #292/B2): the carried army's own casualty pass now runs
            // INSIDE ApplyLaunchedFleetTurn, between the storm pass and the moves formula, so moves are
            // computed from the army's POST-storm troop count -- the original's own order
            // (supply-driven-morale-and-fleet-attrition.md §"The fleet loop, in order", research
            // 3f6ca09). Passing carriedArmy.Units (not just a troop count) is what lets this method
            // update them and hand the result back below.
            var outcome = FleetAttritionRule.ApplyLaunchedFleetTurn(
                ships: fleet.Ships,
                conditionPercent: fleet.ConditionPercent,
                supplyTonsBeforeConsumption: fleet.SupplyTons,
                carriedArmyUnits: carriedArmy?.Units,
                isWinter: isWinter,
                tripleDamageBranchActive: tripleDamageBranchActive,
                nearFriendlyCoast: nearFriendlyCoast,
                rng: context.Rng,
                ruleset: ruleset);

            if (outcome.Destroyed)
            {
                context.Events.Publish(new FleetLostAtSea(fleet.Nation));
                if (carriedArmy is not null)
                {
                    destroyedArmyIds.Add(carriedArmy.Id);
                }

                continue; // the fleet itself is removed -- not added to updatedFleets.
            }

            if (outcome.DamagedInStorm)
            {
                context.Events.Publish(new FleetDamagedInStorm(fleet.Nation));
            }

            // The delete sweep: an emptied carried army is deleted and the fleet's own link to it
            // cleared (bug #289); a surviving, storm-reduced army is written back so its lower troop
            // count and unit list persist (B8 -- an earlier revision only ever wrote this back for the
            // "emptied" case, so a survivor's own casualties were silently dropped).
            var carriedArmyIdAfterStorm = fleet.CarriedArmyId;
            if (carriedArmy is not null && outcome.CarriedArmyUnits is { } armyUnitsAfter)
            {
                if (outcome.CarriedArmyEmptied)
                {
                    destroyedArmyIds.Add(carriedArmy.Id);
                    carriedArmyIdAfterStorm = null;
                }
                else if (outcome.DamagedInStorm)
                {
                    updatedArmies[carriedArmy.Id] = carriedArmy with { Units = armyUnitsAfter };
                }
            }

            updatedFleets.Add(fleet with
            {
                Ships = outcome.Ships,
                ConditionPercent = outcome.ConditionPercent,
                SupplyTons = outcome.SupplyTonsAfterConsumption,
                Moves = outcome.Moves,
                CarriedArmyId = carriedArmyIdAfterStorm,
            });
        }

        var survivingArmies = ValueList.From(state.Armies
            .Where(a => !destroyedArmyIds.Contains(a.Id))
            .Select(a => updatedArmies.TryGetValue(a.Id, out var updated) ? updated : a));

        return state with { Fleets = ValueList.From(updatedFleets), Armies = survivingArmies };
    }

    /// <summary>
    /// <c>[designed]</c> placeholder for the undecompiled <c>FUN_004494e4</c> — see this type's remarks.
    /// </summary>
    private static bool IsNearFriendlyCoast(FleetState fleet, GameState state, NavalRules rules)
    {
        foreach (var city in state.Cities)
        {
            if (!string.Equals(city.Owner, fleet.Nation, StringComparison.Ordinal))
            {
                continue;
            }

            var distance = Math.Max(Math.Abs(city.X - fleet.X), Math.Abs(city.Y - fleet.Y));
            if (distance <= rules.FriendlyCoastRadiusTiles)
            {
                return true;
            }
        }

        return false;
    }
}
