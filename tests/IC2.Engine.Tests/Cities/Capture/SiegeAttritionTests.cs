using IC2.Engine.Battle;
using IC2.Engine.Core;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// <c>docs/task-catalogue.md</c> T17, Done-when 5: "Per-siege attrition runs on every attempt, win or
/// lose." Already implemented and tested by T16 inside <see cref="InstantBattleResolver.ResolveSiege"/>
/// (<c>src/IC2.Engine/Battle/**</c>, not this task's Owns list); this file proves the DoD line through
/// that resolver's own public entry point, which this task is free to call without owning the file.
/// </summary>
public sealed class SiegeAttritionTests
{
    /// <summary>An attacker overwhelming enough to win still takes casualties from the attempt itself.</summary>
    [Fact]
    public void AttackerThatWinsTheSiege_StillTakesAttrition()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "defender", "defender", loyalty: 30, fortificationCode: 0,
            populationThousands: 1, maxPopulationThousands: 10, tribute: 0);
        var attacker = CaptureTestbed.Army(
            "army", "attacker", 0, 0, morale: 90, CaptureTestbed.Unit("heavy_infantry", 20_000));
        var state = CaptureTestbed.StateWith(
            new[] { CaptureTestbed.Nation("attacker"), CaptureTestbed.Nation("defender") },
            new[] { city }, new[] { attacker });

        var rng = new SplitMix64Rng(0x517UL);
        var resolution = InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, rng, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        Assert.Equal(BattleSide.Attacker, resolution.Result.Winner);
        Assert.NotEmpty(resolution.Result.UnitCasualties);
        var attackerAfter = resolution.State.ArmyById("army")!;
        Assert.True(attackerAfter.TotalTroops < attacker.TotalTroops);
    }

    /// <summary>
    /// An attacker too weak to win still takes attrition from the failed attempt. Chosen so the casualty
    /// ratio (<c>loserPower × 40 / winnerPower</c>) truncates to a small but strictly positive value
    /// (attacker power 3,720, defender power 11,500 → ratio 12) rather than to zero: an overwhelming power
    /// gap would make this DoD line vacuously true by giving every slot a zero-troops loss regardless of
    /// whether attrition ran at all, which is exactly the case that must not be mistaken for "no attrition".
    /// </summary>
    [Fact]
    public void AttackerThatLosesTheSiege_StillTakesAttrition()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "defender", "defender", loyalty: 50, fortificationCode: 0,
            populationThousands: 20, maxPopulationThousands: 30, tribute: 0);
        var attacker = CaptureTestbed.Army(
            "army", "attacker", 0, 0, morale: 60, CaptureTestbed.Unit("heavy_infantry", 5000));
        var state = CaptureTestbed.StateWith(
            new[] { CaptureTestbed.Nation("attacker"), CaptureTestbed.Nation("defender") },
            new[] { city }, new[] { attacker });

        var rng = new SplitMix64Rng(0x517UL);
        var resolution = InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, rng, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        Assert.Equal(3720, resolution.Result.AttackerPower);
        Assert.Equal(11_500, resolution.Result.DefenderPower);
        Assert.Equal(BattleSide.Defender, resolution.Result.Winner);
        Assert.NotEmpty(resolution.Result.UnitCasualties);
        var attackerAfter = resolution.State.ArmyById("army")!;
        Assert.True(attackerAfter.TotalTroops < attacker.TotalTroops);
    }
}
