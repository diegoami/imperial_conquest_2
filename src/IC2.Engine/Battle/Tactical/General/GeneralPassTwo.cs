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
/// theirs</c> below 100000000, among candidates passing <c>2 × theirs' div 3 &lt; mine</c>.
/// Strength is <c>((troops × quality) div 100) × morale × M[attacker][defender]</c>, with
/// 32-bit wrapping products and signed division; <c>focus</c> counts own target words, dead slots
/// included. Eligibility precedes strict minimization, so ties keep the first slot. Its moves are not
/// checked again, as the report gives the pass.
/// </description></item>
/// </list>
/// <para>
/// The <c>div 4</c> and the <c>2 × … div 3</c> are the pass's own shape (report §7); <c>combat.tactical</c>
/// has no field for them, so they are written here, cited (T124's entry, amended by plan PR #686). The key
/// is the report's plain <c>troops div s</c>, with no guard: the report gives none, so a bound of 0 divides
/// by zero here as the original's <c>div</c> would. No battle state the original can hold reaches it (the
/// troops word cannot hold the ≥ 131,070 troops whose 16-bit <c>min</c> makes the bound 0).
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

    /// <summary>The pass-2 shot key of enemy <paramref name="enemy"/> for unit <paramref name="slot"/>: <c>troops div (s + s div 4 if it shoots)</c>.</summary>
    public static int ShotKey(TacticalBattleState state, int slot, int enemy, TacticalContext context)
    {
        var board = TacticalBoard.Thaw(state, context, draws: null);
        return ShotKey(board, slot, enemy);
    }

    /// <summary>The pass-2 weighted melee key of enemy <paramref name="enemy"/> for unit <paramref name="slot"/>: <c>theirs − focus × theirs</c>, 32-bit, wrapping.</summary>
    public static long MeleeKey(TacticalBattleState state, int slot, int enemy, TacticalContext context)
    {
        var board = TacticalBoard.Thaw(state, context, draws: null);
        return MeleeKey(board, slot, enemy);
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
                if (!board.IsLive(e) || board.Distance(slot, e) > range)
                {
                    continue;
                }

                var key = ShotKey(board, slot, e);
                if (best < 0 || key < bestKey)
                {
                    best = e;
                    bestKey = key;
                }
            }

            if (best >= 0)
            {
                journal?.Add(new GeneralDecision(slot, GeneralStep.PassTwoShots, best, board.Slots[best].X, board.Slots[best].Y));
                // combat.tactical.shotsNeverNegative stops pass 2 before it spends a nonexistent shot.
                while (board.Slots[slot].Moves > 0 && board.IsLive(best)
                       && (!board.Rules.ShotsNeverNegative || board.Slots[slot].Shots > 0))
                {
                    TacticalShooting.Apply(board, slot, best);
                }
            }
        }

        if (board.IsOver)
        {
            return;
        }

        var melee = -1;
        var meleeTheirs = 100_000_000L; // Original FUN_0043A544's initial strict minimum.
        for (var e = enemyFirst; e < enemyFirst + board.SlotsPerSide; e++)
        {
            if (!board.IsLive(e) || board.Distance(slot, e) != 1)
            {
                continue;
            }

            var adjusted = MeleeKey(board, slot, e);
            var odds = (board.Rules.WideArithmetic ? checked(OddsNumerator * adjusted)
                : unchecked(OddsNumerator * (int)adjusted)) / OddsDenominator;
            if (odds < Strength(board, slot, e) && adjusted < meleeTheirs)
            {
                melee = e;
                meleeTheirs = adjusted;
            }
        }

        if (melee >= 0)
        {
            journal?.Add(new GeneralDecision(slot, GeneralStep.PassTwoTarget, melee, board.Slots[melee].X, board.Slots[melee].Y));
            GeneralOrders.SetTarget(board, slot, melee);
        }
    }

    internal static long MeleeKey(TacticalBoard board, int slot, int enemy)
    {
        var theirs = Strength(board, enemy, slot);
        // Preserve the same operation boundaries in the checked-wide variant.
        var focus = GeneralTargeting.Focus(board, board.SideOf(slot), enemy);
        return board.Rules.WideArithmetic ? checked(theirs - (focus * theirs))
            : unchecked((int)theirs - (focus * (int)theirs));
    }

    private static long Strength(TacticalBoard board, int attacker, int defender)
    {
        var unit = board.Slots[attacker];
        var multiplier = board.Context.Matrix(unit.Type, board.Slots[defender].Type);
        // FUN_0043A544, products 39899–39926: truncate BEFORE morale and matrix products.
        return board.Rules.WideArithmetic
            ? checked((((long)unit.Troops * unit.Quality) / 100) * unit.Morale * multiplier)
            : unchecked(((unit.Troops * unit.Quality) / 100) * unit.Morale * multiplier);
    }

    internal static int ShotKey(TacticalBoard board, int slot, int enemy)
    {
        var unit = board.Slots[slot];
        var them = board.Slots[enemy];
        var s = TacticalShooting.Bound(unit, them, board.Distance(slot, enemy), board.Context);
        if (them.Shots > 0)
        {
            s = unchecked(s + (s / ShooterBonusDivisor));
        }

        return them.Troops / s;
    }
}
