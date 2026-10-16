using Godot;

namespace IC2.Slice.UI;

/// <summary>Shared control builders reading the project-wide T169 theme.</summary>
public static class UiKit
{
    public static Color Background => AppTheme.Color("background");
    public static Color PanelColor => AppTheme.Color("panel");
    public static Color PanelColorRaised => AppTheme.Color("raised");
    public static Color AccentColor => AppTheme.Color("accent");
    public static Color TextColor => AppTheme.Color("text");
    public static Color MutedTextColor => AppTheme.Color("muted");
    public static Color SelectedBorderColor => AppTheme.Color("accent");

    public static PanelContainer MakePanel(Color color, float cornerRadius = -1f)
    {
        var panel = new PanelContainer();
        if (cornerRadius < 0) cornerRadius = AppTheme.CornerRadius;
        var style = new StyleBoxFlat
        {
            BgColor = color,
            CornerRadiusTopLeft = (int)cornerRadius,
            CornerRadiusTopRight = (int)cornerRadius,
            CornerRadiusBottomLeft = (int)cornerRadius,
            CornerRadiusBottomRight = (int)cornerRadius,
            ContentMarginLeft = AppTheme.PanelMarginSide,
            ContentMarginRight = AppTheme.PanelMarginSide,
            ContentMarginTop = AppTheme.PanelMarginVertical,
            ContentMarginBottom = AppTheme.PanelMarginVertical,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    public static Label MakeLabel(string text, int fontSize = 0, Color? color = null)
    {
        var label = new Label { Text = text };
        if (fontSize == 0) fontSize = AppTheme.Body;
        label.AddThemeFontOverride("font", fontSize == AppTheme.Header || fontSize == AppTheme.Title ? AppTheme.DisplayFont : AppTheme.BodyFont);
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color ?? TextColor);
        return label;
    }

    public static Button MakeButton(string text, Action onPressed, int fontSize = 0)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, AppTheme.ButtonHeight) };
        button.AddThemeFontSizeOverride("font_size", fontSize == 0 ? AppTheme.Button : fontSize);
        button.Pressed += onPressed;
        return button;
    }

    /// <summary>Fills a <see cref="Control"/> with a solid background colour via a full-rect <see cref="ColorRect"/>.</summary>
    public static void ApplyBackground(Control root, Color color)
    {
        var rect = new ColorRect { Color = color, MouseFilter = Control.MouseFilterEnum.Ignore };
        rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(rect);
        root.MoveChild(rect, 0);
    }
}
