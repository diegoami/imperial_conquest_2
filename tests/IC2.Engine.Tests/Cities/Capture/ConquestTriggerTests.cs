using System.Linq;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// T86 Done-when 1: <c>FUN_0044BB18</c>'s own post-cascade checks (<c>decompiled-elimination-cleanup.md</c>
/// §4), each boundary pinned independently, driven through the real <see cref="CityCaptureResolver.Capture"/>
/// command — never <see cref="ConquestTrigger"/> called directly, so a mutation to <see cref="CityCaptureResolver"/>'s
/// own wiring (which state it evaluates the trigger against, in which order) is caught here too, the same
/// reasoning <c>CascadeGateTests</c>' own remarks give for testing through <c>Capture</c>.
/// </summary>
public sealed class ConquestTriggerTests
{
    private const string OldOwner = "old";
    private const string NewOwner = "new";

    private static Ruleset Ruleset => CaptureTestbed.Ruleset;

    // Loyalty 90 -- at or above CaptureRules.CascadeLoyaltyThreshold (65) -- so a filler city never
    // qualifies for CityCaptureResolver's own regular defection cascade regardless of its distance from
    // the attacker; without this, the cascade could sweep a filler away in the very same call, before
    // the conquest trigger this file tests even runs, silently changing the city counts these scenarios
    // are built around.
    private static CityState Filler(string id, int x, int y) => CaptureTestbed.City(
        id, id, x, y, OldOwner, OldOwner, loyalty: 90, fortificationCode: 0,
        populationThousands: 10, maxPopulationThousands: 20, tribute: 0);

    private static (GameState State, string CapturedCityId) BuildScenario(
        bool captureCapital, int fillerCount, int unity, int?[]? fillerXCoordinates = null)
    {
        var ruleset = Ruleset;
        var capitalId = "capital";
        // Loyalty 90, the same reason Filler uses it: when the capital itself is not the city being
        // captured, it must not be swept away by the regular defection cascade too (it starts within
        // CascadeDistanceMax of the attacker in every non-capital scenario here), which would silently
        // change these scenarios' own carefully-built city counts.
        var capital = CaptureTestbed.City(
            capitalId, "Capital", 0, 0, OldOwner, OldOwner, loyalty: 90, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 0);

        var fillers = new List<CityState>();
        for (var i = 0; i < fillerCount; i++)
        {
            var x = fillerXCoordinates is not null && i < fillerXCoordinates.Length && fillerXCoordinates[i] is { } explicitX
                ? explicitX
                : 1; // close to the capital by default -- never itself a qualifying capital-move destination.
            fillers.Add(Filler($"filler-{i}", x, 0));
        }

        var oldOwner = CaptureTestbed.Nation(OldOwner, unity: unity, capitalCityId: capitalId);
        var newOwner = CaptureTestbed.Nation(NewOwner);
        var capturedCityId = captureCapital ? capitalId : "filler-0";
        var attackerX = captureCapital ? 0 : 1;
        var attacker = CaptureTestbed.Army(
            "army", NewOwner, attackerX, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1_000_000));

        var allCities = new[] { capital }.Concat(fillers).ToArray();
        var state = CaptureTestbed.StateWith(new[] { oldOwner, newOwner }, allCities, new[] { attacker });

