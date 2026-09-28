using Godot;

namespace IC2.Slice.UI;

/// <summary>
/// <c>docs/tasks/T24.md</c> Scope, <c>docs/game-design.md</c> §"User interface" item 1: "Main menu →
/// New Game / Load / Settings (asset-pack selection lives here) / Quit." The whole app's entry point —
/// <c>project.godot</c>'s own <c>run/main_scene</c> as of this task (T47's <c>Slice.tscn</c> stays
/// reachable by its own explicit path, <c>res://Slice/Slice.tscn</c>, for its own tests and any later
/// task that still wants the walking skeleton directly).
/// </summary>
public partial class MainMenu : Control
{
    /// <summary>Raised by each button — <see cref="AppRoot"/> owns the actual screen swap (this class
    /// never calls <c>ChangeSceneToFile</c> itself, so an in-progress <c>GameSession</c> is never at
    /// risk of being silently discarded by a full scene-tree replacement).</summary>
    public event Action? NewGameRequested;

    public event Action? LoadRequested;

    public event Action? SettingsRequested;

    public override void _Ready()
    {
        UiKit.ApplyBackground(this, UiKit.Background);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var column = new VBoxContainer { CustomMinimumSize = new Vector2(420, 0) };
        column.AddThemeConstantOverride("separation", 18);
        center.AddChild(column);

        var title = UiKit.MakeLabel("Imperial Conquest 2", 34, UiKit.AccentColor);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(title);

        var subtitle = UiKit.MakeLabel("A reimplementation of the 1999 strategy game", 14, UiKit.MutedTextColor);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(subtitle);

        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

        column.AddChild(UiKit.MakeButton("New Game", OnNewGamePressed, 20));
        column.AddChild(UiKit.MakeButton("Load", OnLoadPressed, 20));
        column.AddChild(UiKit.MakeButton("Settings", OnSettingsPressed, 20));
        column.AddChild(UiKit.MakeButton("Quit", OnQuitPressed, 20));
    }

    private void OnNewGamePressed() => NewGameRequested?.Invoke();

    private void OnLoadPressed() => LoadRequested?.Invoke();

    private void OnSettingsPressed() => SettingsRequested?.Invoke();

    private void OnQuitPressed() => GetTree().Quit();
}
