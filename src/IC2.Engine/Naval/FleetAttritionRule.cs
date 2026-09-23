using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Naval;

/// <summary>
/// The per-turn at-sea attrition pass — <c>docs/task-catalogue.md</c> "T14 Naval", the Scope bullet on
/// storm damage, the zero-supply penalty and loss at sea. Transcribed from
/// <c>docs/investigations/thracia-supply-morale.md</c> §"Fleets" and
/// <c>supply-driven-morale-and-fleet-attrition.md</c>, not re-derived.
/// </summary>
/// <remarks>
/// <strong>Deliberately not shared with <see cref="Economy.SupplyMoraleRule"/></strong> — the two rules
/// are structurally analogous (a softer stat scaled by size: <c>ships × condition / 10</c> against
/// <c>troops / 80 × morale</c>) but differ in every dimension the comparison table in
/// <c>docs/investigations/thracia-supply-morale.md</c> lists: trigger (supply exactly <c>0</c> here, a
/// percentage there), decay (<c>−random(0..1)</c> here, a deterministic <c>−2</c> there), floor (none
/// here, a hard 51 there), regeneration (none here — only paid repair — versus a free <c>+1</c>/turn
/// there), applicability (only at sea here, always there) and lethality (destroys the fleet here,
/// survivable there). This file imports no helper from <c>IC2.Engine.Economy</c>.
/// </remarks>
public static class FleetAttritionRule
{
    /// <summary>The storm pass's outcome for one fleet, before the death check.</summary>
    /// <param name="Ships">Ships remaining — unchanged unless <see cref="Damage"/> crossed the heavy-loss threshold.</param>
    /// <param name="ConditionPercent">Condition after the storm pass, floored at 0.</param>
    /// <param name="Damage">
    /// The roll itself, after every multiplier — the value the death check and the "damaged in a storm"
    /// message both key off. This is <c>dmg</c>, not the heavy branch's own <c>d</c> (<see cref="ScaledDamage"/>)
    /// — an earlier revision of this record's remarks (and <see cref="ApplyStormCasualtiesToCarriedArmy"/>'s
    /// own) conflated the two, which would have fed the carried army's casualty pass the wrong ratio.
    /// </param>
    /// <param name="ScaledDamage">
    /// The heavy branch's own <c>d = r² / 100</c> — the SAME ratio <see cref="Ships"/> and
    /// <see cref="ConditionPercent"/> were just reduced by by, and the exact ratio
    /// <see cref="ApplyStormCasualtiesToCarriedArmy"/> takes for the carried army's own casualty pass
    /// (bug #292). <c>0</c> on the light branch (<c>Damage</c> below
    /// <see cref="NavalRules.StormShipLossDamageThreshold"/>), where it is never computed and never read —
    /// the light branch reduces condition alone.
    /// </param>
    public sealed record StormResult(int Ships, int ConditionPercent, int Damage, int ScaledDamage);

