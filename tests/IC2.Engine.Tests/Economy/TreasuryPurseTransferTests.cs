using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T38 Supply dialog follow-ups, treasury ↔ purse transfers, and
/// automatic resupply" (issue #78), Done-when 6: <c>TAFSupply_ChangeMoney</c> moves talents between the
/// national treasury and an army's or fleet's own purse, in either direction, never taking more than the
/// purse source holds and never ending a purse above 1,000 through a positive request. T219 permits faithful manual treasury funding into debt.
/// T72 (bug #757, folded in): the positive arrow is SIGNED like the original's — a request into a purse
/// already above 1,000 moves <c>1000 − purse</c> (negative), the purse falls to exactly 1,000 and the
/// treasury gains the excess (2026-10-05-army-purse-writes-and-the-1000-cap.md rows 1 and 2:
/// <c>step := min(step, 1000 − purse)</c>, "no floor at 0"; in play, one "+100" click from the joined
/// 2,000 moved −1,000 [Wine candidates <c>Q1_06_after_join.SAV</c> → <c>Q1b_01_after_one_up_click.SAV</c>]),
/// replacing the T105 round-1 N5 guard that applied nothing in that case.
/// </summary>
public sealed class TreasuryPurseTransferTests
{
    private static Ruleset DesignedRules => EconomyTestbed.Ruleset with
    { Flags = EconomyTestbed.Ruleset.Flags with { EconomyPurses = EconomyPurseModel.CentralTreasury } };

    private static NationState Nation(int treasury) =>
        new("north", "North", "#000", "Leader", null, SeatControl.Human, null, treasury, 600, 0, 0, 15, 0, 100, 100, 500, 1,
            ValueList<RecruitmentSlot>.Empty, false);

    private static ArmyState Army(int money) =>
        new("a1", "north", 0, 0, 9, 60, money, 0, null, null, ValueList<UnitSlot>.Empty);

    private static FleetState Fleet(int money) =>
        new("f1", "north", 0, 0, 4, 10, 100, money, 0, null, null, null, null);

    [Theory]
    [InlineData(false, -100, -200)]
    [InlineData(false, 0, -100)]
    [InlineData(false, 50, -50)]
    [InlineData(true, -100, -200)]
    [InlineData(true, 0, -100)]
    [InlineData(true, 50, -50)]
    public void Faithful_manual_treasury_funding_matches_the_measured_original_cases(bool fleet, int treasury, int expectedTreasury)
    {
        var nation = Nation(treasury);
        var rules = EconomyTestbed.Ruleset;
        var armyResult = fleet ? null : TreasuryPurseTransfer.TransferWithArmy(nation, Army(100), 100, rules);
        var fleetResult = fleet ? TreasuryPurseTransfer.TransferWithFleet(nation, Fleet(100), 100, rules) : null;
        Assert.Equal(expectedTreasury, armyResult?.Nation.Treasury ?? fleetResult!.Nation.Treasury);
        Assert.Equal(200, armyResult?.Army.Money ?? fleetResult!.Fleet.Money);
        Assert.Equal(100, armyResult?.AppliedTalents ?? fleetResult!.AppliedTalents);
        Assert.Equal(treasury + 100, expectedTreasury + 200);
        Assert.Equal(treasury, nation.Treasury);
    }

    [Theory]
    [InlineData(false, -100, 0)]
    [InlineData(false, 0, 0)]
    [InlineData(false, 50, 50)]
    [InlineData(true, -100, 0)]
    [InlineData(true, 0, 0)]
    [InlineData(true, 50, 50)]
    public void Designed_treasury_funding_keeps_its_balance_limit(bool fleet, int treasury, int expectedMoved)
    {
        var nation = Nation(treasury);
        if (fleet)
        {
            var result = TreasuryPurseTransfer.TransferWithFleet(nation, Fleet(100), 100, DesignedRules);
            Assert.Equal(100 + expectedMoved, result.Fleet.Money);
            Assert.Equal(treasury - expectedMoved, result.Nation.Treasury);
        }
        else
        {
            var result = TreasuryPurseTransfer.TransferWithArmy(nation, Army(100), 100, DesignedRules);
            Assert.Equal(100 + expectedMoved, result.Army.Money);
            Assert.Equal(treasury - expectedMoved, result.Nation.Treasury);
        }
    }

