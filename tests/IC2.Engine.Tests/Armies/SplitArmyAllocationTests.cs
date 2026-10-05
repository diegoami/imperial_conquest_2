using IC2.Engine.Armies;
using IC2.Engine.Armies.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Tests.Core;
using Xunit;
using static IC2.Engine.Tests.Armies.ArmiesTestbed;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// <c>docs/tasks/T141.md</c> Done-when 2–4: a split carries money bounded by the receiver's purse cap, and
/// the parent's and new army's supply are rebalanced by the original's <c>TArmyToArmy_OK</c> steps after
/// the units, supply and money are moved — A's excess down to its <c>troops div 100</c> capacity, then
/// B's excess (including what step 1 pushed) back to A.
/// </summary>
public sealed class SplitArmyAllocationTests
{
    /// <summary>
    /// Done-when 2, first half: a 30,000-troop parent (two 15,000-troop units) holding 500 t splits one
    /// unit off with <c>supply=0</c>. <c>capA = 150</c> pushes 350 t to B; <c>capB = 150</c> sends 200 t
    /// back, so the parent ends at 350 t and the new army at 150 t, conserved exactly.
    /// </summary>
    [Fact]
    public void Rebalance_AParentHoldingFiveHundredTons_SplitsToThreeHundredFiftyAndOneFifty()
    {
        var parent = Army("rebalance-a", NorthNationId, 3, 3,
            new[] { RegularUnit("a", troops: 15_000), RegularUnit("b", troops: 15_000) }, supplyTons: 500);
        var state = WithArmies(InitialState(), parent);

        var result = Dispatcher().Dispatch(
            state,
            new SplitArmyCommand(
                NorthNationId, "rebalance-a", "rebalance-a-new", ValueList.Of(1), SupplyTonsToNewArmy: 0));

        Assert.True(result.IsAccepted, result.ToString());
        var remaining = result.State.ArmyById("rebalance-a")!;
        var created = result.State.ArmyById("rebalance-a-new")!;
        Assert.Equal(350, remaining.SupplyTons);
        Assert.Equal(150, created.SupplyTons);
        Assert.Equal(500, remaining.SupplyTons + created.SupplyTons); // conserved exactly.
    }

    /// <summary>
    /// Done-when 2, second half: the same split with <c>supply=50</c> from a parent holding only 150 t
    /// leaves both sides under capacity, so the rebalance is a no-op — the parent keeps 100 t and the new
    /// army holds 50 t, conserved exactly.
    /// </summary>
    [Fact]
    public void Rebalance_AnAllocationWithinBothCapacities_IsLeftAsMoved()
    {
        var parent = Army("rebalance-b", NorthNationId, 3, 3,
            new[] { RegularUnit("a", troops: 15_000), RegularUnit("b", troops: 15_000) }, supplyTons: 150);
        var state = WithArmies(InitialState(), parent);

        var result = Dispatcher().Dispatch(
            state,
            new SplitArmyCommand(
                NorthNationId, "rebalance-b", "rebalance-b-new", ValueList.Of(1), SupplyTonsToNewArmy: 50));

        Assert.True(result.IsAccepted, result.ToString());
        var remaining = result.State.ArmyById("rebalance-b")!;
        var created = result.State.ArmyById("rebalance-b-new")!;
        Assert.Equal(100, remaining.SupplyTons);
        Assert.Equal(50, created.SupplyTons);
        Assert.Equal(150, remaining.SupplyTons + created.SupplyTons);
    }

