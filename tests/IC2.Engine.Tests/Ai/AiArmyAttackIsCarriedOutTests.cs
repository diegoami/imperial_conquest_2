using IC2.Engine.Ai;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Battle.Commands;
using IC2.Engine.Tests.Core;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T156 (issue #925) Done-when 5: when the tree selects <c>AttackArmy</c> for an AI army, the AI
/// issues the attack (or the march toward it) and the engine accepts it.
/// </summary>
public sealed class AiArmyAttackIsCarriedOutTests
{
    [Fact]
    public void When_the_tree_picks_attack_army_the_AI_issues_an_attack_or_a_march_toward_the_target()
    {
        var toy = CoreTestbed.Toy;
        var ruleset = AiScriptedStates.Ruleset;

        // Strong attacker AI army at (10,10) with 50000 heavy_infantry, morale 60.
        // Weaker enemy army at (11,10) adjacent, with 100 light_infantry, morale 1.
        var nation = new NationDefinition(
            Id: "west", Name: "West", ColorHex: "#fff", LeaderName: "L", CapitalCityId: "west-cap",
            Treasury: 0, Unity: 600, Wealth: 0, TaxBase: 0, TaxRatePercent: 15,
            MobilizedPercent: 0, Population: 100);
        var enemy = new NationDefinition(
            Id: "east", Name: "East", ColorHex: "#fff", LeaderName: "L", CapitalCityId: "east-cap",
            Treasury: 0, Unity: 600, Wealth: 0, TaxBase: 0, TaxRatePercent: 15,
            MobilizedPercent: 0, Population: 100);

        var customWorld = toy.World with { Nations = ValueList.Of(nation, enemy) };
        var scenario = new Scenario(
            SchemaVersion: 1, Id: "scenario-attack", Name: "Attack",
            WorldId: customWorld.Id, RulesetId: ruleset.Id,
            Seats: ValueList.Of(new Seat("west", SeatControl.Ai, null), new Seat("east", SeatControl.Ai, null)),
            Victory: new VictoryCondition(VictoryConditionType.TotalConquest),
            TurnLimit: null, BlindHotseat: false, RandomSeed: 1UL);

        var state = GameStateFactory.CreateInitial(customWorld, ruleset, scenario) with { RandomSeed = 1UL };
        state = state with
        {
            TurnOrder = ValueList<string>.Of("west", "east"),
            Cities = ValueList.Of(
                CaptureFixtures.City("west-cap", "West", 0, 0, "west", "west", loyalty: 90, fortificationCode: 100, populationThousands: 100, maxPopulationThousands: 100, tribute: 0),
                CaptureFixtures.City("east-cap", "East", 20, 20, "east", "east", loyalty: 90, fortificationCode: 100, populationThousands: 100, maxPopulationThousands: 100, tribute: 0)),
            Armies = ValueList.Of(
                CaptureFixtures.Army("attacker", "west", 10, 10, 60,
                    CaptureFixtures.Unit("heavy_infantry", 50_000)) with { Moves = 9 },
                CaptureFixtures.Army("target", "east", 11, 10, 1,
                    CaptureFixtures.Unit("light_infantry", 100)) with { Moves = 5 }),
            ActiveSeatIndex = 0,
            // Reset Eliminated for both nations -- GameStateFactory sets it from world.Cities, which
            // doesn't include our custom cities. Then set the war.
            Nations = ValueList.From(state.Nations.Select(n => n with { Eliminated = false })),
            Relations = state.Relations.WithRelation("west", "east", ruleset.Diplomacy.StateCodes.War),
        };

        var driven = AiScriptedStates.DriveOneTurn(state, 1UL);

        Assert.Equal(0, driven.Outcome.CommandsRejected);
        Assert.Equal(0, driven.Outcome.ProjectionMismatches);

        // Some form of attack-army or movement toward the target was issued.
        var attackedOrMarched = driven.IssuedKinds.Any(k =>
            k == "battle.attack-army" || k == "movement.move-army");
        Assert.True(attackedOrMarched,
            $"expected at least one attack-army or move-army command, got: {string.Join(", ", driven.IssuedKinds)}");
    }
}