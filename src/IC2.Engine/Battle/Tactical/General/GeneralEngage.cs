namespace IC2.Engine.Battle.Tactical.General;

/// <summary>
/// Pass 1's engage, <c>FUN_0043A160</c>: <strong>[confirmed: code, static,
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §7, "Engage"]</strong>;
/// <c>docs/game-design.md</c>, "The computer general", "Pass 1".
/// </summary>
/// <remarks>
/// With <c>u</c> the unit and <c>d</c> its chosen target, the first that applies:
/// <list type="number">
/// <item><description>
/// archers with shots at distance below 3 (<c>archerEngageDistance</c>) fire at <c>d</c> until their
/// moves are 0, their shots are 0, or <c>d</c> is removed;
/// </description></item>
/// <item><description>
/// adjacent (distance 1): the unit shoots while it has shots, until it has 1 move or no shots, then takes
/// <c>d</c> as its melee target if <c>d</c> lives;
/// </description></item>
/// <item><description>if the line to <c>d</c> is clear (<see cref="GeneralLine"/>), the unit moves toward <c>d</c>;</description></item>
/// <item><description>
/// otherwise it moves toward the nearest empty cell with a clear line in the box around <c>d</c>
/// (<see cref="GeneralLine.BoxCell(TacticalBattleState, int, int, TacticalContext)"/>), if there is one.
/// </description></item>
/// </list>
/// Every act is one of T123's: a shot (<see cref="TacticalShooting"/>), a target set, a walk
/// (<see cref="TacticalMovement"/>, whose computer-only branches are T123's). The shot loops call the
/// shot routine, which checks nothing; the loops' own guards are the original's.
/// </remarks>
public static class GeneralEngage
{
    /// <summary>The adjacent case stops shooting at this many moves left, keeping one (report §7).</summary>
    private const int AdjacentKeepMoves = 1;

    /// <summary>Unit <paramref name="slot"/> engages enemy <paramref name="target"/>.</summary>
    public static TacticalBattleState Engage(
        TacticalBattleState state,
        int slot,
        int target,
        TacticalContext context,
        IBattleDraws draws,
        IList<GeneralDecision>? journal = null)
    {
        var board = TacticalBoard.Thaw(state, context, draws);
        Apply(board, slot, target, journal);
        return board.Freeze();
    }

    internal static void Apply(TacticalBoard board, int slot, int target, IList<GeneralDecision>? journal)
    {
        var context = board.Context;
        var unit = board.Slots[slot];
        var aim = board.Slots[target];
        var distance = board.Distance(slot, target);

        if (unit.Type == context.ArcherType && unit.Shots > 0 && distance < board.Rules.ArcherEngageDistance)
        {
            journal?.Add(new GeneralDecision(slot, GeneralStep.ArcherVolley, target, aim.X, aim.Y));
            while (board.Slots[slot].Moves > 0 && board.Slots[slot].Shots > 0 && board.IsLive(target))
            {
                TacticalShooting.Apply(board, slot, target);
            }

            return;
        }

        if (distance == 1)
        {
            journal?.Add(new GeneralDecision(slot, GeneralStep.AdjacentEngage, target, aim.X, aim.Y));
            while (board.Slots[slot].Shots > 0 && board.Slots[slot].Moves > AdjacentKeepMoves && board.IsLive(target))
            {
                TacticalShooting.Apply(board, slot, target);
            }

            if (board.IsLive(target))
            {
                GeneralOrders.SetTarget(board, slot, target);
            }

            return;
        }

        if (GeneralLine.IsClear(board, slot, aim.X, aim.Y))
        {
            journal?.Add(new GeneralDecision(slot, GeneralStep.ApproachLine, target, aim.X, aim.Y));
            TacticalMovement.Walk(board, slot, aim.X, aim.Y);
            return;
        }

        if (GeneralLine.BoxCell(board, slot, target) is { } cell)
        {
            journal?.Add(new GeneralDecision(slot, GeneralStep.ApproachBox, target, cell.X, cell.Y));
            TacticalMovement.Walk(board, slot, cell.X, cell.Y);
        }
    }
}
