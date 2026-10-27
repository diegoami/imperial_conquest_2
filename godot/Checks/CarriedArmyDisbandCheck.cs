using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class CarriedArmyDisbandCheck : Node
{
    private bool _ok = true;
    public override void _Ready()
    {
        try
        {
            foreach (var confirm in new[] { false, true }) Run(confirm);
        }
        catch (Exception ex) { GD.PrintErr($"CarriedArmyDisbandCheck: {ex}"); _ok = false; }
        GD.Print($"CarriedArmyDisbandCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private void Run(bool confirm)
    {
        var data = GameDataContext.Repository.Resolve("toy-3city");
        var original = new GameSession(data.World, data.Ruleset, data.Scenario);
        var city = original.State.Cities.First(c => c.Owner == original.State.ActiveNationId) with { SupplyTons = 330 };
        var army = original.State.Armies.First(a => a.Nation == city.Owner) with { Id = "test-cargo", X = city.X + 1, Y = city.Y,
            AboardFleetId = "test-carrier", CoveredTileCode = null, Money = 37, SupplyTons = 23,
            Units = ValueList.Of(new UnitSlot(0, "light_infantry", 5000, 6, "Cargo")) };
        var fleet = original.State.Fleets.First(f => f.Nation == city.Owner) with { Id = "test-carrier", X = army.X, Y = army.Y,
            Ships = 10, Moves = 7, CarriedArmyId = army.Id, ConstructionTicksRemaining = null };
        var state = original.State with { Armies = ValueList.Of(army), Fleets = ValueList.Of(fleet),
            Cities = ValueList.From(original.State.Cities.Select(c => c.Id == city.Id ? city : c)),
            Nations = ValueList.From(original.State.Nations.Select(n => n.Id == city.Owner ? n with { Treasury = 1730 } : n)) };
        var snapshot = new SaveGame(state.SchemaVersion, "cargo-dialog", "cargo-dialog", state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(data.World, data.Ruleset, data.Scenario, snapshot);
        // The standalone last-unit safeguard remains separate from whole-army disband.
        var guarded = session.Submit($"disband-unit {army.Id} 0");
        Check(guarded.Lines.Any(l => l.Contains("rejected", StringComparison.Ordinal)) && session.State == state,
            "last-unit disband remains refused without changing cargo");
        var main = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot,
            Size = GetViewport().GetVisibleRect().Size };
        AddChild(main);
        main.SelectFleetForCheck(fleet.Id);
        var commands = 0;
        main.CommandIssued += _ => commands++;
        main.MenuBar.PressItemForCheck("unit_map.army_disband");
        var prompt = (ConfirmPrompt)main.ActiveOverlay!;
        Check(prompt.Question.Contains("carried army", StringComparison.Ordinal), "Army command explicitly identifies cargo in its confirmation");
        Check(commands == 0 && session.State == state, "opening confirmation changes nothing");
        prompt.FindChildren("*", "Button", true, false).OfType<Button>()
            .Single(b => b.Text.Equals(confirm ? "Yes" : "No", StringComparison.OrdinalIgnoreCase))
            .EmitSignal(Button.SignalName.Pressed);
        if (confirm)
        {
            Check(commands == 1 && session.State.ArmyById(army.Id) is null, "Yes dispatches exactly one whole-army disband");
            Check(session.State.FleetById(fleet.Id) == fleet with { CarriedArmyId = null }, "carrier remains intact with its cargo link cleared");
            Check(session.State.NationById(army.Nation)!.Treasury == 1767
                  && session.State.CityById(city.Id)!.SupplyTons == 353, "whole purse and supply refunds reach treasury and town");
        }
        else Check(commands == 0 && session.State == state, "No preserves the entire carried-army state");
        RemoveChild(main); main.QueueFree();
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
