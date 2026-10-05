using Godot;

namespace IC2.Slice.UI;

/// <summary>
/// <c>docs/tasks/T100.md</c> Scope, "About": the about box behind Help → About Imperial Conquest —
/// the game's name, its version, and a line saying it is a re-creation of the 1996 game.
/// </summary>
/// <remarks>
/// The version is read from the Godot project's own <c>application/config/version</c> setting rather
/// than hardcoded, with <c>"development"</c> as the honest fallback when the setting is absent (as it
/// is today — <c>godot/project.godot</c> names no version, and this task does not own that file). A
/// hardcoded number here would go stale at the next release and duplicate a value that belongs to the
/// project metadata.
/// </remarks>
public partial class AboutDialog : Control
{
    /// <summary>Raised when the player dismisses the box.</summary>
    public event Action? Closed;

    /// <summary>The version line the box shows — exposed so a headless check asserts what is displayed
    /// rather than re-deriving the setting read.</summary>
    public string VersionText { get; private set; } = string.Empty;

    public override void _Ready()
    {
        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.65f), MouseFilter = MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.MakePanel(UiKit.PanelColorRaised);
        panel.CustomMinimumSize = new Vector2(460, 0);
        center.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 10);
        panel.AddChild(column);

        column.AddChild(UiKit.MakeLabel("Imperial Conquest 2", 26, UiKit.AccentColor));

        VersionText = $"Version {ProjectSettings.GetSetting("application/config/version", "development").AsString()}";
        column.AddChild(UiKit.MakeLabel(VersionText, 14, UiKit.MutedTextColor));

        var recreation = UiKit.MakeLabel("A re-creation of the 1996 strategy game.", 14, UiKit.TextColor);
        recreation.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(recreation);

        var close = UiKit.MakeButton("Close", Close);
        close.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        column.AddChild(close);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Close();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Dismisses the box — what the Close button does, exposed for the headless check.</summary>
    public void Close() => Closed?.Invoke();
}
