using IC2.Engine.Barbarians;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Barbarians;

public sealed class BarbarianArrivalPlannerTests
{
    private static readonly BarbarianArrivalPolicy Policy = new("north");

    [Fact]
    public void Disabled_policy_is_inert_and_draws_nothing()
    {
        var (world, state) = Map("lll", "lll", "lll");
        var rng = new ScriptedRng();
        Assert.Null(BarbarianArrivalPlanner.Plan(world, state, null, rng));
        Assert.Equal(0, rng.Calls);
    }

    [Theory]
    [InlineData("lll")]
    [InlineData("l", "l", "l")]
    [InlineData("l")]
    [InlineData("lll", "lll", "lll")]
    public void Land_entry_is_in_bounds_on_perimeter_without_duplicate_corners(params string[] rows)
    {
        var (world, state) = Map(rows);
        var rng = new ScriptedRng(pickLast: true);
        var arrival = BarbarianArrivalPlanner.Plan(world, state, Policy, rng);
        Assert.Equal(new BarbarianArrival(BarbarianArrivalKind.Land,
            new GridPoint(world.Width - 1, world.Height - 1)), arrival);
        Assert.Equal(1, rng.ChanceCalls);
        var expectedPerimeter = world.Width == 1 || world.Height == 1
            ? world.Width * world.Height : 2 * world.Width + 2 * world.Height - 4;
        Assert.Equal(new[] { expectedPerimeter }, rng.IntegerBounds);
    }

    [Fact]
    public void Chance_failure_produces_no_arrival_or_selection_draws()
    {
        var (world, state) = Map("lll", "lll", "lll");
        var rng = new ScriptedRng(chance: false);
        Assert.Null(BarbarianArrivalPlanner.Plan(world, state, Policy, rng));
        Assert.Equal(1, rng.ChanceCalls);
        Assert.Empty(rng.IntegerBounds);
    }

    [Fact]
    public void Zero_chance_draws_nothing_and_other_nations_do_not_use_the_cap()
    {
        var (world, state) = Map("lll", "lll", "lll");
        var rng = new ScriptedRng();
        Assert.Null(BarbarianArrivalPlanner.Plan(world, state, Policy with { ChancePercent = 0 }, rng));
        Assert.Equal(0, rng.Calls);
        state = state with { Armies = new ValueList<ArmyState>(Enumerable.Range(0, 4)
            .Select(i => Army($"a{i}", 1, 1, "south"))) };
        Assert.NotNull(BarbarianArrivalPlanner.Plan(world, state, Policy, rng));
    }

    [Fact]
    public void Army_cap_nearly_reached_still_permits_one_more_arrival()
    {
        var (world, state) = Map("lll", "lll", "lll");
        state = state with { Armies = new ValueList<ArmyState>(Enumerable.Range(0, 3)
            .Select(i => Army($"a{i}", 1, 1))) };
        Assert.NotNull(BarbarianArrivalPlanner.Plan(world, state, Policy, new ScriptedRng()));
    }

    [Theory]
    [InlineData("rrr", "rlr", "rrr")]
    [InlineData("sss", "sss", "sss")]
    [InlineData("rrr", "rrr", "rrr")]
    public void Interior_land_or_sea_without_landing_cannot_spawn(params string[] rows)
    {
        var (world, state) = Map(rows);
        var rng = new ScriptedRng();
        Assert.Null(BarbarianArrivalPlanner.Plan(world, state, Policy, rng));
        Assert.Equal(0, rng.Calls);
    }

    [Theory]
    [InlineData("army")]
    [InlineData("fleet")]
    [InlineData("city")]
    public void Occupied_only_entry_is_excluded(string occupant)
    {
        var (world, state) = Map("rrr", "lrr", "rrr");
        state = occupant switch
        {
            "army" => state with { Armies = ValueList<ArmyState>.Of(Army("a", 0, 1, "south")) },
            "fleet" => state with { Fleets = ValueList<FleetState>.Of(Fleet("f", 0, 1, "south")) },
            _ => state with { Cities = ValueList<CityState>.Of(
                GameStateFactory.CreateInitial(ToyFixtures.Toy.World, ToyFixtures.Toy.Ruleset,
                    ToyFixtures.Toy.Scenario).Cities[0] with { X = 0, Y = 1 }) },
        };
        var rng = new ScriptedRng();
        Assert.Null(BarbarianArrivalPlanner.Plan(world, state, Policy, rng));
        Assert.Equal(0, rng.Calls);
    }

