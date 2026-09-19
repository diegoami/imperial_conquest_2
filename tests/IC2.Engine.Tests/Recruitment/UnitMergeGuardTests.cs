using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Recruitment;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T13 Recruitment and mercenaries", Done-when 5: a mercenary unit's slot
/// <c>+0</c> is non-zero and a regular's is zero; merging the two is rejected.
/// </summary>
public sealed class UnitMergeGuardTests
{
    private static UnitSlot Regular(int troops = 1000) =>
        new(MercenaryLabel: 0, UnitTypeId: "light_infantry", Troops: troops, Quality: 6, Name: "1st Foot Battalion");

    private static UnitSlot Mercenary(int troops = 1000) =>
        new(MercenaryLabel: 11, UnitTypeId: "light_infantry", Troops: troops, Quality: 7, Name: "Gallic");

    [Fact]
    public void A_mercenarys_slot_plus0_is_non_zero_and_a_regulars_is_zero()
    {
        var mercenary = Mercenary();
        var regular = Regular();

        Assert.NotEqual(0, mercenary.MercenaryLabel);
        Assert.True(mercenary.IsMercenary);
        Assert.False(mercenary.IsRegular);

        Assert.Equal(0, regular.MercenaryLabel);
        Assert.True(regular.IsRegular);
        Assert.False(regular.IsMercenary);
    }

    [Fact]
    public void Merging_a_mercenary_with_a_regular_is_rejected_either_order()
    {
        Assert.False(UnitMergeGuard.IsMergeAllowedByMarker(Mercenary(), Regular()));
        Assert.False(UnitMergeGuard.IsMergeAllowedByMarker(Regular(), Mercenary()));
    }

    [Fact]
    public void Merging_two_mercenaries_is_rejected()
    {
        Assert.False(UnitMergeGuard.IsMergeAllowedByMarker(Mercenary(), Mercenary()));
    }

    [Fact]
    public void Merging_two_regulars_is_allowed_by_the_marker_guard()
    {
        Assert.True(UnitMergeGuard.IsMergeAllowedByMarker(Regular(), Regular()));
    }

    /// <summary>
    /// The real, already-in-the-toy-world pair: north-army-1's regular "2nd Foot Battalion" and its
    /// mercenary "Gallic Bowmen" -- confirms the guard against actual scenario data, not only
    /// hand-built fixtures.
    /// </summary>
    [Fact]
    public void The_toy_worlds_own_regular_and_mercenary_units_cannot_merge()
    {
        var army = CoreTestbed.InitialState().ArmyById("north-army-1")!;
        var regular = Assert.Single(army.Units, u => u.IsRegular);
        var mercenary = Assert.Single(army.Units, u => u.IsMercenary);

        Assert.False(UnitMergeGuard.IsMergeAllowedByMarker(regular, mercenary));
    }
}
