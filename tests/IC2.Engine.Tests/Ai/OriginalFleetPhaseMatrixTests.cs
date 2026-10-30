using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using Fx = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

public sealed class OriginalFleetPhaseMatrixTests
{
    public sealed record FleetInput(string Id, string Owner, int X, int Y, int Moves, int Ships,
        int Condition, bool Building, int RouteX, int RouteY);
    public sealed record Expected(string Id, int X, int Y, string Branch);
    public sealed record Control(string Name, FleetInput[] Fleets, Expected[] Expected, int Draws,
        bool War, bool Protected, bool EasternPort);

    // Interpreted decision controls from phase-v2 (d3542f4), not original record bytes.
    // Compare F608's dispatch boundary, excluding E1FC recursion/combat. Synthetic own ports
    // and post-support actor condition reproduce the declared choice inputs; prepass is tested separately.
    public static IEnumerable<object[]> Controls => new object[][]
    {
        new object[] { new Control("cargo-positive", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, 48, 66), new FleetInput("f1", "south", 0, 0, 0, 70, 214, true, -1, -1)], [new Expected("f0", 48, 66, "stored-route")], 0, true, false, false) },
        new object[] { new Control("cargo-zero", [new FleetInput("f0", "north", 49, 62, 0, 90, 100, false, 48, 66), new FleetInput("f1", "south", 0, 0, 0, 70, 214, true, -1, -1)], [new Expected("f0", 48, 66, "stored-route")], 0, true, false, false) },
        new object[] { new Control("phase-actor-construction", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, true, -1, -1), new FleetInput("f1", "south", 48, 65, 25, 70, 100, false, -1, -1)], [], 0, true, false, false) },
        new object[] { new Control("phase-alliance", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, -1, -1), new FleetInput("f1", "south", 48, 65, 25, 70, 100, false, -1, -1)], [new Expected("f0", 45, 68, "port")], 1, false, false, false) },
        new object[] { new Control("phase-no-moves", [new FleetInput("f0", "north", 49, 62, 0, 90, 100, false, -1, -1), new FleetInput("f1", "south", 48, 65, 25, 70, 100, false, -1, -1)], [], 0, true, false, false) },
        new object[] { new Control("phase-peace", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, -1, -1), new FleetInput("f1", "south", 48, 65, 25, 70, 100, false, -1, -1)], [new Expected("f0", 45, 68, "port")], 1, false, false, false) },
        new object[] { new Control("phase-same-owner", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, -1, -1), new FleetInput("f1", "north", 48, 65, 25, 70, 100, false, -1, -1)], [new Expected("f0", 45, 68, "port"), new Expected("f1", 45, 68, "port")], 2, true, false, false) },
        new object[] { new Control("phase-score100", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, -1, -1), new FleetInput("f1", "south", 41, 80, 25, 90, 100, false, -1, -1)], [new Expected("f0", 45, 68, "port")], 2, true, false, false) },
        new object[] { new Control("phase-score101", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, -1, -1), new FleetInput("f1", "south", 41, 80, 25, 89, 100, false, -1, -1)], [new Expected("f0", 41, 80, "hunt")], 2, true, false, false) },
        new object[] { new Control("phase-stored-a", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, 48, 66), new FleetInput("f1", "south", 48, 65, 25, 70, 100, false, -1, -1)], [new Expected("f0", 48, 66, "stored-route")], 0, true, false, false) },
        new object[] { new Control("phase-stored-b", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, 47, 67), new FleetInput("f1", "south", 48, 65, 25, 70, 100, false, -1, -1)], [new Expected("f0", 47, 67, "stored-route")], 0, true, false, false) },
        new object[] { new Control("phase-target-construction", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, -1, -1), new FleetInput("f1", "south", 48, 65, 25, 70, 135, true, -1, -1)], [new Expected("f0", 45, 68, "port")], 1, true, false, false) },
        new object[] { new Control("phase-trade", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, -1, -1), new FleetInput("f1", "south", 48, 65, 25, 70, 100, false, -1, -1)], [new Expected("f0", 45, 68, "port")], 1, false, false, false) },
        new object[] { new Control("phase-war", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, -1, -1), new FleetInput("f1", "south", 48, 65, 25, 70, 100, false, -1, -1)], [new Expected("f0", 48, 65, "hunt")], 2, true, false, false) },
        new object[] { new Control("phase-war-stronger", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, -1, -1), new FleetInput("f1", "south", 48, 65, 25, 100, 100, false, -1, -1)], [new Expected("f0", 45, 68, "port")], 2, true, false, false) },
        new object[] { new Control("phase-war-weaker", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, -1, -1), new FleetInput("f1", "south", 48, 65, 25, 10, 100, false, -1, -1)], [new Expected("f0", 48, 65, "hunt")], 2, true, false, false) },
        new object[] { new Control("port-protected", [new FleetInput("f0", "north", 186, 90, 25, 90, 100, false, -1, -1), new FleetInput("f1", "south", 189, 93, 25, 10, 100, false, -1, -1)], [new Expected("f0", 124, 105, "port")], 1, true, true, true) },
        new object[] { new Control("port-unprotected", [new FleetInput("f0", "north", 185, 89, 25, 90, 100, false, -1, -1), new FleetInput("f1", "south", 188, 92, 25, 10, 100, false, -1, -1)], [new Expected("f0", 188, 92, "hunt")], 2, true, false, true) },
        new object[] { new Control("rough-ai-one", [new FleetInput("f0", "north", 49, 62, 1, 90, 100, false, 49, 63), new FleetInput("f1", "south", 0, 0, 0, 70, 214, true, -1, -1)], [new Expected("f0", 49, 63, "stored-route")], 0, true, false, false) },
        new object[] { new Control("rough-ai-three", [new FleetInput("f0", "north", 49, 62, 3, 90, 100, false, 49, 63), new FleetInput("f1", "south", 0, 0, 0, 70, 214, true, -1, -1)], [new Expected("f0", 49, 63, "stored-route")], 0, true, false, false) },
        new object[] { new Control("stored-one", [new FleetInput("f0", "north", 49, 62, 1, 90, 100, false, 48, 66), new FleetInput("f1", "south", 0, 0, 0, 70, 214, true, -1, -1)], [new Expected("f0", 48, 66, "stored-route")], 0, true, false, false) },
        new object[] { new Control("stored-zero", [new FleetInput("f0", "north", 49, 62, 0, 90, 100, false, 48, 66), new FleetInput("f1", "south", 0, 0, 0, 70, 214, true, -1, -1)], [new Expected("f0", 48, 66, "stored-route")], 0, true, false, false) },
        new object[] { new Control("route-landing-cargo", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, 47, 64), new FleetInput("f1", "south", 0, 0, 0, 70, 214, true, -1, -1)], [new Expected("f0", 47, 64, "stored-route")], 0, true, false, false) },
        new object[] { new Control("route-landing-no-cargo", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, 47, 64), new FleetInput("f1", "south", 0, 0, 0, 70, 214, true, -1, -1)], [new Expected("f0", 47, 64, "stored-route")], 0, true, false, false) },
        new object[] { new Control("route-own-port", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, 47, 63), new FleetInput("f1", "south", 0, 0, 0, 70, 214, true, -1, -1)], [new Expected("f0", 47, 63, "stored-route")], 0, true, false, false) },
        new object[] { new Control("route-inland", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, 41, 57), new FleetInput("f1", "south", 0, 0, 0, 70, 214, true, -1, -1)], [new Expected("f0", 41, 57, "stored-route")], 0, true, false, false) },
        new object[] { new Control("route-x-minus2", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, -2, 66), new FleetInput("f1", "south", 0, 0, 0, 70, 214, true, -1, -1)], [new Expected("f0", 45, 68, "port")], 1, true, false, false) },
        new object[] { new Control("route-x-minus1-y-kept", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, -1, 66), new FleetInput("f1", "south", 0, 0, 0, 70, 214, true, -1, -1)], [new Expected("f0", 45, 68, "port")], 1, true, false, false) },
        new object[] { new Control("route-x-0-residue", [new FleetInput("f0", "north", 49, 62, 25, 90, 100, false, 0, 0), new FleetInput("f1", "south", 0, 0, 0, 70, 214, true, -1, -1)], [new Expected("f0", 0, 0, "stored-route")], 0, true, false, false) },
        new object[] { new Control("route-zero-moves", [new FleetInput("f0", "north", 49, 62, 0, 90, 100, false, 48, 66), new FleetInput("f1", "south", 0, 0, 0, 70, 214, true, -1, -1)], [new Expected("f0", 48, 66, "stored-route")], 0, true, false, false) },
        new object[] { new Control("route-reached", [new FleetInput("f0", "north", 48, 66, 25, 90, 100, false, 48, 66), new FleetInput("f1", "south", 0, 0, 0, 70, 214, true, -1, -1)], [new Expected("f0", 48, 66, "stored-route")], 0, true, false, false) },
        new object[] { new Control("adjacent-landing-cargo", [new FleetInput("f0", "north", 48, 64, 25, 90, 100, false, 47, 64), new FleetInput("f1", "south", 0, 0, 0, 70, 214, true, -1, -1)], [new Expected("f0", 47, 64, "stored-route")], 0, true, false, false) },
        new object[] { new Control("adjacent-landing-no-cargo", [new FleetInput("f0", "north", 48, 64, 25, 90, 100, false, 47, 64), new FleetInput("f1", "south", 0, 0, 0, 70, 214, true, -1, -1)], [new Expected("f0", 47, 64, "stored-route")], 0, true, false, false) },
        new object[] { new Control("adjacent-own-port", [new FleetInput("f0", "north", 48, 64, 25, 90, 100, false, 47, 63), new FleetInput("f1", "south", 0, 0, 0, 70, 214, true, -1, -1)], [new Expected("f0", 47, 63, "stored-route")], 0, true, false, false) },
        new object[] { new Control("no-route-zero-moves", [new FleetInput("f0", "north", 49, 62, 0, 90, 100, false, -1, 66), new FleetInput("f1", "south", 0, 0, 0, 70, 214, true, -1, -1)], [], 0, true, false, false) },
        new object[] { new Control("hunt-tie-a", [new FleetInput("f0", "north", 49, 62, 25, 1, 100, false, -1, 66), new FleetInput("f1", "south", 52, 62, 10, 1, 20, false, -1, -1), new FleetInput("f2", "south", 49, 65, 10, 1, 20, false, -1, -1)], [new Expected("f0", 52, 62, "hunt")], 3, true, false, false) },
        new object[] { new Control("hunt-tie-b", [new FleetInput("f0", "north", 49, 62, 25, 1, 100, false, -1, 66), new FleetInput("f1", "south", 49, 65, 10, 1, 20, false, -1, -1), new FleetInput("f2", "south", 52, 62, 10, 1, 20, false, -1, -1)], [new Expected("f0", 49, 65, "hunt")], 3, true, false, false) },
        new object[] { new Control("hunt-protected", [new FleetInput("f0", "north", 49, 62, 25, 1, 100, false, -1, 66), new FleetInput("f1", "south", 48, 64, 10, 1, 20, false, -1, -1)], [new Expected("f0", 45, 68, "port")], 1, true, true, false) },
        new object[] { new Control("hunt-unprotected-2", [new FleetInput("f0", "north", 49, 62, 25, 1, 100, false, -1, 66), new FleetInput("f1", "south", 49, 65, 10, 1, 20, false, -1, -1)], [new Expected("f0", 49, 65, "hunt")], 2, true, false, false) },
    };

    [Theory]
    [MemberData(nameof(Controls))]
    public void All_thirty_nine_decision_boundaries_keep_route_precedence_draws_and_table_order(Control c)
    {
        var game = AiTestbed.Classical;
        var initial = GameStateFactory.CreateInitial(game.World, game.Ruleset, game.Scenario);
        var world = game.World with { Terrain = new(TerrainEncoding.RunLength,
            ValueList.Of(new TerrainRun(0, 320 * 140))) };
        var port = AiReachableFallbackTests.City("port", "north",
            c.EasternPort ? 124 : 45, c.EasternPort ? 105 : 68, supply: 500);
        var cities = new List<CityState> { port };
        if (c.Protected)
        {
            var target = c.Fleets[1];
            cities.Add(AiReachableFallbackTests.City("protected", "south", target.X - 1, target.Y - 1));
        }
        var state = BattleCommandTestbed.StateWith([Fx.Nation("north"), Fx.Nation("south")], cities, [],
            c.Fleets.Select(f => new FleetState(f.Id, f.Owner, f.X, f.Y, f.Moves, f.Ships,
                f.Condition, 1000, 500, f.Building ? 5 : null, null, null, 0)));
        if (c.War) state = state with { Relations = state.Relations.WithRelation("north", "south",
            game.Ruleset.Diplomacy.StateCodes.War) };
        var routes = new OriginalFleetRoutes();
        foreach (var f in c.Fleets) routes.Set(f.Id, new(f.RouteX, f.RouteY));
        var events = new RecordingEventSink();
        var dispatch = new DispatchBoundary(game.Ruleset, world);
        var rng = new ZeroRng();
        var result = OriginalFleetPhase.Run(state, game.Ruleset, world, "north", routes, dispatch,
            routes.Capture(events), rng, new());
        Assert.Equal(0, result.Rejected);
        Assert.Equal(c.Draws, rng.Draws);
        Assert.Equal(c.Expected.Select(e => (e.Id, e.X, e.Y)),
            dispatch.Commands.Select(m => (m.FleetId, m.X, m.Y)));
        Assert.Equal(c.Expected.Select(e => e.Branch),
            events.Events.OfType<OriginalFleetDecision>().Where(d => d.X is not null).Select(d => d.Branch));
        Assert.Empty(events.Events.OfType<OriginalFleetRouteRequested>()); // F608 never writes one.
        foreach (var f in c.Fleets)
            Assert.Equal(f.RouteX >= 0 ? new GridPoint(f.RouteX, f.RouteY) : (GridPoint?)null, routes.For(f.Id));
    }

    private sealed class DispatchBoundary(Ruleset rules, World world) : ICommandDispatch
    {
        public List<MoveFleetCommand> Commands { get; } = new();
        public CommandResult Dispatch(GameState state, ICommand command) => Dispatch(state, command, NullEventSink.Instance);
        public CommandResult Dispatch(GameState state, ICommand command, IEventSink events)
        {
            Commands.Add(Assert.IsType<MoveFleetCommand>(command));
            // Stop before E1FC by dispatching its deterministic coincident no-op. The requested
            // command above is what this phase-boundary test checks; movement has its own real tests.
            var move = (MoveFleetCommand)command;
            var fleet = state.FleetById(move.FleetId)!;
            return new CommandDispatcher(SystemRegistry.FromEngineAssembly(), rules, world, events)
                .Dispatch(state, move with { X = fleet.X, Y = fleet.Y }, events);
        }
    }
    private sealed class ZeroRng : IRng
    {
        public int Draws { get; private set; }
        public ulong Seed => 1;
        public ulong State => (ulong)Draws;
        public int NextInt(int bound) { Assert.Equal(4, bound); Draws++; return 0; }
        public int NextInt(int low, int high) => low + NextInt(high - low);
        public ulong NextUInt64() => throw new InvalidOperationException();
        public bool NextChance(int numerator, int denominator) => throw new InvalidOperationException();
        public IRng ForStream(string name) => throw new InvalidOperationException();
    }
}
