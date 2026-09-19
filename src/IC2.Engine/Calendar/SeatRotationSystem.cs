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
/// <para>
/// <strong>Elimination-aware rotation (T17 DoD 8, T06 follow-up
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/43">#43</see>).</strong> A nation
/// eliminated under <c>docs/task-catalogue.md</c> T17 DoD 4
/// (<see cref="IC2.Engine.Cities.Capture.NationElimination"/>) never becomes the active seat again: the
/// search for the next seat skips every <see cref="NationState.Eliminated"/> entry in
/// <see cref="GameState.TurnOrder"/>, including one eliminated earlier in the same round, so it gets no
/// further <see cref="SeatHandoffRequested"/> and — since nothing downstream ever reads
/// <see cref="GameState.ActiveSeatIndex"/> for an eliminated nation — no AI turn either. How the original
/// itself treats an eliminated seat is not decompiled; this is <strong>[designed]</strong>, the direct
/// reading of "gets no further turn" the DoD line's own words ask for, and what was searched for a
/// decompiled rule and came up empty is recorded in this task's PR (<c>design-audit.md</c> §4.5): none of
/// the three reports T17 already cites for elimination
/// (<c>decompiled-city-capture-resolution.md</c>, <c>decompiled-defection-and-siege-attrition.md</c>,
/// <c>galatia-elimination-and-city-resupply-confirmed.md</c>) states a turn-order rule for the eliminated
/// seat, and none was found searching for one. An active seat whose nation id does not resolve at all
/// (as opposed to resolving but eliminated) fails with a typed <see cref="SeatResolutionException"/>
/// rather than being skipped silently — a genuinely corrupt turn order is a defect to surface, not paper
/// over.
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

        var index = state.ActiveSeatIndex;
        var wrapped = false;
        NationState? nextNation = null;
        string nextNationId;

        // Advance at least once, then keep skipping any eliminated seat, for up to one full lap of the
        // turn order -- "including one eliminated earlier in the same round" is exactly a seat this loop
        // would otherwise land back on within that same lap.
        for (var attempts = 0; attempts < state.TurnOrder.Count; attempts++)
        {
            index = (index + 1) % state.TurnOrder.Count;
            if (index == 0)
            {
                // Every seat in the turn-order table has now had its turn: the round is complete and the
                // original's global weekly tick (FUN_004514ec) is due -- true of the lap itself, whether
                // or not the seat landed on this pass turns out to be eliminated too.
                wrapped = true;
            }

            nextNationId = state.TurnOrder[index];
            nextNation = state.NationById(nextNationId)
                         ?? throw new SeatResolutionException(
                             $"Turn-order seat '{nextNationId}' (index {index}) does not resolve to a known nation.");

            if (!nextNation.Eliminated)
            {
                break;
            }

            nextNation = null;
        }

        if (nextNation is null)
        {
            throw new SeatResolutionException(
                "Every seat in the turn order is eliminated; there is no next active seat to rotate to.");
        }

        if (wrapped)
        {
            context.Signals.RequestRoundTick();
        }

        if (nextNation.Control == SeatControl.Human)
        {
            context.Events.Publish(new SeatHandoffRequested(nextNation.Id));
        }

        return state with { ActiveSeatIndex = index };
    }
}

/// <summary>
/// A turn-order seat could not be resolved to a next active nation — either its id names no nation at
/// all, or every seat in the turn order is eliminated. A typed invariant error
/// (<c>docs/task-catalogue.md</c> T17 DoD 8), never a silent skip.
/// </summary>
public sealed class SeatResolutionException : InvalidOperationException
{
    /// <summary>Creates the exception with a message describing which invariant failed.</summary>
    public SeatResolutionException(string message) : base(message)
    {
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
