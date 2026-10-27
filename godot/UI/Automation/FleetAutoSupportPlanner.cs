using System.Globalization;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Slice.UI;

/// <summary>One proposed fleet support command; planning never mutates state or RNG.</summary>
public sealed record FleetAutoSupportProposal(string FleetId, string CommandLine, string Kind,
    int Requested, string? CityId = null);

/// <summary>T208/T228: owned-town maximum supply and repair through ordinary commands.</summary>
public static class FleetAutoSupportPlanner
{
    public static FleetAutoSupportProposal? NextSupply(GameState state, FleetState fleet, Ruleset ruleset,
        UserSettings settings, IReadOnlySet<string> attemptedCities)
    {
        if (!settings.AutoSupplyFleets || fleet.IsUnderConstruction) return null;
        var capacity = SupplyCapacity.FleetCapacityTons(fleet.Ships, ruleset);
        var needed = Math.Max(0, capacity - fleet.SupplyTons);
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
        var room = Math.Max(0, ruleset.Naval.MaxConditionPercent - fleet.ConditionPercent);
        // RepairFleetCommand already permits debt and spends remaining moves;
        // automation adds no reserve or affordability rule.
        return room == 0 ? null : new(fleet.Id, $"repair-fleet {fleet.Id} {room.ToString(CultureInfo.InvariantCulture)}",
            "repair", room, city.Id);
    }

    private static bool OwnTownNearby(CityState city, FleetState fleet, Ruleset ruleset) =>
        string.Equals(city.Owner, fleet.Nation, StringComparison.Ordinal)
        && LandingTile.ChebyshevDistance(new GridPoint(city.X, city.Y),
            new GridPoint(fleet.X, fleet.Y)) <= ruleset.Economy.CommandAdjacencyRadiusTiles;
}
