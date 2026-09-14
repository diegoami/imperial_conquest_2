using IC2.Engine.Economy;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T08 Economy, supply, and purses", Done-when 2:
/// "Ship upkeep <c>= 3 × ships</c> per quarter."
/// </summary>
public sealed class ShipUpkeepTests
{
    [Theory]
    [InlineData(10, 30)]
    [InlineData(90, 270)]
    [InlineData(0, 0)]
    public void Compute_MatchesShipsTimesThree(int ships, int expected)
    {
        Assert.Equal(expected, ShipUpkeep.Compute(ships, EconomyTestbed.Ruleset));
    }

    [Fact]
    public void Compute_UsesTheRulesetsPerQuarterRateNotALiteral()
    {
        var wider = EconomyTestbed.Ruleset with
        {
            Economy = EconomyTestbed.Ruleset.Economy with { ShipUpkeepPerQuarter = 7 },
        };

        Assert.Equal(70, ShipUpkeep.Compute(10, wider));
    }
}
