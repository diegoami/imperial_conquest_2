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
    /// message both key off.
    /// </param>
    public sealed record StormResult(int Ships, int ConditionPercent, int Damage);

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
        if (dmg < rules.StormShipLossDamageThreshold)
        {
            shipsAfter = ships;
            conditionAfter = Math.Max(0, conditionPercent - dmg);
        }
        else
        {
            // FUN_0044b4f8(fleet, 100, dmg + 100) -- the original's shared proportional-damage function,
            // reused here from the storm code rather than from T16's battle path (out of this task's
            // Owns list; see NavalRules.StormShipLossRatioBase's remarks). RatioBase (100) plays both the
            // call's own "winnerStrength" argument (the denominator) and the "+100" added to dmg for the
            // "loserStrength" argument; RatioScale (100) is the formula's own internal percent-scale,
            // used for both the ratio and the squared term; Divisor (300) is shared by the ships and
            // condition losses.
            var ratioNumerator = (dmg + rules.StormShipLossRatioBase) * rules.StormShipLossRatioScale;
            var ratio = Math.Max(1, ratioNumerator / rules.StormShipLossRatioBase);
            var scaledDamage = (ratio * ratio) / rules.StormShipLossRatioScale;

            var shipsLost = (ships * scaledDamage) / rules.StormShipLossDivisor;
            var conditionLost = (conditionPercent * scaledDamage) / rules.StormShipLossDivisor;

            shipsAfter = Math.Max(0, ships - shipsLost);
            conditionAfter = Math.Max(0, conditionPercent - conditionLost);
        }

        return new StormResult(shipsAfter, conditionAfter, dmg);
    }

    /// <summary>The outcome of one turn's full attrition pass for one launched (at-sea) fleet.</summary>
    /// <param name="SupplyTonsAfterConsumption">Supply after the unconditional <c>−= ships</c> consumption.</param>
    /// <param name="Ships">Ships after the storm pass.</param>
    /// <param name="ConditionPercent">Condition after the storm pass and, if it applied, the zero-supply penalty.</param>
    /// <param name="Moves">Moves for this turn, after every penalty.</param>
    /// <param name="Damage">The storm roll, for a caller that wants to know how hard the fleet was hit.</param>
    /// <param name="Destroyed">Whether the fleet is lost at sea this turn — <see cref="ConditionPercent"/> fell below <see cref="NavalRules.DeathConditionThreshold"/>.</param>
    /// <param name="DamagedInStorm">
    /// Whether the fleet took the heavy storm branch and survived — the news-worthy "damaged in a storm"
    /// case. Never true together with <see cref="Destroyed"/>: the original's death check runs first,
    /// and this is only evaluated on its <c>else</c> branch.
    /// </param>
    public sealed record TurnOutcome(
        int SupplyTonsAfterConsumption,
        int Ships,
        int ConditionPercent,
        int Moves,
        int Damage,
        bool Destroyed,
        bool DamagedInStorm);

    /// <summary>
    /// Runs the full per-turn rule for one launched fleet already at sea: the storm pass, the death
    /// check, the moves formula (with its carried-army term), the zero-supply penalty and the
    /// damage-slowdown term — in that order, because two of its consequences depend on it (the death
    /// check precedes the zero-supply penalty, and moves are computed from the storm pass's own,
    /// possibly-reduced ship count).
    /// </summary>
    /// <param name="ships">The fleet's ship count before this turn.</param>
    /// <param name="conditionPercent">The fleet's condition before this turn.</param>
    /// <param name="supplyTonsBeforeConsumption">The fleet's supply stock before this turn's consumption.</param>
    /// <param name="carriedArmyTroops">The embarked army's total troops, or <see langword="null"/> if none.</param>
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
        int? carriedArmyTroops,
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
                Destroyed: true, DamagedInStorm: false);
        }

        var damagedInStorm = storm.Damage >= rules.StormShipLossDamageThreshold;

        var conditionAfterSupply = storm.ConditionPercent;
        if (supplyAfterConsumption == 0)
        {
            conditionAfterSupply -= rng.NextInt(rules.ZeroSupplyConditionRandomBound);
        }

        var moves = MovesForTurn(
            storm.Ships, carriedArmyTroops, supplyIsZero: supplyAfterConsumption == 0,
            conditionAfterZeroSupplyPenalty: conditionAfterSupply, ruleset);

        return new TurnOutcome(
            supplyAfterConsumption, storm.Ships, conditionAfterSupply, moves, storm.Damage,
            Destroyed: false, DamagedInStorm: damagedInStorm);
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
