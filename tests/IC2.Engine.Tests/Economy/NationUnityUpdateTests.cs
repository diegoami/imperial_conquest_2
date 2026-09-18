using IC2.Engine.Economy;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T35 Model: nation tax base, recruitment slots, and the pending
/// diplomatic offer", Done-when 10: the quarterly mobilization decay and unity update, pure-function
/// coverage. Every value below is transcribed from <c>city-population-growth.md</c>.
/// </summary>
public sealed class NationUnityUpdateTests
{
    private static readonly IC2.Engine.Model.EconomyRules Economy = EconomyTestbed.Ruleset.Economy;

    [Theory]
    [InlineData(500, 20, 30, 27, 510)]
    [InlineData(980, 0, 0, 0, 990)] // the unity cap.
    [InlineData(310, 100, 100, 97, 300)] // the unity floor.
    public void DecaysMobilizationThenUpdatesUnity(int unity, int taxRatePercent, int mobilizedPercent, int expectedMobilization, int expectedUnity)
    {
        var decayed = NationUnityUpdate.DecayMobilization(mobilizedPercent, Economy);
        Assert.Equal(expectedMobilization, decayed);

        var newUnity = NationUnityUpdate.Compute(unity, taxRatePercent, decayed, Economy);
        Assert.Equal(expectedUnity, newUnity);
    }

    [Fact]
    public void MobilizationNeverDecaysBelowZero()
    {
        Assert.Equal(0, NationUnityUpdate.DecayMobilization(2, Economy));
        Assert.Equal(0, NationUnityUpdate.DecayMobilization(0, Economy));
    }
}
