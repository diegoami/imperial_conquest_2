using IC2.Engine.Model;
using IC2.Engine.Strength;
using Xunit;

namespace IC2.Engine.Tests.Strength;

/// <summary>
/// <c>docs/build-orchestration-plan.md</c> "T07 Strength functions", Done-when 1, 5 (two of the three
/// required cases) and 6.
/// </summary>
public sealed class ArmyPowerTests
{
    /// <summary>
    /// Done-when 1: reproduces a hand-computed value for the published 13-unit Roman roster, using the
    /// <c>+0x26</c> power weights (via the ruleset, which T04's corpus and the shipped
    /// <c>toy-ruleset.json</c> both source from <c>decompiled-unit-map-orders-and-record-fields.md</c>).
    /// </summary>
    /// <remarks>
    /// Hand computation (weights: light_infantry 20, heavy_infantry 100, light_cavalry 60,
    /// heavy_cavalry 120 -- all confirmed <c>+0x26</c>; morale 59, the confirmed fresh-army starting
    /// value, <see cref="ArmyManagementRules.NewArmyMorale"/>, so even the morale input traces to
    /// evidence rather than being an arbitrary literal):
    /// <code>
    /// unit                 weight   troops   weight*troops   /100 (truncated per unit, per FUN_0044A8CC)
    /// light_infantry(1)    20       4210     84200           842
    /// heavy_infantry(1)    100      4900     490000          4900
    /// heavy_infantry(2)    100      4920     492000          4920
    /// heavy_infantry(3)    100      5747     574700          5747
    /// heavy_cavalry(1)     120      774      92880           928   (928.8 truncated)
    /// heavy_infantry(4)    100      2583     258300          2583
    /// heavy_cavalry(2)     120      1539     184680          1846  (1846.8 truncated)
    /// light_cavalry(1)     60       900      54000           540
    /// heavy_infantry(5)    100      4787     478700          4787
    /// heavy_infantry(6)    100      3571     357100          3571
    /// heavy_infantry(7)    100      5300     530000          5300
    /// heavy_infantry(8)    100      3442     344200          3442
    /// light_infantry(2)    20       5500     110000          1100
    /// sum S = 40,506
    /// S / 80 (truncated) = 506
    /// armyPower = 506 * 59 = 29,854
    /// </code>
    /// </remarks>
    [Fact]
    public void Compute_Roman13UnitRoster_MatchesHandComputedValue()
    {
        var ruleset = StrengthTestbed.Ruleset;
        const int morale = 59; // ArmyManagementRules.NewArmyMorale -- a cited value, not a literal choice.
        Assert.Equal(morale, ruleset.ArmyManagement.NewArmyMorale);

        var totalTroops = StrengthTestbed.Roman13UnitRoster.Sum(u => u.Troops);
        Assert.Equal(48_173, totalTroops); // roman13.troops in the T04 corpus.

        var power = ArmyPower.Compute(StrengthTestbed.Roman13UnitRoster, morale, ruleset);

        Assert.Equal(29_854, power);
    }

    /// <summary>
    /// Done-when 5 (case 1 of 3): the per-unit <c>/ 100</c> truncation happens inside the accumulation
    /// loop (per <c>FUN_0044A8CC</c>), not once on the grand total, and the difference is visible in the
    /// final result, not just an intermediate. Three units:
    /// <code>
    /// heavy_infantry, weight 100, troops 79   -> (100*79)/100  = 7900/100 = 79   (exact)
    /// light_infantry, weight 20,  troops 3    -> (20*3)/100    = 60/100   = 0.6 -> 0 (truncated)
    /// light_infantry, weight 20,  troops 3    -> (20*3)/100    = 60/100   = 0.6 -> 0 (truncated)
    ///
    /// per-unit truncation (correct, FUN_0044A8CC's actual order): S = 79 + 0 + 0 = 79
    /// sum-first truncation (the plausible-looking alternative):   S = floor((7900+60+60)/100)
    ///                                                                = floor(8020/100) = 80
    /// </code>
    /// 79 and 80 straddle the <c>/ 80</c> divisor exactly: <c>79 / 80 = 0</c> but <c>80 / 80 = 1</c>, so
    /// with the sum-first (wrong) order this army would report a nonzero power at any morale, while the
    /// correct, per-unit order reports zero. <c>ArmyPower.Compute</c> must match the latter.
    /// </summary>
    [Fact]
    public void Compute_PerUnitTruncation_DiffersFromSumFirstTruncation()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var units = new[]
        {
            StrengthTestbed.Unit("heavy_infantry", 79),
            StrengthTestbed.Unit("light_infantry", 3),
            StrengthTestbed.Unit("light_infantry", 3),
        };

