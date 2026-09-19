using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Battle;

/// <summary>
/// The instant resolver's casualty arithmetic: the ratio a battle's power gap produces, and the per-unit
/// loss that ratio drives.
/// </summary>
/// <remarks>
/// <para>
/// <strong>It is a ratio, not a troop count (DoD 3).</strong> <c>FUN_0044AEE4</c> calls
/// <c>applyCasualties(winner, loserPower * 40 / winnerPower)</c>, and <c>applyCasualties</c> is
/// <c>FUN_0044AE20(armyIdx, ratio)</c> — the expression is that function's <c>ratio</c> <em>argument</em>.
/// The function's body then applies, to every unit in the army,
/// </para>
/// <code>
/// troops -= troops / (Random(15) + 105) * ratio
/// </code>
/// <para>
/// (<c>decompiled-diplomacy-peace-terms-and-instant-battles.md</c> for the call site,
/// <c>decompiled-defection-and-siege-attrition.md</c> for the body, transcribed in the T04 corpus as
/// <c>siege.attritionFormula</c> — it is the same helper the siege path calls on every attempt). So the
/// ratio is a per-unit multiplier: a divisor in <c>[105, 120)</c> means one unit loses between
/// <c>ratio/120</c> and <c>ratio/105</c> of its strength, and an even fight (ratio 40) costs each unit
/// 33–38 % of its troops.
/// </para>
/// <para>
/// <strong>Why this needed saying.</strong> T16's first round read the expression as a troop count,
/// because the DoD line said so and because the <c>15</c> and the <c>105</c> had no home in the ruleset.
/// Quantified, the count reading is indefensible: powers of 5,000 against 5,200 would have cost the
/// winner <em>38 troops in total</em>, whatever the size of the army. The user settled it on the
/// evidence; <see cref="CombatRules.CasualtyDivisorBase"/> and
/// <see cref="CombatRules.CasualtyDivisorRandomSpan"/> exist for exactly this, and neither number is
/// written here.
/// </para>
/// <para>
/// <strong>Integer semantics, pinned.</strong> Two truncations, in this order and no other.
/// <see cref="Ratio"/> multiplies before it divides, once: read as <c>loserPower * (40 / winnerPower)</c>
/// the inner quotient truncates to zero for every <c>winnerPower</c> above the numerator, i.e. for every
/// real battle. <see cref="Apply"/> divides <c>troops</c> by the drawn divisor <em>first</em> and
/// multiplies by the ratio second, exactly as the decompiled body writes it: the other grouping,
/// <c>(troops * ratio) / divisor</c>, is a different number for almost every input — 2,000 troops at
/// ratio 23 and divisor 109 gives <c>18 * 23 = 414</c> one way and <c>46,000 / 109 = 422</c> the other.
/// <c>BattleCasualtyArithmeticTests</c> pins both.
/// </para>
/// <para>
/// <strong>What this file is not.</strong> It is not the tactical exchange loop. No type-effectiveness
/// matrix, no 40 %-of-own-troops melee cap, no per-unit tactical morale, no shooting-vulnerability
/// weight, and above all no rout mechanic: nothing is ever removed for falling below a battalion-size
/// threshold, no morale cascade runs, and no unit slot is deleted here at all. Those are held in reserve
/// for a possible future detailed resolver (<c>docs/game-design.md</c> §Combat,
/// <c>docs/task-catalogue.md</c> T16 Hazards). The instant resolver annihilates the loser wholesale; the
/// per-unit expression above is the only attrition it tracks.
/// </para>
/// </remarks>
public static class BattleCasualties
{
    /// <summary>
    /// The casualty <em>ratio</em>: <c>numeratorPower × numerator / divisorPower</c>, multiplication
    /// first, one truncating division. Feed it to <see cref="Apply"/>; it is not a troop count.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>The two powers are named for their place in the expression, not for which side they come
    /// from, and that is deliberate.</strong> This function is called two ways round. The confirmed
    /// forward call is <c>Ratio(loserPower, winnerPower, WinnerCasualtyNumerator)</c> — what the winner
    /// pays. The <c>improved</c> ruleset's mirrored call is
    /// <c>Ratio(winnerPower, loserPower, SurvivorCasualtyNumerator)</c>, where the <em>loser's</em>
    /// strength is the divisor. An earlier revision named these parameters <c>loserPower</c> and
    /// <c>winnerPower</c> and justified its degenerate-case guard with "the winner is by construction the
    /// side with the greater-or-equal strength, so a non-positive divisor can only mean two empty
    /// armies". That reasoning is true of the forward call and false of the mirrored one, and the names
    /// were what made it look true of both — so the names are gone.
    /// </para>
    /// <para>
    /// <strong>Both degenerate cases are real, and they are not the same case.</strong>
    /// <see cref="Strength.ArmyPower.Compute"/> truncates to zero whenever a force's weighted troops fall
    /// below <see cref="CombatRules.PowerDivisor"/> — a small, depleted army reaches that easily, for
    /// instance one that scattered out of an earlier defeat and fought again before recovering — and
    /// <see cref="Strength.FleetPower.Compute"/> has the same floor for a low-ship, low-condition fleet.
    /// So:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <paramref name="numeratorPower"/> zero means the side being measured <em>against</em> has no
    /// strength at all, so the ratio is genuinely zero. This is the forward call's "two empty armies"
    /// case, and it is checked first so that <c>0 / 0</c> resolves to "no casualties" rather than to the
    /// saturating branch below.
    /// </description></item>
    /// <item><description>
    /// <paramref name="divisorPower"/> zero with a positive numerator means the ratio is unbounded: the
    /// force being hit has no strength left and is overwhelmed. This returns
    /// <see cref="int.MaxValue"/> as a saturating value, which <see cref="Apply"/> then clamps slot by
    /// slot to each slot's own troops — so the outcome is "everything", which is what an unbounded ratio
    /// means, and the caller's own no-survivors branch turns it into the destroyed outcome. Returning
    /// zero here, as an earlier revision did, inverted the rule exactly: the <em>weakest</em> possible
    /// loser walked away untouched.
    /// </description></item>
    /// </list>
    /// </remarks>
    /// <param name="numeratorPower">
    /// The strength on top of the fraction: the <em>loser's</em> for the forward call, the
    /// <em>winner's</em> for the mirrored one.
    /// </param>
    /// <param name="divisorPower">
    /// The strength underneath: the <em>winner's</em> for the forward call, the <em>loser's</em> for the
    /// mirrored one. See the remarks for what a zero here means.
    /// </param>
    /// <param name="numerator">
    /// <see cref="CombatRules.WinnerCasualtyNumerator"/> for the winner's own losses, or
    /// <see cref="ScatteredDefeatRules.SurvivorCasualtyNumerator"/> for the <c>improved</c> ruleset's
    /// mirrored ratio.
    /// </param>
    public static int Ratio(int numeratorPower, int divisorPower, int numerator)
    {
        if (numeratorPower <= 0)
        {
            return 0;
        }

        if (divisorPower <= 0)
        {
            // Unbounded, saturated. Apply clamps it to each slot's own troops; see the remarks.
            return int.MaxValue;
        }

        // Multiplication first, then ONE truncating division -- see the class remarks.
        return (numeratorPower * numerator) / divisorPower;
    }