    /// <summary>
    /// Runs the storm pass: <c>dmg = max(1, random(100 − condition) / 10)</c>, doubled-and-capped in
    /// Winter, tripled-and-capped on <paramref name="tripleDamageBranchActive"/>, then either
    /// <c>×2 + 1</c> (with a 1-in-20 Winter spike) away from friendly coast or halved next to it, and
    /// finally applied to condition alone (<c>dmg &lt;</c> <see cref="NavalRules.StormShipLossDamageThreshold"/>)
    /// or to ships and condition both (at or above it, through the original's shared proportional-damage
    /// formula — see <see cref="NavalRules.StormShipLossRatioBase"/>'s remarks).
    /// </summary>
    /// <param name="ships">The fleet's current ship count.</param>
    /// <param name="conditionPercent">The fleet's current condition, 0-100.</param>
    /// <param name="isWinter">Whether the current season is Winter.</param>
    /// <param name="tripleDamageBranchActive">
    /// <c>FleetRecord +24 == StormTripleConditionTileCode</c> — see
    /// <see cref="NavalRules.StormTripleConditionTileCode"/>'s remarks on why this reuses
    /// <see cref="FleetState.CoveredTileCode"/> rather than a new field.
    /// </param>
    /// <param name="nearFriendlyCoast">
    /// The confirmed effect's boolean gate. <see cref="NavalRules.FriendlyCoastRadiusTiles"/>'s remarks
    /// cover how a caller derives this — this function takes it as a plain <see langword="bool"/> so its
    /// own arithmetic can be tested independently of that (undecompiled) geometry.
    /// </param>
    /// <param name="rng">
    /// The fleet's own draw for this turn. Callers are expected to pass a stream scoped per fleet per
    /// turn so one fleet's roll cannot perturb another's.
    /// </param>
    /// <param name="ruleset">Supplies every constant via <see cref="Ruleset.Naval"/> — never a C# literal.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rng"/> or <paramref name="ruleset"/> is null.</exception>
    public static StormResult ApplyStormPass(
        int ships,
        int conditionPercent,
        bool isWinter,
        bool tripleDamageBranchActive,
        bool nearFriendlyCoast,
        IRng rng,
        Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(ruleset);
        var rules = ruleset.Naval;

        // random(100 - condition): at condition == MaxConditionPercent the range is empty, and the
        // original's Random(0) has no meaningful reading to reproduce, so the draw is skipped and the
        // raw value is 0 -- max(1, 0 / divisor) still floors dmg at 1 exactly as every other draw would
        // once divided, so this only avoids IRng's positive-bound contract, it does not change the
        // formula's outcome.
        var range = rules.MaxConditionPercent - conditionPercent;
        var rawDraw = range > 0 ? rng.NextInt(range) : 0;
        var dmg = Math.Max(1, rawDraw / rules.StormDamageRandomDivisor);

        if (isWinter)
        {
            dmg = Math.Min(rules.StormWinterDamageCap, dmg * rules.StormWinterDamageMultiplier);
        }

        if (tripleDamageBranchActive)
        {
            dmg = Math.Min(rules.StormTripleDamageCap, dmg * rules.StormTripleDamageMultiplier);
        }

        if (!nearFriendlyCoast)
        {
            dmg = (dmg * rules.StormAwayFromCoastDamageMultiplier) + rules.StormAwayFromCoastDamageAddend;
            if (isWinter && rng.NextChance(1, rules.StormWinterSpikeChanceDenominator))
            {
                dmg = rules.StormWinterSpikeDamage;
            }
        }
        else
        {
            dmg /= rules.StormNearCoastDamageDivisor;
        }

        int shipsAfter;
        int conditionAfter;
        var scaledDamage = 0; // d -- only ever computed (and only ever meaningful) on the heavy branch.
        if (dmg < rules.StormShipLossDamageThreshold)
        {
            shipsAfter = ships;
            conditionAfter = Math.Max(0, conditionPercent - dmg);
        }
        else
        {
            // FUN_0044b4f8(fleet, 100, dmg + 100) -- the original's shared proportional-damage function,
            // reused here from the storm code rather than from T16's battle path (out of this task's
            // Owns list; see NavalRules.StormShipLossRatioBase's remarks). RatioBase (100) is the call's
            // own NUMERATOR argument, not its denominator -- bug #292's fix: r = max(1, (RatioBase x
            // RatioScale) / (dmg + RatioBase)), so r FALLS as dmg rises. RatioScale (100) is both
            // RatioBase's numerator partner and the formula's own internal percent-scale, used again for
            // the squared term; Divisor (300) is shared by the ships and condition losses.
            var ratioNumerator = rules.StormShipLossRatioBase * rules.StormShipLossRatioScale;
            var ratio = Math.Max(1, ratioNumerator / (dmg + rules.StormShipLossRatioBase));
            scaledDamage = (ratio * ratio) / rules.StormShipLossRatioScale;

            var shipsLost = (ships * scaledDamage) / rules.StormShipLossDivisor;
            var conditionLost = (conditionPercent * scaledDamage) / rules.StormShipLossDivisor;

            shipsAfter = Math.Max(0, ships - shipsLost);
            conditionAfter = Math.Max(0, conditionPercent - conditionLost);
        }

        return new StormResult(shipsAfter, conditionAfter, dmg, scaledDamage);
    }

