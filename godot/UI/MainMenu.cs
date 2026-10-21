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
        column.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        center.AddChild(column);

        var title = UiKit.MakeLabel("Open Imperial Conquest", AppTheme.Title, UiKit.AccentColor);
        title.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(title);

        var subtitle = UiKit.MakeLabel("A reimplementation of Imperial Conquest 2, the 1996 strategy game", AppTheme.Body, UiKit.MutedTextColor);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        column.AddChild(subtitle);

        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 20) });

        column.AddChild(UiKit.MakeButton("New Game", OnNewGamePressed, AppTheme.Button));
        column.AddChild(UiKit.MakeButton("Load", OnLoadPressed, AppTheme.Button));
        column.AddChild(UiKit.MakeButton("How to play", OnHowToPlayPressed, AppTheme.Button));
        column.AddChild(UiKit.MakeButton("Settings", OnSettingsPressed, AppTheme.Button));
        column.AddChild(UiKit.MakeButton("Quit", OnQuitPressed, AppTheme.Button));
    }

    private void OnNewGamePressed() => NewGameRequested?.Invoke();

    private void OnLoadPressed() => LoadRequested?.Invoke();

    private void OnSettingsPressed() => SettingsRequested?.Invoke();

    /// <summary>#1289: the Help page, "How to play" first, over the menu (no game yet, so no ruleset).</summary>
    public void OnHowToPlayPressed()
    {
        var page = new HelpPage();
        page.Closed += () =>
        {
            RemoveChild(page);
            page.QueueFree();
        };
        page.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(page);
    }

    private void OnQuitPressed() => GetTree().Quit();
}
