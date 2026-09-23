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
/// weight, and above all no rout mechanic: no morale cascade ever runs here. <see cref="Apply"/>'s own
/// clamp never removes a unit for falling below a battalion-size threshold — <strong>that is a separate
/// pass</strong>, <see cref="DeleteBelowThreshold"/> (bug #289, corrected here; an earlier revision of
/// this remark said no such pass existed anywhere in the instant path, which was the bug), and it is a
/// different rule from the tactical rout floor (<c>size / 25</c>, reserve research, still not
/// implemented here). Those are held in reserve for a possible future detailed resolver
/// (<c>docs/game-design.md</c> §Combat, <c>docs/task-catalogue.md</c> T16 Hazards). The instant resolver
/// annihilates the loser wholesale; <see cref="Apply"/>'s per-unit expression and
/// <see cref="DeleteBelowThreshold"/>'s small-unit sweep are the only attrition the winning side (or a
/// besieging attacker) tracks.
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
    /// <strong>The only clamp <em>this method</em> applies is non-negativity.</strong> A slot cannot lose
    /// more troops than it has, so the computed loss is capped at the slot's own count. That is an
    /// arithmetic guard, not the tactical path's 40 %-of-own-troops loss cap, which is reserve research
    /// and is not applied here — and it genuinely binds only for a very lopsided <c>improved</c> defeat,
    /// where the mirrored ratio can exceed the divisor. <strong>A slot reduced to zero by this method is
    /// not itself removed</strong> — and, per <see cref="DeleteBelowThreshold"/>'s own remarks, a
    /// zero-troop slot is not what that pass removes either, since its guard requires <c>troops &gt;
    /// 0</c>; only a unit left <em>positive but small</em> is deleted, by the caller's separate call to
    /// <see cref="DeleteBelowThreshold"/> (bug #289). Removing a unit for falling below the <em>tactical</em>
    /// rout floor (<c>size / 25</c>) is a different, still-unimplemented reserve rule.
    /// </para>
    /// </remarks>
    /// <param name="units">The force's unit slots.</param>
    /// <param name="ratio">The ratio from <see cref="Ratio"/>. Zero or negative applies nothing, but still draws.</param>
    /// <param name="rng">The battle's stream. One draw per slot.</param>
    /// <param name="rules">Supplies the divisor base and its random span.</param>
    /// <returns>The reduced slots, the per-slot losses, and the total troops actually lost.</returns>
    /// <remarks>
    /// <strong>The saturating ratio is special-cased (T52 DoD 5).</strong> <see cref="Ratio"/> returns
    /// <see cref="int.MaxValue"/> to mean "unbounded" when its divisor power is zero, meant to take a
    /// whole slot regardless of that slot's own size. But the general grouping divides
    /// <c>troops / divisor</c> <em>first</em>, and that truncates to zero for any slot smaller than the
    /// divisor — so <c>0 × int.MaxValue = 0</c> let a slot escape untouched exactly when the rule meant to
    /// take everything. Proved by T16's reviewer: 300 zero-power troops in one slot were wiped, the same
    /// 300 split into three 100-troop slots survived untouched. Only the saturating ratio takes this
    /// branch; the sub-divisor truncation of an ordinary ratio (<c>100 / 105 = 0</c>) is deliberate and
    /// <c>[confirmed]</c> everywhere else, and is untouched here.
    /// </remarks>
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

            long loss;
            if (ratio == int.MaxValue)
            {
                // Saturating: the whole slot, unconditionally -- see the remarks above. The general
                // divide-first grouping below is not run at all for this one case.
                loss = unit.Troops;
            }
            else
            {
                // troops / divisor FIRST, then x ratio -- the decompiled order, which is not the same
                // number as (troops x ratio) / divisor. Widened to long only so that a large ratio on a
                // large slot cannot overflow before the clamp below; the arithmetic itself is the
                // original's.
                loss = ratio <= 0 ? 0L : (long)(unit.Troops / divisor) * ratio;
            }

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
    /// The <c>improved</c> ruleset's mirrored casualty figure, scaled to a beaten <em>fleet's</em> own
    /// hull count (T52 DoD 1): <c>lost = min(ships, (ships × ratio) / (Random(span) + base))</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Why a fleet needs its own method rather than reusing <see cref="Apply"/>.</strong> A fleet
    /// has no unit slots, and <see cref="Apply"/>'s per-unit expression divides <c>troops / divisor</c>
    /// <em>first</em> — for that expression's confirmed source, this is the correct, deliberate order
    /// (see its own remarks). But a fleet's hull count is small enough, and the divisor large enough
    /// (<c>[105, 120)</c>), that dividing a fleet's own hull count by the divisor first truncates to zero
    /// for every fleet at or below the divisor's range, which is every fleet the game can have (they cap
    /// at 100 hulls) — leaving every beaten fleet untouched regardless of the ratio. T16 shipped a hull
    /// <em>count</em> reading instead (<c>min(ships, ratio)</c>), which fixed the truncation but introduced
    /// a flat floor: the mirrored ratio is never below its own numerator (40), so any fleet at or below 40
    /// hulls was annihilated exactly as under <c>classical-faithful</c>, and a larger fleet's loss did not
    /// scale with its own size at all.
    /// </para>
    /// <para>
    /// <strong>Multiply first, on purpose.</strong> This method multiplies the fleet's own hull count into
    /// the ratio <em>before</em> dividing by the drawn divisor, which is the opposite grouping from
    /// <see cref="Apply"/>. That is deliberate, not an inconsistency: multiplying first is what keeps the
    /// loss proportional to the fleet's own size instead of truncating to zero or to a flat floor. Widened
    /// to <see langword="long"/> so a saturating <paramref name="ratio"/> (<see cref="int.MaxValue"/>)
    /// times up to 100 ships cannot overflow before the clamp — and needs no special case for it the way
    /// <see cref="Apply"/> does, because multiplying first before dividing never truncates the saturating
    /// case to zero in the first place.
    /// </para>
    /// <para>
    /// <strong>One draw, unconditionally</strong> — reusing <see cref="CombatRules.CasualtyDivisorBase"/>
    /// and <see cref="CombatRules.CasualtyDivisorRandomSpan"/>, the same two fields <see cref="Apply"/>
    /// draws from, so the two halves of one setting cannot diverge again. No third ruleset field is added.
    /// </para>
    /// </remarks>
    /// <param name="ships">The beaten fleet's own hull count.</param>
    /// <param name="ratio">The mirrored ratio from <see cref="Ratio"/>. Zero or negative loses nothing, but still draws.</param>
    /// <param name="rng">The battle's stream. One draw.</param>
    /// <param name="rules">Supplies the divisor base and its random span.</param>
    /// <returns>The hulls lost, capped at <paramref name="ships"/>.</returns>
    public static int ApplyToFleet(int ships, int ratio, IRng rng, CombatRules rules)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(rules);

        // Drawn unconditionally, exactly as Apply's per-slot draw is unconditional, so a later draw in the
        // same battle (the scatter distance) sits at a fixed position regardless of this ratio's value.
        var divisor = rng.NextInt(rules.CasualtyDivisorRandomSpan) + rules.CasualtyDivisorBase;

        if (ratio <= 0)
        {
            return 0;
        }

        // (ships x ratio) / divisor -- multiply first; see the remarks for why this is the other grouping
        // from Apply's, on purpose.
        var lost = ((long)ships * ratio) / divisor;
        return (int)Math.Min(ships, lost);
    }

    /// <summary>
    /// <c>FUN_0044AE20</c>'s <strong>second</strong> pass (bug #289): deletes every unit left with
    /// <c>troops &gt; 0</c> but below its own small-unit threshold — <c>standardBattalionSize /
    /// </c><see cref="CombatRules.DeletionDivisorNational"/> for a national unit,
    /// <c>standardBattalionSize / </c><see cref="CombatRules.DeletionDivisorMercenary"/> for a mercenary
    /// one. Called after <see cref="Apply"/> and before any promotion roll, at every casualty call site:
    /// the field winner, the siege attacker, and a naval or storm winner's carried army — including a
    /// naval or storm pass whose ratio was <c>0</c>, since this pass reads only the post-casualty troop
    /// count, not the ratio that produced it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>A slot at exactly zero troops is not touched by this pass.</strong> The decompiled loop's
    /// own guard is <c>0 &lt; troops &amp;&amp; troops &lt; threshold</c> — a unit <see cref="Apply"/>
    /// already reduced to zero fails the <c>0 &lt; troops</c> half and is left in place, neither deleted
    /// here nor promoted later (<see cref="Promote"/> already skips a zero-troop slot on its own terms).
    /// This reads as a quirk but is exactly what the original does; inventing a "troops == 0 also
    /// deletes" branch would be a different function from the one the report transcribes.
    /// </para>
    /// <para>
    /// <strong>No swap-with-last here, unlike the naval whole-unit loss.</strong> The original's own
    /// removal helper (<c>FUN_0044ac3c</c>) does swap-remove a fixed-size slot array, but this pass is a
    /// deterministic full sweep — every slot below its threshold is deleted, not a single slot chosen at
    /// random — so a stable filter produces the exact same surviving <em>set</em> as a literal
    /// swap-and-shrink replay would, without needing this port to reproduce the original's fixed 20-slot
    /// array shape or its own effect on later per-slot draw order. <see cref="Promote"/> already draws
    /// "one roll per surviving unit, in slot order" rather than replaying the original's own fixed
    /// per-slot draw pattern (T63 Decision 6's "one draw" departure), so slot order past this pass was
    /// never exactly faithful to begin with.
    /// </para>
    /// </remarks>
    /// <param name="units">The force's unit slots, already reduced by <see cref="Apply"/>.</param>
    /// <param name="ruleset">
    /// Supplies <see cref="Ruleset.UnitTypeById"/> (for each unit's own <c>standardBattalionSize</c>) and
    /// <see cref="Ruleset.Combat"/> (for the two divisors) — a full <see cref="Model.Ruleset"/> rather
    /// than just <see cref="CombatRules"/>, because the threshold is per-unit-type data that lives on
    /// <see cref="UnitTypeRules"/>, not on <see cref="CombatRules"/> itself.
    /// </param>
    /// <returns>The surviving slots, in their original relative order.</returns>
    public static ValueList<UnitSlot> DeleteBelowThreshold(ValueList<UnitSlot> units, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(ruleset);

        var kept = new List<UnitSlot>(units.Count);
        foreach (var unit in units)
        {
            if (unit.Troops > 0)
            {
                var battalionSize = ruleset.UnitTypeById(unit.UnitTypeId)?.StandardBattalionSize ?? 0;
                var threshold = unit.IsMercenary
                    ? battalionSize / ruleset.Combat.DeletionDivisorMercenary
                    : battalionSize / ruleset.Combat.DeletionDivisorNational;

                if (unit.Troops < threshold)
                {
                    continue; // deleted -- below the small-unit threshold (#289).
                }
            }

            kept.Add(unit);
        }

        return ValueList.From(kept);
    }

    /// <summary>
    /// The full "carried army" pass a naval battle's winner or a heavy storm runs against an embarked
    /// army — <c>FUN_0044AE20</c> at ratio <paramref name="damage"/> (<see cref="Apply"/>), then its
    /// deletion pass (<see cref="DeleteBelowThreshold"/>, bug #289), then, only above
    /// <paramref name="unitLossThreshold"/>, <c>(survivingCount × damage) / unitLossDivisor + 1</c>
    /// further whole units — each chosen by <c>Random(current count)</c> and removed swap-with-last,
    /// re-reading the count every iteration (bug #290 part 3's missing <c>+ 1</c> and its shift-not-swap
    /// removal). Shared by <see cref="InstantBattleResolver.ResolveNaval"/> and
    /// <see cref="Naval.FleetAttritionRule"/>'s storm pass, which both call the original's
    /// <c>FUN_0044B4F8</c> against a carried army and must not drift apart on this shared tail.
    /// </summary>
    /// <param name="units">The carried army's unit slots, before this pass.</param>
    /// <param name="damage">
    /// The naval or storm damage figure <c>d</c> — both <see cref="Apply"/>'s ratio argument and the
    /// whole-unit loss's own driver, exactly as <c>FUN_0044AE20(carriedArmy, d)</c> and the loop after it
    /// share the one value.
    /// </param>
    /// <param name="unitLossThreshold">
    /// <see cref="Model.NavalCombatRules.UnitLossDamageThreshold"/> for the naval battle call site, or
    /// <see cref="NavalRules.StormUnitLossDamageThreshold"/> for the storm one — kept as separate ruleset
    /// fields per call site (see either field's own remarks), passed in here rather than read from one
    /// fixed record.
    /// </param>
    /// <param name="unitLossDivisor">
    /// <see cref="Model.NavalCombatRules.UnitLossDivisor"/> or <see cref="NavalRules.StormUnitLossDivisor"/>,
    /// on the same terms as <paramref name="unitLossThreshold"/>.
    /// </param>
    /// <param name="rng">The battle's or storm's stream. One draw per slot for <see cref="Apply"/>, then one draw per whole unit removed.</param>
    /// <param name="ruleset">Supplies <see cref="Ruleset.Combat"/> for <see cref="Apply"/> and <see cref="DeleteBelowThreshold"/>.</param>
    public static CarriedArmyResult ApplyToCarriedArmy(
        ValueList<UnitSlot> units,
        int damage,
        int unitLossThreshold,
        int unitLossDivisor,
        IRng rng,
        Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(ruleset);

        var (reducedUnits, losses, appliedTroops) = Apply(units, damage, rng, ruleset.Combat);
        var survivors = DeleteBelowThreshold(reducedUnits, ruleset);
        var deletedByThreshold = reducedUnits.Count - survivors.Count;

        var remaining = new List<UnitSlot>(survivors);
        var wholeUnitsLost = 0;
        var wholeUnitTroopsLost = 0;

        if (damage > unitLossThreshold)
        {
            // (survivingCount x damage) / unitLossDivisor + 1 -- the "+ 1" and the count taken AFTER the
            // deletion pass are both bug #290 part 3's fix; the original's Delphi `for i := 0 to n` loop
            // always runs at least once.
            var toLose = Math.Min(remaining.Count, ((remaining.Count * damage) / unitLossDivisor) + 1);
            for (var i = 0; i < toLose && remaining.Count > 0; i++)
            {
                var dropped = rng.NextInt(remaining.Count);
                wholeUnitTroopsLost += remaining[dropped].Troops;

                // Swap-with-last, not a shift: move the last slot into the dropped index, then shrink.
                // A test that fails if this were List.RemoveAt(dropped) (a shift) is DoD 3's own proof.
                var lastIndex = remaining.Count - 1;
                remaining[dropped] = remaining[lastIndex];
                remaining.RemoveAt(lastIndex);
                wholeUnitsLost++;
            }
        }

        var finalUnits = ValueList.From(remaining);
        return new CarriedArmyResult(
            finalUnits,
            losses,
            appliedTroops + wholeUnitTroopsLost,
            deletedByThreshold + wholeUnitsLost,
            finalUnits.Count == 0);
    }

    /// <summary>The outcome of <see cref="ApplyToCarriedArmy"/>.</summary>
    /// <param name="Units">The carried army's surviving unit slots.</param>
    /// <param name="Losses">The per-slot losses <see cref="Apply"/> produced, before the deletion and whole-unit passes.</param>
    /// <param name="TroopsLost">Every troop the army lost: <see cref="Apply"/>'s per-slot losses plus the troops in any whole unit removed.</param>
    /// <param name="UnitsLost">Whole unit slots removed, by the deletion pass and the random whole-unit loss combined.</param>
    /// <param name="Emptied">Whether the army has no unit slots left — the caller's cue to delete the army record itself (delete sweep, DoD 1).</param>
    public sealed record CarriedArmyResult(
        ValueList<UnitSlot> Units,
        ValueList<UnitCasualty> Losses,
        int TroopsLost,
        int UnitsLost,
        bool Emptied);

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