    /// <summary>The outcome of <see cref="ApplyStormCasualtiesToCarriedArmy"/>.</summary>
    /// <param name="Units">The carried army's surviving unit slots.</param>
    /// <param name="TroopsLost">Every troop the army lost this storm.</param>
    /// <param name="UnitsLost">Whole unit slots removed, by the deletion pass and the random whole-unit loss combined.</param>
    /// <param name="Emptied">Whether the army has no unit slots left.</param>
    public sealed record StormArmyResult(ValueList<UnitSlot> Units, int TroopsLost, int UnitsLost, bool Emptied);

    /// <summary>
    /// The heavy-storm branch's missing steps 3 and 4 (bug #292): a carried army takes
    /// <c>FUN_0044AE20(army, d)</c> at the SAME <c>d</c> the fleet's own ships and condition just lost --
    /// <see cref="ApplyStormPass"/>'s own <see cref="StormResult.ScaledDamage"/> -- and, above
    /// <see cref="NavalRules.StormUnitLossDamageThreshold"/>, also loses whole units at random,
    /// swap-with-last. Delegates the shared arithmetic to
    /// <see cref="Battle.BattleCasualties.ApplyToCarriedArmy"/>, the same helper
    /// <see cref="Battle.InstantBattleResolver.ResolveNaval"/> uses for its own winner's carried army, so
    /// the two <c>FUN_0044B4F8</c> call sites cannot drift apart on this shared tail.
    /// </summary>
    /// <remarks>
    /// A separate method rather than folded into <see cref="ApplyStormPass"/> or
    /// <see cref="ApplyLaunchedFleetTurn"/>: this task's Owns grant reaches only this file under
    /// <c>src/IC2.Engine/Naval/</c>, and the per-turn caller, <c>FleetTickSystem.cs</c>, was outside it
    /// until the user's narrow Owns amendment (docs/tasks/T63.md, 2026-09-23) granted exactly the call
    /// that reaches this method — see <c>FleetTickSystem.Execute</c> for the wiring.
    /// </remarks>
    /// <param name="units">The carried army's unit slots, before this storm.</param>
    /// <param name="damage">
    /// The storm's own scaled damage figure <c>d</c> -- <see cref="ApplyStormPass"/>'s
    /// <see cref="StormResult.ScaledDamage"/> from the SAME storm pass, NOT its <see cref="StormResult.Damage"/>
    /// (the raw, pre-scaling <c>dmg</c> roll). Only ever meaningful when that pass took the heavy branch
    /// (<c>StormResult.Damage &gt;= </c><see cref="NavalRules.StormShipLossDamageThreshold"/>).
    /// </param>
    /// <param name="rng">
    /// The fleet's own draw stream for this turn -- the SAME stream <see cref="ApplyStormPass"/> drew
    /// from, so a caller applying both in one turn keeps one draw sequence for that fleet.
    /// </param>
    /// <param name="ruleset">Supplies <see cref="Ruleset.Naval"/> for the two whole-unit-loss constants and <see cref="Ruleset.Combat"/> for the casualty and deletion passes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="units"/>, <paramref name="rng"/> or <paramref name="ruleset"/> is null.</exception>
    public static StormArmyResult ApplyStormCasualtiesToCarriedArmy(
        ValueList<UnitSlot> units, int damage, IRng rng, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(ruleset);
        var rules = ruleset.Naval;

        var result = BattleCasualties.ApplyToCarriedArmy(
            units, damage, rules.StormUnitLossDamageThreshold, rules.StormUnitLossDivisor, rng, ruleset);

        return new StormArmyResult(result.Units, result.TroopsLost, result.UnitsLost, result.Emptied);
    }

