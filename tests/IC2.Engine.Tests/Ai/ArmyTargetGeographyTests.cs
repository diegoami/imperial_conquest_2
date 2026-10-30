using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Import;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Presentation;
using IC2.Engine.Strength;
using IC2.Engine.Tests.Battle.Commands;
using IC2.Engine.Tests.Import;
using Xunit;
using Fx = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

public sealed class ArmyTargetGeographyTests
{
    [Theory]
    [InlineData("region", "faithful", 60, 55, 88, 44, false, 77)]
    [InlineData("side", "faithful", 93, 79, 34, 63, false, 93)]
    [InlineData("same", "faithful", 230, 80, 240, 90, true, 126)]
    [InlineData("region", "designed", 60, 55, 88, 44, false, 126)]
    [InlineData("side", "designed", 93, 79, 34, 63, false, 126)]
    [InlineData("same", "designed", 230, 80, 240, 90, true, 68)]
    [InlineData("side", "custom-world", 93, 79, 34, 63, false, 126)]
    [InlineData("same", "custom-world", 230, 80, 240, 90, true, 68)]
    [InlineData("region", "wrong-size", 60, 55, 88, 44, false, 126)]
    public void Army_halving_uses_coordinate_region_and_side_only_inside_the_faithful_classical_gate(
        string geography, string mode, int ax, int ay, int tx, int ty, bool differentCityRegions, int expected)
    {
        var original = mode != "designed";
        var width = mode == "wrong-size" ? 321 : 320;
        var world = AiScriptedStates.World with
        {
            Id = mode == "custom-world" ? "synthetic-custom" : "classical-mediterranean",
            Width = width, Height = 140,
            Terrain = new(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(2, width * 140))),
        };
        var rules = AiScriptedStates.Ruleset with
        {
            Flags = AiScriptedStates.Ruleset.Flags with
            {
                AiEconomy = original ? AiEconomyModel.Original : AiEconomyModel.Designed,
            },
            Ai = AiScriptedStates.Ruleset.Ai with
            {
                CityRegionById = ValueList.Of(new AiCityRegionAssignment("home", 0),
                    new AiCityRegionAssignment("landmark", differentCityRegions ? 1 : 0)),
            },
        };
        var actor = Fx.Army("actor", "north", ax, ay, 69, Fx.Unit("light_infantry", 8000)) with
            { Moves = 8, SupplyTons = 40 };
        var enemy = Fx.Army("enemy", "south", tx, ty, 60, Fx.Unit("light_infantry", 8000));
        var state = BattleCommandTestbed.StateWith([Fx.Nation("north"), Fx.Nation("south")],
            [AiReachableFallbackTests.City("home", "north", ax, ay - 1),
             AiReachableFallbackTests.City("landmark", "north", tx, ty + 1)], [actor, enemy],
            // CAB4 accepts any own fleet, even one under construction. It has no on-map marker.
            [new FleetState("building-ferry", "north", ax, ay - 1, 0, 30, 100, 100, 100, 1, "home", null, null)]);
        state = state with { Relations = state.Relations.WithRelation("north", "south", rules.Diplomacy.StateCodes.War) };
        var view = new AiView(state, rules, world, "north");
        Assert.Equal(mode == "faithful", ClassicalArmyNavigation.Applies(view));
        var from = ClassicalArmyNavigation.Classify(ax, ay);
        var to = ClassicalArmyNavigation.Classify(tx, ty);
        if (geography == "region") Assert.NotEqual(from.Region, to.Region);
        else if (geography == "side") { Assert.Equal(from.Region, to.Region); Assert.NotEqual(from.Side, to.Side); }
        else Assert.Equal(from, to);
        Assert.Equal(126, rules.Ai.ArmyTargetStrengthNumerator * ArmyPower.Compute(actor.Units, actor.Morale, rules)
            / ArmyPower.Compute(enemy.Units, enemy.Morale, rules));

