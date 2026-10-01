using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Slice.Assets;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T97: the (background, foreground) pair a marker is drawn with. <see cref="MarkerTint.ForOwner"/> is
/// the one helper <c>GameMapView</c> builds its marker colours from — the background square is filled
/// with <see cref="NationDefinition.ColorHex"/> and the silhouette is tinted with
/// <see cref="NationDefinition.GlyphColorHex"/> — so a test here asserts the exact components a marker
/// is drawn with for a loaded world's nation, the colouring half a screenshot could otherwise only
/// cover visually. The literal expectations are the report's own values
/// (<c>2026-09-29-nation-marker-colours.md</c>), parsed with the same <c>byte / 255</c> arithmetic
/// Godot's <c>Color.html</c> documents.
/// </summary>
public sealed class MarkerTintTests
{
    private static readonly GameDataRepository Repository = GameDataRepository.Load(ModelTestPaths.DataRoot);

    /// <summary>
    /// The pair is the owner's own two colours, not a fixed value and not another nation's: the
    /// report's Rome is blue-on-purple (128, 0, 128)/(0, 0, 255) and Carthage white-on-red
    /// (255, 0, 0)/(255, 255, 255).
    /// </summary>
    [Fact]
    public void Owner_pair_components_come_from_the_nations_own_background_and_foreground_hex()
    {
        var world = Repository.Resolve("classical-mediterranean").World;

        var rome = MarkerTint.ForOwner(world, "rome");
        Assert.NotNull(rome);
        Assert.Equal(0x80 / 255f, rome!.Value.Background.Red, 6);
        Assert.Equal(0x00 / 255f, rome.Value.Background.Green, 6);
        Assert.Equal(0x80 / 255f, rome.Value.Background.Blue, 6);
        Assert.Equal(1f, rome.Value.Background.Alpha, 6);
        Assert.Equal(0x00 / 255f, rome.Value.Foreground.Red, 6);
        Assert.Equal(0x00 / 255f, rome.Value.Foreground.Green, 6);
        Assert.Equal(0xFF / 255f, rome.Value.Foreground.Blue, 6);

        var carthage = MarkerTint.ForOwner(world, "carthage");
        Assert.NotNull(carthage);
        Assert.Equal(1f, carthage!.Value.Background.Red, 6);
        Assert.Equal(0f, carthage.Value.Background.Green, 6);
        Assert.Equal(0f, carthage.Value.Background.Blue, 6);
        Assert.Equal(1f, carthage.Value.Foreground.Red, 6);
        Assert.Equal(1f, carthage.Value.Foreground.Green, 6);
        Assert.Equal(1f, carthage.Value.Foreground.Blue, 6);

        Assert.NotEqual(rome.Value.Background, carthage.Value.Background);
    }

    /// <summary>
    /// The report's two shared backgrounds are shared on purpose, and the foreground is what tells
    /// each pair apart: Carthage and Media both fill red, Ptolemaic and Illyria both fill navy.
    /// </summary>
    [Fact]
    public void Shared_backgrounds_are_told_apart_by_their_foreground()
    {
        var world = Repository.Resolve("classical-mediterranean").World;

        var carthage = MarkerTint.ForOwner(world, "carthage")!.Value;
        var media = MarkerTint.ForOwner(world, "media")!.Value;
        Assert.Equal(carthage.Background, media.Background);
        Assert.NotEqual(carthage.Foreground, media.Foreground);

        var ptolemaic = MarkerTint.ForOwner(world, "ptolemaic")!.Value;
        var illyria = MarkerTint.ForOwner(world, "illyria")!.Value;
        Assert.Equal(ptolemaic.Background, illyria.Background);
        Assert.NotEqual(ptolemaic.Foreground, illyria.Foreground);
    }

