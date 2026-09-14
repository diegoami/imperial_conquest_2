using IC2.Engine.Economy;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T08 Economy, supply, and purses", Done-when 4: "That army's
/// 482 tons against 48,173 troops reads exactly <c>100%</c>; the supply triple 204/998, 344/998, 184/282
/// reads <c>20% / 34% / 65%</c>." Every value here is transcribed from <c>tests/fixtures/corpus.json</c>'s
/// <c>roman13.*</c> and <c>supply.*</c> entries.
/// </summary>
public sealed class SupplyCapacityTests
{
    [Fact]
    public void PercentFull_Roman13UnitArmy_Reads100Percent()
    {
        // roman13.supplyTons=482, roman13.troops=48173, roman13.supplyPercent=100.
        Assert.Equal(100, SupplyCapacity.PercentFull(482, 48_173, EconomyTestbed.Ruleset));
    }

    [Fact]
    public void PercentFull_Army0First204TonReading_Reads20Percent()
    {
        // supply.army0.troops=99882, supply.army0.tons204Percent20=20.
        Assert.Equal(20, SupplyCapacity.PercentFull(204, 99_882, EconomyTestbed.Ruleset));
    }

    [Fact]
    public void PercentFull_Army0Second344TonReading_Reads34Percent()
    {
        // supply.army0.tons344Percent34=34.
        Assert.Equal(34, SupplyCapacity.PercentFull(344, 99_882, EconomyTestbed.Ruleset));
    }

    [Fact]
    public void PercentFull_Army2184TonReading_Reads65Percent()
    {
        // supply.army2.troops=28227, supply.army2.tons184Percent65=65.
        Assert.Equal(65, SupplyCapacity.PercentFull(184, 28_227, EconomyTestbed.Ruleset));
    }

    [Fact]
    public void ArmyCapacityTons_MatchesTroopsOverOneHundred()
    {
        // supply.army0.capacityTons=998, supply.army2.capacityTons=282.
        Assert.Equal(998, SupplyCapacity.ArmyCapacityTons(99_882, EconomyTestbed.Ruleset));
        Assert.Equal(282, SupplyCapacity.ArmyCapacityTons(28_227, EconomyTestbed.Ruleset));
    }

    [Fact]
    public void FleetCapacityTons_MatchesShipsTimesEight() =>
        Assert.Equal(96, SupplyCapacity.FleetCapacityTons(12, EconomyTestbed.Ruleset));

    [Fact]
    public void PercentFull_ZeroTroops_ThrowsRatherThanSilentlyDividing() =>
        Assert.Throws<DivideByZeroException>(() => SupplyCapacity.PercentFull(10, 0, EconomyTestbed.Ruleset));
}
