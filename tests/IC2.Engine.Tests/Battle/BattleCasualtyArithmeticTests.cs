using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Battle;

/// <summary>
/// Done-when 3's "integer semantics pinned" clause, exercised directly on
/// <see cref="BattleCasualties"/> rather than only through a whole battle: the ratio's own arithmetic,
/// and the per-unit expression the ratio is fed into.
/// </summary>
public class BattleCasualtyArithmeticTests
{
    /// <summary>
    /// <c>loserPower × numerator / winnerPower</c>: the multiplication first, then exactly one truncating
    /// division. The alternative grouping deletes the ratio entirely for any realistic power.
    /// </summary>
    [Theory]
    [InlineData(3600, 6120, 23)]   // 144,000 / 6,120 = 23.52 -> 23
    [InlineData(6120, 6120, 40)]   // an even fight passes the numerator itself
    [InlineData(1, 6120, 0)]       // a hopeless loser costs the winner nothing at all
    [InlineData(6119, 6120, 39)]   // 244,760 / 6,120 = 39.99 -> 39, not 40: truncation, not rounding
    public void TheRatioTruncatesOnceAfterMultiplying(int loserPower, int winnerPower, int expected)
    {
        var numerator = BattleTestbed.Destroyed.Combat.WinnerCasualtyNumerator;
        Assert.Equal(expected, BattleCasualties.Ratio(loserPower, winnerPower, numerator));
        Assert.Equal((loserPower * numerator) / winnerPower, BattleCasualties.Ratio(loserPower, winnerPower, numerator));
    }

    /// <summary>
    /// The grouping that would be wrong, stated on its own: <c>loserPower × (numerator / winnerPower)</c>
    /// truncates the inner quotient to zero for every <c>winnerPower</c> above the numerator, so it would
    /// make the winner of any real battle invulnerable.
    /// </summary>
    [Fact]
    public void TheOtherGroupingWouldDeleteTheRatioEntirely()
    {
        var numerator = BattleTestbed.Destroyed.Combat.WinnerCasualtyNumerator;

        Assert.Equal(0, numerator / 6120);
        Assert.Equal(0, 3600 * (numerator / 6120));
        Assert.Equal(23, BattleCasualties.Ratio(3600, 6120, numerator));
    }

    /// <summary>
    /// Two forces of no strength at all divide by nothing rather than throwing, and cost each other
    /// nothing: the numerator is checked before the divisor, so <c>0 / 0</c> resolves to "no casualties".
    /// </summary>
    [Fact]
    public void TwoStrengthlessForcesCostEachOtherNothing()
    {
        Assert.Equal(0, BattleCasualties.Ratio(0, 0, BattleTestbed.Destroyed.Combat.WinnerCasualtyNumerator));
    }

    /// <summary>
    /// Cloud-review finding 1. A zero <em>divisor</em> with a positive numerator is an unbounded ratio,
    /// not a zero one, and saturates.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the mirrored call's degenerate case and it is the opposite of the forward call's.
    /// <see cref="BattleCasualties.Ratio"/> is called <c>Ratio(loserPower, winnerPower, ...)</c> for what
    /// the winner pays and <c>Ratio(winnerPower, loserPower, ...)</c> for the <c>improved</c> ruleset's
    /// mirrored figure, so the losing side's strength lands in the divisor on the second call — and
    /// <see cref="IC2.Engine.Strength.ArmyPower.Compute"/> truncates to zero for any force whose weighted troops
    /// fall below <see cref="CombatRules.PowerDivisor"/>.
    /// </para>
    /// <para>
    /// A guard that returned zero there — as this code did until the cloud review — inverted the rule
    /// exactly: the weakest possible loser took <em>no</em> casualties instead of near-total ones.
    /// </para>
    /// </remarks>
    [Fact]
    public void AZeroDivisorSaturatesRatherThanReturningZero()
    {
        var numerator = BattleTestbed.Destroyed.Combat.WinnerCasualtyNumerator;

        Assert.Equal(int.MaxValue, BattleCasualties.Ratio(5100, 0, numerator));
        Assert.NotEqual(0, BattleCasualties.Ratio(5100, 0, numerator));

        // And the saturating ratio really does take everything, slot by slot, without overflowing.
        var units = ValueList.Of(
            BattleTestbed.Unit("light_infantry", 300, 6, "Remnant"),
            BattleTestbed.Unit("heavy_infantry", 12000, 6, "Bigger Remnant"));

        var (reduced, _, applied) = BattleCasualties.Apply(
            units, BattleCasualties.Ratio(5100, 0, numerator), BattleTestbed.BattleRng(),
            BattleTestbed.Destroyed.Combat);

        Assert.Equal(12300, applied);
        Assert.Equal(new[] { 0, 0 }, reduced.Select(u => u.Troops).ToArray());
        Assert.Equal(units.Count, reduced.Count);
    }

