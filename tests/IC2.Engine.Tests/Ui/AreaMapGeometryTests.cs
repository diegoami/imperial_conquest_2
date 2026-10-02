using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T102 Done-when 1: the overview mini-map's Godot-free geometry pinned against the classical world's
/// own 320 × 140 size (the world <c>godot/Checks/MapClipCheck.cs</c> already drives). The tile-to-pixel
/// mapping is asserted both ways for the four corners and a middle tile, and the view rectangle is
/// asserted from a given order-map centre and zoom, including the clamp at the world's edges. Every
/// expected number is derived by hand from the world's own dimensions and the mini-map's
/// <see cref="AreaMapGeometry.DefaultMaxPixelWidth"/> budget, never from a literal copied out of the
/// production file.
/// </summary>
public sealed class AreaMapGeometryTests
{
    private const float BaseTileSize = 6f;

    /// <summary>The classical world at the mini-map's default budget: 320 × 140 tiles, one pixel each.</summary>
    private static AreaMapGeometry ClassicalMap() =>
        AreaMapGeometry.ForWorld(320, 140, AreaMapGeometry.DefaultMaxPixelWidth, AreaMapGeometry.DefaultMaxPixelHeight);

    /// <summary>A deliberately tiny world, to exercise a scale above one pixel per tile.</summary>
    private static AreaMapGeometry TinyMap() => AreaMapGeometry.ForWorld(8, 6, 320, 180);

    [Fact]
    public void ClassicalMap_IsTheWorldAtOnePixelPerTile()
    {
        var map = ClassicalMap();

        Assert.Equal(320, map.PixelWidth);
        Assert.Equal(140, map.PixelHeight);
        Assert.Equal(1f, map.Scale);
    }

    [Fact]
    public void SmallWorld_ScalesUpWholePixelsPerTile()
    {
        var map = TinyMap();

        // 8 × 6 tiles under a 320 × 180 budget: min(320/8, 180/6) = 30 pixels per tile.
        Assert.Equal(30f, map.Scale);
        Assert.Equal(240, map.PixelWidth);
        Assert.Equal(180, map.PixelHeight);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(319, 0)]
    [InlineData(0, 139)]
    [InlineData(319, 139)]
    [InlineData(160, 70)]
    public void TileToPixel_AndBack_AtTheClassicalMapCornerOrMiddle(int tileX, int tileY)
    {
        var map = ClassicalMap();

        var pixelX = map.TileToPixelX(tileX);
        var pixelY = map.TileToPixelY(tileY);

        Assert.Equal(map.TileToPixelX(tileX), pixelX);
        Assert.Equal((float)tileX, pixelX);
        Assert.Equal((float)tileY, pixelY);
        Assert.Equal(tileX, map.PixelToTileX(pixelX));
        Assert.Equal(tileY, map.PixelToTileY(pixelY));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(7, 0)]
    [InlineData(0, 5)]
    [InlineData(7, 5)]
    [InlineData(4, 3)]
    public void TileToPixel_AndBack_AtTheScaledMapCornerOrMiddle(int tileX, int tileY)
    {
        var map = TinyMap();

        var pixelX = map.TileToPixelX(tileX);
        var pixelY = map.TileToPixelY(tileY);

        Assert.Equal(tileX * 30f, pixelX);
        Assert.Equal(tileY * 30f, pixelY);
        Assert.Equal(tileX, map.PixelToTileX(pixelX));
        Assert.Equal(tileY, map.PixelToTileY(pixelY));

        // A click lands inside the tile, not only on its top-left corner.
        Assert.Equal(tileX, map.PixelToTileX(pixelX + 15f));
        Assert.Equal(tileY, map.PixelToTileY(pixelY + 15f));
    }

    [Fact]
    public void TileMapping_CoversTheWholeMiniMap()
    {
        var map = ClassicalMap();

        Assert.Equal(0f, map.TileToPixelX(0));
        Assert.Equal(0f, map.TileToPixelY(0));
        Assert.Equal(map.PixelWidth, map.TileToPixelX(map.WorldWidth));
        Assert.Equal(map.PixelHeight, map.TileToPixelY(map.WorldHeight));
    }

    [Fact]
    public void ViewRectangle_FromCentreAndZoom_MapsTheVisibleTiles()
    {
        var map = ClassicalMap();

        // Order map 1200 × 600 at zoom 2 with a 6-px base tile: 50 tiles half-wide, 25 half-tall.
        var rect = map.ViewRectangle(160f, 70f, zoom: 2f, 1200f, 600f, BaseTileSize);

        Assert.Equal(110f, rect.X);
        Assert.Equal(45f, rect.Y);
        Assert.Equal(100f, rect.Width);
        Assert.Equal(50f, rect.Height);
    }

    [Fact]
    public void ViewRectangle_ClampsAtTheTopLeftEdge()
    {
        var map = ClassicalMap();

        var rect = map.ViewRectangle(5f, 5f, zoom: 2f, 1200f, 600f, BaseTileSize);

        Assert.Equal(0f, rect.X);
        Assert.Equal(0f, rect.Y);
        Assert.Equal(55f, rect.Width);
        Assert.Equal(30f, rect.Height);
    }

    [Fact]
    public void ViewRectangle_ClampsAtTheBottomRightEdge()
    {
        var map = ClassicalMap();

        var rect = map.ViewRectangle(315f, 135f, zoom: 2f, 1200f, 600f, BaseTileSize);

        Assert.Equal(265f, rect.X);
        Assert.Equal(110f, rect.Y);
        Assert.Equal(55f, rect.Width);
        Assert.Equal(30f, rect.Height);
    }

    [Fact]
    public void ViewRectangle_EqualsTheVisibleTileRectMappedThroughMapTileRect()
    {
        var map = ClassicalMap();

        // ViewRectangle is MapTileRect over the same half-extents, so the mini-map's two entry points
        // agree: the order map's VisibleTileRect maps to the identical rectangle.
        var fromCentre = map.ViewRectangle(160f, 70f, zoom: 2f, 1200f, 600f, BaseTileSize);

        const float halfWidthTiles = 1200f / (2f * 2f * BaseTileSize);
        const float halfHeightTiles = 600f / (2f * 2f * BaseTileSize);
        var fromRect = map.MapTileRect(
            160f - halfWidthTiles,
            70f - halfHeightTiles,
            160f + halfWidthTiles,
            70f + halfHeightTiles);

        Assert.Equal(fromCentre, fromRect);
    }

    [Fact]
    public void ZeroSizedWorld_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaMapGeometry.ForWorld(0, 140));
        Assert.Throws<ArgumentOutOfRangeException>(() => AreaMapGeometry.ForWorld(320, 0));
    }
}
