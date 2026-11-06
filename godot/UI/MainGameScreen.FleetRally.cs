using Godot;
using IC2.Engine.Model;

namespace IC2.Slice.UI;

public partial class MainGameScreen
{
    private HBoxContainer _fleetRallyRow = null!;
    private Button _fleetRallySetButton = null!, _fleetRallyClearButton = null!;
    private bool _rallyPointPickIsFleet, _runningFleetRally;

    private void BuildFleetRallyControls()
    {
        _fleetRallyRow = new HBoxContainer();
        _fleetRallySetButton = new Button { FocusMode = FocusModeEnum.None };
        QolCommandIcons.Apply(_fleetRallySetButton, "fleet_rally", "Set fleet rally point (experimental)");
        _fleetRallySetButton.Pressed += () => ArmNationRallyPointPick(isFleet: true);
        _fleetRallyClearButton = new Button { FocusMode = FocusModeEnum.None };
        QolCommandIcons.Apply(_fleetRallyClearButton, "fleet_rally_clear", "Clear fleet rally point");
        _fleetRallyClearButton.Pressed += () => ClearNationRallyPoint(isFleet: true);
        _fleetRallyRow.AddChild(_fleetRallySetButton); _fleetRallyRow.AddChild(_fleetRallyClearButton);
        Toolbar.ExtraActions.AddChild(_fleetRallyRow);
    }

    private void RefreshFleetRallyControls()
    {
        var nation = Session.State.ActiveNationId;
        _fleetRallyRow.Visible = Session.State.NationById(nation)?.Control == SeatControl.Human;
        var point = Session.RallyPointFor(nation, isFleet: true);
        _fleetRallyClearButton.Visible = point is not null;
        UiKit.SetHint(_fleetRallySetButton, point is null ? "Set fleet rally point (experimental)"
            : $"Change fleet rally point: {point.CityId ?? $"({point.X}, {point.Y})"}", _hintsEnabled);
        UiKit.SetHint(_fleetRallyClearButton, "Clear fleet rally point for future launches", _hintsEnabled);
    }

    private void SetFleetRallyPoint(string nation, int x, int y, string? cityId)
    {
        var point = new NationRallyPoint(nation, x, y, cityId, IsFleet: true);
        if (FleetRallyPlanner.ValidatePoint(Session.State, Session.World, point) is { } reason)
            ShowScreenMessage($"Fleet rally point refused: {reason}.");
        else
        {
            Session.SetRallyPoint(point);
            ShowScreenMessage($"Fleet rally point set for future {nation} launches.");
        }
        UpdateCommandStrip();
    }

    private string? RunFleetRallyOrders()
    {
        var nation = Session.State.ActiveNationId;
        if (Session.State.NationById(nation)?.Control != SeatControl.Human) return null;
        var stopped = new List<string>();
        if (Session.RallyPointFor(nation, isFleet: true) is { } point
            && FleetRallyPlanner.ValidatePoint(Session.State, Session.World, point) is { } invalid)
        {
            Session.ClearRallyPoint(nation, isFleet: true);
            stopped.Add($"fleet rally point: {invalid}");
        }
        if (Session.FleetRallyOrders.Count == 0 && stopped.Count == 0) return null;
        var moved = 0;
        var orders = Session.FleetRallyOrders.Where(o => o.NationId == nation).ToArray();
        var ids = Session.State.Fleets.Select(f => f.Id).Concat(orders.Select(o => o.FleetId)).Distinct();
        _runningFleetRally = true;
        try
        {
            foreach (var id in ids.ToArray())
            {
                var order = orders.FirstOrDefault(o => o.FleetId == id);
                if (order is null) continue;
                if (Session.RetreatSupplyPlanFor(id, isFleet: true) is not null)
                {
                    Session.ClearFleetRallyOrder(id);
                    stopped.Add($"{id}: conflicting journey cancelled for Retreat and supply");
                    continue;
                }
                var decision = FleetRallyPlanner.ForOrder(Session.State, Session.World, Session.Ruleset, order);
                if (decision.WaitForMoves) continue;
                if (decision.CommandLine is null)
                {
                    Session.ClearFleetRallyOrder(id);
                    if (decision.Reason is { } reason) stopped.Add($"{id}: {reason}");
                    continue;
                }
                var before = Session.State.FleetById(id)!;
                SubmitForCheck(decision.CommandLine);
                if (StrategicTravel.IsActive) StrategicTravel.Skip();
                var after = Session.State.FleetById(id);
                var next = FleetRallyPlanner.ForOrder(Session.State, Session.World, Session.Ruleset, order);
                if (after is null || after.X == before.X && after.Y == before.Y)
                {
                    Session.ClearFleetRallyOrder(id); stopped.Add($"{id}: route blocked or no progress");
                }
                else
                {
                    moved++;
                    if (next.Arrived || next.Reason is not null) Session.ClearFleetRallyOrder(id);
                    if (next.Reason is { } reason) stopped.Add($"{id}: {reason}");
                }
            }
        }
        finally { _runningFleetRally = false; }
        UpdateCommandStrip();
        return $"Fleet rally: {moved} moved." + (stopped.Count == 0 ? "" : " Stopped: " + string.Join("; ", stopped));
    }
}
