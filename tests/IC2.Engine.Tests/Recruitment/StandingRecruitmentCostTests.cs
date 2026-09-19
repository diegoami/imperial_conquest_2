using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T13 Recruitment and mercenaries", Done-when 1: "<c>cost = (troops /
/// 200) × price[type]</c> reproduces every solved value in the corpus." The corpus's only independently
/// solved values for this formula shape are the 1,400-light-cavalry pair
/// (<c>menu-and-toolbar-inventory.md</c>, <c>recruitment.lightCavalry1400.*</c>) — 105 talents initial,
/// 21 talents quarterly — which is also the pair <c>decompiled-recruitment-cost-formula.md</c> itself
/// algebraically solved the unit-type table's 15/3 entries from.
/// </summary>
public sealed class StandingRecruitmentCostTests
{
    private static Ruleset Ruleset => CoreTestbed.Toy.Ruleset;

    // menu-and-toolbar-inventory.md: "1,400 light cavalry: initial 105, quarterly 21".
    private const int LightCavalry1400Troops = 1400;

    [Fact]
    public void InitialCost_reproduces_the_solved_1400_light_cavalry_value()
    {
        var expected = FixtureCorpus.Get("recruitment.lightCavalry1400.initialCost").AsInt();

        var actual = StandingRecruitmentCost.InitialCost(LightCavalry1400Troops, "light_cavalry", Ruleset);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void QuarterlyCost_reproduces_the_solved_1400_light_cavalry_value()
    {
        var expected = FixtureCorpus.Get("recruitment.lightCavalry1400.quarterlyCost").AsInt();

        var actual = StandingRecruitmentCost.QuarterlyCost(LightCavalry1400Troops, "light_cavalry", Ruleset);

        Assert.Equal(expected, actual);
    }

    /// <summary>
    /// Every unit type's table entry reproduces itself at exactly <c>troopsPerCostUnit</c> troops (the
    /// formula's own unit), over all five shipped types — a broader sweep than the single historical
    /// pair, using only ruleset data (<c>unitType.*.recruitCostInitial/Quarterly</c> transcribed into
    /// <c>data/rulesets/toy-ruleset.json</c> from <c>unit-type-stat-table-in-dat.md</c>), never a literal.
    /// </summary>
    [Theory]
    [InlineData("light_infantry")]
    [InlineData("heavy_infantry")]
    [InlineData("archers")]
    [InlineData("light_cavalry")]
    [InlineData("heavy_cavalry")]
    public void InitialCost_and_QuarterlyCost_match_the_ruleset_table_at_one_cost_unit_of_troops(string unitTypeId)
    {
        var type = Ruleset.UnitTypeById(unitTypeId)!;
        var troops = Ruleset.Recruitment.TroopsPerCostUnit;

        Assert.Equal(type.RecruitCost, StandingRecruitmentCost.InitialCost(troops, unitTypeId, Ruleset));
        Assert.Equal(type.QuarterlyPrice, StandingRecruitmentCost.QuarterlyCost(troops, unitTypeId, Ruleset));
    }

    /// <summary>
    /// <see cref="StandingRecruitmentCost.QuarterlyCost"/> and T08/T39's <see cref="ArmyUpkeep.ComputeUnit(UnitSlot, Ruleset)"/>
    /// read the same confirmed quarterly-price table the same way for a regular unit; they must never
    /// diverge, since <c>decompiled-recruitment-cost-formula.md</c> confirms it is one shared table.
    /// </summary>
    [Fact]
    public void QuarterlyCost_agrees_with_ArmyUpkeep_for_a_regular_unit()
    {
        var unit = new UnitSlot(MercenaryLabel: 0, UnitTypeId: "heavy_cavalry", Troops: 2500, Quality: 6, Name: "Test");

        var viaRecruitment = StandingRecruitmentCost.QuarterlyCost(unit.Troops, unit.UnitTypeId, Ruleset);
        var viaUpkeep = ArmyUpkeep.ComputeUnit(unit, Ruleset);

        Assert.Equal(viaUpkeep, viaRecruitment);
    }

    [Fact]
    public void Unknown_unit_type_throws()
    {
        Assert.Throws<ArgumentException>(() => StandingRecruitmentCost.InitialCost(1000, "no-such-type", Ruleset));
        Assert.Throws<ArgumentException>(() => StandingRecruitmentCost.QuarterlyCost(1000, "no-such-type", Ruleset));
    }
}
