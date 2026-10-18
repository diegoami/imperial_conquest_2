using IC2.Engine.Core;
using IC2.Engine.Ai;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Movement;

public sealed class StrategicTravelObservationTests
{
    internal static GameSession Session(bool fleet = false, int moves = 20, SeatControl control = SeatControl.Human,
        SeatAsymmetryModel asymmetry = SeatAsymmetryModel.Normalized, Func<GameState, GameState>? change = null,
        Func<World, World>? changeWorld = null, Ruleset? rules = null)
    {
        var toy = CoreTestbed.Toy;
        var code = toy.World.TileTypes.First(t => t.Id == (fleet ? "sea_calm" : "plain")).Code;
        var world = toy.World with { Terrain = new(TerrainEncoding.RunLength, ValueList<TerrainRun>.Of(new TerrainRun(code, 48))) };
        world = changeWorld?.Invoke(world) ?? world;
        var army = CoreTestbed.InitialState().Armies[0] with { Id = "a", X = 0, Y = 0, Moves = moves, CoveredTileCode = code };
        var ship = new FleetState("f", "north", 0, 0, moves, 30, 100, 100, 100, null, null, null, code);
        var state = CoreTestbed.InitialState() with
        {
            Armies = fleet ? ValueList<ArmyState>.Empty : ValueList<ArmyState>.Of(army),
            Fleets = fleet ? ValueList<FleetState>.Of(ship) : ValueList<FleetState>.Empty,
            Cities = ValueList<CityState>.Empty,
            Nations = ValueList.From(CoreTestbed.InitialState().Nations.Select(n => n with { Control = n.Id == "north" ? control : SeatControl.Human })),
        };
        state = change?.Invoke(state) ?? state;
        var ruleset = rules ?? toy.Ruleset with { Flags = toy.Ruleset.Flags with { SeatAsymmetry = asymmetry } };
        return new(world, ruleset, toy.Scenario, new SaveGame(GameDataSchema.CurrentVersion, "test", "test", toy.Scenario.Id, world.Id, ruleset.Id, state));
    }

