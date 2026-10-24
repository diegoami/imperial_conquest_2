using System.Globalization;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Naval;

namespace IC2.Slice.UI;

/// <summary>One read-only recovery decision; issuing its command and updating the plan belong to the screen.</summary>
public sealed record RetreatSupplyDecision(string UnitId, bool IsFleet, string? CommandLine,
    string? DestinationCityId, string? Reason = null, bool Completed = false,
    bool Cancel = false, bool Waiting = false, bool AtProvider = false);

public static class RetreatSupplyPlanner
{
    public const int DangerRadiusTiles = 10;

    public static RetreatSupplyDecision Next(GameState state, World world, Ruleset ruleset,
        UserSettings settings, RetreatSupplyPlan plan)
    {
        RetreatSupplyDecision Stop(string reason) =>
            new(plan.UnitId, plan.IsFleet, null, null, reason, Cancel: true);
        if (state.WorldId != world.Id) return Stop("plan belongs to another world");
        var nation = state.NationById(plan.NationId);
        if (nation is null) return Stop("nation is missing");
        if (state.ActiveNationId != plan.NationId)
            return new(plan.UnitId, plan.IsFleet, null, plan.DestinationCityId, Waiting: true);
        if (nation.Control != SeatControl.Human) return Stop("active seat is not human");

        var army = plan.IsFleet ? null : state.ArmyById(plan.UnitId);
        var fleet = plan.IsFleet ? state.FleetById(plan.UnitId) : null;
        if (plan.IsFleet && (fleet is null || fleet.Nation != plan.NationId))
            return Stop("fleet is missing or changed nation");
        if (!plan.IsFleet && (army is null || army.Nation != plan.NationId))
            return Stop("army is missing or changed nation");
        if (army is { IsEmbarked: true } || army is { CoveredTileCode: null })
            return Stop("army is embarked");
        if (fleet is { IsUnderConstruction: true }) return Stop("fleet is under construction");

        var x = army?.X ?? fleet!.X;
        var y = army?.Y ?? fleet!.Y;
        var danger = Danger(state, ruleset, plan.NationId, x, y);
        if (danger is not null) return Stop($"enemy {danger} within {DangerRadiusTiles} tiles");

        var fullSupply = army is not null
            ? army.SupplyTons >= SupplyCapacity.ArmyDialogCapacityTons(army.TotalTroops, ruleset)
            : fleet!.SupplyTons >= SupplyCapacity.FleetCapacityTons(fleet.Ships, ruleset);
        var fullCondition = fleet is null || fleet.ConditionPercent >= ruleset.Naval.MaxConditionPercent;
        if (fullSupply && fullCondition)
            return new(plan.UnitId, plan.IsFleet, null, null, Completed: true);
        if (army is not null && !fullSupply)
        {
            foreach (var provider in SupplyProviders.ForArmy(state, army.Id, ruleset))
            {
                // Nearby army/fleet support is free before choosing a town;
                // owned towns still follow the owner's capital/population rank.
                if (!provider.IsFree || provider.Kind == SupplyProviderKind.City) continue;
                var stock = provider.Kind switch
                {
                    SupplyProviderKind.Army => state.ArmyById(provider.Id)?.SupplyTons ?? 0,
                    SupplyProviderKind.Fleet => state.FleetById(provider.Id)?.SupplyTons ?? 0,
                    _ => 0,
                };
                if (stock > 0)
                    return new(plan.UnitId, false, null, null, AtProvider: true);
            }
        }
        var usable = state.Cities.Where(city => city.Owner == plan.NationId
            && (fleet is null || CoastalCity.IsCoastal(city, world))
            && CanService(city, army, fleet, nation, ruleset, settings, fullSupply, fullCondition)).ToArray();
        var chosen = usable.FirstOrDefault(city => city.Id == plan.DestinationCityId)
            ?? usable.OrderByDescending(city => city.Id == nation.CapitalCityId)
                .ThenByDescending(city => city.PopulationThousands)
                .ThenBy(city => Distance(x, y, city.X, city.Y))
                .ThenBy(city => city.Id, StringComparer.Ordinal).FirstOrDefault();
        if (chosen is null) return Stop("no owned town can currently supply or repair this unit");
        if (Distance(x, y, chosen.X, chosen.Y) <= ruleset.Economy.CommandAdjacencyRadiusTiles)
            return new(plan.UnitId, plan.IsFleet, null, chosen.Id, AtProvider: true);
        if ((army?.Moves ?? fleet!.Moves) <= 0)
            return new(plan.UnitId, plan.IsFleet, null, chosen.Id, Waiting: true);

        var verb = plan.IsFleet ? "move-fleet" : "move";
        return new(plan.UnitId, plan.IsFleet,
            $"{verb} {plan.UnitId} {chosen.X.ToString(CultureInfo.InvariantCulture)} {chosen.Y.ToString(CultureInfo.InvariantCulture)}",
            chosen.Id);
    }

    /// <summary>Any on-map army or launched fleet belonging to a nation currently at war.</summary>
    public static string? Danger(GameState state, Ruleset ruleset, string nationId, int x, int y)
    {
        bool AtWar(string other) => other != nationId
            && state.Relations.IndexOf(other) >= 0
            && state.Relations.Get(nationId, other) == ruleset.Diplomacy.StateCodes.War;
        foreach (var army in state.Armies)
            if (army.CoveredTileCode is not null && AtWar(army.Nation)
                && Distance(x, y, army.X, army.Y) <= DangerRadiusTiles)
                return "army";
        foreach (var fleet in state.Fleets)
            if (!fleet.IsUnderConstruction && AtWar(fleet.Nation)
                && Distance(x, y, fleet.X, fleet.Y) <= DangerRadiusTiles)
                return "fleet";
        return null;
    }

    private static bool CanService(CityState city, ArmyState? army, FleetState? fleet,
        NationState nation, Ruleset ruleset, UserSettings settings, bool fullSupply, bool fullCondition)
    {
        if (!fullSupply && city.SupplyTons > 0) return true;
        if (fleet is null || fullCondition || fleet.IsCarryingArmy) return false;
        var available = Math.Max(0, nation.Treasury - settings.AutoTreasuryReserveTalents);
        return available > 0 || fleet.Ships / ruleset.Naval.RepairCostDivisor == 0;
    }

    private static int Distance(int x1, int y1, int x2, int y2) =>
        Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));
}
