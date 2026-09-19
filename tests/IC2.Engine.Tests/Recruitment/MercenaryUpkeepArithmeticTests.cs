using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T13 Recruitment and mercenaries", Done-when 3: mercenary quarterly
/// upkeep <c>= (troops / 200) × price[type] × quality / 5</c>; a regular of identical troops/type costs
/// exactly <c>5 / quality</c> as much. T39 owns <em>charging</em> this upkeep to the army's own purse
/// (and garrison upkeep) — this task does not re-implement either, it asserts the confirmed arithmetic
/// through T39's own already-merged <see cref="ArmyUpkeep"/> API.
/// </summary>
public sealed class MercenaryUpkeepArithmeticTests
{
    private static Ruleset Ruleset => CoreTestbed.Toy.Ruleset;

    /// <summary>
    /// The real, already-in-the-toy-world mercenary unit: north-army-1's "Gallic Bowmen"
    /// (<c>mercenaryLabel</c> 11, archers, 3,500 troops, quality 7) — one test asserting both the
    /// mercenary figure and its regular counterpart, per Done-when 3's own wording.
    /// </summary>
    [Fact]
    public void Mercenary_upkeep_and_the_equal_regular_counterpart_match_the_confirmed_formula_shape()
    {
        var army = CoreTestbed.InitialState().ArmyById("north-army-1")!;
        var mercenary = Assert.Single(army.Units, u => u.IsMercenary);
        Assert.Equal("Gallic Bowmen", mercenary.Name);
        Assert.Equal(3500, mercenary.Troops);
        Assert.Equal(7, mercenary.Quality);

        var regular = mercenary with { MercenaryLabel = 0 };

        var regularCost = ArmyUpkeep.ComputeUnit(regular, Ruleset);
        var mercenaryCost = ArmyUpkeep.ComputeUnit(mercenary, Ruleset);

        var type = Ruleset.UnitTypeById(mercenary.UnitTypeId)!;
        var expectedRegular = (mercenary.Troops / Ruleset.Recruitment.TroopsPerCostUnit) * type.QuarterlyPrice;
        var expectedMercenary = expectedRegular * mercenary.Quality / Ruleset.Recruitment.MercenaryUpkeepQualityDivisor;

        Assert.Equal(expectedRegular, regularCost);
        Assert.Equal(expectedMercenary, mercenaryCost);

        // "a regular of identical troops/type costs exactly 5x/quality as much" -- i.e. mercenaryCost is
        // regularCost scaled by quality/5, the MercenaryUpkeepQualityDivisor ruleset constant.
        Assert.Equal(regularCost * mercenary.Quality / Ruleset.Recruitment.MercenaryUpkeepQualityDivisor, mercenaryCost);
    }

    /// <summary>
    /// The independently-observed Felsina figure: 6,438 "very good" (quality 8) light infantry upkeep at
    /// exactly 51 talents/quarter (<c>mercenary-pool-record.md</c>, transcribed as
    /// <c>mercenary.felsina.quarterlyCostTalents</c>).
    /// </summary>
    [Fact]
    public void Mercenary_upkeep_reproduces_the_confirmed_Felsina_quarterly_figure()
    {
        var troops = FixtureCorpus.Get("mercenary.felsina.troops").AsInt();
        var quality = FixtureCorpus.Get("mercenary.felsina.qualityCode").AsInt();
        var expected = FixtureCorpus.Get("mercenary.felsina.quarterlyCostTalents").AsInt();

        var mercenary = new UnitSlot(MercenaryLabel: 11, UnitTypeId: "light_infantry", Troops: troops, Quality: quality, Name: "Gallic");

        Assert.Equal(expected, ArmyUpkeep.ComputeUnit(mercenary, Ruleset));
    }
}
