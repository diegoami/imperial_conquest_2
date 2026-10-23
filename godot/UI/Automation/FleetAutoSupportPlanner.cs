using System.Globalization;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Slice.UI;

/// <summary>One proposed fleet support command; planning never mutates state or RNG.</summary>
public sealed record FleetAutoSupportProposal(string FleetId, string CommandLine, string Kind,
    int Requested, string? CityId = null);

/// <summary>T208: deterministic owned-town fleet supply and affordable repair suggestions.</summary>
public static class FleetAutoSupportPlanner
{
    public static FleetAutoSupportProposal? NextSupply(GameState state, FleetState fleet, Ruleset ruleset,
        UserSettings settings, IReadOnlySet<string> attemptedCities)
    {
        if (!settings.AutoSupplyFleets || fleet.IsUnderConstruction) return null;
        var capacity = SupplyCapacity.FleetCapacityTons(fleet.Ships, ruleset);
        var target = (int)Math.Ceiling((double)capacity * settings.AutoFleetSupplyTargetPercent / 100);
        var needed = Math.Max(0, target - fleet.SupplyTons);
        if (needed == 0) return null;

        foreach (var city in state.Cities)
        {
            if (!OwnTownNearby(city, fleet, ruleset) || attemptedCities.Contains(city.Id)) continue;
            var tons = Math.Min(needed, Math.Max(0, city.SupplyTons));
            if (tons == 0) continue;
            return new(fleet.Id,
                $"buy-fleet-supply {fleet.Id} {city.Id} {tons.ToString(CultureInfo.InvariantCulture)}",
                "supply", tons, city.Id);
        }

        return null;
    }

    public static FleetAutoSupportProposal? Repair(GameState state, FleetState fleet, Ruleset ruleset,
        UserSettings settings)
    {
        if (!settings.AutoRepairFleets || fleet.IsUnderConstruction || fleet.IsCarryingArmy) return null;
        var city = state.Cities.FirstOrDefault(c => OwnTownNearby(c, fleet, ruleset));
        if (city is null) return null;
        var nation = state.NationById(fleet.Nation);
        if (nation is null) return null;
        var target = Math.Min(settings.AutoFleetRepairTargetPercent, ruleset.Naval.MaxConditionPercent);
        var room = Math.Max(0, target - fleet.ConditionPercent);
        var available = Math.Max(0, nation.Treasury - settings.AutoTreasuryReserveTalents);
        for (var points = room; points > 0; points--)
        {
            var cost = (fleet.Ships * points) / ruleset.Naval.RepairCostDivisor;
            if (cost <= available)
                return new(fleet.Id, $"repair-fleet {fleet.Id} {points.ToString(CultureInfo.InvariantCulture)}",
                    "repair", points, city.Id);
        }

        return null;
    }

    private static bool OwnTownNearby(CityState city, FleetState fleet, Ruleset ruleset) =>
        string.Equals(city.Owner, fleet.Nation, StringComparison.Ordinal)
        && LandingTile.ChebyshevDistance(new GridPoint(city.X, city.Y),
            new GridPoint(fleet.X, fleet.Y)) <= ruleset.Economy.CommandAdjacencyRadiusTiles;
}
