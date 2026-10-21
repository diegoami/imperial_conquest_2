using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Slice.Assets;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>#1291: army and fleet markers draw the owner's three battle colours, the field shaded.</summary>
public sealed class UnitMarkerPaletteTests
{
    private static readonly World Classical =
        GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean").World;

    [Fact]
    public void Rome_draws_a_shaded_purple_field_a_white_figure_and_a_blue_frame()
    {
        var palette = UnitMarkerPalette.ForOwner(Classical, "rome")!.Value;

        // #800080 = (0.50196, 0, 0.50196), 30% toward white at the top and toward black at the bottom.
        AssertTint(palette.Top, 0.50196f + (0.49804f * 0.3f), 0.3f, 0.50196f + (0.49804f * 0.3f));
        AssertTint(palette.Bottom, 0.50196f * 0.7f, 0f, 0.50196f * 0.7f);
        AssertTint(palette.Figure, 1f, 1f, 1f);
        AssertTint(palette.Accent, 0f, 0f, 1f);
        AssertTint(palette.Edge, 0.50196f * 0.4f, 0f, 0.50196f * 0.4f);
    }

    [Fact]
    public void Every_classical_nation_has_three_hues_and_a_visible_shade()
    {
        foreach (var nation in Classical.Nations)
        {
            var palette = UnitMarkerPalette.ForOwner(Classical, nation.Id);
            Assert.NotNull(palette);
            var p = palette.Value;
            Assert.NotEqual(p.Top, p.Bottom);
            Assert.NotEqual(Rgb(p.Figure), Rgb(p.Accent));
        }
    }

    [Fact]
    public void A_nation_without_battle_colours_falls_back_to_its_marker_pair()
    {
        var world = Classical with
        {
            Nations = ValueList.From(Classical.Nations.Select(n => n.Id == "rome" ? n with { BattleColorsHex = null } : n)),
        };
        var palette = UnitMarkerPalette.ForOwner(world, "rome")!.Value;

        AssertTint(palette.Figure, 1f, 1f, 1f);
        AssertTint(palette.Accent, 0f, 0f, 1f); // glyphColorHex #0000FF
    }

    [Fact]
    public void An_unknown_nation_has_no_palette() => Assert.Null(UnitMarkerPalette.ForOwner(Classical, "atlantis"));

    private static (float, float, float) Rgb(MarkerTint t) => (t.Red, t.Green, t.Blue);

    private static void AssertTint(MarkerTint actual, float r, float g, float b)
    {
        Assert.Equal(r, actual.Red, 0.003f);
        Assert.Equal(g, actual.Green, 0.003f);
        Assert.Equal(b, actual.Blue, 0.003f);
        Assert.Equal(1f, actual.Alpha);
    }
}
