using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class StagedChangeUnitsCheck : Node
{
    private bool _ok = true;
    public override async void _Ready()
    {
        try
        {
            await Mixed(commit: false);
            await Mixed(commit: true);
            await RefusedBatch();
            Stale();
        }
        catch (Exception ex) { GD.PrintErr($"StagedChangeUnitsCheck: {ex}"); _ok = false; }
        GD.Print($"StagedChangeUnitsCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private static GameSession Build()
    {
        var data = GameDataContext.Repository.Resolve("toy-3city");
        var rules = data.Ruleset with { UnitTypes = ValueList.From(data.Ruleset.UnitTypes.Select(t =>
            t.Id == "light_infantry" ? t with { StandardBattalionSize = 15000 } : t)) };
        var initial = new GameSession(data.World, rules, data.Scenario, seedOverride: 42, humanSeatNationId: "north");
        var army = initial.State.ArmyById("north-army-1")! with { Money = 123, SupplyTons = 45,
            Units = ValueList.Of(new UnitSlot(0, "light_infantry", 6000, 6, "First"),
                new UnitSlot(0, "light_infantry", 6000, 6, "Second"), new UnitSlot(0, "heavy_infantry", 2400, 6, "Third")) };
        var state = initial.State with { Armies = ValueList.From(initial.State.Armies.Select(a => a.Id == army.Id ? army : a)) };
        return new GameSession(data.World, rules, data.Scenario, new SaveGame(state.SchemaVersion, "stage-check", "stage-check",
            state.ScenarioId, state.WorldId, state.RulesetId, state));
    }

    private ChangeUnitsDialog Open(GameSession session)
    {
        var dialog = new ChangeUnitsDialog { Session = session, ArmyId = "north-army-1" };
        AddChild(dialog); return dialog;
    }

    private static void Select(ChangeUnitsDialog dialog, params int[] indexes)
    {
        dialog.UnitListForCheck.DeselectAll();
        foreach (var index in indexes) dialog.UnitListForCheck.Select(index, false);
    }

    private static void Press(Node root, string caption) => root.FindChildren("*", "Button", true, false)
        .OfType<Button>().First(b => b.Text == caption).EmitSignal(Button.SignalName.Pressed);

    private async Task Settle() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

    private async Task Mixed(bool commit)
    {
        var session = Build(); var before = session.State; var observed = new List<GameState>();
        session.EventsPublished += _ => observed.Add(session.State);
        var dialog = Open(session); var closed = false; dialog.Closed += () => closed = true;
        Select(dialog, 0); Press(dialog, "Rename unit");
        dialog.FindChildren("*", "LineEdit", true, false).OfType<LineEdit>().Single(edit => edit.Text == "First").Text = "Pending";
        Press(dialog, "Rename");
        Check(dialog.ModelForCheck.Units[0].Name == "Pending" && session.State == before, "rename changes only the working list");
        Select(dialog, 0); Press(dialog, "Split unit"); Press(dialog, "Split");
        Check(dialog.ModelForCheck.Units.Count == 4 && session.State == before, "split stays pending");
        Select(dialog, 0, 3); Press(dialog, "Join units");
        Check(dialog.ModelForCheck.Units.Count == 3 && session.State == before, "join stays pending");
        Select(dialog, 1); Press(dialog, "Disband");
        var prompt = dialog.GetChildren().OfType<ConfirmPrompt>().Single(); prompt.AnswerForCheck(false);
        await Settle();
        Check(dialog.ModelForCheck.Units.Count == 3 && session.State == before, "No leaves the working and live units intact");
        Press(dialog, "Disband");
        dialog.GetChildren().OfType<ConfirmPrompt>().Single().AnswerForCheck(true);
        await Settle();
        Check(dialog.ModelForCheck.Units.Count == 2 && session.State == before && observed.Count == 0,
            "confirmed deletion still changes no live state/RNG or events before OK");
        if (commit) dialog.OkForCheck(); else dialog.CancelForCheck();
        Check(closed, "outer confirmation/cancellation closes the editor");
        if (commit)
        {
            var army = session.State.ArmyById("north-army-1")!;
            Check(army.Units.Count == 2 && army.Units[0].Name == "Pending" && army.Units[0].Troops == 6000,
                "OK applies the complete mixed plan");
            Check(observed.Count == 1 && observed[0] == session.State, "observers see only one complete committed state");
            Check(army.Money == 123 && army.SupplyTons == 45, "unit edits preserve resources");
        }
        else Check(session.State == before && observed.Count == 0, "Cancel discards every edit, including confirmed disband");
        RemoveChild(dialog); dialog.QueueFree(); await Settle();
    }

    private async Task RefusedBatch()
    {
        var session = Build(); var before = session.State; var dialog = Open(session);
        Select(dialog, 0, 1, 2); Press(dialog, "Disband");
        dialog.GetChildren().OfType<ConfirmPrompt>().Single().AnswerForCheck(true);
        await Settle();
        Check(dialog.ModelForCheck.Units.Count == 3 && session.State == before,
            "last-unit refusal cannot delete a prefix of a multi-unit working operation");
        Check(dialog.ReplyForCheck.Contains("one unit", StringComparison.OrdinalIgnoreCase), "refused batch explains the existing safeguard");
        dialog.CancelForCheck(); RemoveChild(dialog); dialog.QueueFree(); await Settle();
    }

    private void Stale()
    {
        var session = Build(); var dialog = Open(session); var closed = false; dialog.Closed += () => closed = true;
        dialog.RenameUnitForCheck(0, "Pending");
        session.Submit("rename-unit north-army-1 1 External"); var external = session.State;
        dialog.OkForCheck();
        Check(!closed && session.State == external, "a stale commit leaves the dialog open and external state unchanged");
        Check(dialog.ReplyForCheck.Contains("changed", StringComparison.OrdinalIgnoreCase), "stale commit has actionable feedback");
        dialog.CancelForCheck(); RemoveChild(dialog); dialog.QueueFree();
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message); _ok &= condition;
    }
}
