using IC2.Engine.Economy;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/build-orchestration-plan.md</c> "T08 Economy, supply, and purses", Done-when 1:
/// "<c>income = 2440 × 15 / 100</c> and <c>× 20 / 100</c> reproduce both published Rome figures exactly."
/// </summary>
public sealed class TaxIncomeTests
{
    [Fact]
    public void Compute_RomeAt15Percent_Reproduces366()
    {
        var income = TaxIncome.Compute(nationTaxBase: 2440, taxRatePercent: 15, EconomyTestbed.Ruleset);
        Assert.Equal(366, income);
    }

    [Fact]
    public void Compute_RomeAt20Percent_Reproduces488()
    {
        var income = TaxIncome.Compute(nationTaxBase: 2440, taxRatePercent: 20, EconomyTestbed.Ruleset);
        Assert.Equal(488, income);
    }

    [Fact]
    public void Compute_UsesTheRulesetsTaxRateDivisorNotALiteral()
    {
        // A ruleset with a different divisor drives the model differently -- proves the /100 in the
        // formula is Ruleset.Economy.TaxRateDivisor, not a hardcoded 100.
        var wider = EconomyTestbed.Ruleset with
        {
            Economy = EconomyTestbed.Ruleset.Economy with { TaxRateDivisor = 1000 },
        };

        var income = TaxIncome.Compute(nationTaxBase: 2440, taxRatePercent: 15, wider);

        Assert.Equal(2440 * 15 / 1000, income);
        Assert.NotEqual(366, income);
    }
}
