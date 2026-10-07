using IC2.Engine.Economy;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// T72 (bug #315): <see cref="PurseAccounting.Credit"/> adds a purse without the 1,000 cap and enforces
/// only the field's own <c>0 … 32,767</c> range. The pre-T72 reading of T08 Done-when 6 ("the purse cap
/// of 1,000 is enforced on every path that credits a purse") is superseded by
/// <c>2026-10-05-army-purse-writes-and-the-1000-cap.md</c> (research 9ae8924): 1,000 is the supply
/// dialog's input clamp on one write path (<c>TAFSupply_ChangeMoney</c>, report rows 1 and 2 — the clamp
/// lives in <see cref="TreasuryPurseTransfer"/> now, with the own-city refill of
/// <see cref="AutomaticResupply"/>) — not a property of the purse record. Rows 3, 5b, 7, 12 and 13 and
/// the Join-fleets row of <c>decompiled-unit-map-orders-and-record-fields.md</c> all add with no cap,
/// and <c>IP016.sav</c> army 1 holds 1,066 (the range sweep
/// <c>tests/IC2.Data.Tests/CorpusFixtures/ArmyFleetFieldRangeSweepTests.cs</c>, which bounds the field
/// at <c>0..short.MaxValue</c> and says so explicitly).
/// </summary>
public sealed class PurseAccountingTests
{
    [Fact]
    public void Credit_AddsNormally() =>
        Assert.Equal(300, PurseAccounting.Credit(currentMoney: 100, amount: 200));

    /// <summary>
    /// The bug #315 correction: 900 + 200 lands at 1,100, not at the old 1,000 clamp. An over-1,000
    /// purse is legitimate — the joins make them (row 7: 1,000 + 1,000 = 2,000 [Wine candidates
    /// Q1_05_before_join.SAV → Q1_06_after_join.SAV]) and a real save holds 1,066.
    /// </summary>
    [Fact]
    public void Credit_PastOneThousand_IsNotCutToTheCap() =>
        Assert.Equal(1100, PurseAccounting.Credit(currentMoney: 900, amount: 200));

    /// <summary>A debit is exact: 1,066 − 20 = 1,046 ("a debit never raises or cuts a purse beyond the
    /// amount debited" — Done-when 2). This is what silently cut the purse to 1,000 before T72.</summary>
    [Fact]
    public void Credit_DebitFromAnOverCapPurse_KeepsTheExactBalance() =>
        Assert.Equal(1046, PurseAccounting.Credit(currentMoney: 1066, amount: -20));

    /// <summary>The row 3 giveback: 10,000 troops, 206 supplies, purse 1,000 — room −105, amount −105,
    /// the purse RISES by 21 to 1,021 with no cap [derived: the 2026-10-05 report's own worked
    /// example].</summary>
    [Fact]
    public void Credit_NegativeDebit_Refund_RaisesUncapped() =>
        Assert.Equal(1021, PurseAccounting.Credit(currentMoney: 1000, amount: 21));

    /// <summary>Done-when 4: no purse leaves the field's range. The original's join and captured-purse
    /// adds are 16-bit and WRAP above 32,767, possibly to a negative purse (report rows 7 and 12
    /// [derived: code, not played]); the clone enforces the bound instead [designed: the user's
    /// 2026-10-05 choice not to reproduce the wrap, PR #758's R2 resolution], and the excess above
    /// 32,767 goes nowhere — no treasury on these paths is touched.</summary>
    [Fact]
    public void Credit_AboveFieldMax_LandsAtThirtyTwoThousandSevenHundredSixtySeven() =>
        Assert.Equal(32767, PurseAccounting.Credit(currentMoney: 32000, amount: 1000));

    /// <summary>The field floor: the upkeep tick floors every purse at 0
    /// (<c>upkeep-payment-and-desertion.md</c>, the signed compare at <c>00451c1c</c> and the tick-end
    /// floor), and no wired debit ever reaches this — purchase and resupply debits are affordability-
    /// clamped first. The bound guards a hand-built state.</summary>
    [Fact]
    public void Credit_BelowZero_LandsAtTheFieldFloor() =>
        Assert.Equal(0, PurseAccounting.Credit(currentMoney: 5, amount: -10));

    /// <summary>Done-when 4's bound, pinned at its source: the field is signed 16-bit floored at 0 —
    /// 0 … <see cref="short.MaxValue"/> (<c>ArmyFleetFieldRangeSweepTests</c>).</summary>
    [Fact]
    public void FieldBounds_AreTheSignedSixteenBitRange()
    {
        Assert.Equal(0, PurseAccounting.PurseFieldMin);
        Assert.Equal(short.MaxValue, PurseAccounting.PurseFieldMax);
    }

    /// <summary>
    /// T72: <see cref="PurseAccounting.Credit"/> no longer reads <c>EconomyRules.PurseCapPerUnit</c> at
    /// all — narrowing the ruleset's dialog clamp changes nothing here (the pre-T72 version of this
    /// test pinned <c>Credit(400, 200)</c> to 500; the report's rows settle that 1,000 is a clamp of
    /// three specific paths, not a property of every credit). The writers that need the clamp —
    /// <see cref="TreasuryPurseTransfer"/> and <see cref="AutomaticResupply"/>'s own-city hygiene —
    /// read it themselves and are pinned in their own test files.
    /// </summary>
    [Fact]
    public void Credit_DoesNotReadTheRulesetsPurseCap()
    {
        Assert.Equal(600, PurseAccounting.Credit(currentMoney: 400, amount: 200));
    }
}
