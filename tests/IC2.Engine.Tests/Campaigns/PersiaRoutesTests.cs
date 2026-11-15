using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;
using Xunit.Abstractions;

namespace IC2.Engine.Tests.Campaigns;

/// <summary>
/// T293.2 cross-range route check (the T293 contract; the owner's terrain-authoring rule): the
/// revision-2 plain passes must make the campaign's practical army routes walkable under the
/// production move-cost table and the production <see cref="MovementWalker"/>. The Persian seats
/// need the Zagros (Babylon/Susa to Persis), the Elburz (Ecbatana to Rhagae), the Taurus (Cilicia
/// to Cappadocia) and, at 480 BC, the Greek mountain routes of the invasion theatre.
/// </summary>
public sealed class PersiaRoutesTests(ITestOutputHelper output)
{
    private const int ArmyMoves = 8; // the shipped armies' per-turn budget (build_campaigns.py)

    private static readonly IReadOnlyDictionary<int, (string From, string To)[]> Crossings =
        new Dictionary<int, (string, string)[]>
    {
        [550] =
        [
            ("tarsus", "mazaca"), ("adana", "tyana"), ("babylon", "ecbatana"), ("babylon", "pasargadae"),
            ("susa", "pasargadae"), ("ecbatana", "rhagae"), ("gordion", "sardis"), ("sparta", "olympia"),
            ("sparta", "corinth"),
        ],
        [480] =
        [
            ("tarsus", "mazaca"), ("adana", "tyana"), ("babylon", "ecbatana"), ("susa", "persepolis"),
            ("susa", "pasargadae"), ("ecbatana", "rhagae"), ("gordion", "sardis"), ("sparta", "athens"),
            ("thermopylae", "larisa"), ("aegae", "thermopylae"),
        ],
    };

    [Theory]
    [InlineData(550)]
    [InlineData(480)]
    public void Seat_to_seat_routes_cross_the_ranges_at_practical_cost(int year)
    {
        var resolved = GameDataRepository.Load(TestPaths.DataRoot).Resolve($"persia-{year}bc");
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
            // 2x, not NearEast's 1.4x: the Zagros and Taurus crossings sit at 1.0-1.4x of the straight line,
            // but Greece's own ranges (Taygetos between Sparta and Olympia, Pindos on the Athens road)
            // legitimately detour the route around them. The approved brief adds no terrain change; the
            // routes must be practical (a few turns at the armies' 8-move budget), not shortest-possible.
            Assert.True(cost <= steps * 2, $"{from}->{to}: {cost} exceeds 2x the {steps} steps");
            // The cheapest route, walked cell by cell by the production walker, is charged the same.
            Assert.Equal(cost, WalkRoute(world, rules, cells, route));
        }
        Assert.InRange(checkedPairs, 5, Crossings[year].Length);
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
