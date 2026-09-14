using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/build-orchestration-plan.md</c> "T08 Economy, supply, and purses", Done-when 13: the fixtures
/// corpus is topped up from the 47th report, <c>supply-driven-morale-and-fleet-attrition.md</c>, which
/// postdates T04's 355-entry / 46-report pass. This is a top-up under T04's existing contract
/// (<c>tests/fixtures/**</c>), not a reopen of it: T04's own four Done-when checks
/// (<c>IC2.Engine.Tests.Fixtures.FixturesCorpusTests</c>) already re-run generically over the whole
/// corpus and stay green with these new entries present; this file spot-checks that the specific new
/// entries this task added resolve to the expected transcribed values, the same insurance
/// <c>FixturesCorpusTests.ASampleOfEntriesResolveToTheExpectedTranscribedValues</c> gives the pre-existing
/// corpus.
/// </summary>
public sealed class FixtureCorpusTopUpTests
{
    [Fact]
    public void KnownReportsManifest_IncludesThe47thReport() =>
        Assert.Contains(
            "supply-driven-morale-and-fleet-attrition.md", FixtureCorpus.KnownReportFilenames);

    [Fact]
    public void SeasonTable_MatchesTheRulesetsShippedValues()
    {
        Assert.Equal(50, FixtureCorpus.Get("supplyMorale.seasonTable.spring").AsInt());
        Assert.Equal(80, FixtureCorpus.Get("supplyMorale.seasonTable.summer").AsInt());
        Assert.Equal(80, FixtureCorpus.Get("supplyMorale.seasonTable.autumn").AsInt());
        Assert.Equal(20, FixtureCorpus.Get("supplyMorale.seasonTable.winter").AsInt());

        var rules = EconomyTestbed.Ruleset.Economy.SupplyConsumption;
        Assert.Equal(FixtureCorpus.Get("supplyMorale.seasonTable.spring").AsInt(), rules.SeasonValues[0]);
        Assert.Equal(FixtureCorpus.Get("supplyMorale.seasonTable.summer").AsInt(), rules.SeasonValues[1]);
        Assert.Equal(FixtureCorpus.Get("supplyMorale.seasonTable.autumn").AsInt(), rules.SeasonValues[2]);
        Assert.Equal(FixtureCorpus.Get("supplyMorale.seasonTable.winter").AsInt(), rules.SeasonValues[3]);
    }

    [Fact]
    public void ConsumptionDivisor_MatchesTheRulesetsShippedValue()
    {
        Assert.Equal(20000, FixtureCorpus.Get("supplyMorale.consumptionDivisor").AsInt());
        Assert.Equal(
            FixtureCorpus.Get("supplyMorale.consumptionDivisor").AsInt(),
            EconomyTestbed.Ruleset.Economy.SupplyConsumption.ConsumptionDivisor);
    }

    [Fact]
    public void MoraleClampsAndDeadBand_MatchTheRulesetsShippedValues()
    {
        var rules = EconomyTestbed.Ruleset.Economy.SupplyMorale;

        Assert.Equal(10, FixtureCorpus.Get("supplyMorale.decayThresholdPercent").AsInt());
        Assert.Equal(FixtureCorpus.Get("supplyMorale.decayThresholdPercent").AsInt(), rules.DecayThresholdPercent);

        Assert.Equal(15, FixtureCorpus.Get("supplyMorale.deadBandUpperPercent").AsInt());
        Assert.Equal(FixtureCorpus.Get("supplyMorale.deadBandUpperPercent").AsInt(), rules.DeadBandUpperPercent);

        Assert.Equal(51, FixtureCorpus.Get("supplyMorale.floor").AsInt());
        Assert.Equal(FixtureCorpus.Get("supplyMorale.floor").AsInt(), rules.MoraleFloor);

        Assert.Equal(70, FixtureCorpus.Get("supplyMorale.ceiling").AsInt());
        Assert.Equal(FixtureCorpus.Get("supplyMorale.ceiling").AsInt(), rules.MoraleCeiling);
    }

    [Fact]
    public void NewArmyMorale_CrossConfirmsTheAlreadyShippedArmyManagementValue()
    {
        Assert.Equal(59, FixtureCorpus.Get("supplyMorale.newArmyMorale").AsInt());
        Assert.Equal(
            FixtureCorpus.Get("supplyMorale.newArmyMorale").AsInt(),
            EconomyTestbed.Ruleset.ArmyManagement.NewArmyMorale);
    }

    [Fact]
    public void FleetConditionConstants_ArePresentForT14()
    {
        Assert.Equal(3, FixtureCorpus.Get("fleet.condition.zeroSupplyMovesLoss").AsInt());
        Assert.Equal(1, FixtureCorpus.Get("fleet.condition.zeroSupplyConditionLossMax").AsInt());
        Assert.Equal(40, FixtureCorpus.Get("fleet.condition.deathThreshold").AsInt());
        Assert.Equal(70, FixtureCorpus.Get("fleet.condition.movePenaltyThreshold").AsInt());
        Assert.Equal(2, FixtureCorpus.Get("fleet.condition.movePenaltyShift").AsInt());
        Assert.Equal("supply-driven-morale-and-fleet-attrition.md", FixtureCorpus.Get("fleet.condition.deathThreshold").Source);
    }
}
