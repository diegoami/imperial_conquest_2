using Godot;

namespace IC2.Slice.UI;

/// <summary>Small vector symbols for remake conveniences; no original-game assets.</summary>
public static class QolCommandIcons
{
    private static readonly Dictionary<string, Texture2D> Cache = new();

    public static Texture2D? ForCommand(string command) => command switch
    {
        "unit_map.army_split_two" => Icon("split"),
        "unit_map.army_set_target" => Icon("travel"),
        _ => null,
    };

    public static void Apply(Button button, string icon, string caption)
    {
        button.Text = string.Empty;
        button.Icon = Icon(icon);
        button.CustomMinimumSize = new Vector2(34, 34);
        button.TooltipText = caption;
        button.AccessibilityName = caption;
        button.AccessibilityDescription = caption;
        ApplyTint(button);
    }

    internal static void ApplyTint(Button button)
    {
        button.AddThemeColorOverride("icon_normal_color", UiKit.TextColor);
        button.AddThemeColorOverride("icon_hover_color", UiKit.AccentColor);
        button.AddThemeColorOverride("icon_pressed_color", UiKit.AccentColor);
        button.AddThemeColorOverride("icon_hover_pressed_color", UiKit.AccentColor);
        button.AddThemeColorOverride("icon_focus_color", UiKit.TextColor);
        button.AddThemeColorOverride("icon_disabled_color", UiKit.MutedTextColor);
    }

    private static Texture2D Icon(string name)
    {
        if (Cache.TryGetValue(name, out var cached)) return cached;
        // Original 32-unit artwork rasterizes to the existing 24px toolbar size.
        // Shields represent whole formations; the recovery glyph combines a town,
        // supplies and a return arrow; rally variants share a standard and base.
        var artwork = name switch
        {
            "split" => "<circle cx='16' cy='4' r='2'/><path d='M16 6V10H7V13M16 10H25V13M4 11L7 14L10 11M22 11L25 14L28 11'/><path d='M2 17H12V23Q12 27 7 30Q2 27 2 23ZM20 17H30V23Q30 27 25 30Q20 27 20 23Z' fill='white' fill-opacity='.16'/>",
            "travel" => "<circle cx='5' cy='27' r='2'/><path d='M7 27H12V17H23V5M18 10L23 5L28 10'/>",
            "recover" => "<path d='M3 16C3 8 9 4 17 4M13 1L17 4L13 7M10 16L20 8L30 16M13 15V28H27V15'/><path d='M18 21H25V28H18ZM21.5 21V28M18 23H25' fill='white' fill-opacity='.16'/>",
            "rally" => "<ellipse cx='9' cy='28' rx='6' ry='2'/><path d='M9 27V2M10 4H28L24 10L28 16H10M15 5V15' fill='white' fill-opacity='.16'/>",
            "rally_clear" => "<path d='M8 29V2M9 4H27L23 10L27 16H9M18 21L28 30M28 21L18 30' fill='white' fill-opacity='.16'/>",
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
        var image = new Image();
        image.LoadSvgFromString($"<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 32 32'><g fill='none' stroke='white' stroke-width='2.1' stroke-linecap='round' stroke-linejoin='round'>{artwork}</g></svg>");
        return Cache[name] = ImageTexture.CreateFromImage(image);
    }
}
