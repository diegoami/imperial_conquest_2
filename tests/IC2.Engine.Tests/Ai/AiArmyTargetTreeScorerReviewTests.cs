using IC2.Engine.Ai;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Ai;

public sealed partial class AiArmyTargetTreeScorerTests
{
    private static AiView BlockedFaithfulResupply(ArmyState army, params CityState[] alternatives)
    {
        var island = City("us-capital", Us, 12, 10, supply: 500);
        var world = WorldWithSea((11, 9), (12, 9), (13, 9), (11, 10), (13, 10),
            (11, 11), (12, 11), (13, 11));
        var view = ViewIn(world, new[] { island }.Concat(alternatives), [army]);
        var rules = view.Ruleset with
        {
            Flags = view.Ruleset.Flags with { AiEconomy = AiEconomyModel.Original },
            Ai = view.Ruleset.Ai with { ResupplyStrengthTroopsDivisor = 1_000 },
        };
        return new AiView(view.State, rules, world, Us);
    }

    [Fact]
    public void Faithful_reachable_resupply_skips_empty_town_and_uses_stock_above_threshold()
    {
        var army = Army("a", Us, 10, 10, 29_800) with { Money = 1_000 };
        var view = BlockedFaithfulResupply(army,
            City("empty", Us, 10, 14, supply: 0),
            City("stocked", Us, 10, 16, supply: 30));

        Assert.Equal("us-capital", AiArmyTargetTree.ScoreResupplyCity(view, army, false).City!.Id);
        var resolved = AiArmyTargetTree.ResolveResupplyCity(view, army, false);
        Assert.Equal("stocked", resolved.City!.Id);
        Assert.Equal(-6, resolved.Score);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    public void Faithful_reachable_resupply_has_no_candidate_at_or_below_stock_threshold(int stock)
    {
        var army = Army("a", Us, 10, 10, 29_800);
        var view = BlockedFaithfulResupply(army, City("too-thin", Us, 10, 14, supply: stock));
        Assert.Null(AiArmyTargetTree.ResolveResupplyCity(view, army, false).City);
    }

    [Fact]
    public void Faithful_reachable_resupply_retains_capital_preference()
    {
        var army = Army("a", Us, 10, 10, 29_800);
        var view = BlockedFaithfulResupply(army,
            City("regular", Us, 10, 14, supply: 30),
            City("third-capital", Us, 10, 18, supply: 30));
        var resolved = AiArmyTargetTree.ResolveResupplyCity(view, army, false);
        Assert.Equal("third-capital", resolved.City!.Id);
        Assert.Equal(12, resolved.Score);
    }

    [Fact]
    public void Faithful_reachable_resupply_enforces_foreign_adjusted_distance_gate()
    {
        var army = Army("a", Us, 10, 10, 29_800) with { Money = 1_000 };
        var view = BlockedFaithfulResupply(army, City("foreign", Third, 10, 6, supply: 1_000));
        Assert.Null(AiArmyTargetTree.ResolveResupplyCity(view, army, true).City);
    }
}
