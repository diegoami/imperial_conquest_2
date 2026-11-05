using IC2.Engine.Model;

namespace IC2.Slice.UI;

public partial class MainGameScreen
{
    // A destination click is one Undo action, including the remembered remainder and
    // cancellation of recovery. The engine's ordinary walker still decides the actual route.
    private void IssueArmyTravel(ArmyTravelTarget target, Action<string> submit)
    {
        if (Session.IsObserverMode || Session.State.ArmyById(target.ArmyId) is not { IsEmbarked: false } army
            || army.Nation != Session.State.ActiveNationId) return;
        using var action = Session.BeginStrategicUndoAction();
        Session.ClearRetreatSupplyPlan(army.Id, isFleet: false);
        Session.SetArmyTravelTarget(target);
        var decision = ArmyTravelPlanner.ForTarget(Session.State, Session.World, Session.Ruleset, target);
        if (decision.Arrived)
        {
            Session.ClearArmyTravelTarget(army.Id);
            ShowScreenMessage($"{army.Id} is already at its destination.");
        }
        else if (army.Moves <= 0)
        {
            if (decision.Reason is { } reason)
            {
                Session.ClearArmyTravelTarget(army.Id);
                ShowScreenMessage($"Travel order cancelled: {reason}.");
            }
            else ShowScreenMessage($"Destination remembered for {army.Id}; movement resumes on your next confirmed End turn.");
        }
        else
        {
            // Danger cancels automation, but does not prohibit a deliberate manual move.
            _runningArmyTravel = true;
            try { submit($"move {army.Id} {target.X} {target.Y}"); }
            finally { _runningArmyTravel = false; }
            var after = Session.State.ArmyById(army.Id);
            var next = ArmyTravelPlanner.ForTarget(Session.State, Session.World, Session.Ruleset, target);
            if (next.Arrived) Session.ClearArmyTravelTarget(army.Id);
            else if (decision.Reason is not null || next.Reason is not null
                || after is null || after.X == army.X && after.Y == army.Y)
            {
                Session.ClearArmyTravelTarget(army.Id);
                ShowScreenMessage($"Travel order stopped: {decision.Reason ?? next.Reason ?? "route blocked or no progress"}.");
            }
        }
        UpdateCommandStrip();
        _mapView.Refresh();
        RefreshStrategicUndoControl();
    }
}
