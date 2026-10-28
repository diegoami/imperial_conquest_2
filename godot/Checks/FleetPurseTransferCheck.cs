using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class FleetPurseTransferCheck : Node
{
    private bool _ok = true;
    public override void _Ready()
    {
        try
        {
            foreach (var back in new[] { false, true })
            foreach (var money in new[] { 950, 1000, 1100 })
            foreach (var cancel in new[] { false, true }) Run(back, money, cancel);
            Run(false, 1100, false, shipsOnly: true);
        }
        catch (Exception ex) { GD.PrintErr($"FleetPurseTransferCheck: {ex}"); _ok = false; }
        GD.Print($"FleetPurseTransferCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private void Run(bool back, int receiverMoney, bool cancel, bool shipsOnly = false)
    {
        var data = GameDataContext.Repository.Resolve("toy-3city");
        var original = new GameSession(data.World, data.Ruleset, data.Scenario);
        var template = original.State.Fleets.First(f => f.Nation == original.State.ActiveNationId);
        var a = template with { Id = "purse-a", X = 3, Y = 3, Ships = 10, Money = back ? receiverMoney : 200,
            CarriedArmyId = null, ConstructionTicksRemaining = null };
        var b = a with { Id = "purse-b", X = 4, Money = back ? 200 : receiverMoney };
        var state = original.State with { Fleets = ValueList.Of(a, b) };
        var save = new SaveGame(state.SchemaVersion, "fleet-purse-check", "fleet-purse-check", state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(data.World, data.Ruleset, data.Scenario, save);
        var commands = 0;
        var dialog = new FleetTransferDialog { Session = session, FleetId = a.Id, PartnerId = b.Id,
            CommitEdits = (draft, final) => { commands++; return draft.Commit(final); } };
        AddChild(dialog);
        var step = receiverMoney == 950 ? 50 : receiverMoney == 1100 ? -100 : 0;
        if (shipsOnly) dialog.AdjustShipsForCheck(1);
        else
        {
            dialog.AdjustMoneyForCheck(back ? -100 : 100);
            dialog.AdjustMoneyForCheck(back ? -100 : 100);
            Check(dialog.ModelForCheck.MoneyNet == (back ? -step : step), "repeated real arrows preserve signed working admission");
        }
        Check(commands == 0 && session.State == state, "staging does not change the campaign");
        if (cancel)
        {
            dialog.CancelForCheck();
            Check(commands == 0 && session.State == state, "Cancel discards all staging");
        }
        else
        {
            dialog.OkForCheck();
            Check(commands == (shipsOnly || step != 0 ? 1 : 0) && !dialog.ReplyForCheck.Contains("rejected", StringComparison.Ordinal),
                "OK submits exactly the admitted allocation or ship-only command");
            var donor = session.State.FleetById(back ? b.Id : a.Id)!;
            var receiver = session.State.FleetById(back ? a.Id : b.Id)!;
            Check(donor.Money == (shipsOnly ? 200 : 200 - step)
                  && receiver.Money == (shipsOnly ? receiverMoney : 1000), "committed purses match signed arrows; ship-only leaves money intact");
            Check(session.State.Nations == state.Nations && session.State.Fleets.Sum(f => f.Money) == a.Money + b.Money,
                "money is conserved between fleets without a treasury sweep");
        }
        RemoveChild(dialog);
        dialog.QueueFree();
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
