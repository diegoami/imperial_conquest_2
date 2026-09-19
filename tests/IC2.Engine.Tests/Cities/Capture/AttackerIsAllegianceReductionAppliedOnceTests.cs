using IC2.Engine.Battle;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Strength;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// The task entry's own header requirement: "T16 already applies
/// <see cref="SiegeRules.AttackerIsAllegianceDefenderReductionPercent"/> (the ×9/10) in its siege
/// resolver, with a test. Do not apply it a second time... assert the reduction is applied exactly once
/// across the two tasks." This file is that assertion, from T17's side: T16's
/// <see cref="InstantBattleResolver.ResolveSiege"/> applies the ×9/10 when the attacker is the city's own
/// allegiance, and <see cref="CompleteDefenderStrength"/> — the "complete defender strength" T17's own
/// code (<see cref="CityCaptureResolver.RunCascade"/>) uses for every candidate it evaluates — never does.
/// </summary>
public sealed class AttackerIsAllegianceReductionAppliedOnceTests
{
    /// <summary>
    /// T16's side of "exactly once": when the attacking nation equals the city's allegiance,
    /// <see cref="InstantBattleResolver.ResolveSiege"/>'s reported <see cref="BattleResult.DefenderPower"/>
    /// is <see cref="SiegeStrength.Defender"/>'s own (unreduced) value scaled by the ruleset's ×9/10 —
    /// proving T16 applies the field this entry's blockquote names.
    /// </summary>
    [Fact]
    public void ResolveSiege_AppliesTheReduction_WhenAttackerIsTheCitysAllegiance()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, owner: "occupier", allegiance: "homeland",
            loyalty: 50, fortificationCode: 20, populationThousands: 10, maxPopulationThousands: 20, tribute: 0);
        var attacker = CaptureTestbed.Army(
            "army", "homeland", 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 3000));
        var state = CaptureTestbed.StateWith(
            new[] { CaptureTestbed.Nation("homeland"), CaptureTestbed.Nation("occupier") },
            new[] { city }, new[] { attacker });

        var rng = new SplitMix64Rng(0x99UL);
        var resolution = InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, rng, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        var fortifyOrder = ruleset.CityOrders.Orders.FindById(o => o.Id, CaptureTestbed.FortifyOrderId)!;
        var unreduced = SiegeStrength.Defender(
            city.FortificationCode, fortifyOrder, city.Loyalty, city.PopulationThousands,
            isControllerCapital: false, ownerDiffersFromAllegiance: true, ruleset);
        var remainingPercent = 100 - ruleset.Siege.AttackerIsAllegianceDefenderReductionPercent;
        var expectedReduced = (unreduced * remainingPercent) / 100;

        Assert.NotEqual(unreduced, expectedReduced); // The reduction is non-trivial for this fixture.
        Assert.Equal(expectedReduced, resolution.Result.DefenderPower);
    }

    /// <summary>
    /// T17's side of "exactly once": <see cref="CompleteDefenderStrength"/> — the value
    /// <see cref="CityCaptureResolver"/>'s own cascade uses — is unaffected by the attacking nation being
    /// the candidate city's allegiance. It equals <see cref="SiegeStrength.Defender"/> plus the garrison
    /// term, full stop; the ×9/10 field is never read anywhere in <c>src/IC2.Engine/Cities/Capture/**</c>.
    /// </summary>
    [Fact]
    public void CompleteDefenderStrength_NeverAppliesTheAttackerIsAllegianceReduction()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, owner: "occupier", allegiance: "homeland",
            loyalty: 50, fortificationCode: 20, populationThousands: 10, maxPopulationThousands: 20, tribute: 0);
        var owner = CaptureTestbed.Nation("occupier");
        var fortifyOrder = ruleset.CityOrders.Orders.FindById(o => o.Id, CaptureTestbed.FortifyOrderId)!;

        var strength = CompleteDefenderStrength.Compute(
            city, fortifyOrder, isControllerCapital: false, ownerDiffersFromAllegiance: true, owner, ruleset);

        var expectedUnreduced = SiegeStrength.Defender(
            city.FortificationCode, fortifyOrder, city.Loyalty, city.PopulationThousands,
            isControllerCapital: false, ownerDiffersFromAllegiance: true, ruleset);

        Assert.Equal(expectedUnreduced, strength); // No garrison here, so this is the base value, unreduced.

        var wronglyReduced = (expectedUnreduced * (100 - ruleset.Siege.AttackerIsAllegianceDefenderReductionPercent)) / 100;
        Assert.NotEqual(wronglyReduced, strength);
    }
}
