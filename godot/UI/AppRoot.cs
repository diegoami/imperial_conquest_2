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

    public void ShowLoadGame()
    {
        var screen = new LoadGameScreen();
        screen.BackRequested += ShowMainMenu;
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

        // Belt and suspenders alongside the anchors above: a screen added the very same frame this
        // root's own size last changed can otherwise sit one resize notification behind (observed while
        // capturing this task's own main-game-screen screenshot, where the swapped-in screen kept
        // reading (0,0) several frames later) -- an explicit, exact match is always correct here, never
        // just a fallback for a transient timing gap.
        screen.Size = Size;

        CurrentScreen = screen;
    }
}
