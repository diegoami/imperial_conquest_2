using IC2.Engine.Core;
using IC2.Engine.Movement;

namespace IC2.Engine.Ai;

/// <summary>CD08 request committed by an original-policy army move, consumed in this AI seat only.</summary>
[DomainEvent("ai.fleet-route-requested")]
public sealed record OriginalFleetRouteRequested(string FleetId, int X, int Y) : DomainEvent;

/// <summary>T247: no GameState/save field. Last army writer wins; fleet-table iteration orders execution.</summary>
public sealed class OriginalFleetRoutes
{
    private readonly List<(string FleetId, GridPoint Target)> _requests = new();

    public void Set(string fleetId, GridPoint target)
    {
        var index = _requests.FindIndex(r => r.FleetId == fleetId);
        if (target.X < 0)
        {
            if (index >= 0) _requests.RemoveAt(index);
            return;
        }
        if (index < 0) _requests.Add((fleetId, target));
        else _requests[index] = (fleetId, target);
    }

    public GridPoint? For(string fleetId)
    {
        foreach (var request in _requests) if (request.FleetId == fleetId) return request.Target;
        return null;
    }

    public IEventSink Capture(IEventSink next) => new CaptureSink(this, next);

    private sealed class CaptureSink(OriginalFleetRoutes routes, IEventSink next) : IEventSink
    {
        public void Publish(DomainEvent domainEvent)
        {
            if (domainEvent is OriginalFleetRouteRequested route) routes.Set(route.FleetId, new(route.X, route.Y));
            next.Publish(domainEvent);
        }
    }
}
