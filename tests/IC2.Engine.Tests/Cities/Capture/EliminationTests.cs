using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// <c>docs/task-catalogue.md</c> T17, Done-when 4, as corrected by T86: "losing the last city eliminates
/// the nation (unity reset)" is now <see cref="CityCaptureResolver.Defect"/>'s own path alone -- a forced
/// capture that empties a nation always goes through <see cref="ConquestTrigger"/>/
/// <see cref="ConquestCascade"/> instead, since conquest fires at fewer than
/// <see cref="Model.CaptureRules.ConquestCityCountThreshold"/> (6) cities, strictly before a capture could
/// ever reach zero. <see cref="NationElimination"/> itself is exercised directly here (defection's own
/// capital-preserving, no-conquest-news shape); the capture-reaches-zero shape is exercised through
/// <see cref="CityCaptureResolver.Capture"/> in <c>ConquestCascadeTests</c> instead, since it is now a
/// conquest, not a bare elimination.
/// </summary>
public sealed class EliminationTests
{
    [Fact]
    public void ApplyIfLastCityLost_NationStillOwningACity_IsUnchanged()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City("c1", "City", 0, 0, "nation", "nation", 50, 0, 10, 20, 5);
        var nation = CaptureTestbed.Nation("nation", unity: 400, capitalCityId: "c1");
        var state = CaptureTestbed.StateWith(new[] { nation }, new[] { city });

        var (result, justEliminated) = NationElimination.ApplyIfLastCityLost(state, nation, ruleset, conquerorId: "receiver");