    /// <summary>The outcome of one turn's full attrition pass for one launched (at-sea) fleet.</summary>
    /// <param name="SupplyTonsAfterConsumption">Supply after the unconditional <c>−= ships</c> consumption.</param>
    /// <param name="Ships">Ships after the storm pass.</param>
    /// <param name="ConditionPercent">Condition after the storm pass and, if it applied, the zero-supply penalty.</param>
    /// <param name="Moves">
    /// Moves for this turn, after every penalty -- computed from the carried army's own POST-storm
    /// troop count (<see cref="CarriedArmyUnits"/>), never the pre-storm count a caller passed in
    /// (bug #292/B2: an earlier revision of this method took a plain troop count and had no way to
    /// update it before computing moves, so a heavy storm's own casualty pass -- run by the caller,
    /// afterward -- could never be reflected here).
    /// </param>
    /// <param name="Damage">The storm roll, for a caller that wants to know how hard the fleet was hit.</param>
    /// <param name="ScaledDamage">
    /// <see cref="StormResult.ScaledDamage"/> passed through unchanged, for a caller that wants the raw
    /// ratio the carried army's own casualty pass used.
    /// </param>
    /// <param name="Destroyed">Whether the fleet is lost at sea this turn — <see cref="ConditionPercent"/> fell below <see cref="NavalRules.DeathConditionThreshold"/>.</param>
    /// <param name="DamagedInStorm">
    /// Whether the fleet took the heavy storm branch and survived — the news-worthy "damaged in a storm"
    /// case. Never true together with <see cref="Destroyed"/>: the original's death check runs first,
    /// and this is only evaluated on its <c>else</c> branch.
    /// </param>
    /// <param name="CarriedArmyUnits">
    /// The carried army's unit slots after this turn -- unchanged from the input when there was no
    /// army, the storm was not heavy, or the army was emptied (in which case this is empty and
    /// <see cref="CarriedArmyEmptied"/> is <see langword="true"/>); otherwise the post-casualty slots a
    /// caller writes back onto <c>ArmyState.Units</c>.
    /// </param>
    /// <param name="CarriedArmyTroopsLost">Every troop the carried army lost this storm (0 when not applicable).</param>
    /// <param name="CarriedArmyUnitsLost">Whole unit slots the carried army lost this storm (0 when not applicable).</param>
    /// <param name="CarriedArmyEmptied">Whether the carried army has no unit slots left after this storm.</param>
    public sealed record TurnOutcome(
        int SupplyTonsAfterConsumption,
        int Ships,
        int ConditionPercent,
        int Moves,
        int Damage,
        int ScaledDamage,
        bool Destroyed,
        bool DamagedInStorm,
        ValueList<UnitSlot>? CarriedArmyUnits,
        int CarriedArmyTroopsLost,
        int CarriedArmyUnitsLost,
        bool CarriedArmyEmptied);

