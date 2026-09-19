using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// <c>docs/task-catalogue.md</c> T17, Done-when 4: "Losing the last city eliminates the nation (capital
/// sentinel set, unity reset)." Exercised both directly (<see cref="NationElimination"/>) and through
/// <see cref="CityCaptureResolver.Capture"/>/<see cref="CityCaptureResolver.Defect"/>, whichever
/// mechanism actually takes the last city.
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

        var (result, justEliminated) = NationElimination.ApplyIfLastCityLost(state, nation, ruleset);

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

        var (result, justEliminated) = NationElimination.ApplyIfLastCityLost(state, nation, ruleset);

        Assert.False(justEliminated);
        Assert.Equal(nation, result);
    }

    [Fact]
    public void ApplyIfLastCityLost_ZeroCitiesRemaining_SetsCapitalSentinelAndResetsUnity()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var nation = CaptureTestbed.Nation("galatia", unity: 668, capitalCityId: "ancyra");
        // Every one of galatia's cities now belongs to someone else.
        var city = CaptureTestbed.City("ancyra", "Ancyra", 0, 0, "seleucid", "galatia", 40, 0, 10, 20, 5);
        var state = CaptureTestbed.StateWith(new[] { nation, CaptureTestbed.Nation("seleucid") }, new[] { city });

        var (result, justEliminated) = NationElimination.ApplyIfLastCityLost(state, nation, ruleset);

        Assert.True(justEliminated);
        Assert.True(result.Eliminated);
        Assert.Null(result.CapitalCityId);
        Assert.Equal(ruleset.Capture.EliminationUnityReset, result.Unity);
        Assert.Equal(0, result.Unity); // The confirmed Galatia figure: 668 -> 0.
    }

    /// <summary>
    /// End to end through <see cref="CityCaptureResolver.Capture"/>: a one-city nation loses its only
    /// city and is eliminated in the same call, with <see cref="NationConquered"/> published alongside
    /// <see cref="CityFallsToNation"/> -- and the two-entity probe: a third, uninvolved nation is untouched.
    /// </summary>
    [Fact]
    public void Capture_OfTheLastCity_EliminatesTheOldOwner_AndPublishesNationConquered()
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

        var conquered = Assert.Single(sink.Events.OfType<NationConquered>());
        Assert.Equal("conqueror", conquered.ConqueringNation);
        Assert.Equal("doomed", conquered.ConqueredNation);
        Assert.Single(sink.Events.OfType<CityFallsToNation>());

        // Two-entity probe: the third nation and its capital are untouched.
        Assert.Equal(thirdNation, result.NationById("third"));
        Assert.Equal(thirdCity, result.CityById("third-capital"));
    }

    /// <summary>The same elimination path, reached through <see cref="CityCaptureResolver.Defect"/> instead of <see cref="CityCaptureResolver.Capture"/>.</summary>
    [Fact]
    public void Defect_OfTheLastCity_EliminatesTheOldOwner()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City("lastcity", "Last City", 0, 0, "doomed", "doomed", 30, 0, 10, 20, 5);
        var doomed = CaptureTestbed.Nation("doomed", unity: 668, capitalCityId: "lastcity");
        var newOwner = CaptureTestbed.Nation("newowner");
        var state = CaptureTestbed.StateWith(new[] { doomed, newOwner }, new[] { city });

        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.Defect(state, "lastcity", "newowner", ruleset, sink);

        var doomedAfter = result.NationById("doomed")!;
        Assert.True(doomedAfter.Eliminated);
        Assert.Null(doomedAfter.CapitalCityId);
        Assert.Equal(0, doomedAfter.Unity);
        Assert.Single(sink.Events.OfType<NationConquered>());
    }
}
