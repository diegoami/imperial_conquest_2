namespace IC2.Engine.Battle.Tactical.General;

/// <summary>
/// The clear-line test, <c>FUN_00439F00</c>, and the approach box search of engage (<c>FUN_0043A160</c>)
/// and pass 3 (<c>FUN_0043ABB4</c>): <strong>[confirmed: code, static,
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §7]</strong>; <c>docs/game-design.md</c>,
/// "The computer general", "A clear line".
/// </summary>
/// <remarks>
/// <para>
/// A line is clear when the same 8-connected Bresenham walk the movement makes (<c>err = 2 × minor −
/// major</c>; while <c>err ≥ 0</c> a minor step and <c>err −= 2 × major</c>; then the major step and
/// <c>err += 2 × minor</c>; <c>y</c> is the major axis when <c>|dy| &gt; |dx|</c>, as in
/// <see cref="TacticalMovement"/>) finds every cell strictly between the two ends empty. Adjacent cells,
/// and a cell and itself, have nothing between them, so their line is clear. "Empty" is the grid's
/// empty code, the occupancy map the movement tests (report §1).
/// </para>
/// <para>
/// The box search scans the <c>(2r + 1)²</c> cells around the target, <c>dx</c> outer and <c>dy</c>
/// inner, each from <c>−r</c> to <c>+r</c>, for a cell that is on the board, empty and on a clear line
/// from the unit, and keeps the one nearest the unit (Chebyshev distance) by a strict <c>&lt;</c>, so
/// ties go to the first found. <c>r</c> is <c>combat.tactical.approachBoxRadius[type]</c>.
/// </para>
/// </remarks>
public static class GeneralLine
{
    /// <summary>Whether every cell strictly between <c>(x1, y1)</c> and <c>(x2, y2)</c> is empty.</summary>
    public static bool IsClear(TacticalBattleState state, int x1, int y1, int x2, int y2, TacticalContext context)
    {
        var board = TacticalBoard.Thaw(state, context, draws: null);
        return IsClear(board, x1, y1, x2, y2);
    }

    /// <summary>
    /// The nearest usable cell of the box around <paramref name="target"/>'s cell for unit
    /// <paramref name="slot"/>, or <see langword="null"/> when there is none.
    /// </summary>
    public static (int X, int Y)? BoxCell(TacticalBattleState state, int slot, int target, TacticalContext context)
    {
        var board = TacticalBoard.Thaw(state, context, draws: null);
        return BoxCell(board, slot, target);
    }

    internal static bool IsClear(TacticalBoard board, int x1, int y1, int x2, int y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var sx = Math.Sign(dx);
        var sy = Math.Sign(dy);
        var yMajor = Math.Abs(dy) > Math.Abs(dx);
        var major = yMajor ? Math.Abs(dy) : Math.Abs(dx);
        var minor = yMajor ? Math.Abs(dx) : Math.Abs(dy);
        var err = (2 * minor) - major;
        var px = x1;
        var py = y1;

        while (px != x2 || py != y2)
        {
            while (err >= 0)
            {
                if (yMajor)
                {
                    px += sx;
                }
                else
                {
                    py += sy;
                }

                err -= 2 * major;
            }

            if (yMajor)
            {
                py += sy;
            }
            else
            {
                px += sx;
            }

            err += 2 * minor;

            if ((px != x2 || py != y2) && !board.IsEmptyCell(px, py))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool IsClear(TacticalBoard board, int slot, int x, int y) =>
        IsClear(board, board.Slots[slot].X, board.Slots[slot].Y, x, y);

    internal static (int X, int Y)? BoxCell(TacticalBoard board, int slot, int target)
    {
        var unit = board.Slots[slot];
        var aim = board.Slots[target];
        var radius = board.Rules.ApproachBoxRadius[unit.Type];

        (int X, int Y)? best = null;
        var bestDistance = int.MaxValue;
        for (var dx = -radius; dx <= radius; dx++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                var cx = aim.X + dx;
                var cy = aim.Y + dy;
                if (!board.OnBoard(cx, cy) || !board.IsEmptyCell(cx, cy) || !IsClear(board, slot, cx, cy))
                {
                    continue;
                }

                var distance = TacticalBoard.Distance(unit.X, unit.Y, cx, cy);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = (cx, cy);
                }
            }
        }

        return best;
    }
}
