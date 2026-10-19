using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Movement;

/// <summary>The kind of strategic marker, independent of its current owner.</summary>
public enum StrategicMarkerKind { Army, Fleet, City }

/// <summary>A copied accepted walk. EnteredCells excludes the origin and includes only paid, legal cells.</summary>
public sealed record StrategicTravelObservation(
    StrategicMarkerKind Kind, string EntityId, string OwnerId, string? CarrierId, string? CargoId,
    GridPoint Origin, GridPoint Destination, ValueList<GridPoint> EnteredCells,
    MovementStopReason StopReason, GridPoint? BlockedAt, int MovesSpent);

/// <summary>Display inputs at one immutable boundary. Size is a troop/ship/population input, not a UI tier rule.</summary>
public sealed record StrategicMarkerSnapshot(
    StrategicMarkerKind Kind, string EntityId, string OwnerId, GridPoint Position,
    int Size, bool Visible, string? CarrierId, string? CargoId);

// Engine-only buffered seam. No public subscriber runs while a handler or system executes.
internal interface IStrategicObservationRecorder
{
    void Accepted(ICommand command, GameState before, GameState after,
        ValueList<StrategicTravelObservation> routes, ValueList<DomainEvent> events);
    void CompletedSystem(string systemId, TurnPhase phase, GameState before, GameState after);
    void Disable();
}
