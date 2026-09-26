using System.Linq;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// T91 Done-when 2 (bug #416): the branch after the cascade sweep -- capital-move-or-conquer versus the
/// plain "&lt; 6 cities" conquest -- decides on <see cref="CapitalOwnership.IsAnyNationsCapital"/>, not on
/// whether the captured city was the CURRENT loser's own capital. The reachable case is a city an
/// ELIMINATED nation's stale <see cref="NationState.CapitalCityId"/> still names, while its actual, current
/// owner never held it as a capital at all. Built through two real <see cref="CityCaptureResolver.Capture"/>
/// calls (the entry's own Hazard: "test fixtures must be reachable states... built through real captures"),
/// the same construction T90's own review used to prove the equivalent cascade-gate case reachable
/// (PR #412#issuecomment-5848049029, check (c)):
/// <list type="number">
/// <item><description>
/// N captures X's own capital ("x-capital"). X's only other city ("x-other") then defects in the SAME
/// call's cascade, emptying X to zero cities -- eliminated through <see cref="CityCaptureResolver.Defect"/>,
/// which never clears <see cref="NationState.CapitalCityId"/> (<see cref="NationElimination"/>'s own
/// remarks). X's stale pointer keeps naming "x-capital", which N now owns. Because the elimination happens
/// inside the cascade, <see cref="ConquestTrigger.Evaluate"/> never runs against X at all (its own
/// <c>if (loser.Eliminated)</c> guard) -- no conquest, no capital move, nothing to interfere with the stale
/// pointer surviving.
/// </description></item>
/// <item><description>
/// M captures "x-capital" FROM N. N's own real capital ("n-capital") is untouched and elsewhere -- N never
/// held "x-capital" as its own capital, before or after step 1. Only X's stale pointer still names it. The
/// buggy reading (the CURRENT loser's -- N's -- own capital) says "no"; the confirmed reading
/// (<c>FUN_0044B8D0</c>, any of the sixteen nations') says "yes", and N takes the capital-move-or-conquer
/// branch it would otherwise skip.
/// </description></item>
/// </list>
/// </summary>
public sealed class StaleCapitalPostSweepBranchTests
{
    private const string EliminatedNation = "x";
    private const string MiddleNation = "n";
    private const string FinalAttacker = "m";

