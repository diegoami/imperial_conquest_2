using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using Fx = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

public sealed class OriginalFleetHuntTests
{
    private static FleetState Fleet(string id, string owner, int x, int y, int ships = 30) =>
        new(id, owner, x, y, 25, ships, 100, 100, 100, null, null, null, 0);

    private static AiView View(IEnumerable<FleetState> fleets, bool war = true, IEnumerable<CityState>? cities = null)
    {
        var toy = AiScriptedStates.Ruleset;
        var rules = toy with { Flags = toy.Flags with { AiEconomy = AiEconomyModel.Original } };
        var world = AiScriptedStates.World with
        {
            Width = 320, Height = 140,
            Terrain = new(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(0, 320 * 140))),
        };
        var state = BattleCommandTestbed.StateWith([Fx.Nation("north"), Fx.Nation("south")], cities ?? [], [], fleets);
        if (war) state = state with { Relations = state.Relations.WithRelation("north", "south", rules.Diplomacy.StateCodes.War) };
        return new(state, rules, world, "north");
    }

    private sealed class ZeroRng : IRng
    {
        public List<int> Bounds { get; } = new();
        public ulong Seed => 1;
        public ulong State => (ulong)Bounds.Count;
        public int NextInt(int exclusiveUpperBound) { Bounds.Add(exclusiveUpperBound); return 0; }
        public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound) => inclusiveLowerBound + NextInt(exclusiveUpperBound - inclusiveLowerBound);
        public ulong NextUInt64() => throw new InvalidOperationException("Unexpected raw draw");
        public bool NextChance(int numerator, int denominator) => NextInt(denominator) < numerator;
        public IRng ForStream(string streamName) => throw new InvalidOperationException("Hunt must consume actual ordered draws");
    }

    [Theory]
    [InlineData(14, 586)]
    [InlineData(18, 300)]
    [InlineData(181, 300)]
    public void Distance_ranks_but_does_not_cut_off_a_hunt(int distance, int score)
    {
        var actor = Fleet("actor", "north", 10, 20, 90);
        var enemy = Fleet("enemy", "south", 10 + distance, 20);
        var rng = new ZeroRng();
        var picked = OriginalFleetHunt.Pick(View([actor, enemy]), actor, rng);
        Assert.Equal(enemy.Id, picked.Fleet!.Id);
        Assert.Equal(score, picked.Score);
        Assert.Equal(distance, picked.Distance);
        Assert.Equal(new[] { 4, 4 }, rng.Bounds);
    }

    [Theory]
    [InlineData(99, 100)]
    [InlineData(100, 101)]
    public void The_returned_score_keeps_the_strict_100_101_boundary(int ships, int expected)
    {
        var actor = Fleet("actor", "north", 10, 20, ships);
        var enemy = Fleet("enemy", "south", 28, 20, 99);
        var picked = OriginalFleetHunt.Pick(View([actor, enemy]), actor, new ZeroRng());
        Assert.Equal(expected, picked.Score);
        Assert.Equal(expected == 101, picked.Score > 100);
    }

    [Fact]
    public void Equal_targets_keep_table_order_and_draw_for_each_eligible_fleet()
    {
        var actor = Fleet("actor", "north", 10, 20, 90);
        var first = Fleet("z-first", "south", 13, 20);
        var second = Fleet("a-last", "south", 10, 23);
        var rng = new ZeroRng();
        Assert.Equal(first.Id, OriginalFleetHunt.Pick(View([actor, first, second]), actor, rng).Fleet!.Id);
        Assert.Equal(new[] { 4, 4, 4 }, rng.Bounds);
        Assert.Equal(second.Id, OriginalFleetHunt.Pick(View([actor, second, first]), actor, new ZeroRng()).Fleet!.Id);
    }

    [Fact]
    public void Peace_and_protected_ports_consume_only_the_actor_draw()
    {
        var actor = Fleet("actor", "north", 10, 20, 90);
        var enemy = Fleet("enemy", "south", 30, 20);
        var rng = new ZeroRng();
        var peaceful = OriginalFleetHunt.Pick(View([actor, enemy], war: false), actor, rng);
        Assert.Null(peaceful.Fleet);
        Assert.Equal(-10000, peaceful.Score);
        Assert.Single(rng.Bounds);
        var harbor = Fx.City("harbor", "harbor", 29, 20, "south", "south", 60, 0, 10, 10, 0);
        rng = new ZeroRng();
        Assert.Null(OriginalFleetHunt.Pick(View([actor, enemy], cities: [harbor]), actor, rng).Fleet);
        Assert.Single(rng.Bounds);
    }

    [Fact]
    public void Friendly_and_construction_records_are_not_eligible_targets()
    {
        var actor = Fleet("actor", "north", 10, 20, 90);
        var friendly = Fleet("friendly", "north", 20, 20);
        var building = Fleet("building", "south", 30, 20) with { ConstructionTicksRemaining = 5 };
        var rng = new ZeroRng();
        Assert.Null(OriginalFleetHunt.Pick(View([actor, friendly, building]), actor, rng).Fleet);
        Assert.Single(rng.Bounds);
    }
}
