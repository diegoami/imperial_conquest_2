using IC2.Engine.Economy;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T08 Economy, supply, and purses", Done-when 12: "Morale is
/// hard-clamped to 51…70 on every path that writes it, asserted by a property-style test over the rule
/// (a <c>+1</c> at 70 stays 70; a <c>−2</c> at 51 stays 51; a <c>−2</c> at 52 gives 51, not 50)."
/// </summary>
public sealed class SupplyMoraleClampTests
{
    [Fact]
    public void RegenAtCeiling_StaysAtCeiling()
    {
        var (morale, penalty) = SupplyMoraleRule.ApplyToMorale(currentMorale: 70, supplyPercent: 100, EconomyTestbed.Ruleset);
        Assert.Equal(70, morale);
        Assert.Equal(0, penalty);
    }

    [Fact]
    public void DecayAtFloor_StaysAtFloor()
    {
        var (morale, penalty) = SupplyMoraleRule.ApplyToMorale(currentMorale: 51, supplyPercent: 0, EconomyTestbed.Ruleset);
        Assert.Equal(51, morale);
        Assert.Equal(1, penalty);
    }

    [Fact]
    public void DecayOneAboveFloor_ClampsToFloorNotBelow()
    {
        // 52 - 2 = 50 unclamped; the confirmed floor forces 51, not 50.
        var (morale, penalty) = SupplyMoraleRule.ApplyToMorale(currentMorale: 52, supplyPercent: 0, EconomyTestbed.Ruleset);
        Assert.Equal(51, morale);
        Assert.Equal(1, penalty);
    }

    [Theory]
    [InlineData(51)]
    [InlineData(52)]
    [InlineData(60)]
    [InlineData(69)]
    [InlineData(70)]
    public void PropertyStyle_EveryStartingMoraleInRange_StaysInRangeAfterDecayOrRegen(int startingMorale)
    {
        var ruleset = EconomyTestbed.Ruleset;

        var (afterDecay, _) = SupplyMoraleRule.ApplyToMorale(startingMorale, supplyPercent: 0, ruleset);
        var (afterRegen, _) = SupplyMoraleRule.ApplyToMorale(startingMorale, supplyPercent: 100, ruleset);
        var (afterDeadBand, _) = SupplyMoraleRule.ApplyToMorale(startingMorale, supplyPercent: 12, ruleset);

        Assert.InRange(afterDecay, 51, 70);
        Assert.InRange(afterRegen, 51, 70);
        Assert.InRange(afterDeadBand, 51, 70);

        // The dead band (10 <= pct <= 15) is a genuine no-op, not merely "still in range".
        Assert.Equal(startingMorale, afterDeadBand);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(15)]
    [InlineData(16)]
    public void DeadBandBoundaries_10And15AreInertButTheNeighboursAreNot(int pct)
    {
        var (morale, penalty) = SupplyMoraleRule.ApplyToMorale(currentMorale: 60, supplyPercent: pct, EconomyTestbed.Ruleset);

        if (pct is 10 or 15)
        {
            Assert.Equal(60, morale);
            Assert.Equal(0, penalty);
        }
        else if (pct == 9)
        {
            Assert.Equal(58, morale);
            Assert.Equal(1, penalty);
        }
        else // 16
        {
            Assert.Equal(61, morale);
            Assert.Equal(0, penalty);
        }
    }

    /// <summary>
    /// Review round 1, B1: the clamp must apply on <em>every</em> branch, not only the one whose own
    /// arithmetic happens to overshoot — including the dead band, which previously passed an out-of-range
    /// <c>currentMorale</c> straight through, and the regen branch, which previously clamped only the
    /// ceiling and not the floor. <c>currentMorale</c> is not validated by the caller (a save, a scenario,
    /// or a future battle-entry write could hand this an out-of-range value), so every starting value must
    /// come back inside 51…70 regardless of which band fires.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(71)]
    [InlineData(100)]
    public void PropertyStyle_EveryStartingMoraleOutOfRange_IsClampedIntoRangeOnEveryBand(int startingMorale)
    {
        var ruleset = EconomyTestbed.Ruleset;

        var (afterDecay, _) = SupplyMoraleRule.ApplyToMorale(startingMorale, supplyPercent: 0, ruleset);
        var (afterRegen, _) = SupplyMoraleRule.ApplyToMorale(startingMorale, supplyPercent: 100, ruleset);
        var (afterDeadBand, _) = SupplyMoraleRule.ApplyToMorale(startingMorale, supplyPercent: 12, ruleset);

        Assert.InRange(afterDecay, 51, 70);
        Assert.InRange(afterRegen, 51, 70);
        Assert.InRange(afterDeadBand, 51, 70);
    }

    /// <summary>Review round 1, B1's own reproduction cases, pinned exactly.</summary>
    [Fact]
    public void DeadBand_AboveCeiling_ClampsDownRatherThanPassingThrough()
    {
        var (morale, penalty) = SupplyMoraleRule.ApplyToMorale(currentMorale: 75, supplyPercent: 12, EconomyTestbed.Ruleset);
        Assert.Equal(70, morale);
        Assert.Equal(0, penalty);
    }

    [Fact]
    public void Regen_FromBelowFloor_ClampsToFloorRatherThanStayingBelowIt()
    {
        // 40 + 1 = 41 unclamped; the confirmed floor forces 51, not 41.
        var (morale, penalty) = SupplyMoraleRule.ApplyToMorale(currentMorale: 40, supplyPercent: 50, EconomyTestbed.Ruleset);
        Assert.Equal(51, morale);
        Assert.Equal(0, penalty);
    }

    [Fact]
    public void DeadBand_BelowFloor_ClampsUpRatherThanPassingThrough()
    {
        var (morale, penalty) = SupplyMoraleRule.ApplyToMorale(currentMorale: 40, supplyPercent: 12, EconomyTestbed.Ruleset);
        Assert.Equal(51, morale);
        Assert.Equal(0, penalty);
    }
}
