using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>Manual §4.8.2: drive the real split inputs/buttons, not the command-only check hook.</summary>
public partial class UnitSplitInputCheck : Node
{
    private bool _ok = true;

    public override void _Ready()
    {
        try
        {
            Run("light_infantry", 6000, 3000, 3000, 3000);
            Run("light_infantry", 9000, 3000, 6000, 4500);
            Run("heavy_infantry", 6000, 1200, 4800, 3000);
            Run("archers", 1400, 700, 700, 700);
            Run("light_cavalry", 2800, 1400, 1400, 1400);
            Run("heavy_cavalry", 1000, 500, 500, 500);
            Run("light_infantry", 1001, 500, 501, 500, standardSize: 2503);
            CheckRefusal("light_infantry", 1000, "too small", standardSize: 2503);
            CheckRefusal("light_infantry", 5999, "too small");
            CheckRefusal("light_infantry", 6000, "regular", mercenary: true);
            CheckRefusal("light_infantry", 6000, "20 units", full: true);
            SelectionChange();
        }
        catch (Exception ex) { GD.PrintErr($"UnitSplitInputCheck: {ex}"); _ok = false; }
        GD.Print($"UnitSplitInputCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private static (GameSession Session, string ArmyId) Session(string type, int troops,
        int? standardSize = null, bool mercenary = false, bool full = false)
    {
        var data = GameDataContext.Repository.Resolve("classical-mediterranean");
        var ruleset = standardSize is { } size ? data.Ruleset with {
            UnitTypes = ValueList.From(data.Ruleset.UnitTypes.Select(t => t.Id == type
                ? t with { StandardBattalionSize = size } : t)) } : data.Ruleset;
        var initial = new GameSession(data.World, ruleset, data.Scenario, seedOverride: 1, humanSeatNationId: "rome");
        var units = new List<UnitSlot> { new(mercenary ? 3 : 0, type, troops, 6, "Split source") };
        for (var i = 1; i < (full ? 20 : 2); i++)
            units.Add(new UnitSlot(0, "heavy_infantry", full ? 1000 : 6000, 6, $"Other {i}"));
        var army = new ArmyState("split-input-army", "rome", 100, 100, 8, 70, 500, 100,
            0, null, ValueList.From(units));
        var state = initial.State with { Armies = ValueList.Of(army) };
        var save = new SaveGame(state.SchemaVersion, "split-input-check", "split-input-check", state.ScenarioId,
            state.WorldId, state.RulesetId, state);
        return (new GameSession(data.World, ruleset, data.Scenario, save), army.Id);
    }

    private ChangeUnitsDialog Open(GameSession session, string armyId, List<string> commands)
    {
        var dialog = new ChangeUnitsDialog { Session = session, ArmyId = armyId,
            CommitEdits = (draft, final) => { var planned = draft.Commands.ToArray(); var result = draft.Commit(final);
                if (result.Accepted) commands.AddRange(planned); return result; } };
        AddChild(dialog);
        dialog.SelectUnitForCheck(0);
        Press(dialog, "Split unit");
        return dialog;
    }

    private void Run(string type, int troops, int minimum, int maximum, int initial, int? standardSize = null)
    {
        var (session, armyId) = Session(type, troops, standardSize);
        var before = session.State;
        var commands = new List<string>();
        var dialog = Open(session, armyId, commands);
        var inputs = dialog.FindChildren("*", "SpinBox", true, false).OfType<SpinBox>().ToArray();
        Check(inputs.Length == 1, "split edits one allocation rather than adding unrelated quantities");
        Check(inputs.Sum(s => s.MinValue) == minimum && inputs.Sum(s => s.MaxValue) == maximum,
            $"{type} bounds keep both resulting units above their loaded minimum");
        Check(inputs.Sum(s => s.Value) == initial, "split starts at balanced halves, retaining an odd remainder");
        Check(session.State == before && commands.Count == 0, "opening and input preview issue no command or RNG change");
        Check(dialog.FindChild("SplitBalances", true, false) is Label, "both resulting halves are displayed");
        // This also reproduces the old bug: the first input clamps at 1,000,
        // so the actual button cannot send a legal 3,000 allocation.
        inputs[0].Value = initial;
        foreach (var extra in inputs.Skip(1)) extra.Value = 0;
        Press(dialog, "Split");
        dialog.OkForCheck();
        Check(commands.Count == 1 && !dialog.ReplyForCheck.Contains("rejected", StringComparison.OrdinalIgnoreCase),
            "real split button and outer OK produce one accepted split");
        var after = session.State.ArmyById(armyId)!;
        Check(after.Units.Count == before.ArmyById(armyId)!.Units.Count + 1
            && after.Units[0].Troops == troops - initial && after.Units[^1].Troops == initial,
            "committed halves agree with the input and conserve troops");
        Check(after.Money == before.ArmyById(armyId)!.Money && after.SupplyTons == before.ArmyById(armyId)!.SupplyTons
            && before.Nations == session.State.Nations, "splitting changes no purse, supplies or treasury");
        RemoveChild(dialog); dialog.QueueFree();
    }

    private void CheckRefusal(string type, int troops, string reason, bool mercenary = false, bool full = false,
        int? standardSize = null)
    {
        var (session, id) = Session(type, troops, standardSize, mercenary, full);
        var before = session.State;
        var commands = new List<string>();
        var dialog = Open(session, id, commands);
        Check(dialog.ReplyForCheck.Contains(reason, StringComparison.OrdinalIgnoreCase), "ineligible split explains the existing engine gate");
        Check(commands.Count == 0 && session.State == before, "ineligible split submits nothing and preserves state");
        RemoveChild(dialog); dialog.QueueFree();
    }

    private void SelectionChange()
    {
        var (session, id) = Session("light_infantry", 9000);
        var before = session.State;
        var commands = new List<string>();
        var dialog = Open(session, id, commands);
        var input = dialog.FindChildren("*", "SpinBox", true, false).OfType<SpinBox>().First();
        Press(dialog, "+100");
        Check(dialog.FindChild("SplitBalances", true, false) is Label { Text: var text }
            && text.Contains($"{4400:N0}") && text.Contains($"{4600:N0}"), "edits show both working troop totals");
        Press(dialog, "+1000");
        Check(input.Value == 5600, "the thousand-step button adjusts the same allocation");
        Press(dialog, "+1000");
        Check(input.Value == 6000, "a large step stops at the legal bound for the remaining half");
        Press(dialog, "-1000");
        Press(dialog, "-100");
        Check(input.Value == 4900, "both reverse step buttons move troops back without changing the source total");
        dialog.UnitListForCheck.DeselectAll();
        dialog.SelectUnitForCheck(1);
        dialog.UnitListForCheck.EmitSignal(ItemList.SignalName.MultiSelected, 1L, true);
        Check(input.MinValue == 1200 && input.MaxValue == 4800 && input.Value == 3000,
            "changing selected type resets bounds and balanced default instead of reusing stale allocation");
        // Inner Cancel is an input cancellation, independent of the outer
        // Change units transaction policy tracked by #1556.
        var cancel = dialog.FindChildren("*", "Button", true, false).OfType<Button>()
            .First(b => b.Text == "Cancel" && b.GetParent().GetParent() is Control c && c.Visible);
        cancel.EmitSignal(Button.SignalName.Pressed);
        Check(session.State == before && commands.Count == 0, "cancelling pending allocation leaves the campaign untouched");
        RemoveChild(dialog); dialog.QueueFree();
    }

    private static void Press(Node root, string text) => root.FindChildren("*", "Button", true, false)
        .OfType<Button>().First(b => b.Text == text).EmitSignal(Button.SignalName.Pressed);

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