    /// <summary>
    /// The power floor the finding turns on is real and is reached by an ordinary small army: a
    /// 300-strong light-infantry unit weighs 60, which is below the ruleset's own
    /// <see cref="CombatRules.PowerDivisor"/> of 80, so its strength truncates to zero however good its
    /// morale is.
    /// </summary>
    [Fact]
    public void ASmallDepletedArmyGenuinelyHasZeroPower()
    {
        var ruleset = BattleTestbed.Destroyed;
        var units = ValueList.Of(BattleTestbed.Unit("light_infantry", 300, 6, "Remnant"));

        Assert.Equal(0, IC2.Engine.Strength.ArmyPower.Compute(units, 68, ruleset));
        Assert.True((20 * 300 / ruleset.Combat.PowerTroopDivisor) < ruleset.Combat.PowerDivisor);
    }

    /// <summary>
    /// The per-unit expression, with every input fixed: <c>troops / (draw + base) × ratio</c>, dividing
    /// first and multiplying second.
    /// </summary>
    /// <remarks>
    /// The divisor draw is scripted here rather than taken from the seeded generator, so that the two
    /// truncations can be separated from each other and from the draw itself.
    /// </remarks>
    [Theory]
    [InlineData(12000, 13, 23, 2323)]   // 12,000 / 118 = 101; 101 x 23
    [InlineData(4000, 5, 23, 828)]      //  4,000 / 110 =  36;  36 x 23
    [InlineData(2000, 11, 23, 391)]     //  2,000 / 116 =  17;  17 x 23
    [InlineData(100, 0, 40, 0)]         // a slot smaller than the divisor loses nothing at all
    [InlineData(12000, 0, 40, 4560)]    // the kindest divisor, 105: 114 x 40
    [InlineData(12000, 14, 40, 4000)]   // the harshest, 119: 100 x 40
    public void ThePerUnitExpressionDividesFirstAndMultipliesSecond(int troops, int draw, int ratio, int expected)
    {
        var rules = BattleTestbed.Destroyed.Combat;
        var units = ValueList.Of(BattleTestbed.Unit("light_infantry", troops, 6, "Only"));

        var (reduced, losses, applied) = BattleCasualties.Apply(
            units, ratio, new ScriptedDivisorRng(draw), rules);

        var divisor = draw + rules.CasualtyDivisorBase;
        Assert.Equal(expected, (troops / divisor) * ratio);
        Assert.Equal(expected, applied);
        Assert.Equal(troops - expected, reduced[0].Troops);
        Assert.Equal(expected == 0 ? 0 : 1, losses.Count);
    }

    /// <summary>
    /// The two groupings are genuinely different numbers, which is why the order is pinned rather than
    /// left to taste.
    /// </summary>
    [Fact]
    public void DividingFirstIsNotTheSameAsMultiplyingFirst()
    {
        const int Troops = 2000;
        const int Divisor = 109;
        const int Ratio = 23;

        Assert.Equal(414, (Troops / Divisor) * Ratio);
        Assert.Equal(422, (Troops * Ratio) / Divisor);
    }

    /// <summary>The divisor is drawn from the ruleset's own band, once per slot, in slot order.</summary>
    [Fact]
    public void OneDivisorIsDrawnPerSlotFromTheRulesetsBand()
    {
        var rules = BattleTestbed.Destroyed.Combat;
        Assert.Equal(105, rules.CasualtyDivisorBase);
        Assert.Equal(15, rules.CasualtyDivisorRandomSpan);

        var units = ValueList.Of(
            BattleTestbed.Unit("light_infantry", 12000, 5, "A"),
            BattleTestbed.Unit("heavy_infantry", 4000, 6, "B"),
            BattleTestbed.Unit("archers", 2000, 9, "C"));

        var recorder = new BoundRecordingRng(new SplitMix64Rng(BattleTestbed.Seed));
        BattleCasualties.Apply(units, 23, recorder, rules);

        Assert.Equal(
            new[] { rules.CasualtyDivisorRandomSpan, rules.CasualtyDivisorRandomSpan, rules.CasualtyDivisorRandomSpan },
            recorder.Bounds);
        Assert.Equal(new[] { 13, 5, 11 }, recorder.Values);
        Assert.All(recorder.Values, v => Assert.InRange(v, 0, rules.CasualtyDivisorRandomSpan - 1));
    }

    /// <summary>
    /// The draw is unconditional, so a slot already at zero troops still consumes one — which is what
    /// keeps the sequence a function of the slot count alone.
    /// </summary>
    [Fact]
    public void AZeroTroopSlotStillConsumesItsDraw()
    {
        var rules = BattleTestbed.Destroyed.Combat;
        var units = ValueList.Of(
            BattleTestbed.Unit("light_infantry", 0, 6, "Wiped"),
            BattleTestbed.Unit("light_infantry", 12000, 6, "Alive"));

        var recorder = new BoundRecordingRng(new SplitMix64Rng(BattleTestbed.Seed));
        var (reduced, _, applied) = BattleCasualties.Apply(units, 23, recorder, rules);

        Assert.Equal(2, recorder.Values.Count);
        Assert.Equal(0, reduced[0].Troops);

        // The live slot used the SECOND draw (5, so divisor 110), not the first.
        Assert.Equal((12000 / 110) * 23, applied);
        Assert.Equal(2, reduced.Count);
    }

