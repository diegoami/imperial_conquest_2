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
    /// <summary>
    /// Review round 1, B2/N1: <c>supply.dialogClampsOneModel</c> is tagged <c>derived</c>, not
    /// <c>confirmed</c> -- the full chain, including the paid path's <c>money * 5</c> term, comes only
    /// from the report's own pseudocode in a section headed <c>[derived]</c> ("Which path a purchase
    /// takes"); no instruction listing or save cross-check exercises the combined path. This test pins
    /// the tag explicitly so a later re-tag back to <c>confirmed</c> fails loudly rather than silently.
    /// </summary>
    [Fact]
    public void OneClampingModel_IsTaggedDerived_And_RoomNotFlooredAtZero_IsTaggedConfirmed()
    {
        Assert.Equal(
            "min(request, provider stock, cap - supplies[, money * 5 foreign only])",
            FixtureCorpus.Get("supply.dialogClampsOneModel").AsString());
        Assert.Equal("derived", FixtureCorpus.Get("supply.dialogClampsOneModel").Tag);

        Assert.True(FixtureCorpus.Get("supply.roomNotFlooredAtZero").AsBool());
        Assert.Equal("confirmed", FixtureCorpus.Get("supply.roomNotFlooredAtZero").Tag); // read straight off the [confirmed] instruction listing.
    }

    [Fact]
    public void DialogForeignCredit_IsTaggedDerived()
    {
        Assert.Equal(
            "the selling city's owner's treasury, credited amount / 5",
            FixtureCorpus.Get("supply.dialogForeignCredit.destination").AsString());
        Assert.Equal("derived", FixtureCorpus.Get("supply.dialogForeignCredit.destination").Tag);
    }

    /// <summary>The tons formulas are direct code reads (the report disassembles both functions), so both stay <c>confirmed</c>.</summary>
    [Fact]
    public void AutoResupplyFormulas_MatchTheImplementation_AndAreTaggedConfirmed()
    {
        Assert.Equal(
            "min(troops / 100 - supplies, city stock)",
            FixtureCorpus.Get("autoResupply.army.tonsFormula").AsString());
        Assert.Equal("confirmed", FixtureCorpus.Get("autoResupply.army.tonsFormula").Tag);

        Assert.Equal(
            "min(ships * 8 - supplies, city stock)",
            FixtureCorpus.Get("autoResupply.fleet.tonsFormula").AsString());
        Assert.Equal("confirmed", FixtureCorpus.Get("autoResupply.fleet.tonsFormula").Tag);
    }

    /// <summary>
    /// Review round 1, B2: all three purse-hygiene constants are tagged <c>derived</c>, matching
    /// <c>docs/game-design.md:100</c>'s own "confirmed caps; derived purse rule" -- the report's section
    /// heading is <c>[derived, then confirmed below]</c>, and the "confirmed below" paragraph confirms
    /// only the <c>troops div 100</c> cap, not the purse rule. Pinned explicitly per the review: the
    /// values alone were previously asserted but not the tag, so a re-tag to <c>confirmed</c> would have
    /// passed silently.
    /// </summary>
    [Fact]
    public void AutoResupplyPurseHygieneConstants_MatchTheRulesetsShippedValues_AndAreTaggedDerived()
    {
        Assert.Equal(1000, FixtureCorpus.Get("autoResupply.ownCity.purseExcessThreshold").AsInt());
        Assert.Equal("derived", FixtureCorpus.Get("autoResupply.ownCity.purseExcessThreshold").Tag);
        Assert.Equal(1000, EconomyTestbed.Ruleset.Economy.PurseCapPerUnit);

        Assert.Equal(500, FixtureCorpus.Get("autoResupply.ownCity.purseTopUpThreshold").AsInt());
        Assert.Equal("derived", FixtureCorpus.Get("autoResupply.ownCity.purseTopUpThreshold").Tag);
        Assert.Equal(500, EconomyTestbed.Ruleset.Economy.AutoResupplyPurseTopUpThreshold);

        Assert.Equal(500, FixtureCorpus.Get("autoResupply.ownCity.purseTopUpAmount").AsInt());
        Assert.Equal("derived", FixtureCorpus.Get("autoResupply.ownCity.purseTopUpAmount").Tag);
        Assert.Equal(500, EconomyTestbed.Ruleset.Economy.AutoResupplyPurseTopUpAmount);
    }

    /// <summary>Review round 1, N1: the divisor 5 is confirmed elsewhere as <c>supplyTonsPerTalent</c>, but its use as this specific cap is derived (no foreign automatic-resupply save exists).</summary>
    [Fact]
    public void AutoResupplyForeignMoneyCapDivisor_MatchesTheRulesetsShippedValue_AndIsTaggedDerived()
    {
        Assert.Equal(5, FixtureCorpus.Get("autoResupply.foreignCity.moneyCapDivisor").AsInt());
        Assert.Equal("derived", FixtureCorpus.Get("autoResupply.foreignCity.moneyCapDivisor").Tag);
        Assert.Equal(5, EconomyTestbed.Ruleset.Economy.SupplyTonsPerTalent);
    }

    [Fact]
    public void KnownReportsManifest_AlreadyIncludesSupplyCapacityRounding() =>
        Assert.Contains("supply-capacity-rounding.md", FixtureCorpus.KnownReportFilenames);
}
