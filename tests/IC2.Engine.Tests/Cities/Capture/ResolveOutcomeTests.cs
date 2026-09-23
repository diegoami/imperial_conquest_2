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

    /// <summary>
    /// T63 Decision 3: a besieger that WINS the strength comparison but is emptied by its own siege
    /// casualties (#289's deletion pass) does not capture -- the original captures anyway and leaves the
    /// city with owner -1, which <see cref="Model.CityState.Owner"/> (a non-null nation id) cannot
    /// represent, so both presets treat this as a failed attempt instead, after erosion has already run.
    /// A single, already-small archer unit (340 troops, below the 350 archer deletion threshold before
    /// any casualties at all) against an essentially defenceless city (a plain strength sum of 0) wins the
    /// comparison (120 &gt; 0) but is wiped by the deletion pass regardless of the (floor-clamped) ratio.
    /// </summary>
    /// <remarks>
    /// N7 (T63 review round 1): <see cref="InstantBattleResolver.ResolveSiege"/> now folds this same
    /// emptied-besieger check into <see cref="BattleResult.Winner"/> itself, so it already reads
    /// <see cref="BattleSide.Defender"/> here, not <see cref="BattleSide.Attacker"/> -- a result must not
    /// claim a win the city did not yield. This test's own title still holds ("does not capture, fails
    /// instead"); only the intermediate <c>Winner</c> assertion changed to match. See
    /// <c>SiegeAttritionTests.EmptiedBesieger_ReportsTheDefenderAsWinner_NotACaptureTheCityDidNotYield</c>
    /// for the focused proof of that field itself, with exact casualty-count assertions this test does
    /// not repeat.
    /// </remarks>
    [Fact]
    public void AttackerWinsButIsEmptiedByItsOwnCasualties_DoesNotCapture_FailsInstead()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "defender", "defender", loyalty: 0, fortificationCode: 0,
            populationThousands: 0, maxPopulationThousands: 10, tribute: 0);
        var attacker = CaptureTestbed.Army(
            "army", "attacker", 0, 0, morale: 10, CaptureTestbed.Unit(CaptureTestbed.ArcherUnitTypeId, 340));
        var state = CaptureTestbed.StateWith(
            new[] { CaptureTestbed.Nation("attacker"), CaptureTestbed.Nation("defender") },
            new[] { city }, new[] { attacker });

        var rng = new SplitMix64Rng(0x517UL);
        var siege = InstantBattleResolver.ResolveSiege(
            state, "army", "c1", ruleset, rng, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        // atk = (340 x 3 / 80) x 10 = 120; def = 0 -- the attacker wins the strength comparison...
        Assert.Equal(120, siege.Result.AttackerPower);
        Assert.Equal(0, siege.Result.DefenderPower);

        // ...but the deletion pass's own sweep already emptied the attacking army during ResolveSiege
        // itself (#289), and (N7) ResolveSiege folds that into the reported result: Winner reads
        // Defender, not Attacker, even though the attacker had the higher power.
        Assert.Null(siege.State.ArmyById("army"));
        Assert.Equal(BattleSide.Defender, siege.Result.Winner);
        Assert.False(siege.Result.AttackerWon);

        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.ResolveOutcome(
            siege.State, siege.Result, ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        // Decision 3: the city stays with its original owner -- no capture -- even though the attacker
        // won the strength comparison.
        Assert.Equal("defender", result.CityById("c1")!.Owner);
        Assert.Single(sink.Events.OfType<CityFailsToBeCaptured>());
        Assert.DoesNotContain(sink.Events, e => e is CityFallsToNation);
    }
}
