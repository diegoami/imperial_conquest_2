using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;
using Xunit.Abstractions;

namespace IC2.Engine.Tests.Campaigns;

/// <summary>
/// T293.3 cross-range route check (the T293 contract; the owner's terrain-authoring rule): the
/// Mediterranean map's army routes must be walkable under the production move-cost table and the
/// production <see cref="MovementWalker"/>. The acceptance names the Alpine and Pyrenean crossings
/// and the Greek corridors; Hannibal's 218 corridor (Rome to Saguntum) is the campaign-defining one.
/// </summary>
public sealed class MediterraneanRoutesTests(ITestOutputHelper output)
{
    private const int ArmyMoves = 8; // the shipped armies' per-turn budget (build_campaigns.py)

    private static readonly IReadOnlyDictionary<int, (string From, string To)[]> Crossings =
        new Dictionary<int, (string, string)[]>
        {
            [431] =
            [
                ("sparta", "olympia"),       // the Taygetus crossing (Peloponnese)
                ("ambracia", "larisa"),      // the Pindos corridor
                ("sardis", "tarsus"),        // the Cilician Gates (Taurus)
                ("rome", "massalia"),        // the Ligurian coast road past the Apennines/Alps
                ("massalia", "emporion"),    // the coastal Pyrenees and the Rhone crossing
                ("syracuse", "akragas"),     // the Sicilian interior
            ],
            [280] =
            [
                ("rome", "taras"),           // the Apennines (Cisa/Adriatic passes)
                ("rome", "rhegium"),         // the Italian peninsula end to end
                ("ambracia", "thermon"),     // the Pindos foothills into Aetolia
                ("massalia", "emporion"),    // the coastal Pyrenees
                ("saguntum", "gades"),       // the Iberian east and south coast, past the Betic ranges
                ("tarsus", "mazaca"),        // the Cilician Gates (Taurus)
            ],
            [218] =
            [
                ("rome", "saguntum"),        // Hannibal's corridor: Apennines, Rhone, Pyrenees, Iberia
                ("saguntum", "carthago_nova"),
                ("carthago_nova", "gades"),  // the Betic ranges
                ("massalia", "emporion"),    // the coastal Pyrenees
                ("sparta", "olympia"),       // the Taygetus crossing (Peloponnese)
                ("tarsus", "mazaca"),        // the Cilician Gates (Taurus)
            ],
        };

    [Theory]
    [InlineData(431)]
    [InlineData(280)]
    [InlineData(218)]
    public void Seat_to_seat_routes_cross_the_ranges_at_practical_cost(int year)
    {
        var resolved = GameDataRepository.Load(TestPaths.DataRoot).Resolve($"mediterranean-{year}bc");
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
            // 3x, not NearEast's 1.4x: the Mediterranean pairs wrap around real ranges and the
            // coarse map coast (Ambracia-Thermon pays the Pindos foothills; Massalia-Emporion the
            // Pyrenees), so the routes legitimately cost up to three times the straight line. The
            // approved brief adds no terrain change; the routes must be practical (a few turns at
            // the armies' 8-move budget), not shortest-possible.
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
    /// line. This tolerates a coast-quantized line hugging the sea (the Ligurian coast road) while
    /// failing a pass whose corridor breaks into mountains, rivers or water.
    /// </summary>
    [Fact]
    public void The_authored_pass_lines_give_a_cheap_continuous_corridor()
    {
        var resolved = GameDataRepository.Load(TestPaths.DataRoot).Resolve("mediterranean-431bc");
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
            Assert.True(chain.Count >= 3, $"{name}: the pass line collapses to {chain.Count} cells");

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

    /// <summary>The authored pass lines (tools/Mediterranean/build_campaigns.py), as name and lon/lat polyline.</summary>
    private static readonly IReadOnlyList<(string Name, (double Lon, double Lat)[] Line)> PassLines =
    [
        ("Mont Genevre (Alps)", [(6.6, 44.8), (7.0, 45.2), (7.4, 45.6)]),
        ("Coastal Pyrenees", [(1.4, 42.4), (2.2, 42.3), (2.9, 42.3)]),
        ("Somport (Pyrenees)", [(0.1, 42.7), (0.8, 42.6), (1.4, 42.6)]),
        ("Ligurian coast road", [(8.6, 44.5), (9.2, 44.3), (9.8, 44.2)]),
        ("Cisa (Apennines)", [(9.9, 44.4), (10.4, 44.3), (10.9, 44.2)]),
        ("Adriatic Apennines", [(13.3, 42.9), (13.7, 42.7)]),
        ("Sicilian interior", [(14.2, 37.7), (14.9, 37.5), (15.1, 37.5)]),
        ("Tunisian passes", [(8.8, 36.2), (9.3, 35.7), (9.7, 35.2)]),
        ("Taygetus crossing (Peloponnese)", [(21.7, 37.0), (22.0, 37.0), (22.4, 37.1), (22.6, 37.5), (22.72, 37.63)]),
        ("Pindos corridor", [(21.2, 40.1), (21.6, 39.6), (22.0, 39.0)]),
        ("Succi (Haemus)", [(24.2, 42.5), (24.9, 42.5), (25.5, 42.5)]),
        ("Dinaric coast road", [(15.9, 44.9), (16.8, 44.1), (17.6, 43.3), (18.3, 42.6)]),
        ("Cilician Gates (Taurus)", [(34.85, 36.9), (34.75, 37.5), (34.7, 38.2)]),
        ("Belen (Syrian gates)", [(36.4, 36.5), (36.8, 36.3)]),
        ("Coele corridor", [(35.9, 34.2), (36.2, 34.6)]),
    ];

    private const double West = -10.0;
    private const double North = 46.0;
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
