using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Battle.Commands;
using Xunit;
using Fx = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;

namespace IC2.Engine.Tests.Ai;

public sealed class OriginalWordScoreTests
{
    // Independent expected scalar rows from promoted scorer130 controls-v1, not production-derived
    // expectations. Distances outside the original world are diagnostic arithmetic inputs only.
    public static TheoryData<string, int[], int[], int, int, int> Controls => new()
    {
        { "city-ordinary", [65, 652], [10, 4], 2, 656, 4 },
        { "city-32767", [32767], [10], 1, -32759, 10 },
        { "city-32768", [32768], [10], 1, -32758, 10 },
        { "city-65535", [65535], [10], 1, 9, 10 },
        { "city-65536", [65536], [10], 1, 10, 10 },
        { "city-final-no-carry", [32757], [10], 1, 32767, 10 },
        { "city-final-carry", [32757], [11], 1, -32768, 11 },
        { "city-negative-halving", [-4], [7], 1, 3, 7 },
        { "city-below-sentinel", [-10001], [10001], -1, -10000, 0 },
        { "city-just-above-sentinel", [-9999], [9999], 1, 0, 9999 },
        { "city-two-truncated-first", [40000, 4391], [10, 10], 2, 4401, 10 },
        { "city-two-truncated-second", [30000, 41253], [10, 10], 2, -24273, 10 },
        { "city-tie-after-truncation", [65636, 100], [10, 10], 1, 110, 10 },
        { "city-distance-upper-bits", [2194], [65539], 1, 2197, 3 },
        { "army-ordinary", [65, 1326], [10, 4], 2, 1330, 4 },
        { "army-cap", [1000], [10], 1, 1010, 10 },
        { "army-cap-plus-1000", [2000], [3], 1, 2003, 3 },
        { "army-negative-halving", [-4], [7], 1, 3, 7 },
        // Original leaves an uninitialised index word untouched. Native no-target remains null/-1.
        { "army-no-target", [-10001], [10001], -1, -10000, 0 },
        { "army-two-capped-tie", [1000, 1000], [10, 20], 1, 1010, 10 },
        { "army-negative-distance-word", [1000], [40000], 1, -24536, -25536 },
        { "army-final-carry-diagnostic", [1000], [32000], 1, -32536, 32000 },
    };

    [Theory]
    [MemberData(nameof(Controls))]
    public void Promoted_word_controls_match(string name, int[] scores, int[] distances,
        int expectedIndex, int expectedScore, int expectedDistance)
    {
        Assert.False(string.IsNullOrWhiteSpace(name));
        var stored = OriginalWordScore.Empty;
        var winner = -1;
        for (var i = 0; i < scores.Length; i++)
            if (stored.IsBeatenBy(scores[i]))
            {
                stored = stored.WithWinner(scores[i], distances[i]);
                winner = i + 1;
            }
        Assert.Equal(expectedIndex, winner);
        Assert.Equal(expectedScore, stored.ReturnedScore);
        Assert.Equal(expectedDistance, (int)stored.Distance);
    }

    private static Ruleset Rules(bool original) => AiScriptedStates.Ruleset with
    {
        Flags = AiScriptedStates.Ruleset.Flags with { AiEconomy = original ? AiEconomyModel.Original : AiEconomyModel.Designed },
    };

    private static ArmyState Actor => AiReachableFallbackTests.Army(10, 10, moves: 5, troops: 30_000) with { Morale = 80 };
    private static CityState City(string id, int x, int y, int population = 1) =>
        Fx.City(id, id, x, y, "south", "south", 0, 0, population, population, 0);

    private static AiView View(bool original, IEnumerable<CityState> cities, params ArmyState[] enemies)
    {
        var state = BattleCommandTestbed.StateWith(
            [Fx.Nation("north", capitalCityId: "not-a-city"), Fx.Nation("south", capitalCityId: "not-a-city-either")],
            cities, new[] { Actor }.Concat(enemies));
        state = AiScriptedStates.WithActiveSeat(state, "north");
        state = state with { Relations = state.Relations.WithRelation("north", "south", Rules(original).Diplomacy.StateCodes.War) };
        return new AiView(state, Rules(original), AiReachableFallbackTests.Terrain(), "north");
    }

