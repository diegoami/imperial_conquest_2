using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>T293.3 (#1839): actual New Game, capital framing, period captions and native loaded-map paths
/// for the Mediterranean campaigns, 431, 280 and 218 BC (docs/design/mediterranean-campaigns.md).</summary>
public partial class MediterraneanCampaignCheck : Node
{
    private static readonly IReadOnlyDictionary<int, string> DefaultSeats = new Dictionary<int, string>
    {
        [431] = "athens", // owner decision 2026-11-16: Athens is the default human seat at 431 BC
        [280] = "epirus", // owner decision 2026-11-16: Epirus (Pyrrhus) is the default human seat at 280 BC
        [218] = "carthage", // owner decision 2026-11-16: Carthage (Hannibal) is the default human seat at 218 BC
    };

    private bool _ok = true;

    public override async void _Ready()
    {
        try
        {
            foreach (var year in new[] { 431, 280, 218 })
            {
                var app = new AppRoot { Size = GetViewport().GetVisibleRect().Size };
                AddChild(app);
                app.ShowNewGameFlow();
                var flow = (NewGameFlow)app.CurrentScreen!;
                var card = RulesetPresets.CardsFor(GameDataContext.Repository).Single(c => c.ScenarioId == $"mediterranean-{year}bc");
                flow.Chooser!.SelectCard(card);
                flow.Chooser.ConfirmSelection();
                var seats = flow.SeatScreen!;
                Check(seats.HumanNationIds.SequenceEqual(new[] { DefaultSeats[year] }),
                    $"{year}: {DefaultSeats[year]} is the ordinary default human choice");
                seats.ConfirmSeatAndStart();
                var main = (MainGameScreen)app.CurrentScreen!;
                await Frames(10);
                var state = main.Session.State;
                var nation = state.NationById(state.ActiveNationId)!;
                var capital = state.CityById(nation.CapitalCityId!)!;
                Check(main.Session.World.Id == $"mediterranean-{year}bc", $"{year}: selected world bootstraps through the real flow");
                Check(state.Calendar.YearBc == year, $"{year}: correct opening calendar");
                Check(state.NationById("barbarians") is { IsBarbarian: true, Control: SeatControl.Ai, Eliminated: false },
                    $"{year}: cityless barbarian role is active and non-human");
                Check(main.Session.World.Width == 400 && main.Session.World.Height == 220, $"{year}: the approved 400x220 map");
                Check(main.MapView.ZoomFactor >= 6, $"{year}: new campaign opens at local capital zoom");
                Check(main.MapView.TileCenterForCheck(capital.X, capital.Y).DistanceTo(main.MapView.Size / 2) < 2,
                    $"{year}: player capital is centered after layout");
                var caption = main.CommandTable.MenuRowById("area_map.show_mercs_heavy_infantry")!.Caption;
                Check(caption == (year == 431 ? "Hoplites" : "Spear infantry"),
                    $"{year}: period caption reaches the real menu table");
                var restored = new GameSession(main.Session.World, main.Session.Ruleset, main.Session.Scenario,
                    new SaveGame(state.SchemaVersion, "mediterranean-load", "mediterranean-load", state.ScenarioId, state.WorldId, state.RulesetId, state));
                var root = main.RepositoryRoot;
                RemoveChild(app);
                app.QueueFree();
                await Frames(3);
                var loaded = new MainGameScreen { Session = restored, RepositoryRoot = root,
                    OpenedPath = "mediterranean-load-check.json", Size = GetViewport().GetVisibleRect().Size };
                AddChild(loaded);
                await Frames(8);
                Check(loaded.Session.State == state, $"{year}: actual loaded map retains the native campaign state");
                Check(loaded.MapView.ZoomFactor < 4, $"{year}: load retains existing fit instead of applying new-game capital framing");
                RemoveChild(loaded);
                loaded.QueueFree();
                await Frames(3);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MediterraneanCampaignCheck: {ex}");
            _ok = false;
        }
        GD.Print($"MediterraneanCampaignCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private async Task Frames(int count)
    {
        for (var i = 0; i < count; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Check(bool condition, string message)
    {
        GD.Print($"MediterraneanCampaignCheck: {(condition ? "PASS" : "FAIL")} {message}");
        _ok &= condition;
    }
}
