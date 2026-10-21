using System.Collections.Generic;
using Godot;
using IC2.Slice.Diagnostics;

namespace IC2.Slice.UI;

/// <summary>
/// #1293: applies <see cref="UserSettings"/> to the running app — the colour scheme, the body font, the
/// interface size, full screen and the master volume — live, on whatever screen is open.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Colours are remapped by role.</strong> Every colour the interface draws comes from
/// <c>AppTheme.tres</c>'s six roles (<see cref="ColorScheme"/>), either through the theme or copied into
/// a control's override by <see cref="UiKit"/> when the control was built. A scheme change therefore
/// walks the theme and the live scene tree and replaces any colour equal (to the 8-bit channel) to the
/// previous scheme's role with the new scheme's, keeping its alpha. Colours that are no role (the
/// nations' own, the map, the scrim) are left alone.
/// </para>
/// <para>
/// <strong>Fonts</strong> swap the base font of the theme's shared <c>Body</c> variation, so every label,
/// button and tooltip in the body face follows; the Cinzel display face is not changed.
/// </para>
/// </remarks>
public static class Appearance
{
    private static readonly string[] ColorOverrideNames =
    {
        "font_color", "font_hover_color", "font_pressed_color", "font_focus_color", "font_disabled_color",
        "font_hover_pressed_color", "font_selected_color", "font_uneditable_color", "font_outline_color", "default_color",
    };

    private static readonly string[] StyleboxOverrideNames = { "panel", "normal", "hover", "pressed", "focus", "disabled", "hover_pressed" };

    /// <summary>The scheme the interface is drawn in now; starts as <c>AppTheme.tres</c>'s own.</summary>
    public static ColorScheme AppliedColors { get; private set; } = ColorSchemes.ById(ColorSchemes.DefaultId);

    /// <summary>The settings last applied, or the defaults.</summary>
    public static UserSettings Applied { get; private set; } = new();

    /// <summary>Applies every display setting to the theme and to <paramref name="root"/>'s tree.</summary>
    public static void Apply(UserSettings settings, Node root)
    {
        settings = settings.Normalized();
        ApplyColors(settings.ResolvedColors(), root);
        ApplyFont(settings.Font);
        if (root.GetTree()?.Root is { } window)
        {
            window.ContentScaleFactor = settings.TextScalePercent / 100f;
        }

        ApplyWindowMode(settings.Fullscreen);
        ApplyVolume(settings.SoundEnabled ? settings.VolumePercent : 0);
        Applied = settings;
        AppDiagnostics.Detail(DiagnosticEvent.Settings, nameof(SettingsScreen));
    }

    /// <summary>Remaps the theme and the live tree from <see cref="AppliedColors"/> to <paramref name="colors"/>.</summary>
    public static void ApplyColors(ColorScheme colors, Node root)
    {
        var map = RoleMap(AppliedColors, colors);
        if (map.Count == 0)
        {
            return;
        }

        var seen = new HashSet<ulong>();
        var theme = AppTheme.Current;
        foreach (var type in theme.GetColorTypeList())
        {
            foreach (var name in theme.GetColorList(type))
            {
                theme.SetColor(name, type, Remap(map, theme.GetColor(name, type)));
            }
        }

        foreach (var type in theme.GetStyleboxTypeList())
        {
            foreach (var name in theme.GetStyleboxList(type))
            {
                RemapStylebox(map, theme.GetStylebox(name, type), seen);
            }
        }

        RetintTree(map, root, seen);
        AppliedColors = colors;
        root.PropagateNotification((int)Control.NotificationThemeChanged);
    }

    private static void RetintTree(Dictionary<Color, Color> map, Node node, HashSet<ulong> seen)
    {
        if (node is Control control)
        {
            foreach (var name in ColorOverrideNames)
            {
                if (control.HasThemeColorOverride(name))
                {
                    control.AddThemeColorOverride(name, Remap(map, control.GetThemeColor(name)));
                }
            }

            foreach (var name in StyleboxOverrideNames)
            {
                if (control.HasThemeStyleboxOverride(name))
                {
                    RemapStylebox(map, control.GetThemeStylebox(name), seen);
                }
            }

            if (control is ColorRect rect)
            {
                rect.Color = Remap(map, rect.Color);
            }

            control.QueueRedraw();
        }

        foreach (var child in node.GetChildren(includeInternal: true))
        {
            RetintTree(map, child, seen);
        }
    }

    private static void RemapStylebox(Dictionary<Color, Color> map, StyleBox? box, HashSet<ulong> seen)
    {
        if (box is StyleBoxFlat flat && seen.Add(flat.GetInstanceId()))
        {
            flat.BgColor = Remap(map, flat.BgColor);
            flat.BorderColor = Remap(map, flat.BorderColor);
        }
    }

    /// <summary>
    /// From-colour (opaque, 8-bit) to to-colour per role whose value changes. When two roles share a
    /// from-colour the first in role order wins.
    /// </summary>
    private static Dictionary<Color, Color> RoleMap(ColorScheme from, ColorScheme to)
    {
        var map = new Dictionary<Color, Color>();
        void Add(string a, string b)
        {
            var key = Quantize(Color.FromHtml(a));
            var value = Color.FromHtml(b);
            if (key != Quantize(value)) map.TryAdd(key, value);
        }

        Add(from.Background, to.Background);
        Add(from.Panel, to.Panel);
        Add(from.Raised, to.Raised);
        Add(from.Text, to.Text);
        Add(from.Muted, to.Muted);
        Add(from.Accent, to.Accent);
        return map;
    }

    private static Color Remap(Dictionary<Color, Color> map, Color color) =>
        map.TryGetValue(Quantize(color), out var to) ? new Color(to.R, to.G, to.B, color.A) : color;

    private static Color Quantize(Color c) => Color.Color8((byte)Mathf.RoundToInt(c.R * 255), (byte)Mathf.RoundToInt(c.G * 255), (byte)Mathf.RoundToInt(c.B * 255));

    private static void ApplyFont(string fontId)
    {
        if (AppTheme.Current.DefaultFont is not FontVariation body)
        {
            return;
        }

        var path = UiFonts.ById(fontId).ResourcePath;
        if (body.BaseFont?.ResourcePath != path && ResourceLoader.Exists(path))
        {
            body.BaseFont = GD.Load<Font>(path);
        }
    }

    private static void ApplyWindowMode(bool fullscreen)
    {
        if (DisplayServer.GetName() == "headless")
        {
            return;
        }

        var wanted = fullscreen ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed;
        var current = DisplayServer.WindowGetMode();
        var isFull = current is DisplayServer.WindowMode.Fullscreen or DisplayServer.WindowMode.ExclusiveFullscreen;
        if (isFull != fullscreen)
        {
            DisplayServer.WindowSetMode(wanted);
        }
    }

    private static void ApplyVolume(int percent)
    {
        AudioServer.SetBusMute(0, percent <= 0);
        AudioServer.SetBusVolumeDb(0, percent <= 0 ? -80f : Mathf.LinearToDb(percent / 100f));
    }
}
