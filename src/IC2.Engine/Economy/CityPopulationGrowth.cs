using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// Quarterly city population growth — <c>FUN_00451b40</c>'s city loop, growth step
/// <strong>[confirmed: city-population-growth.md, 494 of 494 growing cities and 1,497 of 1,497 at their
/// maximum, over 6 save pairs]</strong>. Deterministic: no random draw.
/// </summary>
/// <remarks>
/// <c>d = (maxPop − pop) / EconomyRules.PopulationGrowthGapDivisor</c> (truncated); <c>d' = d − d ×
/// taxRatePercent / EconomyRules.PopulationGrowthTaxDivisor</c>; <c>growth = d' − d' × mobilizedPercent /
/// EconomyRules.PopulationGrowthMobilizationDivisor + EconomyRules.PopulationGrowthConstantAddend</c>. A
/// city at or above its maximum, or threatened by a hostile army
/// (<see cref="HostileArmyAdjacent.IsThreatened"/>), does not grow this quarter. The result never exceeds
/// <paramref name="maxPopulationThousands"/> — a safety cap the report notes never actually binds for
/// legal ruleset values, since growth is provably at least 1 and at most the gap.
/// </remarks>
public static class CityPopulationGrowth
{
    /// <summary>
    /// Computes the grown population. Reads the owner's tax rate and mobilization <em>before</em> this
    /// quarter's mobilization decay (<see cref="NationUnityUpdate"/> runs later, in the nation loop).
    /// </summary>
    /// <param name="populationThousands">The city's population before growth.</param>
    /// <param name="maxPopulationThousands">The city's maximum population.</param>
    /// <param name="ownerTaxRatePercent">The owning nation's current tax rate.</param>
    /// <param name="ownerMobilizedPercent">The owning nation's current (pre-decay) mobilization.</param>
    /// <param name="threatened">Whether a hostile army stands adjacent to the city this quarter.</param>
    /// <param name="economy">Supplies every divisor and the addend — never a C# literal.</param>
    public static int Grow(
        int populationThousands,
        int maxPopulationThousands,
        int ownerTaxRatePercent,
        int ownerMobilizedPercent,
        bool threatened,
        EconomyRules economy)
    {
        ArgumentNullException.ThrowIfNull(economy);

        if (threatened || populationThousands >= maxPopulationThousands)
        {
            return populationThousands;
        }

        var gap = maxPopulationThousands - populationThousands;
        var d = gap / economy.PopulationGrowthGapDivisor;
        var dPrime = d - (d * ownerTaxRatePercent / economy.PopulationGrowthTaxDivisor);
        var growth = dPrime
                     - (dPrime * ownerMobilizedPercent / economy.PopulationGrowthMobilizationDivisor)
                     + economy.PopulationGrowthConstantAddend;

        return Math.Min(maxPopulationThousands, populationThousands + growth);
    }
}