        return (state, capturedCityId);
    }

    private static GameState Capture((GameState State, string CapturedCityId) scenario, RecordingEventSink sink) =>
        CityCaptureResolver.Capture(
            scenario.State, "army", scenario.CapturedCityId, Ruleset,
            CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

    // ---- Non-capital capture: cityCount < ConquestCityCountThreshold (6) conquers. ----

    [Fact]
    public void NonCapitalCapture_LeavingSixCities_DoesNotConquer()
    {
        // 1 capital + 6 fillers = 7 total; losing one filler leaves 6 (capital + 5 remaining fillers).
        var scenario = BuildScenario(captureCapital: false, fillerCount: 6, unity: 668);
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.False(result.NationById(OldOwner)!.Eliminated);
        Assert.Equal(6, result.CountCitiesOwnedBy(OldOwner));
        Assert.Empty(sink.Events.OfType<NationConquered>());
    }

    [Fact]
    public void NonCapitalCapture_LeavingFiveCities_Conquers()
    {
        // 1 capital + 5 fillers = 6 total; losing one filler leaves 5 (capital + 4 remaining fillers).
        var scenario = BuildScenario(captureCapital: false, fillerCount: 5, unity: 668);
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.True(result.NationById(OldOwner)!.Eliminated);
        Assert.Equal(0, result.CountCitiesOwnedBy(OldOwner));
        Assert.Single(sink.Events.OfType<NationConquered>());
    }

    // ---- Capital capture, unity gate: > CapitalMoveUnityThreshold (400) attempts a move; at or below, conquered outright. ----

    [Fact]
    public void CapitalCapture_AtUnityFourHundred_DoesNotAttemptAMove_AndConquers()
    {
        var rules = Ruleset.Capture;
        // 8 total (capital + 7 fillers), one far enough to be a valid destination if an attempt were
        // made -- proving the unity gate alone blocks the attempt, not a missing destination.
        var scenario = BuildScenario(
            captureCapital: true, fillerCount: 7, unity: 400 + rules.CaptureUnityLoss,
            fillerXCoordinates: new int?[] { 20 });
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.True(result.NationById(OldOwner)!.Eliminated);
        Assert.Empty(sink.Events.OfType<NationCapitalMoved>());
        Assert.Single(sink.Events.OfType<NationConquered>());
    }

    [Fact]
    public void CapitalCapture_AtUnityFourHundredAndOne_AttemptsAMove_AndSucceeds()
    {
        var rules = Ruleset.Capture;
        var scenario = BuildScenario(
            captureCapital: true, fillerCount: 7, unity: 401 + rules.CaptureUnityLoss,
            fillerXCoordinates: new int?[] { 20 });
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.False(result.NationById(OldOwner)!.Eliminated);
        Assert.Single(sink.Events.OfType<NationCapitalMoved>());
        Assert.Empty(sink.Events.OfType<NationConquered>());
        Assert.Equal("filler-0", result.NationById(OldOwner)!.CapitalCityId);
        // Unity: 401 (post capture-unity-loss) - 50 (the capital-move attempt's own unconditional cost).
        Assert.Equal(401 - rules.CapitalMoveUnityLoss, result.NationById(OldOwner)!.Unity);
    }

    // ---- Capital capture, city-count gate: > CapitalMoveCityCountThreshold (6) attempts a move. ----

    [Fact]
    public void CapitalCapture_WithSixCitiesRemaining_DoesNotAttemptAMove_AndConquers()
    {
        // 7 total (capital + 6 fillers); losing the capital leaves exactly 6 -- not over the threshold.
        var scenario = BuildScenario(
            captureCapital: true, fillerCount: 6, unity: 668, fillerXCoordinates: new int?[] { 20 });
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.True(result.NationById(OldOwner)!.Eliminated);
        Assert.Empty(sink.Events.OfType<NationCapitalMoved>());
    }

    [Fact]
    public void CapitalCapture_WithSevenCitiesRemaining_AttemptsAMove_AndSucceeds()
    {
        // 8 total (capital + 7 fillers); losing the capital leaves exactly 7 -- over the threshold.
        var scenario = BuildScenario(
            captureCapital: true, fillerCount: 7, unity: 668, fillerXCoordinates: new int?[] { 20 });
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.False(result.NationById(OldOwner)!.Eliminated);
        Assert.Single(sink.Events.OfType<NationCapitalMoved>());
    }

    // ---- Capital move, destination distance: > CapitalMoveMinDistanceTiles (10) from the fallen capital. ----

    [Fact]
    public void CapitalMove_WithNoCityMoreThanTenTilesAway_Conquers()
    {
        // Farthest filler at exactly 10 tiles (Chebyshev) from the fallen capital (0,0) -- not over the
        // threshold, so no destination qualifies.
        var scenario = BuildScenario(
            captureCapital: true, fillerCount: 7, unity: 668, fillerXCoordinates: new int?[] { 10 });
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.True(result.NationById(OldOwner)!.Eliminated);
        Assert.Empty(sink.Events.OfType<NationCapitalMoved>());
        Assert.Single(sink.Events.OfType<NationConquered>());
        // The attempt still cost 50 unity before conquest's own effects ran -- but conquest itself
        // resets unity to EliminationUnityReset, so that intermediate value is not independently
        // observable here; ConquestCascadeTests pins the reset itself.
    }

    [Fact]
    public void CapitalMove_WithACityElevenTilesAway_Moves()
    {
        var rules = Ruleset.Capture;
        // Farthest filler at 11 tiles -- one past the threshold, so it qualifies as a destination.
        var scenario = BuildScenario(
            captureCapital: true, fillerCount: 7, unity: 668, fillerXCoordinates: new int?[] { 11 });
        var sink = new RecordingEventSink();
        var result = Capture(scenario, sink);

        Assert.False(result.NationById(OldOwner)!.Eliminated);
        var moved = Assert.Single(sink.Events.OfType<NationCapitalMoved>());
        Assert.Equal(OldOwner, moved.Nation);
        Assert.Equal("filler-0", result.NationById(OldOwner)!.CapitalCityId);
        // 668, less the forced capture's own unity loss (every capture pays this, capital or not), less
        // the capital-move attempt's own separate cost.
        Assert.Equal(668 - rules.CaptureUnityLoss - rules.CapitalMoveUnityLoss, result.NationById(OldOwner)!.Unity);
    }
}
