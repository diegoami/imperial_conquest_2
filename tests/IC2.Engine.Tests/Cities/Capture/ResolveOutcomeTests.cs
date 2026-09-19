using System.Linq;
using IC2.Engine.Battle;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// <see cref="CityCaptureResolver.ResolveOutcome"/>: T17's own wiring onto T16's already-resolved
/// <see cref="BattleResult"/> (DoD 6). Uses the real, un-mocked
/// <see cref="InstantBattleResolver.ResolveSiege"/> so this test also proves
/// <see cref="CityCaptureResolver.ResolveOutcome"/> reads <see cref="BattleResult.Winner"/> as the sole
/// win/loss authority, rather than re-deriving it (see <see cref="CompleteDefenderStrength"/>'s remarks
/// on why the ×9/10 reduction must not be applied a second time).
/// </summary>
public sealed class ResolveOutcomeTests
{
    [Fact]
    public void AttackerWins_TransfersTheCity_AndPublishesFallsTo()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "defender", "defender", loyalty: 20, fortificationCode: 0,
            populationThousands: 1, maxPopulationThousands: 10, tribute: 0);
        var attacker = CaptureTestbed.Army(
            "army", "attacker", 0, 0, morale: 90, CaptureTestbed.Unit("heavy_infantry", 20_000));
        var state = CaptureTestbed.StateWith(
            new[] { CaptureTestbed.Nation("attacker"), CaptureTestbed.Nation("defender") },
            new[] { city }, new[] { attacker });

        var rng = new SplitMix64Rng(0x517UL);
        var siege = InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, rng, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);
        Assert.Equal(BattleSide.Attacker, siege.Result.Winner);

        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.ResolveOutcome(
            siege.State, siege.Result, ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        Assert.Equal("attacker", result.CityById("c1")!.Owner);
        Assert.Single(sink.Events.OfType<CityFallsToNation>());
        Assert.DoesNotContain(sink.Events, e => e is CityFailsToBeCaptured);
    }

    /// <summary>
    /// The defender holds: only <c>city.fails-to-capture</c> is published, and nothing about the city or
    /// either nation changes beyond what <see cref="InstantBattleResolver.ResolveSiege"/> itself already
    /// did (the attacker's own attrition) before <see cref="CityCaptureResolver.ResolveOutcome"/> was
    /// ever called.
    /// </summary>
    [Fact]
    public void AttackerLoses_TransfersNothing_AndPublishesFailsToCapture()
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
        var siege = InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, rng, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);
        Assert.Equal(BattleSide.Defender, siege.Result.Winner);

        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.ResolveOutcome(
            siege.State, siege.Result, ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        Assert.Equal("defender", result.CityById("c1")!.Owner);
        Assert.Equal(siege.State, result); // No further state change beyond what ResolveSiege already made.

        var failed = Assert.Single(sink.Events.OfType<CityFailsToBeCaptured>());
        Assert.Equal("attacker", failed.AttackerNation);
        Assert.Equal("City", failed.CityName);
        Assert.Equal("defender", failed.DefenderNation);
        Assert.DoesNotContain(sink.Events, e => e is CityFallsToNation);
    }
}
