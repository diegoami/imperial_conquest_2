using IC2.Engine.Naval;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T14 Naval", Done-when 8: 90 ships owner 1 → 333; 70 ships owner 3 →
/// 335 — <c>decompiled-unit-map-orders-and-record-fields.md</c> part 3.
/// </summary>
public sealed class FleetMarkerTests
{
    [Fact]
    public void NinetyShipsOwnerOne_Encodes333()
    {
        Assert.Equal(333, FleetMarker.Encode(ships: 90, ownerCode: 1, NavalTestbed.Ruleset));
    }

    [Fact]
    public void SeventyShipsOwnerThree_Encodes335()
    {
        Assert.Equal(335, FleetMarker.Encode(ships: 70, ownerCode: 3, NavalTestbed.Ruleset));
    }

    [Theory]
    [InlineData(1, 0)] // < 25: tier 0.
    [InlineData(24, 0)]
    [InlineData(25, 1)] // 25..49: tier 1.
    [InlineData(49, 1)]
    [InlineData(50, 2)] // >= 50: tier 2.
    [InlineData(200, 2)]
    public void BandSelectionMatchesTheConfirmedThresholds(int ships, int expectedTierIndex)
    {
        var ruleset = NavalTestbed.Ruleset;
        var expected = ruleset.Naval.MarkerBandBaseTier1 + (expectedTierIndex * ruleset.Naval.MarkerBandStep);
        Assert.Equal(expected, FleetMarker.Encode(ships, ownerCode: 0, ruleset) - 0);
    }

    [Fact]
    public void OwnerCodeIsRecoverableModuloTheBandStep()
    {
        var ruleset = NavalTestbed.Ruleset;
        var marker333 = FleetMarker.Encode(90, 1, ruleset);
        var marker335 = FleetMarker.Encode(70, 3, ruleset);

        Assert.Equal(1, (marker333 - ruleset.Naval.MarkerBandBaseTier1) % ruleset.Naval.MarkerBandStep);
        Assert.Equal(3, (marker335 - ruleset.Naval.MarkerBandBaseTier1) % ruleset.Naval.MarkerBandStep);
    }
}
