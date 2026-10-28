using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class StagedArmyDialogsCheck : Node
{
    private bool _ok = true;
    public override async void _Ready()
    {
        try
        {
            foreach (var commit in new[] { false, true })
            {
                await Transfer(commit);
                await Split(commit);
            }
            await DisbandOnlySplit();
        }
        catch (Exception ex) { GD.PrintErr($"StagedArmyDialogsCheck: {ex}"); _ok = false; }
        GD.Print($"StagedArmyDialogsCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private static GameSession Build()
    {
        var data = GameDataContext.Repository.Resolve("toy-3city");
        var initial = new GameSession(data.World, data.Ruleset, data.Scenario, seedOverride: 42, humanSeatNationId: "north");
        var a = initial.State.ArmyById("north-army-1")! with { Id = "a", X = 3, Y = 2, Money = 900, SupplyTons = 206,
            Units = ValueList.Of(new UnitSlot(0, "light_infantry", 4000, 6, "A0"),
                new UnitSlot(0, "heavy_infantry", 4000, 6, "A1"), new UnitSlot(0, "archers", 2000, 6, "A2")) };
        var b = a with { Id = "b", X = 4, Money = 1200, SupplyTons = 10,
            Units = ValueList.Of(new UnitSlot(0, "light_cavalry", 2000, 6, "B0"), new UnitSlot(0, "heavy_cavalry", 2000, 6, "B1")) };
        var state = initial.State with { Armies = ValueList.Of(a, b), Fleets = ValueList<FleetState>.Empty };
        return new GameSession(data.World, data.Ruleset, data.Scenario, new SaveGame(state.SchemaVersion, "army-drafts", "army-drafts",
            state.ScenarioId, state.WorldId, state.RulesetId, state));
    }

    private async Task Settle() => await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    private static void PressFirstDisband(Node dialog) => dialog.FindChildren("*", "Button", true, false)
        .OfType<Button>().First(b => b.Text == "Disband").EmitSignal(Button.SignalName.Pressed);

    private async Task Transfer(bool commit)
    {
        var session = Build(); var before = session.State; var events = 0; session.EventsPublished += _ => events++;
        var dialog = new ArmyTransferDialog { Session = session, ArmyId = "a", PartnerId = "b" };
        AddChild(dialog);
        dialog.SelectedListForCheck.Select(2); dialog.TransferSelectedForCheck(true);
        dialog.PressMoneyForCheck(100); dialog.PressSupplyForCheck(10);
        dialog.SelectedListForCheck.Select(0); PressFirstDisband(dialog);
        Check(dialog.GetChildren().OfType<ConfirmPrompt>().Any(), "Transfer Disband asks before deleting a unit");
        dialog.GetChildren().OfType<ConfirmPrompt>().Single().AnswerForCheck(false);
        await Settle();
        Check(dialog.ModelForCheck.SelectedWorkingUnits.Count == 2, "No retains the transferred working rosters");
        PressFirstDisband(dialog); dialog.GetChildren().OfType<ConfirmPrompt>().Single().AnswerForCheck(true);
        await Settle();
        Check(session.State == before && events == 0, "Transfer deletion, unit moves and resources remain private before OK");
        Check(dialog.ModelForCheck.UnitsToPartner.SequenceEqual(new[] { 1 }) && dialog.ModelForCheck.MoneyNet == 100,
            "disband reindexes surviving transfers without dropping pending allocations");
        var supplyA = dialog.ModelForCheck.SelectedSupplyAfter; var supplyB = dialog.ModelForCheck.PartnerSupplyAfter;
        if (commit) dialog.OkForCheck(); else dialog.CancelForCheck();
        if (commit)
        {
            var a = session.State.ArmyById("a")!; var b = session.State.ArmyById("b")!;
            Check(a.Units.Select(u => u.Name).SequenceEqual(new[] { "A1" }) && b.Units[^1].Name == "A2",
                "OK applies exactly the intended deletion and reindexed unit transfer");
            Check(a.Money == 800 && b.Money == 1300 && a.SupplyTons == supplyA && b.SupplyTons == supplyB,
                "mixed commit agrees with the production resource preview");
            Check(events == 1, "Transfer publishes one completed commit");
        }
        else Check(session.State == before && events == 0, "Transfer Cancel discards deletion and all other staging");
        RemoveChild(dialog); dialog.QueueFree(); await Settle();
    }

    private async Task Split(bool commit)
    {
        var session = Build(); var before = session.State; var events = 0; session.EventsPublished += _ => events++;
        var dialog = new SplitArmyDialog { Session = session, ArmyId = "a", NewArmyId = "new-army" };
        AddChild(dialog);
        dialog.StageUnitForCheck(2); dialog.PressMoneyForCheck(100); dialog.PressSupplyForCheck(10);
        dialog.ArmyListForCheck.Select(0); PressFirstDisband(dialog);
        Check(dialog.GetChildren().OfType<ConfirmPrompt>().Any(), "Split Disband asks before changing its working copy");
        dialog.GetChildren().OfType<ConfirmPrompt>().Single().AnswerForCheck(true);
        await Settle();
        Check(session.State == before && events == 0 && dialog.ModelForCheck.StagedUnits.SequenceEqual(new[] { 1 }),
            "Split deletion stays private and preserves the surviving staged identity");
        if (commit) dialog.OkForCheck(); else dialog.CancelForCheck();
        if (commit)
        {
            var parent = session.State.ArmyById("a")!; var child = session.State.ArmyById("new-army");
            Check(child is not null && child.Units.Single().Name == "A2" && parent.Units.Single().Name == "A1",
                "OK creates the intended split after the pending deletion");
            Check(child is not null && parent.Money + child.Money == 900 && parent.SupplyTons + child.SupplyTons == 206,
                "Split commit conserves remaining stores and money");
            Check(events == 1, "Split publishes one completed commit");
        }
        else Check(session.State == before && events == 0, "Split Cancel now discards confirmed disband too");
        RemoveChild(dialog); dialog.QueueFree(); await Settle();
    }

    private async Task DisbandOnlySplit()
    {
        var session = Build(); var count = session.State.Armies.Count;
        var dialog = new SplitArmyDialog { Session = session, ArmyId = "a", NewArmyId = "unused-new" };
        AddChild(dialog);
        dialog.ArmyListForCheck.Select(0); PressFirstDisband(dialog);
        dialog.GetChildren().OfType<ConfirmPrompt>().Single().AnswerForCheck(true);
        await Settle();
        var ok = dialog.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == "OK");
        Check(!ok.Disabled, "valid pending deletions can be confirmed without an empty new army");
        ok.EmitSignal(Button.SignalName.Pressed);
        Check(session.State.ArmyById("a")!.Units.Count == 2 && session.State.Armies.Count == count
            && session.State.ArmyById("unused-new") is null, "disband-only OK commits no empty split record");
        RemoveChild(dialog); dialog.QueueFree(); await Settle();
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message); _ok &= condition;
    }
}
