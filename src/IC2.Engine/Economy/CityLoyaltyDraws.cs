using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The quarterly loyalty draws and rebellion-risk check — <c>FUN_00451b40</c>'s city loop
/// <strong>[derived: city-population-growth.md, from the code, not save-checked]</strong>, T08 follow-up
/// <c>#76</c> N1 and bug <c>#72</c>: T08 shipped <see cref="EconomyRules.LowTaxLoyaltyThresholdPercent"/>,
/// <see cref="EconomyRules.LowTaxLoyaltyCityThreshold"/> and <see cref="EconomyRules.RebellionLoyaltyThreshold"/>
/// as data nothing read; this task is the first to read them. T89 (<c>#397</c>,
/// <see href="https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-quarterly-rebellion.md">
/// decompiled-quarterly-rebellion.md</see>) is what finally consumes the rebellion-risk flag this type
/// returns — <see cref="Rebellion"/>, called by <see cref="QuarterlyCityEconomySystem"/> right after this
/// type's own draws, in the report's own code order.
/// </summary>
/// <remarks>
/// <c>if taxRate &lt; 11 and loyalty &lt; 80: loyalty += Random(4)</c> (0..3); <c>if Random(3) == 0:
/// loyalty −= Random(taxRate) / 8</c>; <c>if loyalty &lt; 30 and the city is no nation's capital:</c> a
/// rebellion risk. This type still returns only whether the risk fired, never a new owner — deciding a
/// new owner is <see cref="Rebellion.Run"/>'s own job, kept out of this pure-draw type.
/// <para>
/// Both draws happen unconditionally in the order the pseudocode gives, exactly one <see cref="IRng"/>
/// call each where the guarding condition holds — the same conditional-draw discipline the rest of this
/// engine uses, so a fixed seed reproduces the exact sequence a caller processing cities in list (index)
/// order draws from a shared stream.
/// </para>
/// <para>
/// <strong>T89 correction (decompiled-quarterly-rebellion.md §"Random draws": "Random is FUN_0040284C:
/// seed = seed × 0x08088405 + 1; return (seed × range) &gt;&gt; 32. It advances the seed even for range =
/// 0" [confirmed]).</strong> <see cref="IRng.NextInt(int)"/> requires a positive bound (its own contract),
/// so at <paramref name="ownerTaxRatePercent"/> 0 this draws one raw value directly with
/// <see cref="IRng.NextUInt64"/> and discards it — the same "one draw is consumed whatever the odds are,
/// including the degenerate 0-in-n case" idiom <see cref="SplitMix64Rng.NextChance"/>'s own remark
/// already documents — rather than skipping the draw outright, which is what this type did before T89 and
/// is exactly the "real divergence of the random stream" the report's own Scope calls out: the loss is 0
/// either way (the original's own <c>Random(0)</c> also returns 0), but skipping left this engine's stream
/// one draw behind the original's for every later draw in the same quarter, whenever a tax-0 city's
/// <c>Random(3)</c> hit 0.
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
            int loss;
            if (ownerTaxRatePercent > 0)
            {
                loss = rng.NextInt(ownerTaxRatePercent) / economy.LoyaltyFallTaxDivisor;
            }
            else
            {
                // T89: Random(0) still steps the original's own stream once (see this type's own
                // remarks) -- IRng.NextInt cannot be called with a zero bound, so this draws the one raw
                // value directly and discards it. The loss is 0 either way.
                rng.NextUInt64();
                loss = 0;
            }

            loyalty -= loss;
        }

        var rebellionRisk = !isCapital && loyalty < economy.RebellionLoyaltyThreshold;
        return new Result(loyalty, rebellionRisk);
    }
}
