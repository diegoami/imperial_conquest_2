using System.Reflection;
using IC2.Engine.Ai;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Tests.Ai;
using IC2.Engine.Tests.Battle.Commands;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;
using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Movement;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

public sealed class StrategicTravelTimelineTests
{
    [Fact]
    public void Batches_are_immutable_ordered_and_read_only_invalid_empty_quit_do_not_replay()
    {
        var session = StrategicTravelObservationTests.Session();
        var first = session.Submit("move a 2 0");
        var step = Assert.Single(first.StrategicTimeline.Steps, s => s.Routes.Count > 0);
        var frozen = GameJson.Serialize(first.StrategicTimeline);
        Assert.Equal(0, step.MarkersBefore.Single(m => m.EntityId == "a").Position.X);
        Assert.Equal(2, step.MarkersAfter.Single(m => m.EntityId == "a").Position.X);
        var second = session.Submit("move a 4 0");
        Assert.True(second.StrategicTimeline.BatchId > first.StrategicTimeline.BatchId);
        Assert.True(second.StrategicTimeline.Steps[0].StepId > step.StepId);
        Assert.Equal(frozen, GameJson.Serialize(first.StrategicTimeline));
        foreach (var command in new[] { "status", "", "nonsense", "quit" })
            Assert.Empty(session.Submit(command).StrategicTimeline.Steps);
        Assert.DoesNotContain("strategicTimeline", GameJson.Serialize(second), StringComparison.Ordinal);
    }

    [Fact]
    public void Disabled_and_throwing_subscribers_preserve_exact_state_events_transcript_seed_and_save_bytes()
    {
        var enabled = StrategicTravelObservationTests.Session();
        var disabled = StrategicTravelObservationTests.Session();
        disabled.DisableStrategicObservations();
        enabled.StrategicTimelinePublished += _ => throw new InvalidOperationException("presentation only");
        var aEvents = new List<DomainEvent>();
        var bEvents = new List<DomainEvent>();
        enabled.EventsPublished += aEvents.AddRange;
        disabled.EventsPublished += bEvents.AddRange;
        foreach (var command in new[] { "move a 2 0", "move a 4 1", "move a 4 1", "status", "end" })
        {
            var a = enabled.Submit(command);
            var b = disabled.Submit(command);
            Assert.Equal(a.Lines, b.Lines);
            Assert.Equal(a.Battles, b.Battles);
            Assert.Equal(enabled.State, disabled.State);
            Assert.Equal(aEvents, bEvents);
            Assert.Empty(b.StrategicTimeline.Steps);
            Assert.Equal(GameJson.Serialize(Save(enabled)), GameJson.Serialize(Save(disabled)));
        }
        var resumed = new GameSession(enabled.World, enabled.Ruleset, enabled.Scenario, Save(enabled));
        Assert.Empty(resumed.LastStrategicTimeline.Steps);
        Assert.Empty(resumed.Submit("status").StrategicTimeline.Steps);
    }

    [Fact]
    public void An_unexpected_submit_exception_exposes_no_partial_batch()
    {
        var session = StrategicTravelObservationTests.Session();
        Action<IReadOnlyList<DomainEvent>> fault = _ => throw new InvalidOperationException("existing domain subscriber");
        session.EventsPublished += fault;
        Assert.Throws<InvalidOperationException>(() => session.Submit("move a 2 0"));
        Assert.Empty(session.LastStrategicTimeline.Steps);
        session.EventsPublished -= fault;
        Assert.Empty(session.Submit("status").StrategicTimeline.Steps);
    }

