using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

public sealed class StrategicMarkerGeometryTests
{
    [Theory]
    [InlineData(StrategicMarkerKind.Army, 3, 2.4f)]
    [InlineData(StrategicMarkerKind.Fleet, 3, 2.1f)]
    [InlineData(StrategicMarkerKind.Army, 40, 32)]
    [InlineData(StrategicMarkerKind.Fleet, 40, 28)]
    [InlineData(StrategicMarkerKind.Army, 72, 48)]
    [InlineData(StrategicMarkerKind.Fleet, 72, 48)]
    public void Production_size_has_tile_constrained_proportional_middle_and_cap(StrategicMarkerKind kind, float tile, float expected) =>
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
        Assert.Equal(marker, StrategicMarkerGeometry.Pick(new[] { marker }, bounds.X - .01f, bounds.CenterY, tile, panX, panY)); // Real-tile padding remains selectable.
    }

    [Theory]
    [InlineData(.1f, -80, 20)]
    [InlineData(3f, 0, 0)]
    [InlineData(40f, 17, -95)]
    [InlineData(72f, -1000, -800)]
    public void Whole_tile_padding_edges_and_adjacent_tiles_keep_real_target(float tile, float panX, float panY)
    {
        var army = new StrategicMarker("z", StrategicMarkerKind.Army, 10, 20);
        var first = army with { Id = "a" };
        var fleet = army with { Id = "0", Kind = StrategicMarkerKind.Fleet };
        var adjacent = army with { Id = "next", X = 11 };
        var markers = new[] { fleet, army, adjacent, first };
        var left = panX + 10 * tile;
        var top = panY + 20 * tile;
        foreach (var list in new[] { markers, markers.Reverse().ToArray() })
        {
            Assert.Equal(first, StrategicMarkerGeometry.Pick(list, left + tile * .01f, top + tile * .01f, tile, panX, panY));
            Assert.Equal(first, StrategicMarkerGeometry.Pick(list, left + tile * .99f, top + tile * .99f, tile, panX, panY));
            Assert.Equal(adjacent, StrategicMarkerGeometry.Pick(list, panX + 11.5f * tile, top + tile / 2, tile, panX, panY));
            Assert.Null(StrategicMarkerGeometry.Pick(list, panX + 9.5f * tile, top + tile / 2, tile, panX, panY));
        }
        foreach (var selected in new[] { false, true })
        foreach (var marker in markers)
        {
            var bounds = StrategicMarkerGeometry.Bounds(marker, tile, panX, panY);
            var outline = StrategicMarkerGeometry.Outline(bounds, selected);
            var half = StrategicMarkerGeometry.StrokeWidth(bounds.Size, selected) / 2;
            Assert.True(bounds.Size <= tile);
            Assert.True(outline.Size > 0);
            Assert.Equal(bounds.X, outline.X - half, 4);
            Assert.Equal(bounds.Y, outline.Y - half, 4);
            Assert.Equal(bounds.X + bounds.Size, outline.X + outline.Size + half, 4);
        }
    }

    [Fact]
    public void Exact_tile_boundary_belongs_to_next_tile()
    {
        var a = new StrategicMarker("a", StrategicMarkerKind.Army, 10, 20);
        var b = a with { Id = "b", X = 11 };
        Assert.Equal(b, StrategicMarkerGeometry.Pick(new[] { a, b }, 33, 61, 3, 0, 0));
        Assert.Null(StrategicMarkerGeometry.Pick(new[] { a }, 33, 61, 3, 0, 0));
        Assert.Null(StrategicMarkerGeometry.Pick(new[] { a }, 31, 63, 3, 0, 0));
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
