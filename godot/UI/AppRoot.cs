using IC2.Slice.Diagnostics;
using System;
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

    /// <summary>Bug #919: what a confirmed (or unneeded-to-confirm) window close does. Defaults to
    /// quitting the tree; a headless check replaces it so it can observe the quit without ending its run.</summary>
    public Action? QuitAction { get; set; }

    public override void _Ready()
    {
        ObserverLaunchOptions launch;
        try { launch = GetTree().CurrentScene == this ? ObserverLaunchOptions.Parse(OS.GetCmdlineUserArgs()) : new(); }
        catch (ArgumentException ex) { GD.PushError(ex.Message); GetTree().Quit(2); return; }
        if (launch.RecordingDirectory is { } recording)
        {
            // A recording must never load/migrate/save the owner's preferences or original-asset choice.
            System.IO.Directory.CreateDirectory(recording);
            AppDiagnostics.DirectoryOverride = System.IO.Path.Combine(recording, "diagnostics");
            SettingsScreen.SettingsPath = null;
            SettingsScreen.Commit(new UserSettings { PackId = "authored", Fullscreen = false }, this);
        }
        // Bug #919: the window's close button must not quit an open game unasked; the close request
        // arrives as NotificationWMCloseRequest and HandleCloseRequest decides.
        AppDiagnostics.Acquire();
        GetTree().AutoAcceptQuit = false;

        // #1293: the player's remembered settings, only when this is the running game; a check that
        // hosts an AppRoot keeps the defaults and never writes the player's file.
        if (GetTree().CurrentScene == this && launch.RecordingDirectory is null)
        {
            // The rename to "Open Imperial Conquest" moved user://; bring the old folder's saves and settings.
            var userData = OS.GetUserDataDir();
            UserDataMigration.Run(UserDataMigration.FormerDirectoryBeside(userData), userData);
            SettingsScreen.LoadAndApply(ProjectSettings.GlobalizePath("user://settings.json"), this);
        }

        if (GetTree().CurrentScene == this)
        {
            try
            {
                if (launch.Observe) { StartObserverCampaign(launch.Loop, launch.ScenarioId, launch.Seed, launch.RecordingDirectory); return; }
            }
            catch (ArgumentException ex)
            {
                GD.PushError(ex.Message);
                GetTree().Quit(2);
                return;
            }
        }
        ShowMainMenu();
    }

    private ulong? _lastObserverSeed;

    public void StartObserverCampaign(bool loop, string? scenarioId = null, ulong? seedOverride = null, string? recordingDirectory = null)
    {
        var seed = seedOverride ?? (ulong)Random.Shared.NextInt64();
        if (seedOverride is null && seed == _lastObserverSeed) seed++;
        _lastObserverSeed = seed;
        var selection = new NewGameSelection { ObserverMode = true };
        if (scenarioId is not null)
            selection.SelectedCard = RulesetPresets.CardForScenario(GameDataContext.Repository.Resolve(scenarioId));
        var session = GameSessionFactory.CreateSession(GameDataContext.Repository, selection, seed);
        var screen = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot,
            AutoStartObserver = true, StartObserverLoop = loop, ObserverRecordingDirectory = recordingDirectory,
            ObserverViewpointSeed = recordingDirectory is null ? null : unchecked((int)seed ^ (int)(seed >> 32)) };
        WireObserverRestart(screen);
        SwapTo(screen);
        GD.Print($"Observer launch: {session.Scenario.Id}, loop={loop}, seed={seed}.");
        if (recordingDirectory is not null)
            GD.Print("OBSERVER_CAMPAIGN " + System.Text.Json.JsonSerializer.Serialize(new {
                scenarioId = session.Scenario.Id, scenarioName = session.Scenario.Name, seed = seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                frame = Godot.Engine.GetProcessFrames(), pack = SettingsScreen.SelectedPackId }));
    }

    private void WireObserverRestart(MainGameScreen screen) => screen.ObserverRestartRequested +=
        () => Callable.From(() => StartObserverCampaign(true, screen.Session.Scenario.Id,
            recordingDirectory: screen.ObserverRecordingDirectory)).CallDeferred();

    public override void _Notification(int what)
    {
        if (what == NotificationApplicationFocusOut) AppDiagnostics.Mark(DiagnosticEvent.Focus, "lost");
        if (what == NotificationApplicationFocusIn) AppDiagnostics.Mark(DiagnosticEvent.Focus, "gained");
        if (what == NotificationWMCloseRequest)
        {
            HandleCloseRequest();
        }
    }

    /// <summary>Bug #919: with a game open, asks the same leave-game question File -> Close asks and quits
    /// only on Yes; on any other screen nothing is left to lose, so it quits at once.</summary>
    public void HandleCloseRequest()
    {
        if (CurrentScreen is LoadGameScreen load) load.Cancel();
        if (CurrentScreen is MainGameScreen game)
        {
            game.ConfirmQuit(Quit);
        }
        else
        {
            Quit();
        }
    }

    public override void _ExitTree() => AppDiagnostics.Release();

    private void Quit()
    {
        AppDiagnostics.Mark(DiagnosticEvent.Quit, "app");
        if (QuitAction is { } action)
        {
            action();
        }
        else
        {
            GetTree().Quit();
        }
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
            var screen = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot,
                StartAtCapital = true };
            WireObserverRestart(screen);
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
        if (CurrentScreen is LoadGameScreen) return;
        var previous = CurrentScreen;
        var previousMode = previous?.ProcessMode ?? ProcessModeEnum.Inherit;
        if (previous is not null) previous.ProcessMode = ProcessModeEnum.Disabled;
        var screen = new LoadGameScreen();
        screen.BackRequested += () =>
        {
            if (previous is not null) previous.ProcessMode = previousMode;
            CurrentScreen = previous;
            RemoveChild(screen);
            screen.QueueFree();
        };
        screen.GameResumed += session =>
        {
            var mainScreen = new MainGameScreen
            {
                Session = session,
                RepositoryRoot = GameDataContext.RepositoryRoot,
                OpenedPath = screen.LastLoadedPath,
            };
            WireObserverRestart(mainScreen);
            SwapTo(mainScreen);
        };
        // Keep the caller alive while choosing a file; Cancel restores that exact screen/session.
        screen.SetAnchorsPreset(LayoutPreset.FullRect);
        CurrentScreen = screen;
        AddChild(screen);
    }

    public void ShowSettings()
    {
        var screen = new SettingsScreen();
        screen.BackRequested += ShowMainMenu;
        SwapTo(screen);
    }

    private void SwapTo(Control screen)
    {
        AppDiagnostics.Detail(DiagnosticEvent.Screen, screen.GetType().Name);
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
