using System.Linq;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// T86 Done-when 5: <c>decompiled-quarterly-rebellion.md</c> §3 (research <c>235af11</c>) corrects
/// <see cref="LoyaltyFloorTests"/>'s own flat targets into clamp-bound formulas. Every one of the
/// report's 13 save cases (8 cascade defections, 5 forced captures — Laranda and Caere, both allegiant,
/// are the two <see cref="LoyaltyFloorTests"/> already covers exactly) is reproduced here from its own
/// pre-transfer loyalty, plus the boundary either side of each clamp.
/// </summary>
public sealed class LoyaltyFormulaTests
{
    private static Ruleset Ruleset => CaptureTestbed.Ruleset;

    private static int LoyaltyAfterDefection(int preTransferLoyalty, bool allegiant)
    {
        var newOwnerId = "receiver";
        var oldOwnerId = "old";
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, oldOwnerId, allegiant ? newOwnerId : oldOwnerId,
            preTransferLoyalty, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 0);
        var oldOwner = CaptureTestbed.Nation(oldOwnerId);
        var newOwner = CaptureTestbed.Nation(newOwnerId);
        var state = CaptureTestbed.StateWith(new[] { oldOwner, newOwner }, new[] { city });

        var result = CityCaptureResolver.Defect(state, "c1", newOwnerId, Ruleset, NullEventSink.Instance);
        return result.CityById("c1")!.Loyalty;
    }

    private static int LoyaltyAfterCapture(int postErosionLoyalty, bool allegiant)
    {
        var newOwnerId = "attacker";
        var oldOwnerId = "defender";
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, oldOwnerId, allegiant ? newOwnerId : oldOwnerId,
            postErosionLoyalty, fortificationCode: 0, populationThousands: 10, maxPopulationThousands: 20, tribute: 0);
        var oldOwner = CaptureTestbed.Nation(oldOwnerId);
        var newOwner = CaptureTestbed.Nation(newOwnerId);
        var attacker = CaptureTestbed.Army("army", newOwnerId, 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1000));
        var state = CaptureTestbed.StateWith(new[] { oldOwner, newOwner }, new[] { city }, new[] { attacker });

        var result = CityCaptureResolver.Capture(
            state, "army", "c1", Ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);
        return result.CityById("c1")!.Loyalty;
    }

    // ---- The report's 8 cascade defections (non-allegiant unless noted), each from its own pre-transfer
    // loyalty: min(DefectionFloor, max(DefectionFormulaFloor, NonAllegiantTransferBase - L)). ----

    [Theory]
    [InlineData(56, 50)] // Aradus: 100-56=44 -> max(50,44)=50.
    [InlineData(62, 50)] // Hemesa: 100-62=38 -> max(50,38)=50.
    [InlineData(57, 50)] // Palmyra: 100-57=43 -> max(50,43)=50.
    [InlineData(63, 50)] // Modena: 100-63=37 -> max(50,37)=50.
    [InlineData(59, 50)] // Acroinon: 100-59=41 -> max(50,41)=50.
    public void Defection_NonAllegiant_ReproducesTheReportsCascadeDefections(int preTransferLoyalty, int expected)
    {
        Assert.Equal(expected, LoyaltyAfterDefection(preTransferLoyalty, allegiant: false));
    }

    [Theory]
    [InlineData(62, 78)] // Synnada: min(90,140-62)=min(90,78)=78.
    [InlineData(42, 90)] // Tarquinii: min(90,140-42)=min(90,98)=90.
    [InlineData(43, 90)] // Ariminum: min(90,140-43)=min(90,97)=90.
    public void Defection_Allegiant_ReproducesTheReportsCascadeDefections(int preTransferLoyalty, int expected)
    {
        Assert.Equal(expected, LoyaltyAfterDefection(preTransferLoyalty, allegiant: true));
    }

    // ---- The report's 5 non-allegiant forced captures, each from its own post-erosion loyalty L':
    // max(ForcedCaptureFloor, min(ForcedCaptureCap, NonAllegiantTransferBase - L')). ----

    [Theory]
    [InlineData(57, 43)] // Mediolanum: 100-57=43 -> max(40,min(60,43))=43.
    [InlineData(59, 41)] // Felsina: 100-59=41 -> 41.
    [InlineData(56, 44)] // Brixia: 100-56=44 -> 44.
    [InlineData(45, 55)] // Byblos: 100-45=55 -> 55.
    [InlineData(51, 49)] // Gordium: 100-51=49 -> 49.
    public void Capture_NonAllegiant_ReproducesTheReportsSaveCaptures(int postErosionLoyalty, int expected)
    {
        Assert.Equal(expected, LoyaltyAfterCapture(postErosionLoyalty, allegiant: false));
    }

    /// <summary>Damascus: the report gives only the before/after (85 -&gt; 40), consistent with the erosion
    /// floor L' = floor(85*3/4) = 63: 100-63=37, floored at 40.</summary>
    [Fact]
    public void Capture_NonAllegiant_Damascus_LandsOnTheFloor()
    {
        Assert.Equal(40, LoyaltyAfterCapture(postErosionLoyalty: 63, allegiant: false));
    }

    // ---- Boundaries either side of each clamp, independent of any specific save. ----

    [Fact]
    public void Capture_NonAllegiant_AtTheFloorBoundary_StaysAtTheFloor()
    {
        // target = 100 - 60 = 40 -- exactly the floor, not yet below it.
        Assert.Equal(40, LoyaltyAfterCapture(postErosionLoyalty: 60, allegiant: false));
    }

    [Fact]
    public void Capture_NonAllegiant_OneBelowTheFloorBoundary_IsFlooredAnyway()
    {
        // target = 100 - 61 = 39 -- one below the floor; the floor still applies.
        Assert.Equal(40, LoyaltyAfterCapture(postErosionLoyalty: 61, allegiant: false));
    }

    [Fact]
    public void Capture_NonAllegiant_AtTheCapBoundary_StaysAtTheCap()
    {
        // target = 100 - 40 = 60 -- exactly the cap, not yet above it.
        Assert.Equal(60, LoyaltyAfterCapture(postErosionLoyalty: 40, allegiant: false));
    }

    [Fact]
    public void Capture_NonAllegiant_OneAboveTheCapBoundary_IsCappedAnyway()
    {
        // target = 100 - 39 = 61 -- one above the cap; the cap still applies.
        Assert.Equal(60, LoyaltyAfterCapture(postErosionLoyalty: 39, allegiant: false));
    }

    [Fact]
    public void Defection_NonAllegiant_AtTheFloorBoundary_StaysAtTheFloor()
    {
        // target = 100 - 50 = 50 -- exactly the floor.
        Assert.Equal(50, LoyaltyAfterDefection(preTransferLoyalty: 50, allegiant: false));
    }

    [Fact]
    public void Defection_NonAllegiant_AtTheCapBoundary_StaysAtTheCap()
    {
        // target = 100 - 35 = 65 -- exactly the cap.
        Assert.Equal(65, LoyaltyAfterDefection(preTransferLoyalty: 35, allegiant: false));
    }

    [Fact]
    public void Defection_NonAllegiant_OneAboveTheCapBoundary_IsCappedAnyway()
    {
        // target = 100 - 34 = 66 -- one above the cap; the cap still applies.
        Assert.Equal(65, LoyaltyAfterDefection(preTransferLoyalty: 34, allegiant: false));
    }

    [Fact]
    public void Allegiant_AtTheCapBoundary_StaysAtTheCap_ForBothCaptureAndDefection()
    {
        // target = 140 - 50 = 90 -- exactly the cap, shared by both mechanisms.
        Assert.Equal(90, LoyaltyAfterCapture(postErosionLoyalty: 50, allegiant: true));
        Assert.Equal(90, LoyaltyAfterDefection(preTransferLoyalty: 50, allegiant: true));
    }

    [Fact]
    public void Allegiant_OneBelowTheCapBoundary_IsNotCapped()
    {
        // target = 140 - 51 = 89 -- one below the cap.
        Assert.Equal(89, LoyaltyAfterCapture(postErosionLoyalty: 51, allegiant: true));
        Assert.Equal(89, LoyaltyAfterDefection(preTransferLoyalty: 51, allegiant: true));
    }

    // ---- The slot rule (decompiled-quarterly-rebellion.md §"Nothing is written before the transfer"):
    // a defection removes only the old owner's recruitment slots at the city that hold troops; a 0-troop
    // slot stays. ----

    [Fact]
    public void Defection_RemovesOnlyTroopHoldingSlotsAtTheCity_AndKeepsAZeroTroopSlot()
    {
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "old", "old", 30, 0, 10, 20, 0);
        var slotWithTroops = new RecruitmentSlot("c1", "heavy_infantry", 400, StateCode: 4);
        var slotWithZeroTroops = new RecruitmentSlot("c1", "light_infantry", 0, StateCode: 4);
        var slotAtAnotherCity = new RecruitmentSlot("elsewhere", "heavy_infantry", 200, StateCode: 4);

        var oldOwner = CaptureTestbed.Nation(
            "old", recruitmentSlots: ValueList.From(new[] { slotWithTroops, slotWithZeroTroops, slotAtAnotherCity }));
        var newOwner = CaptureTestbed.Nation("new");
        var state = CaptureTestbed.StateWith(new[] { oldOwner, newOwner }, new[] { city });

        var result = CityCaptureResolver.Defect(state, "c1", "new", Ruleset, NullEventSink.Instance);

        var remaining = result.NationById("old")!.RecruitmentSlots;
        Assert.Equal(2, remaining.Count);
        Assert.Contains(remaining, s => s.TargetCityId == "c1" && s.Troops == 0);
        Assert.Contains(remaining, s => s.TargetCityId == "elsewhere");
        Assert.DoesNotContain(remaining, s => s.TargetCityId == "c1" && s.Troops == 400);
    }

    /// <summary>
    /// Contrast: a forced capture's own rule is unchanged -- it removes every slot at the city, troops or
    /// not. Six filler cities keep "old" at CaptureRules.ConquestCityCountThreshold after losing "c1", so
    /// conquest's own separate, total slot wipe never fires here -- this isolates Capture's own
    /// WithoutSlotsTargeting rule from that unrelated effect, which would otherwise produce the same
    /// empty-slots result for a different reason.
    /// </summary>
    [Fact]
    public void Capture_StillRemovesEveryTargetingSlotRegardlessOfTroops()
    {
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "old", "old", 30, 0, 10, 20, 0);
        var slotWithZeroTroops = new RecruitmentSlot("c1", "light_infantry", 0, StateCode: 4);
        var fillerCities = CaptureTestbed.FillerCities("old", 6, startX: 1000, y: 1000);

        var oldOwner = CaptureTestbed.Nation("old", recruitmentSlots: ValueList.From(new[] { slotWithZeroTroops }));
        var newOwner = CaptureTestbed.Nation("new");
        var attacker = CaptureTestbed.Army("army", "new", 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1000));
        var state = CaptureTestbed.StateWith(
            new[] { oldOwner, newOwner }, new[] { city }.Concat(fillerCities), new[] { attacker });

        var result = CityCaptureResolver.Capture(
            state, "army", "c1", Ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        Assert.False(result.NationById("old")!.Eliminated); // conquest did not fire.
        Assert.Empty(result.NationById("old")!.RecruitmentSlots);
    }
}
