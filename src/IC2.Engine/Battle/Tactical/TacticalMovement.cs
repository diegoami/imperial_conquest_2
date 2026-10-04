namespace IC2.Engine.Battle.Tactical;

/// <summary>
/// Movement, <c>FUN_00438D24</c> with its step <c>FUN_00438A6C</c>, and the human's
/// <c>MoveHumanUnit</c>: <strong>[confirmed: code, static,
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §3, "Movement"]</strong>;
/// <c>docs/game-design.md</c>, "Movement".
/// </summary>
/// <remarks>
/// <para>
/// <strong>The path.</strong> The unit walks an 8-connected Bresenham line from its cell to the
/// destination: <c>err = 2 × minor − major</c>; while <c>err ≥ 0</c>, a minor step and
/// <c>err −= 2 × major</c>; then the major step and <c>err += 2 × minor</c>. The path cursor follows
/// the line whatever the unit does; each step costs one move and the unit enters the path cell when it
/// is empty. The walk ends when the cursor reaches the destination.
/// </para>
/// <para>
/// <strong>Detours.</strong> When the path cell is blocked, two alternatives are tried: for a step along
/// a column (only <c>y</c> changes) <c>(px + 1, py)</c> then <c>(px − 1, py)</c>; along a row (only
/// <c>x</c> changes) <c>(px, py + 1)</c> then <c>(px, py − 1)</c>; on a diagonal step <c>(ux + sx, uy)</c>
/// then <c>(ux, uy + sy)</c> from the unit, swapped when <c>y</c> is the major axis. A cell costs 1 when
/// it is on the board, empty, and within distance 1 of both the path cursor and the unit, and 10
/// otherwise. The cheaper alternative is taken when it is cheaper than the path cell, ties going to the
/// first.
/// </para>
/// <para>
/// <strong>Stops.</strong> The walk stops when the moves fall below 1, when the chosen cost is above 9,
/// or when the path reaches an occupied destination.
/// </para>
/// <para>
/// <strong>For a computer-controlled side only</strong> (its nation): a unit with a target does not
/// move; before spending its last move, a unit gives it up (moves 0) when the 3 × 3 around the next path
/// cell holds an enemy at least as strong as itself, by <c>S = (troops × quality div 100) × morale ×
/// M[mine][theirs]</c> (the enemy's <c>S</c> read from its own side, <c>M[theirs][mine]</c>); and each
/// step first checks two things: adjacent to the enemy at its destination, with fewer than 2 moves or no
/// shots, and not archers or no shots, it takes that enemy as its target and stops; with the enemy in
/// range and moves, shots and no target, it shoots once and checks again.
/// </para>
/// <para>
/// The movement draws nothing itself; a computer unit's shot draws as <see cref="TacticalShooting"/> does.
/// The <c>1</c>/<c>10</c> cell costs and the <c>&gt; 9</c> stop are the step's own encoding (T122's entry:
/// "the Bresenham arithmetic ... formula shape").
/// </para>
/// </remarks>
public static class TacticalMovement
{
    /// <summary>The cost of a usable cell (report §3).</summary>
    private const int OpenCost = 1;

    /// <summary>The cost of an unusable cell (report §3).</summary>
    private const int BlockedCost = 10;

    /// <summary>The walk stops when the chosen cost is above this (report §3).</summary>
    private const int StopAboveCost = 9;

    /// <summary>
    /// <c>FUN_00438D24</c>: walks slot <paramref name="slot"/> toward <c>(x, y)</c> once.
    /// </summary>
    /// <param name="state">The battle.</param>
    /// <param name="slot">The moving unit.</param>
    /// <param name="x">The destination column.</param>
    /// <param name="y">The destination row.</param>
    /// <param name="context">The ruleset's numbers.</param>
    /// <param name="draws">The draw seam: a computer unit may shoot on the way.</param>
    public static TacticalBattleState MoveUnit(
        TacticalBattleState state, int slot, int x, int y, TacticalContext context, IBattleDraws draws)
    {
        var board = TacticalBoard.Thaw(state, context, draws);
        Walk(board, slot, x, y);
        return board.Freeze();
    }

