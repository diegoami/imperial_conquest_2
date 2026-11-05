using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval;

namespace IC2.Engine.Presentation;

public sealed partial class GameSession
{
    public ValueList<FleetRallyOrder> FleetRallyOrders { get; private set; } = ValueList<FleetRallyOrder>.Empty;
    public FleetRallyOrder? FleetRallyOrderFor(string fleetId) => FleetRallyOrders.FirstOrDefault(o => o.FleetId == fleetId);
    public void SetFleetRallyOrder(FleetRallyOrder order)
    {
        using var action = BeginStrategicUndoAction();
        ArgumentNullException.ThrowIfNull(order);
        FleetRallyOrders = ValueList.From(FleetRallyOrders.Where(o => o.FleetId != order.FleetId).Append(order));
    }
    public bool ClearFleetRallyOrder(string fleetId)
    {
        using var action = BeginStrategicUndoAction();
        if (FleetRallyOrderFor(fleetId) is null) return false;
        FleetRallyOrders = ValueList.From(FleetRallyOrders.Where(o => o.FleetId != fleetId));
        return true;
    }

    private void BindLaunchedFleetRallies(IReadOnlyList<DomainEvent> events)
    {
        foreach (var launch in events.OfType<FleetLaunched>())
        {
            if (State.NationById(launch.NationId)?.Control != SeatControl.Human
                || RallyPointFor(launch.NationId, isFleet: true) is not { } point
                || State.FleetById(launch.FleetId) is not { IsUnderConstruction: false } fleet
                || fleet.Nation != launch.NationId) continue;
            // This is only a binding. The launch tick never executes the order, even
            // if an imported/reused fleet record happens to retain movement points.
            SetFleetRallyOrder(new(fleet.Id, fleet.Nation, point.X, point.Y, point.CityId));
        }
    }
}
