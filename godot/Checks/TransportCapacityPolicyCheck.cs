using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class TransportCapacityPolicyCheck : Node
{
    private bool _ok = true;

    public override void _Ready()
    {
        try
        {
            foreach (var faithful in new[] { true, false })
            foreach (var troops in new[] { 5000, 5001 }) Run(faithful, troops);
        }
        catch (Exception ex) { GD.PrintErr($"TransportCapacityPolicyCheck: {ex}"); _ok = false; }
        GD.Print($"TransportCapacityPolicyCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private void Run(bool faithful, int troops)
    {
        var data = GameDataContext.Repository.Resolve("toy-3city");
        var ruleset = data.Ruleset with { Flags = data.Ruleset.Flags with {
            SeatAsymmetry = faithful ? SeatAsymmetryModel.Faithful : SeatAsymmetryModel.Normalized } };
        var initial = new GameSession(data.World, ruleset, data.Scenario);
        var army = initial.State.Armies.First(a => a.Nation == initial.State.ActiveNationId) with {
            Money = 100, SupplyTons = 100, CoveredTileCode = null, AboardFleetId = "policy-carrier",
            Units = ValueList.Of(new UnitSlot(0, "light_infantry", troops, 6, "Regulars")) };
        var fleet = new FleetState("policy-carrier", army.Nation, army.X, army.Y, 5, 10, 100,
            0, 0, null, null, army.Id, null);
        var city = initial.State.Cities.First(c => c.Owner == army.Nation);
        var state = initial.State with { Armies = ValueList.Of(army), Fleets = ValueList.Of(fleet),
            MercenaryPool = ValueList.Of(new MercenaryPoolSlot(9, city.X, city.Y, 3, "light_infantry", 1, 5)) };
        var save = new SaveGame(state.SchemaVersion, "transport-policy", "transport-policy", state.ScenarioId,
            state.WorldId, state.RulesetId, state);
        var session = new GameSession(data.World, ruleset, data.Scenario, save);
        var main = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot,
            Size = GetViewport().GetVisibleRect().Size };
        AddChild(main);
        main.SelectFleetForCheck(fleet.Id);
        Check(main.MenuBar.PressItemForCheck("unit_map.army_recruit_mercenaries"), "actual cargo hire menu is available");
        Check(session.State == state, "opening leaves loaded cargo and campaign unchanged");
        var shouldOpen = faithful || troops == 5000;
        Check((main.ActiveOverlay is RecruitMercenariesDialog) == shouldOpen,
            "current-troop opening respects Faithful rounding and Improved exact capacity");
        if (main.ActiveOverlay is RecruitMercenariesDialog dialog)
        {
            dialog.SelectOfferForCheck(9);
            dialog.HireSelectedForCheck();
            Check(session.State.ArmyById(army.Id)!.TotalTroops == (faithful ? troops + 1 : troops),
                "actual hire uses resulting troops and preserves an exact-capacity refusal");
            Check(session.State.FleetById(fleet.Id)!.CarriedArmyId == army.Id,
                "hire leaves the carrier link intact");
        }
        else Check(main.LastMessageForCheck.Contains("fleet", StringComparison.OrdinalIgnoreCase),
            "an over-capacity opening gives explicit fleet feedback");
        RemoveChild(main); main.QueueFree();
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
