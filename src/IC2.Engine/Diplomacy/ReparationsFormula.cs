using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Diplomacy;

/// <summary>
/// The post-battle reparations formula (DoD 7) — <c>FUN_00450C68</c>
/// <strong>[confirmed: decompiled-diplomacy-peace-terms-and-instant-battles.md]</strong>:
/// <c>reparations = W/4 + random(W/4) + cities × 10</c>, where <c>W</c> is the loser's
/// <see cref="NationState.TaxBase"/> (nation <c>+0x44C</c> — <strong>not</strong>
/// <see cref="NationState.Wealth"/>; an earlier reading of the source report called <c>+0x44C</c>
/// "wealth", which <c>nation-tax-base-and-city-economy-fields.md</c> corrects).
/// </summary>
/// <remarks>
/// The one recorded payment checks against this exactly: Ptolemaic, <c>W = 6,188</c>, 48 cities gives
/// the range <c>[2,027, 3,573]</c>, and the observed 2,269 is inside it
/// (<see cref="ReparationsFormula"/> tests reproduce this check directly).
/// </remarks>
public static class ReparationsFormula
{
    /// <summary>
    /// Computes one reparations amount. <paramref name="rng"/> draws exactly once, in
    /// <c>[0, taxBase / <see cref="DiplomacyRules.ReparationsWealthDivisor"/>)</c> — Delphi's
    /// <c>Random(N)</c> range, matching <see cref="IRng.NextInt(int)"/>'s own documented range.
    /// </summary>
    /// <param name="loserTaxBase">The loser's <see cref="NationState.TaxBase"/> at the moment of the treaty.</param>
    /// <param name="loserCityCount">The loser's city count at the same moment.</param>
    /// <param name="ruleset">Supplies the two divisors/multipliers; never a C# literal.</param>
    /// <param name="rng">The treaty's own random stream.</param>
    public static int Compute(int loserTaxBase, int loserCityCount, Ruleset ruleset, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(rng);

        var diplomacy = ruleset.Diplomacy;
        var share = loserTaxBase / diplomacy.ReparationsWealthDivisor;

        // Random(0) has no confirmed meaning in the original and a zero-width IRng draw would throw;
        // a tax base under the divisor (never observed, but not excluded by the formula) draws nothing
        // extra rather than crashing the treaty.
        var randomBonus = share > 0 ? rng.NextInt(share) : 0;

        return share + randomBonus + (loserCityCount * diplomacy.ReparationsPerCity);
    }

    /// <summary>
    /// The inclusive <c>[min, max]</c> range <see cref="Compute"/> can produce for a given tax base and
    /// city count — every value <paramref name="rng"/> could draw, from <c>0</c> to <c>share - 1</c>.
    /// </summary>
    public static (int Min, int Max) Range(int loserTaxBase, int loserCityCount, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);

        var diplomacy = ruleset.Diplomacy;
        var share = loserTaxBase / diplomacy.ReparationsWealthDivisor;
        var flat = share + (loserCityCount * diplomacy.ReparationsPerCity);
        var maxBonus = share > 0 ? share - 1 : 0;

        return (flat, flat + maxBonus);
    }
}
