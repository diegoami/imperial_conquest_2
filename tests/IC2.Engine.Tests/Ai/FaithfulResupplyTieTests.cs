using IC2.Engine.Ai;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Ai;

public sealed partial class AiArmyTargetTreeScorerTests
{
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    public void Resupply_ties_use_table_order_only_for_original_policy(bool original, bool reverse, bool fallback)
    {
        var army = Supplicant();
        // Equal distances; neither is a capital. Stock selects the primary or fallback path.
        var cities = new[] {
            City("z-first", Us, 13, 10, supply: fallback ? 0 : 20),
            City("a-second", Us, 10, 13, supply: fallback ? 0 : 20),
        };
        if (reverse) Array.Reverse(cities);
        var view = FaithfulResupplyView(cities, army);
        if (!original)
            view = new AiView(view.State, view.Ruleset with {
                Flags = view.Ruleset.Flags with { AiEconomy = AiEconomyModel.Designed },
            }, view.World, Us);
        var before = view.State;

        var result = AiArmyTargetTree.ScoreResupplyCity(view, army, atWar: false);

        Assert.Equal(original ? cities[0].Id : "a-second", result.City!.Id);
        Assert.Equal(-3, result.Score);
        Assert.Equal(3, result.Distance);
        Assert.Same(before, view.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Faithful_resupply_unequal_distance_overrides_record_order(bool fallback)
    {
        var army = Supplicant();
        var view = FaithfulResupplyView([
            City("a-far", Us, 16, 10, supply: fallback ? 0 : 20),
            City("z-near", Us, 13, 10, supply: fallback ? 0 : 20),
        ], army);
        Assert.Equal("z-near", AiArmyTargetTree.ScoreResupplyCity(view, army, false).City!.Id);
    }

    [Fact]
    public void Faithful_resupply_ties_compare_adjusted_score_not_raw_distance()
    {
        var army = Supplicant();
        var view = FaithfulResupplyView([
            City("z-regular", Us, 13, 10, supply: 20),
            City("us-capital", Us, 33, 10, supply: 20),
        ], army);
        var result = AiArmyTargetTree.ScoreResupplyCity(view, army, false);
        Assert.Equal("z-regular", result.City!.Id); // -3 == -23 + 20.
        var reversed = new AiView(view.State with { Cities = ValueList.From(view.State.Cities.Reverse()) },
            view.Ruleset, view.World, Us);
        Assert.Equal("us-capital", AiArmyTargetTree.ScoreResupplyCity(reversed, army, false).City!.Id);
    }
}
