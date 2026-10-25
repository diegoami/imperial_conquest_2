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
        button.AddThemeColorOverride("icon_normal_color", UiKit.TextColor);
        button.AddThemeColorOverride("icon_hover_color", UiKit.AccentColor);
        button.AddThemeColorOverride("icon_pressed_color", UiKit.AccentColor);
    }

    private static Texture2D Icon(string name)
    {
        if (Cache.TryGetValue(name, out var cached)) return cached;
        var path = name switch
        {
            "split" => "M12 21V12M12 12L5 5M12 12L19 5M3 10V3H10M14 3H21V10",
            "travel" => "M3 20H8V13H16V5M12 9L16 5L20 9M3 17V23",
            "recover" => "M3 11L12 3L21 11M6 10V21H18V10M10 21V15H14V21M2 3V8H7M2 8L6 4",
            "rally" => "M6 22V3M6 3H20L16 8L20 13H6",
            "rally_clear" => "M5 22V3M5 3H20L16 8L20 13H5M13 17L20 23M20 17L13 23",
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
        var image = new Image();
        image.LoadSvgFromString($"<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24'><path d='{path}' fill='none' stroke='white' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'/></svg>");
        return Cache[name] = ImageTexture.CreateFromImage(image);
    }
}
