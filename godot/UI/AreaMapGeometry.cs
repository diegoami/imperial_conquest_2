using System;

namespace IC2.Slice.UI;

/// <summary>
/// A rectangle in the overview mini-map's own pixels — the <see cref="AreaMapGeometry"/> return type,
/// deliberately a plain value type rather than a Godot one so this file stays Godot-free (the test
/// project links it directly, the seam <c>godot/UI/MapClickRules.cs</c> already established).
/// </summary>
public readonly record struct AreaMapPixelRect(float X, float Y, float Width, float Height)
{
    /// <summary>The rectangle's right edge (<see cref="X"/> + <see cref="Width"/>).</summary>
    public float Right => X + Width;

    /// <summary>The rectangle's bottom edge (<see cref="Y"/> + <see cref="Height"/>).</summary>
    public float Bottom => Y + Height;
}

/// <summary>
/// The overview mini-map's geometry — <c>docs/game-design.md</c>'s Screen/flow "Overview mini-map"
/// bullet: the original's Area map, a small picture of the whole world, with "a rectangle [that] shows
/// the map's current view". This class is the whole Godot-free arithmetic: the tile-to-pixel mapping at
/// the mini-map's own size, and the order map's visible tile rectangle mapped onto it and clamped.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Evidence.</strong> The original's Area map is "the whole 320 × 140 map, scaled" and "draws a
/// rectangle for the Unit map's current view" (<c>docs/investigations/original-ui-command-audit.md</c>
/// §1.5; the click that re-centres the order map is confirmed by
/// <c>2026-09-29-nation-view-origin-and-unit-map-clicks.md</c>). The design viewport is 1500&#160;px
/// wide and the right-hand column already holds the context panel with a 340&#160;px floor
/// (<c>godot/Checks/ContextPanelWidthCheck.cs</c>), so the mini-map's own pixel budget is
/// <see cref="DefaultMaxPixelWidth"/> × <see cref="DefaultMaxPixelHeight"/>: 320&#160;px fits inside the
/// 340&#160;px floor without widening the screen, and 140&#160;px is the world's own height at one pixel
/// per tile. Both maxima are <strong>[designed]</strong> — a search of <c>docs/game-design.md</c>'s
/// Screen/flow "Overview mini-map" bullet, <c>docs/design-audit.md</c> §2 and
/// <c>docs/investigations/original-ui-command-audit.md</c> §1.5 found no report naming a mini-map size —
/// and the integer scale below keeps every tile whole pixels (the "one pixel or more per tile" line in
/// the task entry's Scope).
/// </para>
/// </remarks>
public sealed class AreaMapGeometry
{
    /// <summary>The mini-map's width budget: the 340&#160;px right-hand column's floor leaves 20&#160;px
    /// of margin, so a 320-tile-wide world renders at one pixel per tile without widening the screen.
    /// <strong>[designed]</strong> — searched <c>docs/game-design.md</c>'s Screen/flow "Overview mini-map"
    /// bullet, <c>docs/design-audit.md</c> §2 and
    /// <c>docs/investigations/original-ui-command-audit.md</c> §1.5 for a mini-map size and found none;
    /// chosen against the column's own 340&#160;px floor
    /// (<c>godot/Checks/ContextPanelWidthCheck.cs</c>).</summary>
    public const int DefaultMaxPixelWidth = 320;

    /// <summary>The mini-map's height budget. <strong>[designed]</strong> — the same search
    /// (<c>docs/game-design.md</c>'s Screen/flow bullet, <c>docs/design-audit.md</c> §2 and
    /// <c>docs/investigations/original-ui-command-audit.md</c> §1.5) found no reported size; the
    /// classical world is 140 tiles tall, so this leaves a little headroom for a shorter world to scale
    /// up while the context panel keeps the rest of the column.</summary>
    public const int DefaultMaxPixelHeight = 180;

    private AreaMapGeometry(int worldWidth, int worldHeight, float scale)
    {
        WorldWidth = worldWidth;
        WorldHeight = worldHeight;
        Scale = scale;
        PixelWidth = (int)Math.Round(worldWidth * scale);
        PixelHeight = (int)Math.Round(worldHeight * scale);
    }

    /// <summary>Builds the geometry for a world under the mini-map's default pixel budget.</summary>
    public static AreaMapGeometry ForWorld(int worldWidth, int worldHeight) =>
        ForWorld(worldWidth, worldHeight, DefaultMaxPixelWidth, DefaultMaxPixelHeight);

