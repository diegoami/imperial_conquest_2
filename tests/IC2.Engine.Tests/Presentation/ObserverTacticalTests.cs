using IC2.Engine.Ai;
using IC2.Engine.Battle.Tactical;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

public sealed class ObserverTacticalTests
{
    private static GameSession Build(bool tactical = true, bool capture = true)
    {
        var toy = TacticalSessionTestbed.Toy;
        var world = toy.World with { Width = 20, Height = 15,
            Terrain = new(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(2, 20 * 15))),
            StartingFleets = ValueList<StartingFleet>.Empty,
            StartingArmies = ValueList.Of(
                TacticalSessionTestbed.Army("north-army-1", "north", 3, 3, 68,
                    TacticalSessionTestbed.Unit("heavy_infantry", 20000, 6, "Strong")),
                TacticalSessionTestbed.Army("south-army-1", "south", 4, 3, 60,
                    TacticalSessionTestbed.Unit("light_infantry", 1000, 6, "Weak"))) };
        var scenario = toy.Scenario with { Seats = ValueList.From(toy.Scenario.Seats.Select(s => s with { Control = SeatControl.Ai })) };
        var initial = new GameSession(world, toy.Ruleset, scenario, seedOverride: 42);
        var state = initial.State with { Relations = initial.State.Relations.WithRelation("north", "south", toy.Ruleset.Diplomacy.StateCodes.War) };
        return new GameSession(world, toy.Ruleset, scenario, new SaveGame(state.SchemaVersion, "observer", "observer",
            state.ScenarioId, state.WorldId, state.RulesetId, state))
        { ObserverTacticalBattles = tactical, CaptureObserverBattle = (_, _) => capture };
    }

    [Fact]
    public void Full_tactical_observation_uses_both_generals_and_continues_one_AI_pass()
    {
        var session = Build();
        var events = new List<DomainEvent>();
        session.EventsPublished += events.AddRange;
        var output = session.Submit("observer-step");
        var replay = Assert.Single(output.ObserverBattles);
        Assert.NotEmpty(replay.Frames);
        Assert.All(replay.Frames, frame => Assert.All(frame.After.View.ComputerControlled, Assert.True));
        Assert.Contains(replay.Frames, f => f.Kind == TacticalFrameKind.Terminal);
        Assert.True(replay.Frames[^1].After.View.IsOver);
        Assert.True(replay.Report.ExchangeLog.Count > 0);
        Assert.Null(session.OpenTacticalBattle);
        Assert.Null(output.OpenBattleView);
        Assert.Equal("south", session.State.ActiveNationId);
        var pass = Assert.Single(events.OfType<AiTurnDecided>(), e => e.NationId == "north");
        Assert.Equal(0, pass.CommandsRejected);
        Assert.Single(output.StrategicTimeline.Steps, s => s.Identity == "observer:" + replay.BattleId);
        Assert.Equal(output.ObserverBattles, session.LastObserverBattles);
    }

    [Fact]
    public void Captured_and_fast_forwarded_tactical_fights_have_identical_state_seed_and_reports()
    {
        var watched = Build(capture: true);
        var unwatched = Build(capture: false);
        var events = new List<DomainEvent>();
        var other = new List<DomainEvent>();
        watched.EventsPublished += events.AddRange; unwatched.EventsPublished += other.AddRange;
        var visible = watched.Submit("observer-step");
        var quick = unwatched.Submit("observer-step");
        Assert.Equal(watched.State, unwatched.State);
        Assert.Equal(events, other);
        Assert.Equal(Assert.Single(visible.ObserverBattles).Report, Assert.Single(quick.ObserverBattles).Report);
        Assert.Empty(quick.ObserverBattles[0].Frames);
        var retained = visible.ObserverBattles;
        watched.Submit("status");
        Assert.Empty(watched.LastObserverBattles);
        Assert.NotEmpty(retained[0].Frames);
    }

    [Fact]
    public void Failed_capture_filter_does_not_change_the_battle_or_random_seed()
    {
        var expected = Build(capture: false); expected.Submit("observer-step");
        var failed = Build(); failed.CaptureObserverBattle = (_, _) => throw new InvalidOperationException("view failure");
        var output = failed.Submit("observer-step");
        Assert.Equal(expected.State, failed.State);
        Assert.Empty(Assert.Single(output.ObserverBattles).Frames);
    }

    [Fact]
    public void Legacy_instant_watch_stays_explicit_and_has_no_tactical_replay()
    {
        var plain = Build(tactical: false);
        var instant = Build(); instant.ObserverTacticalBattles = false;
        var a = plain.Submit("observer-step"); var b = instant.Submit("observer-step");
        Assert.Equal(plain.State, instant.State);
        Assert.Equal(a.Lines, b.Lines);
        Assert.Empty(a.ObserverBattles);
        Assert.NotEmpty(a.Battles);
    }

    [Fact]
    public void Automation_budget_aborts_without_advancing_the_seat_or_applying_partial_battles()
    {
        var session = Build();
        session.ObserverAutomaticHalfRoundLimit = 1;
        var before = session.State;
        Assert.Throws<TacticalAutomationLimitException>(() => session.Submit("observer-step"));
        Assert.Equal(before, session.State);
        Assert.Empty(session.LastObserverBattles);
        Assert.Equal(StrategicTravelBatch.Empty, session.LastStrategicTimeline);
        session.ObserverAutomaticHalfRoundLimit = 1000;
        session.Submit("observer-step");
        var fresh = Build(); fresh.Submit("observer-step");
        Assert.Equal(fresh.State, session.State); // Retrying cannot reroll a failed computation.
    }
}
