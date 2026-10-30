using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Tests.Ai;
using Xunit;

namespace IC2.Engine.Tests.Movement.Commands;

public sealed partial class OriginalFleetMoveTests
{
    private const string FleetId = "fleet-probe";

    private static (GameState State, World World, Ruleset Rules, CommandDispatcher Dispatch, RecordingEventSink Events)
        Setup(int moves = 25, bool cargo = false, GridPoint? origin = null, bool port = false, bool rough = false)
    {
        var game = AiTestbed.Classical;
        var initial = GameStateFactory.CreateInitial(game.World, game.Ruleset, game.Scenario);
        var start = origin ?? new GridPoint(49, 62);
        var cells = game.World.Terrain.Decode(game.World.Width, game.World.Height).ToArray();
        // The published synthetic controls declare sea under the relocated actor marker.
        cells[start.Y * game.World.Width + start.X] = 0;
        var world = game.World with
        {
            Terrain = new TerrainGrid(TerrainEncoding.RunLength, ValueList.From(cells.Select(c => new TerrainRun(c, 1)))),
        };
        var fleet = initial.Fleets.First(f => f.Nation == "carthage") with
        {
            Id = FleetId, X = start.X, Y = start.Y, Moves = moves, Ships = 90, ConditionPercent = 100,
            ConstructionTicksRemaining = null, BuildCityId = null, CarriedArmyId = cargo ? "cargo" : null,
            CoveredTileCode = 0, Money = 1000, SupplyTons = 341,
        };
        var army = initial.Armies.First(a => a.Nation == fleet.Nation) with
        {
            Id = "cargo", X = start.X, Y = start.Y, Moves = 8, CoveredTileCode = null, AboardFleetId = fleet.Id,
        };
        var state = initial with
        {
            Fleets = ValueList.Of(fleet), Armies = cargo ? ValueList.Of(army) : ValueList<ArmyState>.Empty,
            Cities = port ? ValueList.Of(initial.Cities.First(c => c.Owner == fleet.Nation) with
                { Id = "port", X = 47, Y = 63, Owner = fleet.Nation, SupplyTons = 5000 }) : ValueList<CityState>.Empty,
            ActiveSeatIndex = initial.TurnOrder.ToList().IndexOf(fleet.Nation),
            Nations = ValueList.From(initial.Nations.Select(n => n.Id == fleet.Nation ? n with { Control = SeatControl.Ai } : n)),
            RoughSeaCells = rough ? ValueList.Of(new RoughSeaCell(49, 63)) : ValueList<RoughSeaCell>.Empty,
        };
        var sink = new RecordingEventSink();
        return (state, world, game.Ruleset,
            new CommandDispatcher(SystemRegistry.FromEngineAssembly(), game.Ruleset, world, sink), sink);
    }

    private static MoveFleetCommand Move(int x, int y, MoveWalkPolicy policy = MoveWalkPolicy.AiFleetContinuation) =>
        new("carthage", FleetId, x, y, policy);