    [Fact]
    public void Mixed_edges_choose_mode_before_tile_and_preserve_state()
    {
        var (world, state) = Map("sss", "sss", "lll");
        var before = state;
        var seaRng = new ScriptedRng(values: new[] { 1, 0 });
        Assert.Equal(new BarbarianArrival(BarbarianArrivalKind.Sea, new GridPoint(0, 0)),
            BarbarianArrivalPlanner.Plan(world, state, Policy, seaRng));
        Assert.Equal(new[] { 2, 5 }, seaRng.IntegerBounds);
        var landRng = new ScriptedRng(values: new[] { 0, 2 });
        Assert.Equal(new BarbarianArrival(BarbarianArrivalKind.Land, new GridPoint(2, 2)),
            BarbarianArrivalPlanner.Plan(world, state, Policy, landRng));
        Assert.Same(before, state);
        Assert.Empty(state.Armies);
        Assert.Empty(state.Fleets);
    }

    [Fact]
    public void Fleet_cap_removes_sea_choice_but_not_land_choice()
    {
        var (world, state) = Map("sss", "sss", "lll");
        state = state with { Fleets = new ValueList<FleetState>(Enumerable.Range(0, 4)
            .Select(i => Fleet($"f{i}", 1, 1))) };
        var rng = new ScriptedRng();
        var arrival = BarbarianArrivalPlanner.Plan(world, state, Policy, rng);
        Assert.Equal(BarbarianArrivalKind.Land, arrival!.Kind);
        Assert.Equal(new[] { 3 }, rng.IntegerBounds);
    }

    [Fact]
    public void Carried_armies_count_against_total_army_cap()
    {
        var (world, state) = Map("lll", "lll", "lll");
        state = state with { Armies = new ValueList<ArmyState>(Enumerable.Range(0, 4)
            .Select(i => Army($"a{i}", 1, 1) with { AboardFleetId = $"f{i}" })) };
        var rng = new ScriptedRng();
        Assert.Null(BarbarianArrivalPlanner.Plan(world, state, Policy, rng));
        Assert.Equal(0, rng.Calls);
    }

    [Fact]
    public void Sea_entry_requires_a_currently_clear_route_to_a_landing()
    {
        var (world, state) = Map("rrrrr", "rrrrr", "ssslr", "rrrrr", "rrrrr");
        Assert.Equal(new BarbarianArrival(BarbarianArrivalKind.Sea, new GridPoint(0, 2)),
            BarbarianArrivalPlanner.Plan(world, state, Policy, new ScriptedRng()));
        state = state with { Fleets = ValueList<FleetState>.Of(Fleet("block", 1, 2, "south")) };
        var rng = new ScriptedRng();
        Assert.Null(BarbarianArrivalPlanner.Plan(world, state, Policy, rng));
        Assert.Equal(0, rng.Calls);
    }

    [Fact]
    public void Occupied_landing_cannot_make_an_isolated_sea_entry_eligible()
    {
        var (world, state) = Map("rrrrr", "rrrrr", "ssslr", "rrrrr", "rrrrr");
        state = state with { Armies = ValueList<ArmyState>.Of(Army("block", 3, 2, "south")) };
        Assert.Null(BarbarianArrivalPlanner.Plan(world, state, Policy, new ScriptedRng()));
    }

    [Theory]
    [InlineData(-1, 4, 4)]
    [InlineData(101, 4, 4)]
    [InlineData(25, 0, 4)]
    [InlineData(25, 4, 0)]
    public void Invalid_policy_is_an_error(int chance, int armies, int fleets)
    {
        var (world, state) = Map("lll");
        Assert.Throws<ArgumentOutOfRangeException>(() => BarbarianArrivalPlanner.Plan(world, state,
            new BarbarianArrivalPolicy("north", chance, armies, fleets), new ScriptedRng()));
    }

