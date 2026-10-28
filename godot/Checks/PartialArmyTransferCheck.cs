using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class PartialArmyTransferCheck : Node
{
    private bool _ok = true;
    public override void _Ready()
    {
        try
        {
            foreach (var back in new[] { false, true })
            foreach (var cancel in new[] { false, true })
            foreach (var mixed in new[] { false, true }) Run(back, cancel, mixed);
        }
        catch (Exception ex) { GD.PrintErr($"PartialArmyTransferCheck: {ex}"); _ok = false; }
        GD.Print($"PartialArmyTransferCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private void Run(bool back, bool cancel, bool mixed)
    {
        var data = GameDataContext.Repository.Resolve("toy-3city");
        var original = new GameSession(data.World, data.Ruleset, data.Scenario);
        var template = original.State.Armies.First(a => a.Nation == original.State.ActiveNationId);
        UnitSlot Unit(string name, int troops) => new(0, "light_infantry", troops, 6, name);
        var donor = template with { Id = "partial-donor", X = 3, Y = 2, SupplyTons = 0, Money = 100,
            Moves = 7, AboardFleetId = null, Units = mixed
                ? ValueList.Of(Unit("D0", 100), Unit("D1", 100), Unit("D2", 500), Unit("D3", 100))
                : ValueList.Of(Unit("D0", 100), Unit("D1", 100), Unit("D2", 100)) };
        var receiver = donor with { Id = "partial-receiver", X = 4, Moves = 6, Money = 200,
            AboardFleetId = mixed ? "partial-carrier" : null,
            Units = ValueList.From(Enumerable.Range(0, 19).Select(i => Unit($"R{i}", mixed && i == 18 ? 13200 : 100))) };
        var fleet = new FleetState("partial-carrier", donor.Nation, receiver.X, receiver.Y, 5, 30, 100, 0, 0,
            null, null, receiver.Id, null);
        var state = original.State with { Armies = ValueList.Of(donor, receiver),
            Fleets = mixed ? ValueList.Of(fleet) : ValueList<FleetState>.Empty };
        var save = new SaveGame(state.SchemaVersion, "partial-check", "partial-check", state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(data.World, data.Ruleset, data.Scenario, save);
        var commands = 0;
        var dialog = new ArmyTransferDialog { Session = session,
            ArmyId = back ? receiver.Id : donor.Id, PartnerId = back ? donor.Id : receiver.Id,
            CommitEdits = (draft, final) => { var result = draft.Commit(final); if (result.Accepted) commands++; return result; } };
        AddChild(dialog);
        var list = back ? dialog.PartnerListForCheck : dialog.SelectedListForCheck;
        Check(list.SelectMode == ItemList.SelectModeEnum.Multi, "unit list supports multi-selection");
        list.Select(0);
        list.Select(1, false);
        if (mixed) list.Select(2, false);
        dialog.DisbandSelectedForCheck(!back);
        Check(commands == 0 && session.State == state && dialog.ReplyForCheck == "Select one unit to disband.",
            "multi-selection does not accidentally disband the first row");
        dialog.TransferSelectedForCheck(!back);
        var working = back ? dialog.ModelForCheck.SelectedWorkingUnits : dialog.ModelForCheck.PartnerWorkingUnits;
        Check(working.Count == 20 && working[^1].Unit.Name == "D1", "highest fitting selected row appends while refused rows stay");
        Check(dialog.ReplyForCheck.StartsWith("This army already has 20 units.", StringComparison.Ordinal)
              && (!mixed || dialog.ReplyForCheck.EndsWith("This fleet can not carry any more troops.", StringComparison.Ordinal)),
            "one or both applicable refusal classes are visible in measured order");
        Check(commands == 0 && session.State == state, "staging changes no live state and submits nothing");
        var expectedSelected = dialog.ModelForCheck.SelectedWorkingUnits.Select(u => u.Unit).ToArray();
        var expectedPartner = dialog.ModelForCheck.PartnerWorkingUnits.Select(u => u.Unit).ToArray();
        if (cancel)
        {
            dialog.CancelForCheck();
            Check(commands == 0 && session.State == state, "Cancel discards accepted and refused staging alike");
        }
        else
        {
            dialog.OkForCheck();
            Check(commands == 1 && !dialog.ReplyForCheck.Contains("rejected", StringComparison.Ordinal), "OK sends one accepted command");
            Check(session.State.ArmyById(dialog.ArmyId)!.Units.SequenceEqual(expectedSelected)
                  && session.State.ArmyById(dialog.PartnerId)!.Units.SequenceEqual(expectedPartner), "committed ordered rosters match both working lists");
            Check(session.State.ArmyById(donor.Id)!.Moves == 7 && session.State.ArmyById(receiver.Id)!.Moves == 0,
                "only the receiving army loses movement");
            Check(session.State.Armies.Sum(a => a.TotalTroops) == state.Armies.Sum(a => a.TotalTroops)
                  && session.State.Armies.Sum(a => a.Money) == 300, "all troops and money are conserved");
            if (mixed) Check(session.State.FleetById(fleet.Id) == fleet, "carrier state/link is unchanged");
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