    [Theory]
    [InlineData(50, 100, 100, 0, 150, 50)]
    [InlineData(0, 100, 100, 0, 100, 0)]
    [InlineData(50, 1100, 100, 150, 1000, -100)]
    [InlineData(50, 100, -100, 150, 0, -100)]
    [InlineData(950, 100, -100, 1000, 50, -50)]
    public void Faithful_fleet_purse_controls_keep_source_and_receiving_limits(int source, int target, int request,
        int expectedSource, int expectedTarget, int expectedMoved)
    {
        var result = TreasuryPurseTransfer.TransferBetweenPurses(source, target, request, EconomyTestbed.Ruleset);
        Assert.Equal(expectedSource, result.FromMoney);
        Assert.Equal(expectedTarget, result.ToMoney);
        Assert.Equal(expectedMoved, result.AppliedTalents);
        Assert.Equal(source + target, result.FromMoney + result.ToMoney);
    }

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
    public void Designed_TransferWithArmy_RequestExceedsTreasury_ClampsToTheTreasurysBalance()
    {
        var nation = Nation(treasury: 50);
        var army = Army(money: 0);

        var result = TreasuryPurseTransfer.TransferWithArmy(nation, army, talentsIntoPurse: 200, DesignedRules);

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
    /// T72, Done-when 3 (the bug #757 case, folded in): the dialog's arrow is signed — a request of 100
    /// into a 2,000 purse moves <c>min(100, 1000 − 2000) = −1,000</c>, leaving the purse at exactly 1,000
    /// and raising the treasury by 1,000 (2026-10-05 report row 1 [derived:
    /// <c>TAFSupply_ChangeMoney</c> :43118-43122]; in play <c>Q1_06_after_join.SAV</c> →
    /// <c>Q1b_01_after_one_up_click.SAV</c>, treasury 201 → 1,201). It fails on pre-T72 <c>main</c>,
    /// where the N5 guard applied nothing: purse stayed 2,000, treasury 201.
    /// </summary>
    [Fact]
    public void TransferWithArmy_PositiveRequestIntoPurseAboveCap_PullsBackToCapAndFeedsTreasury()
    {
        var nation = Nation(treasury: 201);
        var army = Army(money: 2000);

        var result = TreasuryPurseTransfer.TransferWithArmy(nation, army, talentsIntoPurse: 100, EconomyTestbed.Ruleset);

        Assert.Equal(-1000, result.AppliedTalents); // negative into a positive request: the signed arrow.
        Assert.Equal(1000, result.Army.Money); // falls to exactly the cap.
        Assert.Equal(1201, result.Nation.Treasury); // 201 + 1,000: the treasury gains the excess, conserved.
        Assert.Equal(nation.Treasury + army.Money, result.Nation.Treasury + result.Army.Money); // conservation.
    }

    /// <summary>
    /// The signed pull-back moves <c>min(step, 1000 − purse)</c> — when the purse sits only 90 above the
    /// cap, one "+10" click pulls 90 out, not 10 in (row 1's step rule, [derived]).
    /// </summary>
    [Fact]
    public void TransferWithArmy_SmallRequestIntoPurseSlightlyOverCap_PullsBackTheDifference()
    {
        var nation = Nation(treasury: 500);
        var army = Army(money: 1090);

        var result = TreasuryPurseTransfer.TransferWithArmy(nation, army, talentsIntoPurse: 10, EconomyTestbed.Ruleset);

        Assert.Equal(-90, result.AppliedTalents);
        Assert.Equal(1000, result.Army.Money);
        Assert.Equal(590, result.Nation.Treasury);
        Assert.Equal(nation.Treasury + army.Money, result.Nation.Treasury + result.Army.Money); // conservation.
    }

    /// <summary>
    /// Row 2: the "down" arrow drains a purse with no cap logic on money leaving it — a 1,500 purse asked
    /// for −100 simply pays 100 to the treasury (it is the pull-back path, row 1, that touches the cap).
    /// </summary>
    [Fact]
    public void TransferWithArmy_NegativeRequestFromOverCapPurse_DrainsNormally()
    {
        var nation = Nation(treasury: 500);
        var army = Army(money: 1500);

        var result = TreasuryPurseTransfer.TransferWithArmy(nation, army, talentsIntoPurse: -100, EconomyTestbed.Ruleset);

        Assert.Equal(-100, result.AppliedTalents);
        Assert.Equal(1400, result.Army.Money); // drained, not cut to the cap.
        Assert.Equal(600, result.Nation.Treasury);
        Assert.Equal(nation.Treasury + army.Money, result.Nation.Treasury + result.Army.Money); // conservation.
    }

    /// <summary>
    /// Review round 1, B1: a negative treasury is not a hypothetical (<c>PurseAccounting</c>'s own
    /// remark cites <c>reparation.ptolemaicTreasuryAfter: -1,270</c>; <c>docs/design-audit.md</c> Q9
    /// quotes a -818 national balance in the Galatia frames; T39 bills the treasury with no balance
    /// check). Under the designed policy, a treasury that holds nothing supplies nothing -- the request into the purse clamps to
    /// zero, it does not reverse into a withdrawal from the purse. The pre-fix code returned
    /// <c>Math.Min(requested, treasury)</c> unclamped, which for a negative treasury returned the
    /// treasury itself: a request for +100 pulled the full -500 out of the purse instead.
    /// </summary>
    [Fact]
    public void Designed_TransferWithArmy_NegativeTreasury_RequestIntoPurse_TakesNothing()
    {
        var nation = Nation(treasury: -500);
        var army = Army(money: 300);

        var result = TreasuryPurseTransfer.TransferWithArmy(nation, army, talentsIntoPurse: 100, DesignedRules);

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

    /// <summary>The fleet twin of <see cref="Designed_TransferWithArmy_NegativeTreasury_RequestIntoPurse_TakesNothing"/> -- the shared clamp is fixed for both callers, not just the army path.</summary>
    [Fact]
    public void Designed_TransferWithFleet_NegativeTreasury_RequestIntoPurse_TakesNothing()
    {
        var nation = Nation(treasury: -500);
        var fleet = Fleet(money: 300);

        var result = TreasuryPurseTransfer.TransferWithFleet(nation, fleet, talentsIntoPurse: 100, DesignedRules);

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
