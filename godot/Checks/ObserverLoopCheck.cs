using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class ObserverLoopCheck : Control
{
    private bool _ok = true;
    public override async void _Ready()
    {
        var preferences = SettingsScreen.Current;
        SetAnchorsPreset(LayoutPreset.FullRect);
        try
        {
            var unusual = new UserSettings { ObserverFollowMoves = false, ObserverSpeedPercent = 800,
                ObserverTacticalBattles = false }.WithWatchedNations("classical-mediterranean", new List<string>());
            SettingsScreen.Commit(unusual);
            var root = new AppRoot(); root.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(root);
            Check(root.CurrentScreen is MainMenu, "no launch option preserves the menu");
            root.StartObserverCampaign(true);
            var main = (MainGameScreen)root.CurrentScreen!;
            main.SetProcess(false);
            Check(main.Session.Scenario.Id == "classical-mediterranean" && main.Session.Ruleset.Id == "classical-faithful",
                "direct startup uses Classical Faithful");
            Check(main.Session.IsObserverMode && main.Session.State.Nations.All(n => n.Control == SeatControl.Ai), "no human seat");
            Check(main.ObserverPlayback.IsRunning && !main.ObserverPresentationPaused, "direct launch starts running");
            Check(main.ObserverLoopNation is not null && main.Session.State.Nations.Count(n => main.ObserverWatches(n.Id)) == 1,
                "loop watches exactly one live civilization even when remembered list is empty");
            Check(main.ObserverFollowChoice.ButtonPressed && main.ObserverFollowChoice.Disabled && main.ObserverSpeedChoice.GetSelectedId() == 50,
                "loop applies slow follow without using remembered fast settings");
            main.ObserverPause(); var state = main.Session.State;
            main._Process(20);
            Check(main.Session.State == state, "paused loop neither advances nor restarts");
            main.SetObserverLoop(false);
            Check(main.ObserverLoopNation is null && !main.ObserverFollowChoice.ButtonPressed && main.ObserverSpeedChoice.GetSelectedId() == 800,
                "leaving loop restores remembered presentation choices");
            Check(SettingsScreen.Current == unusual, "loop does not write user preferences");
            if (OS.GetCmdlineUserArgs().Contains("--capture"))
            {
                // Optional real-renderer QA; never called by the headless CI lane.
                main.SetObserverLoop(true); main.ObserverPlay();
                GetWindow().Mode = Window.ModeEnum.Windowed;
                GetWindow().ContentScaleFactor = 1;
                GetWindow().Size = new Vector2I(1920, 1080);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Size = GetViewportRect().Size;
                root.Size = Size;
                main.Size = Size;
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var folder = System.IO.Path.Combine(GameDataContext.RepositoryRoot, "artifacts", "observer", "v0.11");
                System.IO.Directory.CreateDirectory(folder);
                GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(folder, "loop-controls.png"));
                main.ObserverPause(); main.SetObserverLoop(false);
            }
            var seed = state.RandomSeed;
            root.StartObserverCampaign(false);
            main = (MainGameScreen)root.CurrentScreen!; main.SetProcess(false); main.ObserverPause();
            Check(main.Session.State.RandomSeed != seed && main.ObserverLoopNation is null, "fresh launch reseeds independently of loop choice");
            RemoveChild(root); root.QueueFree();

            var toy = GameDataContext.Repository.Resolve("toy-3city");
            var scenario = toy.Scenario with { TurnLimit = 0,
                Seats = ValueList.From(toy.Scenario.Seats.Select(s => s with { Control = SeatControl.Ai })) };
            main = new MainGameScreen { Session = new GameSession(toy.World, toy.Ruleset, scenario),
                RepositoryRoot = GameDataContext.RepositoryRoot, AutoStartObserver = true, StartObserverLoop = true };
            var restarts = 0; main.ObserverRestartRequested += () => restarts++;
            AddChild(main); main.SetProcess(false);
            main._Process(2);
            Check(restarts == 0, "completed campaign holds its ending before restart");
            main.ObserverPause(); main._Process(20);
            Check(restarts == 0, "pause freezes ending countdown");
            main.ObserverPlay(); main._Process(1.1);
            main._Process(20);
            Check(restarts == 1, "completed campaign requests exactly one restart after resuming");
            RemoveChild(main); main.QueueFree();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }
        catch (Exception ex) { Check(false, ex.ToString()); }
        finally { SettingsScreen.Commit(preferences); }
        GD.Print($"ObserverLoopCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private void Check(bool condition, string message)
    {
        if (condition) GD.Print("PASS: " + message);
        else { _ok = false; GD.PrintErr("FAIL: " + message); }
    }
}
