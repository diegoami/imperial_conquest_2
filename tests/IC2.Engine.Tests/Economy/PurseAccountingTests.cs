using IC2.Engine.Economy;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/build-orchestration-plan.md</c> "T08 Economy, supply, and purses", Done-when 6: "The purse cap
/// of 1,000 is enforced on every path that credits a purse."
/// </summary>
/// <remarks>
/// Within this task's confirmed supply-purchase mechanic (<c>docs/design-audit.md</c> Q9), no path ever
/// credits an army's or fleet's own purse — the free case moves no money at all, and the paid case only
/// ever debits the buyer. <see cref="PurseAccounting.Credit"/> is the one shared place a credit would be
/// clamped, and <see cref="SupplyPurchase"/>'s debit path already routes through it (proving the two are
/// actually wired together, not just independently correct) — see
/// <c>SupplyPurchaseTests.BuyForArmy_AtForeignCity_Costs20TalentsFor100Tons</c>. These tests exercise the
/// cap itself directly, including exactly at the boundary the Done-when line names.
/// </remarks>
public sealed class PurseAccountingTests
{
    [Fact]
    public void Credit_BelowCap_AddsNormally() =>
        Assert.Equal(300, PurseAccounting.Credit(currentMoney: 100, amount: 200, EconomyTestbed.Ruleset));

    [Fact]
    public void Credit_ExactlyToCap_Allowed() =>
        Assert.Equal(1000, PurseAccounting.Credit(currentMoney: 900, amount: 100, EconomyTestbed.Ruleset));

    [Fact]
    public void Credit_PastCap_ClampsToCapNotThePastValue() =>
        Assert.Equal(1000, PurseAccounting.Credit(currentMoney: 900, amount: 200, EconomyTestbed.Ruleset));

    [Fact]
    public void Credit_AlreadyAtCap_StaysAtCap() =>
        Assert.Equal(1000, PurseAccounting.Credit(currentMoney: 1000, amount: 50, EconomyTestbed.Ruleset));

    [Fact]
    public void Credit_NegativeAmount_IsADebitNotClampedByTheCeiling() =>
        Assert.Equal(80, PurseAccounting.Credit(currentMoney: 100, amount: -20, EconomyTestbed.Ruleset));

    [Fact]
    public void Credit_UsesTheRulesetsCapNotALiteral()
    {
        var narrower = EconomyTestbed.Ruleset with
        {
            Economy = EconomyTestbed.Ruleset.Economy with { PurseCapPerUnit = 500 },
        };

        Assert.Equal(500, PurseAccounting.Credit(currentMoney: 400, amount: 200, narrower));
    }
}
