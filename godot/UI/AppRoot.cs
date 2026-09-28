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
    /// <summary>The screen currently on display — exposed (rather than kept private) so
    /// <c>godot/Checks/ScreenshotTour.cs</c> (this task's own DoD/visual-sign-off driver) can navigate
    /// and inspect the real scene tree without simulating mouse events.</summary>
    public Control? CurrentScreen { get; private set; }

    public override void _Ready()
    {
        ShowMainMenu();
    }

    public void ShowMainMenu()
    {
        var menu = new MainMenu();
        menu.NewGameRequested += ShowNewGameFlow;
        menu.LoadRequested += ShowLoadGame;
        menu.SettingsRequested += ShowSettings;
        SwapTo(menu);
    }

    public void ShowNewGameFlow()
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

    /// <summary>
    /// <c>docs/tasks/T95.md</c> (#467), Owns addendum 2026-09-28: routes <see cref="LoadGameScreen.GameResumed"/>
    /// to the main game screen — the "only" grant this task adds for this file — mirroring
    /// <see cref="ShowNewGameFlow"/>'s own <c>GameStarted</c> handler exactly, so a resumed session
    /// reaches <see cref="MainGameScreen"/> the same way a newly started one already does, and
    /// <see cref="CurrentScreen"/> stays accurate through <see cref="SwapTo"/> either way.
    /// </summary>
    public void ShowLoadGame()
    {
        var screen = new LoadGameScreen();
        screen.BackRequested += ShowMainMenu;
        screen.GameResumed += session =>
        {
            var mainScreen = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
            SwapTo(mainScreen);
        };
        SwapTo(screen);
    }

    public void ShowSettings()
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
        CurrentScreen = screen;
    }
}