    [Fact]
    public void Faithful_arrival_lands_at_the_requested_bare_target_instead_of_a_neighbouring_city()
    {
        var f = Setup(cargo: true, origin: new(101, 40));
        var cells = Enumerable.Repeat(2, 320 * 140).ToArray();
        cells[40 * 320 + 101] = 0; // A boxed-in sea cell: CEC8 returns none and charges one.
        var world = f.World with { Terrain = new(TerrainEncoding.RunLength,
            ValueList.From(cells.Select(c => new TerrainRun(c, 1)))) };
        var city = AiReachableFallbackTests.City("occupied-neighbour", "carthage", 100, 39);
        var state = f.State with { Cities = ValueList.Of(city) };
        var dispatch = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), f.Rules, world, f.Events);
        var result = dispatch.Dispatch(state, Move(100, 40));
        Assert.True(result.IsAccepted);
        var landed = result.State.ArmyById("cargo")!;
        Assert.Equal((100, 40, 0), (landed.X, landed.Y, landed.Moves));
        Assert.Null(landed.AboardFleetId);
        Assert.Null(result.State.FleetById(FleetId)!.CarriedArmyId);
        Assert.DoesNotContain(result.State.Armies, a => a.X == city.X && a.Y == city.Y);
        Assert.Equal(24, Assert.Single(f.Events.Events.OfType<OriginalFleetArrival>()).Moves);
    }

    [Theory]
    [InlineData(48, 66, 48, 66, 21)]
    [InlineData(47, 67, 74, 69, 0)]
    [InlineData(47, 64, 74, 69, 0)]
    [InlineData(47, 63, 74, 69, 0)]
    [InlineData(41, 57, 74, 69, 0)]
    public void Promoted_route_controls_use_direct_or_group_zero_waypoints(int x, int y, int endX, int endY, int left)
    {
        var f = Setup();
        var result = f.Dispatch.Dispatch(f.State, Move(x, y));
        Assert.True(result.IsAccepted, result.ToString());
        var fleet = result.State.FleetById(FleetId)!;
        Assert.Equal((endX, endY, left), (fleet.X, fleet.Y, fleet.Moves));
    }

    [Fact]
    public void A_replaced_coastal_target_still_uses_D174_without_a_second_direct_test()
    {
        var f = Setup(cargo: true);
        var view = new AiView(f.State, f.Rules, f.World, "carthage");
        Assert.Equal(new GridPoint(48, 65), ClassicalFleetNavigation.ReplaceLandTarget(view, new(47, 64)));
        Assert.True(ClassicalArmyNavigation.DirectLine(view, new(49, 62), new(48, 65)));
        var waypoint = ClassicalArmyNavigation.FleetIntermediate(view, new(49, 62), new(48, 65));
        Assert.Equal(new GridPoint(75, 69), waypoint.Point);
        Assert.Equal(5, waypoint.NodeId);
        var state = f.Dispatch.Dispatch(f.State, Move(47, 64)).State;
        var fleet = state.FleetById(FleetId)!;
        var cargo = state.ArmyById("cargo")!;
        Assert.Equal((74, 69, 0), (fleet.X, fleet.Y, fleet.Moves));
        Assert.Equal((74, 69, 8, FleetId), (cargo.X, cargo.Y, cargo.Moves, cargo.AboardFleetId));
    }

    [Theory]
    [InlineData(false, 18)]
    [InlineData(true, 0)]
    public void Adjacent_land_swing_spends_seven_moves_then_lands_cargo_at_child_return(bool cargo, int moves)
    {
        var f = Setup(cargo: cargo, origin: new(48, 64));
        var result = f.Dispatch.Dispatch(f.State, Move(47, 64));
        Assert.True(result.IsAccepted, result.ToString());
        var fleet = result.State.FleetById(FleetId)!;
        Assert.Equal((48, 65, moves), (fleet.X, fleet.Y, fleet.Moves));
        if (cargo)
        {
            var landed = result.State.ArmyById("cargo")!;
            Assert.Equal((47, 64, 0), (landed.X, landed.Y, landed.Moves));
            Assert.Null(landed.AboardFleetId);
            Assert.Null(fleet.CarriedArmyId);
            Assert.Contains(f.Events.Events.OfType<OriginalFleetArrival>(), a => a.Depth == 1 && a.ArrivalKind == "landing");
        }
    }

    [Fact]
    public void Own_port_arrival_runs_at_both_returning_levels()
    {
        var f = Setup(origin: new(48, 64), port: true);
        var result = f.Dispatch.Dispatch(f.State, Move(47, 63));
        Assert.True(result.IsAccepted, result.ToString());
        var fleet = result.State.FleetById(FleetId)!;
        Assert.Equal((48, 64, 18), (fleet.X, fleet.Y, fleet.Moves));
        Assert.Equal(new[] { 1, 0 }, f.Events.Events.OfType<OriginalFleetArrival>().Where(a => a.ArrivalKind == "port").Select(a => a.Depth));
        Assert.True(fleet.SupplyTons > f.State.FleetById(FleetId)!.SupplyTons);
    }

    [Theory]
    [InlineData(0, false, 49, 62, 0)]
    [InlineData(1, false, 49, 63, 0)]
    [InlineData(1, true, 49, 62, 0)]
    [InlineData(3, true, 49, 63, 0)]
    public void Zero_budget_and_rough_step_controls(int moves, bool rough, int x, int y, int left)
    {
        var f = Setup(moves, rough: rough);
        var result = f.Dispatch.Dispatch(f.State, Move(49, 63));
        Assert.True(result.IsAccepted, result.ToString());
        var fleet = result.State.FleetById(FleetId)!;
        Assert.Equal((x, y, left), (fleet.X, fleet.Y, fleet.Moves));
    }

    [Fact]
    public void Coincident_CB40_remains_a_safe_no_op_instead_of_copying_one_undefined_stack_value()
    {
        var f = Setup(origin: new(48, 66));
        var result = f.Dispatch.Dispatch(f.State, Move(48, 66));
        Assert.True(result.IsAccepted);
        Assert.Equal(f.State.Fleets, result.State.Fleets);
        Assert.Empty(f.Events.Events.OfType<OriginalFleetArrival>());
    }
}
