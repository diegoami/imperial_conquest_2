namespace IC2.Engine.Battle.Tactical.General;

/// <summary>
/// Pass 3, <c>FUN_0043ABB4</c>: <strong>[confirmed: code, static,
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §7, "Pass 3"]</strong>;
/// <c>docs/game-design.md</c>, "The computer general", "Pass 3".
/// </summary>
/// <remarks>
/// <list type="number">
/// <item><description>
/// <strong>The threat move.</strong> For each unit, in the general's order, with moves above 0 and no
/// target: the live enemies, in slot order, are sorted ascending by threat, <c>troops × quality × morale
/// × M[theirs][mine] div 10000</c>, an archers enemy with shots counting as <c>−shotBound</c> (its shot
/// bound <c>n</c> against this unit, <see cref="TacticalShooting.Bound"/>), so the most damage comes
/// first. The sort is the original's selection sort (<c>i</c> outer, <c>j = i + 1 …</c> inner, a swap on
/// a strict <c>&lt;</c>), not a library sort: its tie order is the original's. Taking the enemies in
/// that order, the unit moves toward the first with a clear line, or, if the line is blocked, toward
/// the nearest empty cell with a clear line in that enemy's box; an enemy with neither is passed over.
/// </description></item>
/// <item><description>Pass 2 (<see cref="GeneralPassTwo"/>).</description></item>
/// <item><description>
/// <strong>The flank.</strong> For each unit with no target whose moves are untouched (its type's full
/// moves) or are the slow advance's 1 with the counter below 10 and a type other than heavy infantry:
/// <c>h = −1</c> if the enemies' <c>minX ≤ 13 − maxX</c>, else <c>+1</c>; <c>Random(3) = 0</c> flips
/// <c>h</c>; <c>v</c> is the sign of (enemies below − enemies above), 0 counting as −1. It tries
/// <c>(x + moves × h, y + moves × v)</c>, then <c>(x + moves × h, y)</c>, each clamped to the board and
/// needing an empty cell and a clear line, and walks to the first that qualifies.
/// </description></item>
/// <item><description>Pass 2 again.</description></item>
/// </list>
/// <para>
/// Each of the four steps runs over all the general's units before the next starts. "13" is
/// <c>boardWidth − 1</c>; the 1, 10 and heavy infantry of the flank's second clause are the slow
/// advance's own fields (<c>slowAdvanceMoves</c>, <c>slowAdvanceHalfRounds</c>,
/// <c>slowAdvanceExemptType</c>), the counter read as it stands (after the setup's increment).
/// "Below" is a larger <c>y</c>.
/// </para>
/// </remarks>
public static class GeneralPassThree
{
    /// <summary>Runs pass 3 over the side to move's units.</summary>
    public static TacticalBattleState Run(
        TacticalBattleState state, TacticalContext context, IBattleDraws draws, IList<GeneralDecision>? journal = null)
    {
        var board = TacticalBoard.Thaw(state, context, draws);
        Apply(board, journal);
        return board.Freeze();
    }

    /// <summary>Only the flank step, over the side to move's units (for tests).</summary>
    public static TacticalBattleState Flank(
        TacticalBattleState state, TacticalContext context, IBattleDraws draws, IList<GeneralDecision>? journal = null)
    {
        var board = TacticalBoard.Thaw(state, context, draws);
        foreach (var slot in GeneralOrders.InOrder(board))
        {
            FlankUnit(board, slot, journal);
        }

        return board.Freeze();
    }

    /// <summary>Only the threat move, over the side to move's units (for tests).</summary>
    public static TacticalBattleState ThreatMove(
        TacticalBattleState state, TacticalContext context, IBattleDraws draws, IList<GeneralDecision>? journal = null)
    {
        var board = TacticalBoard.Thaw(state, context, draws);
        foreach (var slot in GeneralOrders.InOrder(board))
        {
            ThreatMoveUnit(board, slot, journal);
        }

        return board.Freeze();
    }

    /// <summary>The live enemies of unit <paramref name="slot"/>, in threat order (the selection sort).</summary>
    public static IReadOnlyList<int> ThreatOrder(TacticalBattleState state, int slot, TacticalContext context)
    {
        var board = TacticalBoard.Thaw(state, context, draws: null);
        return ThreatOrder(board, slot);
    }

    /// <summary>The threat of enemy <paramref name="enemy"/> to unit <paramref name="slot"/>.</summary>
    public static int Threat(TacticalBattleState state, int slot, int enemy, TacticalContext context)
    {
        var board = TacticalBoard.Thaw(state, context, draws: null);
        return Threat(board, slot, enemy);
    }

