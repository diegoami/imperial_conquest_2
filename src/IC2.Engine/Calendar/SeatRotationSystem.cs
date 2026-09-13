using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Calendar;

/// <summary>
/// Advances the active seat following <see cref="World.TurnOrder"/> — the save's own 16-entry
/// turn-order table, generalised to any nation count — and signals the coordinator once every seat has
/// gone, so the round-scoped phases run.
/// </summary>
/// <remarks>
/// <para>
/// This is the real implementation of the stand-in T03's own tests built in its place —
/// <c>PipelineSeatEndSystem</c> in <c>tests/IC2.Engine.Tests/Core/PipelineFixtures.cs</c>, whose doc
/// comment says exactly that: "Stands in for T06's seat rotation... belongs to T06; what T03 owns is
/// the signal".
/// </para>
/// <para>
/// Runs in <see cref="TurnPhase.SeatEnd"/>, which T03's own docstring already names as this task's
/// phase: "the seat rotation that follows [the end-turn validity check]. Consumer: T06 calendar and
/// turn sequencing, which owns the rotation rule itself."
/// </para>
/// <para>
/// <c>docs/game-design.md</c> §"Calendar and turns" describes a hotseat "pass the device" pause point
/// as <strong>[designed]</strong> UI behaviour, "surfaced as events (not UI)" per this task's own Scope
/// line — this system raises <see cref="SeatHandoffRequested"/> when the seat about to become active is
/// a local human, and stays silent for an AI seat, which needs no such pause.
/// </para>
/// </remarks>
[GameSystem(TurnPhase.SeatEnd, "calendar.seat-rotation")]
public sealed class SeatRotationSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var state = context.State;
        if (state.TurnOrder.Count == 0)
        {
            throw new InvalidOperationException("A scenario's turn order must name at least one seat.");
        }

        var nextIndex = (state.ActiveSeatIndex + 1) % state.TurnOrder.Count;
        if (nextIndex == 0)
        {
            // Every seat in the turn-order table has now had its turn: the round is complete and the
            // original's global weekly tick (FUN_004514ec) is due.
            context.Signals.RequestRoundTick();
        }

        var nextNationId = state.TurnOrder[nextIndex];
        var nextNation = state.NationById(nextNationId);
        if (nextNation is { Control: SeatControl.Human })
        {
            context.Events.Publish(new SeatHandoffRequested(nextNationId));
        }

        return state with { ActiveSeatIndex = nextIndex };
    }
}

/// <summary>
/// Raised when seat rotation is about to hand control to a local human seat — the hotseat "pass the
/// device" pause point. Not itself a UI action: the engine only says whose turn is next, and the UI
/// decides whether to show a confirmation screen (<c>docs/game-design.md</c> §"Calendar and turns",
/// the <c>BlindHotseat</c> toggle on <see cref="Scenario"/>).
/// </summary>
/// <remarks>
/// Not marked news-worthy: no report or design text gives this a confirmed message literal, and
/// T10's news-log task requires every news-worthy event to carry a catalog entry it would be this
/// task's job, not T10's, to add — out of scope here.
/// </remarks>
[DomainEvent("calendar.seat-handoff-requested")]
public sealed record SeatHandoffRequested(string NationId) : DomainEvent;