    internal static StrategicTravelObservation Route(SessionOutput output) => Assert.Single(output.StrategicTimeline.Steps.SelectMany(s => s.Routes));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Accepted_non_axis_truncation_records_actual_cells_and_not_reconstructed_endpoint(bool fleet)
    {
        var session = Session(fleet, moves: 2);
        var before = session.State;
        var output = session.Submit(fleet ? "move-fleet f 7 3" : "move a 7 3");
        var route = Route(output);
        Assert.Equal(new[] { new GridPoint(1, 0), new GridPoint(2, 1) }, route.EnteredCells);
        Assert.NotEqual(BresenhamPath.Trace(route.Origin, route.Destination).Skip(1), route.EnteredCells);
        Assert.Equal(MovementStopReason.InsufficientMoves, route.StopReason);
        Assert.Equal(new GridPoint(2, 1), route.Destination);
        Assert.Equal(fleet ? StrategicMarkerKind.Fleet : StrategicMarkerKind.Army, route.Kind);
        var action = Assert.Single(output.StrategicTimeline.Steps, s => s.Routes.Count > 0);
        Assert.Same(before, action.Before);
        Assert.Equal(fleet ? session.State.Fleets[0].X : session.State.Armies[0].X, route.Destination.X);
        Assert.Equal("north", route.OwnerId);
        Assert.Equal(output.StrategicTimeline, session.LastStrategicTimeline);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    [InlineData(true, 3)]
    public void Noop_first_blocked_zero_budget_and_refused_orders_emit_no_travel(bool fleet, int mode)
    {
        var session = Session(fleet, moves: mode == 2 ? 0 : 10, change: state => mode == 1
            ? fleet ? state with { Fleets = ValueList.From(state.Fleets.Append(state.Fleets[0] with { Id = "block", X = 1 })) }
                    : state with { Armies = ValueList.From(state.Armies.Append(state.Armies[0] with { Id = "block", X = 1 })) }
            : state);
        var verb = fleet ? "move-fleet" : "move";
        var id = mode == 3 ? "missing" : fleet ? "f" : "a";
        var result = session.Submit($"{verb} {id} {(mode == 0 ? 0 : 2)} 0");
        Assert.Empty(result.StrategicTimeline.Steps.SelectMany(s => s.Routes));
        if (!fleet && mode < 2)
            Assert.Single(result.StrategicTimeline.Steps.SelectMany(s => s.Events).OfType<ArmyMoved>());
        if (fleet) Assert.Empty(result.StrategicTimeline.Steps.SelectMany(s => s.Events).OfType<IC2.Engine.Naval.FleetMoved>());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Occupancy_and_impassable_terrain_stop_before_cell(bool fleet, bool terrain)
    {
        var toy = CoreTestbed.Toy;
        var blockedCode = toy.World.TileTypes.First(t => t.Id == (fleet ? "plain" : "sea_calm")).Code;
        var session = Session(fleet, change: state => terrain ? state
            : fleet ? state with { Fleets = ValueList.From(state.Fleets.Append(state.Fleets[0] with { Id = "block", X = 3 })) }
            : state with { Armies = ValueList.From(state.Armies.Append(state.Armies[0] with { Id = "block", X = 3 })) },
            changeWorld: world => terrain ? world with { Terrain = new(TerrainEncoding.RunLength,
                ValueList<TerrainRun>.Of(new(world.TileTypes.First(t => t.Id == (fleet ? "sea_calm" : "plain")).Code, 3), new(blockedCode, 1), new(world.TileTypes.First(t => t.Id == (fleet ? "sea_calm" : "plain")).Code, 44))) } : world);
        var route = Route(session.Submit(fleet ? "move-fleet f 7 0" : "move a 7 0"));
        Assert.Equal(new[] { new GridPoint(1, 0), new GridPoint(2, 0) }, route.EnteredCells);
        Assert.Equal(new GridPoint(3, 0), route.BlockedAt);
        Assert.Equal(MovementStopReason.BlockedByMarker, route.StopReason);
    }

    [Theory]
    [InlineData(SeatControl.Human, SeatAsymmetryModel.Faithful, 1)]
    [InlineData(SeatControl.Ai, SeatAsymmetryModel.Faithful, 0)]
    [InlineData(SeatControl.Human, SeatAsymmetryModel.Normalized, 0)]
    [InlineData(SeatControl.Ai, SeatAsymmetryModel.Normalized, 0)]
    public void Seat_abort_changes_only_existing_moves_rule(SeatControl control, SeatAsymmetryModel model, int remaining)
    {
        var toy = CoreTestbed.Toy;
        var rules = toy.Ruleset with { Flags = toy.Ruleset.Flags with { SeatAsymmetry = model },
            Terrain = toy.Ruleset.Terrain with { MoveCosts = ValueList.From(toy.Ruleset.Terrain.MoveCosts.Select(c => c.TileTypeId == "plain" ? c with { MoveCost = 2 } : c)) } };
        var session = Session(moves: 3, control: control, rules: rules);
        var output = session.Submit("move a 7 0");
        Assert.Equal(new[] { new GridPoint(1, 0) }, Route(output).EnteredCells);
        Assert.Equal(remaining, session.State.Armies[0].Moves);
    }

    [Fact]
    public void Rough_sea_and_cargo_keep_paid_cells_without_carried_army_travel()
    {
        var cargo = CoreTestbed.InitialState().Armies[0] with { Id = "cargo", X = 0, Y = 0, AboardFleetId = "f", CoveredTileCode = null };
        var session = Session(true, moves: 3, change: s => s with
        {
            Armies = ValueList<ArmyState>.Of(cargo),
            Fleets = ValueList<FleetState>.Of(s.Fleets[0] with { CarriedArmyId = cargo.Id }),
            RoughSeaCells = ValueList<RoughSeaCell>.Of(new RoughSeaCell(2, 0)),
        });
        var output = session.Submit("move-fleet f 7 0");
        var route = Route(output);
        Assert.Equal(new[] { new GridPoint(1, 0) }, route.EnteredCells);
        Assert.Equal("cargo", route.CargoId);
        Assert.Equal(1, session.State.Armies[0].X);
        Assert.All(output.StrategicTimeline.Steps.SelectMany(s => s.MarkersAfter).Where(m => m.EntityId == "cargo"), m => Assert.False(m.Visible));
    }

    [Fact]
    public void Direct_order_to_an_embarked_army_never_exposes_a_hidden_route()
    {
        var session = Session(change: state => state with
        {
            Armies = ValueList<ArmyState>.Of(state.Armies[0] with { AboardFleetId = "carrier", CoveredTileCode = null }),
        });
        var output = session.Submit("move a 2 0");
        Assert.Empty(output.StrategicTimeline.Steps.SelectMany(s => s.Routes));
        Assert.All(output.StrategicTimeline.Steps.SelectMany(s => s.MarkersBefore.Concat(s.MarkersAfter))
            .Where(m => m.EntityId == "a"), m => Assert.False(m.Visible));
    }

    [Fact]
    public void Probe_only_walk_emits_no_observation()
    {
        var session = Session(change: state => state with
        {
            Cities = ValueList<CityState>.Of(CoreTestbed.InitialState().Cities[0] with { Id = "anchor", X = 6, Y = 4 }),
        });
        _ = MovementWalker.Walk(new(0, 0), new(7, 3), 20, session.Ruleset.Terrain,
            _ => "plain", _ => false, NullEventSink.Instance);
        var view = new AiView(session.State, session.Ruleset, session.World, session.State.ActiveNationId);
        Assert.NotNull(AiArmyTargetTree.EmergencyMoveDestination(view, session.State.Armies[0], session.State));
        Assert.Empty(session.Submit("status").StrategicTimeline.Steps);
    }
}

