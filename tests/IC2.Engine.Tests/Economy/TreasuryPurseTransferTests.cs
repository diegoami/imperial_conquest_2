using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T38 Supply dialog follow-ups, treasury ↔ purse transfers, and
/// automatic resupply" (issue #78), Done-when 6: <c>TAFSupply_ChangeMoney</c> moves talents between the
/// national treasury and an army's or fleet's own purse, in either direction, never taking more than the
/// source holds and never taking a purse above 1,000.
/// </summary>
public sealed class TreasuryPurseTransferTests
{
    private static NationState Nation(int treasury) =>
        new("north", "North", "#000", "Leader", null, SeatControl.Human, null, treasury, 600, 0, 15, 100, 100, 500, 1, false);

    private static ArmyState Army(int money) =>
        new("a1", "north", 0, 0, 9, 60, money, 0, null, null, ValueList<UnitSlot>.Empty);

    private static FleetState Fleet(int money) =>
        new("f1", "north", 0, 0, 4, 10, 100, money, 0, null, null, null, null);

    [Fact]
    public void TransferWithArmy_TreasuryToPurse_MovesExactlyTheRequestedAmount()
    {
        var nation = Nation(treasury: 500);
        var army = Army(money: 100);

        var result = TreasuryPurseTransfer.TransferWithArmy(nation, army, talentsIntoPurse: 200, EconomyTestbed.Ruleset);

        Assert.Equal(200, result.AppliedTalents);
        Assert.Equal(300, result.Army.Money);
        Assert.Equal(300, result.Nation.Treasury);
        Assert.Equal(nation.Treasury + army.Money, result.Nation.Treasury + result.Army.Money); // conservation.
    }

    [Fact]
    public void TransferWithArmy_PurseToTreasury_MovesExactlyTheRequestedAmount()
    {
        var nation = Nation(treasury: 500);
        var army = Army(money: 300);

        var result = TreasuryPurseTransfer.TransferWithArmy(nation, army, talentsIntoPurse: -100, EconomyTestbed.Ruleset);

        Assert.Equal(-100, result.AppliedTalents);
        Assert.Equal(200, result.Army.Money);
        Assert.Equal(600, result.Nation.Treasury);
        Assert.Equal(nation.Treasury + army.Money, result.Nation.Treasury + result.Army.Money); // conservation.
    }

    /// <summary>Never takes more than the treasury (the source) holds.</summary>
    [Fact]
    public void TransferWithArmy_RequestExceedsTreasury_ClampsToTheTreasurysBalance()
    {
        var nation = Nation(treasury: 50);
        var army = Army(money: 0);

        var result = TreasuryPurseTransfer.TransferWithArmy(nation, army, talentsIntoPurse: 200, EconomyTestbed.Ruleset);

        Assert.Equal(50, result.AppliedTalents);
        Assert.Equal(50, result.Army.Money);
        Assert.Equal(0, result.Nation.Treasury);
    }

    /// <summary>Never takes more than the purse (the source) holds.</summary>
    [Fact]
    public void TransferWithArmy_RequestExceedsPurse_ClampsToThePursesBalance()
    {
        var nation = Nation(treasury: 500);
        var army = Army(money: 30);

        var result = TreasuryPurseTransfer.TransferWithArmy(nation, army, talentsIntoPurse: -100, EconomyTestbed.Ruleset);

        Assert.Equal(-30, result.AppliedTalents);
        Assert.Equal(0, result.Army.Money);
        Assert.Equal(530, result.Nation.Treasury);
    }

    /// <summary>Never takes a purse above 1,000, even when the treasury could otherwise afford the full request.</summary>
    [Fact]
    public void TransferWithArmy_RequestWouldExceedThePurseCap_ClampsAtOneThousand()
    {
        var nation = Nation(treasury: 5000);
        var army = Army(money: 900);

        var result = TreasuryPurseTransfer.TransferWithArmy(nation, army, talentsIntoPurse: 500, EconomyTestbed.Ruleset);

        Assert.Equal(100, result.AppliedTalents); // only 100 of the 500 requested fits under the 1,000 cap.
        Assert.Equal(1000, result.Army.Money);
        Assert.Equal(4900, result.Nation.Treasury); // the treasury pays for only what actually moved.
    }

