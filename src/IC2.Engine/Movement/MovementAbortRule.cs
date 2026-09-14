using IC2.Engine.Model;

namespace IC2.Engine.Movement;

/// <summary>
/// What happens to a mover's remaining moves after <see cref="MovementStopReason.InsufficientMoves"/>.
/// </summary>
/// <remarks>
/// <para>
/// The original zeroes the whole army's moves for the rest of the turn when a step is unaffordable —
/// but only for a computer-controlled nation; a human seat's walk simply stops, leaving whatever moves
/// were left <strong>[confirmed: terrain-move-cost-table-in-dat.md]</strong>
/// (<c>docs/design-audit.md</c> §2.2: "The zeroing branch is guarded by the computer-controlled flag.
/// For a human player the walk just stops."). <c>classical-faithful</c> reproduces that asymmetry
/// verbatim; <c>improved</c> normalises it so every seat — human or AI — is zeroed the same way
/// (<c>docs/game-design.md</c> "Two shipped presets", audit Q6, <c>seatAsymmetry</c>).
/// </para>
/// <para>
/// Deliberately separate from <see cref="MovementWalker"/>: the walker reports <em>why</em> it stopped
/// and how many moves were left over, and is not told which seat is moving. Only this rule needs
/// <see cref="SeatControl"/> and the ruleset flag, so a caller not moving an army at all (a pure
/// path-cost preview, for example) never has to supply either.
/// </para>
/// </remarks>
public static class MovementAbortRule
{
    /// <summary>
    /// Decides what a mover's moves total becomes after an aborted step.
    /// </summary>
    /// <param name="movesRemainingBeforeAbort">
    /// <see cref="MovementWalkResult.MovesRemaining"/> from the walk that aborted.
    /// </param>
    /// <param name="control">Who controls the mover's nation.</param>
    /// <param name="model">
    /// The loaded ruleset's <see cref="RulesetFlags.SeatAsymmetry"/> setting.
    /// </param>
    /// <returns>
    /// <c>0</c> when the abort zeroes the mover's moves for the rest of the turn; otherwise
    /// <paramref name="movesRemainingBeforeAbort"/>, unchanged.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="movesRemainingBeforeAbort"/> is negative, or <paramref name="model"/> is not a
    /// recognised <see cref="SeatAsymmetryModel"/> value.
    /// </exception>
    public static int MovesAfterAbortedStep(int movesRemainingBeforeAbort, SeatControl control, SeatAsymmetryModel model)
    {
        if (movesRemainingBeforeAbort < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(movesRemainingBeforeAbort), movesRemainingBeforeAbort, "Moves remaining may not be negative.");
        }

        var zeroesMoves = model switch
        {
            SeatAsymmetryModel.Faithful => control == SeatControl.Ai,
            SeatAsymmetryModel.Normalized => true,
            _ => throw new ArgumentOutOfRangeException(nameof(model), model, "Unrecognised seat-asymmetry model."),
        };

        return zeroesMoves ? 0 : movesRemainingBeforeAbort;
    }
}