    /// <summary>
    /// Applies <paramref name="ratio"/> to <paramref name="units"/> as <c>FUN_0044AE20</c> does: for each
    /// slot, <c>troops -= troops / (Random(span) + base) × ratio</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>One draw per slot, in slot order, unconditionally</strong> — the decompiled function loops
    /// over every unit in the army rather than over the occupied ones, so a slot already at zero troops
    /// still consumes its draw and still loses nothing. Keeping the draw unconditional is what makes the
    /// sequence depend only on the slot count, which is what makes a seeded assertion exact.
    /// </para>
    /// <para>
    /// <strong>The only clamp is non-negativity.</strong> A slot cannot lose more troops than it has, so
    /// the computed loss is capped at the slot's own count. That is an arithmetic guard, not the tactical
    /// path's 40 %-of-own-troops loss cap, which is reserve research and is not applied here — and it
    /// genuinely binds only for a very lopsided <c>improved</c> defeat, where the mirrored ratio can
    /// exceed the divisor. <strong>A slot reduced to zero is not removed</strong>; removing a unit for
    /// being small is the rout mechanic.
    /// </para>
    /// </remarks>
    /// <param name="units">The force's unit slots.</param>
    /// <param name="ratio">The ratio from <see cref="Ratio"/>. Zero or negative applies nothing, but still draws.</param>
    /// <param name="rng">The battle's stream. One draw per slot.</param>
    /// <param name="rules">Supplies the divisor base and its random span.</param>
    /// <returns>The reduced slots, the per-slot losses, and the total troops actually lost.</returns>
    public static (ValueList<UnitSlot> Units, ValueList<UnitCasualty> Losses, int Applied) Apply(
        ValueList<UnitSlot> units,
        int ratio,
        IRng rng,
        CombatRules rules)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(rules);