    /// <summary>
    /// Done-when 3: the real published split (<c>pending-offer-block-army-split-and-naupactus.md</c>) —
    /// 75,536 troops across 16 units and 256 talents becomes a 9-unit/37,081-troop parent with 156 talents
    /// and a 7-unit/38,455-troop new army with 100 talents, in one command that names seven indexes and
    /// <c>money=100</c>. The new army stands one tile away, as <see cref="SplitPlacement"/> puts it.
    /// </summary>
    [Fact]
    public void TheObservedSplit_MovesSevenUnitsAndOneHundredTalentsInOneCommand()
    {
        var parent = BuildSixteenUnitArmy();
        var state = WithArmies(InitialState(), parent);

        var movedIndices = ValueList.Of(9, 10, 11, 12, 13, 14, 15);
        var result = Dispatcher().Dispatch(
            state,
            new SplitArmyCommand(NorthNationId, "split-parent", "split-new", movedIndices, MoneyToNewArmy: 100));

        Assert.True(result.IsAccepted, result.ToString());
        var remaining = result.State.ArmyById("split-parent")!;
        var created = result.State.ArmyById("split-new")!;

        Assert.Equal(9, remaining.Units.Count);
        Assert.Equal(37_081, remaining.TotalTroops);
        Assert.Equal(156, remaining.Money);
        Assert.Equal(7, created.Units.Count);
        Assert.Equal(38_455, created.TotalTroops);
        Assert.Equal(100, created.Money);

        // Troops and money conserve exactly across the two resulting armies.
        Assert.Equal(75_536, remaining.TotalTroops + created.TotalTroops);
        Assert.Equal(256, remaining.Money + created.Money);

        // The new army stands one tile from the parent, not on it (T114's SplitPlacement).
        var expectedCell = SplitPlacement.ArmyCell(state, CoreTestbed.Toy.World, new GridPoint(parent.X, parent.Y));
        Assert.NotNull(expectedCell);
        Assert.Equal(expectedCell!.Value.X, created.X);
        Assert.Equal(expectedCell.Value.Y, created.Y);
        Assert.NotEqual(new GridPoint(parent.X, parent.Y), new GridPoint(created.X, created.Y));
    }

    /// <summary>
    /// Done-when 4: the purse bound is the ruleset's <c>economy.purseCapPerUnit</c> (the dialog's money
    /// stepper, the same bound T117 kept for the transfer). A parent holding 1,200 talents lends exactly
    /// the cap and is refused one talent above it, with the state left unchanged.
    /// </summary>
    [Fact]
    public void Money_AtTheRulesetPurseCap_IsAcceptedAndOneAboveIt_IsRefused()
    {
        var cap = ArmiesTestbed.Ruleset.Economy.PurseCapPerUnit;
        Assert.Equal(1000, cap); // the value the entry names; read from the ruleset, not a literal.
        var parent = Army("purse-cap", NorthNationId, 3, 3,
            new[] { RegularUnit("a"), RegularUnit("b") }, money: 1200);
        var state = WithArmies(InitialState(), parent);

        var accepted = Dispatcher().Dispatch(
            state,
            new SplitArmyCommand(
                NorthNationId, "purse-cap", "purse-cap-new", ValueList.Of(1), MoneyToNewArmy: cap));

        Assert.True(accepted.IsAccepted, accepted.ToString());
        Assert.Equal(cap, accepted.State.ArmyById("purse-cap-new")!.Money);
        Assert.Equal(1200 - cap, accepted.State.ArmyById("purse-cap")!.Money);

        var refused = Dispatcher().Dispatch(
            state,
            new SplitArmyCommand(
                NorthNationId, "purse-cap", "purse-cap-new", ValueList.Of(1), MoneyToNewArmy: cap + 1));

        Assert.True(refused.IsRejected);
        Assert.Equal(SplitArmyRejections.PurseCapExceeded, refused.Code);
        Assert.Equal("armies.split-army-purse-cap-exceeded", refused.Rejection!.Code.Value);
        Assert.Same(state, refused.State); // unchanged, because nothing was written.
    }

    /// <summary>
    /// The exact published split's fixture, reconstructed here because the original lives private in
    /// <see cref="SplitArmyCommandHandlerTests"/>: nine kept units sum to 37,081 troops and seven moving
    /// units to 38,455, over 16 units and 75,536 troops.
    /// </summary>
    private static ArmyState BuildSixteenUnitArmy()
    {
        var kept = Enumerable.Range(0, 8).Select(i => RegularUnit($"kept{i}", troops: 4000))
            .Append(RegularUnit("kept8", troops: 5081))
            .ToList();
        var moved = Enumerable.Range(0, 6).Select(i => RegularUnit($"moved{i}", troops: 5000))
            .Append(RegularUnit("moved6", troops: 8455))
            .ToList();

        var allUnits = kept.Concat(moved).ToList();
        Assert.Equal(75_536, allUnits.Sum(u => u.Troops));
        Assert.Equal(16, allUnits.Count);

        return Army("split-parent", NorthNationId, 3, 3, allUnits, moves: 5, morale: 66, money: 256);
    }
}
