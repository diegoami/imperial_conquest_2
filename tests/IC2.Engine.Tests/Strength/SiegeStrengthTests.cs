using IC2.Engine.Strength;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Strength;

/// <summary>
/// <see cref="SiegeStrength.Attacker"/> (archers tripled) and <see cref="SiegeStrength.Defender"/>
/// (loyalty/fortification/population, then the capital-and-loyalty and owner-vs-allegiance scaling
/// branches), matching the two separate decompiled functions <c>FUN_0044A930</c> and
/// <c>FUN_0044A98C</c>. See <see cref="SiegeStrength"/>'s remarks for the full history, and
/// <c>docs/investigations/siege-defender-strength.md</c> (T31/T33) for the decompiled evidence behind
/// every constant and branch here.
/// </summary>
public sealed class SiegeStrengthTests
{
    /// <summary>
    /// Archers count 3x their troops in the besieging army's own strength
    /// (<see cref="SiegeRules.ArcherStrengthMultiplier"/>), everything else counts 1x -- confirmed
    /// twice over: the T04 corpus's <c>capture.attackerSiegeStrengthArcherMultiplier</c> fixture, and
    /// directly reading <c>FUN_0044A930</c> from the local decompiled dump. Same troop count, only the
    /// unit type differs, so the ratio between the two results must be exactly the multiplier.
    /// </summary>
    [Fact]
    public void Attacker_ArchersCountTripleTheTroopsOfAnyOtherType()
    {
        var ruleset = StrengthTestbed.Ruleset;
        const int troops = 8000;
        const int morale = 60;

        var archerUnits = new[] { StrengthTestbed.Unit(StrengthTestbed.ArcherUnitTypeId, troops) };
        var nonArcherUnits = new[] { StrengthTestbed.Unit("heavy_infantry", troops) };

        var archerStrength = SiegeStrength.Attacker(archerUnits, morale, ruleset, StrengthTestbed.ArcherUnitTypeId);
        var nonArcherStrength = SiegeStrength.Attacker(nonArcherUnits, morale, ruleset, StrengthTestbed.ArcherUnitTypeId);

        // archer: (8000*3)/80 * 60 = (24000/80)*60 = 300*60 = 18,000
        // non-archer: (8000*1)/80 * 60 = 100*60 = 6,000
        Assert.Equal(18_000, archerStrength);
        Assert.Equal(6_000, nonArcherStrength);
        Assert.Equal(ruleset.Siege.ArcherStrengthMultiplier, archerStrength / nonArcherStrength);
    }

    /// <summary>
    /// FUN_0044A930 has no intermediate <c>/ 100</c>, unlike FUN_0044A8CC (<see cref="ArmyPower"/>): a
    /// troop count large enough to give a nonzero result under <c>/ 80</c> alone would be truncated to
    /// zero by a spurious <c>/ 100</c> applied first, so this pins the absence of that step directly
    /// rather than via a case that would pass either way.
    /// </summary>
    [Fact]
    public void Attacker_NoDivisionByHundred_UnlikeArmyPower()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var units = new[] { StrengthTestbed.Unit("heavy_infantry", 8000) };

        var strength = SiegeStrength.Attacker(units, morale: 1, ruleset, StrengthTestbed.ArcherUnitTypeId);

