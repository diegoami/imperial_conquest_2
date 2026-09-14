using IC2.Engine.Core;

namespace IC2.Engine.Movement.Commands;

/// <summary>
/// Published by <see cref="MoveArmyCommandHandler"/> once a move is accepted. Not marked news-worthy:
/// <c>docs/build-process.md</c> §2.5 leaves the news-log message literal to whichever task reviews and
/// adds a catalog entry for it (T10's catalog owns that), and this task invents no rendered text —
/// <c>docs/task-catalogue.md</c> "T41 Thin CLI demo": "Only T10's catalog kinds render; that's fine."
/// </summary>
/// <param name="ArmyId">The army that moved.</param>
/// <param name="NationId">The army's nation.</param>
/// <param name="FromX">Where the walk started.</param>
/// <param name="FromY">Where the walk started.</param>
/// <param name="ToX">Where the walk actually ended — <see cref="Movement.MovementWalkResult.FinalPosition"/>.</param>
/// <param name="ToY">Where the walk actually ended.</param>
/// <param name="MovesSpent">The walker's own reported cost — <see cref="Movement.MovementWalkResult.MovesSpent"/>.</param>
[DomainEvent("movement.army-moved")]
public sealed record ArmyMoved(
    string ArmyId,
    string NationId,
    int FromX,
    int FromY,
    int ToX,
    int ToY,
    int MovesSpent) : DomainEvent;
