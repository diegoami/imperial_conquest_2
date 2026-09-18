using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T37 City supply production and famine unrest", Done-when 7, 10 and
/// 12: the corpus top-up. Done-when 10 and 12 are folded corrections to T35's already-merged corpus work
/// (bugs <c>#131</c> and <c>#133</c>) — this file spot-checks that T35's DoD 8 constants finally landed,
/// and that the mis-attributed Winter entry is corrected, alongside Done-when 7's own new entries for
/// this task's supply-production constants. T04's own four Done-when checks
/// (<see cref="IC2.Engine.Tests.Fixtures.FixturesCorpusTests"/>) already re-run generically over the
/// whole corpus and stay green with these additions present.
/// </summary>
public sealed class T37FixtureCorpusTopUpTests
{
    // ---- Done when 10 (bug #131): T35's DoD 8 corpus top-up, which its merge added zero new entries
    // for, finally lands. ----

    [Theory]
    [InlineData("economy.treasuryCreditTaxBaseQuarterShareDivisor", 4)]
    [InlineData("economy.treasuryCreditPerCityUpkeep", 7)]
    [InlineData("economy.treasuryCreditWealthDivisor", 20000)]
    [InlineData("economy.tradeIncomeTaxBaseDivisor", 12)]
    [InlineData("economy.wealthPerPopulationThousand", 3000)]
    [InlineData("economy.populationGrowthGapDivisor", 4)]
    [InlineData("economy.populationGrowthTaxDivisor", 120)]
    [InlineData("economy.populationGrowthMobilizationDivisor", 300)]
    [InlineData("economy.populationGrowthConstantAddend", 1)]
    [InlineData("economy.threatenedCityAdjacencyRadius", 1)]
    [InlineData("economy.unityBaseGainPerQuarter", 25)]
    [InlineData("economy.unityTaxRateDivisor", 2)]
    [InlineData("economy.unityMobilizationDivisor", 5)]
    [InlineData("economy.unityFloor", 300)]
    [InlineData("economy.loyaltyRiseRollBound", 4)]
    [InlineData("economy.loyaltyFallProbabilityDenominator", 3)]
    [InlineData("economy.loyaltyFallTaxDivisor", 8)]
    public void T35sPreviouslyMissingConstants_AreNowInTheCorpus_AndMatchTheRuleset(string fixtureId, int expected)
    {
        var entry = FixtureCorpus.Get(fixtureId);
        Assert.Equal(expected, entry.AsInt());
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

    [Theory]
    [InlineData("citySupply.baselineSeasonValue", 40)]
    [InlineData("citySupply.productionDivisor", 10)]
    [InlineData("citySupply.mobilizationDivisor", 200)]
    [InlineData("citySupply.capTonsPerPopulationThousand", 10)]
    [InlineData("citySupply.winterFamineLoyaltyLossAmount", 1)]
    public void ThisTasksOwnSupplyProductionConstants_AreInTheCorpus_AndMatchTheRuleset(string fixtureId, int expected)
    {
        var entry = FixtureCorpus.Get(fixtureId);
        Assert.Equal(expected, entry.AsInt());
        Assert.Equal("city-population-growth.md", entry.Source);
    }

    // ---- Done when 12 (bug #133): the mis-attributed Winter corpus entry is corrected -- the 1-in-3
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
