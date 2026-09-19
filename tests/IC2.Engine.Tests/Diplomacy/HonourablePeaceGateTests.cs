using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// <c>docs/task-catalogue.md</c> T19 DoD 8: the honourable-peace branch fires when the victor is weaker
/// on population × unity, or on total army strength.
/// </summary>
public sealed class HonourablePeaceGateTests
{
    private const string Winner = "winner-nation";
    private const string Loser = "loser-nation";

    [Fact]
    public void DoD08_Fires_WhenTheVictorScoresLowerOnWealthTimesUnity()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Winner, "Winner", unity: 300, wealth: 1000),
            DiplomacyTestbed.Nation(Loser, "Loser", unity: 900, wealth: 3000));

        // Both armies equal, so only the score term can decide this case.
        var army = DiplomacyTestbed.Unit("light_infantry", 5000, 5);
        state = state with
        {
            Armies = ValueList.Of(
                DiplomacyTestbed.Army("winner-army", Winner, 60, army),
                DiplomacyTestbed.Army("loser-army", Loser, 60, army)),
        };

        Assert.True(HonourablePeaceGate.Fires(state, ruleset, Winner, Loser));
    }

    [Fact]
    public void DoD08_Fires_WhenTheVictorsArmyIsWeaker_EvenWithTheHigherScore()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Winner, "Winner", unity: 900, wealth: 9000),
            DiplomacyTestbed.Nation(Loser, "Loser", unity: 100, wealth: 100));

        state = state with
        {
            Armies = ValueList.Of(
                DiplomacyTestbed.Army("winner-army", Winner, 60, DiplomacyTestbed.Unit("light_infantry", 100, 5)),
                DiplomacyTestbed.Army("loser-army", Loser, 60, DiplomacyTestbed.Unit("light_infantry", 90000, 5))),
        };

        Assert.True(HonourablePeaceGate.Fires(state, ruleset, Winner, Loser));
    }

    [Fact]
    public void DoD08_DoesNotFire_WhenTheVictorIsStrongerOnBoth()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Winner, "Winner", unity: 900, wealth: 9000),
            DiplomacyTestbed.Nation(Loser, "Loser", unity: 100, wealth: 100));

        state = state with
        {
            Armies = ValueList.Of(
                DiplomacyTestbed.Army("winner-army", Winner, 60, DiplomacyTestbed.Unit("light_infantry", 90000, 5)),
                DiplomacyTestbed.Army("loser-army", Loser, 60, DiplomacyTestbed.Unit("light_infantry", 100, 5))),
        };

        Assert.False(HonourablePeaceGate.Fires(state, ruleset, Winner, Loser));
    }

    /// <summary>
    /// A nation with no armies at all still compares correctly (total army power 0, no divide-by-zero,
    /// no null reference) -- exactly the state a battle's fully-annihilated loser leaves behind.
    /// </summary>
    [Fact]
    public void DoD08_ANationWithNoArmies_ComparesAsZeroArmyPower()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var state = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Winner, "Winner", unity: 900, wealth: 9000),
            DiplomacyTestbed.Nation(Loser, "Loser", unity: 100, wealth: 100));

        // Winner destroyed the loser's whole army; winner's own army survives.
        state = state with
        {
            Armies = ValueList.Of(
                DiplomacyTestbed.Army("winner-army", Winner, 60, DiplomacyTestbed.Unit("light_infantry", 1, 5))),
        };

        Assert.False(HonourablePeaceGate.Fires(state, ruleset, Winner, Loser));
    }

    /// <summary>
    /// Mutation proof: if the score comparison read the wrong field (say, <see cref="NationState.Treasury"/>
    /// instead of <see cref="NationState.Wealth"/>), this test -- which varies only Wealth, holding Unity
    /// and every army equal -- would flip.
    /// </summary>
    [Fact]
    public void DoD08_MutationProof_TheScoreTermReadsWealth()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var army = DiplomacyTestbed.Unit("light_infantry", 5000, 5);

        var lowerWealthWins = DiplomacyTestbed.StateOf(
            DiplomacyTestbed.Nation(Winner, "Winner", unity: 500, wealth: 100, taxBase: 0),
            DiplomacyTestbed.Nation(Loser, "Loser", unity: 500, wealth: 900, taxBase: 0)) with
        {
            Armies = ValueList.Of(
                DiplomacyTestbed.Army("w", Winner, 60, army), DiplomacyTestbed.Army("l", Loser, 60, army)),
        };

        Assert.True(HonourablePeaceGate.Fires(lowerWealthWins, ruleset, Winner, Loser));
    }
}
