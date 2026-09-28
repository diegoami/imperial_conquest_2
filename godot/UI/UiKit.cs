using Godot;

namespace IC2.Slice.UI;

/// <summary>
/// Small, shared control-building helpers every T24 screen uses, so the same look (colours, padding,
/// font sizes) does not get re-invented, slightly differently, on each screen. <strong>[designed]</strong>:
/// no report or design document specifies exact colours for a Godot UI (the decompiled game's own forms
/// are not a source for a from-scratch Control tree), so this is this task's own presentation choice,
/// picked for contrast and legibility against <see cref="Background"/> — the same background colour
/// <c>Slice.cs</c> and <c>MapViewer.cs</c> already use, so the new screens do not look like a different
/// application from the map they lead into.
/// </summary>
public static class UiKit
{
    public static readonly Color Background = new(0.07f, 0.11f, 0.15f);
    public static readonly Color PanelColor = new(0.11f, 0.16f, 0.22f);
    public static readonly Color PanelColorRaised = new(0.15f, 0.21f, 0.28f);
    public static readonly Color AccentColor = new(0.86f, 0.66f, 0.27f);
    public static readonly Color TextColor = new(0.93f, 0.94f, 0.92f);
    public static readonly Color MutedTextColor = new(0.68f, 0.71f, 0.74f);
    public static readonly Color SelectedBorderColor = new(0.95f, 0.78f, 0.35f);

    public static PanelContainer MakePanel(Color color, float cornerRadius = 8f)
    {
        var panel = new PanelContainer();
        var style = new StyleBoxFlat
        {
            BgColor = color,
            CornerRadiusTopLeft = (int)cornerRadius,
            CornerRadiusTopRight = (int)cornerRadius,
            CornerRadiusBottomLeft = (int)cornerRadius,
            CornerRadiusBottomRight = (int)cornerRadius,
            ContentMarginLeft = 12,
            ContentMarginRight = 12,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
        };
        panel.AddThemeStyleboxOverride("panel", style);
        return panel;
    }

    public static Label MakeLabel(string text, int fontSize = 16, Color? color = null)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", color ?? TextColor);
        return label;
    }

    public static Button MakeButton(string text, Action onPressed, int fontSize = 16)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 40) };
        button.AddThemeFontSizeOverride("font_size", fontSize);
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
