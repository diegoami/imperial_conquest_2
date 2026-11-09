using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;
using Xunit.Abstractions;

namespace IC2.Engine.Tests.Campaigns;

/// <summary>
/// #1822: terrain revision 2 of the Near East (crest-only mountains, forest foothills, authored plain
/// passes). Checks use the production move-cost table and the production <see cref="MovementWalker"/>,
/// not a private cost model, and prove that the revision changes tile placement only.
/// </summary>
public sealed class NearEastPassesTests(ITestOutputHelper output)
{
    private const int ArmyMoves = 8; // the shipped armies' per-turn budget (build_campaigns.py)

    private static readonly (string From, string To)[] Crossings =
    [
        ("tarsus", "kanesh"), ("adana", "kanesh"), ("tarsus", "hattusa"), ("assur", "godin"),
        ("arrapha", "giyan"), ("babylon", "giyan"), ("susa", "sialk"), ("susa", "godin"),
        ("nineveh", "tushpa"), ("arbela", "qalaichi"), ("sparta", "argos"), ("pylos", "sparta"),
    ];

    private static (World World, Ruleset Rules, int[] Cells) Load(int year, bool revised)
    {
        var repository = GameDataRepository.Load(TestPaths.DataRoot);
        var resolved = repository.Resolve($"near-east-{year}bc");
        var world = revised ? resolved.World : repository.WorldById($"near-east-{year}bc")!;
        return (world, resolved.Ruleset, world.Terrain.Decode(world.Width, world.Height));
    }

    [Theory]
    [InlineData(1350)]
    [InlineData(750)]
    public void New_games_use_revision_two_while_the_original_world_stays_loadable_for_old_saves(int year)
    {
        var resolved = GameDataRepository.Load(TestPaths.DataRoot).Resolve($"near-east-{year}bc");
        Assert.Equal($"near-east-{year}bc-r2", resolved.World.Id);
        var old = GameDataRepository.Load(TestPaths.DataRoot).WorldById($"near-east-{year}bc");
        Assert.NotNull(old);
        Assert.NotEqual(
            resolved.World.Terrain.Decode(resolved.World.Width, resolved.World.Height),
            old!.Terrain.Decode(old.Width, old.Height));
    }

    [Theory]
    [InlineData(1350)]
    [InlineData(750)]
    public void Revision_two_changes_only_relief_and_adds_no_mountain(int year)
    {
        var (oldWorld, _, before) = Load(year, revised: false);
        var (newWorld, _, after) = Load(year, revised: true);
        Assert.Equal((oldWorld.Width, oldWorld.Height), (newWorld.Width, newWorld.Height));
        var mountain = oldWorld.TileTypes.Single(t => t.Id == "mountains").Code;
        var forest = oldWorld.TileTypes.Single(t => t.Id == "forest").Code;
        var plain = oldWorld.TileTypes.Single(t => t.Id == "plain").Code;
        var changed = 0;
        for (var i = 0; i < before.Length; i++)
        {
            if (before[i] == after[i]) continue;
            changed++;
            // Only mountain->forest/plain and forest->plain: water, rivers, deserts and coast never move.
            Assert.True(
                (before[i] == mountain && (after[i] == forest || after[i] == plain))
                || (before[i] == forest && after[i] == plain),
                $"cell {i} changed {before[i]} -> {after[i]}");
        }
        Assert.InRange(changed, 1, before.Length / 10);
        foreach (var city in newWorld.Cities)
            Assert.True(newWorld.TileTypeByCode(after[city.Y * newWorld.Width + city.X])!.PassableByArmies, city.Name);
        Assert.Equal(oldWorld.Cities.Select(c => (c.Id, c.X, c.Y)), newWorld.Cities.Select(c => (c.Id, c.X, c.Y)));
    }

    [Theory]
    [InlineData(1350)]
    [InlineData(750)]
    public void Representative_crossings_are_cheaper_and_practical_under_production_costs(int year)
    {
        var (oldWorld, rules, before) = Load(year, revised: false);
        var (newWorld, _, after) = Load(year, revised: true);
        var checkedPairs = 0;
        foreach (var (from, to) in Crossings)
        {
            var a = oldWorld.CityById(from); var b = oldWorld.CityById(to);
            if (a is null || b is null) continue; // a centre that exists in only one campaign year
            checkedPairs++;
            var start = new GridPoint(a.X, a.Y); var goal = new GridPoint(b.X, b.Y);
            var oldCost = CheapestRoute(oldWorld, rules, before, start, goal).Cost;
            var (newCost, route) = CheapestRoute(newWorld, rules, after, start, goal);
            var steps = Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
            output.WriteLine($"{from}->{to}: steps {steps}, cost {oldCost} -> {newCost}, turns {Turns(oldCost)} -> {Turns(newCost)}");
            Assert.True(newCost <= oldCost, $"{from}->{to} got dearer");
            // 1.4x, not tighter: Babylon->Giyan still pays for river-valley cells near Babylon, not the belt.
            Assert.True(newCost <= steps * 14 / 10, $"{from}->{to}: {newCost} exceeds 1.4x the {steps} steps");

            // The cheapest route, walked cell by cell by the production walker, is charged the same.
            var walked = WalkRoute(newWorld, rules, after, route);
            Assert.Equal(newCost, walked);
        }
        Assert.InRange(checkedPairs, 5, Crossings.Length);
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
