using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Battle;

/// <summary>
/// Done-when 3's "integer semantics pinned" clause, and the distribution rule the figure is applied
/// through, exercised directly on <see cref="BattleCasualties"/> rather than only through a whole
/// battle.
/// </summary>
public class BattleCasualtyArithmeticTests
{
    /// <summary>
    /// <c>loserPower × numerator / winnerPower</c>: the multiplication first, then exactly one truncating
    /// division. The alternative grouping deletes the casualties entirely for any realistic power.
    /// </summary>
    [Theory]
    [InlineData(3600, 6120, 23)]   // 144,000 / 6,120 = 23.52 -> 23
    [InlineData(6120, 6120, 40)]   // an even fight costs the winner the numerator itself
    [InlineData(1, 6120, 0)]       // a hopeless loser costs the winner nothing at all
    [InlineData(6119, 6120, 39)]   // 244,760 / 6,120 = 39.99 -> 39, not 40: truncation, not rounding
    public void TheCasualtyFigureTruncatesOnceAfterMultiplying(int loserPower, int winnerPower, int expected)
    {
        var numerator = BattleTestbed.Destroyed.Combat.WinnerCasualtyNumerator;
        Assert.Equal(expected, BattleCasualties.Count(loserPower, winnerPower, numerator));
        Assert.Equal((loserPower * numerator) / winnerPower, BattleCasualties.Count(loserPower, winnerPower, numerator));
    }

    /// <summary>
    /// The grouping that would be wrong, stated on its own: <c>loserPower × (numerator / winnerPower)</c>
    /// truncates the inner quotient to zero for every <c>winnerPower</c> above the numerator, so it would
    /// make the winner of any real battle invulnerable.
    /// </summary>
    [Fact]
    public void TheOtherGroupingWouldDeleteTheCasualtiesEntirely()
    {
        var numerator = BattleTestbed.Destroyed.Combat.WinnerCasualtyNumerator;

        Assert.Equal(0, numerator / 6120);
        Assert.Equal(0, 3600 * (numerator / 6120));
        Assert.Equal(23, BattleCasualties.Count(3600, 6120, numerator));
    }

    /// <summary>Two empty armies divide by nothing rather than throwing.</summary>
    [Fact]
    public void AZeroPowerWinnerCostsNothingRatherThanDividingByZero()
    {
        Assert.Equal(0, BattleCasualties.Count(0, 0, BattleTestbed.Destroyed.Combat.WinnerCasualtyNumerator));
    }

    /// <summary>
    /// The figure the resolver applies is the one the T04 corpus transcribes from the decompilation:
    /// <c>winner casualties = loserPower * 40 / winnerPower</c>, with the numerator the shipped ruleset
    /// carries.
    /// </summary>
    [Fact]
    public void TheNumeratorMatchesTheTranscribedFormula()
    {
        Assert.Equal(
            "winner casualties = loserPower * 40 / winnerPower",
            FixtureCorpus.Get("battle.instantResolver.casualtyFormula").AsString());
        Assert.Equal(40, BattleTestbed.Destroyed.Combat.WinnerCasualtyNumerator);
    }

    /// <summary>
    /// The distribution is proportional and exact: the per-slot losses always sum to the figure, and no
    /// slot goes negative.
    /// </summary>
    [Theory]
    [InlineData(23)]
    [InlineData(1)]
    [InlineData(17999)]
    [InlineData(18000)]
    public void TheDistributionSumsExactlyToTheFigureAndNeverGoesNegative(int casualties)
    {
        var units = ValueList.Of(
            BattleTestbed.Unit("light_infantry", 12000, 5, "A"),
            BattleTestbed.Unit("heavy_infantry", 4000, 6, "B"),
            BattleTestbed.Unit("archers", 2000, 9, "C"));

        var (reduced, losses, applied) = BattleCasualties.Distribute(units, casualties);

        Assert.Equal(casualties, applied);
        Assert.Equal(casualties, losses.Sum(l => l.TroopsLost));
        Assert.All(reduced, unit => Assert.True(unit.Troops >= 0));
        Assert.Equal(18000 - casualties, reduced.Sum(u => u.Troops));

        // Slot count is never changed: removing a unit is the rout mechanic, which is reserve research.
        Assert.Equal(units.Count, reduced.Count);
    }

