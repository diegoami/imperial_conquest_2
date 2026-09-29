using IC2.Engine.Serialization;
using IC2.Slice.Assets;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

/// <summary>
/// T94 rework round 1, N3 (the user's decision of 2026-09-29): the owner tint a marker's pack icon
/// is drawn with. <see cref="MarkerTint.ForOwner"/> is the one helper <c>GameMapView</c> builds its
/// marker colour from, so a test here asserts the exact components a texture is modulated with for a
/// loaded world's nation — the tinting half a screenshot could otherwise only cover visually.
/// The literal expectations are the loaded world's own <c>colorHex</c> values, parsed with the
/// same <c>byte / 255</c> arithmetic Godot's <c>Color.html</c> documents.
/// </summary>
public sealed class MarkerTintTests
{
    private static readonly GameDataRepository Repository = GameDataRepository.Load(ModelTestPaths.DataRoot);

    /// <summary>
    /// The tint is the owner's own colour, not a fixed value and not another nation's: the toy
    /// world's north (#c62828) and south (#1565c0) resolve to their own components.
    /// </summary>
    [Fact]
    public void Owner_tint_components_come_from_the_nations_own_color_hex()
    {
        var world = Repository.Resolve("toy-3city").World;

        var north = MarkerTint.ForOwner(world, "north");
        Assert.NotNull(north);
        Assert.Equal(0xC6 / 255f, north!.Value.Red, 6);
        Assert.Equal(0x28 / 255f, north.Value.Green, 6);
        Assert.Equal(0x28 / 255f, north.Value.Blue, 6);
        Assert.Equal(1f, north.Value.Alpha, 6);

        var south = MarkerTint.ForOwner(world, "south");
        Assert.NotNull(south);
        Assert.Equal(0x15 / 255f, south!.Value.Red, 6);
        Assert.Equal(0x65 / 255f, south.Value.Green, 6);
        Assert.Equal(0xC0 / 255f, south.Value.Blue, 6);

        Assert.NotEqual(north.Value, south.Value);
    }

    /// <summary>
    /// The fallback path: an unknown nation, a null world or an unparseable colour resolves to
    /// <see langword="null"/>, which callers draw with their unknown-nation colour — exactly the
    /// degradation the Godot-side parse used to give.
    /// </summary>
    [Fact]
    public void Unknown_owner_or_unparseable_colour_has_no_tint()
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
    [InlineData("#4C0D19", 0x4C / 255f, 0x0D / 255f, 0x19 / 255f, 1f)]
    [InlineData("4c0d19", 0x4C / 255f, 0x0D / 255f, 0x19 / 255f, 1f)]
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
