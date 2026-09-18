using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The quarterly loyalty draws and rebellion-risk check — <c>FUN_00451b40</c>'s city loop
/// <strong>[derived: city-population-growth.md, from the code, not save-checked]</strong>, T08 follow-up
/// <c>#76</c> N1 and bug <c>#72</c>: T08 shipped <see cref="EconomyRules.LowTaxLoyaltyThresholdPercent"/>,
/// <see cref="EconomyRules.LowTaxLoyaltyCityThreshold"/>, <see cref="EconomyRules.RebellionLoyaltyThreshold"/>
/// and <see cref="RebellionRiskDetected"/> as data and an event nothing read or published; this task is
/// the first to read either.
/// </summary>
/// <remarks>
/// <c>if taxRate &lt; 11 and loyalty &lt; 80: loyalty += Random(4)</c> (0..3); <c>if Random(3) == 0:
/// loyalty −= Random(taxRate) / 8</c>; <c>if loyalty &lt; 30 and the city is no nation's capital:</c> a
/// rebellion risk. The rebellion itself — which nation the city goes to — is only partly traced
/// (<c>FUN_0044c204</c> depends on an unidentified bitmask) and stays T17's known-open item; this
/// returns only whether the risk fired, never a new owner.
/// <para>
/// Both draws happen unconditionally in the order the pseudocode gives, exactly one <see cref="IRng"/>
/// call each where the guarding condition holds — the same conditional-draw discipline the rest of this
/// engine uses, so a fixed seed reproduces the exact sequence a caller processing cities in list (index)
/// order draws from a shared stream.
/// </para>
/// <para>
/// <c>Random(taxRatePercent)</c> is skipped (loss forced to 0) when <paramref name="ownerTaxRatePercent"/>
/// is not positive: <see cref="IRng.NextInt(int)"/> requires a positive bound, and a zero tax rate would
/// make the original's own <c>Random(0)</c> call — which Delphi defines to return 0 — contribute nothing
/// to the loss either way.
/// </para>
/// </remarks>
public static class CityLoyaltyDraws
{
    /// <summary>The result of one city's quarterly loyalty draws.</summary>
    /// <param name="Loyalty">The city's loyalty after both draws.</param>
    /// <param name="RebellionRisk">Whether the rebellion-risk condition fired.</param>
    public readonly record struct Result(int Loyalty, bool RebellionRisk);

    /// <summary>Applies one city's quarterly loyalty draws.</summary>
    /// <param name="city">The city, read for its current loyalty only.</param>
    /// <param name="ownerTaxRatePercent">The owning nation's current tax rate.</param>
    /// <param name="isCapital">Whether this city is any nation's capital.</param>
    /// <param name="economy">Supplies every threshold and roll constant — never a C# literal.</param>
    /// <param name="rng">This quarter's shared draw stream, consumed in the caller's city-index order.</param>
    public static Result Apply(CityState city, int ownerTaxRatePercent, bool isCapital, EconomyRules economy, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(economy);
        ArgumentNullException.ThrowIfNull(rng);

        var loyalty = city.Loyalty;

        if (ownerTaxRatePercent < economy.LowTaxLoyaltyThresholdPercent && loyalty < economy.LowTaxLoyaltyCityThreshold)
        {
            loyalty += rng.NextInt(economy.LoyaltyRiseRollBound);
        }

        if (rng.NextChance(1, economy.LoyaltyFallProbabilityDenominator))
        {
            var loss = ownerTaxRatePercent > 0 ? rng.NextInt(ownerTaxRatePercent) / economy.LoyaltyFallTaxDivisor : 0;
            loyalty -= loss;
        }

        var rebellionRisk = !isCapital && loyalty < economy.RebellionLoyaltyThreshold;
        return new Result(loyalty, rebellionRisk);
    }
}
