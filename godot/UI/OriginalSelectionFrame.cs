namespace IC2.Slice.UI;

/// <summary>
/// T242 (#1010): the original's selection frame, drawn by the classic look ("Classic tiles") over the
/// selected cell. <c>TUnitMap_SelectUnit</c> calls <c>FUN_004465a8</c> for any selected cell, which
/// draws the unit map's image 12 over it with <c>CopyMode = SRCERASE</c>
/// (research <c>2026-10-30-unit-map-selection-drawing1010.md</c>, <c>[confirmed]</c>).
/// <para>
/// Image 12 is described there as a black rhombus outline on a 32 px tile, corners at the edge
/// midpoints, 3–4 px thick, with 240 black and 784 white pixels. The original image never enters the
/// repository; this mask is reconstructed from that description: a pixel of the 32×32 tile is black
/// when the taxicab distance of its centre from the tile's centre lies in
/// [<see cref="LineInner"/>, <see cref="LineOuter"/>]. That band touches the edge midpoints, is four
/// pixels wide along a row (about three across the line) and holds exactly 240 pixels.
/// </para>
/// <para>
/// SRCERASE is <c>source AND NOT destination</c>: where the image is white the screen is inverted
/// (the whole tile, terrain and marker, outside the line too), and where it is black the screen turns
/// black. There is no timer, so the frame never blinks. The tile scales with the map; the mask is
/// sampled nearest, as an image drawn at a larger size would be.
/// </para>
/// Godot-free, so <c>tests/IC2.Engine.Tests</c> pins the geometry; <c>OriginalSelectionFrame.gdshader</c>
/// evaluates the same rule from the uniforms <c>GameMapView</c> sets from these constants.
/// </summary>
public static class OriginalSelectionFrame
{
    /// <summary>The original's tile and image size, in pixels.</summary>
    public const int MaskSize = 32;

    /// <summary>The smallest taxicab distance (pixel centre to tile centre) on the black line.</summary>
    public const int LineInner = 14;

    /// <summary>The largest taxicab distance (pixel centre to tile centre) on the black line.</summary>
    public const int LineOuter = 17;

    /// <summary>Whether mask pixel (<paramref name="x"/>, <paramref name="y"/>), each 0..31, is black.</summary>
    public static bool IsOutline(int x, int y)
    {
        if (x < 0 || y < 0 || x >= MaskSize || y >= MaskSize)
        {
            return false;
        }

        // Pixel centres sit at half-integers, and so does the tile's centre (15.5): doubling keeps
        // everything integral.
        var distance2 = System.Math.Abs((2 * x) + 1 - MaskSize) + System.Math.Abs((2 * y) + 1 - MaskSize);
        return distance2 >= 2 * LineInner && distance2 <= 2 * LineOuter;
    }

    /// <summary>The mask at a fraction (<paramref name="u"/>, <paramref name="v"/>) of the tile, each in [0, 1).</summary>
    public static bool IsOutlineAt(float u, float v) =>
        IsOutline(MaskPixel(u), MaskPixel(v));

    /// <summary>The mask pixel a fraction of the tile falls in, nearest sampling, clamped to the tile.</summary>
    public static int MaskPixel(float fraction) =>
        System.Math.Clamp((int)System.MathF.Floor(fraction * MaskSize), 0, MaskSize - 1);

    /// <summary>SRCERASE of one 8-bit channel under a mask pixel: the outline is black, the rest inverted.</summary>
    public static byte Apply(bool outline, byte screen) => outline ? (byte)0 : (byte)(255 - screen);
}
