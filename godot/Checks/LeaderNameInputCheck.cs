using Godot;
using IC2.Engine.Presentation;
using IC2.Engine.Persistence;
using IC2.Engine.Serialization;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class LeaderNameInputCheck : Node
{
    private bool _ok = true;
    public override async void _Ready()
    {
        try
        {
            var resolved = GameDataContext.Repository.Resolve("toy-3city");
            var selection = new NewGameSelection { SelectedCard = RulesetPresets.CardForScenario(resolved) };
            var screen = new ScenarioSeatScreen { Selection = selection }; AddChild(screen);
            var edit = screen.LeaderEditForCheck("north");
            var drawn = GameSessionFactory.LeaderNamePreview(resolved)["north"];
            Check(edit.Text == drawn && edit.MaxLength == 25, "New Game displays the actual default draw with a 25-character input limit");
            screen.SetLeaderNameForCheck("north", "Changed"); screen.SetHumanForCheck("north", false);
            Check(!edit.Editable && edit.Text == drawn && !screen.CanStart, "unticking restores the generated name and preserves the human-seat requirement");
            screen.SetHumanForCheck("north", true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            Check(edit.Editable && edit.HasFocus() && edit.GetSelectedText() == drawn, "ticking focuses and selects the editable leader name");
            var name = "ABCDEFGHIJKLMNOPQRSTUVWXY";
            screen.SetLeaderNameForCheck("north", name);
            screen.SetHumanForCheck("south", true); screen.SetLeaderNameForCheck("south", "   ");
            screen.ConfirmSeatAndStart();
            var session = GameSessionFactory.CreateSession(GameDataContext.Repository, selection);
            var baseline = new NewGameSelection { SelectedCard = selection.SelectedCard };
            baseline.HumanNationIds.AddRange(selection.HumanNationIds);
            baseline.AiPersonalityOverride = selection.AiPersonalityOverride;
            var control = GameSessionFactory.CreateSession(GameDataContext.Repository, baseline);
            Check(session.State.NationById("north")!.LeaderName == name
                && session.State.NationById("south")!.LeaderName == control.State.NationById("south")!.LeaderName,
                "all 25 typed characters persist while spaces keep the generated leader");
            Check(session.State.RandomSeed == control.State.RandomSeed,
                "preview, focus and renaming do not consume campaign RNG");
            var save = new IC2.Engine.Model.SaveGame(session.State.SchemaVersion, "leader-check", "leader-check",
                session.State.ScenarioId, session.State.WorldId, session.State.RulesetId, session.State);
            var json = SaveManager.Serialize(save);
            var loaded = SaveManager.Load("leader-check.json", json, resolved.World, resolved.Ruleset);
            Check(loaded.State.NationById("north")!.LeaderName == name, "native save round-trip preserves the full typed name");
            RemoveChild(screen); screen.QueueFree();
        }
        catch (Exception ex) { GD.PrintErr($"LeaderNameInputCheck: {ex}"); _ok = false; }
        GD.Print($"LeaderNameInputCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private void Check(bool condition, string message) { GD.Print((condition ? "PASS: " : "FAIL: ") + message); _ok &= condition; }
}