    [Fact]
    public void Recorder_invariant_failure_disables_observation_without_changing_execution()
    {
        var session = StrategicTravelObservationTests.Session();
        var disabled = StrategicTravelObservationTests.Session();
        disabled.DisableStrategicObservations();
        var recorder = Recorder(session);
        Invoke(recorder, "BeginAttempt", new object());
        recorder.GetType().GetField("_cursor", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(recorder, 3);
        Invoke(recorder, "EndAttempt", true);
        var a = session.Submit("move a 2 0");
        var b = disabled.Submit("move a 2 0");
        Assert.Equal(a.Lines, b.Lines);
        Assert.Equal(session.State, disabled.State);
        Assert.Empty(a.StrategicTimeline.Steps);
    }

    [Fact]
    public void Tactical_suspensions_reserve_boundaries_and_injected_results_complete_once_without_replay()
    {
        var enabled = Battles();
        var disabled = Battles();
        disabled.DisableStrategicObservations();
        var steps = new List<StrategicTravelStep>();
        var eventsA = new List<DomainEvent>();
        var eventsB = new List<DomainEvent>();
        enabled.EventsPublished += eventsA.AddRange;
        disabled.EventsPublished += eventsB.AddRange;
        var first = enabled.Submit("end");
        var other = disabled.Submit("end");
        Assert.NotNull(first.OpenBattleView);
        Assert.Equal(first.Lines, other.Lines);
        Assert.Equal(enabled.State, disabled.State);
        var opening = Assert.Single(first.StrategicTimeline.Steps, s => s.Kind == StrategicTravelStepKind.BattleOpening);
        Assert.Equal("south", enabled.State.ActiveNationId);
        Assert.Equal("north", opening.ViewerNationId);
        steps.AddRange(first.StrategicTimeline.Steps);
        Assert.Same(first.StrategicTimeline, enabled.ConsumeStrategicTimeline());
        Assert.Empty(enabled.LastStrategicTimeline.Steps);
        Assert.Empty(enabled.ConsumeStrategicTimeline().Steps);
        Assert.Empty(enabled.Submit("battle show").StrategicTimeline.Steps);
        Assert.Empty(disabled.Submit("battle show").StrategicTimeline.Steps);
        var current = first;
        for (var i = 0; current.OpenBattleView is not null && i < 4; i++)
        {
            var a = enabled.Submit("battle general on");
            var b = disabled.Submit("battle general on");
            current = a;
            Assert.Equal(a.Battles, b.Battles);
            Assert.Equal(a.EndedBattleReport, b.EndedBattleReport);
            Assert.Equal(a.TacticalTimeline, b.TacticalTimeline);
            Assert.Equal(a.Lines, b.Lines);
            Assert.Equal(enabled.State, disabled.State);
            Assert.Equal(eventsA, eventsB);
            Assert.Empty(b.StrategicTimeline.Steps);
            steps.AddRange(a.StrategicTimeline.Steps);
        }
        Assert.Null(current.OpenBattleView);
        Assert.Equal("north", enabled.State.ActiveNationId);
        Assert.Equal(2, steps.Count(s => s.Kind == StrategicTravelStepKind.BattleOpening));
        Assert.Equal(2, steps.Count(s => s.Kind == StrategicTravelStepKind.BattleCompletion));
        foreach (var start in steps.Where(s => s.Kind == StrategicTravelStepKind.BattleOpening))
            Assert.Single(steps, s => s.Kind == StrategicTravelStepKind.BattleCompletion && s.ActionId == start.ActionId && s.LapId == start.LapId);
        Assert.Equal(steps.Count, steps.Select(s => s.StepId).Distinct().Count());
        for (var i = 1; i < steps.Count; i++) Assert.Equal(steps[i - 1].After, steps[i].Before);
        Assert.Equal(steps.OrderBy(s => s.StepId).Select(s => s.StepId), steps.Select(s => s.StepId));
        Assert.Equal(steps.Count(s => s.Kind == StrategicTravelStepKind.Command),
            steps.Where(s => s.Kind == StrategicTravelStepKind.Command).Select(s => s.ActionId).Distinct().Count());
    }

    [Fact]
    public void Approach_to_tactical_attack_is_delivered_with_the_suspended_prefix()
    {
        var armies = new[]
        {
            TacticalSessionTestbed.Army("north-army-1", "north", 2, 3, 68, TacticalSessionTestbed.Unit("light_infantry", 400, 6, "Foot")),
            TacticalSessionTestbed.Army("south-army-1", "south", 4, 3, 59, TacticalSessionTestbed.Unit("heavy_infantry", 6000, 6, "Guards")),
        };
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, armies: armies);
        session.Submit("declare-war south");
        var output = session.Submit("end");
        Assert.NotNull(output.OpenBattleView);
        var opening = Assert.Single(output.StrategicTimeline.Steps, s => s.Kind == StrategicTravelStepKind.BattleOpening);
        var approach = Assert.Single(output.StrategicTimeline.Steps, s => s.Routes.Any(r => r.EntityId == "south-army-1"));
        Assert.True(approach.StepId < opening.StepId);
        Assert.Equal(new GridPoint(3, 3), Assert.Single(approach.Routes).Destination);
        Assert.Equal(4, session.State.ArmyById("south-army-1")!.X);
        Assert.Equal(3, opening.After.ArmyById("south-army-1")!.X);
        var close = session.Submit("battle general on");
        Assert.DoesNotContain(close.StrategicTimeline.Steps.SelectMany(s => s.Routes), r => r.EntityId == "south-army-1" && r.Origin == new GridPoint(4, 3));
        Assert.Contains(close.StrategicTimeline.Steps, s => s.Kind == StrategicTravelStepKind.BattleCompletion);
    }

