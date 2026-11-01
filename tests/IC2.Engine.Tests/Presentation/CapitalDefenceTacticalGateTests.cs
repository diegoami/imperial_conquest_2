using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

public sealed class CapitalDefenceTacticalGateTests
{
    [Fact]
    public void A_capital_defence_attack_retains_the_partial_pass_then_restarts_the_same_AI_seat()
    {
        var toy = TacticalSessionTestbed.Toy;
        var world = toy.World with
        {
            Id = "classical-mediterranean", Width = 320, Height = 140,
            Terrain = new(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(2, 320 * 140))),
            StartingArmies = ValueList.From(TacticalSessionTestbed.WeakAttacker()),
            StartingFleets = ValueList<StartingFleet>.Empty,
        };
        var rules = toy.Ruleset with { Flags = toy.Ruleset.Flags with { AiEconomy = AiEconomyModel.Original } };
        var scenario = toy.Scenario with { WorldId = world.Id };
        var session = new GameSession(world, rules, scenario) { HumanBattles = HumanBattleMode.Tactical };
        Assert.Contains(session.Submit("declare-war south").Lines, line => line.Contains("accepted", StringComparison.Ordinal));
        var events = new List<DomainEvent>();
        session.EventsPublished += batch => events.AddRange(batch);

        var opened = session.Submit("end");
        Assert.True(opened.OpenBattleView is not null, string.Join("\n", opened.Lines.Concat(
            events.OfType<AiTurnDecided>().SelectMany(e => e.Lines))));
        Assert.Equal(("south-army-1", "north-army-1"),
            (opened.OpenBattleView!.AttackerArmyId, opened.OpenBattleView.DefenderArmyId));
        Assert.Equal("south", session.State.ActiveNationId);
        var closed = session.Submit("battle general on");
        Assert.Null(closed.OpenBattleView);
        Assert.NotNull(closed.EndedBattleReport);
        Assert.Equal("north", session.State.ActiveNationId);
        var turns = events.OfType<AiTurnDecided>().Where(e => e.NationId == "south").ToArray();
        Assert.Equal(2, turns.Length);
        Assert.Contains(turns[0].Lines, line => line.StartsWith("capital defence attack:", StringComparison.Ordinal));
        Assert.All(turns, turn => Assert.Equal(0, turn.CommandsRejected));
    }

    [Fact]
    public void Capital_defence_retains_its_fleet_pass_before_support_and_restarts_without_replaying_it()
    {
        var toy = TacticalSessionTestbed.Toy;
        var cells = new int[320 * 140];
        for (var y = 0; y < 140; y++)
            for (var x = 0; x <= 100; x++) cells[y * 320 + x] = 2;
        var world = toy.World with
        {
            Id = "classical-mediterranean", Width = 320, Height = 140,
            Terrain = new(TerrainEncoding.RunLength, ValueList.From(cells.Select(c => new TerrainRun(c, 1)))),
            Cities = ValueList.From(toy.World.Cities.Where(c => c.Id is "arx" or "meridia").Select(c => c with
            {
                X = c.Owner == "south" ? 100 : 90, Y = 40, FortificationCode = 100,
            })),
            // Keep each responder too large for the initial consolidation pass to merge them.
            StartingArmies = ValueList.Of(
                TacticalSessionTestbed.Army("north-threat", "north", 99, 39, 68,
                    TacticalSessionTestbed.Unit("light_infantry", 400)),
                TacticalSessionTestbed.Army("south-defender", "south", 100, 39, 59,
                    TacticalSessionTestbed.Unit("heavy_infantry", 25000)),
                TacticalSessionTestbed.Army("south-waiting", "south", 99, 36, 59,
                    TacticalSessionTestbed.Unit("heavy_infantry", 25000)) with { Money = 77, SupplyTons = 0 }),
            StartingFleets = ValueList.Of(new StartingFleet("south-fleet", "south", 105, 40, 90, 100, 0, 720, 26)),
        };
        var rules = toy.Ruleset with { Flags = toy.Ruleset.Flags with { AiEconomy = AiEconomyModel.Original } };
        var session = new GameSession(world, rules, toy.Scenario with { WorldId = world.Id })
            { HumanBattles = HumanBattleMode.Tactical };
        Assert.Contains(session.Submit("declare-war south").Lines, line => line.Contains("accepted", StringComparison.Ordinal));
        var before = session.State;
        var events = new List<DomainEvent>();
        session.EventsPublished += batch => events.AddRange(batch);

        var opened = session.Submit("end");
        Assert.NotNull(opened.OpenBattleView);
        Assert.Equal(("south-defender", "north-threat"),
            (opened.OpenBattleView!.AttackerArmyId, opened.OpenBattleView.DefenderArmyId));
        Assert.Equal("south", session.State.ActiveNationId);
        Assert.Equal(before.Calendar, session.State.Calendar);
        var pass = Assert.Single(events.OfType<AiTurnDecided>(), e => e.NationId == "south");
        Assert.Contains("army phase interrupted before support; retaining the fleet pass", pass.Lines);
        Assert.Equal(77, session.State.ArmyById("south-waiting")!.Money);
        var fleet = session.State.FleetById("south-fleet")!;
        Assert.NotEqual((105, 40), (fleet.X, fleet.Y));
        Assert.InRange(fleet.Moves, 1, 25);
        Assert.Single(events.OfType<OriginalFleetDecision>(), e => e.FleetId == "south-fleet");
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
    }

}
