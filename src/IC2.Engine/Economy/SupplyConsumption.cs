using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// Per-turn supply consumption — <c>docs/build-orchestration-plan.md</c> "T08 Economy, supply, and
/// purses", Done-when 10. Transcribed from <c>docs/investigations/thracia-supply-morale.md</c>, not
/// re-derived from the decompilation (the task's own instruction: "an implementer that goes back to the
/// decompilation to rediscover them has misread the task").
/// </summary>
/// <remarks>
/// <strong>[confirmed]</strong>: <c>((90 − seasonVal) × troops) / 20000</c> for an army in the field, read
/// against the season table at DAT <c>0x1F7D8</c> (Spring 50, Summer 80, Autumn 80, Winter 20); a flat
/// <c>troops / 200</c>, no seasonal term, for an army aboard a fleet. Winter costs 7× Summer. Consumption
/// is clamped at the army's current supply — it can reduce supply to (but never below) zero, matching
/// <c>max(0, supplies − consumption)</c> in the source.
/// </remarks>
public static class SupplyConsumption
{
    /// <summary>
    /// One turn's consumption for an army in the field (not aboard a fleet), for the season currently
    /// running.
    /// </summary>
    /// <param name="troops">The army's total troop count.</param>
    /// <param name="seasonIndex">
    /// The <em>pre-advance</em> season — <see cref="Model.CalendarState.SeasonIndex"/> as it stands before
    /// <see cref="Core.TurnPhase.CalendarAdvance"/> runs, since <see cref="Core.TurnPhase.ArmyTick"/> runs
    /// first in the round. Reading the post-advance season here is the documented hazard that makes a
    /// Winter turn's figure wrong by 7×.
    /// </param>
    /// <param name="ruleset">
    /// Supplies <see cref="SupplyConsumptionRules.SeasonValues"/>,
    /// <see cref="SupplyConsumptionRules.ConsumptionBaseValue"/> and
    /// <see cref="SupplyConsumptionRules.ConsumptionDivisor"/> — never a C# literal.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="seasonIndex"/> is outside the ruleset's season table.</exception>
    public static int ArmyFieldConsumption(int troops, int seasonIndex, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        var rules = ruleset.Economy.SupplyConsumption;
        if (seasonIndex < 0 || seasonIndex >= rules.SeasonValues.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seasonIndex), seasonIndex, "Outside the ruleset's supplyConsumption.seasonValues table.");
        }

        var seasonVal = rules.SeasonValues[seasonIndex];
        return (rules.ConsumptionBaseValue - seasonVal) * troops / rules.ConsumptionDivisor;
    }

    /// <summary>One turn's consumption for an army aboard a fleet: a flat rate, no seasonal term.</summary>
    /// <param name="troops">The army's total troop count.</param>
    /// <param name="ruleset">Supplies <see cref="SupplyConsumptionRules.FleetEmbarkedDivisor"/> — never a C# literal.</param>
    public static int ArmyEmbarkedConsumption(int troops, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        return troops / ruleset.Economy.SupplyConsumption.FleetEmbarkedDivisor;
    }

    /// <summary>
    /// Applies one turn's consumption to a current supply stock, clamped so it never goes negative —
    /// <c>max(0, supplies − consumption)</c>.
    /// </summary>
    public static int ApplyConsumption(int currentSupplyTons, int consumption) =>
        Math.Max(0, currentSupplyTons - consumption);
}
