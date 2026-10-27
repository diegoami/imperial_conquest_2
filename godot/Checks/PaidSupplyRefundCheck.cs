using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class PaidSupplyRefundCheck : Node
{
    private bool _ok = true;
    public override void _Ready()
    {
        try
        {
            foreach (var fleet in new[] { false, true })
            foreach (var purchase in new[] { false, true }) Run(fleet, purchase);
        }
        catch (Exception ex) { GD.PrintErr($"PaidSupplyRefundCheck: {ex}"); _ok = false; }
        GD.Print($"PaidSupplyRefundCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private void Run(bool fleet, bool purchase)
    {
        var data = GameDataContext.Repository.Resolve("toy-3city");
        var original = new GameSession(data.World, data.Ruleset, data.Scenario);
        var buyer = original.State.ActiveNationId;
        var army = original.State.Armies.First(a => a.Nation == buyer) with {
            X = 3, Y = 2, SupplyTons = 206, Money = 100, AboardFleetId = null,
            Units = ValueList.Of(new UnitSlot(0, "light_infantry", 10000, 6, "Regulars")) };
        var ship = original.State.Fleets.First(f => f.Nation == buyer) with {
            X = 3, Y = 2, Ships = 20, SupplyTons = 265, Money = 100,
            CarriedArmyId = null, ConstructionTicksRemaining = null };
        var city = original.State.Cities.First(c => c.Owner != buyer) with { X = 2, Y = 2, SupplyTons = 330 };
        var state = original.State with { Armies = ValueList.Of(army), Fleets = ValueList.Of(ship), Cities = ValueList.Of(city),
            Relations = original.State.Relations.WithRelation(buyer, city.Owner, data.Ruleset.Diplomacy.StateCodes.Peace)
                .WithRelation(city.Owner, buyer, data.Ruleset.Diplomacy.StateCodes.Peace) };
        var save = new SaveGame(state.SchemaVersion, "paid-refund-check", "paid-refund-check",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(data.World, data.Ruleset, data.Scenario, save);
        var commands = 0;
        var dialog = new SupplyDialog { Session = session, ArmyId = fleet ? null : army.Id, FleetId = fleet ? ship.Id : null,
            Submit = line => { commands++; return session.Submit(line).Lines; } };
        AddChild(dialog);
        var provider = dialog.ModelForCheck.Providers.ToList().FindIndex(p => p.Id == city.Id);
        Check(provider >= 0, "eligible foreign provider is listed");
        dialog.SelectProviderForCheck(city.Id);
        dialog.AdjustStagedForCheck(100);
        Check(commands == 0 && session.State == state && dialog.ModelForCheck.StagedTons == -105,
            "paid return stages without a live mutation");
        Check(dialog.FindChildren("*", "Label", true, false).OfType<Label>()
            .Any(l => l.Text == "Return 105 t  ·  Refund 21 talents"), "return and refund are visible inside the dialog");
        if (purchase)
        {
            dialog.TransferForCheck();
            Check(commands == 1 && !dialog.ReplyForCheck.Contains("Refused:"), "one real purchase command succeeds");
            Check((fleet ? session.State.FleetById(ship.Id)!.Money : session.State.ArmyById(army.Id)!.Money) == 121,
                "target purse receives its refund");
            Check(session.State.CityById(city.Id)!.SupplyTons == 435
                  && session.State.NationById(city.Owner)!.Treasury == state.NationById(city.Owner)!.Treasury - 21,
                "foreign city receives supplies and its owner funds the refund");
            Check(dialog.ModelForCheck.StagedTons == 0, "purchase refresh clears staging");
        }
        var beforeClose = session.State;
        dialog.Close();
        Check(session.State == beforeClose && commands == (purchase ? 1 : 0), "Close preserves the appropriate pre/post-purchase state");
        RemoveChild(dialog);
        dialog.QueueFree();
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