    [Fact]
    public void Missing_nation_and_mismatched_world_are_errors()
    {
        var (world, state) = Map("lll");
        Assert.Throws<ArgumentException>(() => BarbarianArrivalPlanner.Plan(world, state,
            Policy with { NationId = "absent" }, new ScriptedRng()));
        Assert.Throws<ArgumentException>(() => BarbarianArrivalPlanner.Plan(world,
            state with { WorldId = "different" }, Policy, new ScriptedRng()));
    }

    [Fact]
    public void Seeded_stream_replays_the_same_arrivals_without_touching_input_state()
    {
        var (world, state) = Map("sss", "sss", "lll");
        var left = SplitMix64Rng.ForStream(12345, "barbarians.arrivals");
        var right = SplitMix64Rng.ForStream(12345, "barbarians.arrivals");
        var originalSeed = state.RandomSeed;
        var sawLand = false;
        var sawSea = false;
        for (var i = 0; i < 100; i++)
        {
            var arrival = BarbarianArrivalPlanner.Plan(world, state, Policy, left);
            Assert.Equal(arrival, BarbarianArrivalPlanner.Plan(world, state, Policy, right));
            sawLand |= arrival?.Kind == BarbarianArrivalKind.Land;
            sawSea |= arrival?.Kind == BarbarianArrivalKind.Sea;
        }
        Assert.True(sawLand);
        Assert.True(sawSea);
        Assert.Equal(originalSeed, state.RandomSeed);
    }

    private static (World World, GameState State) Map(params string[] rows)
    {
        var toy = ToyFixtures.Toy;
        var runs = rows.SelectMany(row => row.Select(c => new TerrainRun(c switch
        {
            's' => 0, 'l' => 1, _ => 2,
        }, 1)));
        var world = toy.World with
        {
            Width = rows[0].Length, Height = rows.Length,
            Terrain = new TerrainGrid(TerrainEncoding.RunLength, new ValueList<TerrainRun>(runs)),
            TileTypes = ValueList<TileType>.Of(new("sea", 0, "Sea", false, true),
                new("land", 1, "Land", true, false), new("rock", 2, "Rock", false, false)),
        };
        var state = GameStateFactory.CreateInitial(toy.World, toy.Ruleset, toy.Scenario) with
        {
            Cities = ValueList<CityState>.Empty,
            Armies = ValueList<ArmyState>.Empty,
            Fleets = ValueList<FleetState>.Empty,
        };
        return (world, state);
    }

    private static ArmyState Army(string id, int x, int y, string nation = "north") =>
        new(id, nation, x, y, 0, 100, 0, 0, null, null, ValueList<UnitSlot>.Empty);

    private static FleetState Fleet(string id, int x, int y, string nation = "north") =>
        new(id, nation, x, y, 0, 10, 100, 0, 0, null, null, null, null);

    private sealed class ScriptedRng(bool chance = true, bool pickLast = false, int[]? values = null) : IRng
    {
        private int _index;
        public int ChanceCalls { get; private set; }
        public List<int> IntegerBounds { get; } = new();
        public int Calls => ChanceCalls + IntegerBounds.Count;
        public ulong Seed => 0;
        public ulong State => (ulong)Calls;
        public bool NextChance(int numerator, int denominator)
        {
            Assert.Equal(25, numerator);
            Assert.Equal(100, denominator);
            ChanceCalls++;
            return chance;
        }
        public int NextInt(int exclusiveUpperBound)
        {
            IntegerBounds.Add(exclusiveUpperBound);
            var result = pickLast ? exclusiveUpperBound - 1 : values?[_index++] ?? 0;
            Assert.InRange(result, 0, exclusiveUpperBound - 1);
            return result;
        }
        public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound) => throw new NotSupportedException();
        public ulong NextUInt64() => throw new NotSupportedException();
        public IRng ForStream(string streamName) => throw new NotSupportedException();
    }
}
