using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Battle;

/// <summary>
/// The instant resolver's casualty arithmetic: how many troops the winner loses, and how that figure is
/// spread over its unit slots.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The figure (DoD 3).</strong> <c>FUN_0044AEE4</c> calls
/// <c>applyCasualties(winner, loserPower * 40 / winnerPower)</c>. The <c>40</c> is
/// <see cref="CombatRules.WinnerCasualtyNumerator"/>, and the whole expression is evaluated in the
/// original's own integer arithmetic: the multiplication first, then one truncating division. That order
/// is load-bearing and <c>BattleCasualtyArithmeticTests</c> pins it — reading it as
/// <c>loserPower * (40 / winnerPower)</c> would truncate the inner quotient to zero for every
/// <c>winnerPower &gt; 40</c>, i.e. for every real battle, and silently make the winner invulnerable.
/// </para>
/// <para>
/// <strong>One documented evidence conflict, and why it is resolved this way.</strong> The figure is
/// applied here as a <em>troop count</em>: exactly the reading <c>docs/task-catalogue.md</c> T16 DoD 3
/// states ("Winner casualties equal <c>loserPower × 40 / winnerPower</c>"), and exactly the reading the
/// shipped ruleset's own provenance for <c>combat.winnerCasualtyNumerator</c> states ("the winner takes
/// <c>loserPower × 40 / winnerPower</c> casualties"). The research repo also records
/// <c>FUN_0044AE20</c>, the routine <c>applyCasualties</c> resolves to, as a per-unit
/// <em>ratio</em> distribution — <c>troops -= troops / (Random(15) + 105) * ratio</c>
/// (<c>tests/fixtures/corpus.json</c> <c>siege.attritionFormula</c>, from
/// <c>decompiled-defection-and-siege-attrition.md</c>), under which the same expression would be a
/// multiplier rather than a count. That reading is <strong>not</strong> implementable inside this task's
/// Owns list: its two constants (the <c>15</c>-wide random band and the <c>105</c> base) have no field
/// anywhere in <see cref="Ruleset"/>, and adding them would mean editing
/// <c>src/IC2.Engine/Model/Ruleset.cs</c> and <c>data/rulesets/toy-ruleset.json</c>, which belong to
/// other tasks. Writing them as C# literals is forbidden outright. So this file implements the DoD's
/// literal arithmetic, and the conflict is reported rather than quietly decided — see the pull request's
/// "evidence conflict" note.
/// </para>
/// <para>
/// <strong>What this file is not.</strong> It is not the tactical exchange loop. No type-effectiveness
/// matrix, no 40%-of-own-troops melee cap, no per-unit tactical morale, no shooting-vulnerability weight,
/// and above all no rout mechanic: nothing is ever removed for falling below a battalion-size threshold,
/// no morale cascade runs, and no unit is deleted here at all. Those are held in reserve for a possible
/// future detailed resolver (<c>docs/game-design.md</c> §Combat, <c>docs/task-catalogue.md</c> T16
/// Hazards). The instant resolver annihilates the loser wholesale and tracks no per-unit attrition
/// beyond the proportional split below.
/// </para>
/// </remarks>
public static class BattleCasualties
{
    /// <summary>
    /// The casualty figure: <c>loserPower × numerator / winnerPower</c>, multiplication first, one
    /// truncating division.
    /// </summary>
    /// <param name="loserPower">The losing side's strength.</param>
    /// <param name="winnerPower">
    /// The winning side's strength. A non-positive value would be a division by zero in the original's
    /// own arithmetic; because the winner is by construction the side with the greater-or-equal
    /// strength, this can only happen when both sides' strength is zero (two empty armies), which
    /// produces no casualties rather than an exception.
    /// </param>
    /// <param name="numerator">
    /// <see cref="CombatRules.WinnerCasualtyNumerator"/> for the winner's own losses, or
    /// <see cref="ScatteredDefeatRules.SurvivorCasualtyNumerator"/> for the <c>improved</c> ruleset's
    /// mirrored figure.
    /// </param>
    public static int Count(int loserPower, int winnerPower, int numerator)
    {
        if (winnerPower <= 0)
        {
            return 0;
        }

        // Multiplication first, then ONE truncating division -- see the class remarks.
        return (loserPower * numerator) / winnerPower;
    }

