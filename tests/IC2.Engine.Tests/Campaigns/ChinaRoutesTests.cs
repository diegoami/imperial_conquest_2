using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;
using Xunit.Abstractions;

namespace IC2.Engine.Tests.Campaigns;

/// <summary>
/// T293.5 cross-range route check (the T293 contract; the owner's terrain-authoring rule): the
/// China map's army routes must be walkable under the production move-cost table and the
/// production <see cref="MovementWalker"/>. The acceptance names the Hangu/Xiao corridor, Wu
/// Pass, the middle Yellow River fords, Jingxing and the Taihang crossings, the Daba crossings
/// and the gorges, the Ming'e/Fangcheng lines and the Huai corridor.
/// </summary>
public sealed class ChinaRoutesTests(ITestOutputHelper output)
{
    private const int ArmyMoves = 8; // the shipped armies' per-turn budget (build_campaigns.py)

    private static readonly IReadOnlyDictionary<int, (string From, string To)[]> Crossings =
        new Dictionary<int, (string, string)[]>
        {
            [350] =
            [
                ("xianyang", "luoyang"),      // the Hangu-Xiao corridor between Guanzhong and the plain
                ("xianyang", "nanzheng"),     // the Wu Pass route down the Dan into the Han river
                ("xianyang", "chengdu"),      // the Jinniu road through the Daba into Shu
                ("anyi", "jinyang"),          // the Zhongtiao crossing and the middle Yellow River fords
                ("handan", "yuci"),           // the Jingxing country across the Taihang wall
                ("shangcai", "wan"),          // the Fangcheng gap into the Nanyang basin
                ("jiangzhou", "yufu"),        // the Yangtze gorges down Ba's river reach
                ("jiangling", "guangling"),   // the middle Yangtze route to the river mouth
            ],
            [230] =
            [
                ("xianyang", "daliang"),      // the corridor east across Qin's central-plain gains
                ("xianyang", "chengdu"),      // the Jinniu road into the held Sichuan basin
                ("handan", "yuci"),           // the Taihang wall between Zhao's two capitals
                ("shouchun", "wan"),          // the Fangcheng gap from Chu's Huai heartland north
                ("jiangzhou", "yufu"),        // the gorges between Ba's river towns
                ("dai", "yunzhong"),          // the northern rim road on the Ordos edge
            ],
        };

    [Theory]
    [InlineData(350)]
    [InlineData(230)]
    public void Seat_to_seat_routes_cross_the_ranges_at_practical_cost(int year)
    {
        var resolved = GameDataRepository.Load(TestPaths.DataRoot).Resolve($"china-{year}bc");
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
            // 3x, not NearEast's 1.4x: the routes wrap around real ranges and the coarse map's
            // coasts (the Jinniu road pays the Daba; the river routes follow the water). The
            // approved brief adds no terrain change; the routes must be practical (a few turns
            // at the armies' 8-move budget), not shortest-possible.
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
        var resolved = GameDataRepository.Load(TestPaths.DataRoot).Resolve("china-350bc");
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

    /// <summary>The authored pass lines (tools/China/build_campaigns.py), as name and lon/lat polyline.</summary>
    private static readonly IReadOnlyList<(string Name, (double Lon, double Lat)[] Line)> PassLines =
    [
        ("Hangu-Xiao corridor", [(110.35, 34.5), (110.9, 34.42), (111.55, 34.38)]),
        ("Wu Pass route", [(110.2, 33.8), (110.3, 33.3), (110.15, 33.0)]),
        ("Zhongtiao crossing", [(110.7, 35.0), (111.1, 35.2), (111.5, 35.4)]),
        ("Jingxing crossing", [(114.0, 37.9), (114.2, 38.1), (114.4, 38.3)]),
        ("Taihang south crossing", [(113.3, 36.0), (113.5, 36.3), (113.7, 36.6)]),
        ("Jinniu road", [(106.3, 32.6), (106.0, 32.2), (105.7, 31.8)]),
        ("Yangtze gorges", [(110.2, 31.1), (109.8, 30.8), (109.4, 30.5)]),
        ("Fangcheng gap", [(112.8, 33.05), (113.1, 33.0), (113.4, 32.95)]),
        ("Huai corridor", [(115.5, 32.6), (116.2, 32.5), (116.9, 32.4)]),
        ("Nan Ling west gap", [(110.6, 25.0), (111.0, 24.8), (111.4, 24.6)]),
        ("Nan Ling east gap", [(117.0, 25.2), (117.4, 25.0), (117.8, 24.8)]),
    ];

    private const double West = 98.0;
    private const double North = 44.0;
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
