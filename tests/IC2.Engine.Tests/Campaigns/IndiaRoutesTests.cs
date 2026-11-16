using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;
using Xunit.Abstractions;

namespace IC2.Engine.Tests.Campaigns;

/// <summary>
/// T293.4 cross-range route check (the T293 contract; the owner's terrain-authoring rule): the
/// India map's army routes must be walkable under the production move-cost table and the
/// production <see cref="MovementWalker"/>. The acceptance names the Khyber and Bolan crossings
/// and the Vindhya/Ganges routes; the approved brief adds the Deccan north-south crossings.
/// </summary>
public sealed class IndiaRoutesTests(ITestOutputHelper output)
{
    private const int ArmyMoves = 8; // the shipped armies' per-turn budget (build_campaigns.py)

    private static readonly IReadOnlyDictionary<int, (string From, string To)[]> Crossings =
        new Dictionary<int, (string, string)[]>
        {
            [325] =
            [
                ("taxila", "pushkalavati"),   // the Gandhara route past the Khyber corridor
                ("mulasthana", "roruka"),     // the Indus route down from the Malloi country
                ("pataliputra", "varanasi"),  // the Ganges route through Magadha's core
                ("ujjain", "pratishthana"),   // the Narmada gap into the Deccan
                ("pratishthana", "dharanikota"), // the Deccan crossing to the Andhra coast
                ("uraiyur", "madurai"),       // the Palghat gap between the Chola and Pandya coasts
            ],
            [185] =
            [
                ("sagala", "taxila"),         // the Indo-Greek corridor between the hills
                ("sagala", "sibi"),           // the Punjab republics under the ranges
                ("pataliputra", "ujjain"),    // the Ganges-Vindhya route west from Pataliputra
                ("pratishthana", "banavasi"), // the Kanara pass to the western coast
                ("sisupalgarh", "tamralipti"),// the Kalinga-Bengal coastal route
                ("dharanikota", "madurai"),   // the Coromandel coast road to the Pandya kingdom
            ],
        };

    [Theory]
    [InlineData(325)]
    [InlineData(185)]
    public void Seat_to_seat_routes_cross_the_ranges_at_practical_cost(int year)
    {
        var resolved = GameDataRepository.Load(TestPaths.DataRoot).Resolve($"india-{year}bc");
        var world = resolved.World;
        var rules = resolved.Ruleset;
        var cells = world.Terrain.Decode(world.Width, world.Height);
        var checkedPairs = 0;
        foreach (var (from, to) in Crossings[year])
        {
            var a = world.CityById(from);
            var b = world.CityById(to);
            Assert.NotNull(a);
            Assert.NotNull(b);
            checkedPairs++;
            var (cost, route) = CheapestRoute(world, rules, cells, new GridPoint(a!.X, a.Y), new GridPoint(b!.X, b.Y));
            var steps = Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
            output.WriteLine($"{from}->{to}: steps {steps}, cost {cost}, turns {Turns(cost)}");
            // 3x, not NearEast's 1.4x: the Indian routes wrap around real ranges and the coarse
            // map's coasts (the Deccan crossings pay the Ghats; the coastal roads swing wide).
            // The approved brief adds no terrain change; the routes must be practical (a few
            // turns at the armies' 8-move budget), not shortest-possible.
            Assert.True(cost <= steps * 3, $"{from}->{to}: {cost} exceeds 3x the {steps} steps");
            // The cheapest route, walked cell by cell by the production walker, is charged the same.
            Assert.Equal(cost, WalkRoute(world, rules, cells, route));
        }
        Assert.InRange(checkedPairs, 5, Crossings[year].Length);
    }