        var reduced = new UnitSlot[units.Count];
        var report = new List<UnitCasualty>();
        var applied = 0;

        for (var i = 0; i < units.Count; i++)
        {
            var unit = units[i];

            // Random(15) + 105 -- drawn for every slot, occupied or not, as the decompiled loop does.
            var divisor = rng.NextInt(rules.CasualtyDivisorRandomSpan) + rules.CasualtyDivisorBase;

            // troops / divisor FIRST, then x ratio -- the decompiled order, which is not the same number
            // as (troops x ratio) / divisor. Widened to long only so that a large ratio on a large slot
            // cannot overflow before the clamp below; the arithmetic itself is the original's.
            var loss = ratio <= 0 ? 0L : (long)(unit.Troops / divisor) * ratio;
            var clamped = (int)Math.Min(unit.Troops, loss);

            reduced[i] = unit with { Troops = unit.Troops - clamped };
            applied += clamped;

            if (clamped > 0)
            {
                report.Add(new UnitCasualty(i, unit.Name, unit.Troops, clamped));
            }
        }

        return (ValueList.From(reduced), ValueList.From(report), applied);
    }

    /// <summary>
    /// The instant path's own promotion rule (DoD 5): every surviving unit is raised to at least
    /// <see cref="CombatRules.QualityFloor"/>, then a <c>1</c>-in-<see cref="CombatRules.PromotionChanceDenominator"/>
    /// roll promotes it one further tier, capped at <see cref="CombatRules.QualityCap"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Transcribed from <c>FUN_0044AEE4</c>: <c>quality = max(quality, 6); if (random(4) == 0)
    /// quality = min(quality + 1, 9);</c>. The roll is drawn for every surviving unit, in slot order,
    /// whether or not the floor already moved it — so the draw sequence depends only on how many units
    /// survived, which is what makes DoD 5's "exactly the 1-in-4 further promotions fire for the seeded
    /// roll" an exact assertion.
    /// </para>
    /// <para>
    /// <strong>This is the only promotion rule in the game.</strong> The tactical path's slot-adjacency
    /// rule was withdrawn outright (<c>design-audit.md</c> §2.10, and this task's own corpus correction
    /// to <c>battle.tactical.adjacencyPromotionRule</c>): replayed from the identical save it fails three
    /// ways, and what fits both recorded battles is this same uniform roll. Nothing adjacency-shaped
    /// belongs anywhere near this method.
    /// </para>
    /// <para>
    /// A unit at zero troops is not a survivor and is neither promoted nor rolled for. It is also not
    /// removed — see <see cref="Apply"/>.
    /// </para>
    /// </remarks>
    /// <param name="units">The winner's slots, already reduced by <see cref="Apply"/>.</param>
    /// <param name="rng">The battle's stream. One draw per surviving unit, in slot order.</param>
    /// <param name="rules">Supplies the floor, the cap and the roll's denominator.</param>
    public static (ValueList<UnitSlot> Units, ValueList<UnitPromotion> Promotions) Promote(
        ValueList<UnitSlot> units,
        IRng rng,
        CombatRules rules)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(rules);

        var promoted = new UnitSlot[units.Count];
        var report = new List<UnitPromotion>();

        for (var i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            if (unit.Troops <= 0)
            {
                promoted[i] = unit;
                continue;
            }

            var before = unit.Quality;
            var quality = Math.Max(before, rules.QualityFloor);

            // random(4) == 0 -- transcribed as the original writes it, so the ruleset's denominator is
            // the only number involved and no "1-in-N numerator" has to be invented.
            var rolled = rng.NextInt(rules.PromotionChanceDenominator) == 0;
            if (rolled)
            {
                quality = Math.Min(quality + 1, rules.QualityCap);
            }

            promoted[i] = unit with { Quality = quality };
            report.Add(new UnitPromotion(i, unit.Name, before, quality, rolled));
        }

        return (ValueList.From(promoted), ValueList.From(report));
    }
}
