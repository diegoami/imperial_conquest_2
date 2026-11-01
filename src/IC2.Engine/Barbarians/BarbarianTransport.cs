using IC2.Engine.Ai;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval.Commands;

namespace IC2.Engine.Barbarians;

/// <summary>Chooses transport orders; all sailing and landing is performed by existing commands.</summary>
public static class BarbarianTransport
{
    /// <summary>Stable fleet/table and grid ordering; route planning consumes no random draws.</summary>
    public static AiCandidate? Propose(AiView view, IReadOnlyList<string> sailed)
    {
        foreach (var fleet in view.State.Fleets)
        {
            if (fleet.Nation != view.NationId || fleet.IsUnderConstruction || fleet.CarriedArmyId is not { } cargoId
                || view.State.ArmyById(cargoId) is not { } army || army.Nation != fleet.Nation || army.AboardFleetId != fleet.Id)
                continue;
            var proposal = ForFleet(view, fleet, army, sailed.Contains(fleet.Id));
            if (proposal is not null) return proposal;
        }
        return null;
    }

    private static AiCandidate? ForFleet(AiView view, FleetState fleet, ArmyState army, bool alreadySailed)
    {
        var world = view.World;
        var state = view.State;
        var width = world.Width;
        var height = world.Height;
        var size = checked(width * height);
        var cells = view.TerrainCells;
        var land = new bool[size];
        var sea = new bool[size];
        var occupied = new bool[size];
        for (var i = 0; i < size; i++)
        {
            var tile = world.TileTypeByCode(cells[i]);
            land[i] = tile?.PassableByArmies == true;
            sea[i] = tile?.PassableByFleets == true;
        }
        foreach (var city in state.Cities) occupied[city.Y * width + city.X] = true;
        foreach (var other in state.Armies.Where(a => !a.IsEmbarked)) occupied[other.Y * width + other.X] = true;
        foreach (var other in state.Fleets.Where(f => !f.IsUnderConstruction && f.Id != fleet.Id)) occupied[other.Y * width + other.X] = true;

        IEnumerable<int> Neighbours(int index)
        {
            var x = index % width;
            var y = index / width;
            for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var nx = x + dx;
                var ny = y + dy;
                if ((uint)nx < (uint)width && (uint)ny < (uint)height) yield return ny * width + nx;
            }
        }

        // A multi-source land distance to hostile towns avoids repeatedly searching the whole land
        // map for every possible shore. It is a target heuristic, not a replacement move/capture rule.
        var landDistance = Enumerable.Repeat(-1, size).ToArray();
        var queue = new Queue<int>();
        foreach (var city in state.Cities)
        {
            if (city.Owner == fleet.Nation || state.NationById(city.Owner)?.Eliminated != false
                || state.Relations.Get(fleet.Nation, city.Owner) != view.Ruleset.Diplomacy.StateCodes.War) continue;
            var index = city.Y * width + city.X;
            if (!land[index] || landDistance[index] >= 0) continue;
            landDistance[index] = 0;
            queue.Enqueue(index);
        }
        while (queue.TryDequeue(out var current))
        foreach (var next in Neighbours(current))
        {
            if (!land[next] || landDistance[next] >= 0 || occupied[next]) continue;
            landDistance[next] = landDistance[current] + 1;
            queue.Enqueue(next);
        }

        var origin = fleet.Y * width + fleet.X;
        var distance = Enumerable.Repeat(-1, size).ToArray();
        var parent = Enumerable.Repeat(-1, size).ToArray();
        distance[origin] = 0;
        queue.Enqueue(origin);
        var bestSea = -1;
        var bestLand = -1;
        var bestCost = int.MaxValue;
        while (queue.TryDequeue(out var current))
        {
            foreach (var next in Neighbours(current))
            {
                if (land[next] && !occupied[next] && landDistance[next] >= 0)
                {
                    var cost = distance[current] + landDistance[next];
                    if (cost < bestCost || (cost == bestCost && (next < bestLand || (next == bestLand && current < bestSea))))
                    {
                        bestCost = cost;
                        bestSea = current;
                        bestLand = next;
                    }
                }
                if (!sea[next] || occupied[next] || distance[next] >= 0) continue;
                distance[next] = distance[current] + 1;
                parent[next] = current;
                queue.Enqueue(next);
            }
        }
        if (bestSea < 0) return null;
        var score = Math.Max(view.Ruleset.Ai.MinimumActionScore, view.Ruleset.Ai.SailAtFleetBaseScore);
        if (bestSea == origin)
            return AiCandidate.Single(AiPhase.Military, "land", new DisembarkArmyCommand(fleet.Nation, army.Id,
                bestLand % width, bestLand / width), score, "land beside a route to a hostile town", army.Id);
        if (alreadySailed || fleet.Moves <= 0) return null;

        // Aim at the furthest straight, unoccupied sea waypoint on the route. The normal fleet move
        // handler charges every entered cell and stops at its normal allowance/terrain/marker gates.
        var from = new GridPoint(fleet.X, fleet.Y);
        for (var waypoint = bestSea; waypoint != origin && waypoint >= 0; waypoint = parent[waypoint])
        {
            var target = new GridPoint(waypoint % width, waypoint / width);
            if (!BresenhamPath.Trace(from, target).Skip(1).All(p => sea[p.Y * width + p.X] && !occupied[p.Y * width + p.X])) continue;
            return AiCandidate.Single(AiPhase.Military, AiCandidate.SailKind,
                new MoveFleetCommand(fleet.Nation, fleet.Id, target.X, target.Y), score,
                "sail toward a legal landing and hostile town", fleet.Id);
        }
        return null;
    }
}