    /// <summary>
    /// <c>MoveHumanUnit</c>: the target is cleared (moving a unit clears its target), the walk is made,
    /// and made a second time when the first did not arrive.
    /// </summary>
    public static TacticalBattleState MoveHumanUnit(
        TacticalBattleState state, int slot, int x, int y, TacticalContext context, IBattleDraws draws)
    {
        var board = TacticalBoard.Thaw(state, context, draws);
        ApplyHumanMove(board, slot, x, y);
        return board.Freeze();
    }

    internal static void ApplyHumanMove(TacticalBoard board, int slot, int x, int y)
    {
        if (board.Slots[slot].Target != TacticalSlot.NoTarget)
        {
            board.Slots[slot] = board.Slots[slot] with { Target = TacticalSlot.NoTarget };
            board.Log.Add(new TacticalTargetClearedEvent(slot));
        }

        if (!Walk(board, slot, x, y))
        {
            Walk(board, slot, x, y);
        }
    }

    /// <summary>One call of <c>FUN_00438D24</c>; returns whether the unit stands on the destination.</summary>
    internal static bool Walk(TacticalBoard board, int slot, int x, int y)
    {
        var side = board.SideOf(slot);
        var computer = board.ComputerControlled[side];
        if (computer && board.Slots[slot].Target >= 0)
        {
            return Arrived(board, slot, x, y);
        }

        var start = board.Slots[slot];
        var dx = x - start.X;
        var dy = y - start.Y;
        var sx = Math.Sign(dx);
        var sy = Math.Sign(dy);
        var yMajor = Math.Abs(dy) > Math.Abs(dx);
        var major = yMajor ? Math.Abs(dy) : Math.Abs(dx);
        var minor = yMajor ? Math.Abs(dx) : Math.Abs(dy);
        var err = (2 * minor) - major;
        var px = start.X;
        var py = start.Y;

        while (px != x || py != y)
        {
            if (computer && ComputerChecks(board, slot, x, y))
            {
                return Arrived(board, slot, x, y);
            }

            if (board.Slots[slot].Moves < 1)
            {
                break;
            }

            // The path cursor's next cell: an optional minor step, then the major step.
            var fromX = px;
            var fromY = py;
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

            if (px == x && py == y && !board.IsEmptyCell(x, y))
            {
                break;
            }

            if (computer && board.Slots[slot].Moves == 1 && InDanger(board, slot, px, py))
            {
                board.Slots[slot] = board.Slots[slot] with { Moves = 0 };
                board.Log.Add(new TacticalMoveForfeitedEvent(slot));
                break;
            }

            var (cx, cy, cost) = Step(board, slot, px, py, px != fromX, py != fromY, sx, sy, yMajor);
            if (cost > StopAboveCost)
            {
                break;
            }

            var unit = board.Slots[slot];
            var (oldX, oldY) = (unit.X, unit.Y);
            board.Grid[board.CellIndex(oldX, oldY)] = TacticalIcon.Empty;
            unit = unit with { X = cx, Y = cy, Moves = unchecked(unit.Moves - 1) };
            board.Slots[slot] = unit;
            board.PaintIcon(slot);
            board.Log.Add(new TacticalMovedEvent(slot, oldX, oldY, cx, cy, unit.Moves));
        }

        return Arrived(board, slot, x, y);
    }

