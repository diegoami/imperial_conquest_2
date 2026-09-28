using Godot;

namespace IC2.Slice.UI;

/// <summary>
/// The whole app's own screen host — <c>project.godot</c>'s <c>run/main_scene</c> as of this task.
/// Owns every screen swap itself (never <c>ChangeSceneToFile</c>) so an in-progress
/// <c>GameSession</c> is held in memory across menu round-trips (Settings, a dismissed Load) rather
/// than being destroyed by a full scene-tree replacement.
/// </summary>
public partial class AppRoot : Control
{
    public override void _Ready()
    {
        ShowMainMenu();
    }

    private void ShowMainMenu()
    {
        var menu = new MainMenu();
        menu.NewGameRequested += ShowNewGameFlow;
        menu.LoadRequested += ShowLoadGame;
        menu.SettingsRequested += ShowSettings;
        SwapTo(menu);
    }

    private void ShowNewGameFlow()
    {
        var flow = new NewGameFlow();
        flow.BackToMenuRequested += ShowMainMenu;
        flow.GameStarted += session =>
        {
            var screen = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
            SwapTo(screen);
        };
        SwapTo(flow);
    }

    private void ShowLoadGame()
    {
        var screen = new LoadGameScreen();
        screen.BackRequested += ShowMainMenu;
        SwapTo(screen);
    }

    private void ShowSettings()
    {
        var screen = new SettingsScreen();
        screen.BackRequested += ShowMainMenu;
        SwapTo(screen);
    }

    private void SwapTo(Control screen)
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        screen.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(screen);
    }
}
