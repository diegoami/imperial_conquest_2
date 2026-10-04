namespace IC2.Engine.Battle.Tactical;

/// <summary>
/// Surrender and the end of the battle: <strong>[confirmed: code, static,
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §6 and §8]</strong>; <c>docs/game-design.md</c>,
/// "Surrender and the end".
/// </summary>
/// <remarks>
/// <em>Surrender</em> removes every unit of the side, with no cascade, ends the battle and ends the
/// half-round (<see cref="TacticalHalfRound.End"/>, whose melee then finds no live unit of the side and
/// whose setup is skipped because the battle is over). The battle is over when, after any rout or a
/// surrender, one side has no live unit, and nothing else ends it: no half-round limit, no retreat. The
/// winner is the attacker if any attacker slot has troops, else the defender; when both sides are
/// emptied, the defender "wins" with no units <strong>[derived: an unexercised edge of the same
/// code]</strong>.
/// </remarks>
public static class TacticalSurrender
{
    /// <summary>The side to move surrenders.</summary>
    public static TacticalBattleState Surrender(TacticalBattleState state, TacticalContext context, IBattleDraws draws)
    {
        var board = TacticalBoard.Thaw(state, context, draws);
        Apply(board);
        return board.Freeze();
    }

    /// <summary>The winning side: the attacker if any attacker slot has troops, else the defender.</summary>
    public static int Winner(TacticalBattleState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.HasLiveUnit(TacticalBattleState.AttackerSide)
            ? TacticalBattleState.AttackerSide
            : TacticalBattleState.DefenderSide;
    }

    internal static void Apply(TacticalBoard board)
    {
        var side = board.SideToMove;
        var first = board.FirstSlotOf(side);
        for (var s = first; s < first + board.SlotsPerSide; s++)
        {
            if (board.IsLive(s))
            {
                board.Remove(s);
            }
        }

        board.Log.Add(new TacticalSurrenderedEvent(side));
        board.EndBattle();
        TacticalHalfRound.ApplyEnd(board);
    }
}
