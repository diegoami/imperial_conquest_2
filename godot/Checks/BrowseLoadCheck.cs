using Godot;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// <c>docs/tasks/T165.md</c> (#1021) Done-when 2: saves a game, copies the save to a temporary folder
/// outside <c>user://saves</c>, opens the copy through Load game's Browse path
/// (<see cref="LoadGameScreen.BrowseForCheck"/>, the handler the Browse dialog's <c>FileSelected</c> calls)
/// and checks the resumed session is on the same turn and active seat. It also checks the Browse dialog's
/// own setup (whole file system, <c>*.json</c> and <c>*.sav</c>) and that an unreadable <c>.sav</c> gives a
/// message and starts nothing. It needs no original save, so CI can run it. On <c>origin/main</c> the
/// Browse hook does not exist, so the check does not even build there.
/// </summary>
public partial class BrowseLoadCheck : Node
{
    private bool _ok = true;
    private readonly List<string> _cleanUp = new();
    private string? _tempDir;

    public override void _Ready()
    {
        try
        {
            Run();
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"BrowseLoadCheck: unhandled exception: {ex}");
            _ok = false;
        }
        finally
        {
            foreach (var f in _cleanUp)
            {
                try { if (File.Exists(f)) File.Delete(f); } catch (System.Exception) { }
            }

            try { if (_tempDir is not null && Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch (System.Exception) { }
        }

        var exitCode = _ok ? 0 : 1;
        GD.Print($"BrowseLoadCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    private void Run()
    {
        var appRoot = new AppRoot();
        AddChild(appRoot);

        appRoot.ShowNewGameFlow();
        var flow = appRoot.CurrentScreen as NewGameFlow;
        if (!Check(flow?.Chooser is not null, "New Game reaches the ruleset chooser")) return;
        flow!.Chooser!.ConfirmSelection();
        if (!Check(flow.SeatScreen is not null, "the seat step is reached")) return;
        flow.SeatScreen!.ConfirmSeatAndStart();
        if (!Check(appRoot.CurrentScreen is MainGameScreen, "Start Game reaches the main game screen")) return;

        var original = (MainGameScreen)appRoot.CurrentScreen!;
        var originalState = original.Session.State;
        original.PressSaveForCheck("t165-browseloadcheck");
        var saved = original.LastSavedPath;
        if (!Check(saved is not null && File.Exists(saved), $"the Save action wrote a file ({saved ?? "<none>"})")) return;
        _cleanUp.Add(saved!);

        _tempDir = Path.Combine(Path.GetTempPath(), "ic2-t165-browse-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var copy = Path.Combine(_tempDir, "elsewhere.json");
        File.Copy(saved!, copy);
        var savesDir = Path.GetDirectoryName(saved)!;
        Check(
            !string.Equals(Path.GetFullPath(_tempDir), Path.GetFullPath(savesDir), System.StringComparison.OrdinalIgnoreCase),
            "the copy is outside user://saves");

        appRoot.ShowLoadGame();
        if (!Check(appRoot.CurrentScreen is LoadGameScreen, "AppRoot shows the Load screen")) return;
        var load = (LoadGameScreen)appRoot.CurrentScreen!;

        var dialog = load.BrowseDialog;
        Check(dialog.Access == FileDialog.AccessEnum.Filesystem, "Browse opens over the whole file system");
        var filters = string.Join(" ", dialog.Filters);
        Check(filters.Contains("*.json") && filters.Contains("*.sav"), $"Browse filters to *.json and *.sav ('{filters}')");

        // A file Browse cannot open gives a message and starts nothing.
        var bad = Path.Combine(_tempDir, "broken.sav");
        File.WriteAllBytes(bad, new byte[] { 1, 2, 3, 4 });
        load.BrowseForCheck(bad);
        Check(appRoot.CurrentScreen is LoadGameScreen, "an unreadable .sav leaves the Load screen showing, no game started");

        load.BrowseForCheck(copy);
        Check(LoadGameScreen.LastBrowsedFolderForCheck == _tempDir, "Browse remembers the folder it last opened from");
        if (!Check(appRoot.CurrentScreen is MainGameScreen, "opening the copy through Browse reaches the main game screen")) return;

        var resumed = ((MainGameScreen)appRoot.CurrentScreen!).Session.State;
        Check(resumed.ActiveNationId == originalState.ActiveNationId,
            $"same active seat ('{resumed.ActiveNationId}' vs '{originalState.ActiveNationId}')");
        Check(resumed.Calendar.TurnIndex == originalState.Calendar.TurnIndex,
            $"same turn ({resumed.Calendar.TurnIndex} vs {originalState.Calendar.TurnIndex})");
    }

    private bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
        return condition;
    }
}