    [Theory]
    [InlineData(HumanBattleMode.Instant)]
    [InlineData(HumanBattleMode.Tactical)]
    public void Rolled_back_composed_declarations_leave_no_observation(HumanBattleMode mode)
    {
        var toy = CoreTestbed.Toy;
        var session = new GameSession(toy.World, toy.Ruleset, toy.Scenario) { HumanBattles = mode };
        var before = session.State;
        var output = session.Submit("attack-army north-army-1 south-army-1");
        Assert.Equal(before, session.State);
        Assert.Empty(output.StrategicTimeline.Steps);
    }

    [Fact]
    public void Embark_disembark_and_fleet_merge_reconcile_without_inventing_routes()
    {
        var session = StrategicTravelObservationTests.Session(change: state => state with
        {
            Armies = ValueList<ArmyState>.Of(state.Armies[0] with { X = 1, Units = ValueList<UnitSlot>.Of(new UnitSlot(0, "light_infantry", 100, 6, "Foot")) }),
            Fleets = ValueList<FleetState>.Of(new FleetState("f", "north", 0, 0, 10, 10, 100, 0, 100, null, null, null, 1)),
        });
        var embarked = session.Submit("embark-army a f");
        var step = Assert.Single(embarked.StrategicTimeline.Steps, s => s.Kind == StrategicTravelStepKind.Command);
        Assert.Empty(step.Routes);
        Assert.True(step.MarkersBefore.Single(m => m.EntityId == "a").Visible);
        Assert.False(step.MarkersAfter.Single(m => m.EntityId == "a").Visible);
        Assert.Equal("f", step.MarkersAfter.Single(m => m.EntityId == "a").CarrierId);
        var landed = session.Submit("disembark-army a 1 0");
        step = Assert.Single(landed.StrategicTimeline.Steps, s => s.Kind == StrategicTravelStepKind.Command);
        Assert.Empty(step.Routes);
        Assert.False(step.MarkersBefore.Single(m => m.EntityId == "a").Visible);
        Assert.True(step.MarkersAfter.Single(m => m.EntityId == "a").Visible);
        var mergeSession = StrategicTravelObservationTests.Session(true, change: state => state with
        {
            Fleets = ValueList.From(state.Fleets.Append(state.Fleets[0] with { Id = "second", X = 1, Ships = 20 })),
        });
        var merged = mergeSession.Submit("join-fleets f second");
        step = Assert.Single(merged.StrategicTimeline.Steps, s => s.Kind == StrategicTravelStepKind.Command);
        Assert.Empty(step.Routes);
        Assert.Contains(step.MarkersBefore, m => m.EntityId == "second");
        Assert.DoesNotContain(step.MarkersAfter, m => m.EntityId == "second");
        Assert.Equal(30, step.MarkersBefore.Single(m => m.EntityId == "f").Size);
        Assert.Equal(50, step.MarkersAfter.Single(m => m.EntityId == "f").Size);
    }