        // With no /100: (8000/80)*1 = 100. If a /100 were wrongly applied first: (8000/100)/80*1 = 0.
        Assert.Equal(100, strength);
    }

    /// <summary>
    /// Round-1: a caller supplying an archer unit-type id the ruleset does not define must not have it
    /// silently treated as "not an archer" (which would compute a wrong, quiet answer); it must fail the
    /// same way <see cref="ArmyPower.Compute"/> fails on an unknown unit type.
    /// </summary>
    [Fact]
    public void Attacker_UnknownArcherUnitTypeId_Throws()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var units = new[] { StrengthTestbed.Unit(StrengthTestbed.ArcherUnitTypeId, 8000) };

        var exception = Assert.Throws<ArgumentException>(() => SiegeStrength.Attacker(units, morale: 60, ruleset, "archer"));

        // This throws from the EARLIER guard (SiegeStrength.cs:79-83), on the archerUnitTypeId
        // parameter itself -- every unit here has a valid type. It does not exercise the per-unit
        // validation added in the loop; see Attacker_UnitWithUnknownTypeId_Throws_WithUnitsAsTheParamName
        // for that, distinguished by ParamName.
        Assert.Equal("archerUnitTypeId", exception.ParamName);
    }

    /// <summary>
    /// Round-1 review (issue #49 item 2, blocking finding B1): a unit whose OWN type id the ruleset
    /// does not define must also throw, not just an unrecognised <c>archerUnitTypeId</c> parameter.
    /// This exercises the loop's own per-unit validation (<c>SiegeStrength.cs:90-94</c>) rather than the
    /// earlier <c>archerUnitTypeId</c> guard <see cref="Attacker_UnknownArcherUnitTypeId_Throws"/>
    /// exercises: here <c>archerUnitTypeId</c> is valid, and the unrecognised id is on the
    /// <see cref="UnitSlot"/> instead. <c>ParamName</c> must be <c>"units"</c>, matching
    /// <see cref="ArmyPower.Compute"/>'s own failure mode for the same situation (the DoD's own
    /// standard, "the way ArmyPower does"). Deleting the loop's validation block leaves every other
    /// test in this file green; only this assertion catches that regression.
    /// </summary>
    [Fact]
    public void Attacker_UnitWithUnknownTypeId_Throws_WithUnitsAsTheParamName()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var units = new[] { StrengthTestbed.Unit("not_a_real_type", 100) };

        var exception = Assert.Throws<ArgumentException>(
            () => SiegeStrength.Attacker(units, morale: 60, ruleset, StrengthTestbed.ArcherUnitTypeId));

        Assert.Equal("units", exception.ParamName);
    }

    /// <summary>
    /// All three confirmed weighted-sum terms are present, each with its own weight, matching the
    /// corrected <c>FUN_0044A98C</c> field mapping (<c>docs/investigations/siege-defender-strength.md</c>):
    /// loyalty × 150, finished fortification percent × 250, population (thousands) × 200. No
    /// fortification order is pending in this test, so the stored word equals the finished percent
    /// directly. Neither scaling branch applies here (not a capital, owner equals allegiance), isolating
    /// the weighted sum itself.
    /// </summary>
    [Fact]
    public void Defender_AppliesLoyaltyFortificationAndPopulationTerms()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var order = StrengthTestbed.FortifyOrder;

        var loyaltyOnly = SiegeStrength.Defender(fortificationCode: 0, order, loyalty: 10, populationThousands: 0, isControllerCapital: false, ownerDiffersFromAllegiance: false, ruleset);
        var fortificationOnly = SiegeStrength.Defender(fortificationCode: 10, order, loyalty: 0, populationThousands: 0, isControllerCapital: false, ownerDiffersFromAllegiance: false, ruleset);
        var populationOnly = SiegeStrength.Defender(fortificationCode: 0, order, loyalty: 0, populationThousands: 10, isControllerCapital: false, ownerDiffersFromAllegiance: false, ruleset);
        var allThree = SiegeStrength.Defender(fortificationCode: 10, order, loyalty: 10, populationThousands: 10, isControllerCapital: false, ownerDiffersFromAllegiance: false, ruleset);

        Assert.Equal(10 * ruleset.Siege.DefenderLoyaltyWeight, loyaltyOnly);
        Assert.Equal(10 * ruleset.Siege.DefenderFortificationWeight, fortificationOnly);
        Assert.Equal(10 * ruleset.Siege.DefenderPopulationWeight, populationOnly);
        Assert.Equal(loyaltyOnly + fortificationOnly + populationOnly, allThree);

        // Pinned against the corrected weights directly: loyalty 150, fortification 250, population 200
        // -- the swap T31 fixed, so a regression back to the old (wrong) assignment fails these, not
        // just the ratio-style assertions above.
        Assert.Equal(1500, loyaltyOnly);
        Assert.Equal(2500, fortificationOnly);
        Assert.Equal(2000, populationOnly);
        Assert.Equal(6000, allThree);
    }

    /// <summary>
    /// The T04 corpus's <c>capture.siegeDefenderStrengthFormula</c> fixture is a prose string, so unlike
    /// the numeric <c>+0x26</c> weights it is not something a numeric assertion can cross-check by
    /// itself. This test at least confirms the corpus's own transcription names the same three terms in
    /// the same order this file implements, catching a re-introduction of the pre-T31 wording (which
    /// named fortification and loyalty in the other order and called the third term unidentified).
    /// </summary>
    [Fact]
    public void Defender_FormulaShape_MatchesCorpusFixtureWording()
    {
        var formula = FixtureCorpus.Get("capture.siegeDefenderStrengthFormula").AsString();

        Assert.Contains("loyalty*150", formula, StringComparison.Ordinal);
        Assert.Contains("fortification*250", formula, StringComparison.Ordinal);
        Assert.Contains("population*200", formula, StringComparison.Ordinal);
    }

    /// <summary>
    /// Round-1 (the review's blocking finding): the stored fortification word is dual-encoded — at or
    /// below the order's maximum it is a finished percentage, above it it encodes
    /// <c>finishedPercent + pendingPoints × radix</c> — and <c>FUN_0044A98C</c> decodes it
    /// unconditionally before weighting it. A code of 130 (30% finished, 1 point pending, radix 100)
    /// must weight as 30, not 130.
    /// </summary>
    [Fact]
    public void Defender_DecodesInProgressFortificationWord()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var order = StrengthTestbed.FortifyOrder;

        var decoded = SiegeStrength.Defender(fortificationCode: 130, order, loyalty: 0, populationThousands: 0, isControllerCapital: false, ownerDiffersFromAllegiance: false, ruleset);
        var ifNeverDecoded = 130 * ruleset.Siege.DefenderFortificationWeight;

        Assert.Equal(30 * ruleset.Siege.DefenderFortificationWeight, decoded);
        Assert.NotEqual(ifNeverDecoded, decoded);
    }

    /// <summary>
    /// The other half of the decode, called out explicitly in
    /// <c>docs/investigations/siege-defender-strength.md</c>: an <em>unguarded</em>
    /// <c>code % 100</c> would turn a fully-finished city's stored 100 into 0, silently deleting the
    /// entire fortification term. The guarded decode (<see cref="Model.FortificationCode.FinishedPercent"/>,
    /// which only applies the modulus once the word exceeds the order's maximum) must read 100 as 100.
    /// </summary>
    [Fact]
    public void Defender_FinishedFortificationAtExactlyMaximum_IsNotZeroed()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var order = StrengthTestbed.FortifyOrder;

        var atMaximum = SiegeStrength.Defender(fortificationCode: 100, order, loyalty: 0, populationThousands: 0, isControllerCapital: false, ownerDiffersFromAllegiance: false, ruleset);

        Assert.Equal(100 * ruleset.Siege.DefenderFortificationWeight, atMaximum);
        Assert.NotEqual(0, atMaximum);
    }

    [Fact]
    public void Defender_HigherFortificationOrLoyalty_IncreasesStrength()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var order = StrengthTestbed.FortifyOrder;

        var lower = SiegeStrength.Defender(fortificationCode: 20, order, loyalty: 40, populationThousands: 0, isControllerCapital: false, ownerDiffersFromAllegiance: false, ruleset);
        var higherFortification = SiegeStrength.Defender(fortificationCode: 60, order, loyalty: 40, populationThousands: 0, isControllerCapital: false, ownerDiffersFromAllegiance: false, ruleset);
        var higherLoyalty = SiegeStrength.Defender(fortificationCode: 20, order, loyalty: 90, populationThousands: 0, isControllerCapital: false, ownerDiffersFromAllegiance: false, ruleset);

        Assert.True(higherFortification > lower);
        Assert.True(higherLoyalty > lower);
    }

    // ---- T33 Done-when 3: SiegeStrength.Defender applies both scaling branches, in the decompiled
    // function's own order (weighted sum, then capital-and-loyalty, then owner-vs-allegiance), each
    // truncating at each step. One test per branch, one with both, and one input where applying the two
    // branches in the other order gives a different result. ----

    /// <summary>
    /// The capital-and-loyalty branch alone: <c>loyalty=100, fortification=0, population=0</c> gives a
    /// weighted sum of <c>100 * 150 = 15,000</c>; with <c>isControllerCapital=true</c> and loyalty (100)
    /// above <see cref="SiegeRules.HighLoyaltyThreshold"/> (59), the sum scales by
    /// <c>5/3</c>: <c>15,000 * 5 / 3 = 25,000</c> exactly. The owner-vs-allegiance branch does not apply.
    /// </summary>
    [Fact]
    public void Defender_CapitalAndHighLoyaltyBranch_Alone_ScalesByFiveThirds()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var order = StrengthTestbed.FortifyOrder;

        var strength = SiegeStrength.Defender(
            fortificationCode: 0, order, loyalty: 100, populationThousands: 0,
            isControllerCapital: true, ownerDiffersFromAllegiance: false, ruleset);

        Assert.Equal(25_000, strength);
    }

    /// <summary>
    /// The owner-vs-allegiance branch alone: <c>loyalty=0, fortification=40, population=0</c> gives a
    /// weighted sum of <c>40 * 250 = 10,000</c>; with <c>ownerDiffersFromAllegiance=true</c> the sum
    /// scales by <see cref="SiegeRules.DefenderNonAllegiantNumerator"/>/<see cref="SiegeRules.DefenderNonAllegiantDenominator"/>
    /// (4/5): <c>10,000 * 4 / 5 = 8,000</c> exactly. The capital-and-loyalty branch does not apply
    /// (not a capital, and loyalty is 0).
    /// </summary>
    [Fact]
    public void Defender_OwnerDiffersFromAllegianceBranch_Alone_ScalesByFourFifths()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var order = StrengthTestbed.FortifyOrder;

        var strength = SiegeStrength.Defender(
            fortificationCode: 40, order, loyalty: 0, populationThousands: 0,
            isControllerCapital: false, ownerDiffersFromAllegiance: true, ruleset);

        Assert.Equal(8_000, strength);
    }

    /// <summary>
    /// Both branches together, applied in the decompiled function's order (capital-and-loyalty first,
    /// then owner-vs-allegiance): the same <c>loyalty=100</c> weighted sum (15,000) scales to 25,000 by
    /// the first branch, then to <c>25,000 * 4 / 5 = 20,000</c> by the second.
    /// </summary>
    [Fact]
    public void Defender_BothBranches_ApplyInSequence()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var order = StrengthTestbed.FortifyOrder;

        var strength = SiegeStrength.Defender(
            fortificationCode: 0, order, loyalty: 100, populationThousands: 0,
            isControllerCapital: true, ownerDiffersFromAllegiance: true, ruleset);

        Assert.Equal(20_000, strength);
    }

    /// <summary>
    /// The two branches do not commute under truncating integer division: applying the owner-vs-allegiance
    /// branch <em>before</em> the capital-and-loyalty branch (the wrong order) gives a different final
    /// result than the decompiled function's actual order (capital-and-loyalty, then owner-vs-allegiance),
    /// for the same inputs. Weighted sum: <c>loyalty=61 (61*150=9,150) + fortification=1 (1*250=250) =
    /// 9,400</c>.
    /// <code>
    /// correct order:  9,400 * 5 / 3 = 15,666 (truncated); 15,666 * 4 / 5 = 12,532 (truncated)
    /// wrong order:    9,400 * 4 / 5 = 7,520  (exact);      7,520 * 5 / 3 = 12,533 (truncated)
    /// </code>
    /// 12,532 ≠ 12,533 -- the order is load-bearing, not merely a stylistic choice.
    /// </summary>
    [Fact]
    public void Defender_BranchOrder_IsLoadBearing_ReversedOrderGivesADifferentResult()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var order = StrengthTestbed.FortifyOrder;
        var rules = ruleset.Siege;

        var strength = SiegeStrength.Defender(
            fortificationCode: 1, order, loyalty: 61, populationThousands: 0,
            isControllerCapital: true, ownerDiffersFromAllegiance: true, ruleset);

        const int weightedSum = (61 * 150) + (1 * 250); // 9,400 -- loyalty and fortification terms only.
        var wrongOrder = weightedSum * rules.DefenderNonAllegiantNumerator / rules.DefenderNonAllegiantDenominator;
        wrongOrder = wrongOrder * rules.HighLoyaltyBonusNumerator / rules.HighLoyaltyBonusDenominator;

        Assert.Equal(12_532, strength);
        Assert.Equal(12_533, wrongOrder);
        Assert.NotEqual(wrongOrder, strength);
    }

    // ---- Round-1 review: blocking finding B2 and non-blocking N2. Every branch test above passes
    // isControllerCapital: true, so neither the capital conjunct nor the loyalty threshold's exact
    // boundary was independently pinned -- a mutant dropping "isControllerCapital &&" from the guard,
    // or loosening "loyalty > threshold" to ">=", left the whole suite (494 tests) green. ----

    /// <summary>
    /// B2 (blocking): the capital conjunct is not optional. Loyalty (100) is well above
    /// <see cref="SiegeRules.HighLoyaltyThreshold"/> (59), but <c>isControllerCapital: false</c> must
    /// keep the branch from firing at all -- the result is the bare weighted sum
    /// (<c>100 * 150 = 15,000</c>), not <c>15,000 * 5 / 3 = 25,000</c>. A mutant that dropped
    /// <c>isControllerCapital &amp;&amp;</c> from <c>SiegeStrength.cs:165</c>'s guard would wrongly scale
    /// this and fail the assertion -- verified locally by deleting that conjunct, watching this test
    /// fail (25,000 actual vs 15,000 expected) while the rest of the suite stayed green, then
    /// reverting.
    /// </summary>
    [Fact]
    public void Defender_HighLoyaltyBranch_DoesNotFire_WhenNotTheCapital()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var order = StrengthTestbed.FortifyOrder;

        var strength = SiegeStrength.Defender(
            fortificationCode: 0, order, loyalty: 100, populationThousands: 0,
            isControllerCapital: false, ownerDiffersFromAllegiance: false, ruleset);

        Assert.Equal(15_000, strength);
    }

    /// <summary>
    /// N2 (non-blocking): the threshold is a strict "greater than" (<c>0x3b &lt; loyalty</c>, i.e.
    /// loyalty &gt; 59), not "at or above". At loyalty=59 (the threshold itself) the branch must not
    /// fire: the weighted sum <c>59 * 150 = 8,850</c> is unscaled. At loyalty=60 -- one above the
    /// threshold -- it must fire: <c>60 * 150 = 9,000</c> scales to <c>9,000 * 5 / 3 = 15,000</c>. A
    /// mutant changing <c>&gt;</c> to <c>&gt;=</c> in the guard would wrongly scale the loyalty=59 case
    /// and fail its assertion -- verified locally the same way as B2 above.
    /// </summary>
    [Fact]
    public void Defender_HighLoyaltyThreshold_IsExclusive_AtTheBoundary()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var order = StrengthTestbed.FortifyOrder;

        var atThreshold = SiegeStrength.Defender(
            fortificationCode: 0, order, loyalty: 59, populationThousands: 0,
            isControllerCapital: true, ownerDiffersFromAllegiance: false, ruleset);
        var oneAboveThreshold = SiegeStrength.Defender(
            fortificationCode: 0, order, loyalty: 60, populationThousands: 0,
            isControllerCapital: true, ownerDiffersFromAllegiance: false, ruleset);

        Assert.Equal(8_850, atThreshold);        // No bonus: 59 is not > 59.
        Assert.Equal(15_000, oneAboveThreshold); // Bonus applies: 9,000 * 5 / 3 = 15,000.
    }
}
