using System.Linq;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// T91 Done-when 2 (bug #416): <see cref="CityCaptureResolver.Capture"/>'s own post-cascade branch --
/// capital-move-or-conquer versus the plain "&lt; 6 cities" conquest -- decides on
/// <see cref="CapitalOwnership.IsAnyNationsCapital"/>, not on whether the captured city was the CURRENT
/// loser's own capital. This is call site 2 of <see cref="CapitalOwnership"/>'s own list of four (the
/// cascade's exclusion gate, the destination-candidate scoring and the initiating siege's own scoring are
/// each pinned elsewhere -- <c>CascadeGateTests</c>, <c>ConquestTriggerTests</c>'
/// <c>BuildLoyaltyBoundaryDestinationScenario</c>, and <c>SiegeBattleTests</c> respectively); this file is
/// the only place that exercises <see cref="CityCaptureResolver.Capture"/>'s own <c>wasCapital</c> read.
/// </summary>
/// <remarks>
/// <para>
/// <strong>T92 (bug #424): the original two-real-capture reconstruction no longer reaches this state.</strong>
/// An earlier revision of this test built the scenario through two live <see cref="CityCaptureResolver.Capture"/>
/// calls: the first capturing "x"'s own capital while its only other city defected away in the same
/// cascade, eliminating "x" while <see cref="ConquestTrigger.Evaluate"/>'s own (then-present)
/// <c>if (loser.Eliminated)</c> guard skipped the conquest routine entirely, leaving "x"'s
/// <see cref="NationState.CapitalCityId"/> stale rather than cleared. Bug #424 removed that guard: a
/// capture whose sweep empties the loser now always runs <see cref="ConquestCascade.Apply"/> against it,
/// same as the original <c>FUN_0044BB18</c> always did, and that routine nulls the loser's own capital
/// pointer. The two-capture reconstruction's own first step is therefore no longer reachable with a stale
/// (non-null) result -- it is the exact gap #424 closed, not a side effect of this task.
/// </para>
/// <para>
/// <strong>Review round 1, B3: the state stays reachable in the ORIGINAL, through rebirth, then
/// rebellion -- just not yet through this engine.</strong> An earlier revision of this remark claimed
/// rebellion "cannot reach it either" and that "the only way a capital city ever changes hands is a
/// direct siege capture", concluding "there is no reachable 'eliminated, but still stale' window left to
/// construct through live play" -- true of THIS engine, since it has no rebirth yet (<see cref="IC2.Engine.Economy.Rebellion"/>'s
/// own remarks: <c>FUN_0044C360</c> is left to T87), but false of the original the task's own Scope cites
/// ("Stale capitals stay reachable in the original through defection by rebellion … and through rebirth").
/// Rebirth (<c>FUN_0044C360</c>) defects every city whose allegiance is the reborn nation and whose
/// loyalty is under 40, through <c>FUN_0044BED8</c> (:50559) -- with no <c>FUN_0044B8D0</c> capital test
/// of its own, unlike the regular cascade's gate and rebellion's own caller. So a LIVE nation's capital
/// city can change hands this way: if it was that nation's last city, the nation is eliminated
/// immediately with a stale (non-null) capital pointer, since <c>FUN_0044BED8</c> writes no capital field
/// at all; if it was not the last city, the quarterly rebellion (caller :54851-54856, which skips only a
/// city that IS a capital) can later take the nation's remaining non-capital cities and eliminate it with
/// that same stale pointer -- the Scope's own "defection by rebellion" path. Once this engine models
/// rebirth (T87 or whoever adds it), this construction should be replaced with that live path; until then,
/// a directly built state is an acceptable stand-in, per <strong>What replaces it</strong> below.
/// </para>
/// <para>
/// <strong>What replaces it.</strong> The property this test pins -- <c>Capture</c>'s own branch decision
/// reads ANY nation's capital pointer, alive or eliminated, never just the current loser's -- is
/// independent of how the eliminated nation's record came to exist. <c>ConquestTriggerTests</c>' own
/// <c>BuildLoyaltyBoundaryDestinationScenario</c> and <c>CascadeGateTests</c>' equivalent already prove the
/// other three <see cref="CapitalOwnership.IsAnyNationsCapital"/> call sites this same way -- a directly
/// built, already-eliminated nation whose <see cref="NationState.CapitalCityId"/> names a city a live
/// nation now owns, with no attempt to replay how it got that way. This file does the same for the
/// fourth (<c>Capture</c>'s own <c>wasCapital</c>): "gone" is built pre-eliminated, owning nothing, its
/// stale pointer naming a city the (live, still-capitaled-elsewhere) old owner currently holds. The single
/// <see cref="CityCaptureResolver.Capture"/> call this test drives is itself perfectly ordinary and fully
/// reachable; only the third nation's own backstory is asserted rather than replayed.
/// </para>
/// <para>
/// Review round 1, N1: an earlier revision of this remark said the original reaches the conquest routine
/// "regardless of how many cities the loser has left" -- overstated, since the count still gates the
/// capital-move attempt itself (<see cref="Model.CaptureRules.CapitalMoveCityCountThreshold"/>). What #424
/// actually establishes is narrower: the conquest routine is still reached for a loser the sweep has
/// emptied to zero, which the engine's own guard used to skip.
/// </para>
/// </remarks>
public sealed class StaleCapitalPostSweepBranchTests
{
    private const string OldOwner = "n";
    private const string NewOwner = "m";
    private const string StaleNation = "gone";

