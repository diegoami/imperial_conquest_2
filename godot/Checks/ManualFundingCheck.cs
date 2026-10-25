using Godot;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>T219: real supply dialogs and button callbacks on public synthetic accounts.</summary>
public partial class ManualFundingCheck : Node
{
    private bool _ok = true;
    private const string Provider = "funding-provider";

    public override void _Ready()
    {
        try
        {
            foreach (var fleet in new[] { false, true })
            {
                foreach (var treasury in new[] { -100, 0, 50 }) CheckTransfer(fleet, treasury, via: false);
                CheckTransfer(fleet, -100, via: true);
            }
        }
        catch (Exception ex) { GD.PrintErr($"ManualFundingCheck: {ex}"); _ok = false; }
        GD.Print($"ManualFundingCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private static GameSession Session(int treasury)
    {
        var data = GameDataContext.Repository.Resolve("toy-3city");
        var initial = new GameSession(data.World, data.Ruleset, data.Scenario).State;
        var targetFleet = initial.FleetById("north-fleet-1")!;
        var state = initial with
        {
            Nations = ValueList.From(initial.Nations.Select(n => n.Id == "north"
                ? n with { Treasury = treasury, Wealth = 100_000 } : n)),
            Armies = ValueList.From(initial.Armies.Select(a => a.Id == "north-army-1"
                ? a with { X = 1, Y = 3, Money = 100 } : a)),
            Fleets = ValueList.From(initial.Fleets.Select(f => f.Id == targetFleet.Id ? f with { Money = 100 } : f)
                .Append(targetFleet with { Id = Provider, Money = 50, SupplyTons = 100 })),
        };
        return new GameSession(data.World, data.Ruleset, data.Scenario,
            new SaveGame(state.SchemaVersion, "manual-funding", "manual-funding", state.ScenarioId, state.WorldId, state.RulesetId, state));
    }

    private void CheckTransfer(bool fleet, int treasury, bool via)
    {
        var session = Session(treasury);
        var main = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
        AddChild(main);
        var unitId = fleet ? "north-fleet-1" : "north-army-1";
        if (fleet) main.SelectFleetForCheck(unitId); else main.SelectArmyForCheck(unitId);
        main.MenuBar.PressItemForCheck(fleet ? "unit_map.fleet_supply" : "unit_map.army_supply");
        if (main.ActiveOverlay is not SupplyDialog dialog)
            throw new InvalidOperationException("Production supply menu did not open its dialog.");
        dialog.SelectProviderForCheck(Provider);
        Check(dialog.ModelForCheck.SelectedViaFleetId is null,
            "choosing a provisions fleet does not change the money source from treasury");
        if (via)
        {
            var picker = dialog.FindChildren("*", "OptionButton", true, false).OfType<OptionButton>().Single();
            var index = dialog.ModelForCheck.MoneyViaChoices.ToList().FindIndex(choice => choice.FleetId == Provider);
            Check(index >= 0, "explicit money-source picker retains the fleet extension");
            picker.Select(index);
            picker.EmitSignal(OptionButton.SignalName.ItemSelected, index);
        }
        var count = 0;
        main.CommandIssued += _ => count++;
        var before = session.State;
        dialog.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == "+100")
            .EmitSignal(Button.SignalName.Pressed);
        var expectedMoved = via ? 50 : 100;
        Check(count == 1, "real +100 button submits exactly one command");
        Check(dialog.ModelForCheck.UnitMoney == 100 + expectedMoved
              && dialog.ModelForCheck.NationalTreasury == (via ? treasury : treasury - 100),
            "dialog refreshes the actual purse and treasury balances after funding");
        Check(session.State.FleetById(Provider)!.Money == (via ? 0 : 50),
            "only an explicitly chosen fleet purse funds the extended transfer");
        Check(dialog.ReplyForCheck.Contains($"received {expectedMoved}", StringComparison.Ordinal),
            "visible reply reports the actual transferred amount");
        Check(session.State.RandomSeed == RngStreams.Advance(before.RandomSeed)
              && session.State.Calendar == before.Calendar, "funding keeps existing command seed bookkeeping and does not advance time");
        var targetMoney = session.State.ArmyById(unitId)?.Money ?? session.State.FleetById(unitId)!.Money;
        Check(treasury + 100 + 50 == session.State.NationById("north")!.Treasury + targetMoney + session.State.FleetById(Provider)!.Money,
            "all three accounts conserve their total");
        RemoveChild(main);
        main.QueueFree();
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