    [Fact]
    public void Siege_capture_retains_pre_capture_owner_and_no_atomic_travel()
    {
        var source = CoreTestbed.InitialState();
        var session = StrategicTravelObservationTests.Session(change: state => state with
        {
            Armies = ValueList<ArmyState>.Of(state.Armies[0] with
            {
                Units = ValueList<UnitSlot>.Of(new UnitSlot(0, "archers", 400_000, 6, "Siege")),
            }),
            Cities = ValueList<CityState>.Of(source.Cities[0] with
            {
                Id = "target", X = 2, Y = 0, Owner = "south", Allegiance = "south", Loyalty = 0,
                Garrison = ValueList<UnitSlot>.Empty, FortificationCode = 0,
            }),
        });
        var march = session.Submit("move a 1 0");
        Assert.Single(march.StrategicTimeline.Steps.SelectMany(s => s.Routes));
        var snapshot = march.StrategicTimeline.Steps.First().MarkersBefore.Single(m => m.EntityId == "target");
        var siege = session.Submit("besiege-city a target");
        Assert.Single(siege.Battles);
        Assert.Equal("north", session.State.CityById("target")!.Owner);
        Assert.Equal("south", snapshot.OwnerId);
        var capture = Assert.Single(siege.StrategicTimeline.Steps,
            s => s.MarkersBefore.Any(m => m.EntityId == "target" && m.OwnerId == "south")
                 && s.MarkersAfter.Any(m => m.EntityId == "target" && m.OwnerId == "north"));
        Assert.Empty(capture.Routes);
        Assert.True(capture.StepId > march.StrategicTimeline.Steps.Last().StepId);
    }

    [Fact]
    public void Construction_launch_is_a_completed_system_reconciliation_without_a_walk()
    {
        var toy = CoreTestbed.Toy;
        var state = CoreTestbed.InitialState() with
        {
            Armies = ValueList<ArmyState>.Empty,
            Nations = ValueList.From(CoreTestbed.InitialState().Nations.Select(n => n with { Control = SeatControl.Human })),
            Fleets = ValueList<FleetState>.Of(new FleetState("building", "north", 0, 0, 0, 10, 100, 0, 100,
                1, "arx", null, null)),
        };
        // Arx is coastal in this synthetic layout, so the real fleet tick can choose an adjacent sea cell.
        var sea = toy.World.TileTypes.First(t => t.Id == "sea_calm").Code;
        var world = toy.World with { Terrain = new(TerrainEncoding.RunLength, ValueList<TerrainRun>.Of(new TerrainRun(sea, 48))) };
        var session = From(state, world);
        var steps = session.Submit("end").StrategicTimeline.Steps.Concat(session.Submit("end").StrategicTimeline.Steps).ToList();
        var launch = Assert.Single(steps, s => s.Kind == StrategicTravelStepKind.System
            && s.MarkersBefore.Any(m => m.EntityId == "building" && !m.Visible)
            && s.MarkersAfter.Any(m => m.EntityId == "building" && m.Visible));
        Assert.Contains("naval.fleet-tick", launch.Identity);
        Assert.Empty(launch.Routes);
        Assert.False(session.State.FleetById("building")!.IsUnderConstruction);
    }

    [Fact]
    public void Actual_ai_fleet_approach_is_observed_with_other_seats_in_one_submit()
    {
        var toy = CoreTestbed.Toy;
        var state = CoreTestbed.InitialState();
        state = state with
        {
            Armies = ValueList<ArmyState>.Empty,
            Fleets = ValueList<FleetState>.Of(
                new FleetState("human", "north", 0, 5, 5, 10, 100, 0, 500, null, null, null, 0),
                new FleetState("computer", "south", 0, 1, 5, 10, 100, 0, 500, null, null, null, 0)),
            Relations = state.Relations.WithRelation("north", "south", toy.Ruleset.Diplomacy.StateCodes.War),
        };
        var session = From(state, toy.World);
        var output = session.Submit("end");
        var step = Assert.Single(output.StrategicTimeline.Steps, s => s.Routes.Any(r => r.EntityId == "computer"));
        Assert.Equal(StrategicMarkerKind.Fleet, Assert.Single(step.Routes).Kind);
        Assert.Equal("south", Assert.Single(step.Routes).OwnerId);
        Assert.Equal("north", step.ViewerNationId);
        Assert.Equal("north", session.State.ActiveNationId);
        Assert.True(output.StrategicTimeline.Steps.Count > 1);
    }

    private static GameSession From(GameState state, World world)
    {
        var toy = CoreTestbed.Toy;
        return new GameSession(world, toy.Ruleset, toy.Scenario,
            new SaveGame(GameDataSchema.CurrentVersion, "test", "test", toy.Scenario.Id, world.Id, toy.Ruleset.Id, state));
    }

