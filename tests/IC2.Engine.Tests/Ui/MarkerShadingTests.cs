using IC2.Slice.Assets;
using Xunit;

namespace IC2.Engine.Tests.Ui;

/// <summary>The 2026-10-21 marker pass: the shared shading of town, army and fleet markers.</summary>
public sealed class MarkerShadingTests
{
    private static readonly MarkerTint Purple = new(0.5f, 0.1f, 0.5f, 1f);

    [Fact]
    public void A_field_is_lit_at_the_top_shaded_at_the_bottom_and_edged_darker_still()
    {
        var top = MarkerShading.Top(Purple);
        var bottom = MarkerShading.Bottom(Purple);
        var edge = MarkerShading.Edge(Purple);

        Assert.True(MarkerShading.Luminance(top) > MarkerShading.Luminance(Purple));
        Assert.True(MarkerShading.Luminance(bottom) < MarkerShading.Luminance(Purple));
        Assert.True(MarkerShading.Luminance(edge) < MarkerShading.Luminance(bottom));
        Assert.Equal(1f, top.Alpha);
    }

    [Theory]
    [InlineData(1f, 1f, 1f, true)]   // a white figure gets a dark halo
    [InlineData(0f, 0f, 1f, false)]  // Rome's blue figure gets a light halo
    [InlineData(0f, 0f, 0f, false)]  // a black figure gets a light halo
    public void The_halo_contrasts_with_the_figure(float r, float g, float b, bool darkHalo)
    {
        var halo = MarkerShading.Halo(new MarkerTint(r, g, b, 1f));

        Assert.Equal(darkHalo, MarkerShading.Luminance(halo) < 0.5f);
        Assert.Equal(MarkerShading.HaloAlpha, halo.Alpha);
    }

    [Fact]
    public void The_unit_palette_and_the_town_shading_lighten_and_darken_by_the_same_amount()
    {
        Assert.Equal(UnitMarkerPalette.Shade, MarkerShading.Shade);
        Assert.Equal(UnitMarkerPalette.EdgeShade, MarkerShading.EdgeShade);
    }
}
