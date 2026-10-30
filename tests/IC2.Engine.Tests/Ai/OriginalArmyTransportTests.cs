using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Movement.Commands;
using Xunit;

namespace IC2.Engine.Tests.Ai;

public sealed class OriginalArmyTransportTests
{
    private static (GameState State, World World, Ruleset Rules, RecordingEventSink Events, CommandDispatcher Dispatch)
        Setup(int armyX, int shipX, bool aboard = false)
    {
        var game = AiTestbed.Classical;
        var initial = GameStateFactory.CreateInitial(game.World, game.Ruleset, game.Scenario);
        var cells = new int[320 * 140];
        for (var y = 0; y < 140; y++)
            for (var x = 0; x <= 100; x++) cells[y * 320 + x] = 2;
        cells[90 * 320 + 120] = 2; // Requested land target across a geographic/sea boundary.
        var world = game.World with { Terrain = new(TerrainEncoding.RunLength, ValueList.From(cells.Select(c => new TerrainRun(c, 1)))) };
        var army = initial.Armies.First(a => a.Nation == "carthage") with
        {
            Id = "army", X = armyX, Y = 40, Moves = 8, CoveredTileCode = aboard ? null : 2,
            AboardFleetId = aboard ? "ship" : null,
            Units = ValueList.Of(IC2.Engine.Tests.Cities.Capture.CaptureTestbed.Unit("light_infantry", 10_000)),
        };
        var fleet = initial.Fleets.First(f => f.Nation == "carthage") with
        {
            Id = "ship", X = shipX, Y = 40, Moves = 25, Ships = 90,
            ConstructionTicksRemaining = null, BuildCityId = null, CarriedArmyId = aboard ? army.Id : null, CoveredTileCode = 0,
        };
        var state = initial with
        {
            Armies = ValueList.Of(army), Fleets = ValueList.Of(fleet), Cities = ValueList<CityState>.Empty,
            ActiveSeatIndex = initial.TurnOrder.ToList().IndexOf("carthage"),
            Nations = ValueList.From(initial.Nations.Select(n => n.Id == "carthage" ? n with { Control = SeatControl.Ai } : n)),
        };
        var events = new RecordingEventSink();
        return (state, world, game.Ruleset, events,
            new CommandDispatcher(SystemRegistry.FromEngineAssembly(), game.Ruleset, world, events));
    }

    private static MoveArmyCommand Travel(int x, int y) => new("carthage", "army", x, y, MoveWalkPolicy.AiContinuation, x, y);

    [Fact]
    public void Distant_ferry_writes_first_sea_request_and_walks_the_army_to_last_land()
    {
        var f = Setup(98, 105);
        var result = f.Dispatch.Dispatch(f.State, Travel(120, 90));
        Assert.True(result.IsAccepted, result.ToString());
        var army = result.State.ArmyById("army")!;
        Assert.Equal((100, 40, 6), (army.X, army.Y, army.Moves));
        Assert.Equal(f.State.Fleets, result.State.Fleets);
        Assert.Equal(new OriginalFleetRouteRequested("ship", 101, 40), Assert.Single(f.Events.Events.OfType<OriginalFleetRouteRequested>()));
    }

    [Fact]
    public void Already_at_shore_keeps_the_request_even_when_no_army_step_is_available()
    {
        var f = Setup(100, 105);
        var result = f.Dispatch.Dispatch(f.State, Travel(120, 90));
        Assert.True(result.IsAccepted);
        Assert.Equal(f.State.Armies, result.State.Armies);
        Assert.Equal(new OriginalFleetRouteRequested("ship", 101, 40), Assert.Single(f.Events.Events.OfType<OriginalFleetRouteRequested>()));
    }

    [Fact]
    public void Adjacent_usable_ferry_embarks_immediately_with_both_budgets_zero()
    {
        var f = Setup(100, 101);
        var result = f.Dispatch.Dispatch(f.State, Travel(120, 90));
        Assert.True(result.IsAccepted, result.ToString());
        var army = result.State.ArmyById("army")!;
        Assert.Equal((101, 40, 0, "ship"), (army.X, army.Y, army.Moves, army.AboardFleetId));
        Assert.Equal((0, "army"), (result.State.FleetById("ship")!.Moves, result.State.FleetById("ship")!.CarriedArmyId));
        Assert.Empty(f.Events.Events.OfType<OriginalFleetRouteRequested>());
    }

    [Fact]
    public void Carried_army_spends_two_and_queues_last_land_without_sailing_yet()
    {
        var f = Setup(105, 105, aboard: true);
        var routes = new OriginalFleetRoutes();
        var result = f.Dispatch.Dispatch(f.State, Travel(98, 40), routes.Capture(f.Events));
        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(6, result.State.ArmyById("army")!.Moves);
        Assert.Equal(f.State.Fleets, result.State.Fleets);
        Assert.Equal(new GridPoint(100, 40), routes.For("ship"));
        var nextSeat = new OriginalFleetRoutes();
        Assert.Null(nextSeat.For("ship"));
        Assert.DoesNotContain(typeof(FleetState).GetProperties(), p => p.Name.Contains("Route", StringComparison.Ordinal));
    }