    [Fact]
    public void Construction_prelude_is_delivered_once_for_the_designated_viewer()
    {
        var toy = CoreTestbed.Toy;
        var session = new GameSession(toy.World, toy.Ruleset, toy.Scenario, humanSeatNationId: "south");
        var output = session.Submit("status");
        Assert.NotEmpty(output.StrategicTimeline.Steps);
        Assert.All(output.StrategicTimeline.Steps, s => Assert.Equal("south", s.ViewerNationId));
        Assert.Empty(session.Submit("status").StrategicTimeline.Steps);
    }

    [Fact]
    public void Battle_removal_or_scatter_reconciles_atomically_after_a_real_route()
    {
        var armies = TacticalSessionTestbed.StrongAttacker();
        armies[0] = armies[0] with { X = 2 };
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Instant, armies: armies);
        var move = session.Submit("move north-army-1 3 2");
        Assert.Single(move.StrategicTimeline.Steps.SelectMany(s => s.Routes));
        var before = session.State.ArmyById("south-army-1")!;
        var battle = session.Submit("attack-army north-army-1 south-army-1");
        Assert.Single(battle.Battles);
        Assert.Empty(battle.StrategicTimeline.Steps.SelectMany(s => s.Routes));
        var step = Assert.Single(battle.StrategicTimeline.Steps, s => s.Events.OfType<BattleResolved>().Any());
        Assert.Contains(step.MarkersBefore, m => m.EntityId == before.Id);
        Assert.True(step.After.ArmyById(before.Id) is null || !Equals(before, step.After.ArmyById(before.Id)));
        Assert.True(step.StepId > move.StrategicTimeline.Steps.Last().StepId);
    }

    [Fact]
    public void Actual_ai_fallback_uses_real_session_recorder_and_discards_the_noop_route()
    {
        // As in AiGarrisonFallbackTests, inject an accepted same-tile first order to exercise
        // the defensive fallback. A truthful planner now excludes every first-blocked march.
        var state = AiScriptedStates.WithActiveSeat(BattleCommandTestbed.StateWith(
            new[]
            {
                CaptureFixtures.Nation("north", treasury: 1000, capitalCityId: "n-cap") with { Personality = AiScriptedStates.DefaultPersonality },
                CaptureFixtures.Nation("south", capitalCityId: "s-cap") with { Personality = AiScriptedStates.DefaultPersonality },
            },
            new[]
            {
                CaptureFixtures.City("n-cap", "Capital", 1, 1, "north", "north", 90, 100, 100, 100, 0) with { SupplyTons = 5000 },
                CaptureFixtures.City("r-city", "Resupply", 6, 3, "north", "north", 90, 100, 100, 100, 0) with { SupplyTons = 0 },
                CaptureFixtures.City("s-cap", "Other", 5, 4, "south", "south", 90, 100, 100, 100, 0),
            },
            new[] { CaptureFixtures.Army("a1", "north", 3, 3, 60, CaptureFixtures.Unit("light_infantry", 10000)) with { Moves = 9, Money = 500 } }), "north");
        var session = From(state, AiScriptedStates.World);
        var dispatcher = (CommandDispatcher)typeof(GameSession).GetField("_dispatcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var injected = new NoopFirstMove(dispatcher);
        var events = new RecordingEventSink();
        var outcome = AiTurn.Run(state, session.Ruleset, session.World, injected,
            SplitMix64Rng.ForStream(1, "ai.turn"), events);
        Assert.True(injected.Replaced);
        Assert.Contains(outcome.Log, line => line.Contains("garrison fallback moved", StringComparison.Ordinal));
        Assert.Equal(0, outcome.CommandsRejected);
        typeof(GameSession).GetProperty(nameof(GameSession.State))!.SetValue(session, outcome.State);
        var output = session.Submit("status");
        var route = Assert.Single(output.StrategicTimeline.Steps.SelectMany(s => s.Routes));
        Assert.Equal("a1", route.EntityId);
        Assert.Equal(new GridPoint(3, 3), route.Origin);
        var army = outcome.State.ArmyById("a1")!;
        Assert.Equal(new GridPoint(army.X, army.Y), route.Destination);
        var noop = Assert.Single(output.StrategicTimeline.Steps,
            s => s.Events.OfType<ArmyMoved>().Any(m => m.FromX == m.ToX && m.FromY == m.ToY));
        Assert.Empty(noop.Routes);
        Assert.True(noop.StepId < output.StrategicTimeline.Steps.Single(s => s.Routes.Count > 0).StepId);
    }

    private sealed class NoopFirstMove(ICommandDispatch inner) : ICommandDispatch
    {
        public bool Replaced { get; private set; }
        public CommandResult Dispatch(GameState state, ICommand command) => Dispatch(state, command, NullEventSink.Instance);
        public CommandResult Dispatch(GameState state, ICommand command, IEventSink events)
        {
            if (!Replaced && command is MoveArmyCommand move)
            {
                Replaced = true;
                command = move with { X = 3, Y = 3 };
            }
            return inner.Dispatch(state, command, events);
        }
    }

    [Fact]
    public void Composed_human_tactical_opening_has_a_complete_snapshot_chain_including_news()
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical);
        var opening = session.Submit("attack-army north-army-1 south-army-1");
        Assert.NotNull(opening.OpenBattleView);
        var steps = opening.StrategicTimeline.Steps;
        Assert.Contains(steps, s => s.Identity == "human-battle-opening" && s.Kind == StrategicTravelStepKind.Reconciliation);
        for (var i = 1; i < steps.Count; i++) Assert.Equal(steps[i - 1].After, steps[i].Before);
        Assert.Equal(session.State, steps.Last().After);
        var state = session.State;
        session.DisableStrategicObservations();
        Assert.Empty(session.LastStrategicTimeline.Steps);
        Assert.Same(state, session.State);
    }

    [Fact]
    public void Accepted_commands_reconcile_exact_pre_action_noncommand_changes_without_a_route()
    {
        var session = StrategicTravelObservationTests.Session(change: state => state with
        {
            Cities = ValueList<CityState>.Of(CoreTestbed.InitialState().Cities[0] with { Id = "city", X = 7, Y = 5, Owner = "south" }),
        });
        var dispatcher = (CommandDispatcher)typeof(GameSession).GetField("_dispatcher", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        var first = dispatcher.Dispatch(session.State, new MoveArmyCommand("north", "a", 1, 0));
        Assert.True(first.IsAccepted);
        // Supply an immutable noncommand ownership boundary between actual accepted commands.
        var changed = first.State with { Cities = ValueList.From(first.State.Cities.Select(c => c with { Owner = "north" })) };
        var second = dispatcher.Dispatch(changed, new MoveArmyCommand("north", "a", 2, 0));
        Assert.True(second.IsAccepted);
        typeof(GameSession).GetProperty(nameof(GameSession.State))!.SetValue(session, second.State);
        var steps = session.Submit("status").StrategicTimeline.Steps;
        var reconciliation = Assert.Single(steps, s => s.Kind == StrategicTravelStepKind.Reconciliation);
        Assert.Empty(reconciliation.Routes);
        Assert.Equal("south", reconciliation.MarkersBefore.Single(m => m.EntityId == "city").OwnerId);
        Assert.Equal("north", reconciliation.MarkersAfter.Single(m => m.EntityId == "city").OwnerId);
        Assert.Equal(2, steps.SelectMany(s => s.Routes).Count());
        for (var i = 1; i < steps.Count; i++) Assert.Equal(steps[i - 1].After, steps[i].Before);
    }

    private static GameSession Battles()
    {
        var armies = new[]
        {
            TacticalSessionTestbed.Army("north-army-1", "north", 3, 2, 68, TacticalSessionTestbed.Unit("light_infantry", 400, 6, "Foot")),
            TacticalSessionTestbed.Army("north-army-2", "north", 6, 4, 68, TacticalSessionTestbed.Unit("light_infantry", 400, 6, "Foot")),
            TacticalSessionTestbed.Army("south-army-1", "south", 4, 2, 59, TacticalSessionTestbed.Unit("heavy_infantry", 6000, 6, "Guards")),
            TacticalSessionTestbed.Army("south-army-2", "south", 6, 5, 59, TacticalSessionTestbed.Unit("heavy_infantry", 6000, 6, "Guards")),
        };
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical, armies: armies);
        session.Submit("declare-war south");
        return session;
    }

    internal static SaveGame Save(GameSession s) => new(GameDataSchema.CurrentVersion, "test", "test", s.Scenario.Id, s.World.Id, s.Ruleset.Id, s.State);
    private static object Recorder(GameSession s) => typeof(GameSession).GetField("_strategicTimeline", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(s)!;
    private static object? Invoke(object target, string name, params object[] args) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(target, args);
}

