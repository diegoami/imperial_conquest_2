using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement.Commands;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using Fx = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

public sealed class MercenaryCityContinuationTests
{
    private static (AiView View, ArmyState Army) Setup(string mode, bool blockedCapital)
    {
        var cells = Enumerable.Repeat(2, 320 * 140).ToArray();
        if (blockedCapital)
            for (var x = 229; x <= 231; x++) cells[75 * 320 + x] = 0;
        var world = AiScriptedStates.World with
        {
            Id = mode == "custom" ? "custom-diagnostic" : "classical-mediterranean", Width = 320, Height = 140,
            Terrain = new(TerrainEncoding.RunLength, ValueList.From(cells.Select(c => new TerrainRun(c, 1)))),
        };
        var rules = AiScriptedStates.Ruleset with
        {
            Flags = AiScriptedStates.Ruleset.Flags with
            {
                AiEconomy = mode == "designed" ? AiEconomyModel.Designed : AiEconomyModel.Original,
            },
        };
        var army = AiReachableFallbackTests.Army(230, 80, 9, 40_000) with { SupplyTons = 1000 };
        var state = AiScriptedStates.WithActiveSeat(BattleCommandTestbed.StateWith(
            [Fx.Nation("north", treasury: 10_000, capitalCityId: "home"), Fx.Nation("south", capitalCityId: "other")],
            [AiReachableFallbackTests.City("home", "north", 230, 72, supply: 5000),
             AiReachableFallbackTests.City("resupply", "north", 235, 80, supply: 5000),
             Fx.City("target", "target", 230, 92, "south", "south", 90, 0, 100, 100, 0),
             AiReachableFallbackTests.City("other", "south", 300, 120)], [army]), "north");
        state = state with { Relations = state.Relations.WithRelation("north", "south", rules.Diplomacy.StateCodes.War),
            MercenaryPool = ValueList<MercenaryPoolSlot>.Empty };
        return (new(state, rules, world, "north"), army);
    }

    [Theory]
    [InlineData("faithful", false, 230, 92)]
    [InlineData("faithful", true, 230, 92)]
    [InlineData("designed", false, 235, 80)]
    [InlineData("designed", true, 235, 80)]
    [InlineData("custom", false, 230, 72)]
    [InlineData("custom", true, 235, 80)]
    public void Continuation_marches_to_the_enemy_city_only_in_faithful_classical_mode(
        string mode, bool blockedCapital, int x, int y)
    {
        var (view, army) = Setup(mode, blockedCapital);
        var scored = AiArmyTargetTree.ScoreCityTarget(view, army);
        Assert.Equal("target", scored.City?.Id);
        Assert.InRange(scored.Score, 86, 100);
        var decision = AiArmyTargetTree.Decide(view, army, atWar: true);
        Assert.Equal(AiArmyTargetTree.Kind.MercenaryRun, decision.Selected);

        var candidates = new List<AiCandidate>();
        AiMilitaryPhase.Propose(view, AiPersonalityProfile.For(view.Nation, view.Ruleset),
            SplitMix64Rng.ForStream(1, "continuation"), [], candidates);
        var move = Assert.Single(candidates.SelectMany(c => c.Commands).OfType<MoveArmyCommand>(), m => m.ArmyId == army.Id);
        Assert.Equal((x, y), (move.TargetX ?? move.X, move.TargetY ?? move.Y));
        var result = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), view.Ruleset, view.World, NullEventSink.Instance)
            .Dispatch(view.State, move);
        Assert.True(result.IsAccepted, result.ToString());
        if (mode == "faithful")
        {
            Assert.Equal((230, 89, 0), (result.State.ArmyById(army.Id)!.X,
                result.State.ArmyById(army.Id)!.Y, result.State.ArmyById(army.Id)!.Moves));
        }
    }

    [Fact]
    public void Faithful_continuation_does_not_add_a_second_march_after_an_existing_march()
    {
        var (view, army) = Setup("faithful", false);
        var candidates = new List<AiCandidate>();
        AiMilitaryPhase.Propose(view, AiPersonalityProfile.For(view.Nation, view.Ruleset),
            SplitMix64Rng.ForStream(1, "continuation"), [army.Id], candidates);
        Assert.DoesNotContain(candidates.SelectMany(c => c.Commands), c => c is MoveArmyCommand m && m.ArmyId == army.Id);
    }
}
