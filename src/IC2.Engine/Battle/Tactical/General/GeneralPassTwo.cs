namespace IC2.Engine.Battle.Tactical.General;

/// <summary>
/// Pass 2, <c>FUN_0043A544</c>: <strong>[confirmed: code, static,
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §7, "Pass 2"]</strong>;
/// <c>docs/game-design.md</c>, "The computer general", "Pass 2".
/// </summary>
/// <remarks>
/// <para>
/// For each of the general's units, in its order, with moves above 0 and no target:
/// </para>
/// <list type="number">
/// <item><description>
/// If it has shots, it takes the in-range enemy (distance ≤ the unit's range) with the smallest
/// <c>troops div s</c>, where <c>s</c> is the shot bound <c>n</c> (<see cref="TacticalShooting.Bound"/>,
/// its <c>+ 1</c> included) plus <c>s div 4</c> when that enemy has shots; ties go to the first, by a
/// strict <c>&lt;</c>. It fires at it until its moves are 0 or the target is removed. <strong>The loop
/// does not check shots</strong>, so shots can go below 0; the shot routine checks nothing either.
/// </description></item>
/// <item><description>
/// It then takes as melee target the adjacent enemy with the smallest <c>theirs' = theirs − focus ×
/// theirs</c> (troops; <c>focus</c> the own slots, alive or not, already targeting it; 32-bit, wrapping),
/// ties to the first, provided <c>2 × theirs' div 3 &lt; mine</c> (the unit's troops). Its moves are not
/// checked again, as the report gives the pass.
/// </description></item>
/// </list>
/// <para>
/// The <c>div 4</c> and the <c>2 × … div 3</c> are the pass's own shape (report §7); <c>combat.tactical</c>
/// has no field for them (T122's entry), so they are written here, cited. A bound <c>s</c> of 0, which a
/// wrapped 32-bit shot base could give, would divide by zero in the original; the port skips that enemy
/// rather than crash <strong>[designed: the report does not reach the case]</strong>.
/// </para>
/// </remarks>
public static class GeneralPassTwo
{
    /// <summary>The shooter bonus divisor: <c>s + s div 4</c> for an enemy with shots (report §7).</summary>
    private const int ShooterBonusDivisor = 4;

    /// <summary>The <c>2</c> of <c>2 × theirs' div 3 &lt; mine</c> (report §7).</summary>
    private const int OddsNumerator = 2;

    /// <summary>The <c>3</c> of <c>2 × theirs' div 3 &lt; mine</c> (report §7).</summary>
    private const int OddsDenominator = 3;

    /// <summary>Runs pass 2 over the side to move's units.</summary>
    public static TacticalBattleState Run(
        TacticalBattleState state, TacticalContext context, IBattleDraws draws, IList<GeneralDecision>? journal = null)
    {
        var board = TacticalBoard.Thaw(state, context, draws);
        Apply(board, journal);
        return board.Freeze();
    }

    /// <summary>The pass-2 shot key of enemy <paramref name="enemy"/> for unit <paramref name="slot"/>: <c>troops div (s + s div 4 if it shoots)</c>, or <see langword="null"/> for a zero bound.</summary>
    public static int? ShotKey(TacticalBattleState state, int slot, int enemy, TacticalContext context)
    {
        var board = TacticalBoard.Thaw(state, context, draws: null);
        return ShotKey(board, slot, enemy);
    }

    internal static void Apply(TacticalBoard board, IList<GeneralDecision>? journal)
    {
        foreach (var slot in GeneralOrders.InOrder(board))
        {
            ApplyUnit(board, slot, journal);
        }
    }

    internal static void ApplyUnit(TacticalBoard board, int slot, IList<GeneralDecision>? journal)
    {
        var unit = board.Slots[slot];
        if (unit.Moves <= 0 || unit.Target >= 0)
        {
            return;
        }

        var enemyFirst = board.FirstSlotOf(TacticalBoard.Other(board.SideOf(slot)));
        if (unit.Shots > 0)
        {
            var range = board.Context.RangeOf(unit.Type);
            var best = -1;
            var bestKey = 0;
            for (var e = enemyFirst; e < enemyFirst + board.SlotsPerSide; e++)
            {
                if (!board.IsLive(e) || board.Distance(slot, e) > range || ShotKey(board, slot, e) is not { } key)
                {
                    continue;
                }

                if (best < 0 || key < bestKey)
                {
                    best = e;
                    bestKey = key;
                }
            }

            if (best >= 0)
            {
                journal?.Add(new GeneralDecision(slot, GeneralStep.PassTwoShots, best, board.Slots[best].X, board.Slots[best].Y));
                while (board.Slots[slot].Moves > 0 && board.IsLive(best))
                {
                    TacticalShooting.Apply(board, slot, best);
                }
            }
        }

        if (board.IsOver)
        {
            return;
        }

        var side = board.SideOf(slot);
        var melee = -1;
        var meleeTheirs = 0;
        for (var e = enemyFirst; e < enemyFirst + board.SlotsPerSide; e++)
        {
            if (!board.IsLive(e) || board.Distance(slot, e) != 1)
            {
                continue;
            }

            var theirs = board.Slots[e].Troops;
            var adjusted = unchecked(theirs - (GeneralTargeting.Focus(board, side, e) * theirs));
            if (melee < 0 || adjusted < meleeTheirs)
            {
                melee = e;
                meleeTheirs = adjusted;
            }
        }

        if (melee >= 0 && unchecked(OddsNumerator * meleeTheirs) / OddsDenominator < board.Slots[slot].Troops)
        {
            journal?.Add(new GeneralDecision(slot, GeneralStep.PassTwoTarget, melee, board.Slots[melee].X, board.Slots[melee].Y));
            GeneralOrders.SetTarget(board, slot, melee);
        }
    }

    internal static int? ShotKey(TacticalBoard board, int slot, int enemy)
    {
        var unit = board.Slots[slot];
        var them = board.Slots[enemy];
        var s = TacticalShooting.Bound(unit, them, board.Distance(slot, enemy), board.Context);
        if (them.Shots > 0)
        {
            s = unchecked(s + (s / ShooterBonusDivisor));
        }

        return s == 0 ? null : them.Troops / s;
    }
}
