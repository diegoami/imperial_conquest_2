using Godot;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// Bug #919. Part 1: the window's close request (NotificationWMCloseRequest) with a game open asks the
/// File -> Close question and quits only on Yes; with no game open it quits at once. Part 2: after
/// File -> Open, Save rewrites the opened file instead of writing a new default-named one. Part 3 (fix #1081):
/// Save As to a folder outside user://saves becomes the current file, Save writes back to it, Cancel writes nothing.
/// <code>godot --headless --path godot res://Checks/CloseAndSaveCheck.tscn --quit-after 900</code>
/// Saves go to user://saves under a check-unique suffix; every file this check creates is removed.
/// </summary>
public partial class CloseAndSaveCheck : Node
{
    private const string Suffix = "b919-closeandsavecheck";
    private bool _ok = true;
    private string? _savesDirectory;
    private HashSet<string> _before = new();

    public override void _Ready()
    {
        try
        {
            Run();
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"CloseAndSaveCheck: unhandled exception: {ex}");
            _ok = false;
        }
        finally
        {
            CleanUp();
        }

        var exitCode = _ok ? 0 : 1;
        GD.Print($"CloseAndSaveCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    private void CleanUp()
    {
        try
        {
            if (_savesDirectory is not null && Directory.Exists(_savesDirectory))
            {
                foreach (var file in Directory.GetFiles(_savesDirectory).Where(f => !_before.Contains(f)))
                {
                    File.Delete(file);
                }

                if (Directory.GetFileSystemEntries(_savesDirectory).Length == 0)
                {
                    Directory.Delete(_savesDirectory);
                }
            }
        }
        catch (System.Exception ex)
        {
            GD.PrintErr($"CloseAndSaveCheck: cleanup failed: {ex.Message}");
        }
    }

    private static void PressButton(Node root, string text)
    {
        foreach (var child in root.GetChildren())
        {
            if (child is Button button && button.Text == text)
            {
                button.EmitSignal(BaseButton.SignalName.Pressed);
                return;
            }

            PressButton(child, text);
        }
    }

    private AppRoot StartGame()
    {
        var appRoot = new AppRoot();
        AddChild(appRoot);
        appRoot.ShowNewGameFlow();
        var flow = (NewGameFlow)appRoot.CurrentScreen!;
        flow.Chooser!.ConfirmSelection();
        flow.SeatScreen!.ConfirmSeatAndStart();
        return appRoot;
    }

    private void Run()
    {
        _savesDirectory = ProjectSettings.GlobalizePath("user://saves");
        _before = Directory.Exists(_savesDirectory) ? Directory.GetFiles(_savesDirectory).ToHashSet() : new();

        // ---- Part 1: close request ----
        var menuRoot = new AppRoot();
        AddChild(menuRoot);
        var quits = 0;
        menuRoot.QuitAction = () => quits++;
        menuRoot.Notification((int)NotificationWMCloseRequest);
        Check(quits == 1, "with no game open (title screen) the close request quits at once");

        var appRoot = StartGame();
        Check(appRoot.CurrentScreen is MainGameScreen, "a game is open");
        var game = (MainGameScreen)appRoot.CurrentScreen!;
        quits = 0;
        appRoot.QuitAction = () => quits++;
        appRoot.Notification((int)NotificationWMCloseRequest);
        Check(game.ActiveOverlay is ConfirmPrompt, "with a game open the close request shows the confirmation");
        Check(quits == 0, "and does not quit yet");

        if (game.ActiveOverlay is not null)
        {
            PressButton(game.ActiveOverlay, "No");
        }

        Check(quits == 0, "answering No does not quit");
        Check(game.ActiveOverlay is not ConfirmPrompt, "answering No closes the prompt");

        appRoot.Notification((int)NotificationWMCloseRequest);
        if (game.ActiveOverlay is ConfirmPrompt)
        {
            PressButton(game.ActiveOverlay, "Yes");
        }

        Check(quits == 1, "answering Yes quits");

        // ---- Part 2: Save writes back to the opened file ----
        game.PressSaveForCheck(Suffix);
        var saved = game.LastSavedPath;
        if (!Check(saved is not null && File.Exists(saved), $"Save As to a name wrote a file ({saved ?? "<none>"})"))
        {
            return;
        }

        appRoot.ShowLoadGame();
        ((LoadGameScreen)appRoot.CurrentScreen!).ContinueForCheck(saved!);
        var opened = (MainGameScreen)appRoot.CurrentScreen!;
        opened.SubmitForCheck("end");
        var contentBefore = File.ReadAllText(saved!);
        var filesBefore = Directory.GetFiles(_savesDirectory).ToHashSet();

        opened.PressSaveForCheck();

        var filesAfter = Directory.GetFiles(_savesDirectory).ToHashSet();
        Check(File.ReadAllText(saved!) != contentBefore, "Save rewrote the opened file (its content changed)");
        Check(filesAfter.SetEquals(filesBefore), "Save created no new file");
        Check(opened.LastSavedPath == saved, "Save wrote to the opened path");

        // ---- Part 3 (fix #1081): Save As to a folder outside user://saves ----
        var elsewhere = Path.Combine(Path.GetTempPath(), "ic2-check-1081-" + System.Guid.NewGuid().ToString("N"));
        try
        {
            var target = Path.Combine(elsewhere, "chosen.json");
            var savedBefore = opened.LastSavedPath;

            opened.SaveAsDialog.Visible = false;
            opened.SaveAsDialog.EmitSignal(FileDialog.SignalName.Canceled);
            Check(!Directory.Exists(elsewhere) && opened.LastSavedPath == savedBefore, "Save As cancelled wrote nothing");

            opened.SaveAsForCheck(target);
            Check(File.Exists(target), "Save As wrote to a folder outside user://saves");
            Check(opened.LastSavedPath == target, "the chosen path became the current file");

            opened.SubmitForCheck("end");
            var contentAs = File.ReadAllText(target);
            var savesNow = Directory.GetFiles(_savesDirectory).ToHashSet();
            opened.PressSaveForCheck();
            Check(File.ReadAllText(target) != contentAs, "a later Save wrote back to the Save As file");
            Check(opened.LastSavedPath == target, "and kept it as the current file");
            Check(Directory.GetFiles(_savesDirectory).ToHashSet().SetEquals(savesNow), "and wrote nothing under user://saves");
        }
        finally
        {
            if (Directory.Exists(elsewhere))
            {
                Directory.Delete(elsewhere, true);
            }
        }
    }

    private bool Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
        return condition;
    }
}
