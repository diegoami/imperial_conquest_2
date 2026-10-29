using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// #1549 N4: the Supply army dialog's real money buttons with another own army as the money source. An
/// arrow that moves nothing (empty giver, full receiver) submits no command, leaves stocks, purses and
/// the random seed alone, and shows the no-money feedback; an arrow that moves money still submits one
/// <c>army-transfer</c>.
/// </summary>
public partial class SupplyArmyMoneySourceCheck : Node
{
    private const string NoMoneyMoved = "No money moved: source empty or receiving purse full.";
    private bool _ok = true;

    public override void _Ready()
    {
        try
        {
            Run(selectedMoney: 100, sourceMoney: 0, button: "+100", moves: false, "empty source army");
            Run(selectedMoney: 0, sourceMoney: 100, button: "-100", moves: false, "empty selected army");
            Run(selectedMoney: 32767, sourceMoney: 100, button: "+100", moves: false, "selected purse full");
            Run(selectedMoney: 100, sourceMoney: 32767, button: "-100", moves: false, "source purse full");
            Run(selectedMoney: 100, sourceMoney: 300, button: "+100", moves: true, "money available");
        }
        catch (Exception ex) { GD.PrintErr($"SupplyArmyMoneySourceCheck: {ex}"); _ok = false; }
        GD.Print($"SupplyArmyMoneySourceCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private void Run(int selectedMoney, int sourceMoney, string button, bool moves, string label)
    {
        var data = GameDataContext.Repository.Resolve("toy-3city");
        var original = new GameSession(data.World, data.Ruleset, data.Scenario);
        var template = original.State.Armies.First(a => a.Nation == original.State.ActiveNationId);
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 10000, 6, "Regulars"));
        // 206 t on a 100 t army: an unchanged army-transfer would rebalance to 106 / 100.
        var selected = template with { Id = "supply-money-a", X = 3, Y = 2, SupplyTons = 206, Money = selectedMoney,
            AboardFleetId = null, Units = units };
        var source = selected with { Id = "supply-money-b", X = 4, SupplyTons = 0, Money = sourceMoney };
        var state = original.State with { Armies = ValueList.Of(selected, source), Fleets = ValueList<FleetState>.Empty };
        var save = new SaveGame(state.SchemaVersion, "supply-money-check", "supply-money-check",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(data.World, data.Ruleset, data.Scenario, save);
        var commands = 0;
        var dialog = new SupplyDialog { Session = session, ArmyId = selected.Id,
            Submit = line => { commands++; return session.Submit(line).Lines; } };
        AddChild(dialog);
        dialog.SelectProviderForCheck(source.Id);
        Check(dialog.ModelForCheck.SelectedViaArmyId == source.Id, $"{label}: the army provider is the money source");
        var before = session.State;
        dialog.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == button)
            .EmitSignal(BaseButton.SignalName.Pressed);
        var a = session.State.ArmyById(selected.Id)!;
        var b = session.State.ArmyById(source.Id)!;
        if (moves)
        {
            Check(commands == 1 && a.Money == selectedMoney + 100 && b.Money == sourceMoney - 100
                  && dialog.ReplyForCheck != NoMoneyMoved,
                $"{label}: {button} submits one army-transfer that moves 100");
        }
        else
        {
            Check(commands == 0 && session.State == before && a.SupplyTons == 206 && b.SupplyTons == 0
                  && a.Money == selectedMoney && b.Money == sourceMoney
                  && session.State.RandomSeed == before.RandomSeed,
                $"{label}: {button} submits nothing and changes no stock, purse or random seed");
            Check(dialog.ReplyForCheck == NoMoneyMoved, $"{label}: the no-money feedback is shown");
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