    [Fact]
    public void CaptureOfAnEliminatedNationsStaleCapital_TakesTheCapitalMoveBranch_NotThePlainCityCountBranch()
    {
        var ruleset = CaptureTestbed.Ruleset;

        // ---- Step 1: N captures X's own capital, and X's only other city defects in the same cascade,
        // eliminating X while leaving its CapitalCityId stale. ----
        var xCapital = CaptureTestbed.City(
            "x-capital", "X Capital", 0, 0, EliminatedNation, EliminatedNation,
            loyalty: 10, fortificationCode: 0, populationThousands: 1, maxPopulationThousands: 10, tribute: 0);
        var xOther = CaptureTestbed.City(
            "x-other", "X Other", 1, 1, EliminatedNation, EliminatedNation,
            loyalty: 10, fortificationCode: 0, populationThousands: 1, maxPopulationThousands: 10, tribute: 0);

        var nCapital = CaptureTestbed.City(
            "n-capital", "N Capital", 500, 500, MiddleNation, MiddleNation,
            loyalty: 90, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 0);
        var nFillers = Enumerable.Range(0, 6)
            .Select(i => CaptureTestbed.City(
                $"n-filler-{i}", $"N Filler {i}", 501 + i, 500, MiddleNation, MiddleNation,
                loyalty: 90, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 0))
            .ToArray();

        var x = CaptureTestbed.Nation(EliminatedNation, unity: 660, capitalCityId: "x-capital");
        var n = CaptureTestbed.Nation(MiddleNation, unity: 668, capitalCityId: "n-capital");
        var nAttacker = CaptureTestbed.Army(
            "n-army", MiddleNation, 0, 0, morale: 80, CaptureTestbed.Unit("heavy_infantry", 50_000));

        var initialCities = new[] { xCapital, xOther, nCapital }.Concat(nFillers).ToArray();
        var initialState = CaptureTestbed.StateWith(new[] { x, n }, initialCities, new[] { nAttacker });

        var sink = new RecordingEventSink();
        var afterStep1 = CityCaptureResolver.Capture(
            initialState, "n-army", "x-capital", ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        // X is eliminated through the cascade's own defection, not through ConquestCascade -- its stale
        // capital pointer survives, and no conquest news was published for it.
        var xAfterStep1 = afterStep1.NationById(EliminatedNation)!;
        Assert.True(xAfterStep1.Eliminated);
        Assert.Equal("x-capital", xAfterStep1.CapitalCityId);
        Assert.Empty(sink.Events.OfType<NationConquered>());
        Assert.Single(sink.Events.OfType<CityDefectsToNation>());
        Assert.Equal(MiddleNation, afterStep1.CityById("x-capital")!.Owner);
        Assert.Equal(MiddleNation, afterStep1.CityById("x-other")!.Owner);

        // N's own real capital is unaffected -- still "n-capital", still N's.
        Assert.Equal("n-capital", afterStep1.NationById(MiddleNation)!.CapitalCityId);
        Assert.Equal(9, afterStep1.CountCitiesOwnedBy(MiddleNation)); // n-capital + 6 fillers + x-capital + x-other.

        // ---- Step 2: M captures "x-capital" FROM N. N's own capital is "n-capital", untouched -- only
        // X's stale pointer still names "x-capital". ----
        var m = CaptureTestbed.Nation(FinalAttacker, unity: 500);
        var mAttacker = CaptureTestbed.Army(
            "m-army", FinalAttacker, 0, 0, morale: 80, CaptureTestbed.Unit("heavy_infantry", 50_000));
        var stateForStep2 = afterStep1 with
        {
            Nations = ValueList.From(afterStep1.Nations.Append(m)),
            Armies = ValueList.From(new[] { mAttacker }),
        };

        var sink2 = new RecordingEventSink();
        var final = CityCaptureResolver.Capture(
            stateForStep2, "m-army", "x-capital", ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink2);

        // The confirmed reading: N takes the capital-move-or-conquer branch, even though "x-capital" was
        // never N's OWN capital. Unity pays the capture's own -15 AND the capital-move attempt's own -50
        // (668 + 9 [step 1 capture] + 3 [x-other's defection] - 15 [step 2 capture] - 50 [move attempt] =
        // 615), and a valid destination exists ("x-other", the only one of N's remaining cities more than
        // CapitalMoveMinDistanceTiles from "n-capital") so the move succeeds.
        var nFinal = final.NationById(MiddleNation)!;
        Assert.False(nFinal.Eliminated);
        Assert.Equal(615, nFinal.Unity);
        Assert.Equal("x-other", nFinal.CapitalCityId);
        var moved = Assert.Single(sink2.Events.OfType<NationCapitalMoved>());
        Assert.Equal(MiddleNation, moved.Nation);

        // A mutation that checks only the CURRENT loser's (N's) own capital would see "n-capital" !=
        // "x-capital" and take the plain city-count branch instead: cityCount after this capture is 8
        // (n-capital + 6 fillers + x-other), which is NOT under ConquestCityCountThreshold (6), so nothing
        // would happen at all -- no unity change beyond the plain -15, no capital move, N's own capital
        // staying "n-capital". This test's own assertions above (Unity == 615, CapitalCityId == "x-other",
        // one NationCapitalMoved) fail under that reading, which is exactly what the mutation proof for
        // Done-when 2 needs: checking only the loser's own capital.
        Assert.Equal(8, final.CountCitiesOwnedBy(MiddleNation));
    }
}
