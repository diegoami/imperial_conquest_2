namespace IC2.Engine.Battle.Tactical;

/// <summary>
/// The half-round's setup (<c>FUN_00439968</c>, also the resume path) and end (<c>FUN_00439C20</c>):
/// <strong>[confirmed: code, static, <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §3]</strong>;
/// <c>docs/game-design.md</c>, "The half-round".
/// </summary>
public static class TacticalHalfRound
{
    /// <summary>
    /// <c>FUN_00439968</c>, the half-round setup for the side to move.
    /// </summary>
    /// <remarks>
    /// Let <c>dmin</c> be the smallest distance between a live own unit and a live enemy unit. For each of
    /// the side's 20 slots the melee target is cleared and the moves are reset to the type's stat; if the
    /// side is computer-controlled (its nation; <em>Computer general</em> does not count), the type is not
    /// the exempt type (heavy infantry), <c>dmin &gt; 2</c> and the counter, read before its increment, is
    /// below 10, the moves are 1 instead (the slow advance). Then the computer general's 40 claims are
    /// cleared and the counter goes up by 1. It draws nothing.
    /// </remarks>
    public static TacticalBattleState Setup(TacticalBattleState state, TacticalContext context)
    {
        var board = TacticalBoard.Thaw(state, context, draws: null);
        ApplySetup(board);
        return board.Freeze();
    }

    /// <summary>
    /// Resumes a loaded battle block: the half-round setup for the side to move, exactly as the original's
    /// load path runs it (moves refilled, targets cleared, the counter incremented again, so a pending
    /// melee is lost). <c>game-design.md</c>, "Saves".
    /// </summary>
    public static TacticalBattleState Resume(TacticalBattleState state, TacticalContext context) => Setup(state, context);

    /// <summary>
    /// <c>FUN_00439C20</c>, the half-round end.
    /// </summary>
    /// <remarks>
    /// The phase advances (during placement side 1 hands to side 0, and side 0 sets "placed" without
    /// changing side; afterwards the side toggles), then the melee of <em>the side that just moved</em> is
    /// resolved with the targets it set, and, if the battle is not over, the next half-round is set up.
    /// </remarks>
    public static TacticalBattleState End(TacticalBattleState state, TacticalContext context, IBattleDraws draws)
    {
        var board = TacticalBoard.Thaw(state, context, draws);
        ApplyEnd(board);
        return board.Freeze();
    }

    internal static void ApplySetup(TacticalBoard board)
    {
        var rules = board.Rules;
        var context = board.Context;
        var side = board.SideToMove;
        var own = board.FirstSlotOf(side);
        var enemy = board.FirstSlotOf(TacticalBoard.Other(side));

        var minDistance = int.MaxValue;
        for (var s = own; s < own + board.SlotsPerSide; s++)
        {
            if (!board.IsLive(s))
            {
                continue;
            }

            for (var e = enemy; e < enemy + board.SlotsPerSide; e++)
            {
                if (board.IsLive(e))
                {
                    minDistance = Math.Min(minDistance, board.Distance(s, e));
                }
            }
        }

        var slowAdvance = board.ComputerControlled[side]
                          && minDistance > rules.SlowAdvanceMinDistance
                          && board.Counter < rules.SlowAdvanceHalfRounds;

        for (var s = own; s < own + board.SlotsPerSide; s++)
        {
            var unit = board.Slots[s];
            var moves = slowAdvance && unit.Type != context.SlowAdvanceExemptType
                ? rules.SlowAdvanceMoves
                : context.MovesOf(unit.Type);
            board.Slots[s] = unit with { Target = TacticalSlot.NoTarget, Moves = moves };
        }

        Array.Clear(board.GeneralClaims);
        board.Counter = unchecked(board.Counter + 1);
        board.Log.Add(new TacticalSetUpEvent(side, board.Counter, minDistance));
    }

    internal static void ApplyEnd(TacticalBoard board)
    {
        var moved = board.SideToMove;
        if (!board.Placed)
        {
            if (moved == TacticalBattleState.DefenderSide)
            {
                board.SideToMove = TacticalBattleState.AttackerSide;
            }
            else
            {
                board.Placed = true;
            }
        }
        else
        {
            board.SideToMove = TacticalBoard.Other(moved);
        }

        board.Log.Add(new TacticalHalfRoundEndedEvent(moved, board.SideToMove, board.Placed));
        TacticalMelee.Apply(board, moved);

        if (!board.IsOver)
        {
            ApplySetup(board);
        }
    }
}
