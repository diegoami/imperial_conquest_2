using System.Linq;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// T90/#409, Done-when 3: bug #407's own symptom, reproduced through the real capture command rather than
/// asserted against <see cref="CityCaptureResolver.Defect"/> in isolation. Before this fix, a cascade whose
/// only remaining candidate was the loser's own capital took it anyway (T17's <see cref="CityState.UnderSiege"/>
/// gate never excluded a capital), which could empty the loser down to its last city through
/// <em>defection</em> — publishing no <see cref="NationConquered"/>, crediting no treasury, granting no
/// conquest unity — instead of through the conquest path <see cref="ConquestCascade"/> owns. This scenario
/// pins the corrected shape: the capital survives the cascade, the loser is left owning only that one city,
/// and the conquest trigger (T86, already below the six-city threshold) decides the rest.
/// </summary>
public sealed class CascadeCapitalConquestTests
{
    private const string Loser = "loser";
    private const string Winner = "winner";
    private const string CapturedId = "captured";
    private const string CapitalId = "capital";

    [Fact]
    public void CascadeThatWouldHaveTakenTheLastCityIsTheLosersOwnCapital_LeavesItForConquestInstead()
    {
        var ruleset = CaptureTestbed.Ruleset;

        // The city taken by the initiating siege -- not the capital, so ConquestTrigger.Evaluate's own
        // capital-move branch (a different mechanism, not this task's Owns) never engages.
        var captured = CaptureTestbed.City(
            CapturedId, "Captured", 0, 0, Loser, Loser,
            loyalty: 10, fortificationCode: 0, populationThousands: 1, maxPopulationThousands: 10, tribute: 0);

        // The loser's ONLY other city -- also its capital -- placed and weakened exactly like
        // CascadeGateTests' own always-qualifying baseline, so it would defect under every gate BUT the
        // capital one: within CascadeDistanceMax, loyalty well under CascadeLoyaltyThreshold, and a
        // defense the attacker's strength comfortably beats.
        var capital = CaptureTestbed.City(
            CapitalId, "Capital", 1, 0, Loser, Loser,
            loyalty: 10, fortificationCode: 0, populationThousands: 1, maxPopulationThousands: 10, tribute: 0);

        var loser = CaptureTestbed.Nation(Loser, unity: 600, capitalCityId: CapitalId);
        var winner = CaptureTestbed.Nation(Winner, unity: 500);
        var attacker = CaptureTestbed.Army(
            "army", Winner, 0, 0, morale: 80, CaptureTestbed.Unit("heavy_infantry", 50_000));

        var state = CaptureTestbed.StateWith(
            new[] { loser, winner }, new[] { captured, capital }, new[] { attacker });

        Assert.Equal(2, state.CountCitiesOwnedBy(Loser));

        var sink = new RecordingEventSink();
        var final = CityCaptureResolver.Capture(
            state, "army", CapturedId, ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        // The capital never defects -- the ONLY candidate the cascade ever considers is excluded outright,
        // so no CityDefectsToNation fires at all.
        Assert.Empty(sink.Events.OfType<CityDefectsToNation>());

        // Exactly one CityFallsToNation, for the siege's own capture -- unaffected by this task.
        var fallsTo = Assert.Single(sink.Events.OfType<CityFallsToNation>());
        Assert.Equal("Captured", fallsTo.CityName);

        // The loser is left owning only its capital after the siege+cascade -- 1 city, under
        // CaptureRules.ConquestCityCountThreshold -- so the SAME call's conquest trigger fires and takes
        // it too. That is the conquest path, not defection: exactly one NationConquered, the capital ends
        // up with the winner, the loser's capital sentinel is cleared (ConquestCascade's own effect list,
        // unlike a defection-only elimination), and the loser owns nothing.
        var conquered = Assert.Single(sink.Events.OfType<NationConquered>());
        Assert.Equal(Winner, conquered.ConqueringNation);
        Assert.Equal(Loser, conquered.ConqueredNation);

        Assert.Equal(Winner, final.CityById(CapitalId)!.Owner);
        Assert.Equal(0, final.CountCitiesOwnedBy(Loser));

        var loserAfter = final.NationById(Loser)!;
        Assert.True(loserAfter.Eliminated);
        Assert.Null(loserAfter.CapitalCityId);
        Assert.Equal(Winner, loserAfter.ConqueredBy);
    }
}
