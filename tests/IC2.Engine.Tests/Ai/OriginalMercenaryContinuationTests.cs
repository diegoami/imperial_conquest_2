using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using F = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

public sealed class OriginalMercenaryContinuationTests
{
    private static (GameState State, Ruleset Rules, World World) Fixture(bool faithful, bool chase, int moves = 8,
        bool noContinuation = false)
    {
        var toy = AiScriptedStates.Ruleset;
        // Declared branch fixture: force the weak-target arm of F31C without altering its independent
        // continuation thresholds (>70 / >85). Targets, movement and commands are real engine paths.
        var rules = toy with { Ai = toy.Ai with { CityScoreThreshold = 100000, ArmyScoreThreshold = 100000 },
            Flags = toy.Flags with { AiEconomy = faithful ? AiEconomyModel.Original : AiEconomyModel.Designed } };
        var world = AiScriptedStates.World with { Id = "classical-mediterranean", Width = 320, Height = 140,
            Terrain = new(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(2, 320 * 140))) };
        var actor = F.Army("actor", "north", 230, 80, 80, F.Unit("light_infantry", noContinuation ? 5000 : 50000))
            with { Moves = moves, SupplyTons = 1000, Money = 500 };
        var enemy = F.Army("enemy", "south", 243, 85, 80, F.Unit("light_infantry", 1000)) with { Moves = 8 };
        var state = BattleCommandTestbed.StateWith(
            [F.Nation("north", capitalCityId: "home") with { MobilizedPercent = rules.Recruitment.MobilizationCapPercent },
             F.Nation("south", capitalCityId: "goal")],
            [F.City("home", "Home", 200, 80, "north", "north", 80, 100, 100, 100, 1),
             F.City("offer", "Offer", 236, 80, "north", "north", 80, 75, 10, 10, 1),
             F.City("goal", "Goal", 240, 85, "south", "south", 80, 75, 1, 1, 1)],
            chase ? [actor, enemy] : [actor]);
        state = AiScriptedStates.WithActiveSeat(state, "north");
        state = state with { Relations = state.Relations.WithRelation("north", "south", rules.Diplomacy.StateCodes.War),
            MercenaryPool = ValueList.Of(new MercenaryPoolSlot(0, 236, 80, 1, "light_infantry", 1000, 6)) };
        var decision = AiArmyTargetTree.DecideUnlogged(new(state, rules, world, "north"), actor, true, out _);
        Assert.Equal(AiArmyTargetTree.Kind.MercenaryRun, decision.Selected);
        var expected = noContinuation ? AiArmyTargetTree.Continuation.None : chase ? AiArmyTargetTree.Continuation.ChaseArmy : faithful
            ? AiArmyTargetTree.Continuation.MarchAtCityTarget : AiArmyTargetTree.Continuation.DefendResupplyCity;
        Assert.True(expected == decision.After, $"{decision}; city={AiArmyTargetTree.ScoreCityTarget(new(state, rules, world, "north"), actor)}");
        return (state, rules, world);
    }

    private sealed class Trace(ICommandDispatch inner, bool refuse = false, int refuseAt = 1) : ICommandDispatch
    {
        public List<MoveArmyCommand> Moves { get; } = [];
        public List<GameState> Before { get; } = [];
        public CommandResult Dispatch(GameState state, ICommand command) => Dispatch(state, command, NullEventSink.Instance);
        public CommandResult Dispatch(GameState state, ICommand command, IEventSink events)
        {
            if (command is MoveArmyCommand move)
            {
                Moves.Add(move); Before.Add(state);
                if (refuse && Moves.Count == refuseAt) return UnavailableCommandDispatch.Instance.Dispatch(state, command, events);
            }
            return inner.Dispatch(state, command, events);
        }
    }

    private static (AiTurnOutcome Outcome, Trace Trace, RecordingEventSink Sink) Run(
        GameState state, Ruleset rules, World world, bool refuse = false, int refuseAt = 1, SystemRegistry? registry = null)
    {
        var sink = new RecordingEventSink();
        var trace = new Trace(new CommandDispatcher(registry ?? SystemRegistry.FromEngineAssembly(), rules, world, sink), refuse, refuseAt);
        return (AiTurn.Run(state, rules, world, trace, SplitMix64Rng.ForStream(1, "ai.turn"), sink), trace, sink);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public void An_offer_walk_is_followed_by_the_selected_continuation_only_in_Faithful(bool faithful, bool chase)
    {
        var (state, rules, world) = Fixture(faithful, chase);
        var result = Run(state, rules, world);
        Assert.Equal(faithful ? 2 : 1, result.Trace.Moves.Count);
        Assert.Equal((236, 80), (result.Trace.Moves[0].TargetX ?? result.Trace.Moves[0].X, result.Trace.Moves[0].TargetY ?? result.Trace.Moves[0].Y));
        if (faithful)
        {
            var second = result.Trace.Moves[1];
            Assert.Equal((chase ? 243 : 240, 85), (second.TargetX, second.TargetY));
            var afterRun = result.Trace.Before[1].ArmyById("actor")!;
            Assert.Equal((235, 80, 3), (afterRun.X, afterRun.Y, afterRun.Moves));
            Assert.Equal((238, chase ? 82 : 83, 0), (result.Outcome.State.ArmyById("actor")!.X,
                result.Outcome.State.ArmyById("actor")!.Y, result.Outcome.State.ArmyById("actor")!.Moves));
        }
        Assert.Equal(0, result.Outcome.CommandsRejected);
        var replay = Run(state, rules, world);
        Assert.Equal(result.Trace.Moves, replay.Trace.Moves);
        Assert.Equal(result.Outcome.State, replay.Outcome.State);
    }

    [Fact]
    public void Both_walks_finish_before_the_next_army_receives_a_command()
    {
        var (state, rules, world) = Fixture(true, false);
        state = state with { Armies = ValueList.From(state.Armies.Append(state.Armies[0] with { Id = "later", X = 250 })) };
        var result = Run(state, rules, world);
        Assert.True(result.Trace.Moves.Count >= 3);
        Assert.Equal(new[] { "actor", "actor", "later" }, result.Trace.Moves.Take(3).Select(c => c.ArmyId));
        Assert.Equal(0, result.Outcome.CommandsRejected);
    }

    [Fact]
    public void The_bounded_run_and_continuation_share_one_scored_action()
    {
        var (state, rules, world) = Fixture(true, false);
        rules = rules with { Ai = rules.Ai with { MaxActionsPerTurn = 1 } };
        var result = Run(state, rules, world);
        Assert.Equal(2, result.Trace.Moves.Count);
        Assert.Equal(2, result.Outcome.CommandsIssued);
        Assert.True(result.Outcome.HitActionCap);
        Assert.Equal(0, result.Outcome.CommandsRejected);
    }

    [Fact]
    public void A_run_without_either_continuation_condition_remains_one_walk()
    {
        var (state, rules, world) = Fixture(true, false, noContinuation: true);
        var result = Run(state, rules, world);
        Assert.Single(result.Trace.Moves);
        Assert.Equal(0, result.Outcome.CommandsRejected);
    }

    private static (GameState State, Ruleset Rules, World World) FerryFixture(bool chase = false, int moves = 8)
    {
        var (state, rules, world) = Fixture(true, chase, moves);
        var cells = new int[320 * 140];
        for (var y = 0; y < 140; y++) for (var x = 0; x <= 100; x++) cells[y * 320 + x] = 2;
        cells[90 * 320 + 120] = 2;
        cells[91 * 320 + 120] = 2;
        world = world with { Terrain = new(TerrainEncoding.RunLength, ValueList.From(cells.Select(c => new TerrainRun(c, 1)))) };
        var game = AiTestbed.Classical;
        var fleet = GameStateFactory.CreateInitial(game.World, game.Ruleset, game.Scenario).Fleets[0] with {
            Id = "ferry", Nation = "north", X = 105, Y = 40, Moves = 26, Ships = 90,
            ConstructionTicksRemaining = null, BuildCityId = null, CarriedArmyId = null, CoveredTileCode = 0
        };
        state = state with { Fleets = ValueList.Of(fleet),
            Armies = ValueList.From(state.Armies.Select(a => a.Id == "actor"
                ? a with { X = 98, Y = 40, Money = 0 } : a with { X = 120, Y = 91 })),
            Cities = ValueList.From(state.Cities.Select(c => c.Id switch {
                "home" => c with { X = 90, Y = 30 },
                "offer" => c with { X = 100, Y = 40, SupplyTons = 50000 },
                _ => c with { X = 120, Y = 90, FortificationCode = 0 }
            })),
            MercenaryPool = ValueList.Of(state.MercenaryPool[0] with { X = 100, Y = 40 })
        };
        var decision = AiArmyTargetTree.DecideUnlogged(new(state, rules, world, "north"), state.Armies[0], true, out _);
        Assert.Equal(chase ? AiArmyTargetTree.Continuation.ChaseArmy : AiArmyTargetTree.Continuation.MarchAtCityTarget, decision.After);
        return (state, rules, world);
    }

    [Fact]
    public void A_positive_budget_continuation_writes_the_same_seat_ferry_route()
    {
        var (state, rules, world) = FerryFixture();
        var result = Run(state, rules, world);
        Assert.Equal(2, result.Trace.Moves.Count);
        Assert.Equal(7, result.Trace.Before[1].ArmyById("actor")!.Moves);
        Assert.Equal(new OriginalFleetRouteRequested("ferry", 101, 40),
            Assert.Single(result.Sink.Events.OfType<OriginalFleetRouteRequested>()));
        Assert.Contains(result.Sink.Events.OfType<OriginalFleetDecision>(), e => e.FleetId == "ferry" && e.Branch == "stored-route");
        Assert.Equal(0, result.Outcome.CommandsRejected);
    }

    [Fact]
    public void A_refused_offer_walk_does_not_execute_its_continuation()
    {
        var (state, rules, world) = Fixture(true, false);
        var result = Run(state, rules, world, refuse: true);
        Assert.Single(result.Trace.Moves);
        Assert.Equal(1, result.Outcome.CommandsRejected);
        Assert.Equal(state.Armies, result.Outcome.State.Armies);
    }

    [Fact]
    public void A_refused_continuation_keeps_the_accepted_offer_walk_without_repeating_it()
    {
        var (state, rules, world) = Fixture(true, false);
        var result = Run(state, rules, world, refuse: true, refuseAt: 2);
        Assert.Equal(2, result.Trace.Moves.Count);
        var army = result.Outcome.State.ArmyById("actor")!;
        Assert.Equal((235, 80, 3), (army.X, army.Y, army.Moves));
        Assert.Equal(1, result.Outcome.CommandsIssued);
        Assert.Equal(1, result.Outcome.CommandsRejected);
    }

    private static SystemRegistry MoveRegistry(Type handler)
    {
        var engine = typeof(SystemRegistry).Assembly;
        return SystemRegistry.FromAssemblies([engine, handler.Assembly],
            type => type == handler || (type.Assembly == engine && type != typeof(MoveArmyCommandHandler)));
    }

    [CommandHandler]
    public sealed class RemoveAfterMove : ICommandHandler<MoveArmyCommand>
    {
        public CommandOutcome Handle(MoveArmyCommand command, CommandContext context)
        {
            var result = new MoveArmyCommandHandler().Handle(command, context);
            return result.AcceptedState is { } state ? CommandOutcome.Accept(state with {
                Armies = ValueList.From(state.Armies.Where(a => a.Id != command.ArmyId))
            }) : result;
        }
    }

    [CommandHandler]
    public sealed class NoOpOfferMove : ICommandHandler<MoveArmyCommand>
    {
        public CommandOutcome Handle(MoveArmyCommand command, CommandContext context) =>
            command.TargetX == 236 && command.TargetY == 80 ? CommandOutcome.Accept(context.State)
                : new MoveArmyCommandHandler().Handle(command, context);
    }

    [Fact]
    public void A_missing_post_run_actor_is_not_sent_a_stale_continuation()
    {
        var (state, rules, world) = Fixture(true, false);
        var result = Run(state, rules, world, registry: MoveRegistry(typeof(RemoveAfterMove)));
        Assert.Single(result.Trace.Moves);
        Assert.Null(result.Outcome.State.ArmyById("actor"));
        Assert.Equal(0, result.Outcome.CommandsRejected);
    }

    [Fact]
    public void An_accepted_no_op_offer_is_not_counted_when_only_the_continuation_changes_state()
    {
        var (state, rules, world) = Fixture(true, false);
        var result = Run(state, rules, world, registry: MoveRegistry(typeof(NoOpOfferMove)));
        Assert.Equal(2, result.Trace.Moves.Count);
        Assert.Equal((240, 85), (result.Trace.Moves[1].TargetX, result.Trace.Moves[1].TargetY));
        Assert.Equal(1, result.Outcome.CommandsIssued);
        Assert.Equal(0, result.Outcome.CommandsRejected);
        Assert.NotEqual((230, 80), (result.Outcome.State.ArmyById("actor")!.X, result.Outcome.State.ArmyById("actor")!.Y));
    }

    [Fact]
    public void A_zero_move_city_continuation_requests_a_ferry_without_moving_the_army()
        => CheckZeroRoute(chase: false, fleetMoves: 26, fleetX: 105, expectRoute: true);

    [Fact]
    public void A_zero_move_army_continuation_requests_a_ferry_without_moving_the_army()
        => CheckZeroRoute(chase: true, fleetMoves: 26, fleetX: 105, expectRoute: true);

    [Fact]
    public void A_zero_move_rendezvous_already_at_the_fleet_stays_a_stored_route()
        => CheckZeroRoute(chase: false, fleetMoves: 26, fleetX: 101, expectRoute: true);

    [Fact]
    public void Zero_fleet_moves_prevent_the_zero_army_move_rendezvous()
        => CheckZeroRoute(chase: false, fleetMoves: 0, fleetX: 105, expectRoute: false);

    private static void CheckZeroRoute(bool chase, int fleetMoves, int fleetX, bool expectRoute)
    {
        var (state, rules, world) = FerryFixture(chase, moves: 1);
        state = state with { Fleets = ValueList.Of(state.Fleets[0] with { X = fleetX, Moves = fleetMoves }) };
        var result = Run(state, rules, world);
        Assert.Single(result.Trace.Moves); // The budget was spent by the offer walk; no second army order.
        var army = result.Outcome.State.ArmyById("actor")!;
        Assert.Equal((99, 40, 0), (army.X, army.Y, army.Moves));
        Assert.Null(army.AboardFleetId);
        var requests = result.Sink.Events.OfType<OriginalFleetRouteRequested>().ToArray();
        if (expectRoute)
        {
            Assert.Equal(new OriginalFleetRouteRequested("ferry", 101, 40), Assert.Single(requests));
            Assert.Contains(result.Sink.Events.OfType<OriginalFleetDecision>(), e => e.FleetId == "ferry" && e.Branch == "stored-route");
            Assert.Equal((101, 40), (result.Outcome.State.FleetById("ferry")!.X, result.Outcome.State.FleetById("ferry")!.Y));
        }
        else Assert.Empty(requests);
        Assert.Equal(0, result.Outcome.CommandsRejected);
        var replay = Run(state, rules, world);
        Assert.Equal(result.Outcome.State, replay.Outcome.State);
        Assert.Equal(requests, replay.Sink.Events.OfType<OriginalFleetRouteRequested>());
    }

    [Theory]
    [InlineData(SeatControl.Ai)]
    [InlineData(SeatControl.Human)]
    public void Public_zero_move_orders_still_refuse_without_a_route_side_effect(SeatControl control)
    {
        var (state, rules, world) = FerryFixture();
        state = state with { Armies = ValueList.From(state.Armies.Select(a => a with { Moves = 0 })),
            Nations = ValueList.From(state.Nations.Select(n => n.Id == "north" ? n with { Control = control } : n)) };
        var sink = new RecordingEventSink();
        var dispatch = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), rules, world, sink);
        var result = dispatch.Dispatch(state, new MoveArmyCommand("north", "actor", 120, 90,
            IC2.Engine.Movement.MoveWalkPolicy.AiContinuation, 120, 90));
        Assert.Equal(MoveArmyRejections.NoMovesLeft, result.Code);
        Assert.Same(state, result.State);
        Assert.Empty(sink.Events.OfType<OriginalFleetRouteRequested>());
    }

    [Fact]
    public void The_unmeasured_adjacent_zero_move_embark_keeps_its_existing_guard()
    {
        var (state, rules, world) = FerryFixture(moves: 1);
        var cells = world.Terrain.Decode(world.Width, world.Height).ToArray();
        cells[41 * 320 + 100] = 0;
        world = world with { Terrain = new(TerrainEncoding.RunLength, ValueList.From(cells.Select(c => new TerrainRun(c, 1)))) };
        state = state with { Fleets = ValueList.Of(state.Fleets[0] with { X = 100, Y = 41 }) };
        var result = Run(state, rules, world);
        Assert.Empty(result.Sink.Events.OfType<OriginalFleetRouteRequested>());
        Assert.Null(result.Outcome.State.ArmyById("actor")!.AboardFleetId);
        Assert.Equal((99, 40, 0), (result.Outcome.State.ArmyById("actor")!.X,
            result.Outcome.State.ArmyById("actor")!.Y, result.Outcome.State.ArmyById("actor")!.Moves));
        Assert.Equal(0, result.Outcome.CommandsRejected);
    }

    [CommandHandler]
    public sealed class NegativeAfterMove : ICommandHandler<MoveArmyCommand>
    {
        public CommandOutcome Handle(MoveArmyCommand command, CommandContext context)
        {
            var outcome = new MoveArmyCommandHandler().Handle(command, context);
            return outcome.AcceptedState is { } state ? CommandOutcome.Accept(state with {
                Armies = ValueList.From(state.Armies.Select(a => a.Id == command.ArmyId ? a with { Moves = -1 } : a))
            }) : outcome;
        }
    }

    [Fact]
    public void A_negative_post_run_budget_does_not_gain_the_zero_move_route_path()
    {
        var (state, rules, world) = FerryFixture(moves: 1);
        var result = Run(state, rules, world, registry: MoveRegistry(typeof(NegativeAfterMove)));
        Assert.Empty(result.Sink.Events.OfType<OriginalFleetRouteRequested>());
        Assert.Single(result.Trace.Moves);
        Assert.Equal(-1, result.Outcome.State.ArmyById("actor")!.Moves);
        Assert.Equal(0, result.Outcome.CommandsRejected);
    }

    [Fact]
    public void Improved_does_not_use_the_original_zero_move_rendezvous_planner()
    {
        var (state, rules, world) = FerryFixture(moves: 1);
        rules = rules with { Flags = rules.Flags with { AiEconomy = AiEconomyModel.Designed } };
        var result = Run(state, rules, world);
        Assert.Empty(result.Sink.Events.OfType<OriginalFleetRouteRequested>());
        Assert.Equal(0, result.Outcome.CommandsRejected);
    }

    [Fact]
    public void Zero_move_ferry_ties_keep_the_first_eligible_fleet_record()
    {
        var (state, rules, world) = FerryFixture(moves: 1);
        state = state with { Fleets = ValueList.Of(state.Fleets[0] with { Id = "z-first" },
            state.Fleets[0] with { Id = "a-second", X = 105, Y = 41 }) };
        var result = Run(state, rules, world);
        Assert.Equal("z-first", Assert.Single(result.Sink.Events.OfType<OriginalFleetRouteRequested>()).FleetId);
        Assert.Equal(0, result.Outcome.CommandsRejected);
    }

    [Fact]
    public void A_zero_move_land_continuation_does_not_walk()
    {
        var (state, rules, world) = Fixture(true, false, moves: 5);
        var result = Run(state, rules, world);
        Assert.Single(result.Trace.Moves);
        Assert.Equal(0, result.Outcome.State.ArmyById("actor")!.Moves);
        Assert.Equal(0, result.Outcome.CommandsRejected);
    }
}
