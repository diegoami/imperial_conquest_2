using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using Xunit;

namespace IC2.Engine.Tests.Presentation;

public sealed class CapitalDefenceTacticalGateTests
{
    [Fact]
    public void A_capital_defence_attack_opens_the_humans_battle_and_replays_the_same_AI_turn()
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
        var turn = Assert.Single(events.OfType<AiTurnDecided>(), e => e.NationId == "south");
        Assert.Contains(turn.Lines, line => line.StartsWith("capital defence attack:", StringComparison.Ordinal));
        Assert.Equal(0, turn.CommandsRejected);
    }
}
