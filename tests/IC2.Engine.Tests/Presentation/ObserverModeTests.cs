using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

public sealed class ObserverModeTests
{
    private static GameSession Observer()
    {
        var f = TacticalSessionTestbed.Toy;
        return new(f.World, f.Ruleset, f.Scenario with {
            Seats = ValueList.From(f.Scenario.Seats.Select(s => s with { Control = SeatControl.Ai })) })
            { HumanBattles = HumanBattleMode.Tactical };
    }

    [Fact]
    public void Seat_steps_match_existing_watch_rounds_including_state_and_rng()
    {
        var stepped = Observer();
        var reference = Observer();
        var controller = new ObserverRunController();
        Assert.True(stepped.IsObserverMode);
        Assert.Null(controller.Advance(stepped));
        for (var round = 0; round < 2; round++)
        {
            var turn = stepped.State.Calendar.TurnIndex;
            controller.NextRound(stepped);
            var count = 0;
            while (controller.IsRunning && count++ < stepped.State.Nations.Count + 1)
                Assert.NotNull(controller.Advance(stepped));
            Assert.False(controller.IsRunning);
            Assert.Equal(turn + 1, stepped.State.Calendar.TurnIndex);
            reference.Submit("end");
            Assert.Equal(reference.State, stepped.State);
            var paused = stepped.State;
            Assert.Null(controller.Advance(stepped));
            Assert.Same(paused, stepped.State);
        }
    }

    [Fact]
    public void Pause_between_seats_and_new_controller_after_load_do_not_advance()
    {
        var session = Observer();
        var controller = new ObserverRunController();
        controller.Play();
        Assert.NotNull(controller.Advance(session));
        controller.Pause();
        var state = session.State;
        Assert.Null(controller.Advance(session));
        Assert.Same(state, session.State);
        var path = Path.Combine(Path.GetTempPath(), "ic2-observer-" + Guid.NewGuid().ToString("N") + ".json");
        var loaded = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical);
        try
        {
            session.Submit($"save {path}");
            Assert.True(File.Exists(path));
            loaded.Submit($"load {path}");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        Assert.True(loaded.IsObserverMode);
        Assert.Equal(state, loaded.State);
        Assert.Null(new ObserverRunController().Advance(loaded));
        var expected = session.Submit("observer-step");
        loaded.Submit("observer-step");
        Assert.False(expected.ShouldExit);
        Assert.Equal(session.State, loaded.State);
    }

