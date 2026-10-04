namespace IC2.Engine.Battle.Tactical;

/// <summary>
/// A shot, <c>FUN_0043910C</c>: <strong>[confirmed: code, static,
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §4]</strong>; <c>docs/game-design.md</c>,
/// "Shooting".
/// </summary>
/// <remarks>
/// <code>
/// shots_s -= 1; moves_s -= 1; target_s = none
/// base = (troops_s × quality_s × morale_s × vuln[type_t]) div (troops_s × 5 + 150000)     (32-bit)
/// if distance(s, t) &lt; range[type_s]: base × 2                       (only archers at distance 1)
/// n    = min(base, min(troops_s div 3, troops_t div 2)) + 1          (the min taken in 16 bits)
/// loss = Random(n) + Random(n)
/// morale_t -= min(3, (loss × 35) div (troops_t + 1))                 (troops before the loss)
/// troops_t -= loss
/// Rout(t)
/// </code>
/// The products are 32-bit and wrap (<c>unchecked</c>, the design's "Integer semantics"). The routine
/// checks nothing: the caller decides whether the shot is allowed, which is how the computer general's
/// pass 2 drives shots below 0 (T124).
/// </remarks>
public static class TacticalShooting
{
    /// <summary>Slot <paramref name="shooter"/> shoots slot <paramref name="target"/>.</summary>
    public static TacticalBattleState Shoot(
        TacticalBattleState state, int shooter, int target, TacticalContext context, IBattleDraws draws)
    {
        var board = TacticalBoard.Thaw(state, context, draws);
        Apply(board, shooter, target);
        return board.Freeze();
    }

    /// <summary>
    /// The shot bound <c>n</c>, its <c>+ 1</c> included: exposed so the computer general's pass 2 (T124)
    /// reads the same expression the shot uses.
    /// </summary>
    public static int Bound(TacticalSlot shooter, TacticalSlot target, int distance, TacticalContext context)
    {
        ArgumentNullException.ThrowIfNull(shooter);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(context);

        var rules = context.Rules;
        var numerator = unchecked(shooter.Troops * shooter.Quality * shooter.Morale * context.VulnerabilityOf(target.Type));
        var denominator = unchecked((shooter.Troops * rules.ShotTroopsFactor) + rules.ShotDivisorBase);
        var shotBase = numerator / denominator;
        if (distance < context.RangeOf(shooter.Type))
        {
            shotBase = unchecked(shotBase * context.Melee.InRangeShotMultiplier);
        }

        var caps = Min16(shooter.Troops / rules.ShotShooterTroopsDivisor, target.Troops / rules.ShotTargetTroopsDivisor);
        return Min16(shotBase, caps) + 1;
    }

    /// <summary>
    /// The 16-bit <c>min</c> the shot bound takes (report §4): both operands truncated to a signed 16-bit
    /// word, the original's slot fields being signed words, and the smaller returned sign-extended.
    /// </summary>
    public static int Min16(int a, int b) => Math.Min(unchecked((short)a), unchecked((short)b));

    internal static void Apply(TacticalBoard board, int shooter, int target)
    {
        var rules = board.Rules;
        var s = board.Slots[shooter];
        s = s with
        {
            Shots = unchecked(s.Shots - 1),
            Moves = unchecked(s.Moves - 1),
            Target = TacticalSlot.NoTarget,
        };
        board.Slots[shooter] = s;

        var t = board.Slots[target];
        var n = Bound(s, t, board.Distance(shooter, target), board.Context);
        var loss = unchecked(board.Draw(n) + board.Draw(n));
        var moraleLoss = Math.Min(rules.ShotMoraleCap, unchecked(loss * rules.ShotMoraleNumerator) / unchecked(t.Troops + 1));
        t = t with
        {
            Morale = unchecked(t.Morale - moraleLoss),
            Troops = unchecked(t.Troops - loss),
        };
        board.Slots[target] = t;
        board.PaintIcon(target);
        board.Log.Add(new TacticalShotEvent(shooter, target, n, loss, moraleLoss, t.Troops, t.Morale));

        TacticalRout.Apply(board, target);
    }
}
