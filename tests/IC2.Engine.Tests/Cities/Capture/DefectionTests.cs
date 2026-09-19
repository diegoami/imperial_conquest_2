using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// <see cref="CityCaptureResolver.Defect"/> (<c>FUN_0044bed8</c>) — <c>docs/task-catalogue.md</c> T17's
/// Known-open item, used with <c>[derived]</c> provenance naming the inference (see
/// <see cref="CaptureRules"/>'s own field remarks and <see cref="CityCaptureResolver"/>'s class remarks).
/// </summary>
public sealed class DefectionTests
{
    /// <summary>
    /// Unity +3/−20, floored at 250 for the losing side — unlike a forced capture's −15, which the
    /// report gives no floor for. Contribution = 10 × 20 / 40 = 5, so the treasury credit is
    /// <c>5 × 6 = 30</c>, not <c>5 × 4 = 20</c> (the forced-capture multiplier).
    /// </summary>
    [Fact]
    public void MovesUnityByThreeAndTwenty_AndCreditsTreasuryAtItsOwnMultiplier()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "old", "old", loyalty: 30, fortificationCode: 0,
            populationThousands: 20, maxPopulationThousands: 40, tribute: 10);
        // A second city keeps "old" from being eliminated by this defection, isolating the unity term
        // under test from NationElimination's own unity reset.
        var oldOwnerOtherCity = CaptureTestbed.City("c2", "Other", 9, 9, "old", "old", 50, 0, 5, 10, 3);
        var oldOwner = CaptureTestbed.Nation("old", treasury: 0, unity: 600);
        var newOwner = CaptureTestbed.Nation("new", treasury: 1000, unity: 600);
        var state = CaptureTestbed.StateWith(new[] { oldOwner, newOwner }, new[] { city, oldOwnerOtherCity });

        var sink = new RecordingEventSink();
        var result = CityCaptureResolver.Defect(state, "c1", "new", ruleset, sink);

        Assert.Equal(603, result.NationById("new")!.Unity); // 600 + 3.
        Assert.Equal(580, result.NationById("old")!.Unity);  // 600 - 20.
        Assert.Equal(1030, result.NationById("new")!.Treasury); // 1000 + 5*6.
        Assert.Equal(0, result.NationById("old")!.Treasury); // Old owner's treasury is never debited.

        var defected = Assert.Single(sink.Events.OfType<CityDefectsToNation>());
        Assert.Equal("City", defected.CityName);
        Assert.Equal("old", defected.OldOwner);
        Assert.Equal("new", defected.NewOwner);
    }

    /// <summary>
    /// The floor: an old owner already near 250 does not drop below it. Mutation proof — replacing
    /// <c>Math.Max(floor, unity - loss)</c> with a plain subtraction makes this fail (255 - 20 = 235,
    /// under the floor), while <see cref="MovesUnityByThreeAndTwenty_AndCreditsTreasuryAtItsOwnMultiplier"/>
    /// stays green either way (600 - 20 = 580 is already above the floor) -- exactly the case a
    /// same-subtraction-everywhere mutant would slip past.
    /// </summary>
    [Fact]
    public void OldOwnersUnityLoss_IsFlooredAtTwoHundredFifty()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "old", "old", loyalty: 30, fortificationCode: 0,
            populationThousands: 10, maxPopulationThousands: 20, tribute: 5);
        var oldOwnerOtherCity = CaptureTestbed.City("c2", "Other", 9, 9, "old", "old", 50, 0, 5, 10, 3);
        var oldOwner = CaptureTestbed.Nation("old", unity: 255);
        var newOwner = CaptureTestbed.Nation("new", unity: 600);
        var state = CaptureTestbed.StateWith(new[] { oldOwner, newOwner }, new[] { city, oldOwnerOtherCity });

        var result = CityCaptureResolver.Defect(state, "c1", "new", ruleset, NullEventSink.Instance);

        Assert.Equal(ruleset.Capture.DefectionUnityLossFloor, result.NationById("old")!.Unity);
        Assert.Equal(250, result.NationById("old")!.Unity);
        Assert.NotEqual(235, result.NationById("old")!.Unity); // What an unfloored subtraction would give.
    }

    /// <summary>
    /// <c>defection.neverChangesPopOrFort</c>: population and fortification are byte-for-byte identical
    /// before and after, unlike a forced capture (whose city arrives already at its post-siege-attrition
    /// figures, but is never further modified by the transfer itself either -- see
    /// <see cref="CaptureTests"/>). Mutation proof: adding a population or fortification write to
    /// <see cref="CityCaptureResolver.Defect"/>'s city <c>with</c> expression would fail this test, and
    /// only this test, in the whole suite.
    /// </summary>
    [Fact]
    public void NeverChangesPopulationOrFortification()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "c1", "City", 0, 0, "old", "old", loyalty: 30, fortificationCode: 77,
            populationThousands: 42, maxPopulationThousands: 60, tribute: 5);
        var state = CaptureTestbed.StateWith(
            new[] { CaptureTestbed.Nation("old"), CaptureTestbed.Nation("new") }, new[] { city });

        var result = CityCaptureResolver.Defect(state, "c1", "new", ruleset, NullEventSink.Instance);

        var after = result.CityById("c1")!;
        Assert.Equal(77, after.FortificationCode);
        Assert.Equal(42, after.PopulationThousands);
        Assert.Equal("new", after.Owner);
    }

    /// <summary>The old owner's recruitment slots at the defecting city are cleared, same as a forced capture.</summary>
    [Fact]
    public void ClearsTheOldOwnersRecruitmentSlotsAtTheDefectingCity()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City("c1", "City", 0, 0, "old", "old", 30, 0, 10, 20, 5);
        var slot = new IC2.Engine.Model.RecruitmentSlot("c1", "heavy_infantry", 400, 4);
        var oldOwner = CaptureTestbed.Nation("old", recruitmentSlots: IC2.Engine.Model.ValueList.From(new[] { slot }));
        var state = CaptureTestbed.StateWith(new[] { oldOwner, CaptureTestbed.Nation("new") }, new[] { city });

        var result = CityCaptureResolver.Defect(state, "c1", "new", ruleset, NullEventSink.Instance);

        Assert.Empty(result.NationById("old")!.RecruitmentSlots);
    }
}
