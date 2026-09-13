using IC2.Engine.Strength;
using Xunit;

namespace IC2.Engine.Tests.Strength;

/// <summary>
/// <c>docs/build-orchestration-plan.md</c> "T07 Strength functions", Done-when 4 ("siege defender
/// strength triples archers, and applies the fortification/loyalty term"). See
/// <see cref="SiegeStrength"/>'s remarks for why this splits into <see cref="SiegeStrength.Attacker"/>
/// (archers tripled) and <see cref="SiegeStrength.Defender"/> (fortification/loyalty), matching the two
/// separate decompiled functions the task's own wording summarises in one sentence.
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

    [Fact]
    public void Attacker_NoDivisionByHundred_UnlikeArmyPower()
    {
        // FUN_0044A930 has no intermediate /100, unlike FUN_0044A8CC (ArmyPower). A single unit whose
        // weight*troops product would truncate under a /100 step, but must not here.
        var ruleset = StrengthTestbed.Ruleset;
        var units = new[] { StrengthTestbed.Unit("heavy_infantry", 37) };

        var strength = SiegeStrength.Attacker(units, morale: 1, ruleset, StrengthTestbed.ArcherUnitTypeId);

        // (37 * 1) / 80 * 1 = 0. If a /100 were wrongly applied first, this would still be 0, so use a
        // count that only distinguishes the two under /80 directly: troops large enough that /80 alone
        // gives a nonzero result, which a spurious /100 step would zero out.
        Assert.Equal(0, strength); // 37/80 == 0 -- sanity: no crash, no unexpected extra division.

        var largerUnits = new[] { StrengthTestbed.Unit("heavy_infantry", 8000) };
        var largerStrength = SiegeStrength.Attacker(largerUnits, morale: 1, ruleset, StrengthTestbed.ArcherUnitTypeId);

        // With no /100: (8000/80)*1 = 100. If a /100 were wrongly applied first: (8000/100)/80*1 = 1.
        Assert.Equal(100, largerStrength);
    }

    /// <summary>
    /// "Applies the fortification/loyalty term": both terms are present and independently weighted, per
    /// <see cref="SiegeRules.DefenderFortificationWeight"/> and <see cref="SiegeRules.DefenderLoyaltyWeight"/>
    /// (both sourced from the ruleset, matching the T04 corpus's
    /// <c>capture.siegeDefenderStrengthFormula</c> fixture and the shipped <c>toy-ruleset.json</c>).
    /// </summary>
    [Fact]
    public void Defender_AppliesFortificationAndLoyaltyTerms()
    {
        var ruleset = StrengthTestbed.Ruleset;

        var fortificationOnly = SiegeStrength.Defender(fortification: 10, loyalty: 0, unidentifiedFieldValue: 0, ruleset);
        var loyaltyOnly = SiegeStrength.Defender(fortification: 0, loyalty: 10, unidentifiedFieldValue: 0, ruleset);
        var thirdFieldOnly = SiegeStrength.Defender(fortification: 0, loyalty: 0, unidentifiedFieldValue: 10, ruleset);
        var allThree = SiegeStrength.Defender(fortification: 10, loyalty: 10, unidentifiedFieldValue: 10, ruleset);

        Assert.Equal(10 * ruleset.Siege.DefenderFortificationWeight, fortificationOnly);
        Assert.Equal(10 * ruleset.Siege.DefenderLoyaltyWeight, loyaltyOnly);
        Assert.Equal(10 * ruleset.Siege.DefenderUnidentifiedFieldWeight, thirdFieldOnly);
        Assert.Equal(fortificationOnly + loyaltyOnly + thirdFieldOnly, allThree);

        // Pinned against the currently shipped toy-ruleset.json values too (150 / 250 / 200), which
        // match the T04 corpus's capture.siegeDefenderStrengthFormula fixture exactly, so a silent
        // ruleset-data change would be caught here as well as by the ratio assertions above.
        Assert.Equal(1500, fortificationOnly);
        Assert.Equal(2500, loyaltyOnly);
        Assert.Equal(2000, thirdFieldOnly);
        Assert.Equal(6000, allThree);
    }

    [Fact]
    public void Defender_HigherFortificationOrLoyalty_IncreasesStrength()
    {
        var ruleset = StrengthTestbed.Ruleset;

        var lower = SiegeStrength.Defender(fortification: 20, loyalty: 40, unidentifiedFieldValue: 0, ruleset);
        var higherFortification = SiegeStrength.Defender(fortification: 60, loyalty: 40, unidentifiedFieldValue: 0, ruleset);
        var higherLoyalty = SiegeStrength.Defender(fortification: 20, loyalty: 90, unidentifiedFieldValue: 0, ruleset);

        Assert.True(higherFortification > lower);
        Assert.True(higherLoyalty > lower);
    }
}