    /// <summary>
    /// The only clamp is non-negativity: a ratio past the divisor takes the whole slot and stops, and the
    /// slot is still not removed.
    /// </summary>
    [Fact]
    public void ARatioPastTheDivisorTakesTheWholeSlotAndNoMore()
    {
        var rules = BattleTestbed.Destroyed.Combat;
        var units = ValueList.Of(BattleTestbed.Unit("light_infantry", 12000, 6, "Doomed"));

        var (reduced, losses, applied) = BattleCasualties.Apply(units, 5000, new ScriptedDivisorRng(0), rules);

        Assert.Equal(12000, applied);
        Assert.Equal(0, reduced[0].Troops);
        Assert.Single(reduced);
        Assert.Equal(12000, losses[0].TroopsLost);
    }

    /// <summary>
    /// Both halves of the formula are transcribed, not invented: the ratio from the call site's report,
    /// the per-unit body from the corpus entry for <c>FUN_0044AE20</c>.
    /// </summary>
    [Fact]
    public void BothHalvesOfTheFormulaMatchTheTranscribedSources()
    {
        Assert.Equal(
            "winner casualties = loserPower * 40 / winnerPower",
            FixtureCorpus.Get("battle.instantResolver.casualtyFormula").AsString());
        Assert.Equal(40, BattleTestbed.Destroyed.Combat.WinnerCasualtyNumerator);

        var body = FixtureCorpus.Get("siege.attritionFormula");
        Assert.Equal("troops -= troops / (Random(15) + 105) * ratio", body.AsString());
        Assert.Equal("confirmed", body.Tag);
        Assert.Contains("FUN_0044ae20", body.Note!, StringComparison.OrdinalIgnoreCase);

        // The two numbers in that transcription are exactly the two ruleset fields this task added.
        Assert.Equal(15, BattleTestbed.Destroyed.Combat.CasualtyDivisorRandomSpan);
        Assert.Equal(105, BattleTestbed.Destroyed.Combat.CasualtyDivisorBase);
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
            BattleTestbed.Unit("archers", 100, combat.QualityCap, "Elite"),
            BattleTestbed.Unit("archers", 100, combat.QualityFloor, "Average"));

        var (promoted, report) = BattleCasualties.Promote(units, BattleTestbed.BattleRng(), combat);

        // The wiped slot is untouched and unrolled, so the seed's first three draws land on "Green",
        // "Elite" and "Average" in that order.
        Assert.Equal(1, promoted[0].Quality);
        Assert.Equal(0, promoted[0].Troops);
        Assert.Equal(combat.QualityFloor, promoted[1].Quality);
        Assert.Equal(combat.QualityCap, promoted[2].Quality);

        Assert.Equal(new[] { 1, 2, 3 }, report.Select(p => p.SlotIndex).ToArray());
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

    /// <summary>An <see cref="IRng"/> that always returns the same casualty-divisor draw.</summary>
    private sealed class ScriptedDivisorRng : IRng
    {
        private readonly int _draw;

        public ScriptedDivisorRng(int draw) => _draw = draw;

        public ulong Seed => 0;

        public ulong State => 0;

        public ulong NextUInt64() => throw new NotSupportedException("Not scripted for this test.");

        public int NextInt(int exclusiveUpperBound) => _draw;

        public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound) =>
            throw new NotSupportedException("Not scripted for this test.");

        public bool NextChance(int numerator, int denominator) =>
            throw new NotSupportedException("Not scripted for this test.");

        public IRng ForStream(string streamName) => this;
    }

    /// <summary>A pass-through <see cref="IRng"/> that records the bound and value of each draw.</summary>
    private sealed class BoundRecordingRng : IRng
    {
        private readonly IRng _inner;

        public BoundRecordingRng(IRng inner) => _inner = inner;

        public List<int> Bounds { get; } = new();

        public List<int> Values { get; } = new();

        public ulong Seed => _inner.Seed;

        public ulong State => _inner.State;

        public ulong NextUInt64() => _inner.NextUInt64();

        public int NextInt(int exclusiveUpperBound)
        {
            var value = _inner.NextInt(exclusiveUpperBound);
            Bounds.Add(exclusiveUpperBound);
            Values.Add(value);
            return value;
        }

        public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound) =>
            _inner.NextInt(inclusiveLowerBound, exclusiveUpperBound);

        public bool NextChance(int numerator, int denominator) => _inner.NextChance(numerator, denominator);

        public IRng ForStream(string streamName) => _inner.ForStream(streamName);
    }
}
