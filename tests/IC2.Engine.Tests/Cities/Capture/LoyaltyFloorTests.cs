using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// <c>docs/task-catalogue.md</c> T17, Done-when 2: "Loyalty floors hold: 40 after a forced capture, 65
/// after a defection, toward 90 when the allegiant nation recaptures."
/// </summary>
public sealed class LoyaltyFloorTests
{
    /// <summary>
    /// <c>[confirmed]</c>: the Sidon example (<c>loyalty.sidonExample.before/after</c>) is an exact
    /// 90 → 40 assignment, not a partial move toward the floor.
    /// </summary>
    [Fact]
    public void ForcedCapture_ByANonAllegiantOwner_SetsLoyaltyToTheForty_Floor()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "sidon", "Sidon", 0, 0, owner: "ptolemaic", allegiance: "ptolemaic",
            loyalty: 90, fortificationCode: 0, populationThousands: 20, maxPopulationThousands: 40, tribute: 5);
        var oldOwner = CaptureTestbed.Nation("ptolemaic");
        var newOwner = CaptureTestbed.Nation("rome");
        var attacker = CaptureTestbed.Army("army", "rome", 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1000));

        var state = CaptureTestbed.StateWith(new[] { oldOwner, newOwner }, new[] { city }, new[] { attacker });

        var result = CityCaptureResolver.Capture(
            state, "army", "sidon", ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        Assert.Equal(ruleset.Loyalty.ForcedCaptureFloor, result.CityById("sidon")!.Loyalty);
        Assert.Equal(40, result.CityById("sidon")!.Loyalty);
    }

    /// <summary>The recapture branch: the city's own allegiance already matches the new owner.</summary>
    [Fact]
    public void ForcedCapture_ByTheAllegiantNation_SetsLoyaltyTowardNinety()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "taurasia", "Taurasia", 0, 0, owner: "gaul-rebel", allegiance: "gaul",
            loyalty: 20, fortificationCode: 0, populationThousands: 20, maxPopulationThousands: 40, tribute: 5);
        var oldOwner = CaptureTestbed.Nation("gaul-rebel");
        var newOwner = CaptureTestbed.Nation("gaul");
        var attacker = CaptureTestbed.Army("army", "gaul", 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1000));

        var state = CaptureTestbed.StateWith(new[] { oldOwner, newOwner }, new[] { city }, new[] { attacker });

        var result = CityCaptureResolver.Capture(
            state, "army", "taurasia", ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        Assert.Equal(ruleset.Loyalty.AllegiantRecaptureTarget, result.CityById("taurasia")!.Loyalty);
        Assert.Equal(90, result.CityById("taurasia")!.Loyalty);
    }

    /// <summary>
    /// A defection (no siege) pulls loyalty to 65, not 40 — gentler than a forced capture, per
    /// <c>decompiled-defection-and-siege-attrition.md</c>.
    /// </summary>
    [Fact]
    public void Defection_ByANonAllegiantOwner_SetsLoyaltyToTheSixtyFive_Floor()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "synnada", "Synnada", 0, 0, owner: "galatia", allegiance: "galatia",
            loyalty: 30, fortificationCode: 41, populationThousands: 20, maxPopulationThousands: 40, tribute: 5);
        var oldOwner = CaptureTestbed.Nation("galatia");
        var newOwner = CaptureTestbed.Nation("seleucid");

        var state = CaptureTestbed.StateWith(new[] { oldOwner, newOwner }, new[] { city });

        var result = CityCaptureResolver.Defect(state, "synnada", "seleucid", ruleset, NullEventSink.Instance);

        Assert.Equal(ruleset.Loyalty.DefectionFloor, result.CityById("synnada")!.Loyalty);
        Assert.Equal(65, result.CityById("synnada")!.Loyalty);

        // defection.neverChangesPopOrFort: fortification and population are untouched by a defection.
        Assert.Equal(41, result.CityById("synnada")!.FortificationCode);
        Assert.Equal(20, result.CityById("synnada")!.PopulationThousands);
    }
}
