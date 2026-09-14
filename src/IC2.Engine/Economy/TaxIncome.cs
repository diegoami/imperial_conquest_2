using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The original's tax-income formula — <c>docs/build-orchestration-plan.md</c> "T08 Economy, supply, and
/// purses", Done-when 1.
/// </summary>
/// <remarks>
/// <c>income = nationTaxBase × taxRatePercent / 100</c>
/// <strong>[confirmed: decompiled-fleet-tax-and-mercenary-formulas.md, decompiled-quarterly-billing-and-economy.md]</strong>
/// — <c>TChangeTax_PrintNewNumbers</c>, and confirmed as the literal quarterly treasury credit (not just a
/// dialog preview) by the billing report. Rome's <c>nationTaxBase = 2,440</c> was solved from the two
/// published data points (366 at 15%, 488 at 20%) and reproduces both exactly with no rounding
/// (<c>tests/fixtures/corpus.json</c> <c>tax.*</c>).
/// </remarks>
public static class TaxIncome
{
    /// <summary>Computes one quarter's tax income for a nation.</summary>
    /// <param name="nationTaxBase">
    /// The nation's tax-base field (the original's <c>+0x44C</c>-adjacent field solved to 2,440 for
    /// Rome). Not yet a persisted <see cref="NationState"/> field — no other task has claimed it, and no
    /// <c>GameState</c> field for it exists to read here — so it is taken as a plain input, exactly the
    /// way <see cref="IC2.Engine.Strength.ArmyPower"/> takes morale as a plain input rather than reaching
    /// into <see cref="Model.ArmyState"/> itself.
    /// </param>
    /// <param name="taxRatePercent">The nation's current tax rate, as a whole percentage.</param>
    /// <param name="ruleset">Supplies <see cref="EconomyRules.TaxRateDivisor"/> — never a C# literal.</param>
    public static int Compute(int nationTaxBase, int taxRatePercent, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(ruleset);
        return nationTaxBase * taxRatePercent / ruleset.Economy.TaxRateDivisor;
    }
}