    /// <summary>
    /// Spreads <paramref name="casualties"/> over <paramref name="units"/> in proportion to each slot's
    /// troops, and returns both the reduced slots and the per-slot losses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Largest-remainder apportionment: each slot takes <c>troops × casualties / total</c>, and the
    /// shortfall left by those truncations is handed out one troop at a time to the slots with the
    /// largest remainders, ties broken by ascending slot index. That makes the split exact — the per-slot
    /// losses always sum to the figure <see cref="Count"/> produced (after the clamp below) — and
    /// deterministic, with no random draw of its own, so two runs on one seed agree slot for slot.
    /// </para>
    /// <para>
    /// <paramref name="casualties"/> is clamped to the force's total troops, so a lopsided battle cannot
    /// drive a slot negative. A slot reaches zero only when the clamp binds, i.e. when the whole force is
    /// wiped out; short of that, <c>troops × casualties / total ≤ troops</c> strictly, and the one extra
    /// troop a remainder can add cannot reach the slot's own count. <strong>No slot is ever removed
    /// here</strong> — removing a unit for being small is the rout mechanic, which is reserve research
    /// and must not appear in this diff.
    /// </para>
    /// </remarks>
    /// <param name="units">The force's unit slots.</param>
    /// <param name="casualties">The casualty figure from <see cref="Count"/>.</param>
    /// <returns>The reduced slots, the per-slot losses, and the total actually applied.</returns>
    public static (ValueList<UnitSlot> Units, ValueList<UnitCasualty> Losses, int Applied) Distribute(
        ValueList<UnitSlot> units,
        int casualties)
    {
        ArgumentNullException.ThrowIfNull(units);

        var total = 0;
        foreach (var unit in units)
        {
            total += unit.Troops;
        }

        if (casualties <= 0 || total <= 0 || units.Count == 0)
        {
            return (units, ValueList<UnitCasualty>.Empty, 0);
        }

        var applied = Math.Min(casualties, total);

        var losses = new int[units.Count];
        var remainders = new long[units.Count];
        var handedOut = 0;
        for (var i = 0; i < units.Count; i++)
        {
            var scaled = (long)units[i].Troops * applied;
            losses[i] = (int)(scaled / total);
            remainders[i] = scaled % total;
            handedOut += losses[i];
        }

        // The shortfall the truncations left, one troop at a time to the largest remainder first.
        var shortfall = applied - handedOut;
        while (shortfall > 0)
        {
            var best = -1;
            for (var i = 0; i < units.Count; i++)
            {
                if (losses[i] >= units[i].Troops)
                {
                    continue;
                }

                if (best < 0 || remainders[i] > remainders[best])
                {
                    best = i;
                }
            }

            if (best < 0)
            {
                break;
            }

            losses[best]++;
            remainders[best] = -1;
            shortfall--;
        }

        var reduced = new UnitSlot[units.Count];
        var report = new List<UnitCasualty>();
        var actuallyApplied = 0;
        for (var i = 0; i < units.Count; i++)
        {
            reduced[i] = units[i] with { Troops = units[i].Troops - losses[i] };
            actuallyApplied += losses[i];
            if (losses[i] > 0)
            {
                report.Add(new UnitCasualty(i, units[i].Name, units[i].Troops, losses[i]));
            }
        }

        return (ValueList.From(reduced), ValueList.From(report), actuallyApplied);
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
    /// removed — see <see cref="Distribute"/>.
    /// </para>
    /// </remarks>
    /// <param name="units">The winner's slots, already reduced by <see cref="Distribute"/>.</param>
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