    [Fact]
    public void Ordinary_CLI_policy_still_refuses_to_move_an_embarked_army()
    {
        var f = Setup(105, 105, aboard: true);
        var result = f.Dispatch.Dispatch(f.State, new MoveArmyCommand("carthage", "army", 98, 40));
        Assert.True(result.IsRejected);
        Assert.Equal(f.State, result.State);
        Assert.Empty(f.Events.Events.OfType<OriginalFleetRouteRequested>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    public void Accepted_army_request_is_consumed_by_the_real_following_fleet_phase(int fleetMoves)
    {
        var f = Setup(105, 105, aboard: true);
        var state = f.State with { Fleets = ValueList.Of(f.State.Fleets[0] with { Moves = fleetMoves }) };
        var routes = new OriginalFleetRoutes();
        var sink = routes.Capture(f.Events);
        var army = f.Dispatch.Dispatch(state, Travel(98, 40), sink);
        Assert.True(army.IsAccepted);
        var phase = OriginalFleetPhase.Run(army.State, f.Rules, f.World, "carthage", routes, f.Dispatch,
            sink, SplitMix64Rng.ForStream(1, "phase"), new());
        Assert.Equal(0, phase.Rejected);
        Assert.Equal(1, phase.Attempts);
        var decision = Assert.Single(f.Events.Events.OfType<OriginalFleetDecision>());
        Assert.Equal(("stored-route", 100, 40), (decision.Branch, decision.X, decision.Y));
        if (fleetMoves == 0) Assert.Equal(army.State.Fleets, phase.State.Fleets);
        else Assert.NotEqual((105, 40), (phase.State.Fleets[0].X, phase.State.Fleets[0].Y));
        foreach (var cargo in phase.State.Armies.Where(a => a.IsEmbarked))
        {
            var ship = phase.State.FleetById(cargo.AboardFleetId!)!;
            Assert.Equal((ship.X, ship.Y, cargo.Id), (cargo.X, cargo.Y, ship.CarriedArmyId));
        }
    }

    [Fact]
    public void Immediate_carried_mismatch_clears_an_older_request_and_still_charges_two()
    {
        var f = Setup(101, 101, aboard: true);
        var routes = new OriginalFleetRoutes();
        routes.Set("ship", new(98, 40));
        var state = f.State with { Armies = ValueList.Of(f.State.Armies[0] with { Moves = 1 }) };
        var result = f.Dispatch.Dispatch(state, Travel(100, 40), routes.Capture(f.Events));
        Assert.True(result.IsAccepted);
        Assert.Equal(-1, result.State.Armies[0].Moves);
        Assert.Null(routes.For("ship"));
        Assert.Equal(-1, Assert.Single(f.Events.Events.OfType<OriginalFleetRouteRequested>()).X);
    }

    [Fact]
    public void Real_AI_turn_queues_then_executes_a_carried_armys_request_without_persisting_it()
    {
        var f = Setup(105, 105, aboard: true);
        var city = f.State.Nations.First(n => n.Id == "carthage");
        var state = f.State with
        {
            Cities = ValueList.Of(AiReachableFallbackTests.City("resupply", city.Id, 98, 40, supply: 5000)),
        };
        var result = AiTurn.Run(state, f.Rules, f.World, f.Dispatch,
            SplitMix64Rng.ForStream(1, "turn"), f.Events);
        Assert.Equal(0, result.CommandsRejected);
        Assert.NotEmpty(f.Events.Events.OfType<OriginalFleetRouteRequested>());
        Assert.Contains(f.Events.Events.OfType<OriginalFleetDecision>(), d => d.Branch == "stored-route");
        Assert.NotEqual((105, 40), (result.State.Fleets[0].X, result.State.Fleets[0].Y));
        Assert.Null(new OriginalFleetRoutes().For("ship"));
    }

    [Fact]
    public void Even_zero_move_fleet_gets_its_support_once_before_the_port_pick()
    {
        var f = Setup(98, 101);
        var near = AiReachableFallbackTests.City("near", "carthage", 100, 40, supply: 21);
        var far = AiReachableFallbackTests.City("far", "carthage", 80, 40, supply: 5000);
        // Make far a port without adding a path: E920 needs only one local sea cell.
        var cells = f.World.Terrain.Decode(320, 140).ToArray();
        cells[39 * 320 + 80] = 0;
        var world = f.World with { Terrain = new(TerrainEncoding.RunLength,
            ValueList.From(cells.Select(c => new TerrainRun(c, 1)))) };
        var ship = f.State.Fleets[0] with { Moves = 0, SupplyTons = 0, Money = 0, ConditionPercent = 40 };
        var state = f.State with { Cities = ValueList.Of(near, far), Fleets = ValueList.Of(ship),
            Armies = ValueList<ArmyState>.Empty };
        Assert.Equal(near.Id, OriginalFleetPort.Pick(new(state, f.Rules, world, "carthage"), ship)!.Id);
        var expected = AiResupplyPass.Run(state, f.Rules, "carthage", onlyFleetId: ship.Id);
        var result = OriginalFleetPhase.Run(state, f.Rules, world, "carthage", new(), f.Dispatch,
            f.Events, SplitMix64Rng.ForStream(1, "prepass"), new());
        Assert.Equal(expected.State, result.State);
        Assert.Equal(0, result.Attempts);
        Assert.Equal(0, result.State.CityById(near.Id)!.SupplyTons);
        Assert.Equal(far.Id, Assert.Single(f.Events.Events.OfType<OriginalFleetDecision>()).PortId);
    }
}
