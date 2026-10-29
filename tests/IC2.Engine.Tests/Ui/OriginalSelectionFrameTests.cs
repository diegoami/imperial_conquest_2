using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

/// <summary>T242 (#1010): the image-12 mask against the research description
/// (2026-10-30-unit-map-selection-drawing1010.md): a black rhombus outline on a 32 px tile, corners at
/// the edge midpoints, 3-4 px thick, 240 black and 784 white pixels; SRCERASE inverts under white.
/// The exact pixel rule, black when 14 &lt;= |x - 15.5| + |y - 15.5| &lt;= 17, is confirmed from the EXE's
/// resource as the only match (2026-10-30-selection-rhombus-band124.md, research 576d097).</summary>
public sealed class OriginalSelectionFrameTests
{
    private static int Black()
    {
        var black = 0;
        for (var y = 0; y < 32; y++)
            for (var x = 0; x < 32; x++)
                if (OriginalSelectionFrame.IsOutline(x, y)) black++;
        return black;
    }

    /// <summary>The confirmed row runs of image 12 (research #124), rows mirroring y to 31 - y.</summary>
    [Fact]
    public void Mask_matches_the_confirmed_row_runs()
    {
        for (var y = 0; y < 32; y++)
        {
            var r = y < 16 ? y : 31 - y;
            for (var x = 0; x < 32; x++)
            {
                var expected = r == 15
                    ? x <= 2 || x >= 29
                    : (x >= 14 - r && x <= 17 - r) || (x >= 14 + r && x <= 17 + r);
                Assert.True(expected == OriginalSelectionFrame.IsOutline(x, y), $"pixel ({x},{y})");
            }
        }
    }

    [Fact]
    public void Mask_has_the_described_pixel_counts() => Assert.Equal((240, 784), (Black(), 1024 - Black()));

    [Theory]
    [InlineData(15, 0)]
    [InlineData(16, 0)]
    [InlineData(0, 15)]
    [InlineData(0, 16)]
    [InlineData(31, 16)]
    [InlineData(16, 31)]
    public void Corners_touch_the_edge_midpoints(int x, int y) => Assert.True(OriginalSelectionFrame.IsOutline(x, y));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(31, 31)]
    [InlineData(15, 15)]
    [InlineData(16, 16)]
    [InlineData(4, 4)]
    public void Tile_corners_and_centre_are_white(int x, int y) => Assert.False(OriginalSelectionFrame.IsOutline(x, y));

    [Fact]
    public void Mask_is_symmetric_and_three_to_four_pixels_thick()
    {
        for (var y = 0; y < 32; y++)
        {
            var row = 0;
            for (var x = 0; x < 32; x++)
            {
                Assert.Equal(OriginalSelectionFrame.IsOutline(x, y), OriginalSelectionFrame.IsOutline(31 - x, y));
                Assert.Equal(OriginalSelectionFrame.IsOutline(x, y), OriginalSelectionFrame.IsOutline(y, x));
                if (x < 16 && OriginalSelectionFrame.IsOutline(x, y)) row++;
            }

            // Along a row each side of the line is four pixels, fewer only where the tile's edge cuts
            // it at the rhombus's corners: about three across the diagonal line.
            Assert.InRange(row, 1, 4);
            if (y is >= 3 and <= 13 or >= 18 and <= 28) Assert.Equal(4, row);
        }
    }

    [Theory]
    [InlineData(0.0f, 0)]
    [InlineData(0.999f, 31)]
    [InlineData(1.0f, 31)]
    [InlineData(0.5f, 16)]
    public void Scaled_tiles_sample_the_mask_nearest(float fraction, int pixel) =>
        Assert.Equal(pixel, OriginalSelectionFrame.MaskPixel(fraction));

    [Fact]
    public void Srcerase_inverts_under_white_and_blackens_the_outline()
    {
        Assert.Equal(0, OriginalSelectionFrame.Apply(true, 200));
        Assert.Equal(255 - 200, OriginalSelectionFrame.Apply(false, 200));
        Assert.Equal(255, OriginalSelectionFrame.Apply(false, 0));
    }
}
