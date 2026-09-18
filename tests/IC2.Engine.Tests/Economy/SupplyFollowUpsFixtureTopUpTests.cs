using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T38 Supply dialog follow-ups, treasury ↔ purse transfers, and
/// automatic resupply" (issue #78), Done-when 8: "the report's constants join the corpus as confirmed or
/// derived entries matching its tags... T04's four checks stay green." This is a top-up under T04's
/// existing contract (<c>tests/fixtures/**</c>), the same pattern <c>FixtureCorpusTopUpTests</c> (T08)
/// already used: T04's own four Done-when checks
/// (<c>IC2.Engine.Tests.Fixtures.FixturesCorpusTests</c>) already re-run generically over the whole
/// corpus and stay green with these new entries present; this file spot-checks that the specific entries
/// T38 added resolve to the expected transcribed values.
/// </summary>
public sealed class SupplyFollowUpsFixtureTopUpTests
{
    [Fact]
    public void OneClampingModel_And_RoomNotFlooredAtZero_AreRecorded()
    {
        Assert.Equal(
            "min(request, provider stock, cap - supplies[, money * 5 foreign only])",
            FixtureCorpus.Get("supply.dialogClampsOneModel").AsString());
        Assert.Equal("confirmed", FixtureCorpus.Get("supply.dialogClampsOneModel").Tag);

        Assert.True(FixtureCorpus.Get("supply.roomNotFlooredAtZero").AsBool());
        Assert.Equal("confirmed", FixtureCorpus.Get("supply.roomNotFlooredAtZero").Tag);
    }

    [Fact]
    public void DialogForeignCredit_IsTaggedDerived()
    {
        Assert.Equal(
            "the selling city's owner's treasury, credited amount / 5",
            FixtureCorpus.Get("supply.dialogForeignCredit.destination").AsString());
        Assert.Equal("derived", FixtureCorpus.Get("supply.dialogForeignCredit.destination").Tag);
    }

    [Fact]
    public void AutoResupplyFormulas_MatchTheImplementation()
    {
        Assert.Equal(
            "min(troops / 100 - supplies, city stock)",
            FixtureCorpus.Get("autoResupply.army.tonsFormula").AsString());
        Assert.Equal(
            "min(ships * 8 - supplies, city stock)",
            FixtureCorpus.Get("autoResupply.fleet.tonsFormula").AsString());
    }

    [Fact]
    public void AutoResupplyPurseHygieneConstants_MatchTheRulesetsShippedValues()
    {
        Assert.Equal(1000, FixtureCorpus.Get("autoResupply.ownCity.purseExcessThreshold").AsInt());
        Assert.Equal(1000, EconomyTestbed.Ruleset.Economy.PurseCapPerUnit);

        Assert.Equal(500, FixtureCorpus.Get("autoResupply.ownCity.purseTopUpThreshold").AsInt());
        Assert.Equal(500, EconomyTestbed.Ruleset.Economy.AutoResupplyPurseTopUpThreshold);

        Assert.Equal(500, FixtureCorpus.Get("autoResupply.ownCity.purseTopUpAmount").AsInt());
        Assert.Equal(500, EconomyTestbed.Ruleset.Economy.AutoResupplyPurseTopUpAmount);
    }

    [Fact]
    public void AutoResupplyForeignMoneyCapDivisor_MatchesTheRulesetsShippedValue()
    {
        Assert.Equal(5, FixtureCorpus.Get("autoResupply.foreignCity.moneyCapDivisor").AsInt());
        Assert.Equal(5, EconomyTestbed.Ruleset.Economy.SupplyTonsPerTalent);
    }

    [Fact]
    public void KnownReportsManifest_AlreadyIncludesSupplyCapacityRounding() =>
        Assert.Contains("supply-capacity-rounding.md", FixtureCorpus.KnownReportFilenames);
}
