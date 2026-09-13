using IC2.Engine.Strength;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Strength;

/// <summary>
/// <c>docs/build-orchestration-plan.md</c> "T07 Strength functions", Done-when 4 ("siege defender
/// strength triples archers, and applies the fortification/loyalty term"). See
/// <see cref="SiegeStrength"/>'s remarks for why this splits into <see cref="SiegeStrength.Attacker"/>
/// (archers tripled) and <see cref="SiegeStrength.Defender"/> (loyalty/fortification/population),
/// matching the two separate decompiled functions the task's own wording summarises in one sentence,
/// and for the round-1 correction to <see cref="SiegeStrength.Defender"/>'s field identities
/// (<c>docs/investigations/siege-defender-strength.md</c>, T31).
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

        Assert.Throws<ArgumentException>(() => SiegeStrength.Attacker(units, morale: 60, ruleset, "archer"));
    }

    /// <summary>
    /// "Applies the fortification/loyalty term": all three confirmed terms are present, each with its
    /// own weight, matching the corrected <c>FUN_0044A98C</c> field mapping
    /// (<c>docs/investigations/siege-defender-strength.md</c>): loyalty × 150, finished fortification
    /// percent × 250, population (thousands) × 200. No fortification order is pending in this test, so
    /// the stored word equals the finished percent directly.
    /// </summary>
    [Fact]
    public void Defender_AppliesLoyaltyFortificationAndPopulationTerms()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var order = StrengthTestbed.FortifyOrder;

        var loyaltyOnly = SiegeStrength.Defender(fortificationCode: 0, order, loyalty: 10, populationThousands: 0, ruleset);
        var fortificationOnly = SiegeStrength.Defender(fortificationCode: 10, order, loyalty: 0, populationThousands: 0, ruleset);
        var populationOnly = SiegeStrength.Defender(fortificationCode: 0, order, loyalty: 0, populationThousands: 10, ruleset);
        var allThree = SiegeStrength.Defender(fortificationCode: 10, order, loyalty: 10, populationThousands: 10, ruleset);

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

        var decoded = SiegeStrength.Defender(fortificationCode: 130, order, loyalty: 0, populationThousands: 0, ruleset);
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

        var atMaximum = SiegeStrength.Defender(fortificationCode: 100, order, loyalty: 0, populationThousands: 0, ruleset);

        Assert.Equal(100 * ruleset.Siege.DefenderFortificationWeight, atMaximum);
        Assert.NotEqual(0, atMaximum);
    }

    [Fact]
    public void Defender_HigherFortificationOrLoyalty_IncreasesStrength()
    {
        var ruleset = StrengthTestbed.Ruleset;
        var order = StrengthTestbed.FortifyOrder;

        var lower = SiegeStrength.Defender(fortificationCode: 20, order, loyalty: 40, populationThousands: 0, ruleset);
        var higherFortification = SiegeStrength.Defender(fortificationCode: 60, order, loyalty: 40, populationThousands: 0, ruleset);
        var higherLoyalty = SiegeStrength.Defender(fortificationCode: 20, order, loyalty: 90, populationThousands: 0, ruleset);

        Assert.True(higherFortification > lower);
        Assert.True(higherLoyalty > lower);
    }
}
