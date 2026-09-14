using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Tests.Strength;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T08 Economy, supply, and purses", Done-when 3:
/// "The 13-unit Roman roster's regular upkeep computes to exactly <c>442</c>."
/// </summary>
/// <remarks>
/// Reuses <c>StrengthTestbed.Roman13UnitRoster</c> (T07's exact per-unit troop/type transcription of the
/// published roster, already cross-checked against <c>roman13.troops</c> = 48,173) rather than
/// re-transcribing it — the same roster, the same evidence, two different formulas over it.
/// </remarks>
public sealed class ArmyUpkeepTests
{
    /// <summary>
    /// Per-unit reproduction of <c>roman13.regularUpkeepFormulaCheck</c>
    /// (<c>21+48+48+56+12+24+28+12+46+34+52+34+27 = 442</c>) in the fixtures corpus, unit for unit, in
    /// roster order.
    /// </summary>
    private static readonly int[] ExpectedPerUnit = { 21, 48, 48, 56, 12, 24, 28, 12, 46, 34, 52, 34, 27 };

    [Fact]
    public void Compute_Roman13UnitRoster_Reproduces442()
    {
        var ruleset = EconomyTestbed.Ruleset;

        var totalTroops = StrengthTestbed.Roman13UnitRoster.Sum(u => u.Troops);
        Assert.Equal(48_173, totalTroops); // roman13.troops in the T04 corpus.

        var upkeep = ArmyUpkeep.Compute(StrengthTestbed.Roman13UnitRoster, ruleset);

        Assert.Equal(442, upkeep);
    }

    [Fact]
    public void ComputeUnit_Roman13UnitRoster_MatchesThePublishedPerUnitBreakdown()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var roster = StrengthTestbed.Roman13UnitRoster;

        Assert.Equal(ExpectedPerUnit.Length, roster.Count);
        for (var i = 0; i < roster.Count; i++)
        {
            Assert.Equal(ExpectedPerUnit[i], ArmyUpkeep.ComputeUnit(roster[i], ruleset));
        }

        Assert.Equal(442, ExpectedPerUnit.Sum());
    }

    /// <summary>
    /// The Felsina mercenary's recorded 51 talents/quarter (<c>mercenary.felsina.quarterlyCostTalents</c>):
    /// <c>(6438/200) × 1 × 8 / 5 = 32 × 8 / 5 = 51</c>, truncated at each step.
    /// </summary>
    [Fact]
    public void ComputeUnit_FelsinaMercenary_Reproduces51TalentsPerQuarter()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var felsina = new UnitSlot(
            MercenaryLabel: 11,
            UnitTypeId: "light_infantry",
            Troops: 6438,
            Quality: 8,
            Name: "Gallic Mercenary");

        Assert.True(felsina.IsMercenary);
        Assert.Equal(51, ArmyUpkeep.ComputeUnit(felsina, ruleset));
    }

    [Fact]
    public void ComputeUnit_RegularVersusMercenary_UseDifferentFormulas()
    {
        var ruleset = EconomyTestbed.Ruleset;
        var regular = StrengthTestbed.Unit("light_infantry", 6438, quality: 8);
        var mercenary = new UnitSlot(11, "light_infantry", 6438, 8, "Mercenary");

        var regularUpkeep = ArmyUpkeep.ComputeUnit(regular, ruleset);
        var mercenaryUpkeep = ArmyUpkeep.ComputeUnit(mercenary, ruleset);

        Assert.NotEqual(regularUpkeep, mercenaryUpkeep);
        Assert.Equal(32, regularUpkeep); // (6438/200) * 1, the regular formula, ignoring quality entirely.
        Assert.Equal(51, mercenaryUpkeep); // 32 * 8 / 5, the mercenary formula.
    }

    [Fact]
    public void Compute_EmptyRoster_IsZero() =>
        Assert.Equal(0, ArmyUpkeep.Compute(Array.Empty<UnitSlot>(), EconomyTestbed.Ruleset));

    [Fact]
    public void ComputeUnit_UnknownUnitType_Throws()
    {
        var unit = StrengthTestbed.Unit("not_a_real_type", 100);
        Assert.Throws<ArgumentException>(() => ArmyUpkeep.ComputeUnit(unit, EconomyTestbed.Ruleset));
    }
}
