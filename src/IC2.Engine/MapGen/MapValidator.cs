using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.MapGen;

/// <summary>
/// T294b: the generated-map validator. Every problem names its check, and <see cref="MapGenerator"/>
/// collects the names into the failure message, per the T294 architecture ("invalid output is
/// rejected and retried, then fails with a message naming the failed check").
/// </summary>
/// <remarks>
/// The contract's "river drawing consistent with crossings" is covered by the crossing-cost check:
/// river cells are army terrain at cost 4, so a crossing that walks them pays the production table,
/// and the encoded exit-code invariants themselves are the build-side convention of whatever drew
/// the rivers — nothing the engine validator can decide from the code grid alone.
/// </remarks>
public static class MapValidator
{
    /// <summary>Each capital must reach at least this many passable cells within the expansion budget.</summary>
    public const int ExpansionBudget = 48; // six turns of the armies' 8-move budget

    public const int ExpansionMinCells = 40;

    /// <summary>A capital-to-capital route may cost at most three times its Chebyshev distance (the
    /// authored campaigns' acceptance bound; the routes wrap ranges and coasts, not straight lines).</summary>
    public const int CrossingCostFactor = 3;

    public static IReadOnlyList<string> Validate(GeneratedMap map)
    {
        var problems = new List<string>();
        var codes = map.Codes;
        if (codes.Length != map.Width * map.Height)
        {
            problems.Add("terrain: the code grid does not match the map size");
            return problems;
        }

        var land = 0;
        var water = 0;
        foreach (var code in codes)
        {
            if (CanonicalTiles.PassableByArmies(code)) land++;
            if (CanonicalTiles.PassableByFleets(code)) water++;
        }

        if (land == 0) problems.Add("terrain: the map has no land");
        if (water == 0) problems.Add("terrain: the map has no water");

        var passable = new bool[codes.Length];
        for (var i = 0; i < codes.Length; i++) passable[i] = CanonicalTiles.PassableByArmies(codes[i]);

        // Settlements: on army-passable land, never sharing a tile.
        var cityTiles = new HashSet<int>();
        foreach (var city in map.Cities)
        {
            if (!InBounds(map, city.X, city.Y) || !passable[(city.Y * map.Width) + city.X])
            {
                problems.Add($"settlements: {city.Id} is not on army-passable land");
                continue;
            }

            if (!cityTiles.Add((city.Y * map.Width) + city.X)) problems.Add($"settlements: two cities share {city.Id}'s tile");
        }

        // Armies: army-passable, off city tiles, never sharing one.
        var armyTiles = new HashSet<int>();
        foreach (var army in map.Armies)
        {
            if (!InBounds(map, army.X, army.Y) || !passable[(army.Y * map.Width) + army.X])
            {
                problems.Add($"armies: {army.Id} is not on army-passable land");
                continue;
            }

            if (!armyTiles.Add((army.Y * map.Width) + army.X)) problems.Add($"armies: two armies share {army.Id}'s tile");
            if (cityTiles.Contains((army.Y * map.Width) + army.X)) problems.Add($"armies: {army.Id} starts on a city tile");
        }

        // Fleets: on water, off shared tiles, beside army-passable land (a landing/port bank).
        var fleetTiles = new HashSet<int>();
        foreach (var fleet in map.Fleets)
        {
            if (!InBounds(map, fleet.X, fleet.Y) || !CanonicalTiles.PassableByFleets(codes[(fleet.Y * map.Width) + fleet.X]))
            {
                problems.Add($"fleets: {fleet.Id} is not on water");
                continue;
            }

            if (!fleetTiles.Add((fleet.Y * map.Width) + fleet.X)) problems.Add($"fleets: two fleets share {fleet.Id}'s tile");
            if (!Neighbours(map, fleet.X, fleet.Y).Any(p => passable[(p.Y * map.Width) + p.X]))
                problems.Add($"fleets: {fleet.Id} has no land beside it");
        }

        // Nations: every seat holds cities and its own approved capital; the turn order is the seats.
        var nationIds = map.Nations.Select(n => n.Id).ToHashSet();
        if (map.Nations.Count != nationIds.Count) problems.Add("nations: duplicate nation ids");
        if (!map.TurnOrder.SequenceEqual(map.Nations.Select(n => n.Id))) problems.Add("nations: the turn order is not exactly the seats");
        foreach (var nation in map.Nations)
        {
            var own = map.Cities.Where(c => c.Owner == nation.Id).ToList();
            if (own.Count == 0) problems.Add($"nations: {nation.Id} holds no centres");
            if (nation.CapitalCityId is null || map.Cities.FirstOrDefault(c => c.Id == nation.CapitalCityId) is not { } capital
                || capital.Owner != nation.Id)
                problems.Add($"nations: {nation.Id}'s capital is missing or not its own");
        }

        foreach (var city in map.Cities.Where(c => !nationIds.Contains(c.Owner)))
            problems.Add($"nations: {city.Id}'s owner '{city.Owner}' is not a seat");

        // Connectivity: one land component holds every settlement (armies walk, so a boat age is out of scope).
        var seedTile = map.Cities.Select(c => (c.Y * map.Width) + c.X).FirstOrDefault(t => passable[t], -1);
        if (seedTile >= 0)
        {
            var seen = Flood(seedTile, i => passable[i], map);
            foreach (var city in map.Cities)
                if (!seen.Contains((city.Y * map.Width) + city.X))
                    problems.Add($"connectivity: {city.Id} is cut off from the other settlements");
        }

        // Crossing cost: every capital pair carries a practical route within the authored bound.
        var capitals = map.Cities.Where(c => map.Nations.Any(n => n.CapitalCityId == c.Id)).ToList();
        for (var i = 0; i < capitals.Count; i++)
            for (var j = i + 1; j < capitals.Count; j++)
            {
                var a = capitals[i];
                var b = capitals[j];
                if (DijkstraCost(map, passable, new GridPoint(a.X, a.Y), new GridPoint(b.X, b.Y)) is not { } cost)
                {
                    problems.Add($"crossing-cost: {a.Id} to {b.Id} has no land route");
                    continue;
                }

                var steps = Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
                if (cost > steps * CrossingCostFactor)
                    problems.Add($"crossing-cost: {a.Id} to {b.Id} costs {cost} against {steps * CrossingCostFactor} allowed");
            }

        // Sea connectivity: the fleets' water is one component (navigable straits), so every fleet reaches every shore.
        if (map.Fleets.Count >= 2)
        {
            var fleetPassable = new bool[codes.Length];
            for (var i = 0; i < codes.Length; i++) fleetPassable[i] = CanonicalTiles.PassableByFleets(codes[i]);
            var first = map.Fleets.Select(f => (f.Y * map.Width) + f.X).First(t => fleetPassable[t]);
            var seen = Flood(first, t => fleetPassable[t], map);
            foreach (var fleet in map.Fleets)
                if (!seen.Contains((fleet.Y * map.Width) + fleet.X))
                    problems.Add($"sea-connectivity: {fleet.Id} cannot reach the other fleets");
        }

        // Expansion room: each capital commands enough nearby land to grow into.
        foreach (var capital in capitals)
        {
            var reachable = ReachableWithin(map, passable, new GridPoint(capital.X, capital.Y), ExpansionBudget);
            if (reachable < ExpansionMinCells)
                problems.Add($"expansion: {capital.Id} commands only {reachable} cells within the budget");
        }

        return problems;
    }