    /// <summary>
    /// <c>FUN_00438A6C</c>, one step's choice: the path cell, or the cheaper of its two alternatives when
    /// that is cheaper, ties to the first. Returns the chosen cell and its cost.
    /// </summary>
    private static (int X, int Y, int Cost) Step(
        TacticalBoard board, int slot, int px, int py, bool stepX, bool stepY, int sx, int sy, bool yMajor)
    {
        var unit = board.Slots[slot];
        var pathCost = Cost(board, px, py, px, py, unit.X, unit.Y);
        if (pathCost <= OpenCost)
        {
            return (px, py, pathCost);
        }

        int ax1, ay1, ax2, ay2;
        if (stepX && stepY)
        {
            (ax1, ay1, ax2, ay2) = (unit.X + sx, unit.Y, unit.X, unit.Y + sy);
            if (yMajor)
            {
                (ax1, ay1, ax2, ay2) = (ax2, ay2, ax1, ay1);
            }
        }
        else if (stepY)
        {
            (ax1, ay1, ax2, ay2) = (px + 1, py, px - 1, py);
        }
        else
        {
            (ax1, ay1, ax2, ay2) = (px, py + 1, px, py - 1);
        }

        var cost1 = Cost(board, ax1, ay1, px, py, unit.X, unit.Y);
        var cost2 = Cost(board, ax2, ay2, px, py, unit.X, unit.Y);
        var (bx, by, bestCost) = cost1 <= cost2 ? (ax1, ay1, cost1) : (ax2, ay2, cost2);
        return bestCost < pathCost ? (bx, by, bestCost) : (px, py, pathCost);
    }

    /// <summary>A cell's cost: 1 when on the board, empty and within distance 1 of both the cursor and the unit; 10 otherwise.</summary>
    private static int Cost(TacticalBoard board, int cx, int cy, int px, int py, int ux, int uy) =>
        board.OnBoard(cx, cy)
        && board.IsEmptyCell(cx, cy)
        && TacticalBoard.Distance(cx, cy, px, py) <= 1
        && TacticalBoard.Distance(cx, cy, ux, uy) <= 1
            ? OpenCost
            : BlockedCost;

    /// <summary>
    /// The computer unit's two checks before each step. Returns <see langword="true"/> when the unit took
    /// the enemy at its destination as its target and stops.
    /// </summary>
    private static bool ComputerChecks(TacticalBoard board, int slot, int x, int y)
    {
        var context = board.Context;
        while (true)
        {
            var enemy = board.SlotAt(x, y);
            if (enemy < 0 || board.SideOf(enemy) == board.SideOf(slot))
            {
                return false;
            }

            var unit = board.Slots[slot];
            var distance = board.Distance(slot, enemy);
            if (distance == 1
                && (unit.Moves < 2 || unit.Shots < 1)
                && (unit.Type != context.ArcherType || unit.Shots < 1))
            {
                board.Slots[slot] = unit with { Target = enemy };
                board.Log.Add(new TacticalTargetSetEvent(slot, enemy));
                return true;
            }

            if (distance <= context.RangeOf(unit.Type) && unit.Moves > 0 && unit.Shots > 0 && unit.Target < 0)
            {
                TacticalShooting.Apply(board, slot, enemy);
                continue;
            }

            return false;
        }
    }

    /// <summary>
    /// The last-move danger test: an enemy in the 3 × 3 around the next path cell whose
    /// <c>S = (troops × quality div 100) × morale × M[its type][my type]</c> is at least the unit's own
    /// <c>S</c> against it.
    /// </summary>
    private static bool InDanger(TacticalBoard board, int slot, int px, int py)
    {
        var side = board.SideOf(slot);
        for (var cx = px - 1; cx <= px + 1; cx++)
        {
            for (var cy = py - 1; cy <= py + 1; cy++)
            {
                if (!board.OnBoard(cx, cy))
                {
                    continue;
                }

                var enemy = board.SlotAt(cx, cy);
                if (enemy < 0 || board.SideOf(enemy) == side)
                {
                    continue;
                }

                if (Strength(board, enemy, slot) >= Strength(board, slot, enemy))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary><c>S = (troops × quality div 100) × morale × M[mine][theirs]</c>, 32-bit.</summary>
    internal static int Strength(TacticalBoard board, int mine, int theirs)
    {
        var me = board.Slots[mine];
        var them = board.Slots[theirs];
        return unchecked(me.Troops * me.Quality / board.Rules.DangerQualityDivisor * me.Morale
                         * board.Context.Matrix(me.Type, them.Type));
    }

    private static bool Arrived(TacticalBoard board, int slot, int x, int y) =>
        board.Slots[slot].X == x && board.Slots[slot].Y == y;
}
