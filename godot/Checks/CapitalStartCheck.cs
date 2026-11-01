using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class CapitalStartCheck : Node
{
    private bool _ok = true;
    public override async void _Ready()
    {
        try
        {
            var app = new AppRoot { Size = GetViewport().GetVisibleRect().Size };
            AddChild(app);
            app.ShowNewGameFlow();
            var flow = (NewGameFlow)app.CurrentScreen!;
            flow.Chooser!.ConfirmSelection();
            flow.SeatScreen!.ConfirmSeatAndStart();
            var main = (MainGameScreen)app.CurrentScreen!;
            var before = main.Session.State;
            var nation = before.NationById(before.ActiveNationId)!;
            var capital = before.CityById(nation.CapitalCityId!)!;
            await Frames(6);
            Check(main.MapView.ZoomFactor >= 6, "actual New Game opens closer than the former 4x view");
            var centre = main.MapView.TileCenterForCheck(capital.X, capital.Y);
            Check(centre.DistanceTo(main.MapView.Size / 2) < 2, "player capital is centered after initial layout");
            var cellPixels = main.MapView.TileCenterForCheck(capital.X + 1, capital.Y).DistanceTo(centre);
            var visibleSpan = Math.Min(main.MapView.Size.X, main.MapView.Size.Y) / cellPixels;
            var atMaximum = Math.Abs(main.MapView.ZoomFactor - GameMapView.MaxZoom) < 0.001f;
            Check(visibleSpan <= 14 || atMaximum,
                $"actual footprint is local or reaches the supported zoom ceiling ({visibleSpan:0.0} tiles on the short axis)");
            Check(main.Session.State == before, "initial camera changes no campaign state or RNG");
            var initialZoom = main.MapView.ZoomFactor;
            main.MapView.ZoomFromOverview(increase: false);
            await Frames(3);
            Check(main.MapView.ZoomFactor < initialZoom, "subsequent manual zoom is retained rather than overwritten by initial framing");
            var loadedSession = new GameSession(main.Session.World, main.Session.Ruleset, main.Session.Scenario,
                new SaveGame(before.SchemaVersion, "capital-load", "capital-load", before.ScenarioId, before.WorldId, before.RulesetId, before));
            RemoveChild(app); app.QueueFree();
            var loaded = new MainGameScreen { Session = loadedSession, RepositoryRoot = GameDataContext.RepositoryRoot,
                OpenedPath = "capital-load-check.json", Size = GetViewport().GetVisibleRect().Size };
            AddChild(loaded);
            await Frames(6);
            Check(loaded.MapView.ZoomFactor < 4, "loaded-game path keeps its existing fit rather than new-game capital framing");
            Check(loaded.Session.State == before, "loaded view changes no state or RNG");
            RemoveChild(loaded); loaded.QueueFree();
        }
        catch (Exception ex) { GD.PrintErr($"CapitalStartCheck: {ex}"); _ok = false; }
        GD.Print($"CapitalStartCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