    /// <summary>
    /// Runs the full per-turn rule for one launched fleet already at sea: the storm pass, the death
    /// check, the carried army's own casualty pass (bug #292 steps 3 and 4, only on a heavy storm), the
    /// zero-supply penalty, and the moves formula — in that order.
    /// </summary>
    /// <remarks>
    /// <strong>This method's own order is not quite the original's (N-4, T63 review round 3), though the
    /// two never disagree on final state.</strong> In the original
    /// (<c>supply-driven-morale-and-fleet-attrition.md</c> §"The fleet loop, in order", research
    /// 3f6ca09), the army's own casualty pass is INSIDE <c>FUN_0044b4f8</c> itself (the same call that
    /// computes the storm's ship/condition loss), so it runs BEFORE the caller's own death check --
    /// unconditionally, on every heavy storm, whether or not that same storm goes on to sink the fleet.
    /// This method's death check (the early return two lines below) comes first, so on a turn where the
    /// storm both damages heavily AND sinks the fleet, this method skips the army's casualty pass and its
    /// <see cref="IRng"/> draws entirely, where the original would have taken them. The final STATE never
    /// differs -- a sunk fleet's carried army is deleted either way, whatever its troop count was the
    /// instant before -- so this is purely a draw-count/order divergence on that one turn shape, which
    /// Decision 6 (<c>docs/tasks/T63.md</c>) already disclaims for exactly this kind of case. <strong>Bug
    /// #292/B2:</strong> an earlier revision of this method computed moves from a troop count the CALLER
    /// passed in before applying that turn's own storm casualties to the army, which is backwards -- the
    /// probe was a 40-ship, condition-68 fleet carrying 80,000 troops into a known heavy storm (seed 14):
    /// moves came out as -1, from the STALE 80,000, where the original (and this corrected method) give
    /// 23, from the post-storm 10,330.
    /// </remarks>
    /// <param name="ships">The fleet's ship count before this turn.</param>
    /// <param name="conditionPercent">The fleet's condition before this turn.</param>
    /// <param name="supplyTonsBeforeConsumption">The fleet's supply stock before this turn's consumption.</param>
    /// <param name="carriedArmyUnits">The embarked army's unit slots before this turn, or <see langword="null"/> if none.</param>
    /// <param name="isWinter">Whether the current season is Winter.</param>
    /// <param name="tripleDamageBranchActive">See <see cref="ApplyStormPass"/>.</param>
    /// <param name="nearFriendlyCoast">See <see cref="ApplyStormPass"/>.</param>
    /// <param name="rng">The fleet's own draw stream for this turn.</param>
    /// <param name="ruleset">Supplies every constant — never a C# literal.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rng"/> or <paramref name="ruleset"/> is null.</exception>
    public static TurnOutcome ApplyLaunchedFleetTurn(
        int ships,
        int conditionPercent,
        int supplyTonsBeforeConsumption,
        ValueList<UnitSlot>? carriedArmyUnits,
        bool isWinter,
        bool tripleDamageBranchActive,
        bool nearFriendlyCoast,
        IRng rng,
        Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(ruleset);
        var rules = ruleset.Naval;

        // fleet[+14] = max(0, fleet[+14] - fleet[+18]) -- every turn, every fleet, in port or at sea.
        var supplyAfterConsumption = Math.Max(0, supplyTonsBeforeConsumption - ships);

        var storm = ApplyStormPass(ships, conditionPercent, isWinter, tripleDamageBranchActive, nearFriendlyCoast, rng, ruleset);

        // A fleet the heavy storm branch reduces to 0 ships has nothing left to sail: not itself an
        // observed case, but a code-level guard against dividing by zero in the moves formula below
        // rather than a ruleset-governed rule, so it is not backed by a named constant.
        if (storm.Ships <= 0 || storm.ConditionPercent < rules.DeathConditionThreshold)
        {
            return new TurnOutcome(
                supplyAfterConsumption, storm.Ships, storm.ConditionPercent, Moves: 0, storm.Damage,
                storm.ScaledDamage, Destroyed: true, DamagedInStorm: false,
                CarriedArmyUnits: carriedArmyUnits, CarriedArmyTroopsLost: 0, CarriedArmyUnitsLost: 0,
                CarriedArmyEmptied: false);
        }

        var damagedInStorm = storm.Damage >= rules.StormShipLossDamageThreshold;

        // Steps 3 and 4 (bug #292): the carried army's own casualty pass, at the SAME d the ships and
        // condition just lost by -- run here, BEFORE the moves formula reads the army's troop count
        // (the fix for B2). Only on a heavy storm; a light storm never reaches this branch in the
        // original either.
        var carriedArmyAfter = carriedArmyUnits;
        var carriedTroopsLost = 0;
        var carriedUnitsLost = 0;
        var carriedEmptied = false;
        if (damagedInStorm && carriedArmyUnits is { } unitsAboard)
        {
            var carriedResult = ApplyStormCasualtiesToCarriedArmy(unitsAboard, storm.ScaledDamage, rng, ruleset);
            carriedArmyAfter = carriedResult.Units;
            carriedTroopsLost = carriedResult.TroopsLost;
            carriedUnitsLost = carriedResult.UnitsLost;
            carriedEmptied = carriedResult.Emptied;
        }

        var conditionAfterSupply = storm.ConditionPercent;
        if (supplyAfterConsumption == 0)
        {
            conditionAfterSupply -= rng.NextInt(rules.ZeroSupplyConditionRandomBound);
        }

        // An emptied army adds no term at all -- the same as the fleet never having carried one, and
        // consistent with FleetTickSystem clearing CarriedArmyId when this happens.
        int? carriedArmyTroopsForMoves = carriedEmptied ? null : TotalTroops(carriedArmyAfter);

        var moves = MovesForTurn(
            storm.Ships, carriedArmyTroopsForMoves, supplyIsZero: supplyAfterConsumption == 0,
            conditionAfterZeroSupplyPenalty: conditionAfterSupply, ruleset);

        return new TurnOutcome(
            supplyAfterConsumption, storm.Ships, conditionAfterSupply, moves, storm.Damage,
            storm.ScaledDamage, Destroyed: false, DamagedInStorm: damagedInStorm,
            CarriedArmyUnits: carriedArmyAfter, CarriedArmyTroopsLost: carriedTroopsLost,
            CarriedArmyUnitsLost: carriedUnitsLost, CarriedArmyEmptied: carriedEmptied);
    }

