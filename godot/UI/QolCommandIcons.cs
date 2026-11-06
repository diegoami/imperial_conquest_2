using Godot;
using IC2.Slice.Assets;

namespace IC2.Slice.UI;

/// <summary>Authored illustrated actions with legacy fallbacks; no original-game assets.</summary>
public static class QolCommandIcons
{
    private static readonly Dictionary<string, Texture2D> Cache = new();
    private static AssetPackTextureLoader? _illustrations;
    private static bool _triedIllustrations;
    public static IReadOnlyList<string> IllustratedNames { get; } = new[] {
        "play", "pause", "skip", "battle_reports", "next_round", "watch", "take_control", "undo",
        "split", "fleet_split", "recover", "rally", "fleet_rally", "rally_clear", "fleet_rally_clear" };

    public static bool IsIllustrated(Texture2D? texture) =>
        texture?.ResourceName.StartsWith("ui.action.", StringComparison.Ordinal) == true;

    public static Texture2D? ForCommand(string command) => command switch
    {
        "unit_map.army_split_two" => Icon("split"),
        "unit_map.fleet_split_two" => Icon("fleet_split"),
        _ => null,
    };

    public static void Apply(Button button, string icon, string caption)
    {
        var texture = Icon(icon);
        if (button.Icon == texture && button.TooltipText == caption && button.AccessibilityName == caption
            && button.CustomMinimumSize == new Vector2(34, 34)) return;
        button.Icon = texture;
        button.Text = button.Icon is null ? caption : string.Empty;
        button.CustomMinimumSize = new Vector2(34, 34);
        button.TooltipText = caption;
        button.AccessibilityName = caption;
        button.AccessibilityDescription = caption;
        ApplyTint(button);
    }

    internal static void ApplyTint(Button button)
    {
        var illustrated = IsIllustrated(button.Icon);
        button.AddThemeColorOverride("icon_normal_color", illustrated ? Colors.White : UiKit.TextColor);
        button.AddThemeColorOverride("icon_hover_color", illustrated ? Colors.White : UiKit.AccentColor);
        button.AddThemeColorOverride("icon_pressed_color", illustrated ? Colors.White : UiKit.AccentColor);
        button.AddThemeColorOverride("icon_hover_pressed_color", illustrated ? Colors.White : UiKit.AccentColor);
        button.AddThemeColorOverride("icon_focus_color", illustrated ? Colors.White : UiKit.TextColor);
        button.AddThemeColorOverride("icon_disabled_color", illustrated ? new Color(1, 1, 1, 0.35f) : UiKit.MutedTextColor);
    }

    private static Texture2D? Icon(string name)
    {
        if (Cache.TryGetValue(name, out var cached)) return cached;
        if (!_triedIllustrations)
        {
            _triedIllustrations = true;
            _illustrations = AssetPackTextureLoader.TryLoadPack(GameDataContext.RepositoryRoot,
                AssetPackManifestLoader.DefaultPackId, message => GD.PushWarning("Action icons: " + message));
        }
        if (IllustratedNames.Contains(name) && _illustrations?.TryGetTexture("ui.action." + name) is { } source)
        {
            var original = source.GetImage();
            var pixels = Image.CreateFromData(original.GetWidth(), original.GetHeight(), original.HasMipmaps(),
                original.GetFormat(), original.GetData());
            pixels.Resize(24, 24, Image.Interpolation.Lanczos);
            var colored = ImageTexture.CreateFromImage(pixels);
            colored.ResourceName = "ui.action." + name;
            return Cache[name] = colored;
        }
        // Original 32-unit artwork rasterizes to the existing 24px toolbar size.
        // Shields represent whole formations; the recovery glyph combines a town,
        // supplies and a return arrow; rally variants share a standard and base.
        var artwork = name switch
        {
            "undo" => "<path d='M12 5L4 12L12 19M5 12H20C31 12 31 28 19 28H13'/>",
            "split" or "fleet_split" => "<circle cx='16' cy='4' r='2'/><path d='M16 6V10H7V13M16 10H25V13M4 11L7 14L10 11M22 11L25 14L28 11'/><path d='M2 17H12V23Q12 27 7 30Q2 27 2 23ZM20 17H30V23Q30 27 25 30Q20 27 20 23Z' fill='white' fill-opacity='.16'/>",
            "travel" => "<circle cx='5' cy='27' r='2'/><path d='M7 27H12V17H23V5M18 10L23 5L28 10'/>",
            "recover" => "<path d='M3 16C3 8 9 4 17 4M13 1L17 4L13 7M10 16L20 8L30 16M13 15V28H27V15'/><path d='M18 21H25V28H18ZM21.5 21V28M18 23H25' fill='white' fill-opacity='.16'/>",
            "rally" => "<ellipse cx='9' cy='28' rx='6' ry='2'/><path d='M9 27V2M10 4H28L24 10L28 16H10M15 5V15' fill='white' fill-opacity='.16'/>",
            "fleet_rally" => "<path d='M16 2V24M17 4H29L26 9L29 14H17M8 18H24L27 24H5ZM2 28Q6 24 10 28Q14 32 18 28Q22 24 30 28'/>",
            "rally_clear" or "fleet_rally_clear" => "<path d='M8 29V2M9 4H27L23 10L27 16H9M18 21L28 30M28 21L18 30' fill='white' fill-opacity='.16'/>",
            _ => null,
        };
        if (artwork is null) return null;
        var image = new Image();
        image.LoadSvgFromString($"<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 32 32'><g fill='none' stroke='white' stroke-width='2.1' stroke-linecap='round' stroke-linejoin='round'>{artwork}</g></svg>");
        return Cache[name] = ImageTexture.CreateFromImage(image);
    }
}
