using Godot;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// <c>docs/tasks/T24.md</c>'s own binding visual sign-off instruction (Q-B): a windowed run that drives
/// <see cref="AppRoot"/> through every new screen and saves one screenshot each — the main menu, the
/// ruleset chooser, the scenario/seat step, and the main game screen with a selection showing the
/// context panel and news log. <strong>Headless screenshots do not work</strong> (issue #156, the dummy
/// renderer has no texture to read back) — this must be run windowed:
/// <c>Godot_..._console.exe --path godot res://Checks/ScreenshotTour.tscn --quit-after 40</c>, with
/// <c>IC2_SCREENSHOT_DIR</c> set to an existing directory outside the repository.
/// </summary>
/// <remarks>
/// Drives the real <see cref="AppRoot"/>/<see cref="NewGameFlow"/>/<see cref="ScenarioSeatScreen"/>
/// classes directly, through the same public hooks (<see cref="AppRoot.CurrentScreen"/>,
/// <see cref="RulesetChooserScreen.ConfirmSelection"/>, <see cref="ScenarioSeatScreen.ConfirmSeatAndStart"/>)
/// <c>godot/Checks/RulesetFlowCheck.cs</c> and any test can also use, rather than simulating mouse
/// clicks at hardcoded coordinates.
/// </remarks>
public partial class ScreenshotTour : Control
{
    private const int SettleFrames = 6;

    private AppRoot _appRoot = null!;
    private string _outputDirectory = string.Empty;
    private int _frame;
    private int _step;

    public override void _Ready()
    {
        _outputDirectory = System.Environment.GetEnvironmentVariable("IC2_SCREENSHOT_DIR") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_outputDirectory))
        {
            GD.PrintErr("ScreenshotTour: IC2_SCREENSHOT_DIR is not set. Nothing will be captured.");
        }
        else
        {
            Directory.CreateDirectory(_outputDirectory);
        }

        // This driver's own root Control keeps its default (all-zero, equal) anchors deliberately --
        // Godot recomputes any Control's Size from its anchors × its parent's Size whenever the anchors
        // are non-equal ("full rect"), which only resolves correctly once the *parent* chain has a real
        // size somewhere. run/main_scene gets that for free (Godot sizes a project's own configured main
        // scene's root Control to the viewport); a scene reached by an explicit --path argument, as this
        // driver is, does not get the same treatment and stayed at (0,0) even after SetAnchorsPreset and
        // repeated explicit Size assignments (confirmed by a debug size dump during this task's own
        // screenshot pass -- every explicit Size assignment was silently overridden back to (0,0) by
        // that same anchor recomputation, both here and on AppRoot). Setting Size directly, with equal
        // anchors, is the one assignment that sticks.
        Size = GetViewport().GetVisibleRect().Size;

        _appRoot = new AppRoot();
        _appRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_appRoot);
    }

    public override void _Process(double delta)
    {
        _frame++;

        switch (_step)
        {
            case 0 when _frame >= SettleFrames:
                Capture("01-main-menu.png");
                _appRoot.ShowNewGameFlow();
                _frame = 0;
                _step = 1;
                break;

            case 1 when _frame >= SettleFrames:
                Capture("02-ruleset-chooser.png");
                ((NewGameFlow)_appRoot.CurrentScreen!).Chooser!.ConfirmSelection();
                _frame = 0;
                _step = 2;
                break;

            case 2 when _frame >= SettleFrames:
                Capture("03-scenario-seat.png");
                ((NewGameFlow)_appRoot.CurrentScreen!).SeatScreen!.ConfirmSeatAndStart();
                _frame = 0;
                _step = 3;
                break;

            case 3 when _frame >= SettleFrames:
                var mainGame = (MainGameScreen)_appRoot.CurrentScreen!;
                mainGame.ContextPanel.ShowCity("rome");
                mainGame.NewsLog.Toggle();
                _frame = 0;
                _step = 4;
                break;

            case 4 when _frame >= SettleFrames:
                Capture("04-main-game-screen.png");
                GD.Print("ScreenshotTour: done.");
                GetTree().Quit(0);
                break;
        }
    }

    private void Capture(string fileName)
    {
        if (string.IsNullOrWhiteSpace(_outputDirectory))
        {
            return;
        }

        var path = Path.Combine(_outputDirectory, fileName);
        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(path);
        GD.Print(error == Error.Ok
            ? $"ScreenshotTour: saved {path}"
            : $"ScreenshotTour: could not save {path}: {error}");
    }
}
