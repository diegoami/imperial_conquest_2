using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The supply→strategic-morale rule and the weekly moves recomputation that runs alongside it —
/// <c>docs/task-catalogue.md</c> "T08 Economy, supply, and purses", Done-when 11 and 12.
/// Transcribed from <c>docs/investigations/thracia-supply-morale.md</c>, not re-derived.
/// </summary>
/// <remarks>
/// <para>
/// <strong>[confirmed]</strong>, <c>FUN_004514ec</c>: supply percentage is computed <em>after</em> that
/// turn's consumption; <c>pct &lt; 10</c> → morale <c>−2</c>, floored at 51, and one fewer move;
/// <c>10 ≤ pct ≤ 15</c> → no change (a deliberate dead band); <c>pct &gt; 15</c> → morale <c>+1</c>,
/// capped at 70. Base moves are <c>10 − min(5, troops / 20000)</c>, recomputed fresh every turn — not
/// decremented from the previous turn's stored value — before the decay penalty is applied.
/// </para>
/// <para>
/// <strong>Only the strategic army morale (<see cref="Model.ArmyState.Morale"/>, army record <c>+14</c>)
/// is written here.</strong> The per-unit tactical morale array is a different field entirely and must
/// never appear in this file — <c>docs/design-audit.md</c> §2.9. This is also deliberately not shared
/// with T14's fleet-condition attrition: the two rules differ in trigger (a percentage here, an absolute
/// zero there), decay shape (deterministic here, <c>−random(0..1)</c> there), floor (51 here, none
/// there), regeneration (a free <c>+1</c>/turn here, none — only paid repair — there) and applicability
/// (every army, every turn, here; only a fleet at sea there). See the comparison table in
/// <c>docs/investigations/thracia-supply-morale.md</c>.
/// </para>
/// </remarks>
public static class SupplyMoraleRule
{
    /// <summary>The outcome of applying one turn's supply→morale rule to one army.</summary>
    /// <param name="SupplyTonsAfterConsumption">The army's supply stock after this turn's consumption.</param>
    /// <param name="SupplyPercent">The supply percentage the rule below was decided from.</param>
    /// <param name="Morale">The army's strategic morale after this turn, clamped to 51…70.</param>
    /// <param name="Moves">The army's recomputed moves for this turn.</param>
    public sealed record Outcome(int SupplyTonsAfterConsumption, int SupplyPercent, int Morale, int Moves);

    /// <summary>
    /// Computes the base weekly moves cap, before the supply-decay penalty:
    /// <c>10 − min(5, troops / 20000)</c>.
    /// </summary>
    /// <param name="ruleset">
    /// Supplies <see cref="SupplyMoraleRules.BaseMovesMax"/>, <see cref="SupplyMoraleRules.MovesReductionCap"/>
    /// and <see cref="SupplyMoraleRules.MovesTroopDivisor"/> — never a C# literal.
    /// </param>
    public static int BaseMoves(int troops, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        var rules = ruleset.Economy.SupplyMorale;
        return rules.BaseMovesMax - Math.Min(rules.MovesReductionCap, troops / rules.MovesTroopDivisor);
    }

    /// <summary>
    /// Applies the morale rule to an already-computed supply percentage, returning the clamped morale and
    /// the moves penalty (0 or <see cref="SupplyMoraleRules.MovesPenaltyOnDecay"/>) it triggers. Pure with
    /// respect to <paramref name="currentMorale"/> and <paramref name="supplyPercent"/> alone — this is
    /// the function Done-when 12's clamp property test exercises directly.
    /// </summary>
    /// <param name="currentMorale">The army's morale before this turn's rule runs. Not itself validated.</param>
    /// <param name="supplyPercent">The supply percentage, computed after this turn's consumption.</param>
    /// <param name="ruleset">Supplies every constant of the rule — never a C# literal.</param>
    /// <returns>The new morale (hard-clamped to <c>[MoraleFloor, MoraleCeiling]</c>) and the moves penalty.</returns>
    /// <remarks>
    /// The clamp applies on <em>every</em> branch, including the dead band and the regen branch's floor
    /// side — not only the branch whose own arithmetic happens to push past a bound. <c>currentMorale</c>
    /// is not itself validated (a save, scenario, or a future battle-entry write such as the confirmed
    /// <c>+= 3</c> in <c>FUN_00437DE4</c> could hand this an out-of-range value), and this is the one
    /// function every write path funnels through, so the clamp belongs here rather than at each caller.
    /// </remarks>
    public static (int Morale, int MovesPenalty) ApplyToMorale(int currentMorale, int supplyPercent, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        var rules = ruleset.Economy.SupplyMorale;

        int unclamped;
        int movesPenalty;
        if (supplyPercent < rules.DecayThresholdPercent)
        {
            unclamped = currentMorale - rules.DecayAmount;
            movesPenalty = rules.MovesPenaltyOnDecay;
        }
        else if (supplyPercent > rules.DeadBandUpperPercent)
        {
            unclamped = currentMorale + rules.RegenAmount;
            movesPenalty = 0;
        }
        else
        {
            unclamped = currentMorale;
            movesPenalty = 0;
        }

        var clamped = Math.Clamp(unclamped, rules.MoraleFloor, rules.MoraleCeiling);
        return (clamped, movesPenalty);
    }

    /// <summary>
    /// Runs the full per-turn rule for one army: consumption, the resulting supply percentage, the
    /// morale write, and the recomputed moves — the same sequence <c>FUN_004514ec</c> runs, in the same
    /// order.
    /// </summary>
    /// <param name="troops">The army's total troop count, floored at 1 — matching the original's own
    /// troop-counting helper, which floors at 1 so a (transient) zero-troop army never divides by zero.</param>
    /// <param name="currentSupplyTons">The army's supply stock before this turn's consumption.</param>
    /// <param name="currentMorale">The army's morale before this turn.</param>
    /// <param name="isEmbarked">Whether the army is aboard a fleet (the flat-rate consumption case).</param>
    /// <param name="seasonIndex">The pre-advance season — see <see cref="SupplyConsumption.ArmyFieldConsumption"/>.</param>
    /// <param name="ruleset">Supplies every constant — never a C# literal.</param>
    public static Outcome ApplyTurn(
        int troops,
        int currentSupplyTons,
        int currentMorale,
        bool isEmbarked,
        int seasonIndex,
        Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        var flooredTroops = Math.Max(1, troops);

        var consumption = isEmbarked
            ? SupplyConsumption.ArmyEmbarkedConsumption(flooredTroops, ruleset)
            : SupplyConsumption.ArmyFieldConsumption(flooredTroops, seasonIndex, ruleset);

        var supplyAfter = SupplyConsumption.ApplyConsumption(currentSupplyTons, consumption);
        var pct = SupplyCapacity.PercentFull(supplyAfter, flooredTroops, ruleset);

        var baseMoves = BaseMoves(flooredTroops, ruleset);
        var (morale, movesPenalty) = ApplyToMorale(currentMorale, pct, ruleset);

        return new Outcome(supplyAfter, pct, morale, baseMoves - movesPenalty);
    }
}
