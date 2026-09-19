using IC2.Engine.Cities.Capture;
using IC2.Engine.Model;
using IC2.Engine.Strength;
using Xunit;

namespace IC2.Engine.Tests.Cities.Capture;

/// <summary>
/// <c>docs/task-catalogue.md</c> T17, Done-when 7: the complete <c>FUN_0044A98C</c> defender strength —
/// T33's <see cref="Strength.SiegeStrength.Defender"/> (already covered by <c>SiegeStrengthTests</c>,
/// T33's Owns) plus this task's own garrison-troops addend, added last, after both scaling branches.
/// "One test for the garrison term alone, one combining it with both branches."
/// </summary>
public sealed class CompleteDefenderStrengthTests
{
    private static CityOrderRule FortifyOrder =>
        CaptureTestbed.Ruleset.CityOrders.Orders.FindById(o => o.Id, CaptureTestbed.FortifyOrderId)!;

    /// <summary>
    /// The garrison term alone: no capital bonus, owner equals allegiance, so the weighted sum is
    /// unscaled (loyalty 0, fortification 0, population 0 → 0), leaving only the garrison addend. Two
    /// recruitment slots targeting this city, 700 and 300 troops: <c>700/2 + 300/2 = 350 + 150 = 500</c>.
    /// </summary>
    [Fact]
    public void GarrisonTermAlone_SumsTroopsPerQualifyingSlot_DividedIndividually()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City("c1", "City", 0, 0, "owner", "owner", loyalty: 0, fortificationCode: 0, populationThousands: 0, maxPopulationThousands: 10, tribute: 0);
        var slotHere1 = new RecruitmentSlot("c1", "heavy_infantry", 700, 4);
        var slotHere2 = new RecruitmentSlot("c1", "heavy_infantry", 300, 4);
        var slotElsewhere = new RecruitmentSlot("elsewhere", "heavy_infantry", 9999, 4);
        var owner = CaptureTestbed.Nation("owner", recruitmentSlots: ValueList.From(new[] { slotHere1, slotHere2, slotElsewhere }));

        var strength = CompleteDefenderStrength.Compute(
            city, FortifyOrder, isControllerCapital: false, ownerDiffersFromAllegiance: false, owner, ruleset);

        Assert.Equal(500, strength);
        Assert.Equal(500, CompleteDefenderStrength.GarrisonTerm("c1", owner, ruleset));
    }

    /// <summary>
    /// Per-slot division is not the same as dividing the total once, under truncation: two slots of 3
    /// troops each give <c>3/2 + 3/2 = 1 + 1 = 2</c>, not <c>(3+3)/2 = 3</c>. Mutation proof: summing
    /// first and dividing once would pass every other test in this file (their slot troop counts are all
    /// even) but fails this one specifically.
    /// </summary>
    [Fact]
    public void GarrisonTerm_DividesEachSlotBeforeSumming_NotTheTotal()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var slotA = new RecruitmentSlot("c1", "heavy_infantry", 3, 4);
        var slotB = new RecruitmentSlot("c1", "heavy_infantry", 3, 4);
        var owner = CaptureTestbed.Nation("owner", recruitmentSlots: ValueList.From(new[] { slotA, slotB }));

        var garrisonTerm = CompleteDefenderStrength.GarrisonTerm("c1", owner, ruleset);

        Assert.Equal(2, garrisonTerm);
        Assert.NotEqual(3, garrisonTerm); // What summing 3+3=6 then dividing by 2 once would give.
    }

    /// <summary>
    /// The garrison term combined with both <see cref="Strength.SiegeStrength.Defender"/> scaling
    /// branches, added last, after both — folding it into the weighted sum instead would run it through
    /// the ×5/3 and ×4/5 branches too, which the decompiled function never does. Weighted sum:
    /// loyalty 100 × 150 = 15,000 → ×5/3 (capital, loyalty &gt; 59) = 25,000 → ×4/5 (owner ≠ allegiance)
    /// = 20,000 → + garrison (400/2 = 200) = 20,200.
    /// </summary>
    [Fact]
    public void GarrisonTerm_CombinedWithBothScalingBranches_IsAddedLastAfterBothBranches()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City(
            "capital", "Capital", 0, 0, "owner", "allegiant-nation", loyalty: 100, fortificationCode: 0,
            populationThousands: 0, maxPopulationThousands: 10, tribute: 0);
        var slot = new RecruitmentSlot("capital", "heavy_infantry", 400, 4);
        var owner = CaptureTestbed.Nation("owner", recruitmentSlots: ValueList.From(new[] { slot }));

        var strength = CompleteDefenderStrength.Compute(
            city, FortifyOrder, isControllerCapital: true, ownerDiffersFromAllegiance: true, owner, ruleset);

        Assert.Equal(20_200, strength);

        // Proof that the garrison term is added AFTER scaling, not before: folding 200 into the weighted
        // sum before the branches would give (15,000 + 200) * 5/3 * 4/5 = 20,266, not 20,200.
        var ifFoldedBeforeBranches = ((15_000 + 200) * ruleset.Siege.HighLoyaltyBonusNumerator / ruleset.Siege.HighLoyaltyBonusDenominator)
                                      * ruleset.Siege.DefenderNonAllegiantNumerator / ruleset.Siege.DefenderNonAllegiantDenominator;
        Assert.NotEqual(ifFoldedBeforeBranches, strength);
    }

    /// <summary>A city with no recruitment slots at all contributes a zero garrison term.</summary>
    [Fact]
    public void NoRecruitmentSlots_GarrisonTermIsZero()
    {
        var ruleset = CaptureTestbed.Ruleset;
        var city = CaptureTestbed.City("c1", "City", 0, 0, "owner", "owner", loyalty: 50, fortificationCode: 20, populationThousands: 5, maxPopulationThousands: 10, tribute: 0);
        var owner = CaptureTestbed.Nation("owner");

        var strength = CompleteDefenderStrength.Compute(
            city, FortifyOrder, isControllerCapital: false, ownerDiffersFromAllegiance: false, owner, ruleset);

        var baseOnly = SiegeStrength.Defender(
            city.FortificationCode, FortifyOrder, city.Loyalty, city.PopulationThousands, false, false, ruleset);
        Assert.Equal(baseOnly, strength);
    }
}