        Assert.False(justEliminated);
        Assert.Equal(nation, result);
    }

    /// <summary>
    /// Mutation proof: delete the <c>Eliminated</c>-guard's early return and this fails, because an
    /// already-eliminated nation (now owning zero cities, exactly as it did the moment it was eliminated)
    /// would be reported <c>JustEliminated: true</c> a second time.
    /// </summary>
    [Fact]
    public void ApplyIfLastCityLost_AlreadyEliminated_ReturnsUnchangedAndNotJustEliminated()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var nation = CaptureTestbed.Nation("nation", unity: 0, eliminated: true);
        var state = CaptureTestbed.StateWith(new[] { nation }, Array.Empty<CityState>());

        var (result, justEliminated) = NationElimination.ApplyIfLastCityLost(state, nation, ruleset, conquerorId: "receiver");

        Assert.False(justEliminated);
        Assert.Equal(nation, result);
    }

    /// <summary>
    /// T86: the capital is deliberately left stale here -- <c>FUN_0044BED8</c>'s own elimination block
    /// never writes nation-record <c>+0x444</c> at all (<c>decompiled-elimination-cleanup.md</c> §4),
    /// unlike the conquest cascade's own explicit sentinel write. Mutation proof: writing
    /// <c>CapitalCityId = null</c> here (this method's own pre-T86 behaviour) would still pass every
    /// other assertion in this file but fails this one directly.
    /// </summary>
    [Fact]
    public void ApplyIfLastCityLost_ZeroCitiesRemaining_ResetsUnityAndSetsConqueredByButLeavesCapitalStale()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var nation = CaptureTestbed.Nation("galatia", unity: 668, capitalCityId: "ancyra");
        // Every one of galatia's cities now belongs to someone else.
        var city = CaptureTestbed.City("ancyra", "Ancyra", 0, 0, "seleucid", "galatia", 40, 0, 10, 20, 5);
        var state = CaptureTestbed.StateWith(new[] { nation, CaptureTestbed.Nation("seleucid") }, new[] { city });

        var (result, justEliminated) = NationElimination.ApplyIfLastCityLost(state, nation, ruleset, conquerorId: "seleucid");

        Assert.True(justEliminated);
        Assert.True(result.Eliminated);
        Assert.Equal("ancyra", result.CapitalCityId); // stale, not cleared -- see this test's own remarks.
        Assert.Equal("seleucid", result.ConqueredBy);
        Assert.Equal(ruleset.Capture.EliminationUnityReset, result.Unity);
        Assert.Equal(0, result.Unity); // The confirmed Galatia figure: 668 -> 0.
    }

    /// <summary>
    /// T86: a one-city nation whose city IS its own capital now goes through the conquest trigger, not a
    /// bare elimination -- but with only 0 cities remaining and cityCount (0) not over
    /// <see cref="Model.CaptureRules.CapitalMoveCityCountThreshold"/> (6), no capital-move attempt is even
    /// made, so it is conquered outright either way. The observable shape (eliminated, capital cleared,
    /// unity reset, conquered-by set, NationConquered published) is unchanged from before T86, even
    /// though the code path underneath (ConquestCascade, not NationElimination) is entirely new -- proven
    /// by <c>ConquestCascadeTests</c>' own dedicated tests, this one only pins the end-to-end shape
    /// through the real command, plus the two-entity probe.
    /// </summary>
    [Fact]
    public void Capture_OfTheLastCity_ConquersTheOldOwner_AndPublishesNationConquered()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "lastcity", "Last City", 0, 0, "doomed", "doomed", 40, 0, 10, 20, 5);
        var doomed = CaptureTestbed.Nation("doomed", unity: 668, capitalCityId: "lastcity");
        var conqueror = CaptureTestbed.Nation("conqueror");
        var thirdNation = CaptureTestbed.Nation("third", unity: 500, capitalCityId: "third-capital");
        var thirdCity = CaptureTestbed.City("third-capital", "Third Capital", 50, 50, "third", "third", 80, 0, 15, 30, 5);
        var attacker = CaptureTestbed.Army("army", "conqueror", 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1000));

        var state = CaptureTestbed.StateWith(
            new[] { doomed, conqueror, thirdNation }, new[] { city, thirdCity }, new[] { attacker });

        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.Capture(
            state, "army", "lastcity", ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        var doomedAfter = result.NationById("doomed")!;
        Assert.True(doomedAfter.Eliminated);
        Assert.Null(doomedAfter.CapitalCityId);
        Assert.Equal(0, doomedAfter.Unity);
        Assert.Equal("conqueror", doomedAfter.ConqueredBy);

        var conquered = Assert.Single(sink.Events.OfType<NationConquered>());
        Assert.Equal("conqueror", conquered.ConqueringNation);
        Assert.Equal("doomed", conquered.ConqueredNation);
        Assert.Single(sink.Events.OfType<CityFallsToNation>());

        // Two-entity probe: the third nation and its capital are untouched.
        Assert.Equal(thirdNation, result.NationById("third"));
        Assert.Equal(thirdCity, result.CityById("third-capital"));
    }

    /// <summary>
    /// T86 (#368 item 7): the original's defection elimination block writes no conquest news, no
    /// treasury change and no capital sentinel, unlike conquest's own. This directly pins that contrast
    /// against <see cref="Capture_OfTheLastCity_ConquersTheOldOwner_AndPublishesNationConquered"/>'s own
    /// shape: same "loses its only, capital city" scenario, opposite mechanism, different result.
    /// </summary>
    [Fact]
    public void Defect_OfTheLastCity_EliminatesTheOldOwnerWithoutConquestNewsOrACapitalSentinel()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City("lastcity", "Last City", 0, 0, "doomed", "doomed", 30, 0, 10, 20, 5);
        var doomed = CaptureTestbed.Nation("doomed", unity: 668, capitalCityId: "lastcity", treasury: 500);
        var newOwner = CaptureTestbed.Nation("newowner");
        var state = CaptureTestbed.StateWith(new[] { doomed, newOwner }, new[] { city });

        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.Defect(state, "lastcity", "newowner", ruleset, sink);

        var doomedAfter = result.NationById("doomed")!;
        Assert.True(doomedAfter.Eliminated);
        Assert.Equal("lastcity", doomedAfter.CapitalCityId); // stale, not cleared.
        Assert.Equal(0, doomedAfter.Unity);
        Assert.Equal("newowner", doomedAfter.ConqueredBy);
        Assert.Equal(500, doomedAfter.Treasury); // untouched -- defection never changes it, even at elimination.
        Assert.Empty(sink.Events.OfType<NationConquered>());
    }
}
