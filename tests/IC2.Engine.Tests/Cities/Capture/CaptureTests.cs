using System.Linq;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// <c>docs/task-catalogue.md</c> T17, Done-when 3 and 9: <see cref="CityCaptureResolver.Capture"/>
/// (<c>FUN_0044bb18</c>) reproduces the Naupactus capture exactly, including the DoD 9 treasury credit
/// this task adds, and emits DoD 6's <c>"falls to"</c> message.
/// </summary>
public sealed class CaptureTests
{
    /// <summary>
    /// The report's own Naupactus capture (Illyria ← Greece), extended with the terms
    /// <c>CityOwnershipTaxTransferTests</c> does not cover: the DoD 9 treasury credit (Illyria's
    /// treasury −2,021 → −1,973), unity +9/−15, and city count 11 → 12 / 19 → 18 — all from
    /// <c>docs/task-catalogue.md</c> T17 DoD 3's own reproduction figures.
    /// </summary>
    [Fact]
    public void ReproducesTheNaupactusCaptureExactly()
    {
        var ruleset = CaptureTestbed.Ruleset;

        var naupactus = CaptureTestbed.City(
            "naupactus", "Naupactus", 5, 5, "greece", "greece",
            loyalty: 40, fortificationCode: 0, populationThousands: 25, maxPopulationThousands: 30, tribute: 15);

        var illyriaFillers = CaptureTestbed.FillerCities("illyria", 11, startX: 20, y: 0).ToArray();
        var greeceFillers = CaptureTestbed.FillerCities("greece", 18, startX: 40, y: 0).ToArray();

        var illyria = CaptureTestbed.Nation("illyria", treasury: -2021, unity: 600, wealth: 768_000, taxBase: 396);
        var greece = CaptureTestbed.Nation("greece", treasury: 0, unity: 600, wealth: 2_490_000, taxBase: 2296);
        var untouchedThirdNation = CaptureTestbed.Nation("third", treasury: 12345, unity: 500, wealth: 999_000, taxBase: 777);

        var attacker = CaptureTestbed.Army(
            "illyria-army", "illyria", 5, 5, morale: 60,
            CaptureTestbed.Unit("heavy_infantry", 5000));

        var untouchedThirdCity = CaptureTestbed.City(
            "untouched", "Untouched", 99, 99, "third", "third",
            loyalty: 70, fortificationCode: 0, populationThousands: 5, maxPopulationThousands: 10, tribute: 3);

        var state = CaptureTestbed.StateWith(
            nations: new[] { illyria, greece, untouchedThirdNation },
            cities: illyriaFillers.Concat(greeceFillers).Append(naupactus).Append(untouchedThirdCity),
            armies: new[] { attacker });

        Assert.Equal(11, state.CountCitiesOwnedBy("illyria"));
        Assert.Equal(19, state.CountCitiesOwnedBy("greece"));

        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.Capture(
            state, "illyria-army", "naupactus", ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, sink);

        var newIllyria = result.NationById("illyria")!;
        var newGreece = result.NationById("greece")!;

        // DoD 9: the treasury credit this task adds. Contribution = 15*25/30 = 12; 12*4 = 48.
        Assert.Equal(-1973, newIllyria.Treasury);
        Assert.Equal(0, newGreece.Treasury); // Old owner's treasury is never debited (report gives no such term).

        // T35's tax-base/wealth transfer, reproduced end to end through this task's own call.
        Assert.Equal(444, newIllyria.TaxBase);
        Assert.Equal(2248, newGreece.TaxBase);
        Assert.Equal(843_000, newIllyria.Wealth);
        Assert.Equal(2_415_000, newGreece.Wealth);

        // Unity +9/-15.
        Assert.Equal(609, newIllyria.Unity);
        Assert.Equal(585, newGreece.Unity);

        // City count +-1, via ownership rather than an explicit counter field.
        Assert.Equal(12, result.CountCitiesOwnedBy("illyria"));
        Assert.Equal(18, result.CountCitiesOwnedBy("greece"));

        // DoD 6: the confirmed "falls to" message.
        var fallsTo = Assert.Single(sink.Events.OfType<CityFallsToNation>());
        Assert.Equal("Naupactus", fallsTo.CityName);
        Assert.Equal("greece", fallsTo.OldOwner);
        Assert.Equal("illyria", fallsTo.NewOwner);

        // The two-entity instinct applied to events, not just state: Naupactus is the only city that
        // changes hands here -- none of Greece's filler cities (far from the besieging army) cascade into
        // a defection.
        Assert.DoesNotContain(sink.Events, e => e is CityDefectsToNation);

        // Neither nation was eliminated (Greece still owns 18 cities).
        Assert.False(newGreece.Eliminated);
        Assert.DoesNotContain(sink.Events, e => e is NationConquered);

        // The two-entity probe: a third nation and a third city, untouched by this capture.
        var thirdAfter = result.NationById("third")!;
        Assert.Equal(untouchedThirdNation, thirdAfter);
        var thirdCityAfter = result.CityById("untouched")!;
        Assert.Equal(untouchedThirdCity, thirdCityAfter);
    }

