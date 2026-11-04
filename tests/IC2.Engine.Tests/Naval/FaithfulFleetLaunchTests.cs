using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval;
using Xunit;

namespace IC2.Engine.Tests.Naval;

public sealed class FaithfulFleetLaunchTests
{
    public static TheoryData<int, int, (int X, int Y, int Code)[], int, int> LaunchCases => new()
    {
        { 37, 84, [(36,83,0), (37,83,0)], 37,83 }, // Siga, measured.
        { 220,54, [(219,54,0), (219,55,0)], 219,55 }, // Issus, measured.
        { 10,10, [(9,11,0), (11,9,0)], 11,9 }, // Column-major last, not row-major last.
        { 10,10, [(9,9,0), (12,12,0)], 9,9 }, // First radius, not last globally.
        { 10,10, [(8,11,0), (12,10,0)], 12,10 }, // Beyond adjacent tiles.
        { 10,10, [(10,10,0), (11,11,0)], 11,11 }, // City marker covers radius zero.
        { 0,0, [(0,1,0), (1,0,0)], 1,0 }, // Clamped corner.
        { 319,139, [(213,98,0)], 213,98 }, // Excluded edge, distant valid ring.
        { 10,10, [(11,10,1)], 11,10 }, // Both sea codes.
        { 247,50, [(246,50,0), (248,50,0)], 246,50 },
        { 213,99, [(214,98,0), (214,99,0)], 214,98 },
        { 10,10, [], -1,-1 }, // Safe retry; no eligible sea.
        { 248,50, [(248,50,0), (249,50,1)], -1,-1 }, // Only excluded sea.
    };

    [Theory]
    [MemberData(nameof(LaunchCases))]
    public void Faithful_completion_uses_the_original_mode_zero_ring_scan(
        int x, int y, (int X, int Y, int Code)[] sea, int expectedX, int expectedY)
    {
        var result = Complete(x, y, sea);
        var fleet = result.State.FleetById("new")!;
        if (expectedX < 0)
        {
            Assert.True(fleet.IsUnderConstruction);
            Assert.Equal(1, fleet.ConstructionTicksRemaining);
            Assert.Equal((0,0), (fleet.X, fleet.Y));
            Assert.Empty(result.Events.OfType<FleetFinished>());
        }
        else
        {
            Assert.False(fleet.IsUnderConstruction);
            Assert.Equal((expectedX, expectedY), (fleet.X, fleet.Y));
            Assert.Equal(NavalTestbed.Ruleset.Naval.LaunchConditionPercent, fleet.ConditionPercent);
            Assert.Equal(NavalTestbed.Ruleset.Naval.LaunchSupplyTons, fleet.SupplyTons);
            Assert.Equal(0, fleet.Money);
            Assert.Equal(10, fleet.Ships);
            Assert.Single(result.Events.OfType<FleetFinished>());
        }
    }

    [Theory]
    [InlineData(false, false, 320, 140)] // Improved.
    [InlineData(true, true, 320, 140)] // Authored world.
    [InlineData(true, false, 64, 64)] // Different map geometry.
    public void Non_classical_faithful_completion_retains_adjacent_row_major_first(
        bool original, bool customWorld, int width, int height)
    {
        var result = Complete(10,10, [(9,10,0), (9,11,0)], original, customWorld, width, height);
        var fleet = result.State.FleetById("new")!;
        Assert.False(fleet.IsUnderConstruction);
        Assert.Equal((9,10), (fleet.X, fleet.Y));
    }

    [Fact]
    public void Launch_skips_an_existing_fleets_live_marker()
    {
        var result = Complete(37,84, [(36,83,0), (37,83,0)], occupied: true);
        Assert.Equal((36,83), (result.State.FleetById("new")!.X, result.State.FleetById("new")!.Y));
        Assert.NotNull(result.State.FleetById("existing"));
    }

    [Fact]
    public void Successive_completions_see_the_previous_launch_in_fleet_table_order()
    {
        var result = Complete(37,84, [(36,83,0), (37,83,0)], secondOrder: true);
        Assert.Equal((37,83), (result.State.FleetById("new")!.X, result.State.FleetById("new")!.Y));
        Assert.Equal((36,83), (result.State.FleetById("second")!.X, result.State.FleetById("second")!.Y));
        Assert.Equal(2, result.Events.OfType<FleetFinished>().Count());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void City_and_army_markers_also_cover_sea_terrain(bool cityMarker)
    {
        var result = Complete(37,84, [(36,83,0), (37,83,0)], cityMarker: cityMarker, armyMarker: !cityMarker);
        Assert.Equal((36,83), (result.State.FleetById("new")!.X, result.State.FleetById("new")!.Y));
    }

    [Fact]
    public void Improved_keeps_its_previous_terrain_only_launch()
    {
        var result = Complete(37,84, [(37,83,0)], original: false, occupied: true);
        Assert.Equal((37,83), (result.State.FleetById("new")!.X, result.State.FleetById("new")!.Y));
    }

    private static (GameState State, IReadOnlyList<DomainEvent> Events) Complete(
        int x, int y, (int X, int Y, int Code)[] sea, bool original = true,
        bool customWorld = false, int width = 320, int height = 140, bool occupied = false, bool secondOrder = false,
        bool cityMarker = false, bool armyMarker = false)
    {
        var cells = Enumerable.Repeat(2, width * height).ToArray();
        foreach (var tile in sea) cells[tile.Y * width + tile.X] = tile.Code;
        var world = NavalTestbed.Toy.World with {
            Id = customWorld ? "authored" : "classical-mediterranean", Width = width, Height = height,
            Terrain = new TerrainGrid(TerrainEncoding.RunLength,
                ValueList.From(cells.Select(c => new TerrainRun(c, 1)))),
        };
        var rules = NavalTestbed.Ruleset with {
            Flags = NavalTestbed.Ruleset.Flags with {
                AiEconomy = original ? AiEconomyModel.Original : AiEconomyModel.Designed,
            },
        };
        var state = NavalTestbed.InitialState();
        var city = state.Cities[0] with { X = x, Y = y };
        var fleet = new FleetState("new", city.Owner, 0, 0, 0, 10, 0, 50, 100,
            rules.Naval.ConstructionTickStep, city.Id, null, null);
        state = state with {
            Cities = cityMarker ? ValueList.Of(city, city with { Id = "marker", X = 37, Y = 83 }) : ValueList.Of(city),
            Armies = armyMarker ? ValueList.Of(state.Armies[0] with { X = 37, Y = 83, CoveredTileCode = 0 }) : ValueList<ArmyState>.Empty,
            Fleets = occupied ? ValueList.Of(fleet, fleet with {
                Id = "existing", X = 37, Y = 83, ConstructionTicksRemaining = null,
                ConditionPercent = 100, SupplyTons = 1000,
            }) : secondOrder ? ValueList.Of(fleet, fleet with { Id = "second" }) : ValueList.Of(fleet),
        };
        var sink = new RecordingEventSink();
        var coordinator = new TurnCoordinator(SystemRegistry.FromAssemblies(
            [typeof(FleetTickSystem).Assembly], t => t == typeof(FleetTickSystem)), rules, world, sink);
        return (coordinator.RunRoundTick(state).State, sink.Events);
    }
}
