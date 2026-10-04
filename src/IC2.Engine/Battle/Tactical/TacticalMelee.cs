namespace IC2.Engine.Battle.Tactical;

/// <summary>
/// The melee of the side that just moved, <c>FUN_004393EC</c>: <strong>[confirmed: code, static,
/// <c>2026-10-04-decompiled-tactical-battle-rules.md</c> §5]</strong>; <c>docs/game-design.md</c>,
/// "Melee".
/// </summary>
/// <remarks>
/// <para>
/// Each of the side's slots in order that has troops, a target, and a target with troops fights it.
/// With <c>a</c> the slot, <c>d</c> its target and <c>M</c> the ruleset's matrix,
/// <c>M[attackerType][defenderType]</c>:
/// </para>
/// <code>
/// f  = min(4, number of the side's slots, alive or not, whose target is d)       (at least 1)
/// A  = (M[type_a][type_d] × troops_a × (quality_a × 10 + morale_a)) div 2000 + 12
/// D  = (M[type_d][type_a] × troops_d × (quality_d × 10 + morale_d)) div 2000 + 12
/// nA = ((troops_a × D) div A) div 12 + 1
/// la = min(30000, ((Random(nA) + Random(nA)) × (5 − f)) div 5);  la = min(la, (troops_a × 4) div 10) + 1
/// nD = ((troops_d × A) div D) div 10 + 1
/// ld = min(30000, ((Random(nD) + Random(nD)) × (2f + 5)) div 5); ld = min(ld, (troops_d × 4) div 10) + 1
/// if troops_d div ld &lt; troops_a div la: morale_a = min(99, morale_a + 2); morale_d = min(99, morale_d − 3)
/// else:                                 morale_a = min(99, morale_a − 3); morale_d = min(99, morale_d + 2)
/// troops_a -= la; troops_d -= ld
/// Rout(a); Rout(d)
/// </code>
/// <para>
/// The ruleset carries the matrix, <c>2000</c> (<c>meleePowerDivisor</c>), <c>12</c>
/// (<c>meleeBasePowerFloor</c>), <c>30000</c> (<c>meleeLossHardCap</c>), the 40 % cap and its
/// <c>+ 1</c> (<c>meleeLossCapPercent</c>, <c>meleeLossCapOffset</c>), the focus cap 4, the exchange
/// divisors 12 and 10 and the morale deltas. The <c>quality × 10</c>, <c>(5 − f)</c>,
/// <c>(2f + 5)</c> and <c>div 5</c> are the formula's shape and stay here (T122's entry, "What stays
/// where it is"). Every product is 32-bit and wraps (<c>unchecked</c>): <c>troops_a × D</c> overflows
/// above about 23,000 troops against a strong defender, and the original computes the wrapped value.
/// </para>
/// </remarks>
public static class TacticalMelee
{
    /// <summary>The <c>× 10</c> on quality in the power terms (the formula's shape, report §5).</summary>
    private const int QualityWeight = 10;

    /// <summary>The <c>5</c> of <c>(5 − f)</c>, <c>(2f + 5)</c> and their <c>div 5</c> (the formula's shape, report §5).</summary>
    private const int FocusScale = 5;

    /// <summary>The <c>2</c> of <c>(2f + 5)</c> (the formula's shape, report §5).</summary>
    private const int DefenderFocusWeight = 2;

    /// <summary>The <c>100</c> the 40 % cap is a percentage of (<c>(troops × 4) div 10</c> as <c>× 40 div 100</c>).</summary>
    private const int Percent = 100;

    /// <summary>Resolves the melee of <paramref name="side"/>, the side that just moved.</summary>
    public static TacticalBattleState Resolve(TacticalBattleState state, int side, TacticalContext context, IBattleDraws draws)
    {
        var board = TacticalBoard.Thaw(state, context, draws);
        Apply(board, side);
        return board.Freeze();
    }

    internal static void Apply(TacticalBoard board, int side)
    {
        var first = board.FirstSlotOf(side);
        for (var a = first; a < first + board.SlotsPerSide; a++)
        {
            var attacker = board.Slots[a];
            if (attacker.Troops <= 0 || attacker.Target < 0 || !board.IsLive(attacker.Target))
            {
                continue;
            }

            Exchange(board, a, attacker.Target);
        }
    }

    private static void Exchange(TacticalBoard board, int a, int d)
    {
        var context = board.Context;
        var rules = board.Rules;
        var melee = context.Melee;

        var focus = 0;
        var first = board.FirstSlotOf(board.SideOf(a));
        for (var s = first; s < first + board.SlotsPerSide; s++)
        {
            if (board.Slots[s].Target == d)
            {
                focus++;
            }
        }

        focus = Math.Min(rules.MeleeFocusCap, focus);

        var att = board.Slots[a];
        var def = board.Slots[d];

        var powerA = unchecked((context.Matrix(att.Type, def.Type) * att.Troops * ((att.Quality * QualityWeight) + att.Morale))
                               / melee.MeleePowerDivisor) + melee.MeleeBasePowerFloor;
        var powerD = unchecked((context.Matrix(def.Type, att.Type) * def.Troops * ((def.Quality * QualityWeight) + def.Morale))
                               / melee.MeleePowerDivisor) + melee.MeleeBasePowerFloor;

        var boundA = (unchecked(att.Troops * powerD) / powerA / rules.MeleeAttackerExchangeDivisor) + 1;
        var drawA = unchecked(board.Draw(boundA) + board.Draw(boundA));
        var lossA = Math.Min(melee.MeleeLossHardCap, unchecked(drawA * (FocusScale - focus)) / FocusScale);
        lossA = Math.Min(lossA, unchecked(att.Troops * melee.MeleeLossCapPercent) / Percent) + melee.MeleeLossCapOffset;

        var boundD = (unchecked(def.Troops * powerA) / powerD / rules.MeleeDefenderExchangeDivisor) + 1;
        var drawD = unchecked(board.Draw(boundD) + board.Draw(boundD));
        var lossD = Math.Min(
            melee.MeleeLossHardCap,
            unchecked(drawD * ((DefenderFocusWeight * focus) + FocusScale)) / FocusScale);
        lossD = Math.Min(lossD, unchecked(def.Troops * melee.MeleeLossCapPercent) / Percent) + melee.MeleeLossCapOffset;

        int moraleA;
        int moraleD;
        if (def.Troops / lossD < att.Troops / lossA)
        {
            moraleA = Math.Min(rules.MoraleCap, att.Morale + rules.MoraleWinnerDelta);
            moraleD = Math.Min(rules.MoraleCap, def.Morale + rules.MoraleLoserDelta);
        }
        else
        {
            moraleA = Math.Min(rules.MoraleCap, att.Morale + rules.MoraleLoserDelta);
            moraleD = Math.Min(rules.MoraleCap, def.Morale + rules.MoraleWinnerDelta);
        }

        att = att with { Morale = moraleA, Troops = unchecked(att.Troops - lossA) };
        def = def with { Morale = moraleD, Troops = unchecked(def.Troops - lossD) };
        board.Slots[a] = att;
        board.Slots[d] = def;
        board.PaintIcon(a);
        board.PaintIcon(d);
        board.Log.Add(new TacticalMeleeEvent(
            a, d, focus, powerA, powerD, boundA, boundD, lossA, lossD, att.Troops, def.Troops, att.Morale, def.Morale));

        TacticalRout.Apply(board, a);
        TacticalRout.Apply(board, d);
    }
}
