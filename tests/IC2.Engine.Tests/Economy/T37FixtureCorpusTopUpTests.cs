using IC2.Engine.Model;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T37 City supply production and famine unrest", Done-when 7, 9 and
/// 11: the corpus top-up. Done-when 9 and 11 are folded corrections to T35's already-merged corpus work
/// (bugs <c>#131</c> and <c>#133</c>) — this file spot-checks that T35's DoD 8 constants finally landed,
/// and that the mis-attributed Winter entry is corrected, alongside Done-when 7's own new entries for
/// this task's supply-production constants. T04's own four Done-when checks
/// (<see cref="IC2.Engine.Tests.Fixtures.FixturesCorpusTests"/>) already re-run generically over the
/// whole corpus and stay green with these additions present.
/// </summary>
/// <remarks>
/// Review round 1, Finding 3: the two theories below are named <c>..._AndMatchTheRuleset</c> and now
/// actually do, via <see cref="AssertEconomyConstant"/> — each case checks the corpus entry <em>and</em>
/// the live <c>toy-ruleset.json</c> value read through <see cref="EconomyRules"/>, not the corpus alone.
/// </remarks>
public sealed class T37FixtureCorpusTopUpTests
{
    private static readonly EconomyRules Economy = EconomyTestbed.Ruleset.Economy;

    private static void AssertEconomyConstant(string fixtureId, int expected, Func<EconomyRules, int> rulesetValue)
    {
        var entry = FixtureCorpus.Get(fixtureId);
        Assert.Equal(expected, entry.AsInt());
        Assert.Equal(expected, rulesetValue(Economy));
    }

    // ---- Done when 9 (bug #131): T35's DoD 8 corpus top-up, which its merge added zero new entries
    // for, finally lands. ----

    [Fact]
    public void T35sPreviouslyMissingConstants_AreNowInTheCorpus_AndMatchTheRuleset()
    {
        AssertEconomyConstant("economy.treasuryCreditTaxBaseQuarterShareDivisor", 4, e => e.TreasuryCreditTaxBaseQuarterShareDivisor);
        AssertEconomyConstant("economy.treasuryCreditPerCityUpkeep", 7, e => e.TreasuryCreditPerCityUpkeep);
        AssertEconomyConstant("economy.treasuryCreditWealthDivisor", 20000, e => e.TreasuryCreditWealthDivisor);
        AssertEconomyConstant("economy.tradeIncomeTaxBaseDivisor", 12, e => e.TradeIncomeTaxBaseDivisor);
        AssertEconomyConstant("economy.wealthPerPopulationThousand", 3000, e => e.WealthPerPopulationThousand);
        AssertEconomyConstant("economy.populationGrowthGapDivisor", 4, e => e.PopulationGrowthGapDivisor);
        AssertEconomyConstant("economy.populationGrowthTaxDivisor", 120, e => e.PopulationGrowthTaxDivisor);
        AssertEconomyConstant("economy.populationGrowthMobilizationDivisor", 300, e => e.PopulationGrowthMobilizationDivisor);
        AssertEconomyConstant("economy.populationGrowthConstantAddend", 1, e => e.PopulationGrowthConstantAddend);
        AssertEconomyConstant("economy.threatenedCityAdjacencyRadius", 1, e => e.ThreatenedCityAdjacencyRadius);
        AssertEconomyConstant("economy.unityBaseGainPerQuarter", 25, e => e.UnityBaseGainPerQuarter);
        AssertEconomyConstant("economy.unityTaxRateDivisor", 2, e => e.UnityTaxRateDivisor);
        AssertEconomyConstant("economy.unityMobilizationDivisor", 5, e => e.UnityMobilizationDivisor);
        AssertEconomyConstant("economy.unityFloor", 300, e => e.UnityFloor);
        AssertEconomyConstant("economy.loyaltyRiseRollBound", 4, e => e.LoyaltyRiseRollBound);
        AssertEconomyConstant("economy.loyaltyFallProbabilityDenominator", 3, e => e.LoyaltyFallProbabilityDenominator);
        AssertEconomyConstant("economy.loyaltyFallTaxDivisor", 8, e => e.LoyaltyFallTaxDivisor);
    }

