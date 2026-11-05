using System.Globalization;
using IC2.Engine.Model;
using IC2.Engine.Naval;

namespace IC2.Slice.UI;

public sealed record FleetRallyDecision(string FleetId, string? CommandLine, string? Reason,
    bool Arrived = false, bool WaitForMoves = false);

public static class FleetRallyPlanner
{
    public static string? ValidatePoint(GameState state, World world, NationRallyPoint point)
    {
        if (state.WorldId != world.Id) return "rally belongs to another world";
        if (point.CityId is { } id)
        {
            var city = state.CityById(id);
            if (city is null || city.Owner != point.NationId) return "rally city is missing or changed owner";
            return CoastalCity.IsCoastal(city, world) ? null : "rally city is not coastal";
        }
        return IsSea(world, point.X, point.Y) ? null : "rally destination must be sea or an owned coastal city";
    }

    public static FleetRallyDecision ForOrder(GameState state, World world, Ruleset ruleset, FleetRallyOrder order)
    {
        FleetRallyDecision Stop(string reason) => new(order.FleetId, null, reason);
        var fleet = state.FleetById(order.FleetId);
        if (fleet is null || fleet.Nation != order.NationId) return Stop("fleet is missing or changed nation");
        if (state.ActiveNationId != order.NationId) return new(order.FleetId, null, null, WaitForMoves: true);
        if (state.NationById(order.NationId)?.Control != SeatControl.Human) return Stop("active seat is not human");
        if (fleet.IsUnderConstruction) return Stop("fleet is under construction");
        var point = new NationRallyPoint(order.NationId, order.X, order.Y, order.CityId, IsFleet: true);
        if (ValidatePoint(state, world, point) is { } invalid) return Stop(invalid);
        var cells = world.Terrain.Decode(world.Width, world.Height);
        bool Sea(int cx, int cy) => (uint)cx < (uint)world.Width && (uint)cy < (uint)world.Height
            && world.TileTypeByCode(cells[cy * world.Width + cx])?.PassableByFleets == true;
        var x = order.X;
        var y = order.Y;
        if (order.CityId is { } cityId)
        {
            var city = state.CityById(cityId)!;
            if (Distance(fleet.X, fleet.Y, city.X, city.Y) <= 1 && Sea(fleet.X, fleet.Y))
                return new(order.FleetId, null, null, Arrived: true);
            var approaches = Enumerable.Range(city.Y - 1, 3)
                .SelectMany(cy => Enumerable.Range(city.X - 1, 3).Select(cx => (X: cx, Y: cy)))
                .Where(p => (p.X != city.X || p.Y != city.Y) && Sea(p.X, p.Y)
                    && !state.Fleets.Any(f => f.Id != fleet.Id && !f.IsUnderConstruction && f.X == p.X && f.Y == p.Y))
                .OrderBy(p => Distance(fleet.X, fleet.Y, p.X, p.Y)).ThenBy(p => p.Y).ThenBy(p => p.X).ToArray();
            if (approaches.Length == 0) return Stop("rally city has no free sea approach");
            (x, y) = approaches[0];
        }
        if (fleet.X == x && fleet.Y == y) return new(order.FleetId, null, null, Arrived: true);
        if (RetreatSupplyPlanner.Danger(state, ruleset, fleet.Nation, fleet.X, fleet.Y) is { } danger)
            return Stop($"enemy {danger} within ten tiles");
        if (fleet.Moves <= 0) return new(order.FleetId, null, null, WaitForMoves: true);
        return new(order.FleetId, $"move-fleet {fleet.Id} {x.ToString(CultureInfo.InvariantCulture)} {y.ToString(CultureInfo.InvariantCulture)}", null);
    }

    private static int Distance(int x, int y, int dx, int dy) => Math.Max(Math.Abs(x - dx), Math.Abs(y - dy));
    private static bool IsSea(World world, int x, int y) => (uint)x < (uint)world.Width && (uint)y < (uint)world.Height
        && world.TileTypeByCode(world.Terrain.Decode(world.Width, world.Height)[y * world.Width + x])?.PassableByFleets == true;
}