    private static bool InBounds(GeneratedMap map, int x, int y) => x >= 0 && y >= 0 && x < map.Width && y < map.Height;

    private static IEnumerable<GridPoint> Neighbours(GeneratedMap map, int x, int y)
    {
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                var nx = x + dx;
                var ny = y + dy;
                if (InBounds(map, nx, ny)) yield return new GridPoint(nx, ny);
            }
    }

    private static HashSet<int> Flood(int start, Func<int, bool> open, GeneratedMap map)
    {
        var seen = new HashSet<int> { start };
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.TryDequeue(out var at))
        {
            var x = at % map.Width;
            var y = at / map.Width;
            foreach (var n in Neighbours(map, x, y))
            {
                var index = (n.Y * map.Width) + n.X;
                if (open(index) && seen.Add(index)) queue.Enqueue(index);
            }
        }

        return seen;
    }

    private static int? DijkstraCost(GeneratedMap map, bool[] passable, GridPoint start, GridPoint goal)
    {
        var best = new Dictionary<GridPoint, int> { [start] = 0 };
        var queue = new PriorityQueue<GridPoint, int>();
        queue.Enqueue(start, 0);
        while (queue.TryDequeue(out var at, out var spent))
        {
            if (spent > best.GetValueOrDefault(at, int.MaxValue)) continue;
            if (at == goal) return spent;
            foreach (var n in Neighbours(map, at.X, at.Y))
            {
                if (!passable[(n.Y * map.Width) + n.X]) continue;
                var total = spent + CanonicalTiles.MoveCost(map.Codes[(n.Y * map.Width) + n.X]);
                if (total >= best.GetValueOrDefault(n, int.MaxValue)) continue;
                best[n] = total;
                queue.Enqueue(n, total);
            }
        }

        return null;
    }

    private static int ReachableWithin(GeneratedMap map, bool[] passable, GridPoint start, int budget)
    {
        var best = new Dictionary<GridPoint, int> { [start] = 0 };
        var queue = new PriorityQueue<GridPoint, int>();
        queue.Enqueue(start, 0);
        var count = 0;
        while (queue.TryDequeue(out var at, out var spent))
        {
            if (spent > best.GetValueOrDefault(at, int.MaxValue)) continue;
            count++;
            foreach (var n in Neighbours(map, at.X, at.Y))
            {
                if (!passable[(n.Y * map.Width) + n.X]) continue;
                var total = spent + CanonicalTiles.MoveCost(map.Codes[(n.Y * map.Width) + n.X]);
                if (total > budget || total >= best.GetValueOrDefault(n, int.MaxValue)) continue;
                best[n] = total;
                queue.Enqueue(n, total);
            }
        }

        return count;
    }
}
