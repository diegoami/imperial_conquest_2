using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

public sealed class FaithfulPartialAiPassTests
{
    private static GameSession Session(bool faithful, HumanBattleMode mode, bool two = false)
    {
        var toy = TacticalSessionTestbed.Toy;
        var cells = new int[320 * 140];
        for (var y = 0; y < 140; y++) for (var x = 0; x <= 100; x++) cells[y * 320 + x] = 2;
        var world = toy.World with { Id = "classical-mediterranean", Width = 320, Height = 140,
            Terrain = new(TerrainEncoding.RunLength, ValueList.From(cells.Select(c => new TerrainRun(c, 1)))),
            Cities = ValueList.From(toy.World.Cities.Where(c => c.Id is "arx" or "meridia").Select(c => c with {
                X = c.Owner == "south" ? 100 : 90, Y = 40, FortificationCode = 100 })),
            StartingArmies = ValueList.Of(
                TacticalSessionTestbed.Army("north-army-1", "north", 49, 30, 68,
                    TacticalSessionTestbed.Unit("light_infantry", 400, 6, "Human")),
                TacticalSessionTestbed.Army("south-army-1", "south", 50, 30, 59,
                    TacticalSessionTestbed.Unit("heavy_infantry", 25000, 6, "First")) with { Money = 77 },
                TacticalSessionTestbed.Army("south-waiting", "south", 99, 36, 59,
                    TacticalSessionTestbed.Unit("heavy_infantry", 25000, 6, "Waiting")) with { Money = 77, SupplyTons = 0 }),
            StartingFleets = ValueList.Of(new StartingFleet("south-fleet", "south", 105, 40, 90, 100, 0, 720, 26)) };
        if (two) world = world with { StartingArmies = ValueList.From(world.StartingArmies.Concat(new[] {
            TacticalSessionTestbed.Army("north-army-2", "north", 58, 30, 68,
                TacticalSessionTestbed.Unit("light_infantry", 400, 6, "Human2")),
            TacticalSessionTestbed.Army("south-army-2", "south", 59, 30, 59,
                TacticalSessionTestbed.Unit("heavy_infantry", 25000, 6, "Second"))
        })) };
        var rules = toy.Ruleset with { Flags = toy.Ruleset.Flags with {
            AiEconomy = faithful ? AiEconomyModel.Original : AiEconomyModel.Designed,
            CombatOnDefeat = DefeatOutcome.Destroyed,
            UnitTableCompaction = faithful ? UnitTableCompactionPolicy.SwapRemoveSweep : toy.Ruleset.Flags.UnitTableCompaction } };
        var session = new GameSession(world, rules, toy.Scenario with { WorldId = world.Id }) { HumanBattles = mode };
        Assert.Contains(session.Submit("declare-war south").Lines, l => l.Contains("accepted", StringComparison.Ordinal));
        return session;
    }

    [Fact]
    public void Faithful_retains_the_first_fleet_pass_and_restarts_the_same_seat_after_the_human_battle()
    {
        var session = Session(true, HumanBattleMode.Tactical);
        var before = session.State;
        var events = new List<DomainEvent>();session.EventsPublished += e => events.AddRange(e);
        var opened = session.Submit("end");
        Assert.NotNull(opened.OpenBattleView);
        Assert.Equal("south", session.State.ActiveNationId);
        Assert.Equal(before.Calendar, session.State.Calendar);
        var firstFleet = session.State.FleetById("south-fleet")!;
        Assert.NotEqual((105, 40), (firstFleet.X, firstFleet.Y));
        Assert.InRange(firstFleet.Moves, 1, 25);
        Assert.Equal(77, session.State.ArmyById("south-waiting")!.Money); // This army has not had its support pass yet.
        Assert.Single(events.OfType<AiTurnDecided>(), e => e.NationId == "south");
        Assert.Single(opened.StrategicTimeline.Steps.SelectMany(s => s.Routes), r => r.EntityId == "south-fleet");
        var closed = session.Submit("battle general on");
        Assert.Null(closed.OpenBattleView);
        Assert.NotNull(closed.EndedBattleReport);
        Assert.Equal("north", session.State.ActiveNationId);
        Assert.Equal(before.Calendar.TurnIndex + 1, session.State.Calendar.TurnIndex);
        var passes = events.OfType<AiTurnDecided>().Where(e => e.NationId == "south").ToArray();
        Assert.Equal(2, passes.Length);
        Assert.All(passes, p => Assert.Equal(0, p.CommandsRejected));
        Assert.DoesNotContain(closed.StrategicTimeline.Steps.SelectMany(s => s.Routes),
            r => r.EntityId == "south-fleet" && r.Origin.X == 105 && r.Origin.Y == 40);
        Assert.True(session.State.ArmyById("south-waiting")!.Money >= 500);
    }

    [Fact]
    public void Improved_tactical_preserves_the_existing_pre_turn_replay_policy()
    {
        var session = Session(false, HumanBattleMode.Tactical);
        var before = session.State;
        var opened = session.Submit("end");
        Assert.NotNull(opened.OpenBattleView);
        Assert.Equal(before.Fleets, session.State.Fleets);
        var events = new List<DomainEvent>();session.EventsPublished += e => events.AddRange(e);
        var closed = session.Submit("battle general on");
        Assert.Null(closed.OpenBattleView);
        Assert.Single(events.OfType<AiTurnDecided>(), e => e.NationId == "south");
    }

    [Fact]
    public void Each_human_battle_retains_its_pass_and_advances_the_seat_only_after_the_last_restart()
    {
        var session = Session(true, HumanBattleMode.Tactical, two: true);
        var turn = session.State.Calendar.TurnIndex;
        var events = new List<DomainEvent>();session.EventsPublished += e => events.AddRange(e);
        Assert.NotNull(session.Submit("end").OpenBattleView);
        var second = session.Submit("battle general on");
        Assert.NotNull(second.EndedBattleReport);
        Assert.NotNull(second.OpenBattleView);
        Assert.Equal("south", session.State.ActiveNationId);
        Assert.Equal(turn, session.State.Calendar.TurnIndex);
        var completed = session.Submit("battle general on");
        Assert.Null(completed.OpenBattleView);
        Assert.Equal("north", session.State.ActiveNationId);
        Assert.Equal(turn + 1, session.State.Calendar.TurnIndex);
        Assert.Equal(3, events.OfType<AiTurnDecided>().Count(e => e.NationId == "south"));
        Assert.All(events.OfType<AiTurnDecided>(), e => Assert.Equal(0, e.CommandsRejected));
    }

    [Fact]
    public void Approved_Instant_mode_still_runs_one_AI_pass_without_a_tactical_pause()
    {
        var session = Session(true, HumanBattleMode.Instant);
        var events = new List<DomainEvent>();session.EventsPublished += e => events.AddRange(e);
        var result = session.Submit("end");
        Assert.Null(result.OpenBattleView);
        Assert.Equal("north", session.State.ActiveNationId);
        Assert.Single(events.OfType<AiTurnDecided>(), e => e.NationId == "south");
    }

    [Fact]
    public void Retained_passes_reproduce_the_same_final_state_and_event_order()
    {
        (GameState State, string[] Events) Play()
        {
            var session = Session(true, HumanBattleMode.Tactical);
            var events = new List<DomainEvent>();session.EventsPublished += e => events.AddRange(e);
            session.Submit("end");session.Submit("battle general on");
            return (session.State, events.Select(e => e.ToString()).ToArray());
        }
        var first = Play();var second = Play();
        Assert.Equal(first.State, second.State);Assert.Equal(first.Events, second.Events);
    }
}
