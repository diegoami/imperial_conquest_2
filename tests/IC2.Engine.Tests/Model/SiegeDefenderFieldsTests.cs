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

        // At or below MaxPercent: the word IS the finished percentage, unchanged.
        Assert.Equal(100, FortificationCode.FinishedPercent(100, fortify));

        // Above MaxPercent: an order is in progress, and the guarded decode strips it via
        // "% InProgressEncodingRadix" -- an unguarded "% 100" would give the same answer only by
        // coincidence for this particular ruleset's radix, which is exactly the ambiguity DoD 3 exists
        // to pin down before it can be lost.
        Assert.Equal(0, FortificationCode.FinishedPercent(200, fortify));
    }
}