        var perUnitSum = (100 * 79 / 100) + (20 * 3 / 100) + (20 * 3 / 100);
        var sumFirst = ((100 * 79) + (20 * 3) + (20 * 3)) / 100;
        Assert.Equal(79, perUnitSum);
        Assert.Equal(80, sumFirst);
        Assert.NotEqual(perUnitSum, sumFirst);

        var power = ArmyPower.Compute(units, morale: 51, ruleset);

        // (79 / 80) * 51 = 0. The sum-first alternative would give (80 / 80) * 51 = 51.
        Assert.Equal(0, power);

        // Parity with the other two DoD 5 cases: also pin against the real-number evaluation directly
        // (round-1 review finding) -- (8020 / 100.0 / 80.0) * 51 = 51.1275 -> rounds to 51, not 0.
        Assert.NotEqual((int)Math.Round(8020 / 100.0 / 80.0 * 51), power);
    }

    /// <summary>
    /// Done-when 5 (case 2 of 3): the second truncation point, <c>S / 80</c> before multiplying by
    /// morale, also diverges from evaluating the whole expression as real numbers and rounding once at
    /// the end. One unit, light_infantry (weight 20), troops 425: <c>(20*425)/100 = 8500/100 = 85</c>
    /// exactly (no ambiguity at the first division), so <c>S = 85</c> cleanly isolates the second one.
    /// <code>
    /// truncated:    (85 / 80) * 51 = 1 * 51 = 51
    /// real-then-round: (85 / 80.0) * 51 = 1.0625 * 51 = 54.1875 -> 54
    /// </code>
    /// </summary>
    [Fact]
    public void Compute_FinalDivisionTruncation_DiffersFromRealDivision()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var units = new[] { StrengthTestbed.Unit("light_infantry", 425) };

        var power = ArmyPower.Compute(units, morale: 51, ruleset);

        Assert.Equal(51, power);
        Assert.NotEqual((int)Math.Round(85 / 80.0 * 51), power);
    }

    /// <summary>
    /// Done-when 6: <c>armyPower</c> is exercised across the full confirmed 51...70 morale range
    /// (<c>docs/design-audit.md</c> §2.9a; <c>docs/investigations/thracia-supply-morale.md</c>) and at
    /// both bounds, and is monotonic in morale over that range. This function does not clamp or
    /// otherwise validate morale -- the range is exercised, not enforced.
    /// </summary>
    [Fact]
    public void Compute_AcrossConfirmedMoraleRange_IsMonotonicAndCoversBothBounds()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var values = new List<int>();

        for (var morale = 51; morale <= 70; morale++)
        {
            values.Add(ArmyPower.Compute(StrengthTestbed.Roman13UnitRoster, morale, ruleset));
        }

        Assert.Equal(20, values.Count);

        for (var i = 1; i < values.Count; i++)
        {
            Assert.True(
                values[i] > values[i - 1],
                $"armyPower must strictly increase with morale: values[{i - 1}]={values[i - 1]}, values[{i}]={values[i]}.");
        }

        // Both bounds explicitly, not just "somewhere in the loop".
        var atFloor = ArmyPower.Compute(StrengthTestbed.Roman13UnitRoster, 51, ruleset);
        var atCeiling = ArmyPower.Compute(StrengthTestbed.Roman13UnitRoster, 70, ruleset);
        Assert.Equal(506 * 51, atFloor);
        Assert.Equal(506 * 70, atCeiling);
        Assert.True(atFloor < atCeiling);
    }

    [Fact]
    public void Compute_EmptyArmy_IsZero()
    {
        var power = ArmyPower.Compute(Array.Empty<UnitSlot>(), morale: 70, StrengthTestbed.Ruleset);
        Assert.Equal(0, power);
    }

    [Fact]
    public void Compute_UnknownUnitType_Throws()
    {
        var units = new[] { StrengthTestbed.Unit("not_a_real_type", 100) };
        Assert.Throws<ArgumentException>(() => ArmyPower.Compute(units, 60, StrengthTestbed.Ruleset));
    }
}