    /// <summary>
    /// The fallback path, pinned on a world built for it: a nation with no <c>glyphColorHex</c> gets
    /// white on a dark background and black on a light one (the report records a foreground for all 16
    /// classical nations, so the fallback only ever applies to worlds that predate the field — the
    /// shipped toy world is one). The toy world's own two nations still load and resolve their
    /// backgrounds unchanged, which is Done-when 4's no-edit check at the data level.
    /// </summary>
    [Fact]
    public void Nations_without_a_foreground_get_white_on_dark_and_black_on_light()
    {
        var toy = Repository.Resolve("toy-3city").World;

        var north = MarkerTint.ForOwner(toy, "north");
        Assert.NotNull(north);
        Assert.Equal(0xC6 / 255f, north!.Value.Background.Red, 6);
        Assert.Equal(1f, north.Value.Foreground.Red, 6);
        Assert.Equal(1f, north.Value.Foreground.Green, 6);
        Assert.Equal(1f, north.Value.Foreground.Blue, 6);

        var south = MarkerTint.ForOwner(toy, "south");
        Assert.NotNull(south);
        Assert.Equal(0x15 / 255f, south!.Value.Background.Red, 6);
        Assert.Equal(1f, south.Value.Foreground.Red, 6);

        // Both toy backgrounds are dark, so the light-background half needs a nation built for it:
        // the toy world with its nations replaced by a white and a black one.
        var light = new NationDefinition("light", "Light", "#FFFFFF", "Leader", null, 0, 0, 0, 0, 0, 0, 0);
        var dark = new NationDefinition("dark", "Dark", "#000000", "Leader", null, 0, 0, 0, 0, 0, 0, 0);
        var probe = toy with { Nations = ValueList.Of(light, dark) };

        var onLight = MarkerTint.ForOwner(probe, "light")!.Value;
        Assert.Equal(1f, onLight.Background.Red, 6);
        Assert.Equal(0f, onLight.Foreground.Red, 6);
        Assert.Equal(0f, onLight.Foreground.Green, 6);
        Assert.Equal(0f, onLight.Foreground.Blue, 6);

        var onDark = MarkerTint.ForOwner(probe, "dark")!.Value;
        Assert.Equal(0f, onDark.Background.Red, 6);
        Assert.Equal(1f, onDark.Foreground.Red, 6);
        Assert.Equal(1f, onDark.Foreground.Green, 6);
        Assert.Equal(1f, onDark.Foreground.Blue, 6);
    }

    /// <summary>
    /// The fallback helper itself: a background's WCAG relative luminance picks the glyph colour, with
    /// the threshold at 0.5 (the report gives no rule, so this is the pinned [designed] choice).
    /// </summary>
    [Theory]
    [InlineData("#000000", 1f, 1f, 1f)]
    [InlineData("#800000", 1f, 1f, 1f)]
    [InlineData("#FFFFFF", 0f, 0f, 0f)]
    [InlineData("#FFFF00", 0f, 0f, 0f)]
    [InlineData("#808080", 0f, 0f, 0f)]
    public void Fallback_foreground_is_white_on_dark_and_black_on_light(
        string backgroundHex, float red, float green, float blue)
    {
        var background = MarkerTint.TryParseHtml(backgroundHex)!.Value;
        var foreground = MarkerTint.FallbackForeground(background);

        Assert.Equal(red, foreground.Red, 6);
        Assert.Equal(green, foreground.Green, 6);
        Assert.Equal(blue, foreground.Blue, 6);
        Assert.Equal(1f, foreground.Alpha, 6);
    }

    /// <summary>
    /// The fallback path: an unknown nation, a null world or an unparseable background resolves to
    /// <see langword="null"/>, which callers draw with their unknown-nation colour — exactly the
    /// degradation the Godot-side parse used to give.
    /// </summary>
    [Fact]
    public void Unknown_owner_or_unparseable_colour_has_no_pair()
    {
        var world = Repository.Resolve("toy-3city").World;

        Assert.Null(MarkerTint.ForOwner(world, "nowhere"));
        Assert.Null(MarkerTint.ForOwner(null, "north"));
        Assert.Null(MarkerTint.TryParseHtml(null));
        Assert.Null(MarkerTint.TryParseHtml("  "));
        Assert.Null(MarkerTint.TryParseHtml("not a colour"));
        Assert.Null(MarkerTint.TryParseHtml("#AABBC"));
        Assert.Null(MarkerTint.TryParseHtml("#55aaFF5"));
        Assert.Null(MarkerTint.TryParseHtml("0x123456"));
    }

    /// <summary>
    /// The parser accepts exactly the HTML shapes Godot does, with Godot's documented normalisation:
    /// <c>html("663399cc") == Color(0.4, 0.2, 0.6, 0.8)</c> and <c>html("#0F0") == Color(0, 1, 0)</c>
    /// are the class reference's own examples, asserted here on the Godot-free twin.
    /// </summary>
    [Theory]
    [InlineData("#0F0", 0f, 1f, 0f, 1f)]
    [InlineData("#f00", 1f, 0f, 0f, 1f)]
    [InlineData("663399cc", 0.4f, 0.2f, 0.6f, 0.8f)]
    [InlineData("#800080", 0x80 / 255f, 0x00 / 255f, 0x80 / 255f, 1f)]
    [InlineData("800080", 0x80 / 255f, 0x00 / 255f, 0x80 / 255f, 1f)]
    public void Tint_parser_matches_godots_documented_html_parse(
        string html, float red, float green, float blue, float alpha)
    {
        var tint = MarkerTint.TryParseHtml(html);

        Assert.NotNull(tint);
        Assert.Equal(red, tint!.Value.Red, 6);
        Assert.Equal(green, tint.Value.Green, 6);
        Assert.Equal(blue, tint.Value.Blue, 6);
        Assert.Equal(alpha, tint.Value.Alpha, 6);
    }
}
