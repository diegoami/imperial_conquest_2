using Godot;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using System.Security.Cryptography;
using System.Text.Json;
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

    public override async void _Ready()
    {
        try
        {
            var captureDirectory = System.Environment.GetEnvironmentVariable("IC2_LOAD_LIST_CAPTURE_DIR");
            if (string.IsNullOrWhiteSpace(captureDirectory)) Run();
            else await CaptureNativeList(captureDirectory);
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"BrowseLoadCheck: unhandled exception: {ex}");
            _ok = false;
        }
        finally
        {
            CleanUp();
        }

        var exitCode = _ok ? 0 : 1;
        GD.Print($"BrowseLoadCheck: exiting with code {exitCode}.");
        CheckExit.Quit(this, exitCode);
    }

    // Set IC2_LOAD_LIST_CAPTURE_DIR and IC2_CAPTURE_HEAD for the optional windowed capture route.
    // It never writes user://saves or uses owner data. Its only output is the
    // final real LoadGameScreen viewport and a receipt describing the isolated synthetic fixture.
    private async Task CaptureNativeList(string outputDirectory)
    {
        if (DisplayServer.GetName() == "headless")
            throw new InvalidOperationException("Native-list capture requires a windowed display.");
        var head = System.Environment.GetEnvironmentVariable("IC2_CAPTURE_HEAD");
        if (head is null || head.Length != 40 || !head.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Set IC2_CAPTURE_HEAD to the exact checked-out commit.");

        Directory.CreateDirectory(outputDirectory);
        _tempDir = Path.Combine(Path.GetTempPath(), "ic2-1259-capture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var data = GameDataContext.Repository.Resolve("classical-mediterranean");
        var session = new GameSession(data.World, data.Ruleset, data.Scenario);
        var seed = Path.Combine(_tempDir, "seed.json");
        session.Submit($"save {seed}");
        var save = SaveManager.LoadFile(seed, data.World, data.Ruleset) with { Label = "Stored label: 2001-01-01" };
        File.Delete(seed);
        var samples = new[]
        {
            (Name: "z-2026-10-19-newest.json", Time: new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc)),
            (Name: "b-2026-10-18-tie.json", Time: new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc)),
            (Name: "c-2026-10-18-tie.json", Time: new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc)),
            (Name: "a-2026-10-01-oldest.json", Time: new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc)),
        };
        foreach (var sample in samples.Reverse())
        {
            var path = Path.Combine(_tempDir, sample.Name);
            SaveManager.WriteFile(path, save);
            File.SetLastWriteTimeUtc(path, sample.Time);
        }
        var screen = new LoadGameScreen { SavesDirectoryForCheck = _tempDir, Size = GetViewport().GetVisibleRect().Size };
        AddChild(screen);
        var labels = screen.SaveListForCheck.GetChildren().OfType<HBoxContainer>()
            .Select(row => row.GetChildren().OfType<Label>().Single().Text).ToArray();
        Check(labels.Length == samples.Length, "capture contains only the isolated synthetic saves");
        for (var i = 0; i < Math.Min(labels.Length, samples.Length); i++)
            Check(labels[i].StartsWith(samples[i].Name + " \u2014 ", StringComparison.Ordinal), "capture uses actual filename and expected newest-first/tie order");
        if (!_ok) throw new InvalidOperationException("Native-list capture assertions failed.");

        for (var frame = 0; frame < 6; frame++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var png = Path.Combine(outputDirectory, "load-game-filenames-newest-first.png");
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(png) != Error.Ok) throw new IOException("Could not save native-list viewport.");
        File.WriteAllText(Path.Combine(outputDirectory, "load-game-receipt.json"), JsonSerializer.Serialize(new
        {
            Head = head, Scene = "res://Checks/BrowseLoadCheck.tscn", Scenario = data.Scenario.Id,
            Synthetic = true, StoredLabel = save.Label, Order = samples.Select(sample => new { sample.Name, LastWriteTimeUtc = sample.Time }),
            RowText = labels, Display = DisplayServer.GetName(), Renderer = RenderingServer.GetCurrentRenderingMethod(),
            Width = image.GetWidth(), Height = image.GetHeight(), PngSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(png))),
        }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"BrowseLoadCheck: captured final native list at {png}; HEAD {head}.");
    }

    public override void _ExitTree() => CleanUp();

    private void CleanUp()
    {
        foreach (var file in _cleanUp)
        {
            try { if (File.Exists(file)) File.Delete(file); } catch (Exception) { }
        }
        try { if (_tempDir is not null && Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch (Exception) { }
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

        CheckNativeRows(saved!);

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

    private void CheckNativeRows(string source)
    {
        var folder = Path.Combine(_tempDir!, "native-list");
        Directory.CreateDirectory(folder);
        var old = Path.Combine(folder, "a-old.json");
        var newest = Path.Combine(folder, "z-newest.json");
        var tie = Path.Combine(folder, "b-tie.json");
        foreach (var path in new[] { old, newest, tie }) File.Copy(source, path);
        var time = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(old, time.AddDays(-1));
        File.SetLastWriteTimeUtc(newest, time);
        File.SetLastWriteTimeUtc(tie, time);
        var broken = Path.Combine(folder, "broken.json");
        File.WriteAllText(broken, "bad json");
        File.SetLastWriteTimeUtc(broken, time.AddDays(-2));

        var screen = new LoadGameScreen { SavesDirectoryForCheck = folder };
        AddChild(screen);
        var rows = screen.SaveListForCheck.GetChildren().OfType<HBoxContainer>().ToArray();
        Check(rows.Length == 4, "native list retains malformed saves");
        var expected = new[] { tie, newest, old, broken };
        for (var i = 0; i < Math.Min(rows.Length, expected.Length); i++)
        {
            var text = rows[i].GetChildren().OfType<Label>().Single().Text;
            Check(text.StartsWith(Path.GetFileName(expected[i]) + " — ", StringComparison.Ordinal),
                "native row shows actual filename in newest-first order with filename ties");
        }
        if (rows.Length == 4)
        {
            var resumed = 0;
            screen.GameResumed += _ => resumed++;
            rows[1].GetChildren().OfType<Button>().Single().EmitSignal(BaseButton.SignalName.Pressed);
            Check(resumed == 1 && screen.LastLoadedPath == newest, "real Continue button loads its corresponding actual path");
            File.Delete(tie);
            rows[0].GetChildren().OfType<Button>().Single().EmitSignal(BaseButton.SignalName.Pressed);
            rows[3].GetChildren().OfType<Button>().Single().EmitSignal(BaseButton.SignalName.Pressed);
            Check(resumed == 1, "disappearing and malformed native rows start no session");
        }
        RemoveChild(screen);
        screen.QueueFree();
    }

    private bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
        return condition;
    }
}
