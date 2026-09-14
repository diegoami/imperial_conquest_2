using IC2.Engine.Economy;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/build-orchestration-plan.md</c> "T08 Economy, supply, and purses", Done-when 10: "a
/// 22,000-troop army consumes exactly <c>44 / 11 / 11 / 77</c> tons per turn in Spring / Summer / Autumn /
/// Winter, from the season values 50/80/80/20 read out of the ruleset (not hardcoded in C#); an army
/// aboard a fleet consumes exactly <c>troops / 200</c> in every season, asserted separately."
/// </summary>
public sealed class SupplyConsumptionTests
{
    private const int Troops = 22_000;

    [Theory]
    [InlineData(0, 44)] // Spring: (90-50)*22000/20000 = 44.
    [InlineData(1, 11)] // Summer: (90-80)*22000/20000 = 11.
    [InlineData(2, 11)] // Autumn: (90-80)*22000/20000 = 11.
    [InlineData(3, 77)] // Winter: (90-20)*22000/20000 = 77.
    public void ArmyFieldConsumption_22000TroopArmy_MatchesEachSeason(int seasonIndex, int expectedTons)
    {
        Assert.Equal(expectedTons, SupplyConsumption.ArmyFieldConsumption(Troops, seasonIndex, EconomyTestbed.Ruleset));
    }

    [Fact]
    public void ArmyFieldConsumption_WinterCosts7xSummer()
    {
        var winter = SupplyConsumption.ArmyFieldConsumption(Troops, seasonIndex: 3, EconomyTestbed.Ruleset);
        var summer = SupplyConsumption.ArmyFieldConsumption(Troops, seasonIndex: 1, EconomyTestbed.Ruleset);

        Assert.Equal(7 * summer, winter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ArmyEmbarkedConsumption_IsFlatTroopsOver200RegardlessOfSeason(int seasonIndex)
    {
        // seasonIndex is unused by the embarked formula at all -- the point of the test is that it does
        // not need to be, unlike ArmyFieldConsumption above.
        _ = seasonIndex;
        Assert.Equal(110, SupplyConsumption.ArmyEmbarkedConsumption(Troops, EconomyTestbed.Ruleset));
    }

    [Fact]
    public void ArmyFieldConsumption_ReadsTheSeasonTableFromTheRulesetNotALiteral()
    {
        // Changing the ruleset's season table changes the result -- proves the season values are read
        // from EconomyRules.SupplyConsumption.SeasonValues, not a hardcoded 50/80/80/20 in C#.
        var rules = EconomyTestbed.Ruleset.Economy;
        var widened = rules with
        {
            SupplyConsumption = rules.SupplyConsumption with
            {
                SeasonValues = IC2.Engine.Model.ValueList.Of(0, 0, 0, 0),
            },
        };
        var wideRuleset = EconomyTestbed.Ruleset with { Economy = widened };

        // seasonVal=0 -> (90-0)*22000/20000 = 99, not the shipped Spring figure of 44.
        Assert.Equal(99, SupplyConsumption.ArmyFieldConsumption(Troops, seasonIndex: 0, wideRuleset));
    }

    [Fact]
    public void ArmyFieldConsumption_OutOfRangeSeasonIndex_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => SupplyConsumption.ArmyFieldConsumption(Troops, seasonIndex: 4, EconomyTestbed.Ruleset));

    [Fact]
    public void ApplyConsumption_NeverGoesBelowZero() =>
        Assert.Equal(0, SupplyConsumption.ApplyConsumption(currentSupplyTons: 5, consumption: 44));
}
