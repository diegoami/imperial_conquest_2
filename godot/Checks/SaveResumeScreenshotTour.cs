using Godot;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// <c>docs/tasks/T95.md</c>'s own binding visual sign-off (Q-B, added to this task's entry after PR #481
/// and the user's decision on #375's original governance note): a windowed run that drives
/// <see cref="AppRoot"/> through the Save action and the Load-and-resume flow and saves one screenshot
/// each — the Save control in the main game screen, the confirmation after saving, the Load screen
/// listing that real save, and the resumed main game screen after "Continue". <strong>Headless
/// screenshots do not work</strong> (the same reason <c>godot/Checks/ScreenshotTour.cs</c> gives, issue
/// #156 — the dummy renderer has no texture to read back), so this must be run windowed:
/// <c>Godot_..._console.exe --path godot res://Checks/SaveResumeScreenshotTour.tscn --quit-after 40</c>,
/// with <c>IC2_SCREENSHOT_DIR</c> set to an existing directory outside the repository.
/// </summary>
/// <remarks>
/// Drives the real <see cref="AppRoot"/>/<see cref="NewGameFlow"/>/<see cref="LoadGameScreen"/>/
/// <see cref="MainGameScreen"/> classes directly, through the same public hooks
/// <c>godot/Checks/ScreenshotTour.cs</c> already establishes (<see cref="AppRoot.CurrentScreen"/>,
/// <c>ConfirmSelection</c>, <c>ConfirmSeatAndStart</c>) plus this task's own two additions
/// (<see cref="MainGameScreen.PressSaveForCheck"/>, <see cref="LoadGameScreen.ContinueForCheck"/>) —
/// never simulated mouse clicks at hardcoded coordinates. The save file this tour produces (through the
/// real Save action, exactly like <c>godot/Checks/SaveResumeCheck.cs</c>) is deleted once the last
/// screenshot is captured, the same "never leaves a save behind" cleanup that check performs — see its
/// own remarks for why no <c>--user-data-dir</c> flag exists in this Godot build to avoid the real
/// profile directory in the first place.
/// </remarks>
public partial class SaveResumeScreenshotTour : Control
{
    private const int SettleFrames = 6;

    private AppRoot _appRoot = null!;
    private string _outputDirectory = string.Empty;
    private string? _savedPath;
    private int _frame;
    private int _step;

    public override void _Ready()
    {
        _outputDirectory = System.Environment.GetEnvironmentVariable("IC2_SCREENSHOT_DIR") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_outputDirectory))
        {
            GD.PrintErr("SaveResumeScreenshotTour: IC2_SCREENSHOT_DIR is not set. Nothing will be captured.");
        }
        else
        {
            Directory.CreateDirectory(_outputDirectory);
        }

        // Same anchor/size caveat ScreenshotTour.cs documents: a scene reached by an explicit --path
        // argument (not the project's own run/main_scene) never gets its Size resolved from its anchors,
        // so this is set directly instead.
        Size = GetViewport().GetVisibleRect().Size;

        _appRoot = new AppRoot();
        _appRoot.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_appRoot);
    }

    public override void _Process(double delta)
    {
        _frame++;

        switch (_step)
        {
            case 0 when _frame >= SettleFrames:
                // Reach the main game screen the same way ScreenshotTour.cs's own tour does (default
                // ruleset card, seat 0).
                _appRoot.ShowNewGameFlow();
                _frame = 0;
                _step = 1;
                break;

            case 1 when _frame >= SettleFrames:
                ((NewGameFlow)_appRoot.CurrentScreen!).Chooser!.ConfirmSelection();
                _frame = 0;
                _step = 2;
                break;

            case 2 when _frame >= SettleFrames:
                ((NewGameFlow)_appRoot.CurrentScreen!).SeatScreen!.ConfirmSeatAndStart();
                _frame = 0;
                _step = 3;
                break;

            case 3 when _frame >= SettleFrames:
                Capture("01-main-game-screen-save-control.png");
                ((MainGameScreen)_appRoot.CurrentScreen!).PressSaveForCheck();
                _savedPath = ((MainGameScreen)_appRoot.CurrentScreen!).LastSavedPath;
                _frame = 0;
                _step = 4;
                break;

            case 4 when _frame >= SettleFrames:
                Capture("02-save-confirmation.png");
                _appRoot.ShowLoadGame();
                _frame = 0;
                _step = 5;
                break;

            case 5 when _frame >= SettleFrames:
                Capture("03-load-screen-listing-a-real-save.png");
                if (_savedPath is not null)
                {
                    ((LoadGameScreen)_appRoot.CurrentScreen!).ContinueForCheck(_savedPath);
                }

                _frame = 0;
                _step = 6;
                break;

            case 6 when _frame >= SettleFrames:
                Capture("04-resumed-main-game-screen.png");
                CleanUpSavedFile();
                GD.Print("SaveResumeScreenshotTour: done.");
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
            ? $"SaveResumeScreenshotTour: saved {path}"
            : $"SaveResumeScreenshotTour: could not save {path}: {error}");
    }

    /// <summary>See this class's own remarks — the same cleanup <c>SaveResumeCheck.cs</c> performs.</summary>
    private void CleanUpSavedFile()
    {
        if (_savedPath is null)
        {
            return;
        }

        try
        {
            if (File.Exists(_savedPath))
            {
                File.Delete(_savedPath);
            }

            var directory = Path.GetDirectoryName(_savedPath);
            if (directory is not null && Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0)
            {
                Directory.Delete(directory);
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"SaveResumeScreenshotTour: could not clean up '{_savedPath}': {ex.Message}");
        }
    }
}
