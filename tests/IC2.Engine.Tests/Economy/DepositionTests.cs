using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T39 Quarterly upkeep: who pays, mercenary desertion, and deposition
/// for debt", Done-when 6: the debt test's three arms, and the deposition effects shared by the AI and
/// human paths.
/// </summary>
public sealed class DepositionTests
{
    private static NationState BaseNation(int treasury, int unity, int wealth) =>
        EconomyTestbed.InitialState().NationById("north")! with
        {
            Treasury = treasury,
            Unity = unity,
            Wealth = wealth,
        };

    // ---- Done-when 6: the debt test's three arms, one each. ----

    [Fact]
    public void InDebt_TreasuryBelowNegativeWealthOverFiveHundred_IsTrue()
    {
        var ruleset = EconomyTestbed.Ruleset;
        // wealth 500,000 -> -(wealth/500) = -1,000; treasury -1,001 is below it.
        var nation = BaseNation(treasury: -1_001, unity: 990, wealth: 500_000);

        Assert.True(Deposition.InDebt(nation, ruleset));
    }

    [Fact]
    public void InDebt_TreasuryBelowTwentyThousandNegative_IsTrueRegardlessOfWealth()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var nation = BaseNation(treasury: -20_001, unity: 990, wealth: 0);

        Assert.True(Deposition.InDebt(nation, ruleset));
    }

    [Fact]
    public void InDebt_UnityBelowFourHundred_IsTrueRegardlessOfTreasury()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var nation = BaseNation(treasury: 1_000_000, unity: 399, wealth: 0);

        Assert.True(Deposition.InDebt(nation, ruleset));
    }

    [Fact]
    public void InDebt_NoneOfTheThreeArms_IsFalse()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var nation = BaseNation(treasury: 0, unity: 400, wealth: 0);

        Assert.False(Deposition.InDebt(nation, ruleset));
    }

    // ---- The effects, shared by both paths -- the report's own Gaul example. ----

    [Fact]
    public void ApplyEffects_Gaul_UnityFourSeventyToFiveFifty_TreasuryNegativeToZero()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var gaul = BaseNation(treasury: -2_658, unity: 470, wealth: 0);

        var after = Deposition.ApplyEffects(gaul, ruleset);

        Assert.Equal(550, after.Unity); // min(550, 470 + 150) = 550.
        Assert.Equal(0, after.Treasury); // negative -> floored to zero, not credited the flat 1,000.
    }

    [Fact]
    public void ApplyEffects_APositiveTreasury_GainsTheFlatOneThousand()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var nation = BaseNation(treasury: 500, unity: 300, wealth: 0);

        var after = Deposition.ApplyEffects(nation, ruleset);

        Assert.Equal(1_500, after.Treasury); // 500 + 1,000.
    }

    [Fact]
    public void ApplyEffects_UnityNeverDecreases_EvenWellAboveTheCeiling()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var nation = BaseNation(treasury: 0, unity: 600, wealth: 0); // already above the 550 ceiling.

        var after = Deposition.ApplyEffects(nation, ruleset);

        Assert.Equal(600, after.Unity); // max(600, min(550, 600+150)) = max(600, 550) = 600, never lowered.
    }

    // ---- The relation reset: -5 .. -1 -> 0, everything else untouched. ----

    [Fact]
    public void ResetRelations_MinusFiveToMinusOne_ResetToZero_EverythingElseUntouched()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var state = EconomyTestbed.InitialState();
        var relations = state.Relations
            .WithRelation("north", "south", -3);

        var after = Deposition.ResetRelations(relations, "north", ruleset);

        Assert.Equal(0, after.Get("north", "south"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-5)]
    public void ResetRelations_BoundaryValues_AreReset(int boundaryValue)
    {
        var ruleset = EconomyTestbed.Ruleset;
        var state = EconomyTestbed.InitialState();
        var relations = state.Relations.WithRelation("north", "south", boundaryValue);

        var after = Deposition.ResetRelations(relations, "north", ruleset);

        Assert.Equal(0, after.Get("north", "south"));
    }

    [Theory]
    [InlineData(-6)] // one below the threshold -- a deeper cooldown survives.
    [InlineData(0)] // already at peace -- untouched, not an error.
    [InlineData(2)] // alliance -- a positive relation is never reset by this rule.
    public void ResetRelations_OutsideTheRange_IsLeftUnchanged(int untouchedValue)
    {
        var ruleset = EconomyTestbed.Ruleset;
        var state = EconomyTestbed.InitialState();
        var relations = state.Relations.WithRelation("north", "south", untouchedValue);

        var after = Deposition.ResetRelations(relations, "north", ruleset);

        Assert.Equal(untouchedValue, after.Get("north", "south"));
    }
}