    /// <summary>A figure larger than the force is clamped to the force, not applied past zero.</summary>
    [Fact]
    public void AFigureLargerThanTheForceIsClampedToTheForce()
    {
        var units = ValueList.Of(BattleTestbed.Unit("light_infantry", 1000, 6, "Only"));
        var (reduced, _, applied) = BattleCasualties.Distribute(units, 5000);

        Assert.Equal(1000, applied);
        Assert.Equal(0, reduced[0].Troops);
        Assert.Single(reduced);
    }

    /// <summary>
    /// The promotion rule, exercised on its own: the floor first, then one <c>random(4)</c> per surviving
    /// slot in order, capped. A slot at zero troops is not a survivor — it is neither promoted nor rolled
    /// for, and it is not removed either.
    /// </summary>
    [Fact]
    public void PromotionAppliesTheFloorThenOneRollPerSurvivorInSlotOrder()
    {
        var combat = BattleTestbed.Destroyed.Combat;
        var units = ValueList.Of(
            BattleTestbed.Unit("light_infantry", 0, 1, "Wiped"),
            BattleTestbed.Unit("heavy_infantry", 100, 1, "Green"),
            BattleTestbed.Unit("archers", 100, combat.QualityCap, "Elite"));

        var (promoted, report) = BattleCasualties.Promote(units, BattleTestbed.BattleRng(), combat);

        // The wiped slot is untouched and unrolled, so the seed's first draw (2) lands on "Green" and its
        // second (0) on "Elite".
        Assert.Equal(1, promoted[0].Quality);
        Assert.Equal(0, promoted[0].Troops);
        Assert.Equal(combat.QualityFloor, promoted[1].Quality);
        Assert.Equal(combat.QualityCap, promoted[2].Quality);

        Assert.Equal(new[] { 1, 2 }, report.Select(p => p.SlotIndex).ToArray());
        Assert.Equal(new[] { false, true }, report.Select(p => p.PromotedByRoll).ToArray());
        Assert.Equal(units.Count, promoted.Count);
    }

    /// <summary>
    /// The one promotion rule, and the withdrawn one: the corpus entry that used to carry the tactical
    /// slot-adjacency rule now says so, and this task's resolver implements the uniform roll instead.
    /// </summary>
    [Fact]
    public void TheAdjacencyPromotionRuleIsMarkedWithdrawnInTheCorpus()
    {
        var withdrawn = FixtureCorpus.Get("battle.tactical.adjacencyPromotionRule");
        Assert.StartsWith("WITHDRAWN", withdrawn.AsString(), StringComparison.Ordinal);
        Assert.Contains("battle.instantResolver.promotionChance", withdrawn.AsString(), StringComparison.Ordinal);

        // And its sibling no longer describes the withdrawn rule as a live second rule on a second path.
        var live = FixtureCorpus.Get("battle.instantResolver.promotionChance");
        Assert.Equal("1 in 4", live.AsString());
        Assert.DoesNotContain("a second, separate rule", live.Note!, StringComparison.Ordinal);
        Assert.Contains("withdrawn", live.Note!, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(4, BattleTestbed.Destroyed.Combat.PromotionChanceDenominator);
        Assert.Equal(6, FixtureCorpus.Get("battle.instantResolver.qualityFloor").AsInt());
        Assert.Equal(6, BattleTestbed.Destroyed.Combat.QualityFloor);
    }

    /// <summary>
    /// The Rome/Gaul per-type numbers stay in the corpus as reserve evidence and are <em>not</em> a
    /// target: this resolver annihilates the loser rather than producing a per-type attrition table, so
    /// <see cref="BattleResult"/> has no field that could carry them (<c>design-audit.md</c> Q1).
    /// </summary>
    [Fact]
    public void TheRomeGaulPerTypeNumbersAreNotSomethingThisResolverCanProduce()
    {
        Assert.Equal(99882, FixtureCorpus.Get("battle.romeGaul.attackerTroopsBefore").AsInt());
        Assert.Equal(63282, FixtureCorpus.Get("battle.romeGaul.attackerTroopsAfter").AsInt());

        var perUnitTypeProperties = typeof(BattleResult)
            .GetProperties()
            .Where(p => p.Name.Contains("Type", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(perUnitTypeProperties);
    }
}
