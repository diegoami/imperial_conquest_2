using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>T222: actual transfer dialog OK/Cancel, signed arrows and live previews.</summary>
public partial class ArmyTransferConfirmationCheck : Node
{
    private bool _ok = true;

    public override void _Ready()
    {
        try
        {
            foreach (var reverse in new[] { false, true })
            foreach (var cancel in new[] { false, true })
                Run(reverse, cancel, moneyArrow: false);
            foreach (var reverse in new[] { false, true })
            foreach (var cancel in new[] { false, true })
                Run(reverse, cancel, moneyArrow: true);
            Run(reverse: false, cancel: false, moneyArrow: true, rejected: true);
            // #1549 N2: a receiver above 32,767 (reachable through an emptied-army merge, N1) is where the
            // T231 no-reversal floor differs from the original signed arrow; both directions.
            foreach (var overFieldSelected in new[] { false, true })
                RunTowardOverFieldReceiver(overFieldSelected);
        }
        catch (Exception ex) { GD.PrintErr($"ArmyTransferConfirmationCheck: {ex}"); _ok = false; }
        GD.Print($"ArmyTransferConfirmationCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private void Run(bool reverse, bool cancel, bool moneyArrow, bool rejected = false)
    {
        var data = GameDataContext.Repository.Resolve("toy-3city");
        var original = new GameSession(data.World, data.Ruleset, data.Scenario);
        var template = original.State.Armies.First(a => a.Nation == original.State.ActiveNationId);
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 10000, 6, "Regulars"));
        var a = template with { Id = "confirmation-a", X = 3, Y = 2, SupplyTons = 206,
            Money = moneyArrow ? 900 : 100, AboardFleetId = null, Units = units };
        var b = a with { Id = "confirmation-b", X = rejected ? 8 : 4, SupplyTons = 0, Money = moneyArrow ? 1200 : 100 };
        var state = original.State with { Armies = ValueList.Of(a, b), Fleets = ValueList<FleetState>.Empty };
        var save = new SaveGame(state.SchemaVersion, "confirmation-check", "confirmation-check",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(data.World, data.Ruleset, data.Scenario, save);
        var commands = 0;
        var dialog = new ArmyTransferDialog { Session = session,
            ArmyId = reverse ? b.Id : a.Id, PartnerId = reverse ? a.Id : b.Id,
            CommitEdits = (draft, final) => { var result = draft.Commit(final); if (result.Accepted) commands++; return result; } };
        AddChild(dialog);
        Check(commands == 0 && session.State == state, "opening and preview do not change live state");
        if (rejected)
        {
            var closed = false;
            dialog.Closed += () => closed = true;
            dialog.PressMoneyForCheck(100);
            var reason = dialog.ModelForCheck.PreviewRejection;
            Check(reason is { Length: > 0 } && !dialog.ConfirmationEnabledForCheck
                  && dialog.PreviewNoticeForCheck == reason,
                "a rejected preview shows its own reason (not the pending caption) and disables OK");
            Check(dialog.ModelForCheck.SelectedMoneyAfter == 900 && dialog.ModelForCheck.PartnerMoneyAfter == 1200,
                "a rejected preview shows unchanged live purses");
            dialog.OkForCheck();
            Check(!closed && commands == 0 && session.State == state,
                "invalid confirmation stays open without submitting or changing state");
            Check(dialog.ReplyForCheck == reason, "a refused OK repeats the preview's reason");
            RemoveChild(dialog); dialog.QueueFree();
            return;
        }
        Check(dialog.ModelForCheck.SelectedSupplyAfter == 106 && dialog.ModelForCheck.PartnerSupplyAfter == 100,
            "unchanged OK preview uses the selected-first rebalance");
        if (moneyArrow)
        {
            dialog.PressMoneyForCheck(reverse ? -100 : 100);
            dialog.PressMoneyForCheck(reverse ? -100 : 100);
            Check(dialog.ModelForCheck.MoneyNet == (reverse ? -200 : 200)
                  && dialog.ModelForCheck.SelectedMoneyAfter == (reverse ? 1400 : 700)
                  && dialog.ModelForCheck.PartnerMoneyAfter == (reverse ? 700 : 1400),
                "real arrows move in the requested direction beyond 1,000 after repeated presses");
        }
        if (cancel)
        {
            dialog.CancelForCheck();
            Check(commands == 0 && session.State == state, "Cancel submits nothing and discards all working changes");
        }
        else
        {
            dialog.OkForCheck();
            Check(commands == 1 && !dialog.ReplyForCheck.Contains("rejected", StringComparison.OrdinalIgnoreCase), "OK submits one accepted command, including without edits");
            Check(session.State.ArmyById(a.Id)!.SupplyTons == (reverse ? 100 : 106)
                  && session.State.ArmyById(b.Id)!.SupplyTons == (reverse ? 106 : 100),
                "committed supplies match the selected-first preview");
            Check(session.State.ArmyById(a.Id)!.Money == (moneyArrow ? 700 : 100)
                  && session.State.ArmyById(b.Id)!.Money == (moneyArrow ? 1400 : 100),
                "committed purse changes match the working previews");
        }
        RemoveChild(dialog);
        dialog.QueueFree();
    }

    private void RunTowardOverFieldReceiver(bool overFieldSelected)
    {
        var data = GameDataContext.Repository.Resolve("toy-3city");
        var original = new GameSession(data.World, data.Ruleset, data.Scenario);
        var template = original.State.Armies.First(a => a.Nation == original.State.ActiveNationId);
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 10000, 6, "Regulars"));
        var over = template with { Id = "over-field", X = 3, Y = 2, SupplyTons = 0, Money = 60000,
            AboardFleetId = null, Units = units };
        var other = over with { Id = "under-field", X = 4, Money = 100 };
        var state = original.State with { Armies = ValueList.Of(over, other), Fleets = ValueList<FleetState>.Empty };
        var save = new SaveGame(state.SchemaVersion, "over-field-check", "over-field-check",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(data.World, data.Ruleset, data.Scenario, save);
        var commands = 0;
        var dialog = new ArmyTransferDialog { Session = session,
            ArmyId = overFieldSelected ? over.Id : other.Id, PartnerId = overFieldSelected ? other.Id : over.Id,
            CommitEdits = (draft, final) => { var result = draft.Commit(final); if (result.Accepted) commands++; return result; } };
        AddChild(dialog);
        var toward = overFieldSelected ? -100 : 100;
        dialog.PressMoneyForCheck(toward);
        dialog.PressMoneyForCheck(toward);
        Check(dialog.ModelForCheck.MoneyNet == 0 && dialog.ModelForCheck.PreviewRejection is null
              && dialog.ConfirmationEnabledForCheck,
            $"an arrow toward a purse above 32,767 moves nothing and never reverses ({(overFieldSelected ? "selected" : "partner")} over the field)");
        dialog.OkForCheck();
        Check(commands == 1 && session.State.ArmyById(over.Id)!.Money == 60000
              && session.State.ArmyById(other.Id)!.Money == 100,
            "OK after the no-movement arrow commits both purses unchanged");
        RemoveChild(dialog);
        dialog.QueueFree();
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
