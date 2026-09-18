using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The tax-base and wealth adjustment a city-ownership change makes — <c>FUN_0044bb18</c> and siblings
/// — for T17 (capture/siege/defection) and any later ownership change to call, so the adjustment is
/// written once rather than re-derived at every capture site
/// <strong>[confirmed: nation-tax-base-and-city-economy-fields.md]</strong>.
/// </summary>
/// <remarks>
/// Reproduces the report's own Naupactus capture exactly: tribute 15, population 25, maximum 30, so its
/// contribution is <c>15 × 25 / 30 = 12</c>; Illyria's tax base <c>396 → 444</c>, Greece's
/// <c>2,296 → 2,248</c>; wealth <c>768,000 → 843,000</c> and <c>2,490,000 → 2,415,000</c>. This helper
/// covers only the tax-base and wealth terms; the capture path's treasury credit, unity and city-count
/// adjustments are T17's own.
/// </remarks>
public static class CityOwnershipTaxTransfer
{
    /// <summary>
    /// Returns the new and old owner with <paramref name="city"/>'s tax-base and wealth contribution
    /// moved from one to the other, using <paramref name="city"/>'s state <em>at transfer time</em> (after
    /// any siege damage).
    /// </summary>
    public static (NationState NewOwner, NationState OldOwner) Transfer(
        CityState city, NationState newOwner, NationState oldOwner, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(newOwner);
        ArgumentNullException.ThrowIfNull(oldOwner);
        ArgumentNullException.ThrowIfNull(ruleset);

        var economy = ruleset.Economy;
        var contribution = CityTaxContribution.Compute(city);
        var taxBaseDelta = contribution * economy.TaxBaseContributionMultiplier;
        var wealthDelta = city.PopulationThousands * economy.WealthPerPopulationThousand;

        var updatedNewOwner = newOwner with
        {
            TaxBase = newOwner.TaxBase + taxBaseDelta,
            Wealth = newOwner.Wealth + wealthDelta,
        };
        var updatedOldOwner = oldOwner with
        {
            TaxBase = oldOwner.TaxBase - taxBaseDelta,
            Wealth = oldOwner.Wealth - wealthDelta,
        };

        return (updatedNewOwner, updatedOldOwner);
    }
}
