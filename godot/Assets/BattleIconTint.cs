using IC2.Engine.Model;

namespace IC2.Slice.Assets;

/// <summary>
/// One nation's three tactical-battle icon colours, as 8-bit channels, in the original's substitution
/// order: <see cref="First"/> replaces the template's purple, <see cref="Second"/> its white and
/// <see cref="Third"/> its blue.
/// </summary>
public readonly record struct BattleIconColors(
    (byte R, byte G, byte B) First,
    (byte R, byte G, byte B) Second,
    (byte R, byte G, byte B) Third);

/// <summary>
/// T128: the three-region recolour of a battle unit icon, Godot-free. The original's
/// <c>FUN_0044A6C8</c> replaces a template's purple <c>0x800080</c>, white and blue with the nation's
/// three colours (nation <c>+0x424</c>, <c>+0x428</c>, <c>+0x42C</c>) <strong>[confirmed: code]</strong>,
/// exported as <see cref="NationDefinition.BattleColorsHex"/>; every other pixel (the outline, the lime
/// dotted frame) is kept.
/// </summary>
/// <remarks>
/// <strong>[designed]</strong> fallback for a world without the field (a custom world): the nation's
/// <see cref="NationDefinition.ColorHex"/>, white, and <see cref="NationDefinition.GlyphColorHex"/>
/// (or, with none, the luminance foreground of <see cref="MarkerTint.FallbackForeground"/>). Searched
/// the research reports and found no rule for a nation without the dwords; bug #912 notes that
/// <c>GlyphColorHex</c> is the outline for Macedonia, Gaul and Illyria, which does not matter for the
/// classical world as it carries <c>battleColorsHex</c>.
/// </remarks>
public static class BattleIconTint
{
    /// <summary>The template's first region, purple <c>0x800080</c>.</summary>
    public static readonly (byte R, byte G, byte B) PurpleMarker = (0x80, 0x00, 0x80);

    /// <summary>The template's second region, white.</summary>
    public static readonly (byte R, byte G, byte B) WhiteMarker = (0xFF, 0xFF, 0xFF);

    /// <summary>The template's third region, blue <c>0x0000FF</c>.</summary>
    public static readonly (byte R, byte G, byte B) BlueMarker = (0x00, 0x00, 0xFF);

    /// <summary>
    /// The colours <paramref name="nationId"/>'s units are drawn with, or <see langword="null"/> when
    /// the nation is unknown or has no parseable colour at all.
    /// </summary>
    public static BattleIconColors? ForNation(World? world, string nationId)
    {
        ArgumentNullException.ThrowIfNull(nationId);

        var nation = world?.NationById(nationId);
        if (nation is null)
        {
            return null;
        }

        if (nation.BattleColorsHex is { Count: 3 } triple
            && Parse(triple[0]) is { } first
            && Parse(triple[1]) is { } second
            && Parse(triple[2]) is { } third)
        {
            return new BattleIconColors(first, second, third);
        }

        if (MarkerTint.TryParseHtml(nation.ColorHex) is not { } background)
        {
            return null;
        }

        var glyph = MarkerTint.TryParseHtml(nation.GlyphColorHex) ?? MarkerTint.FallbackForeground(background);
        return new BattleIconColors(ToBytes(background), WhiteMarker, ToBytes(glyph));
    }

    /// <summary>The replacement for one pixel: a marker colour maps to its nation colour, anything else is kept.</summary>
    public static (byte R, byte G, byte B) Map(BattleIconColors colors, byte r, byte g, byte b)
    {
        var pixel = (r, g, b);
        if (pixel == PurpleMarker)
        {
            return colors.First;
        }

        if (pixel == WhiteMarker)
        {
            return colors.Second;
        }

        return pixel == BlueMarker ? colors.Third : pixel;
    }

    /// <summary>
    /// Recolours a buffer of <paramref name="bytesPerPixel"/> bytes per pixel (3 or 4) in R, G, B[, A]
    /// order, in place; any alpha byte is left alone.
    /// </summary>
    public static void Recolour(BattleIconColors colors, Span<byte> pixels, int bytesPerPixel)
    {
        if (bytesPerPixel is not (3 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(bytesPerPixel), "3 (RGB) or 4 (RGBA) bytes per pixel.");
        }

        for (var i = 0; i + bytesPerPixel <= pixels.Length; i += bytesPerPixel)
        {
            var (r, g, b) = Map(colors, pixels[i], pixels[i + 1], pixels[i + 2]);
            pixels[i] = r;
            pixels[i + 1] = g;
            pixels[i + 2] = b;
        }
    }

    private static (byte R, byte G, byte B)? Parse(string? html) =>
        MarkerTint.TryParseHtml(html) is { } tint ? ToBytes(tint) : null;

    private static (byte R, byte G, byte B) ToBytes(MarkerTint tint) =>
        (Channel(tint.Red), Channel(tint.Green), Channel(tint.Blue));

    private static byte Channel(float value) => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);
}
