using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;
using CaptureFixtures = IC2.Engine.Tests.Cities.Capture.CaptureTestbed;
using Kind = IC2.Engine.Ai.AiArmyTargetTree.Kind;
using After = IC2.Engine.Ai.AiArmyTargetTree.Continuation;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T156 (issue #925) Done-when 2: the decision tree of <c>2026-10-07-strategic-ai-turn.md</c> §3.3, walked
/// branch by branch and boundary by boundary over the scorers' results. Every row differs from its
/// neighbour in exactly one input, so changing the threshold or the comparison it names flips a row.
/// </summary>
public sealed class AiArmyTargetTreeDecisionTests
{
    // The base army: 9 moves, morale 70, 10 tons of supplies, 2,000 troops (2000/500 = 4 < 10, well supplied).
    private const int Moves = 9;

    private static AiWeightsRules Rules => AiScriptedStates.Ruleset.Ai;

    private static ArmyState ArmyWith(int moves, int morale, int supplies, int troops) =>
        CaptureFixtures.Army("a", "north", 5, 5, morale, CaptureFixtures.Unit("light_infantry", troops))
        with { Moves = moves, SupplyTons = supplies };

    private static AiArmyTargetTree.Decision Decide(
        long armyScore, int armyDist, long cityScore, int cityDist, bool hasCity,
        int supplies = 10, int morale = 70, int moves = Moves, int troops = 2_000)
    {
        var army = ArmyWith(moves, morale, supplies, troops);
        var enemyCity = CaptureFixtures.City("c", "c", 9, 9, "south", "south", 90, 100, 100, 100, 0);
        var enemyArmy = CaptureFixtures.Army("e", "south", 7, 7, 60, CaptureFixtures.Unit("light_infantry", 100));
        var resupply = new AiArmyTargetTree.ResupplyCity(enemyCity, -3, 3);
        return AiArmyTargetTree.DecideFromScores(
            Rules,
            army,
            new AiArmyTargetTree.CityTarget(hasCity ? enemyCity : null, cityScore, cityDist),
            new AiArmyTargetTree.ArmyTarget(enemyArmy, armyScore, armyDist),
            resupply);
    }

    // ---------- attack the army, and the 100 boundary of armyScore < 100 ----------

    [Theory]
    [InlineData(100L, Kind.AttackArmy)]
    [InlineData(99L, Kind.MoveToResupplyCity)]
    public void Army_score_below_100_leaves_the_army_target(long armyScore, Kind expected)
    {
        // City score 50 (< 100) and a thin pack (4 troops-per-500 not below 4 tons): resupply when the army target is dropped.
        var decision = Decide(armyScore, 3, 50, 3, hasCity: true, supplies: 4);

        Assert.Equal(expected, decision.Selected);
    }

    // ---------- the city is the better buy: cityScore > 100, cityDist < moves, armyDist > 2 x moves ----------

    [Theory]
    [InlineData(101L, 3, 25, Kind.AttackCity)]
    [InlineData(100L, 3, 25, Kind.AttackArmy)]
    [InlineData(101L, Moves - 1, 25, Kind.AttackCity)]
    [InlineData(101L, Moves, 25, Kind.AttackArmy)]
    [InlineData(101L, 3, (2 * Moves) + 1, Kind.AttackCity)]
    [InlineData(101L, 3, 2 * Moves, Kind.AttackArmy)]
    public void City_wins_over_a_good_army_target_only_when_clearly_the_better_buy(
        long cityScore, int cityDist, int armyDist, Kind expected)
    {
        var decision = Decide(500, armyDist, cityScore, cityDist, hasCity: true);

        Assert.Equal(expected, decision.Selected);
    }

    // ---------- demoralised: supplies < 1 and morale < 60 and armyDist > 8 ----------

    [Theory]
    [InlineData(0, 59, 9, Kind.AttackCity)]
    [InlineData(1, 59, 9, Kind.AttackArmy)]
    [InlineData(0, 60, 9, Kind.AttackArmy)]
    [InlineData(0, 59, 8, Kind.AttackArmy)]
    public void A_demoralised_army_out_of_reach_of_its_target_takes_the_city_instead(
        int supplies, int morale, int armyDist, Kind expected)
    {
        // City score 150 within reach (cityDist 3 < 9 moves) but armyDist 9 is not above 2 x 9, so the
        // better-buy clause is off and only the demoralised clause can leave the army target.
        var decision = Decide(500, armyDist, 150, 3, hasCity: true, supplies: supplies, morale: morale);

        Assert.Equal(expected, decision.Selected);
    }

    // ---------- attack the city versus the resupply branch: cityScore < 100, or supplies < 1 and cityDist > 19 ----------

    [Theory]
    [InlineData(100L, Kind.AttackCity)]
    [InlineData(99L, Kind.MoveToResupplyCity)]
    public void City_score_below_100_goes_to_the_resupply_branch_when_the_army_is_not_well_supplied(long cityScore, Kind expected)
    {
        // armyScore 50 drops the army target; supplies 4 against 2000/500 = 4 is not well supplied.
        var decision = Decide(50, 3, cityScore, 3, hasCity: true, supplies: 4);

        Assert.Equal(expected, decision.Selected);
    }

    [Theory]
    [InlineData(19, Kind.AttackCity)]
    [InlineData(20, Kind.MoveToResupplyCity)]
    public void A_city_beyond_19_with_no_supplies_is_not_attacked(int cityDist, Kind expected)
    {
        // supplies 0, morale 59, armyDist 9: the demoralised clause is on; the city score is 150 either way.
        var decision = Decide(500, 9, 150, cityDist, hasCity: true, supplies: 0, morale: 59);

        Assert.Equal(expected, decision.Selected);
    }

    // ---------- the mercenary run: troops / 500 < supplies and a city target exists ----------

    [Theory]
    [InlineData(5, true, Kind.MercenaryRun)]
    [InlineData(4, true, Kind.MoveToResupplyCity)]
    [InlineData(5, false, Kind.MoveToResupplyCity)]
    public void The_mercenary_run_needs_supplies_above_troops_over_500_and_a_city_target(int supplies, bool hasCity, Kind expected)
    {
        // 2000 / 500 = 4 < supplies only for supplies 5.
        var decision = Decide(50, 3, 50, 3, hasCity, supplies: supplies);

        Assert.Equal(expected, decision.Selected);
    }

    // ---------- the mercenary run's two continuations: armyScore < 71 and cityScore > 85, or armyScore >= 71 ----------

    [Theory]
    [InlineData(70L, 86L, After.DefendResupplyCity)]
    [InlineData(70L, 85L, After.None)]
    [InlineData(71L, 85L, After.ChaseArmy)]
    [InlineData(71L, 86L, After.ChaseArmy)]
    [InlineData(99L, 50L, After.ChaseArmy)]
    [InlineData(0L, 99L, After.DefendResupplyCity)]
    public void The_mercenary_run_is_followed_by_defending_the_resupply_city_or_chasing_the_army(
        long armyScore, long cityScore, After expected)
    {
        var decision = Decide(armyScore, 3, cityScore, 3, hasCity: true, supplies: 10);

        Assert.Equal(Kind.MercenaryRun, decision.Selected);
        Assert.Equal(expected, decision.After);
    }

    [Fact]
    public void Only_the_mercenary_run_carries_a_continuation()
    {
        Assert.Equal(After.None, Decide(500, 3, 50, 3, hasCity: true).After);
        Assert.Equal(After.None, Decide(50, 3, 50, 3, hasCity: true, supplies: 4).After);
    }
}
