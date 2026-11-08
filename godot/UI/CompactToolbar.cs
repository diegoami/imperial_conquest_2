using Godot;

namespace IC2.Slice.UI;

/// <summary>24px illustrations in 32px pointer targets, with theme feedback preserved.</summary>
public static class CompactToolbar
{
    public static void Apply(Button button)
    {
        button.ExpandIcon = true;
        button.AddThemeConstantOverride("icon_max_width", 24);
        button.CustomMinimumSize = new Vector2(32, 32);
        button.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        foreach (var state in new[] { "normal", "hover", "pressed", "disabled", "hover_pressed" })
        {
            var style = (StyleBox)button.GetThemeStylebox(state).Duplicate();
            style.ContentMarginLeft = style.ContentMarginRight = 4;
            style.ContentMarginTop = style.ContentMarginBottom = 4;
            button.AddThemeStyleboxOverride(state, style);
        }
    }
}
