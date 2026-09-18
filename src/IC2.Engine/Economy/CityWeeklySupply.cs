using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The weekly city loop's supply-production and famine-unrest rule — <c>FUN_004514ec</c>'s first loop
/// (<c>54443–54492</c>), run once per round over every city, before the army and fleet loops —
/// <c>docs/task-catalogue.md</c> "T37 City supply production and famine unrest", Done-when 1-6.
/// Transcribed from <c>city-population-growth.md</c> §"The weekly step is city supply production"
/// <strong>[confirmed]</strong>: 33 save pairs, 10,693 of 10,980 city-turns exact, and nearly all the
/// rest fit armies drawing on or depositing into city stocks during the round (untraced, not this
/// task's — see <see cref="WeeklyCitySupplySystem"/>'s remarks).
/// </summary>
/// <remarks>
/// <c>v = seasonValue[season]</c> (the same table T08's army consumption already reads,
/// <see cref="SupplyConsumptionRules.SeasonValues"/>); <c>s = pop × (v − <see
/// cref="EconomyRules.CitySupplyBaselineSeasonValue"/>) / <see cref="EconomyRules.CitySupplyProductionDivisor"/></c>;
/// <c>inc = s − s × mobilized / <see cref="EconomyRules.CitySupplyMobilizationDivisor"/></c>; a threatened
/// city (<see cref="HostileArmyAdjacent.IsThreatened"/>) cannot gain, so <c>inc</c> is clamped at 0 from
/// above, but a Winter loss still applies; <c>supplies = max(0, min(supplies + inc, pop × <see
/// cref="EconomyRules.CitySupplyCapTonsPerPopulationThousand"/>))</c>. This step never writes population:
/// mobilization scales the supply change, not loyalty, and loyalty changes only through the famine-unrest
/// roll below — the older report's two misreadings this task's own catalogue entry names.
/// <para>
/// <strong>Famine unrest.</strong> When the resulting stock is exactly 0 and the season ending is Winter,
/// one <see cref="IRng.NextChance"/> draw (a <see cref="EconomyRules.FamineLoyaltyLossProbabilityDenominator"/>-in-1
/// chance) decides whether the city loses <see cref="EconomyRules.FamineLoyaltyLossAmount"/> loyalty. No
/// other city, and no city with stock left, draws at all — the report's "cities with stock left, and
/// every city outside Winter, never lose loyalty this way" (1,874 and 8,980 city-turns unchanged). This
/// is numerically the same 1-in-3 shape as T35's quarterly <see cref="CityLoyaltyDraws"/> fall roll but a
/// separate ruleset field and a separate draw, so the two can vary independently.
/// </para>
/// <para>
/// Winter is the season at index <c><see cref="CalendarRules.SeasonsPerYear"/> − 1</c> — the fixed
/// Spring/Summer/Autumn/Winter ordering every other season-indexed table in this ruleset already assumes
/// (<see cref="SupplyConsumptionRules.SeasonValues"/>, <see cref="NewsLogRules.SeasonNames"/>), the same
/// structural (not ruleset-data) convention <see cref="Calendar.CalendarSystem"/> already relies on for
/// its own "wraps to a new year" check (season index 0 = Spring).
/// </para>
/// </remarks>
public static class CityWeeklySupply
{
    /// <summary>One city's weekly supply-production result.</summary>
    /// <param name="SupplyTons">The city's supply stock after this week's production, floored at 0.</param>
    /// <param name="LoyaltyDelta">
    /// The loyalty change from the famine-unrest roll: <c>0</c>, or <c>−<see
    /// cref="EconomyRules.FamineLoyaltyLossAmount"/></c> when it fired.
    /// </param>
    public readonly record struct Result(int SupplyTons, int LoyaltyDelta);

    /// <summary>
    /// Computes one city's weekly supply change and, when it qualifies, draws for famine unrest.
    /// </summary>
    /// <param name="currentSupplyTons">The city's supply stock before this week's step.</param>
    /// <param name="populationThousands">The city's population — read before T35's quarterly growth, at a quarter boundary.</param>
    /// <param name="seasonIndex">
    /// The season that is <em>ending</em> — <see cref="Model.CalendarState.SeasonIndex"/> as it stands
    /// before <see cref="Core.TurnPhase.CalendarAdvance"/> runs, since <see cref="Core.TurnPhase.CityTick"/>
    /// runs first in the round. Reading the post-advance season here would make every Winter turn's figure
    /// wrong, the same hazard <see cref="SupplyConsumption.ArmyFieldConsumption"/> documents.
    /// </param>
    /// <param name="ownerMobilizedPercent">The owning nation's current mobilization.</param>
    /// <param name="threatened">Whether a hostile army stands adjacent to the city this week.</param>
    /// <param name="ruleset">
    /// Supplies <see cref="SupplyConsumptionRules.SeasonValues"/> and every <see cref="EconomyRules"/>
    /// divisor, cap and roll constant below — never a C# literal.
    /// </param>
    /// <param name="rng">
    /// This system's own draw stream. Consumed only when the city qualifies for the famine-unrest roll —
    /// a city with stock left, or outside Winter, draws nothing.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="seasonIndex"/> is outside the ruleset's season table.</exception>
    public static Result Apply(
        int currentSupplyTons,
        int populationThousands,
        int seasonIndex,
        int ownerMobilizedPercent,
        bool threatened,
        Ruleset ruleset,
        IRng rng)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(rng);

        var economy = ruleset.Economy;
        var seasonValues = economy.SupplyConsumption.SeasonValues;
        if (seasonIndex < 0 || seasonIndex >= seasonValues.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(seasonIndex), seasonIndex, "Outside the ruleset's supplyConsumption.seasonValues table.");
        }

        var seasonValue = seasonValues[seasonIndex];
        var s = populationThousands * (seasonValue - economy.CitySupplyBaselineSeasonValue) / economy.CitySupplyProductionDivisor;
        var inc = s - (s * ownerMobilizedPercent / economy.CitySupplyMobilizationDivisor);

        if (threatened)
        {
            inc = Math.Min(inc, 0);
        }

        var cap = populationThousands * economy.CitySupplyCapTonsPerPopulationThousand;
        var supplyTons = Math.Max(0, Math.Min(currentSupplyTons + inc, cap));

        var isWinter = seasonIndex == ruleset.Calendar.SeasonsPerYear - 1;
        var loyaltyDelta = 0;
        if (supplyTons == 0 && isWinter && rng.NextChance(1, economy.FamineLoyaltyLossProbabilityDenominator))
        {
            loyaltyDelta = -economy.FamineLoyaltyLossAmount;
        }

        return new Result(supplyTons, loyaltyDelta);
    }
}
