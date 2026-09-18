using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The quarterly mobilization decay and unity update — <c>FUN_00451b40</c>'s nation loop
/// <strong>[confirmed: city-population-growth.md, mobilization -3 in 71 of 80 sampled nation-quarters;
/// unity matched in 60 of 80]</strong>. There is no flat unity decay: the quarterly "−3"
/// <c>decompiled-quarterly-billing-and-economy.md</c> attributed to unity is mobilization's
/// (bug <c>#68</c>).
/// </summary>
public static class NationUnityUpdate
{
    /// <summary>
    /// Decays mobilization by <see cref="EconomyRules.MobilizationDecayPerQuarter"/>, floored at 0.
    /// </summary>
    public static int DecayMobilization(int mobilizedPercent, EconomyRules economy)
    {
        ArgumentNullException.ThrowIfNull(economy);
        return Math.Max(0, mobilizedPercent - economy.MobilizationDecayPerQuarter);
    }

    /// <summary>
    /// <c>unity = min(UnityCap, max(UnityFloor, unity + UnityBaseGainPerQuarter − taxRatePercent /
    /// UnityTaxRateDivisor − decayedMobilizedPercent / UnityMobilizationDivisor))</c>.
    /// </summary>
    /// <param name="unity">The nation's unity before this quarter's update.</param>
    /// <param name="taxRatePercent">The nation's current tax rate.</param>
    /// <param name="decayedMobilizedPercent">
    /// The nation's mobilization <em>after</em> this quarter's <see cref="DecayMobilization"/>.
    /// </param>
    /// <param name="economy">Supplies every constant of the clamp and the update — never a C# literal.</param>
    public static int Compute(int unity, int taxRatePercent, int decayedMobilizedPercent, EconomyRules economy)
    {
        ArgumentNullException.ThrowIfNull(economy);

        var raw = unity
                  + economy.UnityBaseGainPerQuarter
                  - (taxRatePercent / economy.UnityTaxRateDivisor)
                  - (decayedMobilizedPercent / economy.UnityMobilizationDivisor);

        return Math.Min(economy.UnityCap, Math.Max(economy.UnityFloor, raw));
    }
}
