using Godot;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class SaveNamingCheck : Node
{
    private bool _ok = true;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "ic2-save-name-" + Guid.NewGuid().ToString("N"));
    public override async void _Ready()
    {
        try
        {
            Directory.CreateDirectory(_folder);
            var data = GameDataContext.Repository.Resolve("classical-mediterranean");
            var original = new GameSession(data.World, data.Ruleset, data.Scenario, humanSeatNationId: "ptolemaic");
            var state = original.State with { Calendar = original.State.Calendar with { TurnIndex = 7 } };
            var snapshot = new SaveGame(state.SchemaVersion, "naming", "naming", state.ScenarioId, state.WorldId, state.RulesetId, state);
            var session = new GameSession(data.World, data.Ruleset, data.Scenario, snapshot);
            var main = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot,
                Size = GetViewport().GetVisibleRect().Size };
            AddChild(main);
            var legacy = Path.Combine(_folder, "classical-mediterranean-save.json");
            main.SaveAsForCheck(legacy); // Prime the last-browsed folder without touching user:// files.
            Check(File.Exists(legacy) && main.LastSavedPath == legacy, "explicit Save As keeps the player's chosen filename");
            await CheckSuggestion(main, "ptolemeans_7.json");
            var collision = Path.Combine(_folder, "ptolemeans_7.json");
            File.WriteAllText(collision, "reserved");
            await CheckSuggestion(main, "ptolemeans_7_2.json");
            main.MenuBar.PressItemForCheck("file.save");
            Check(main.LastSavedPath == legacy && File.ReadAllText(collision) == "reserved",
                "ordinary Save writes back to its explicit path without overwriting a suggested-name collision");
            Check(session.State == state, "suggesting and saving changes no campaign state or RNG");
            RemoveChild(main); main.QueueFree();

            var loaded = SaveManager.LoadFile(legacy, data.World, data.Ruleset);
            var resumed = new GameSession(data.World, data.Ruleset, data.Scenario, loaded);
            var reopen = new MainGameScreen { Session = resumed, RepositoryRoot = GameDataContext.RepositoryRoot,
                OpenedPath = legacy, Size = GetViewport().GetVisibleRect().Size };
            AddChild(reopen);
            await CheckSuggestion(reopen, "ptolemeans_7_2.json");
            reopen.MenuBar.PressItemForCheck("file.save");
            Check(reopen.LastSavedPath == legacy, "loaded game preserves writeback while Save As proposes concise nation/turn naming");
            RemoveChild(reopen); reopen.QueueFree();
        }
        catch (Exception ex) { GD.PrintErr($"SaveNamingCheck: {ex}"); _ok = false; }
        finally
        {
            if (Directory.Exists(_folder))
            {
                foreach (var file in Directory.GetFiles(_folder)) File.Delete(file);
                Directory.Delete(_folder);
            }
        }
        GD.Print($"SaveNamingCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private async Task CheckSuggestion(MainGameScreen main, string expected)
    {
        Check(main.MenuBar.PressItemForCheck("file.save_as"), "actual Save As menu route is wired");
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(main.SaveAsDialog.CurrentFile == expected, "actual Save As proposes " + expected);
        main.SaveAsDialog.Hide();
    }
    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
