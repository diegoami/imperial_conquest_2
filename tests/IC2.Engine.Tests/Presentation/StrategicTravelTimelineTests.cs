using System.Reflection;
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
        Assert.Empty(enabled.Submit("battle show").StrategicTimeline.Steps);
        Assert.Empty(disabled.Submit("battle show").StrategicTimeline.Steps);
        var current = first;
        for (var i = 0; current.OpenBattleView is not null && i < 4; i++)
        {
            var a = enabled.Submit("battle general on");
            var b = disabled.Submit("battle general on");
            current = a;
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
