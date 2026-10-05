using IC2.Engine.Model;

namespace IC2.Slice.Assets;

/// <summary>
/// The two colours one owner's map marker is drawn with (T97, the correction from the user's visual
/// review of 2026-09-29): the <em>background</em> square filled with
/// <see cref="NationDefinition.ColorHex"/>, and the <em>foreground</em> glyph tinted with
/// <see cref="NationDefinition.GlyphColorHex"/> — the original's own scheme, one (background,
/// foreground) pair per nation (<c>2026-09-29-nation-marker-colours.md</c>).
/// <see cref="ForOwner"/> returns the pair for a loaded world's nation, falling back to
/// <see cref="FallbackForeground"/> for a nation that carries no foreground.
/// </summary>
/// <remarks>
/// Godot-free by construction, like <see cref="MapMarkerKeys"/> and <see cref="AssetKeyResolver"/>: it
/// is linked into <c>tests/IC2.Engine.Tests</c> so a test can assert the exact colours a marker is
/// drawn with, rather than trusting a screenshot. The parser accepts Godot's documented HTML
/// subset (3/4/6/8 hex digits, optional <c>#</c>, one-digit channels doubled) and normalises with the
/// same <c>byte / 255</c> arithmetic Godot's own <c>Color.html</c>/<c>Color(code)</c> uses — see the
/// Godot 4 <c>Color</c> class reference's <c>html()</c> example
/// (<c>html("663399cc") == Color(0.4, 0.2, 0.6, 0.8)</c>). Named colours and 5/7-digit strings are
/// not accepted; every <c>data/worlds/*.json</c> nation ships an <c>#RRGGBB</c> <c>colorHex</c>, and
/// anything else degrades to the caller's unknown-nation colour exactly as the old Godot-side parse
/// did.
/// </remarks>
public readonly record struct MarkerTint(float Red, float Green, float Blue, float Alpha)
{
    /// <summary>
    /// The (background, foreground) pair an owned marker is drawn with: <paramref name="nationId"/>'s
    /// own <see cref="NationDefinition.ColorHex"/> and <see cref="NationDefinition.GlyphColorHex"/>
    /// from the loaded world. <see langword="null"/> when the nation is unknown or its background
    /// colour is not a parseable HTML hex string, which callers render with their unknown-nation
    /// colour. A nation with no foreground (every pre-T97 construction) falls back to
    /// <see cref="FallbackForeground"/>.
    /// </summary>
    public static MarkerColors? ForOwner(World? world, string nationId)
    {
        ArgumentNullException.ThrowIfNull(nationId);

        var nation = world?.NationById(nationId);
        if (TryParseHtml(nation?.ColorHex) is not { } background)
        {
            return null;
        }

        return new MarkerColors(
            background,
            TryParseHtml(nation?.GlyphColorHex) ?? FallbackForeground(background));
    }

    /// <summary>
    /// The glyph colour for a background the world gives no foreground for: white on a dark
    /// background, black on a light one, chosen by the background's WCAG relative luminance
    /// (<c>0.2126 R + 0.7152 G + 0.0722 B</c>, threshold 0.5).
    /// </summary>
    /// <remarks>
    /// [designed] presentation fallback: the report (<c>2026-09-29-nation-marker-colours.md</c>)
    /// records a foreground for all 16 nations and no rule for one that lacks it, and the DAT carries
    /// no such field either. Searched the report, <c>docs/investigations/</c> and the terrain/marker
    /// reports and found no fallback rule, so this is the conventional luminance choice, pinned by
    /// <c>MarkerTintTests</c>.
    /// </remarks>
    public static MarkerTint FallbackForeground(MarkerTint background)
    {
        var luminance = (0.2126f * background.Red) + (0.7152f * background.Green) + (0.0722f * background.Blue);
        return luminance >= 0.5f
            ? new MarkerTint(0f, 0f, 0f, 1f)
            : new MarkerTint(1f, 1f, 1f, 1f);
    }

    /// <summary>
    /// Parses an HTML hex colour (<c>#RGB</c>, <c>#RGBA</c>, <c>#RRGGBB</c>, <c>#RRGGBBAA</c>, the
    /// <c>#</c> optional and the digits case-insensitive) into components in the 0..1 range, or
    /// <see langword="null"/> when the string is not one of those shapes. Alpha defaults to 1.
    /// </summary>
    public static MarkerTint? TryParseHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var text = html.AsSpan().Trim();
        if (!text.IsEmpty && text[0] == '#')
        {
            text = text[1..];
        }

        // A one-digit channel is doubled (0xF -> 0xFF): Godot's own HTML expansion.
        var digits = text.Length switch
        {
            3 or 4 => 1,
            6 or 8 => 2,
            _ => 0,
        };
        if (digits == 0)
        {
            return null;
        }

        if (!TryParseChannel(text, 0, digits, out var red)
            || !TryParseChannel(text, digits, digits, out var green)
            || !TryParseChannel(text, digits * 2, digits, out var blue))
        {
            return null;
        }

        var alpha = 1f;
        if (text.Length > digits * 3 && !TryParseChannel(text, digits * 3, digits, out alpha))
        {
            return null;
        }

        return new MarkerTint(red, green, blue, alpha);
    }

    private static bool TryParseChannel(ReadOnlySpan<char> text, int index, int digits, out float value)
    {
        var channel = 0;
        for (var i = 0; i < digits; i++)
        {
            var digit = HexDigit(text[index + i]);
            if (digit < 0)
            {
                value = 0f;
                return false;
            }

            channel = (channel * 16) + digit;
        }

        value = digits == 1 ? channel * 17 / 255f : channel / 255f;
        return true;
    }

    private static int HexDigit(char character) => character switch
    {
        >= '0' and <= '9' => character - '0',
        >= 'a' and <= 'f' => character - 'a' + 10,
        >= 'A' and <= 'F' => character - 'A' + 10,
        _ => -1,
    };
}

/// <summary>
/// One nation's marker colours (T97): the <paramref name="Background"/> the marker's square is
/// filled with and the <paramref name="Foreground"/> its glyph is tinted with. Both are the world's
/// own parsed hex values, or a luminance-chosen fallback for a nation with no foreground.
/// </summary>
public readonly record struct MarkerColors(MarkerTint Background, MarkerTint Foreground);
