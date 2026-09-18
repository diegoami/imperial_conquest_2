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

        foreach (var fleet in state.Fleets)
        {
            if (fleet.IsUnderConstruction)
            {
                var supplyAfterConsumption = Math.Max(0, fleet.SupplyTons - fleet.Ships);
                var ticksRemaining = fleet.ConstructionTicksRemaining!.Value - rules.ConstructionTickStep;

                if (ticksRemaining <= 0)
                {
                    var buildCity = fleet.BuildCityId is { } buildCityId ? state.CityById(buildCityId) : null;
                    var launchPoint = buildCity is not null
                        ? CoastalCity.FirstAdjacentSeaTile(buildCity, context.World)
                        : null;

                    var launched = fleet with
                    {
                        X = launchPoint?.X ?? fleet.X,
                        Y = launchPoint?.Y ?? fleet.Y,
                        ConditionPercent = rules.LaunchConditionPercent,
                        SupplyTons = rules.LaunchSupplyTons,
                        Money = 0,
                        ConstructionTicksRemaining = null,
                    };
                    updatedFleets.Add(launched);

                    context.Events.Publish(new FleetFinished(fleet.Nation, buildCity?.Name ?? "?"));
                }
                else
                {
                    updatedFleets.Add(fleet with { SupplyTons = supplyAfterConsumption, ConstructionTicksRemaining = ticksRemaining });
                }

                continue;
            }

            var carriedArmy = fleet.CarriedArmyId is { } armyId ? state.ArmyById(armyId) : null;
            var tripleDamageBranchActive = fleet.CoveredTileCode == rules.StormTripleConditionTileCode;
            var nearFriendlyCoast = IsNearFriendlyCoast(fleet, state, rules);

            var outcome = FleetAttritionRule.ApplyLaunchedFleetTurn(
                ships: fleet.Ships,
                conditionPercent: fleet.ConditionPercent,
                supplyTonsBeforeConsumption: fleet.SupplyTons,
                carriedArmyTroops: carriedArmy?.TotalTroops,
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

            updatedFleets.Add(fleet with
            {
                Ships = outcome.Ships,
                ConditionPercent = outcome.ConditionPercent,
                SupplyTons = outcome.SupplyTonsAfterConsumption,
                Moves = outcome.Moves,
            });
        }

        var survivingArmies = destroyedArmyIds.Count == 0
            ? state.Armies
            : ValueList.From(state.Armies.Where(a => !destroyedArmyIds.Contains(a.Id)));

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
