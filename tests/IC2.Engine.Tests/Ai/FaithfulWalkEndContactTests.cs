using IC2.Engine.Ai;
using IC2.Engine.Battle;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Battle.Commands;
using IC2.Engine.Tests.Core;
using Xunit;
using F = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

public sealed class FaithfulWalkEndContactTests
{
    private static (AiView View, ArmyState Actor) Fixture(bool armyTarget = false, int x = 230, int moves = 8)
    {
        var rules = AiScriptedStates.Ruleset with { Flags = AiScriptedStates.Ruleset.Flags with { AiEconomy = AiEconomyModel.Original } };
        var world = AiScriptedStates.World with { Id = "classical-mediterranean", Width = 320, Height = 140,
            Terrain = new(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(2, 320 * 140))) };
        var actor = F.Army("actor", "north", x, 80, 60, F.Unit("light_infantry", 40000))
            with { Moves = moves, SupplyTons = 1000, Money = 500 };
        var enemy = F.Army("enemy", "south", 235, 80, 60, F.Unit("light_infantry", 1000)) with { Moves = 9 };
        var state = AiScriptedStates.WithActiveSeat(BattleCommandTestbed.StateWith(
            [F.Nation("north", treasury: 10000, capitalCityId: "home"), F.Nation("south", capitalCityId: "other")],
            // Keep the threat outside capital defence: these tests exercise F31C,
            // not the independently implemented E094 arrival contact.
            [F.City("home", "Home", 200, 50, "north", "north", 90, 0, 100, 100, 0) with { SupplyTons = 5000 },
             F.City("resupply", "Resupply", 235, 85, "north", "north", 90, 0, 100, 100, 0) with { SupplyTons = 5000 },
             F.City("target", "Target", 231, 80, "south", "south", 90, 0, 100, 100, 0),
             F.City("other", "Other", 300, 120, "south", "south", 90, 0, 100, 100, 0)], armyTarget ? [actor, enemy] : [actor]), "north");
        state = state with { Relations = state.Relations.WithRelation("north", "south", rules.Diplomacy.StateCodes.War),
            MercenaryPool = ValueList<MercenaryPoolSlot>.Empty };
        if (armyTarget) state = state with { Cities = ValueList.From(state.Cities.Where(c => c.Id != "target")) };
        return (new(state, rules, world, "north"), actor);
    }

    private sealed class Trace(ICommandDispatch inner, Func<GameState, ICommand, ICommand>? replace = null) : ICommandDispatch
    {
        public List<(ICommand Command, GameState Before, CommandResult Result)> Calls { get; } = [];
        public CommandResult Dispatch(GameState state, ICommand command) => Dispatch(state, command, NullEventSink.Instance);
        public CommandResult Dispatch(GameState state, ICommand command, IEventSink events)
        {
            var actual = replace?.Invoke(state, command) ?? command;
            var result = inner.Dispatch(state, actual, events);
            Calls.Add((actual, state, result)); return result;
        }
    }

    private static (AiTurnOutcome Outcome, Trace Trace, RecordingEventSink Sink) Run(AiView view,
        Func<GameState, ICommand, ICommand>? replace = null)
    {
        var sink = new RecordingEventSink();
        var trace = new Trace(new CommandDispatcher(SystemRegistry.FromEngineAssembly(), view.Ruleset, view.World, sink), replace);
        return (AiTurn.Run(view.State, view.Ruleset, view.World, trace, SplitMix64Rng.ForStream(1, "ai.turn"), sink), trace, sink);
    }

    [Fact]
    public void Adjacent_city_target_without_an_offer_besieges_before_the_no_progress_fallback()
    {
        var (view, actor) = Fixture();
        var decision = AiArmyTargetTree.Decide(view, actor, true);
        Assert.Equal(AiArmyTargetTree.Kind.MercenaryRun, decision.Selected);
        Assert.Equal(AiArmyTargetTree.Continuation.MarchAtCityTarget, decision.After);
        Assert.InRange(AiArmyTargetTree.ScoreCityTarget(view, actor).Score, 86, 100);
        var run = Run(view);
        var siege = Assert.Single(run.Trace.Calls, c => c.Command is BesiegeCityCommand);
        Assert.Equal((actor.X, actor.Y, actor.Moves), (siege.Before.ArmyById(actor.Id)!.X,
            siege.Before.ArmyById(actor.Id)!.Y, siege.Before.ArmyById(actor.Id)!.Moves));
        Assert.True(siege.Result.IsAccepted);
        Assert.Equal(0, siege.Result.State.ArmyById(actor.Id)!.Moves);
        Assert.True(siege.Result.State.ArmyById(actor.Id)!.TotalTroops < actor.TotalTroops);
        Assert.True(siege.Result.State.CityById("target")!.Loyalty < view.State.CityById("target")!.Loyalty);
        Assert.DoesNotContain(run.Outcome.Log, l => l.Contains("garrison fallback", StringComparison.Ordinal));
        Assert.Equal(0, run.Outcome.CommandsRejected); Assert.Equal(0, run.Outcome.ProjectionMismatches);
    }

    [Fact]
    public void Army_target_walk_attacks_from_its_arrival_tile_in_the_same_seat()
    {
        var (view, actor) = Fixture(armyTarget: true);
        Assert.Equal(AiArmyTargetTree.Kind.AttackArmy, AiArmyTargetTree.Decide(view, actor, true).Selected);
        var run = Run(view);
        var march = Assert.Single(run.Trace.Calls, c => c.Command is MoveArmyCommand);
        Assert.Contains(march.Result.Events, e => e is OriginalArmyWalkContactReached { EndCheck: OriginalWalkEndCheck.Army });
        var attack = Assert.Single(run.Trace.Calls, c => c.Command is AttackArmyCommand);
        Assert.Equal(run.Trace.Calls.IndexOf(march) + 1, run.Trace.Calls.IndexOf(attack));
        var at = attack.Before.ArmyById(actor.Id)!;
        var target = attack.Before.ArmyById("enemy")!;
        Assert.NotEqual((actor.X, actor.Y), (at.X, at.Y));
        Assert.Equal(1, AiView.Distance(at.X, at.Y, target.X, target.Y)); Assert.True(at.Moves > 0);
        Assert.True(attack.Result.IsAccepted);
        Assert.Single(run.Sink.Events.OfType<BattleResolved>());
        Assert.Equal(0, run.Outcome.CommandsRejected); Assert.Equal(0, run.Outcome.ProjectionMismatches);
    }

    [Fact]
    public void City_attack_walk_besieges_after_travel_in_the_same_dispatch_sequence()
    {
        var (view, actor) = Fixture();
        actor = actor with { Units = ValueList.Of(F.Unit("light_infantry", 60000)), Moves = 9 };
        var state = view.State with { Armies = ValueList.Of(actor), Cities = ValueList.From(view.State.Cities.Select(c =>
            c.Id == "target" ? c with { X = 236 } : c)) };
        // E41C support runs before the live tree and may change its score. Keep this fixture on the
        // AttackCity arm even after preparation; the default thresholds were actually taking F4C7.
        // One scored action prevents a later greedy siege from hiding a lost walk end check.
        var rules = view.Ruleset with { Ai = view.Ruleset.Ai with { CityScoreThreshold = 1, MaxActionsPerTurn = 1 } };
        view = new(state, rules, view.World, "north");
        Assert.Equal(AiArmyTargetTree.Kind.AttackCity, AiArmyTargetTree.Decide(view, actor, true).Selected);
        var run = Run(view);
        var march = Assert.Single(run.Trace.Calls, c => c.Command is MoveArmyCommand);
        var siege = Assert.Single(run.Trace.Calls, c => c.Command is BesiegeCityCommand);
        Assert.Contains(run.Outcome.Log, l => l.Contains("approach target city", StringComparison.Ordinal));
        Assert.Equal(AiArmyTargetTree.Kind.AttackCity, AiArmyTargetTree.Decide(
            new(march.Before, rules, view.World, "north"), march.Before.ArmyById(actor.Id)!, true).Selected);
        Assert.Equal(run.Trace.Calls.IndexOf(march) + 1, run.Trace.Calls.IndexOf(siege));
        var at = siege.Before.ArmyById(actor.Id)!;
        Assert.NotEqual((actor.X, actor.Y), (at.X, at.Y));
        Assert.Equal(1, AiView.Distance(at.X, at.Y, 236, 80)); Assert.True(at.Moves > 0);
        Assert.Equal(0, siege.Result.State.ArmyById(actor.Id)!.Moves);
        Assert.Equal(0, run.Outcome.CommandsRejected); Assert.Equal(0, run.Outcome.ProjectionMismatches);
    }

    [Fact]
    public void Actual_offer_run_then_mercenary_continuation_dispatches_a_siege_from_its_arrival_tile()
    {
        var (view, actor) = Fixture(moves: 9);
        // Declared branch fixture, as in OriginalMercenaryContinuationTests: keep F31C on its weak
        // target arm while leaving the independent F4C7 continuation threshold and real moves intact.
        var rules = view.Ruleset with { Ai = view.Ruleset.Ai with
            { CityScoreThreshold = 100000, ArmyScoreThreshold = 100000, MaxActionsPerTurn = 1 } };
        actor = actor with { Units = ValueList.Of(F.Unit("light_infantry", 50000)) };
        var state = view.State with { Armies = ValueList.Of(actor),
            Cities = ValueList.From(view.State.Cities.Select(c => c.Id == "target" ? c with
                { X = 237, PopulationThousands = 1, FortificationCode = 75 } : c)
                .Append(F.City("offer", "Offer", 236, 80, "north", "north", 80, 75, 10, 10, 0))),
            MercenaryPool = ValueList.Of(new MercenaryPoolSlot(0, 236, 80, 1, "light_infantry", 1000, 6)) };
        view = new(state, rules, view.World, "north");
        var decision = AiArmyTargetTree.Decide(view, actor, true);
        Assert.Equal(AiArmyTargetTree.Kind.MercenaryRun, decision.Selected);
        Assert.Equal(AiArmyTargetTree.Continuation.MarchAtCityTarget, decision.After);
        var run = Run(view);
        var walks = run.Trace.Calls.Where(c => c.Command is MoveArmyCommand).ToArray();
        Assert.Equal(2, walks.Length);
        var offer = Assert.IsType<MoveArmyCommand>(walks[0].Command);
        var continuation = Assert.IsType<MoveArmyCommand>(walks[1].Command);
        Assert.Equal((236, 80), (offer.TargetX, offer.TargetY));
        Assert.Equal((237, 80), (continuation.TargetX, continuation.TargetY));
        Assert.Empty(walks[0].Result.Events.OfType<OriginalArmyWalkContactReached>());
        Assert.Contains(run.Outcome.Log, l => l.Contains("original mercenary continuation actor", StringComparison.Ordinal));
        var siege = Assert.Single(run.Trace.Calls, c => c.Command is BesiegeCityCommand);
        Assert.Single(walks[1].Result.Events.OfType<OriginalArmyWalkContactReached>());
        Assert.Equal(run.Trace.Calls.IndexOf(walks[1]) + 1, run.Trace.Calls.IndexOf(siege));
        var at = siege.Before.ArmyById(actor.Id)!;
        Assert.Equal(1, AiView.Distance(at.X, at.Y, 237, 80)); Assert.True(at.Moves > 0);
        Assert.True(siege.Result.IsAccepted);
        Assert.Equal(0, siege.Result.State.ArmyById(actor.Id)!.Moves);
        Assert.True(siege.Result.State.ArmyById(actor.Id)!.TotalTroops < actor.TotalTroops);
        Assert.True(siege.Result.State.CityById("target")!.Loyalty < state.CityById("target")!.Loyalty);
        Assert.Equal(0, run.Outcome.CommandsRejected); Assert.Equal(0, run.Outcome.ProjectionMismatches);
    }

    [Fact]
    public void Repeated_supplied_seats_continue_the_siege_without_sideways_marches_or_duplicate_sieges()
    {
        var (view, actor) = Fixture();
        var state = view.State with { Armies = ValueList.Of(actor) };
        for (var seat = 0; seat < 3; seat++)
        {
            // Controlled seat boundary, not a claim about the unhooked mid-round refill.
            var before = state.ArmyById(actor.Id)! with { Moves = 8, SupplyTons = 1000 };
            state = state with { Armies = ValueList.Of(before) };
            var city = state.CityById("target")!;
            var current = new AiView(state, view.Ruleset, view.World, "north");
            if (seat == 0) Assert.Equal(AiArmyTargetTree.Kind.MercenaryRun, AiArmyTargetTree.Decide(current, before, true).Selected);
            var run = Run(current);
            Assert.Single(run.Trace.Calls, c => c.Command is BesiegeCityCommand);
            var after = run.Outcome.State.ArmyById(actor.Id)!;
            Assert.Equal((actor.X, actor.Y, 0), (after.X, after.Y, after.Moves));
            Assert.True(after.TotalTroops < before.TotalTroops);
            Assert.True(run.Outcome.State.CityById("target")!.Loyalty < city.Loyalty);
            Assert.Equal(0, run.Outcome.CommandsRejected); Assert.Equal(0, run.Outcome.ProjectionMismatches);
            state = run.Outcome.State;
        }
    }

    [Theory]
    [InlineData("peace")]
    [InlineData("own-city-hostile-allegiance")]
    [InlineData("zero")]
    [InlineData("far")]
    [InlineData("human")]
    [InlineData("improved")]
    [InlineData("custom")]
    [InlineData("embarked")]
    [InlineData("wrong-target")]
    [InlineData("unmeasured-caller")]
    public void Contact_guards_do_not_invent_a_battle(string mode)
    {
        var (view, actor) = Fixture(); var state = view.State; var rules = view.Ruleset; var world = view.World;
        var target = new GridPoint(231, 80); var kind = OriginalWalkEndCheck.City;
        switch (mode)
        {
            case "peace": state = state with { Relations = state.Relations.WithRelation("north", "south", rules.Diplomacy.StateCodes.Peace) }; break;
            case "own-city-hostile-allegiance": state = state with { Cities = ValueList.From(state.Cities.Select(c => c.Id == "target" ? c with { Owner = "north" } : c)) }; break;
            case "zero": actor = actor with { Moves = 0 }; break;
            case "far": actor = actor with { X = 220 }; break;
            case "human": state = state with { Nations = ValueList.From(state.Nations.Select(n => n.Id == "north" ? n with { Control = SeatControl.Human } : n)) }; break;
            case "improved": rules = rules with { Flags = rules.Flags with { AiEconomy = AiEconomyModel.Designed } }; break;
            case "custom": world = world with { Id = "custom-diagnostic" }; break;
            case "embarked": actor = actor with { AboardFleetId = "carrier", CoveredTileCode = null }; break;
            case "wrong-target": target = new(230, 79); break;
            case "unmeasured-caller": kind = OriginalWalkEndCheck.None; break;
        }
        state = state with { Armies = ValueList.Of(actor) };
        var result = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), rules, world, NullEventSink.Instance)
            .Dispatch(state, new MoveArmyCommand("north", actor.Id, target.X, target.Y, MoveWalkPolicy.AiContinuation,
                target.X, target.Y, kind));
        Assert.Empty(result.Events.OfType<OriginalArmyWalkContactReached>());
        Assert.Empty(result.Events.OfType<BattleResolved>());
    }

    [Fact]
    public void City_contact_checks_owner_not_allegiance_and_movement_itself_does_not_resolve_the_siege()
    {
        var (view, actor) = Fixture();
        var state = view.State with { Cities = ValueList.From(view.State.Cities.Select(c => c.Id == "target" ? c with { Allegiance = "north" } : c)) };
        var dispatch = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), view.Ruleset, view.World, NullEventSink.Instance);
        var result = dispatch.Dispatch(state, new MoveArmyCommand("north", actor.Id, 231, 80, MoveWalkPolicy.AiContinuation,
            231, 80, OriginalWalkEndCheck.City));
        Assert.True(result.IsAccepted); Assert.Single(result.Events.OfType<OriginalArmyWalkContactReached>());
        Assert.Equal(state.CityById("target"), result.State.CityById("target"));
        Assert.Equal(actor, result.State.ArmyById(actor.Id));
        Assert.Empty(result.Events.OfType<BattleResolved>());
    }

    [Fact]
    public void Exhausting_the_last_move_at_adjacency_does_not_request_contact()
    {
        var (view, actor) = Fixture(x: 229, moves: 1);
        var result = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), view.Ruleset, view.World, NullEventSink.Instance)
            .Dispatch(view.State, new MoveArmyCommand("north", actor.Id, 231, 80, MoveWalkPolicy.AiContinuation, 231, 80, OriginalWalkEndCheck.City));
        Assert.True(result.IsAccepted);
        var end = result.State.ArmyById(actor.Id)!;
        Assert.Equal(1, AiView.Distance(end.X, end.Y, 231, 80)); Assert.Equal(0, end.Moves);
        Assert.Empty(result.Events.OfType<OriginalArmyWalkContactReached>());
        Assert.Equal(view.State.CityById("target"), result.State.CityById("target"));
    }

    [Theory]
    [InlineData("unmeasured")]
    [InlineData("custom")]
    [InlineData("improved")]
    [InlineData("human")]
    [InlineData("peace")]
    public void Non_opted_in_orders_keep_the_existing_state_and_random_stream(string mode)
    {
        var (view, actor) = Fixture(); var state = view.State; var rules = view.Ruleset; var world = view.World;
        if (mode == "custom") world = world with { Id = "custom-diagnostic" };
        if (mode == "improved") rules = rules with { Flags = rules.Flags with { AiEconomy = AiEconomyModel.Designed } };
        if (mode == "human") state = state with { Nations = ValueList.From(state.Nations.Select(n => n.Id == "north" ? n with { Control = SeatControl.Human } : n)) };
        if (mode == "peace") state = state with { Relations = state.Relations.WithRelation("north", "south", rules.Diplomacy.StateCodes.Peace) };
        var dispatch = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), rules, world, NullEventSink.Instance);
        var plain = new MoveArmyCommand("north", actor.Id, 231, 80, MoveWalkPolicy.AiContinuation, 231, 80);
        var baseline = dispatch.Dispatch(state, plain);
        var unchanged = dispatch.Dispatch(state, plain with { EndCheck = mode == "unmeasured" ? OriginalWalkEndCheck.None : OriginalWalkEndCheck.City });
        Assert.Equal(baseline.State, unchanged.State); Assert.Equal(baseline.Events, unchanged.Events);
        Assert.Empty(unchanged.Events.OfType<OriginalArmyWalkContactReached>());
    }

    [Fact]
    public void Walk_contact_opens_the_human_battle_and_resumes_without_replaying_the_walk()
    {
        var (view, _) = Fixture(armyTarget: true);
        var scenario = CoreTestbed.Toy.Scenario with { WorldId = view.World.Id, RulesetId = view.Ruleset.Id,
            Victory = new(VictoryConditionType.TotalConquest), TurnLimit = null,
            Seats = ValueList.Of(new Seat("north", SeatControl.Ai), new Seat("south", SeatControl.Human)) };
        var state = view.State with { WorldId = view.World.Id, RulesetId = view.Ruleset.Id, ScenarioId = scenario.Id,
            Nations = ValueList.From(view.State.Nations.Select(n => n.Id == "south" ? n with { Control = SeatControl.Human } : n)) };
        state = AiScriptedStates.WithActiveSeat(state, "south");
        var save = new SaveGame(state.SchemaVersion, "walk-contact", "walk-contact", scenario.Id, view.World.Id, view.Ruleset.Id, state);
        var session = new GameSession(view.World, view.Ruleset, scenario, save) { HumanBattles = HumanBattleMode.Tactical };
        var events = new List<DomainEvent>();
        session.EventsPublished += batch => events.AddRange(batch);
        var opened = session.Submit("end");
        Assert.NotNull(opened.OpenBattleView);
        Assert.Equal(("actor", "enemy"), (opened.OpenBattleView!.AttackerArmyId, opened.OpenBattleView.DefenderArmyId));
        Assert.Equal("north", session.State.ActiveNationId);
        var finished = session.Submit("battle general on");
        Assert.Null(finished.OpenBattleView);
        Assert.NotNull(finished.EndedBattleReport);
        Assert.Equal("south", session.State.ActiveNationId);
        Assert.Single(events.OfType<OriginalArmyWalkContactReached>());
        Assert.Single(events.OfType<OriginalArmyMarchDispatched>(), e => e.ArmyId == "actor");
        Assert.Single(finished.TacticalTimeline, frame => frame.Kind == TacticalFrameKind.Terminal);
    }

    private static AiView GarrisonFixture(string mode = "faithful", bool originalTreeCommand = false)
    {
        var (view, actor) = Fixture();
        actor = actor with { Units = ValueList.Of(F.Unit("light_infantry", originalTreeCommand ? 36000 : 34400)), Moves = 9 };
        var guard = actor with { Id = "capital-guard", X = 201, Y = 50, Moves = 0 };
        var state = view.State with { Armies = ValueList.Of(actor, guard),
            Cities = ValueList.From(view.State.Cities.Select(c => c.Id == "target" ? c with
            {
                PopulationThousands = originalTreeCommand ? 90 : c.PopulationThousands,
                Owner = mode == "friendly" ? "north" : c.Owner,
            } : c)) };
        var rules = mode == "improved" ? view.Ruleset with
            { Flags = view.Ruleset.Flags with { AiEconomy = AiEconomyModel.Designed } } : view.Ruleset;
        var world = mode == "custom" ? view.World with { Id = "custom-diagnostic" } : view.World;
        return new(state, rules, world, "north");
    }

    [Fact]
    public void Garrison_with_no_tree_command_besieges_the_adjacent_hostile_city()
    {
        var view = GarrisonFixture(); var actor = view.State.ArmyById("actor")!;
        var decision = AiArmyTargetTree.Decide(view, actor, true);
        Assert.InRange(AiArmyTargetTree.ScoreCityTarget(view, actor).Score, 1, 85);
        Assert.Equal(AiArmyTargetTree.Kind.MercenaryRun, decision.Selected);
        Assert.Equal(AiArmyTargetTree.Continuation.None, decision.After);
        Assert.Equal((231, 80), AiArmyTargetTree.GarrisonFallback(view, actor, view.State));
        var run = Run(view);
        var move = Assert.Single(run.Trace.Calls, c => c.Command is MoveArmyCommand m && m.ArmyId == actor.Id);
        var siege = Assert.Single(run.Trace.Calls, c => c.Command is BesiegeCityCommand);
        Assert.Contains(run.Outcome.Log, l => l.Contains("garrison fallback", StringComparison.Ordinal));
        Assert.Contains(move.Result.Events, e => e is OriginalArmyWalkContactReached { EndCheck: OriginalWalkEndCheck.City });
        Assert.Equal((actor.X, actor.Y, actor.Moves), (siege.Before.ArmyById(actor.Id)!.X,
            siege.Before.ArmyById(actor.Id)!.Y, siege.Before.ArmyById(actor.Id)!.Moves));
        Assert.Equal(0, siege.Result.State.ArmyById(actor.Id)!.Moves);
        Assert.True(siege.Result.State.CityById("target")!.Loyalty < view.State.CityById("target")!.Loyalty);
        Assert.True(siege.Result.State.ArmyById(actor.Id)!.TotalTroops < actor.TotalTroops);
        Assert.Equal(0, run.Outcome.CommandsRejected); Assert.Equal(0, run.Outcome.ProjectionMismatches);
    }

    [Fact]
    public void Post_execution_garrison_uses_the_same_hostile_city_end_check()
    {
        var view = GarrisonFixture(originalTreeCommand: true); var actor = view.State.ArmyById("actor")!;
        Assert.Equal(AiArmyTargetTree.Continuation.MarchAtCityTarget, AiArmyTargetTree.Decide(view, actor, true).After);
        var replaced = false;
        var run = Run(view, (state, command) =>
        {
            if (replaced || command is not MoveArmyCommand { ArmyId: "actor" }) return command;
            replaced = true;
            // A real accepted no-op leaves the full allowance: force the second
            // production garrison path without fabricating state or contact events.
            return new MoveArmyCommand("north", "actor", actor.X, actor.Y);
        });
        Assert.True(replaced);
        var moves = run.Trace.Calls.Where(c => c.Command is MoveArmyCommand m && m.ArmyId == actor.Id).ToArray();
        Assert.Equal(2, moves.Length);
        Assert.Empty(moves[0].Result.Events.OfType<OriginalArmyWalkContactReached>());
        Assert.Single(moves[1].Result.Events.OfType<OriginalArmyWalkContactReached>());
        Assert.Single(run.Trace.Calls, c => c.Command is BesiegeCityCommand);
        var end = run.Outcome.State.ArmyById(actor.Id)!;
        Assert.Equal((actor.X, actor.Y, 0), (end.X, end.Y, end.Moves));
        Assert.Equal(0, run.Outcome.CommandsRejected); Assert.Equal(0, run.Outcome.ProjectionMismatches);
    }

    [Theory]
    [InlineData("improved")]
    [InlineData("custom")]
    [InlineData("friendly")]
    public void Garrison_scope_does_not_add_contacts_for_other_modes_or_friendly_cities(string mode)
    {
        var view = GarrisonFixture(mode);
        var run = Run(view);
        Assert.Empty(run.Sink.Events.OfType<OriginalArmyWalkContactReached>());
        Assert.DoesNotContain(run.Trace.Calls, c => c.Command is BesiegeCityCommand);
        Assert.Equal(view.State.CityById("target"), run.Outcome.State.CityById("target"));
        Assert.Equal(0, run.Outcome.CommandsRejected); Assert.Equal(0, run.Outcome.ProjectionMismatches);
    }
}
