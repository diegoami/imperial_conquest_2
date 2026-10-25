using Godot;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>Load opens the real browser directly; retry and cancel preserve the caller and source files.</summary>
public partial class BrowseLoadCheck : Node
{
    private bool _ok = true;
    private string? _tempDir;

    public override async void _Ready()
    {
        try { await Run(); }
        catch (Exception ex) { GD.PrintErr($"BrowseLoadCheck: {ex}"); _ok = false; }
        finally
        {
            if (_tempDir is not null && Path.GetFullPath(_tempDir).StartsWith(
                    Path.Combine(Path.GetTempPath(), "ic2-direct-load-"), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(_tempDir, recursive: true);
        }
        GD.Print($"BrowseLoadCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private async Task Settle()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task Run()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ic2-direct-load-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var data = GameDataContext.Repository.Resolve("toy-3city");
        var original = new GameSession(data.World, data.Ruleset, data.Scenario);
        var source = Path.Combine(_tempDir, "campaign.json");
        original.Submit($"save {source}");
        var bytes = File.ReadAllBytes(source);
        var bad = Path.Combine(_tempDir, "broken.SAV");
        File.WriteAllBytes(bad, new byte[] { 1, 2, 3, 4 });

        var app = new AppRoot { Size = GetViewport().GetVisibleRect().Size };
        AddChild(app);
        var menu = app.CurrentScreen;
        app.ShowLoadGame();
        var load = (LoadGameScreen)app.CurrentScreen!;
        await Settle();
        Check(load.BrowseDialog.Visible, "main-menu Load opens the file browser without another click");
        Check(load.BrowseDialog.Access == FileDialog.AccessEnum.Filesystem
              && load.BrowseDialog.FileMode == FileDialog.FileModeEnum.OpenFile,
            "Load browses the file system for an existing save");
        var filters = string.Join(" ", load.BrowseDialog.Filters);
        Check(filters.Contains("*.json") && filters.Contains("*.sav"), "native JSON and original SAV share the initial filter");
        load.BrowseDialog.EmitSignal(FileDialog.SignalName.Canceled);
        Check(ReferenceEquals(app.CurrentScreen, menu), "Cancel restores the exact menu that opened Load");

        app.ShowLoadGame();
        load = (LoadGameScreen)app.CurrentScreen!;
        await Settle();
        load.BrowseDialog.EmitSignal(FileDialog.SignalName.FileSelected, bad);
        Check(load.ErrorDialog.Visible && load.LastLoadMessage.Length > 0 && app.CurrentScreen == load,
            "invalid original save shows an error without starting a game");
        load.ErrorDialog.Hide();
        load.ErrorDialog.EmitSignal(AcceptDialog.SignalName.Confirmed);
        await Settle();
        Check(load.BrowseDialog.Visible && Path.GetFullPath(load.BrowseDialog.CurrentDir) == Path.GetFullPath(_tempDir),
            "acknowledging an error returns to Browse in the selected folder");
        load.BrowseDialog.EmitSignal(FileDialog.SignalName.FileSelected, source);
        Check(app.CurrentScreen is MainGameScreen, "selecting JSON directly resumes the game");
        var main = (MainGameScreen)app.CurrentScreen!;
        Check(main.OpenedPath == source && main.Session.State.Calendar == original.State.Calendar
              && main.Session.State.ActiveNationId == original.State.ActiveNationId,
            "native save keeps its writeback path, calendar and active seat");
        Check(LoadGameScreen.LastBrowsedFolderForCheck == _tempDir, "the selected folder is remembered");
        Check(bytes.SequenceEqual(File.ReadAllBytes(source)) && File.ReadAllBytes(bad).SequenceEqual(new byte[] { 1, 2, 3, 4 }),
            "successful and refused loading leave both source files unchanged");

        var state = main.Session.State;
        var mode = main.ProcessMode;
        app.ShowLoadGame();
        load = (LoadGameScreen)app.CurrentScreen!;
        await Settle();
        Check(main.ProcessMode == ProcessModeEnum.Disabled && main.GetParent() == app,
            "the existing campaign is retained while Browse owns input");
        load.BrowseDialog.EmitSignal(FileDialog.SignalName.Canceled);
        Check(ReferenceEquals(app.CurrentScreen, main) && ReferenceEquals(main.Session.State, state) && main.ProcessMode == mode,
            "Cancel restores the same campaign and input processing without losing progress");

        app.ShowLoadGame();
        load = (LoadGameScreen)app.CurrentScreen!;
        await Settle();
        load.BrowseDialog.EmitSignal(FileDialog.SignalName.FileSelected, Path.Combine(_tempDir, "missing.json"));
        Check(load.ErrorDialog.Visible && app.CurrentScreen == load, "a missing save reports an error instead of discarding the campaign");
        load.Cancel();
        Check(ReferenceEquals(app.CurrentScreen, main), "cancelling after a load error also restores the existing campaign");

        RemoveChild(app);
        app.QueueFree();
    }

    private void Check(bool condition, string description)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + description);
        _ok &= condition;
    }
}
