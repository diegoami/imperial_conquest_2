using Godot;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// v0.11 visual sign-off: Settings (Original assets section), the Diadochi 323 BC chooser card, seat step and
/// strategic map, and the 301 BC map. Windowed only (headless has no renderer):
/// <c>godot --path godot res://Checks/DiadochiScreenshotTour.tscn --quit-after 200</c> with
/// <c>IC2_SCREENSHOT_DIR</c> set to a directory outside the repository.
/// </summary>
public partial class DiadochiScreenshotTour : Control
{
    private const int Settle = 8;
    private AppRoot _app = null!;
    private string _dir = "";
    private int _frame, _step;

    public override void _Ready()
    {
        _dir = System.Environment.GetEnvironmentVariable("IC2_SCREENSHOT_DIR") ?? "";
        if (_dir.Length > 0) Directory.CreateDirectory(_dir);
        // Optional: show the map with a local original-assets cache (paths come from the environment, never the repo).
        if (System.Environment.GetEnvironmentVariable("IC2_TOUR_ORIGINAL_INSTALL") is { Length: > 0 } install)
        {
            IC2.Slice.Assets.OriginalAssetCache.CacheDirectory = System.Environment.GetEnvironmentVariable("IC2_TOUR_ORIGINAL_CACHE")!;
            SettingsScreen.Commit(SettingsScreen.Current with { OriginalInstallPath = install, UseOriginalAssets = true });
        }
        Size = GetViewport().GetVisibleRect().Size;
        _app = new AppRoot();
        _app.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_app);
    }

    private RulesetCard Card(string scenario) =>
        RulesetPresets.CardsFor(GameDataContext.Repository).First(c => c.ScenarioId == scenario);

    private void Start(string scenario)
    {
        _app.ShowNewGameFlow();
        var flow = (NewGameFlow)_app.CurrentScreen!;
        flow.Chooser!.SelectCard(Card(scenario));
        flow.Chooser.ConfirmSelection();
    }

    public override void _Process(double delta)
    {
        if (++_frame < Settle) return;
        _frame = 0;
        switch (_step)
        {
            case 0: _app.ShowSettings(); break;
            case 1: Capture("01-settings"); Start("diadochi-323bc"); break;
            case 2: Capture("02-diadochi-323-seats"); ((NewGameFlow)_app.CurrentScreen!).SeatScreen!.ConfirmSeatAndStart(); break;
            case 3: Capture("03-diadochi-323-map"); _app.ShowNewGameFlow(); break;
            case 4: Start("diadochi-301bc"); break;
            case 5: Capture("04-diadochi-301-seats"); ((NewGameFlow)_app.CurrentScreen!).SeatScreen!.ConfirmSeatAndStart(); break;
            case 6: Capture("05-diadochi-301-map"); _app.ShowNewGameFlow(); break;
            case 7:
                {
                    var flow = (NewGameFlow)_app.CurrentScreen!;
                    flow.Chooser!.SelectCard(RulesetPresets.CardFor(RulesetPresets.Default));
                    flow.Chooser.ConfirmSelection();
                    break;
                }
            case 8: ((NewGameFlow)_app.CurrentScreen!).SeatScreen!.ConfirmSeatAndStart(); break;
            case 9: Capture("06-classical-map"); CheckExit.Quit(this, 0); break;
        }
        _step++;
    }

    private void Capture(string name)
    {
        if (_dir.Length == 0) return;
        var error = GetViewport().GetTexture().GetImage().SavePng(Path.Combine(_dir, name + ".png"));
        GD.Print($"DiadochiScreenshotTour: {name} {error}");
    }
}
