using IC2.Engine.Model;

namespace IC2.Slice.Assets;

/// <summary>
/// T94 rework round 1, N3 (the user's decision of 2026-09-29): the <em>owner tint</em> a map marker's
/// asset-pack icon is drawn with. With a texture, every nation's icon is the same bitmap, and only
/// the thin ring around it told owners apart; the pre-T94 shapes were filled in the owner's colour.
/// Tinting the texture with the owner's own colour restores that, and it is what makes a neutral
/// silhouette pack (T51) draw owner-coloured figures with no further code change. The placeholder
/// pack's deliberately flat, single-colour squares are deliberately <strong>not</strong> special-cased:
/// they are tinted as they are.
/// </summary>
/// <remarks>
/// Godot-free by construction, like <see cref="MapMarkerKeys"/> and <see cref="AssetKeyResolver"/>: it
/// is linked into <c>tests/IC2.Engine.Tests</c> so a test can assert the exact components a marker is
/// modulated with, rather than trusting a screenshot. The parser accepts Godot's documented HTML
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
    /// The tint an owned marker is drawn with: <paramref name="nationId"/>'s own
    /// <see cref="NationDefinition.ColorHex"/> from the loaded world. <see langword="null"/> when the
    /// nation is unknown or its colour is not a parseable HTML hex string, which callers render with
    /// their unknown-nation colour.
    /// </summary>
    public static MarkerTint? ForOwner(World? world, string nationId)
    {
        ArgumentNullException.ThrowIfNull(nationId);

        return TryParseHtml(world?.NationById(nationId)?.ColorHex);
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