    /// <summary>
    /// Review round 1, B1: a negative treasury is not a hypothetical (<c>PurseAccounting</c>'s own
    /// remark cites <c>reparation.ptolemaicTreasuryAfter: -1,270</c>; <c>docs/design-audit.md</c> Q9
    /// quotes a -818 national balance in the Galatia frames; T39 bills the treasury with no balance
    /// check). A treasury that holds nothing supplies nothing -- the request into the purse clamps to
    /// zero, it does not reverse into a withdrawal from the purse. The pre-fix code returned
    /// <c>Math.Min(requested, treasury)</c> unclamped, which for a negative treasury returned the
    /// treasury itself: a request for +100 pulled the full -500 out of the purse instead.
    /// </summary>
    [Fact]
    public void TransferWithArmy_NegativeTreasury_RequestIntoPurse_TakesNothing()
    {
        var nation = Nation(treasury: -500);
        var army = Army(money: 300);

        var result = TreasuryPurseTransfer.TransferWithArmy(nation, army, talentsIntoPurse: 100, EconomyTestbed.Ruleset);

        Assert.Equal(0, result.AppliedTalents); // not -500: an empty treasury supplies nothing, in either direction.
        Assert.Equal(300, result.Army.Money); // unchanged.
        Assert.Equal(-500, result.Nation.Treasury); // unchanged.
        Assert.Equal(nation.Treasury + army.Money, result.Nation.Treasury + result.Army.Money); // conservation.
    }

    /// <summary>The mirror of the case above: a negative purse supplies nothing to the treasury either.</summary>
    [Fact]
    public void TransferWithArmy_NegativePurse_RequestIntoTreasury_TakesNothing()
    {
        var nation = Nation(treasury: 500);
        var army = Army(money: -200);

        var result = TreasuryPurseTransfer.TransferWithArmy(nation, army, talentsIntoPurse: -100, EconomyTestbed.Ruleset);

        Assert.Equal(0, result.AppliedTalents); // not +100: an empty purse supplies nothing, in either direction.
        Assert.Equal(-200, result.Army.Money); // unchanged.
        Assert.Equal(500, result.Nation.Treasury); // unchanged.
        Assert.Equal(nation.Treasury + army.Money, result.Nation.Treasury + result.Army.Money); // conservation.
    }

    /// <summary>The fleet twin of <see cref="TransferWithArmy_NegativeTreasury_RequestIntoPurse_TakesNothing"/> -- the shared clamp is fixed for both callers, not just the army path.</summary>
    [Fact]
    public void TransferWithFleet_NegativeTreasury_RequestIntoPurse_TakesNothing()
    {
        var nation = Nation(treasury: -500);
        var fleet = Fleet(money: 300);

        var result = TreasuryPurseTransfer.TransferWithFleet(nation, fleet, talentsIntoPurse: 100, EconomyTestbed.Ruleset);

        Assert.Equal(0, result.AppliedTalents);
        Assert.Equal(300, result.Fleet.Money);
        Assert.Equal(-500, result.Nation.Treasury);
    }

    [Fact]
    public void TransferWithFleet_MirrorsTransferWithArmy()
    {
        var nation = Nation(treasury: 500);
        var fleet = Fleet(money: 100);

        var result = TreasuryPurseTransfer.TransferWithFleet(nation, fleet, talentsIntoPurse: 200, EconomyTestbed.Ruleset);

        Assert.Equal(200, result.AppliedTalents);
        Assert.Equal(300, result.Fleet.Money);
        Assert.Equal(300, result.Nation.Treasury);
    }

    [Fact]
    public void TransferWithArmy_ZeroRequest_IsANoOp()
    {
        var nation = Nation(treasury: 500);
        var army = Army(money: 100);

        var result = TreasuryPurseTransfer.TransferWithArmy(nation, army, talentsIntoPurse: 0, EconomyTestbed.Ruleset);

        Assert.Equal(0, result.AppliedTalents);
        Assert.Equal(100, result.Army.Money);
        Assert.Equal(500, result.Nation.Treasury);
    }
}
