using System.Globalization;
using IC2.Engine.Model;

namespace IC2.Slice.UI;

/// <summary>A read-only proposal for one campaign-saved army target.</summary>
public sealed record ArmyTravelDecision(string ArmyId, string? CommandLine, string? Reason,
    bool Arrived = false, bool WaitForMoves = false);

public static class ArmyTravelPlanner
{
    public static ArmyTravelDecision ForTarget(GameState state, World world, Ruleset ruleset,
        ArmyTravelTarget target)
    {
        ArmyTravelDecision Stop(string reason) => new(target.ArmyId, null, reason);
        if (state.WorldId != world.Id) return Stop("target belongs to another world");
        var nation = state.NationById(state.ActiveNationId);
        if (nation?.Control != SeatControl.Human) return Stop("active seat is not human");
        var army = state.ArmyById(target.ArmyId);
        if (army is null || army.Nation != target.NationId) return Stop("army is missing or changed nation");
        if (army.Nation != nation.Id) return new(target.ArmyId, null, "another human seat's target", WaitForMoves: true);
        if (army.IsEmbarked || army.CoveredTileCode is null) return Stop("army is embarked");

        var x = target.X;
        var y = target.Y;
        if (target.CityId is { } cityId)
        {
            var city = world.Cities.FirstOrDefault(candidate => candidate.Id == cityId);
            var liveCity = state.CityById(cityId);
            if (city is null || liveCity is null) return Stop("target city is missing");
            if (target.FromRally && liveCity.Owner != target.NationId)
                return Stop("rally city changed owner");
            x = city.X;
            y = city.Y;
        }
        if ((uint)x >= (uint)world.Width || (uint)y >= (uint)world.Height)
            return Stop("target is outside this world");
        if (target.CityId is null)
        {
            var cells = world.Terrain.Decode(world.Width, world.Height);
            if (world.TileTypeByCode(cells[y * world.Width + x])?.PassableByArmies != true)
                return Stop("target cell is not land");
        }

        var distance = Math.Max(Math.Abs(army.X - x), Math.Abs(army.Y - y));
        if (distance == 0 || target.CityId is not null && distance <= 1)
            return new(target.ArmyId, null, null, Arrived: true);
        if (HasHostileContact(state, ruleset, army)) return Stop("hostile contact");
        if (army.Moves <= 0) return new(target.ArmyId, null, null, WaitForMoves: true);
        return new(target.ArmyId,
            $"move {army.Id} {x.ToString(CultureInfo.InvariantCulture)} {y.ToString(CultureInfo.InvariantCulture)}",
            null);
    }

    public static bool HasHostileContact(GameState state, Ruleset ruleset, ArmyState army)
    {
        bool AtWar(string other) => other != army.Nation
            && state.Relations.Get(army.Nation, other) == ruleset.Diplomacy.StateCodes.War;
        return state.Armies.Any(other => other.CoveredTileCode is not null && AtWar(other.Nation)
                && Math.Max(Math.Abs(other.X - army.X), Math.Abs(other.Y - army.Y)) <= 1)
            || state.Cities.Any(city => AtWar(city.Owner)
                && Math.Max(Math.Abs(city.X - army.X), Math.Abs(city.Y - army.Y)) <= 1);
    }
}