    [Fact]
    public void CaptureOfALiveNationsCity_NamedByAnEliminatedThirdNationsStaleCapital_TakesTheCapitalMoveBranch()
    {
        var ruleset = CaptureTestbed.Ruleset;

        // "n"'s own real capital, and six near fillers that keep it at loyalty 90 / distance 6 or less --
        // clear of both the regular cascade's own loyalty gate (65) and, moot here anyway, since n's own
        // unity after this capture's -15 (653) already stays at or above CascadeUnityThreshold (650), so
        // no candidate in the cascade this capture triggers can qualify regardless of distance or loyalty.
        var nCapital = CaptureTestbed.City(
            "n-capital", "N Capital", 500, 500, OldOwner, OldOwner,
            loyalty: 90, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 0);
        var nFillers = CaptureTestbed.FillerCities(OldOwner, 6, 501, 500);

        // Chebyshev distance 500 from n-capital -- well past CapitalMoveMinDistanceTiles (10). Loyalty 65
        // (not the original revision's 10): that revision's own destination reached this search already
        // sitting at 65, LoyaltyAfterDefection's own non-allegiant floor (100 - 10 = 90, clamped to
        // [50, 65]) -- a side effect of step 1's live defection, which this direct construction has no
        // replay of. Built here at that same settled value instead: weighted sum 65x150 + 1x200 = 9950;
        // /10 = 995; /500 = 1 -- strictly positive, so this qualifies as the only candidate past the
        // distance gate.
        var nOther = CaptureTestbed.City(
            "n-other", "N Other", 0, 0, OldOwner, OldOwner,
            loyalty: 65, fortificationCode: 0, populationThousands: 1, maxPopulationThousands: 10, tribute: 0);

        // The captured city: n's own, but never n's own capital -- only "gone"'s stale pointer names it.
        var xCapital = CaptureTestbed.City(
            "x-capital", "X Capital", 1, 1, OldOwner, OldOwner,
            loyalty: 10, fortificationCode: 0, populationThousands: 1, maxPopulationThousands: 10, tribute: 0);

        var n = CaptureTestbed.Nation(OldOwner, unity: 668, capitalCityId: "n-capital");
        var m = CaptureTestbed.Nation(NewOwner, unity: 500);
        // Built pre-eliminated, owning nothing -- see this type's own remarks: no live replay is needed to
        // reach "an eliminated nation's stale capital pointer names a city a live nation now owns", and
        // #424 leaves no reachable live-play window that produces it through this file's own call site.
        var gone = CaptureTestbed.Nation(StaleNation, capitalCityId: "x-capital", eliminated: true);

        var attacker = CaptureTestbed.Army(
            "m-army", NewOwner, 1, 1, morale: 80, CaptureTestbed.Unit("heavy_infantry", 50_000));

        var cities = new[] { nCapital, nOther, xCapital }.Concat(nFillers).ToArray();
        var state = CaptureTestbed.StateWith(new[] { n, m, gone }, cities, new[] { attacker });

        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.Capture(
            state, "m-army", "x-capital", ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        // The confirmed reading: n takes the capital-move-or-conquer branch even though "x-capital" was
        // never n's OWN capital -- only the eliminated "gone"'s stale pointer names it. Unity pays the
        // capture's own -15 AND the capital-move attempt's own -50 (668 - 15 - 50 = 603), and a valid
        // destination exists ("n-other", the only one of n's remaining cities more than
        // CapitalMoveMinDistanceTiles from "n-capital") so the move succeeds.
        var nFinal = result.NationById(OldOwner)!;
        Assert.False(nFinal.Eliminated);
        Assert.Equal(603, nFinal.Unity);
        Assert.Equal("n-other", nFinal.CapitalCityId);
        var moved = Assert.Single(sink.Events.OfType<NationCapitalMoved>());
        Assert.Equal(OldOwner, moved.Nation);
        Assert.Empty(sink.Events.OfType<NationConquered>());

        // A mutation that checks only the CURRENT loser's (n's) own capital would see "n-capital" !=
        // "x-capital" and take the plain city-count branch instead: cityCount after this capture is 8
        // (n-capital + 6 fillers + n-other), which is NOT under ConquestCityCountThreshold (6), so nothing
        // would happen at all -- no unity change beyond the plain -15, no capital move, n's own capital
        // staying "n-capital". This test's own assertions above (Unity == 603, CapitalCityId == "n-other",
        // one NationCapitalMoved) fail under that reading, which is exactly what the mutation proof needs.
        Assert.Equal(8, result.CountCitiesOwnedBy(OldOwner));

        // "gone" itself is untouched by this capture -- still eliminated, still owning nothing, its own
        // stale pointer unaffected by n's capital move.
        var goneFinal = result.NationById(StaleNation)!;
        Assert.True(goneFinal.Eliminated);
        Assert.Equal("x-capital", goneFinal.CapitalCityId);
        Assert.Equal(0, result.CountCitiesOwnedBy(StaleNation));
    }
}
