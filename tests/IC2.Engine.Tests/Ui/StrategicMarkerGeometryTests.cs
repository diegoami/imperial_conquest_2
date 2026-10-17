using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

public sealed class StrategicMarkerGeometryTests
{
    [Theory]
    [InlineData(StrategicMarkerKind.Army, 3, 24)]
    [InlineData(StrategicMarkerKind.Fleet, 3, 24)]
    [InlineData(StrategicMarkerKind.Army, 40, 32)]
    [InlineData(StrategicMarkerKind.Fleet, 40, 28)]
    [InlineData(StrategicMarkerKind.Army, 72, 48)]
    [InlineData(StrategicMarkerKind.Fleet, 72, 48)]
    public void Production_size_has_screen_minimum_proportional_middle_and_cap(StrategicMarkerKind kind, float tile, float expected) =>
        Assert.Equal(expected, StrategicMarkerGeometry.Size(kind, tile));

    [Theory]
    [InlineData(3, -80, 20)]
    [InlineData(40, 17, -95)]
    [InlineData(72, -1000, -800)]
    public void Bounds_transform_and_whole_footprint_are_the_production_pick(float tile, float panX, float panY)
    {
        var marker = new StrategicMarker("a", StrategicMarkerKind.Army, 12, 15);
        var bounds = StrategicMarkerGeometry.Bounds(marker, tile, panX, panY);
        Assert.Equal(panX + 12.5f * tile, bounds.CenterX);
        Assert.Equal(panY + 15.5f * tile, bounds.CenterY);
        foreach (var (x, y) in new[] { (bounds.X, bounds.Y), (bounds.X + bounds.Size, bounds.Y + bounds.Size), (bounds.CenterX, bounds.CenterY) })
            Assert.Equal(marker, StrategicMarkerGeometry.Pick(new[] { marker }, x, y, tile, panX, panY));
        Assert.Null(StrategicMarkerGeometry.Pick(new[] { marker }, bounds.X - .01f, bounds.CenterY, tile, panX, panY));
    }

    [Fact]
    public void Overview_overhang_hits_entity_without_changing_its_world_tile()
    {
        var marker = new StrategicMarker("a", StrategicMarkerKind.Army, 10, 20);
        var result = StrategicMarkerGeometry.Pick(new[] { marker }, 42, 61.5f, 3, 0, 0);
        Assert.Equal(marker, result); // x=42 is tile 14, four tiles beyond the marker's real x.
        Assert.Null(StrategicMarkerGeometry.Pick(new[] { marker }, 44, 61.5f, 3, 0, 0));
    }

    [Fact]
    public void Dense_overlaps_use_nearest_center_then_army_then_ordinal_id_independent_of_list_order()
    {
        var a = new StrategicMarker("z", StrategicMarkerKind.Army, 10, 10);
        var b = new StrategicMarker("b", StrategicMarkerKind.Army, 11, 10);
        var c = b with { Id = "a" };
        var fleet = b with { Id = "0", Kind = StrategicMarkerKind.Fleet };
        var markers = new[] { fleet, a, b, c };
        Assert.Equal(a, StrategicMarkerGeometry.Pick(markers, 31.5f, 31.5f, 3, 0, 0));
        Assert.Equal(c, StrategicMarkerGeometry.Pick(markers, 34.5f, 31.5f, 3, 0, 0));
        Assert.Equal(c, StrategicMarkerGeometry.Pick(markers.Reverse(), 33, 31.5f, 3, 0, 0));
        Assert.Null(StrategicMarkerGeometry.Pick(markers, 100, 100, 3, 0, 0));
    }

    [Fact]
    public void Pointer_winner_is_drawn_above_selected_and_idle_boards_without_changing_pick()
    {
        var army = new StrategicMarker("a", StrategicMarkerKind.Army, 10, 10);
        var fleet = new StrategicMarker("f", StrategicMarkerKind.Fleet, 11, 10);
        var markers = new[] { army, fleet };
        Assert.Equal(army, StrategicMarkerGeometry.DrawOrder(markers, null, null, null).Last());
        Assert.Equal(fleet, StrategicMarkerGeometry.DrawOrder(markers, null, "f", null).Last());
        var picked = StrategicMarkerGeometry.Pick(markers, 31.5f, 31.5f, 3, 0, 0);
        Assert.Equal(army, StrategicMarkerGeometry.DrawOrder(markers, null, "f", picked).Last());
        Assert.Equal(picked, StrategicMarkerGeometry.Pick(markers.Reverse(), 31.5f, 31.5f, 3, 0, 0));
    }


    [Fact]
    public void Selection_promotion_keeps_entity_kind_when_army_and_fleet_share_an_id()
    {
        var army = new StrategicMarker("shared", StrategicMarkerKind.Army, 10, 10);
        var fleet = new StrategicMarker("shared", StrategicMarkerKind.Fleet, 11, 10);
        Assert.Equal(fleet, StrategicMarkerGeometry.DrawOrder(new[] { fleet, army }, null, "shared", null).Last());
        Assert.Equal(army, StrategicMarkerGeometry.DrawOrder(new[] { fleet, army }, "shared", null, null).Last());
    }

}