    [Fact]
    public void Entering_observer_changes_only_control_without_spending_rng_or_time()
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical);
        var before = session.State;
        var output = session.Submit("observe");
        Assert.Contains(output.Lines, l => l.Contains("Observation starts paused", StringComparison.Ordinal));
        Assert.True(session.IsObserverMode);
        Assert.Equal(before with { Nations = ValueList.From(before.Nations.Select(n => n with { Control = SeatControl.Ai })) }, session.State);
        var actor = session.State.Armies[0];
        var observed = session.State;
        session.Submit($"move {actor.Id} {actor.X + 1} {actor.Y}");
        Assert.Equal(observed, session.State);
        session.Submit("set-tax 20");
        Assert.Equal(observed, session.State);
    }

    [Fact]
    public void Ordinary_campaign_cannot_be_advanced_by_observer_step()
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical);
        var before = session.State;
        session.Submit("observer-step");
        Assert.False(session.IsObserverMode);
        Assert.Equal(before, session.State);
        var controller = new ObserverRunController();
        controller.Play();
        Assert.Null(controller.Advance(session));
        Assert.False(controller.IsRunning);
    }

    [Fact]
    public void Entering_observation_cannot_discard_a_tactical_battle()
    {
        var f = TacticalSessionTestbed.For("toy", HumanBattleMode.Tactical);
        Assert.NotNull(f.Session.Submit($"attack-army {f.AttackerId} {f.DefenderId}").OpenBattleView);
        var before = f.Session.State;
        f.Session.Submit("observe");
        Assert.False(f.Session.IsObserverMode);
        Assert.Equal(before, f.Session.State);
        Assert.NotNull(f.Session.OpenTacticalBattle);
    }

    [Fact]
    public void Taking_the_active_nation_changes_only_control_and_keeps_human_battles_tactical()
    {
        var session = Observer();
        var before = session.State;
        var nation = before.ActiveNationId;
        session.Submit($"take-control {nation}");
        Assert.False(session.IsObserverMode);
        Assert.Equal(before with { Nations = ValueList.From(before.Nations.Select(n => n with {
            Control = n.Id == nation ? SeatControl.Human : SeatControl.Ai })) }, session.State);
        Assert.Equal(HumanBattleMode.Tactical, session.HumanBattles);
        var controller = new ObserverRunController();
        controller.Play();
        Assert.Null(controller.Advance(session));
    }

    [Fact]
    public void Taking_a_later_nation_runs_intervening_AI_but_not_that_nations_AI_turn()
    {
        var session = Observer();
        var chosen = session.State.TurnOrder.First(id => id != session.State.ActiveNationId);
        var events = new List<IC2.Engine.Core.DomainEvent>();
        session.EventsPublished += batch => events.AddRange(batch);
        session.Submit($"take-control {chosen}");
        Assert.Equal(chosen, session.State.ActiveNationId);
        Assert.Equal(SeatControl.Human, session.State.NationById(chosen)!.Control);
        Assert.Single(session.State.Nations, n => n.Control == SeatControl.Human);
        Assert.NotEmpty(events.OfType<IC2.Engine.Ai.AiTurnDecided>());
        Assert.DoesNotContain(events.OfType<IC2.Engine.Ai.AiTurnDecided>(), e => e.NationId == chosen);
        var before = session.State;
        session.Submit("take-control missing");
        Assert.Equal(before, session.State);
    }

    [Fact]
    public void Entering_observation_refuses_an_unresolved_offer_without_losing_it()
    {
        var session = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical);
        var f = TacticalSessionTestbed.Toy;
        var state = session.State with { PendingOffer = new PendingDiplomaticOffer("south", f.Ruleset.Diplomacy.StateCodes.Trade) };
        var save = new SaveGame(GameDataSchema.CurrentVersion, "offer", "Offer", f.Scenario.Id,
            f.World.Id, f.Ruleset.Id, state);
        var loaded = new GameSession(f.World, f.Ruleset, f.Scenario, save);
        loaded.Submit("observe");
        Assert.False(loaded.IsObserverMode);
        Assert.Equal(state, loaded.State);
    }

    [Fact]
    public void Losing_the_last_human_seat_does_not_silently_opt_into_the_new_observer_UI()
    {
        var normal = TacticalSessionTestbed.ToySession(HumanBattleMode.Tactical);
        var f = TacticalSessionTestbed.Toy;
        var state = normal.State with { Nations = ValueList.From(normal.State.Nations.Select(n =>
            n.Id == "north" ? n with { Treasury = -1_000_000 } : n)) };
        var save = new SaveGame(GameDataSchema.CurrentVersion, "debt", "Debt", f.Scenario.Id,
            f.World.Id, f.Ruleset.Id, state);
        var session = new GameSession(f.World, f.Ruleset, f.Scenario, save);
        session.Submit("end");
        Assert.True(session.IsGameOver);
        Assert.False(session.IsObserverMode);
    }

    [Fact]
    public void The_surviving_barbarian_faction_cannot_be_taken_over()
    {
        var f = GameDataRepository.Load(IC2.Engine.Tests.Model.TestPaths.DataRoot).Resolve("italy-500bc-barbarians");
        var scenario = f.Scenario with { Seats = ValueList.From(f.Scenario.Seats.Select(s => s with { Control = SeatControl.Ai })) };
        var session = new GameSession(f.World, f.Ruleset, scenario);
        Assert.True(session.IsObserverMode);
        var barbarian = scenario.Barbarians!.NationId;
        Assert.False(session.State.NationById(barbarian)!.Eliminated);
        var before = session.State;
        session.Submit($"take-control {barbarian}");
        Assert.Equal(before, session.State);
        Assert.True(session.IsObserverMode);
    }
}