    /// <summary>Sums <see cref="UnitSlot.Troops"/> across a unit list, or <see langword="null"/> for a null list.</summary>
    private static int? TotalTroops(ValueList<UnitSlot>? units)
    {
        if (units is not { } list)
        {
            return null;
        }

        var total = 0;
        foreach (var unit in list)
        {
            total += unit.Troops;
        }

        return total;
    }

    /// <summary>
    /// The moves formula's full shape for a launched, at-sea fleet — base moves, the carried-army term,
    /// the zero-supply <c>−3</c>, and the damage-slowdown term, all applied to the ship count and the
    /// condition value <em>after</em> the storm pass and (if it applied) the zero-supply condition
    /// penalty. <c>docs/task-catalogue.md</c> "T14 Naval" Done-when 10 calls this out by name as "the one
    /// directly separable naval assertion available": every input here is deterministic (no
    /// <see cref="IRng"/> draw), which is what lets the Carthaginian series' recorded moves be replayed
    /// exactly from the save data's own condition column, without needing this engine's storm draws to
    /// reproduce the original's.
    /// </summary>
    /// <param name="ships">The ship count after the storm pass (never the pre-storm count, if it changed).</param>
    /// <param name="carriedArmyTroops">The embarked army's total troops, or <see langword="null"/> if none.</param>
    /// <param name="supplyIsZero">Whether this turn's post-consumption supply is exactly 0.</param>
    /// <param name="conditionAfterZeroSupplyPenalty">
    /// Condition after the storm pass and, if <paramref name="supplyIsZero"/>, the zero-supply
    /// <c>−random(0..1)</c> penalty — the same value <see cref="TurnOutcome.ConditionPercent"/> ends the
    /// turn at.
    /// </param>
    public static int MovesForTurn(
        int ships, int? carriedArmyTroops, bool supplyIsZero, int conditionAfterZeroSupplyPenalty, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        var rules = ruleset.Naval;

        var moves = rules.MovesBaseValue - ((ships - rules.MovesShipOffset) / rules.MovesShipDivisor);
        if (carriedArmyTroops is { } troops)
        {
            moves -= (troops / rules.MovesCarriedArmyTroopDivisor / ships) + rules.MovesCarriedArmyAddend;
        }

        if (supplyIsZero)
        {
            moves -= rules.ZeroSupplyMovesPenalty;
        }

        if (conditionAfterZeroSupplyPenalty < rules.DamageSlowdownConditionThreshold)
        {
            moves -= (rules.DamageSlowdownConditionThreshold - conditionAfterZeroSupplyPenalty) / rules.DamageSlowdownDivisor;
        }

        return moves;
    }
}
