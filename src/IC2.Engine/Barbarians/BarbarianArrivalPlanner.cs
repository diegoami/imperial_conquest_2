using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.Barbarians;

/// <summary>Owner-approved initial arrival tuning. The seasonal caller owns cadence.</summary>
public sealed record BarbarianArrivalPolicy(
    string NationId, int ChancePercent = 25, int ArmyCap = 4, int FleetCap = 4);

/// <summary>A sea arrival requires a transport and a carried army; a land arrival only an army.</summary>
public enum BarbarianArrivalKind
{
    Land,
    Sea,
}

/// <summary>An in-bounds boundary entry proposal, not an accepted spawn or a reserved tile.</summary>
public sealed record BarbarianArrival(BarbarianArrivalKind Kind, GridPoint Entry);

/// <summary>
/// Pure boundary-arrival planning for the designed barbarian feature (T259).
/// Does not register a seasonal handler, create forces, or mutate campaign state.
/// </summary>
public static class BarbarianArrivalPlanner
{
    /// <summary>
    /// Selects one legal arrival using the caller's dedicated seasonal RNG stream.
    /// A null policy, reached army cap or lack of legal entries consumes no draws.
    /// Sea candidates must connect to an empty land landing through unoccupied sea.
    /// </summary>
    public static BarbarianArrival? Plan(
        World world, GameState state, BarbarianArrivalPolicy? policy, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rng);
        if (policy is null) return null;

        ArgumentException.ThrowIfNullOrWhiteSpace(policy.NationId);
        if (policy.ChancePercent is < 0 or > 100 || policy.ArmyCap <= 0 || policy.FleetCap <= 0)
            throw new ArgumentOutOfRangeException(nameof(policy), "Invalid arrival chance or force cap.");
        if (world.Width <= 0 || world.Height <= 0 || state.WorldId != world.Id)
            throw new ArgumentException("Arrival planning requires a positive-sized matching world.", nameof(world));
        if (state.NationById(policy.NationId) is null)
            throw new ArgumentException("The barbarian nation must exist in the campaign.", nameof(policy));

        if (policy.ChancePercent == 0 || state.Armies.Count(a => a.Nation == policy.NationId) >= policy.ArmyCap)
            return null;

        var cells = world.Terrain.Decode(world.Width, world.Height);
        var occupied = new bool[cells.Length];
        void Occupy(int x, int y)
        {
            if ((uint)x < (uint)world.Width && (uint)y < (uint)world.Height)
                occupied[y * world.Width + x] = true;
        }
        foreach (var city in state.Cities) Occupy(city.X, city.Y);
        foreach (var army in state.Armies) Occupy(army.X, army.Y);
        foreach (var fleet in state.Fleets)
            if (!fleet.IsUnderConstruction) Occupy(fleet.X, fleet.Y);

        var land = new bool[cells.Length];
        var sea = new bool[cells.Length];
        for (var i = 0; i < cells.Length; i++)
        {
            var tile = world.TileTypeByCode(cells[i]);
            land[i] = !occupied[i] && tile?.PassableByArmies == true;
            sea[i] = !occupied[i] && tile?.PassableByFleets == true;
        }

        var allowSea = state.Fleets.Count(f => f.Nation == policy.NationId) < policy.FleetCap;
        var canLand = allowSea ? SeaWithLanding(world.Width, world.Height, land, sea) : null;
        var landEntries = new List<GridPoint>();
        var seaEntries = new List<GridPoint>();
        // Row-major scan visits corners once, including one-row/one-column worlds.
        for (var y = 0; y < world.Height; y++)
        for (var x = 0; x < world.Width; x++)
        {
            if (x != 0 && y != 0 && x != world.Width - 1 && y != world.Height - 1) continue;
            var i = y * world.Width + x;
            if (land[i]) landEntries.Add(new GridPoint(x, y));
            if (canLand?[i] == true) seaEntries.Add(new GridPoint(x, y));
        }

        if (landEntries.Count == 0 && seaEntries.Count == 0) return null;
        if (!rng.NextChance(policy.ChancePercent, 100)) return null;

        var chooseSea = landEntries.Count == 0 || (seaEntries.Count > 0 && rng.NextInt(2) == 1);
        var entries = chooseSea ? seaEntries : landEntries;
        var entry = entries[rng.NextInt(entries.Count)];
        return new BarbarianArrival(chooseSea ? BarbarianArrivalKind.Sea : BarbarianArrivalKind.Land, entry);
    }

    private static bool[] SeaWithLanding(int width, int height, bool[] land, bool[] sea)
    {
        var reachable = new bool[sea.Length];
        var queue = new Queue<int>();
        // Seed every empty sea tile adjacent to empty land, then flood outward.
        // Occupied sea blocks the route, not only the boundary entry itself.
        for (var i = 0; i < sea.Length; i++)
        {
            if (!sea[i]) continue;
            foreach (var neighbour in Neighbours(i, width, height))
            {
                if (!land[neighbour]) continue;
                reachable[i] = true;
                queue.Enqueue(i);
                break;
            }
        }
        while (queue.TryDequeue(out var current))
        {
            foreach (var neighbour in Neighbours(current, width, height))
            {
                if (!sea[neighbour] || reachable[neighbour]) continue;
                reachable[neighbour] = true;
                queue.Enqueue(neighbour);
            }
        }
        return reachable;
    }

    private static IEnumerable<int> Neighbours(int index, int width, int height)
    {
        var x = index % width;
        var y = index / width;
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (dx == 0 && dy == 0) continue;
            var nx = x + dx;
            var ny = y + dy;
            if ((uint)nx < (uint)width && (uint)ny < (uint)height)
                yield return ny * width + nx;
        }
    }
}