    [Fact]
    public void Production_city_scorer_compares_the_next_candidate_against_the_truncated_winner()
    {
        var cities = new[] { City("z-first", 12, 10), City("a-second", 13, 10, 2) };
        var original = AiArmyTargetTree.ScoreCityTarget(View(true, cities), Actor);
        var designed = AiArmyTargetTree.ScoreCityTarget(View(false, cities), Actor);
        // A=30000. First: (16500-2)*2 =32996 -> -32540. Second: (8250-3)*2=16494 wins.
        Assert.Equal("a-second", original.City!.Id);
        Assert.Equal(16497, original.Score);
        Assert.Equal(3, original.Distance);
        Assert.Equal("z-first", designed.City!.Id);
        Assert.Equal(32998, designed.Score);
    }

    [Fact]
    public void The_real_tree_receives_the_signed_wrapped_city_return()
    {
        var view = View(true, [City("weak", 12, 10)]);
        var before = view.State;
        var score = AiArmyTargetTree.ScoreCityTarget(view, Actor);
        Assert.Equal(-32538, score.Score);
        var decision = AiArmyTargetTree.DecideUnlogged(view, Actor, atWar: true, out var record);
        Assert.Equal(-32538, record.CityScore);
        Assert.NotEqual(AiArmyTargetTree.Kind.AttackCity, decision.Selected);
        Assert.Equal(before, view.State);
    }

    [Fact]
    public void City_ties_follow_table_order_only_under_the_original_policy()
    {
        var cities = new[] { City("z-first", 12, 10, 10), City("a-second", 10, 12, 10) };
        Assert.Equal("z-first", AiArmyTargetTree.ScoreCityTarget(View(true, cities), Actor).City!.Id);
        Assert.Equal("a-second", AiArmyTargetTree.ScoreCityTarget(View(false, cities), Actor).City!.Id);
        Assert.Equal("a-second", AiArmyTargetTree.ScoreCityTarget(View(true, cities.Reverse()), Actor).City!.Id);
    }

    [Fact]
    public void Empty_original_scorers_return_minus_10000_without_manufacturing_a_target()
    {
        var original = View(true, []);
        var city = AiArmyTargetTree.ScoreCityTarget(original, Actor);
        var army = AiArmyTargetTree.ScoreArmyTarget(original, Actor);
        Assert.Null(city.City);
        Assert.Null(army.Army);
        Assert.Equal((-10000L, 0), (city.Score, city.Distance));
        Assert.Equal((-10000L, 0), (army.Score, army.Distance));
        Assert.Equal(0, AiArmyTargetTree.ScoreCityTarget(View(false, []), Actor).Score);
        Assert.Equal(0, AiArmyTargetTree.ScoreArmyTarget(View(false, []), Actor).Score);
    }

    [Fact]
    public void Army_cap_and_zero_divisor_safeguards_remain_in_the_production_scorers()
    {
        var weak = Actor with { Id = "weak", Nation = "south", X = 13, Units = ValueList.Of(Fx.Unit("light_infantry", 1_000)) };
        var view = View(true, [], weak);
        var target = AiArmyTargetTree.ScoreArmyTarget(view, Actor);
        Assert.Equal(weak.Id, target.Army!.Id);
        Assert.Equal(2003, target.Score);
        weak = weak with { Units = ValueList<UnitSlot>.Empty };
        target = AiArmyTargetTree.ScoreArmyTarget(View(true, [], weak), Actor);
        Assert.Null(target.Army);
        Assert.Equal(-10000, target.Score);
        var zeroDefense = City("empty", 12, 10, 0);
        Assert.Null(AiArmyTargetTree.ScoreCityTarget(View(true, [zeroDefense]), Actor).City);
    }
}
