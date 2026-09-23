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
    /// T52 DoD 1: <see cref="BattleCasualties.ApplyToFleet"/> multiplies the fleet's own hull count into
    /// the mirrored ratio <em>before</em> dividing by the drawn divisor -- the opposite grouping from
    /// <see cref="BattleCasualties.Apply"/>'s per-unit expression, and deliberately so.
    /// </summary>
    [Fact]
    public void DoD01_ApplyToFleetMultipliesTheHullCountBeforeDividing()
    {
        var rules = BattleTestbed.Destroyed.Combat;

        // 100 hulls, ratio 47 (T16's own DoD10 naval fixture: 1300 x 40 / 1100 = 47), divisor 105
        // (draw 0): multiply first gives (100 x 47) / 105 = 44.
        var lost = BattleCasualties.ApplyToFleet(100, 47, new ScriptedDivisorRng(0), rules);
        Assert.Equal(44, lost);
        Assert.Equal((100 * 47) / 105, lost);

        // The OTHER grouping -- divide first, as Apply's per-unit expression correctly does for its own,
        // different, confirmed source -- would truncate every fleet in this game to zero, because no
        // fleet has more hulls than the divisor's own range [105, 120).
        Assert.Equal(0, 100 / 105);
        Assert.Equal(0, (100 / 105) * 47);
    }

    /// <summary>
    /// T52 DoD 2: the 40-hull cliff is gone. A 10-hull and a 100-hull fleet beaten by the same margin --
    /// the same mirrored ratio, drawn against the same divisor -- lose proportionally similar fractions of
    /// their own size, not a flat floor.
    /// </summary>
    [Fact]
    public void DoD02_SmallAndLargeFleetsBeatenByTheSameMarginLoseProportionalFractions()
    {
        var rules = BattleTestbed.Destroyed.Combat;
        const int Ratio = 47; // T16's own DoD10 naval fixture: 1300 x 40 / 1100 = 47.

        var smallLost = BattleCasualties.ApplyToFleet(10, Ratio, new ScriptedDivisorRng(0), rules);
        var largeLost = BattleCasualties.ApplyToFleet(100, Ratio, new ScriptedDivisorRng(0), rules);

        // divisor 105 (draw 0): (10 x 47) / 105 = 4 (40% of the fleet); (100 x 47) / 105 = 44 (44%).
        Assert.Equal(4, smallLost);
        Assert.Equal(44, largeLost);
        Assert.Equal(0.4, smallLost / 10.0);
        Assert.Equal(0.44, largeLost / 100.0);

        // The cliff this fixes: under T16's own hull-COUNT reading, min(ships, ratio), this SAME ratio
        // (47, at or above the mirrored figure's own floor of 40) annihilated the 10-hull fleet OUTRIGHT
        // -- min(10, 47) = 10, its entire strength -- while the 100-hull fleet lost only min(100, 47),
        // 47% of itself. The two fractions were nowhere near each other; now they are.
        Assert.Equal(10, Math.Min(10, Ratio));
        Assert.Equal(47, Math.Min(100, Ratio));
        Assert.True(smallLost < 10, "the 10-hull fleet must no longer be annihilated outright by this ratio");
    }

    /// <summary>
    /// T52 DoD 5, the two-entity probe: the same 300 troops, once as a single 300-troop slot the OLD code
    /// already annihilated correctly (<c>300 / 105 = 2</c>, nonzero, so the saturating ratio was never
    /// truncated to zero), and once split into three 100-troop slots, where <c>100 / 105 = 0</c> truncated
    /// the loss to zero <em>before</em> the saturating value could apply -- exactly T16's reviewer's own
    /// proof: <em>"loser SURVIVED. fate=Scattered, casualties=0, troops left=300"</em>.
    /// </summary>
    [Fact]
    public void DoD05_TheSaturatingRatioTakesTheWholeSlotEvenWhenTroopsAreBelowTheDivisor()
    {
        var rules = BattleTestbed.Destroyed.Combat;

        // Unbounded: a zero divisor power with a positive numerator (T16's own zero-strength-loser case).
        var saturating = BattleCasualties.Ratio(5100, 0, rules.WinnerCasualtyNumerator);
        Assert.Equal(int.MaxValue, saturating);

        var oneSlot = ValueList.Of(BattleTestbed.Unit("light_infantry", 300, 6, "Whole"));
        var threeSlots = ValueList.Of(
            BattleTestbed.Unit("light_infantry", 100, 6, "A"),
            BattleTestbed.Unit("light_infantry", 100, 6, "B"),
            BattleTestbed.Unit("light_infantry", 100, 6, "C"));

        var (reducedOne, _, appliedOne) =
            BattleCasualties.Apply(oneSlot, saturating, new ScriptedDivisorRng(0), rules);
        var (reducedThree, _, appliedThree) =
            BattleCasualties.Apply(threeSlots, saturating, new ScriptedDivisorRng(0), rules);

        // The single-slot case was already correct and must stay correct.
        Assert.Equal(300, appliedOne);
        Assert.Equal(0, reducedOne[0].Troops);

        // The three-slot case is the one this fix repairs: every slot is now annihilated too, the same
        // total, split differently -- not left "surviving untouched" the way T16's reviewer found it.
        Assert.Equal(300, appliedThree);
        Assert.Equal(new[] { 0, 0, 0 }, reducedThree.Select(u => u.Troops).ToArray());
        Assert.Equal(appliedOne, appliedThree);
    }

    /// <summary>
    /// Both halves of the formula are transcribed, not invented: the ratio from the call site's report,
    /// the per-unit body from the corpus entry for <c>FUN_0044AE20</c>.
    /// </summary>
    /// <remarks>
    /// T52 DoD 10: <c>value</c> now reads as the ratio it is, not as a troop-count assignment -- the exact
    /// wording that sent T16's first implementation and its first review down the count-reading path.
    /// Pinned to the exact string, not a substring: a reviewer weakening this to
    /// <c>Assert.Contains</c> would let the entry drift back toward the count reading unnoticed.
    /// </remarks>
    [Fact]
    public void BothHalvesOfTheFormulaMatchTheTranscribedSources()
    {
        var ratioEntry = FixtureCorpus.Get("battle.instantResolver.casualtyFormula");
        Assert.Equal(
            "winner casualty ratio = loserPower * 40 / winnerPower",
            ratioEntry.AsString());
        Assert.Contains("RATIO ARGUMENT", ratioEntry.Note!, StringComparison.Ordinal);
        Assert.Contains("not a troop count", ratioEntry.Note!, StringComparison.Ordinal);
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

    /// <summary>
    /// T63 DoD 1 (bug #289): the deletion pass's boundary for a NATIONAL unit -- exactly at
    /// <c>standardBattalionSize / deletionDivisorNational</c> survives, one troop below is deleted.
    /// light_infantry's battalion size is 15,000, so the national threshold is 1,500.
    /// </summary>
    [Fact]
    public void DeleteBelowThreshold_NationalBoundary_ExactlySurvivesOneBelowIsDeleted()
    {
        var ruleset = BattleTestbed.Destroyed;
        var units = ValueList.Of(
            BattleTestbed.Unit("light_infantry", 1500, 6, "AtThreshold"),
            BattleTestbed.Unit("light_infantry", 1499, 6, "OneBelow"));

        var survivors = BattleCasualties.DeleteBelowThreshold(units, ruleset);

        Assert.Equal(new[] { "AtThreshold" }, survivors.Select(u => u.Name).ToArray());
    }

    /// <summary>
    /// T63 DoD 1 (bug #289): the same boundary for a MERCENARY unit (origin label &gt; 0), which gets
    /// twice the national unit's tolerance -- <c>standardBattalionSize / deletionDivisorMercenary</c>,
    /// 3,000 for light infantry.
    /// </summary>
    [Fact]
    public void DeleteBelowThreshold_MercenaryBoundary_ExactlySurvivesOneBelowIsDeleted()
    {
        var ruleset = BattleTestbed.Destroyed;
        var units = ValueList.Of(
            new UnitSlot(MercenaryLabel: 7, UnitTypeId: "light_infantry", Troops: 3000, Quality: 6, Name: "AtThreshold"),
            new UnitSlot(MercenaryLabel: 7, UnitTypeId: "light_infantry", Troops: 2999, Quality: 6, Name: "OneBelow"));

        var survivors = BattleCasualties.DeleteBelowThreshold(units, ruleset);

        Assert.Equal(new[] { "AtThreshold" }, survivors.Select(u => u.Name).ToArray());
    }

    /// <summary>
    /// The deletion pass's own guard requires <c>troops &gt; 0</c> -- a slot <see cref="BattleCasualties.Apply"/>
    /// already reduced to zero is left in place by this pass, exactly as the decompiled loop's own guard
    /// requires (it is simply never a survivor for <see cref="BattleCasualties.Promote"/>'s purposes
    /// either, on that method's own separate terms).
    /// </summary>
    [Fact]
    public void DeleteBelowThreshold_AZeroTroopSlotIsNotRemoved()
    {
        var ruleset = BattleTestbed.Destroyed;
        var units = ValueList.Of(BattleTestbed.Unit("light_infantry", 0, 6, "Empty"));

        var survivors = BattleCasualties.DeleteBelowThreshold(units, ruleset);

        Assert.Single(survivors);
        Assert.Equal(0, survivors[0].Troops);
    }

    /// <summary>
    /// T63 DoD 1: a deleted unit gets no promotion roll. Proved by contrast, not by inference: promoting
    /// the deletion pass's OWN output draws once (the one survivor); promoting the ORIGINAL, pre-deletion
    /// list -- as if the deletion pass had not run at all -- draws twice, from the same seed. The
    /// difference in draw count is the proof, not just the resulting promotion list.
    /// </summary>
    [Fact]
    public void DeletedUnit_GetsNoPromotionRoll()
    {
        var ruleset = BattleTestbed.Destroyed;
        var rules = ruleset.Combat;
        var units = ValueList.Of(
            BattleTestbed.Unit("light_infantry", 1499, 6, "Deleted"),   // below the 1,500 national threshold
            BattleTestbed.Unit("light_infantry", 12000, 6, "Survivor"));

        var survivors = BattleCasualties.DeleteBelowThreshold(units, ruleset);
        Assert.Single(survivors);

        var recorder = new BoundRecordingRng(new SplitMix64Rng(BattleTestbed.Seed));
        var (promoted, promotions) = BattleCasualties.Promote(survivors, recorder, rules);

        Assert.Single(recorder.Values);
        Assert.Single(promoted);
        Assert.Single(promotions);
        Assert.Equal("Survivor", promotions[0].UnitName);

        // Contrast: promoting the two-unit list the deletion pass was never run against draws TWICE.
        var recorderWithoutDeletion = new BoundRecordingRng(new SplitMix64Rng(BattleTestbed.Seed));
        BattleCasualties.Promote(units, recorderWithoutDeletion, rules);
        Assert.Equal(2, recorderWithoutDeletion.Values.Count);
    }

    /// <summary>
    /// T63 DoD 3: at the whole-unit-loss threshold exactly (<c>d == 70</c>, not <c>d &gt; 70</c>), no
    /// whole unit is removed -- only <see cref="BattleCasualties.Apply"/>'s per-unit expression runs.
    /// </summary>
    [Fact]
    public void ApplyToCarriedArmy_AtTheThreshold_NoWholeUnitIsRemoved()
    {
        var ruleset = BattleTestbed.Destroyed;
        var units = ValueList.Of(
            BattleTestbed.Unit("heavy_cavalry", 2000, 6, "A"),
            BattleTestbed.Unit("heavy_cavalry", 2000, 6, "B"));

        var rng = new SplitMix64Rng(BattleTestbed.Seed);
        var result = BattleCasualties.ApplyToCarriedArmy(
            units, damage: 70, unitLossThreshold: 70, unitLossDivisor: 250, rng, ruleset);

        Assert.Equal(0, result.UnitsLost);
        Assert.Equal(2, result.Units.Count);
        Assert.False(result.Emptied);
    }

    /// <summary>
    /// T63 DoD 3: one point above the threshold (<c>d == 71</c>), the whole-unit-loss branch fires --
    /// <c>(count × d) / divisor + 1</c> units removed, the "+ 1" bug #290 part 3 restores.
    /// </summary>
    [Fact]
    public void ApplyToCarriedArmy_JustAboveTheThreshold_RemovesWholeUnits()
    {
        var ruleset = BattleTestbed.Destroyed;
        var units = ValueList.Of(
            BattleTestbed.Unit("heavy_cavalry", 2000, 6, "A"),
            BattleTestbed.Unit("heavy_cavalry", 2000, 6, "B"));

        var rng = new SplitMix64Rng(BattleTestbed.Seed);
        var result = BattleCasualties.ApplyToCarriedArmy(
            units, damage: 71, unitLossThreshold: 70, unitLossDivisor: 250, rng, ruleset);

        // (2 x 71) / 250 + 1 = 0 + 1 = 1 -- without the "+ 1" this would floor to 0 and remove nothing,
        // which is exactly bug #290 part 3.
        Assert.Equal(1, result.UnitsLost);
        Assert.Single(result.Units);
    }

    /// <summary>T63 DoD 3: a one-unit carried army is destroyed outright once <c>d &gt; 70</c>.</summary>
    [Fact]
    public void ApplyToCarriedArmy_OneUnitArmy_IsDestroyedAboveTheThreshold()
    {
        var ruleset = BattleTestbed.Destroyed;
        var units = ValueList.Of(BattleTestbed.Unit("heavy_cavalry", 2000, 6, "Only"));

        var rng = new SplitMix64Rng(BattleTestbed.Seed);
        var result = BattleCasualties.ApplyToCarriedArmy(
            units, damage: 71, unitLossThreshold: 70, unitLossDivisor: 250, rng, ruleset);

        Assert.True(result.Emptied);
        Assert.Empty(result.Units);
    }

    /// <summary>
    /// T63 DoD 3: <c>d == 0</c> still runs the casualty pass -- <see cref="BattleCasualties.Apply"/> draws
    /// its divisor unconditionally, exactly as it does for a field or siege battle at any other ratio.
    /// </summary>
    [Fact]
    public void ApplyToCarriedArmy_RatioZero_StillRunsTheCasualtyPass()
    {
        var ruleset = BattleTestbed.Destroyed;
        var units = ValueList.Of(BattleTestbed.Unit("heavy_cavalry", 2000, 6, "A"));

        var recorder = new BoundRecordingRng(new SplitMix64Rng(BattleTestbed.Seed));
        var result = BattleCasualties.ApplyToCarriedArmy(
            units, damage: 0, unitLossThreshold: 70, unitLossDivisor: 250, recorder, ruleset);

        Assert.Single(recorder.Values); // the one, unconditional casualty-divisor draw.
        Assert.Equal(0, result.TroopsLost);
        Assert.Equal(0, result.UnitsLost);
    }

    /// <summary>
    /// T63 DoD 3: the whole-unit loss is removed swap-with-last, not by a shift -- proved by a scripted
    /// draw that picks a MIDDLE slot (index 1 of 4) to drop, where the two algorithms give different
    /// surviving orders (dropping the last slot of the four would make them coincide, which is exactly
    /// why the drop here is not the last one). Swap-with-last moves the LAST slot ("D") into the dropped
    /// slot's place, giving [A, D, C]; a shift-based <c>RemoveAt</c> would instead give [A, C, D].
    /// </summary>
    [Fact]
    public void ApplyToCarriedArmy_RemovesSwapWithLast_NotAShift()
    {
        var ruleset = BattleTestbed.Destroyed;
        var units = ValueList.Of(
            BattleTestbed.Unit("heavy_cavalry", 2000, 6, "A"),
            BattleTestbed.Unit("heavy_cavalry", 2000, 6, "B"),
            BattleTestbed.Unit("heavy_cavalry", 2000, 6, "C"),
            BattleTestbed.Unit("heavy_cavalry", 2000, 6, "D"));

        // Four casualty-divisor draws (Apply, one per slot; the value never matters here since the
        // survivor count -- not the exact troop loss -- is what this test proves), then one draw of 1
        // (out of 4) for the whole-unit loss: divisor = 1,000 keeps toLose at exactly 1 despite damage 71
        // ((4 x 71) / 1,000 + 1 = 0 + 1 = 1), so only the single scripted index is ever consulted.
        var rng = new ScriptedSequenceRng(0, 0, 0, 0, 1);
        var result = BattleCasualties.ApplyToCarriedArmy(
            units, damage: 71, unitLossThreshold: 70, unitLossDivisor: 1000, rng, ruleset);

        Assert.Equal(1, result.UnitsLost);
        Assert.Equal(new[] { "A", "D", "C" }, result.Units.Select(u => u.Name).ToArray());
        Assert.NotEqual(new[] { "A", "C", "D" }, result.Units.Select(u => u.Name).ToArray()); // what a shift would give
    }

    /// <summary>
    /// An <see cref="IRng"/> that plays back a fixed sequence of <see cref="NextInt(int)"/> results, one
    /// per call, regardless of the requested bound -- for a test that needs to control more than one draw
    /// (a casualty-divisor draw per slot, then a specific "which index was dropped" draw) precisely.
    /// </summary>
    private sealed class ScriptedSequenceRng : IRng
    {
        private readonly int[] _values;
        private int _index;

        public ScriptedSequenceRng(params int[] values) => _values = values;

        public ulong Seed => 0;

        public ulong State => 0;

        public ulong NextUInt64() => throw new NotSupportedException("Not scripted for this test.");

        public int NextInt(int exclusiveUpperBound) => _values[_index++];

        public int NextInt(int inclusiveLowerBound, int exclusiveUpperBound) =>
            throw new NotSupportedException("Not scripted for this test.");

        public bool NextChance(int numerator, int denominator) =>
            throw new NotSupportedException("Not scripted for this test.");

        public IRng ForStream(string streamName) => this;
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