    /// <summary>
    /// Builds the geometry for a world under a caller-chosen pixel budget. The scale is the largest
    /// <em>integer</em> number of pixels per tile that still fits, and never below 1 (Scope: "one pixel
    /// or more per tile") — a fractional scale would leave seams between tiles.
    /// </summary>
    public static AreaMapGeometry ForWorld(int worldWidth, int worldHeight, int maxPixelWidth, int maxPixelHeight)
    {
        if (worldWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(worldWidth));
        }

        if (worldHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(worldHeight));
        }

        var fit = Math.Min(maxPixelWidth / (double)worldWidth, maxPixelHeight / (double)worldHeight);
        var scale = (float)Math.Max(1d, Math.Floor(fit));
        return new AreaMapGeometry(worldWidth, worldHeight, scale);
    }

    /// <summary>The world's tile width (the classical world's 320).</summary>
    public int WorldWidth { get; }

    /// <summary>The world's tile height (the classical world's 140).</summary>
    public int WorldHeight { get; }

    /// <summary>Mini-map pixels per world tile — always 1 or more.</summary>
    public float Scale { get; }

    /// <summary>The mini-map's own pixel width (<see cref="WorldWidth"/> × <see cref="Scale"/>).</summary>
    public int PixelWidth { get; }

    /// <summary>The mini-map's own pixel height (<see cref="WorldHeight"/> × <see cref="Scale"/>).</summary>
    public int PixelHeight { get; }

    /// <summary>A tile's left edge, in mini-map pixels.</summary>
    public float TileToPixelX(float tileX) => tileX * Scale;

    /// <summary>A tile's top edge, in mini-map pixels.</summary>
    public float TileToPixelY(float tileY) => tileY * Scale;

    /// <summary>The tile containing a mini-map pixel's x — the inverse of <see cref="TileToPixelX"/>.</summary>
    public int PixelToTileX(float pixelX) => (int)Math.Floor(pixelX / Scale);

    /// <summary>The tile containing a mini-map pixel's y — the inverse of <see cref="TileToPixelY"/>.</summary>
    public int PixelToTileY(float pixelY) => (int)Math.Floor(pixelY / Scale);

    /// <summary>
    /// The centre of a tile, in mini-map pixels — what a click on the mini-map is resolved to before
    /// the order map is re-centred on it.
    /// </summary>
    public AreaMapPixelRect TileCentre(int tileX, int tileY) =>
        new((tileX + 0.5f) * Scale, (tileY + 0.5f) * Scale, 0f, 0f);

    /// <summary>
    /// The order map's current view as a rectangle on the mini-map, from its centre tile and zoom.
    /// <paramref name="viewportWidth"/> and <paramref name="viewportHeight"/> are the order map's own
    /// size in pixels and <paramref name="baseTileSize"/> its one-tile size at zoom 1, so the visible
    /// tile count is <c>viewport / (baseTileSize × zoom)</c>. The result is clamped to the mini-map's
    /// own rect, so a view that runs off the world's edge stops at the edge.
    /// </summary>
    public AreaMapPixelRect ViewRectangle(
        float centreTileX,
        float centreTileY,
        float zoom,
        float viewportWidth,
        float viewportHeight,
        float baseTileSize)
    {
        var tileSize = baseTileSize * zoom;
        var halfWidth = viewportWidth / (2f * tileSize);
        var halfHeight = viewportHeight / (2f * tileSize);
        return MapTileRect(
            centreTileX - halfWidth,
            centreTileY - halfHeight,
            centreTileX + halfWidth,
            centreTileY + halfHeight);
    }

    /// <summary>
    /// Maps an order-map tile rectangle (left, top, right, bottom in tile coordinates) onto the
    /// mini-map and clamps it to the world's own bounds. This is the same mapping
    /// <see cref="ViewRectangle"/> uses, so the mini-map can also take the order map's own
    /// <c>VisibleTileRect</c> directly and get the identical rectangle.
    /// </summary>
    public AreaMapPixelRect MapTileRect(float left, float top, float right, float bottom)
    {
        // N1 (T102 review): no swap of a reversed pair is needed. Clamping keeps left <= right and
        // top <= bottom (the clamp is monotonic), and both callers pass an ordered rectangle:
        // ViewRectangle builds centre +/- half, and VisibleTileRect always has a non-negative size.
        var x0 = Math.Clamp(left, 0f, WorldWidth) * Scale;
        var y0 = Math.Clamp(top, 0f, WorldHeight) * Scale;
        var x1 = Math.Clamp(right, 0f, WorldWidth) * Scale;
        var y1 = Math.Clamp(bottom, 0f, WorldHeight) * Scale;
        return new AreaMapPixelRect(x0, y0, x1 - x0, y1 - y0);
    }
}