    /// <summary>
    /// All land is walkable under the shipped table (mountains cost 4, rivers 4), so a pass is a
    /// cost corridor, not a connectivity gate: each authored pass line must carry a cheap route
    /// (plain/desert/forest, cost <= 2) from one end to the other within a two-cell band of the
    /// line. This fails a pass whose corridor breaks into mountains, rivers or water.
    /// </summary>
    [Fact]
    public void The_authored_pass_lines_give_a_cheap_continuous_corridor()
    {
        var resolved = GameDataRepository.Load(TestPaths.DataRoot).Resolve("india-325bc");
        var world = resolved.World;
        var rules = resolved.Ruleset;
        var cells = world.Terrain.Decode(world.Width, world.Height);
        foreach (var (name, line) in PassLines)
        {
            var chain = new List<GridPoint>();
            foreach (var point in Samples(line))
            {
                var cell = Cell(point.Lon, point.Lat);
                if (chain.Count > 0 && chain[^1] == cell) continue;
                chain.Add(cell);
            }
            Assert.True(chain.Count >= 2, $"{name}: the pass line collapses to {chain.Count} cells");

            bool Cheap(GridPoint p)
            {
                var type = world.TileTypeByCode(cells[p.Y * world.Width + p.X]);
                return type is { PassableByArmies: true } && rules.MoveCostFor(type.Id) <= 2;
            }
            var band = new HashSet<GridPoint>();
            foreach (var at in chain)
                for (var dy = -2; dy <= 2; dy++)
                    for (var dx = -2; dx <= 2; dx++)
                    {
                        var (x, y) = (at.X + dx, at.Y + dy);
                        if (x >= 0 && y >= 0 && x < world.Width && y < world.Height)
                            band.Add(new GridPoint(x, y));
                    }
            var best = new Dictionary<GridPoint, int> { [chain[0]] = 0 };
            var queue = new PriorityQueue<GridPoint, int>();
            queue.Enqueue(chain[0], 0);
            while (queue.TryDequeue(out var at, out var spent))
            {
                if (spent > best[at]) continue;
                if (at == chain[^1]) break;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        var next = new GridPoint(at.X + dx, at.Y + dy);
                        if (!band.Contains(next) || !Cheap(next)) continue;
                        var total = spent + rules.MoveCostFor(world.TileTypeByCode(cells[next.Y * world.Width + next.X])!.Id);
                        if (total >= best.GetValueOrDefault(next, int.MaxValue)) continue;
                        best[next] = total; queue.Enqueue(next, total);
                    }
            }
            Assert.True(best.ContainsKey(chain[^1]),
                $"{name}: no cheap corridor runs the pass line from {chain[0]} to {chain[^1]}");
        }
    }

    /// <summary>The authored pass lines (tools/India/build_campaigns.py), as name and lon/lat polyline.</summary>
    private static readonly IReadOnlyList<(string Name, (double Lon, double Lat)[] Line)> PassLines =
    [
        ("Khyber corridor", [(70.9, 34.5), (71.3, 34.1), (71.8, 33.9)]),
        ("Bolan corridor", [(66.6, 30.1), (67.0, 29.5), (67.5, 29.0)]),
        ("Ganges doab road", [(78.0, 28.0), (78.6, 27.4), (79.2, 26.9)]),
        ("Narmada gap", [(74.6, 22.0), (75.2, 21.8), (75.8, 21.9)]),
        ("Naneghat crossing", [(73.5, 18.7), (73.9, 18.3), (74.3, 18.0)]),
        ("Kanara pass", [(74.4, 14.2), (74.8, 13.8), (75.2, 13.4)]),
        ("Palghat gap", [(75.9, 10.9), (76.3, 10.8), (76.7, 10.7)]),
        ("Vindhya east road", [(80.0, 23.6), (80.6, 23.3), (81.2, 23.1)]),
        ("Coastal Coromandel", [(80.2, 15.4), (79.9, 14.6), (79.6, 13.8)]),
    ];

    private const double West = 60.0;
    private const double North = 37.0;
    private const double TilesPerDegreeX = 8.0;
    private const double TilesPerDegreeY = 10.0;

    private static GridPoint Cell(double lon, double lat) =>
        new((int)((lon - West) * TilesPerDegreeX), (int)((North - lat) * TilesPerDegreeY));

    private static IEnumerable<(double Lon, double Lat)> Samples((double Lon, double Lat)[] line)
    {
        for (var s = 0; s < line.Length - 1; s++)
        {
            var (a, b) = (line[s], line[s + 1]);
            for (var t = 0; t < 40; t++)
            {
                var f = (double)t / 40;
                yield return (a.Lon + (b.Lon - a.Lon) * f, a.Lat + (b.Lat - a.Lat) * f);
            }
        }
        yield return line[^1];
    }

    private static int Turns(int cost) => (cost + ArmyMoves - 1) / ArmyMoves;

    private static int WalkRoute(World world, Ruleset rules, int[] cells, IReadOnlyList<GridPoint> route)
    {
        var spent = 0;
        for (var i = 1; i < route.Count; i++)
        {
            var result = MovementWalker.Walk(
                route[i - 1], route[i], int.MaxValue / 2, rules.Terrain,
                p => world.TileTypeByCode(cells[p.Y * world.Width + p.X])?.Id,
                _ => false, NullEventSink.Instance);
            Assert.Equal(MovementStopReason.ReachedDestination, result.StopReason);
            spent += result.MovesSpent;
        }
        return spent;
    }

    private static (int Cost, List<GridPoint> Route) CheapestRoute(
        World world, Ruleset rules, int[] cells, GridPoint start, GridPoint goal)
    {
        int Cost(GridPoint p)
        {
            var type = world.TileTypeByCode(cells[p.Y * world.Width + p.X]);
            return type is { PassableByArmies: true } ? rules.MoveCostFor(type.Id) : -1;
        }
        var best = new Dictionary<GridPoint, int> { [start] = 0 };
        var from = new Dictionary<GridPoint, GridPoint>();
        var queue = new PriorityQueue<GridPoint, int>();
        queue.Enqueue(start, 0);
        while (queue.TryDequeue(out var at, out var spent))
        {
            if (spent > best[at]) continue;
            if (at == goal) break;
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var next = new GridPoint(at.X + dx, at.Y + dy);
                    if ((dx == 0 && dy == 0) || next.X < 0 || next.Y < 0 || next.X >= world.Width || next.Y >= world.Height) continue;
                    var step = Cost(next);
                    if (step < 0) continue;
                    var total = spent + step;
                    if (total >= best.GetValueOrDefault(next, int.MaxValue)) continue;
                    best[next] = total; from[next] = at; queue.Enqueue(next, total);
                }
        }
        Assert.True(best.ContainsKey(goal), "no land route");
        var route = new List<GridPoint> { goal };
        while (route[^1] != start) route.Add(from[route[^1]]);
        route.Reverse();
        return (best[goal], route);
    }
}
