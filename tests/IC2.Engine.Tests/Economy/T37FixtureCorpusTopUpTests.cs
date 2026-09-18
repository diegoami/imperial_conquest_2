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

    /// <summary>
    /// T39's folded follow-up (docs/task-catalogue.md Done-when 10, the lowest-value item there): a
    /// <c>[Theory]</c> restoring the per-case naming an earlier <c>foreach</c>-in-one-<c>[Fact]</c>
    /// consolidation lost, so a run that fails reports which specific id and constant broke, not just
    /// the first one alphabetically encountered inside a single failing test.
    /// </summary>
    public static IEnumerable<object[]> T35MissingConstantsCases()
    {
        yield return new object[] { "economy.treasuryCreditTaxBaseQuarterShareDivisor", 4, (Func<EconomyRules, int>)(e => e.TreasuryCreditTaxBaseQuarterShareDivisor) };
        yield return new object[] { "economy.treasuryCreditPerCityUpkeep", 7, (Func<EconomyRules, int>)(e => e.TreasuryCreditPerCityUpkeep) };
        yield return new object[] { "economy.treasuryCreditWealthDivisor", 20000, (Func<EconomyRules, int>)(e => e.TreasuryCreditWealthDivisor) };
        yield return new object[] { "economy.tradeIncomeTaxBaseDivisor", 12, (Func<EconomyRules, int>)(e => e.TradeIncomeTaxBaseDivisor) };
        yield return new object[] { "economy.wealthPerPopulationThousand", 3000, (Func<EconomyRules, int>)(e => e.WealthPerPopulationThousand) };
        yield return new object[] { "economy.populationGrowthGapDivisor", 4, (Func<EconomyRules, int>)(e => e.PopulationGrowthGapDivisor) };
        yield return new object[] { "economy.populationGrowthTaxDivisor", 120, (Func<EconomyRules, int>)(e => e.PopulationGrowthTaxDivisor) };
        yield return new object[] { "economy.populationGrowthMobilizationDivisor", 300, (Func<EconomyRules, int>)(e => e.PopulationGrowthMobilizationDivisor) };
        yield return new object[] { "economy.populationGrowthConstantAddend", 1, (Func<EconomyRules, int>)(e => e.PopulationGrowthConstantAddend) };
        yield return new object[] { "economy.threatenedCityAdjacencyRadius", 1, (Func<EconomyRules, int>)(e => e.ThreatenedCityAdjacencyRadius) };
        yield return new object[] { "economy.unityBaseGainPerQuarter", 25, (Func<EconomyRules, int>)(e => e.UnityBaseGainPerQuarter) };
        yield return new object[] { "economy.unityTaxRateDivisor", 2, (Func<EconomyRules, int>)(e => e.UnityTaxRateDivisor) };
        yield return new object[] { "economy.unityMobilizationDivisor", 5, (Func<EconomyRules, int>)(e => e.UnityMobilizationDivisor) };
        yield return new object[] { "economy.unityFloor", 300, (Func<EconomyRules, int>)(e => e.UnityFloor) };
        yield return new object[] { "economy.loyaltyRiseRollBound", 4, (Func<EconomyRules, int>)(e => e.LoyaltyRiseRollBound) };
        yield return new object[] { "economy.loyaltyFallProbabilityDenominator", 3, (Func<EconomyRules, int>)(e => e.LoyaltyFallProbabilityDenominator) };
        yield return new object[] { "economy.loyaltyFallTaxDivisor", 8, (Func<EconomyRules, int>)(e => e.LoyaltyFallTaxDivisor) };
    }

    [Theory]
    [MemberData(nameof(T35MissingConstantsCases))]
    public void T35sPreviouslyMissingConstants_AreNowInTheCorpus_AndMatchTheRuleset(
        string fixtureId, int expected, Func<EconomyRules, int> rulesetValue) =>
        AssertEconomyConstant(fixtureId, expected, rulesetValue);

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

    public static IEnumerable<object[]> SupplyProductionConstantsCases()
    {
        yield return new object[] { "citySupply.baselineSeasonValue", 40, (Func<EconomyRules, int>)(e => e.CitySupplyBaselineSeasonValue) };
        yield return new object[] { "citySupply.productionDivisor", 10, (Func<EconomyRules, int>)(e => e.CitySupplyProductionDivisor) };
        yield return new object[] { "citySupply.mobilizationDivisor", 200, (Func<EconomyRules, int>)(e => e.CitySupplyMobilizationDivisor) };
        yield return new object[] { "citySupply.capTonsPerPopulationThousand", 10, (Func<EconomyRules, int>)(e => e.CitySupplyCapTonsPerPopulationThousand) };
        yield return new object[] { "citySupply.winterFamineLoyaltyLossAmount", 1, (Func<EconomyRules, int>)(e => e.FamineLoyaltyLossAmount) };
    }

    [Theory]
    [MemberData(nameof(SupplyProductionConstantsCases))]
    public void ThisTasksOwnSupplyProductionConstants_AreInTheCorpus_AndMatchTheRuleset(
        string fixtureId, int expected, Func<EconomyRules, int> rulesetValue)
    {
        AssertEconomyConstant(fixtureId, expected, rulesetValue);
        Assert.Equal("city-population-growth.md", FixtureCorpus.Get(fixtureId).Source);
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