    [Fact]
    public void UpkeepPaymentAndDesertion_JoinsTheKnownReportsManifest()
    {
        // toy-ruleset.json already cites it (economy.tradeIncomeTaxBaseDivisor's _provenance); T04's own
        // check does not cover ruleset provenance, so this closes the gap directly.
        Assert.Contains("upkeep-payment-and-desertion.md", FixtureCorpus.KnownReportFilenames);
    }

    [Fact]
    public void TradeIncomeTaxBaseDivisor_CitesUpkeepPaymentAndDesertion()
    {
        var entry = FixtureCorpus.Get("economy.tradeIncomeTaxBaseDivisor");
        Assert.Equal("upkeep-payment-and-desertion.md", entry.Source);
    }

    // ---- Done when 7: this task's own supply-production constants join the corpus. ----

    [Fact]
    public void ThisTasksOwnSupplyProductionConstants_AreInTheCorpus_AndMatchTheRuleset()
    {
        AssertEconomyConstant("citySupply.baselineSeasonValue", 40, e => e.CitySupplyBaselineSeasonValue);
        AssertEconomyConstant("citySupply.productionDivisor", 10, e => e.CitySupplyProductionDivisor);
        AssertEconomyConstant("citySupply.mobilizationDivisor", 200, e => e.CitySupplyMobilizationDivisor);
        AssertEconomyConstant("citySupply.capTonsPerPopulationThousand", 10, e => e.CitySupplyCapTonsPerPopulationThousand);
        AssertEconomyConstant("citySupply.winterFamineLoyaltyLossAmount", 1, e => e.FamineLoyaltyLossAmount);

        foreach (var id in new[]
                 {
                     "citySupply.baselineSeasonValue", "citySupply.productionDivisor",
                     "citySupply.mobilizationDivisor", "citySupply.capTonsPerPopulationThousand",
                     "citySupply.winterFamineLoyaltyLossAmount",
                 })
        {
            Assert.Equal("city-population-growth.md", FixtureCorpus.Get(id).Source);
        }
    }

    // ---- Done when 11 (bug #133): the mis-attributed Winter corpus entry is corrected -- the 1-in-3
    // value survives, but the id, note and attributed effect (loyalty, not population; supply stock,
    // not a growth accumulator) are corrected. ----

    [Fact]
    public void TheOldMisattributedCalendarEntry_NoLongerExists()
    {
        Assert.Throws<KeyNotFoundException>(() => FixtureCorpus.Get("calendar.winterPopulationDeclineChance"));
    }

    [Fact]
    public void TheWinterFamineChance_SurvivesAt1In3_ButIsNowAttributedToLoyaltyNotPopulation()
    {
        var entry = FixtureCorpus.Get("citySupply.winterFamineLoyaltyLossChance");

        Assert.Equal("1 in 3", entry.AsString());
        Assert.Equal("city-population-growth.md", entry.Source);
        Assert.Contains("loyalty", entry.Note, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("loses a population unit", entry.Note, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AllFourT04ChecksStayGreenWithTheCorrectedCorpus()
    {
        // Re-runs the same assertions FixturesCorpusTests makes, generically, over the corpus this
        // task's edits produced -- the top-up contract's own requirement.
        Assert.NotEmpty(FixtureCorpus.All);
        Assert.True(FixtureCorpus.All.All(e => !string.IsNullOrWhiteSpace(e.Id)));
        var known = new HashSet<string>(FixtureCorpus.KnownReportFilenames, StringComparer.Ordinal);
        Assert.True(FixtureCorpus.All.Select(e => e.Source).Distinct().All(known.Contains));
        Assert.True(FixtureCorpus.RequiredIds.All(id => FixtureCorpus.All.Any(e => e.Id == id)));
        Assert.Equal(FixtureCorpus.All.Count, FixtureCorpus.All.Select(e => e.Id).Distinct().Count());
    }
}