    /// <summary>
    /// DoD 9's own treasury credit, isolated: mutation proof that the new owner's treasury actually moves
    /// by <c>contribution × capture.captureTreasuryCreditMultiplier</c>, not by
    /// <c>capture.taxBaseMultiplier</c> (T35's separate, pre-existing tax-base term) mistaken for it —
    /// exactly the confusion T37's review found. Deleting the treasury-credit line in
    /// <see cref="CityCaptureResolver.Capture"/> leaves <c>ReproducesTheNaupactusCaptureExactly</c>'s
    /// tax-base and wealth assertions green and only this one (and that test's own treasury assertion)
    /// red -- verified by temporarily commenting out the <c>Treasury = ...</c> line and re-running.
    /// </summary>
    [Fact]
    public void TreasuryCreditIsContributionTimesCaptureMultiplier_NotTheTaxBaseMultiplier()
    {
        var ruleset = CaptureTestbed.Ruleset;

        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "old", "old", loyalty: 40, fortificationCode: 0,
            populationThousands: 20, maxPopulationThousands: 40, tribute: 10); // contribution = 10*20/40 = 5

        var oldOwner = CaptureTestbed.Nation("old", treasury: 0, unity: 600);
        var newOwner = CaptureTestbed.Nation("new", treasury: 1000, unity: 600);
        var attacker = CaptureTestbed.Army("army", "new", 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1000));

        var state = CaptureTestbed.StateWith(
            nations: new[] { oldOwner, newOwner },
            cities: new[] { city },
            armies: new[] { attacker });

        var result = CityCaptureResolver.Capture(
            state, "army", "c1", ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        var contribution = 5;
        var expectedTreasury = 1000 + (contribution * ruleset.Capture.CaptureTreasuryCreditMultiplier);
        Assert.Equal(1020, expectedTreasury); // 1000 + 5*4.
        Assert.Equal(expectedTreasury, result.NationById("new")!.Treasury);

        // The tax-base multiplier is a DIFFERENT field, wired through T35's helper, not this one.
        var expectedTaxBaseDelta = contribution * ruleset.Economy.TaxBaseContributionMultiplier;
        Assert.Equal(expectedTaxBaseDelta, result.NationById("new")!.TaxBase);
    }

    /// <summary>
    /// The two multipliers are both 4 in the shipped toy ruleset, so a numeric-only assertion cannot by
    /// itself distinguish "read <c>capture.captureTreasuryCreditMultiplier</c>" from "read
    /// <c>economy.taxBaseContributionMultiplier</c> instead" -- exactly the confusion DoD 9 exists to
    /// close. This test forces the two ruleset fields apart so the treasury credit can only match one of
    /// them. Mutation proof: swapping <see cref="CityCaptureResolver.Capture"/>'s treasury-credit line to
    /// read <c>ruleset.Economy.TaxBaseContributionMultiplier</c> instead of
    /// <c>ruleset.Capture.CaptureTreasuryCreditMultiplier</c> leaves every other test in this file green
    /// (the shipped ruleset's two fields coincide at 4) and fails only this one -- verified locally by
    /// making that swap and re-running.
    /// </summary>
    [Fact]
    public void TreasuryCreditReadsTheCaptureField_EvenWhenItDivergesFromTheTaxBaseField()
    {
        var baseRuleset = CaptureTestbed.Ruleset;
        var ruleset = baseRuleset with
        {
            Capture = baseRuleset.Capture with { CaptureTreasuryCreditMultiplier = 9 },
            Economy = baseRuleset.Economy with { TaxBaseContributionMultiplier = 2 },
        };

        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "old", "old", loyalty: 40, fortificationCode: 0,
            populationThousands: 20, maxPopulationThousands: 40, tribute: 10); // contribution = 5
        var oldOwner = CaptureTestbed.Nation("old");
        var newOwner = CaptureTestbed.Nation("new", treasury: 1000);
        var attacker = CaptureTestbed.Army("army", "new", 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1000));
        var state = CaptureTestbed.StateWith(new[] { oldOwner, newOwner }, new[] { city }, new[] { attacker });

        var result = CityCaptureResolver.Capture(
            state, "army", "c1", ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        Assert.Equal(1045, result.NationById("new")!.Treasury); // 1000 + 5*9 -- the Capture field, not 1000 + 5*2.
        Assert.Equal(10, result.NationById("new")!.TaxBase);    // 5*2 -- the Economy field, via T35's helper.
    }

    /// <summary>
    /// DoD 3 / <c>capture.garrisonClearedOnTransfer</c>: the old owner's recruitment slots targeting the
    /// captured city are cleared; a slot targeting a <em>different</em> city, and the new owner's own
    /// slots, are untouched.
    /// </summary>
    [Fact]
    public void ClearsOnlyTheOldOwnersRecruitmentSlotsTargetingTheCapturedCity()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "old", "old", loyalty: 40, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var otherCity = CaptureTestbed.City(
            "c2", "Other", 5, 5, "old", "old", loyalty: 40, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);

        var slotAtCapturedCity = new RecruitmentSlot("c1", "heavy_infantry", 500, StateCode: 4);
        var slotAtOtherCity = new RecruitmentSlot("c2", "heavy_infantry", 300, StateCode: 4);
        var newOwnerSlot = new RecruitmentSlot("elsewhere", "heavy_infantry", 200, StateCode: 4);

        var oldOwner = CaptureTestbed.Nation(
            "old", recruitmentSlots: ValueList.From(new[] { slotAtCapturedCity, slotAtOtherCity }));
        var newOwner = CaptureTestbed.Nation("new", recruitmentSlots: ValueList.From(new[] { newOwnerSlot }));
        var attacker = CaptureTestbed.Army("army", "new", 0, 0, morale: 50, CaptureTestbed.Unit("heavy_infantry", 1000));

        var state = CaptureTestbed.StateWith(
            nations: new[] { oldOwner, newOwner },
            cities: new[] { city, otherCity },
            armies: new[] { attacker });

        var result = CityCaptureResolver.Capture(
            state, "army", "c1", ruleset, CaptureTestbed.ArcherUnitTypeId, CaptureTestbed.FortifyOrderId, NullEventSink.Instance);

        var oldOwnerAfter = result.NationById("old")!;
        Assert.Single(oldOwnerAfter.RecruitmentSlots);
        Assert.Equal("c2", oldOwnerAfter.RecruitmentSlots[0].TargetCityId);

        var newOwnerAfter = result.NationById("new")!;
        Assert.Single(newOwnerAfter.RecruitmentSlots);
        Assert.Equal("elsewhere", newOwnerAfter.RecruitmentSlots[0].TargetCityId);
    }
}
