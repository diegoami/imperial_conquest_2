using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Model;

/// <summary>
/// T31 (docs/build-orchestration-plan.md, "T31 Correct <c>Ruleset.Siege</c>'s defender-strength field
/// identities") Done-when lines 1-3: the corrected <c>SiegeRules</c> weights, the renamed population
/// field, and the pinned <see cref="FortificationCode.FinishedPercent"/> decode the formula's doc comment
/// depends on. See <c>docs/investigations/siege-defender-strength.md</c> for the decompiled evidence.
/// </summary>
public class SiegeDefenderFieldsTests
{
    // ---- Done when 1: DefenderLoyaltyWeight is 150 and DefenderFortificationWeight is 250 on the
    // loaded Ruleset -- the swap of T02's shipped 250 and 150. ----

    [Fact]
    public void Defender_loyalty_and_fortification_weights_are_corrected_on_the_loaded_ruleset()
    {
        var siege = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile).Siege;

        Assert.Equal(150, siege.DefenderLoyaltyWeight);
        Assert.Equal(250, siege.DefenderFortificationWeight);
    }

    // ---- Done when 2: DefenderUnidentifiedFieldWeight is renamed DefenderPopulationWeight
    // (JSON defenderPopulationWeight), value unchanged at 200; a round-trip test covers the renamed
    // JSON key. ----

    [Fact]
    public void Defender_population_weight_is_200_and_the_json_key_is_renamed()
    {
        var ruleset = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);

        Assert.Equal(200, ruleset.Siege.DefenderPopulationWeight);

        var json = GameJson.Serialize(ruleset);
        Assert.Contains("\"defenderPopulationWeight\":", json, StringComparison.Ordinal);

        // No *key* named after the old, unidentified-field name survives. The old name may still
        // appear in _provenance prose explaining the rename's history -- only the JSON key matters here.
        Assert.DoesNotContain("\"defenderUnidentifiedFieldWeight\":", json, StringComparison.Ordinal);

        // Full round-trip: reload from the serialized JSON and confirm the renamed field survives.
        var reloaded = GameDataLoader.Load<Ruleset>("ruleset", json);
        Assert.Equal(200, reloaded.Siege.DefenderPopulationWeight);
        Assert.Equal(ruleset.Siege, reloaded.Siege);
    }

    // ---- Done when 3: a test pins FinishedPercent(100, fortifyRule) == 100 and
    // FinishedPercent(200, fortifyRule) == 0, so the difference between the guarded and unguarded
    // decode the SiegeRules formula depends on cannot be lost later. ----

    [Fact]
    public void FinishedPercent_uses_the_guarded_decode_the_siege_formula_depends_on()
    {
        var ruleset = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);
        var fortify = ruleset.CityOrders.Orders.FindById(o => o.Id, "fortify")!;

        // At MaxPercent exactly: a fully-finished city. The guard (code > MaxPercent) is false here, so
        // the word is returned unchanged. An UNGUARDED "% 100" would instead compute 100 % 100 = 0,
        // silently turning a finished 100% fortification into 0% -- this is the case the guard exists
        // to protect, and DoD 3 pins it down so the guard cannot be lost later.
        Assert.Equal(100, FortificationCode.FinishedPercent(100, fortify));

        // Above MaxPercent: an order is in progress, and the guarded decode strips the pending points
        // via "% InProgressEncodingRadix", giving the correct finished percentage (0, here). Reading the
        // RAW stored word instead (200) would misreport it as 200% finished with no pending order,
        // rather than 0% finished with one order's worth of points pending.
        Assert.Equal(0, FortificationCode.FinishedPercent(200, fortify));
    }

    // ---- T33 (issue #46) Done-when 1: HighFortificationThreshold/BonusNumerator/BonusDenominator are
    // renamed HighLoyaltyThreshold/BonusNumerator/BonusDenominator (JSON keys likewise), values 59/5/3
    // unchanged -- the branch tests loyalty > 59, not fortification. ----

    [Fact]
    public void HighLoyalty_threshold_and_bonus_values_are_unchanged_under_the_corrected_names()
    {
        var siege = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile).Siege;

        Assert.Equal(59, siege.HighLoyaltyThreshold);
        Assert.Equal(5, siege.HighLoyaltyBonusNumerator);
        Assert.Equal(3, siege.HighLoyaltyBonusDenominator);
    }

    [Fact]
    public void HighLoyalty_fields_round_trip_under_their_renamed_json_keys()
    {
        var ruleset = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);

        var json = GameJson.Serialize(ruleset);
        Assert.Contains("\"highLoyaltyThreshold\":", json, StringComparison.Ordinal);
        Assert.Contains("\"highLoyaltyBonusNumerator\":", json, StringComparison.Ordinal);
        Assert.Contains("\"highLoyaltyBonusDenominator\":", json, StringComparison.Ordinal);

        // No key named after the old, fortification-misnamed fields survives.
        Assert.DoesNotContain("\"highFortificationThreshold\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"highFortificationBonusNumerator\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"highFortificationBonusDenominator\":", json, StringComparison.Ordinal);

        var reloaded = GameDataLoader.Load<Ruleset>("ruleset", json);
        Assert.Equal(ruleset.Siege, reloaded.Siege);
    }

    // ---- T33 (issue #47) Done-when 2: DefenderOwnerNotAllegiancePenaltyPercent (20) becomes
    // DefenderNonAllegiantNumerator (4) and DefenderNonAllegiantDenominator (5), applied as
    // (strength * 4) / 5 -- not a 20% subtraction. ----

    [Fact]
    public void DefenderNonAllegiant_numerator_and_denominator_are_4_and_5()
    {
        var siege = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile).Siege;

        Assert.Equal(4, siege.DefenderNonAllegiantNumerator);
        Assert.Equal(5, siege.DefenderNonAllegiantDenominator);
    }

    [Fact]
    public void DefenderNonAllegiant_fields_round_trip_under_their_renamed_json_keys()
    {
        var ruleset = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile);

        var json = GameJson.Serialize(ruleset);
        Assert.Contains("\"defenderNonAllegiantNumerator\":", json, StringComparison.Ordinal);
        Assert.Contains("\"defenderNonAllegiantDenominator\":", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"defenderOwnerNotAllegiancePenaltyPercent\":", json, StringComparison.Ordinal);

        var reloaded = GameDataLoader.Load<Ruleset>("ruleset", json);
        Assert.Equal(ruleset.Siege, reloaded.Siege);
    }

    /// <summary>
    /// The pinned example from issue #47: <c>strength = 9</c> gives <c>7</c> under the actual
    /// <c>(strength * 4) / 5</c> operation, not the <c>8</c> a naive 20%-subtraction reading
    /// (<c>strength - strength / 5</c>) would give. <c>(9 &lt;&lt; 2) / 5 = 36 / 5 = 7</c>, truncated;
    /// <c>9 - 9 / 5 = 9 - 1 = 8</c>. The two readings diverge here specifically because 9 is not a
    /// multiple of 5.
    /// </summary>
    [Fact]
    public void DefenderNonAllegiant_penalty_pins_strength_9_to_7_not_a_20_percent_subtraction()
    {
        var siege = GameDataLoader.LoadFile<Ruleset>(TestPaths.ToyRulesetFile).Siege;

        var actual = (9 * siege.DefenderNonAllegiantNumerator) / siege.DefenderNonAllegiantDenominator;
        var naiveTwentyPercentSubtraction = 9 - (9 / 5);

        Assert.Equal(7, actual);
        Assert.Equal(8, naiveTwentyPercentSubtraction);
        Assert.NotEqual(naiveTwentyPercentSubtraction, actual);
    }
}