        var result = AiArmyTargetTree.ScoreArmyTarget(view, actor);
        Assert.Equal(enemy.Id, result.Army?.Id);
        Assert.Equal(expected, result.Score);
        Assert.Equal(AiView.Distance(ax, ay, tx, ty), result.Distance);
        if (geography == "side")
        {
            // The model/native city score91 is a controlled input here, not an original hooked score.
            var city = AiReachableFallbackTests.City("model-city", "south", 49, 41);
            var decision = AiArmyTargetTree.DecideFromScores(rules.Ai, actor,
                new(city, 91, 44), result, new(null, 0, 0), ClassicalArmyNavigation.Applies(view));
            Assert.Equal(mode == "faithful" ? AiArmyTargetTree.Kind.MercenaryRun : AiArmyTargetTree.Kind.AttackArmy,
                decision.Selected);
            if (decision.Selected == AiArmyTargetTree.Kind.MercenaryRun)
            {
                var destination = AiReachableFallbackTests.City("offer", "north", 111, 69);
                var withOffer = new AiView(state with
                {
                    Cities = ValueList.From(state.Cities.Append(destination)),
                    MercenaryPool = ValueList.Of(new MercenaryPoolSlot(0, 111, 69, 1, "light_infantry", 1000, 5)),
                }, rules, world, "north");
                Assert.Equal(destination, AiArmyTargetTree.MercenaryRunDestination(withOffer, actor));
                Assert.Equal(AiArmyTargetTree.Continuation.ChaseArmy, decision.After);
            }
        }
    }

    [SkippableFact]
    public void Published_BASE_control_selects_the_original_branch_score_and_requested_aim()
    {
        Skip.IfNot(LocalOriginalAssets.IsConfigured, LocalOriginalAssets.SkipReason);
        var path = OriginalFixture.TryResolve("BASE.SAV");
        Skip.If(path is null, "The published army3 BASE control is not configured.");
        var world = RealGameData.World;
        var rules = RealGameData.Ruleset;
        var scenario = RealGameData.Scenario;
        var imported = OriginalSaveImporter.Import(File.ReadAllBytes(path!), "BASE.SAV", world, rules, scenario,
            "army3-control", "army3-control").Save;
        var actor = imported.State.ArmyById("army-3");
        var target = imported.State.ArmyById("army-10");
        Skip.If(actor is not { X: 93, Y: 79, Nation: "carthage" } || target is not { X: 34, Y: 63 },
            "The configured BASE save is not the published army3 start context.");
        var decisions = AiArmyTargetTree.NewDecisionLog();
        try
        {
            var session = new GameSession(world, rules, scenario,
                imported with { State = imported.State with { RandomSeed = 12345 } }) { HumanBattles = HumanBattleMode.Instant };
            var events = new List<DomainEvent>();
            session.EventsPublished += batch => events.AddRange(batch);
            session.Submit("end");
            var decision = decisions.First(d => d.ArmyId == "army-3" && d.ArmyX == 93 && d.ArmyY == 79);
            Assert.Equal((AiArmyTargetTree.Kind.MercenaryRun, 93L, "army-10", 59),
                (decision.Kind, decision.ArmyScore, decision.TargetArmyId, decision.ArmyDistance));
            Assert.Contains(events.OfType<AiTurnDecided>().Where(e => e.NationId == "carthage").SelectMany(e => e.Lines),
                line => line.Contains("mercenary run (111, 69) with army-3", StringComparison.Ordinal));
            var move = Assert.Single(events.OfType<ArmyMoved>(), m => m.ArmyId == "army-3");
            Assert.Equal((93, 79, 88, 77, 8), (move.FromX, move.FromY, move.ToX, move.ToY, move.MovesSpent));
            Assert.Equal((88, 77), (session.State.ArmyById("army-3")!.X, session.State.ArmyById("army-3")!.Y));
            // End-of-round refill is separate from the hooked original walk's zero remaining moves.
        }
        finally { AiArmyTargetTree.ClearDecisionLog(); }
    }
}
