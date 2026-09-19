using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using Xunit;

namespace IC2.Engine.Tests.Diplomacy;

/// <summary>
/// <c>docs/task-catalogue.md</c> T19 DoD 7: <c>reparations = W/4 + random(W/4) + cities × 10</c>, <c>W</c>
/// the loser's tax base, exact under a fixed seed, and the one recorded Ptolemaic payment in range.
/// </summary>
public sealed class ReparationsFormulaTests
{
    /// <summary>
    /// The report's own check, reproduced directly: Ptolemaic, <c>W</c> = 6,188, 48 cities, gives
    /// <c>[2,027, 3,573]</c>, and the observed 2,269 is inside it.
    /// </summary>
    [Fact]
    public void DoD07_PtolemaicRange_MatchesTheReportsCheck()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var (min, max) = ReparationsFormula.Range(loserTaxBase: 6188, loserCityCount: 48, ruleset);

        Assert.Equal(2027, min);
        Assert.Equal(3573, max);
        Assert.InRange(2269, min, max);
    }

    /// <summary>Every draw within the range that <see cref="ReparationsFormula.Compute"/> can produce lands in it.</summary>
    [Fact]
    public void DoD07_Compute_AlwaysLandsWithinItsOwnRange()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var (min, max) = ReparationsFormula.Range(6188, 48, ruleset);
        var rng = DiplomacyTestbed.Rng();

        for (var i = 0; i < 50; i++)
        {
            var value = ReparationsFormula.Compute(6188, 48, ruleset, rng);
            Assert.InRange(value, min, max);
        }
    }

    /// <summary>Exact under a fixed seed: the formula's first draw from a known seed is pinned by value.</summary>
    [Fact]
    public void DoD07_ExactUnderAFixedSeed()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var rng = new SplitMix64Rng(0x1234ABCDUL);

        // W/4 = 1547; the random(1547) draw and the exact sum are pinned below, so a mutation to the
        // formula's shape (order of operations, which divisor multiplies which term) changes this number.
        var reparations = ReparationsFormula.Compute(loserTaxBase: 6188, loserCityCount: 48, ruleset, rng);

        var expectedDraw = new SplitMix64Rng(0x1234ABCDUL).NextInt(1547);
        Assert.Equal(1547 + expectedDraw + 480, reparations);
        Assert.InRange(reparations, 2027, 3573);
    }

    /// <summary>Mutation proof: the tax base term, not city count, drives the flat share.</summary>
    [Fact]
    public void DoD07_MutationProof_UsesTaxBase_NotCityCountAlone_ForTheShare()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var (lowTaxBaseMin, _) = ReparationsFormula.Range(loserTaxBase: 0, loserCityCount: 48, ruleset);
        var (highTaxBaseMin, _) = ReparationsFormula.Range(loserTaxBase: 6188, loserCityCount: 48, ruleset);

        Assert.Equal(480, lowTaxBaseMin);
        Assert.Equal(2027, highTaxBaseMin);
        Assert.NotEqual(lowTaxBaseMin, highTaxBaseMin);
    }

    /// <summary>A tax base under the divisor draws nothing extra rather than throwing.</summary>
    [Fact]
    public void DoD07_ATaxBaseUnderTheDivisor_DrawsNothingExtra_AndDoesNotThrow()
    {
        var ruleset = DiplomacyTestbed.Ruleset;
        var rng = DiplomacyTestbed.Rng();

        var reparations = ReparationsFormula.Compute(loserTaxBase: 2, loserCityCount: 1, ruleset, rng);

        Assert.Equal(0 + 0 + (1 * ruleset.Diplomacy.ReparationsPerCity), reparations);
    }
}