    internal static void Apply(TacticalBoard board, IList<GeneralDecision>? journal)
    {
        foreach (var slot in GeneralOrders.InOrder(board))
        {
            ThreatMoveUnit(board, slot, journal);
        }

        GeneralPassTwo.Apply(board, journal);

        foreach (var slot in GeneralOrders.InOrder(board))
        {
            FlankUnit(board, slot, journal);
        }

        GeneralPassTwo.Apply(board, journal);
    }

    internal static int Threat(TacticalBoard board, int slot, int enemy)
    {
        var them = board.Slots[enemy];
        if (them.Type == board.Context.ArcherType && them.Shots > 0)
        {
            return unchecked(-TacticalShooting.Bound(them, board.Slots[slot], board.Distance(enemy, slot), board.Context));
        }

        return GeneralTargeting.Score(board, slot, enemy);
    }

    internal static int[] ThreatOrder(TacticalBoard board, int slot)
    {
        var enemyFirst = board.FirstSlotOf(TacticalBoard.Other(board.SideOf(slot)));
        var enemies = new List<int>();
        for (var e = enemyFirst; e < enemyFirst + board.SlotsPerSide; e++)
        {
            if (board.IsLive(e))
            {
                enemies.Add(e);
            }
        }

        var order = enemies.ToArray();
        var keys = order.Select(e => Threat(board, slot, e)).ToArray();
        for (var i = 0; i < order.Length - 1; i++)
        {
            for (var j = i + 1; j < order.Length; j++)
            {
                if (keys[j] < keys[i])
                {
                    (order[i], order[j]) = (order[j], order[i]);
                    (keys[i], keys[j]) = (keys[j], keys[i]);
                }
            }
        }

        return order;
    }

    private static void ThreatMoveUnit(TacticalBoard board, int slot, IList<GeneralDecision>? journal)
    {
        var unit = board.Slots[slot];
        if (unit.Moves <= 0 || unit.Target >= 0)
        {
            return;
        }

        foreach (var enemy in ThreatOrder(board, slot))
        {
            var aim = board.Slots[enemy];
            if (GeneralLine.IsClear(board, slot, aim.X, aim.Y))
            {
                journal?.Add(new GeneralDecision(slot, GeneralStep.ThreatLine, enemy, aim.X, aim.Y));
                TacticalMovement.Walk(board, slot, aim.X, aim.Y);
                return;
            }

            if (GeneralLine.BoxCell(board, slot, enemy) is { } cell)
            {
                journal?.Add(new GeneralDecision(slot, GeneralStep.ThreatBox, enemy, cell.X, cell.Y));
                TacticalMovement.Walk(board, slot, cell.X, cell.Y);
                return;
            }
        }
    }

    private static void FlankUnit(TacticalBoard board, int slot, IList<GeneralDecision>? journal)
    {
        var rules = board.Rules;
        var context = board.Context;
        var unit = board.Slots[slot];
        if (unit.Target >= 0 || unit.Moves <= 0)
        {
            return;
        }

        var untouched = unit.Moves == context.MovesOf(unit.Type)
                        || (unit.Moves == rules.SlowAdvanceMoves
                            && board.Counter < rules.SlowAdvanceHalfRounds
                            && unit.Type != context.SlowAdvanceExemptType);
        if (!untouched)
        {
            return;
        }

        var enemyFirst = board.FirstSlotOf(TacticalBoard.Other(board.SideOf(slot)));
        var minX = int.MaxValue;
        var maxX = int.MinValue;
        var below = 0;
        var above = 0;
        for (var e = enemyFirst; e < enemyFirst + board.SlotsPerSide; e++)
        {
            if (!board.IsLive(e))
            {
                continue;
            }

            var enemy = board.Slots[e];
            minX = Math.Min(minX, enemy.X);
            maxX = Math.Max(maxX, enemy.X);
            if (enemy.Y > unit.Y)
            {
                below++;
            }
            else if (enemy.Y < unit.Y)
            {
                above++;
            }
        }

        var h = minX <= rules.BoardWidth - 1 - maxX ? -1 : 1;
        if (board.Draw(rules.FlankFlipChanceDenominator) == 0)
        {
            h = -h;
        }

        var v = Math.Sign(below - above);
        if (v == 0)
        {
            v = -1;
        }

        var moves = unit.Moves;
        var x = Math.Clamp(unit.X + (moves * h), 0, rules.BoardWidth - 1);
        var y = Math.Clamp(unit.Y + (moves * v), 0, rules.BoardHeight - 1);
        if (!Usable(board, slot, x, y))
        {
            y = unit.Y;
            if (!Usable(board, slot, x, y))
            {
                return;
            }
        }

        journal?.Add(new GeneralDecision(slot, GeneralStep.Flank, -1, x, y));
        TacticalMovement.Walk(board, slot, x, y);
    }

    private static bool Usable(TacticalBoard board, int slot, int x, int y) =>
        board.IsEmptyCell(x, y) && GeneralLine.IsClear(board, slot, x, y);
}
