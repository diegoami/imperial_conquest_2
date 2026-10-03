using IC2.Engine.Armies.Commands;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;
using static IC2.Engine.Tests.Armies.ArmiesTestbed;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// <c>docs/tasks/T106.md</c> "Army-to-army transfer of units, supply and money", Done-when 1-3: one
/// command moves the listed units and the given supply and money between two of the issuing nation's own
/// armies exactly one tile apart, conserving troops, supply and money; distance 0, distance 2 and a
/// foreign army are refused with the whole state untouched; and each receiving cap — 20 units, 100,000
/// troops, the dialog supply capacity and the purse cap — accepts at the boundary and refuses one past it.
/// </summary>
public sealed class ArmyTransferCommandTests
{
    private static IEnumerable<UnitSlot> Units(int count, int troops = 1000, string prefix = "u") =>
        Enumerable.Range(0, count).Select(i => RegularUnit($"{prefix}{i}", troops: troops));

    /// <summary>Troops, supply and money summed over every army in <paramref name="state"/>.</summary>
    private static (int Troops, int Supply, int Money) Totals(GameState state)
    {
        var troops = 0;
        var supply = 0;
        var money = 0;
        foreach (var army in state.Armies)
        {
            troops += army.TotalTroops;
            supply += army.SupplyTons;
            money += army.Money;
        }

        return (troops, supply, money);
    }

    [Fact]
    public void Transfer_MovesOneUnitSupplyAndMoney_AndConservesAllThree()
    {
        var moving = RegularUnit("rome-1st", troops: 1500);
        var state = WithArmies(
            InitialState(),
            Army("rome-a", NorthNationId, 5, 5, new[] { moving, RegularUnit("rome-keep") }, money: 100, supplyTons: 50),
            Army("rome-b", NorthNationId, 6, 5, new[] { RegularUnit("rome-b-1", troops: 5000) }, money: 30, supplyTons: 0));
        var before = Totals(state);

        var result = Dispatcher().Dispatch(
            state, new ArmyTransferCommand(NorthNationId, "rome-a", "rome-b", ValueList.Of(0), 10, 20));

        Assert.True(result.IsAccepted, result.ToString());

        var source = result.State.ArmyById("rome-a")!;
        var target = result.State.ArmyById("rome-b")!;
        Assert.Contains(target.Units, u => string.Equals(u.Name, "rome-1st", StringComparison.Ordinal));
        Assert.DoesNotContain(source.Units, u => string.Equals(u.Name, "rome-1st", StringComparison.Ordinal));
        Assert.Equal(1500, target.Units.Single(u => u.Name == "rome-1st").Troops);
        Assert.Equal(80, source.Money);
        Assert.Equal(40, source.SupplyTons);
        Assert.Equal(50, target.Money);
        Assert.Equal(10, target.SupplyTons);

        var after = Totals(result.State);
        Assert.Equal(before.Troops, after.Troops);
        Assert.Equal(before.Supply, after.Supply);
        Assert.Equal(before.Money, after.Money);
    }

    [Fact]
    public void Transfer_AtDistanceZero_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(
            InitialState(),
            Army("rome-a", NorthNationId, 5, 5, Units(1)),
            Army("rome-b", NorthNationId, 5, 5, Units(1, prefix: "v")));

        var result = Dispatcher().Dispatch(
            state, new ArmyTransferCommand(NorthNationId, "rome-a", "rome-b", ValueList.Of(0), 0, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.NotAdjacent, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_AtDistanceTwo_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(
            InitialState(),
            Army("rome-a", NorthNationId, 5, 5, Units(1)),
            Army("rome-b", NorthNationId, 7, 5, Units(1, prefix: "v")));

        var result = Dispatcher().Dispatch(
            state, new ArmyTransferCommand(NorthNationId, "rome-a", "rome-b", ValueList.Of(0), 0, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.NotAdjacent, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_FromAForeignArmy_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(
            InitialState(),
            Army("rome-a", SouthNationId, 5, 5, Units(1)),
            Army("rome-b", NorthNationId, 6, 5, Units(1, prefix: "v")));

        var result = Dispatcher().Dispatch(
            state, new ArmyTransferCommand(NorthNationId, "rome-a", "rome-b", ValueList.Of(0), 0, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.NotYourArmy, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_ToAForeignArmy_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(
            InitialState(),
            Army("rome-a", NorthNationId, 5, 5, Units(1)),
            Army("rome-b", SouthNationId, 6, 5, Units(1, prefix: "v")));

        var result = Dispatcher().Dispatch(
            state, new ArmyTransferCommand(NorthNationId, "rome-a", "rome-b", ValueList.Of(0), 0, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.NotYourArmy, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_TwentyUnitsOnTheReceiver_IsAccepted()
    {
        var state = WithArmies(
            InitialState(),
            Army("cap-a", NorthNationId, 5, 5, Units(19, troops: 10)),
            Army("cap-b", NorthNationId, 6, 5, Units(1, troops: 10, prefix: "v")));

        var result = Dispatcher().Dispatch(
            state, new ArmyTransferCommand(NorthNationId, "cap-b", "cap-a", ValueList.Of(0), 0, 0));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(ArmiesTestbed.Ruleset.ArmyManagement.MaxUnitsPerArmy, result.State.ArmyById("cap-a")!.Units.Count);
    }

    [Fact]
    public void Transfer_TwentyOneUnitsOnTheReceiver_IsRejected()
    {
        var state = WithArmies(
            InitialState(),
            Army("cap-c", NorthNationId, 5, 5, Units(20, troops: 10)),
            Army("cap-d", NorthNationId, 6, 5, Units(1, troops: 10, prefix: "v")));

        var result = Dispatcher().Dispatch(
            state, new ArmyTransferCommand(NorthNationId, "cap-d", "cap-c", ValueList.Of(0), 0, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.CombinedUnitsTooLarge, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_ExactlyOneHundredThousandTroopsOnTheReceiver_IsAccepted()
    {
        var state = WithArmies(
            InitialState(),
            Army("troop-a", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 99_000) }),
            Army("troop-b", NorthNationId, 6, 5, new[] { RegularUnit("b", troops: 1_000) }));

        var result = Dispatcher().Dispatch(
            state, new ArmyTransferCommand(NorthNationId, "troop-b", "troop-a", ValueList.Of(0), 0, 0));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(ArmiesTestbed.Ruleset.ArmyManagement.MaxTroopsPerArmy, result.State.ArmyById("troop-a")!.TotalTroops);
    }

    [Fact]
    public void Transfer_PastOneHundredThousandTroopsOnTheReceiver_IsRejected()
    {
        var state = WithArmies(
            InitialState(),
            Army("troop-c", NorthNationId, 5, 5, new[] { RegularUnit("c", troops: 99_000) }),
            Army("troop-d", NorthNationId, 6, 5, new[] { RegularUnit("d", troops: 1_001) }));

        var result = Dispatcher().Dispatch(
            state, new ArmyTransferCommand(NorthNationId, "troop-d", "troop-c", ValueList.Of(0), 0, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.CombinedTroopsTooLarge, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_SupplyExactlyAtTheReceiverCapacity_IsAccepted()
    {
        // The receiver's capacity follows its post-transfer troops; a second unit stays behind so the
        // source survives and the supply cap -- not the empty-army merge -- is what is under test.
        const int receiverTroops = 10_000;
        const int movedTroops = 500;
        var capacity = SupplyCapacity.ArmyDialogCapacityTons(receiverTroops + movedTroops, ArmiesTestbed.Ruleset);
        var state = WithArmies(
            InitialState(),
            Army(
                "sup-a", NorthNationId, 5, 5,
                new[] { RegularUnit("a", troops: receiverTroops) },
                supplyTons: capacity - 1),
            Army(
                "sup-b", NorthNationId, 6, 5,
                new[] { RegularUnit("b", troops: movedTroops), RegularUnit("b2", troops: 100) },
                supplyTons: 5));

        var result = Dispatcher().Dispatch(
            state, new ArmyTransferCommand(NorthNationId, "sup-b", "sup-a", ValueList.Of(0), 1, 0));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(capacity, result.State.ArmyById("sup-a")!.SupplyTons);
    }

    [Fact]
    public void Transfer_SupplyOnePastTheReceiverCapacity_IsRejected()
    {
        const int receiverTroops = 10_000;
        const int movedTroops = 500;
        var capacity = SupplyCapacity.ArmyDialogCapacityTons(receiverTroops + movedTroops, ArmiesTestbed.Ruleset);
        var state = WithArmies(
            InitialState(),
            Army(
                "sup-c", NorthNationId, 5, 5,
                new[] { RegularUnit("c", troops: receiverTroops) },
                supplyTons: capacity),
            Army(
                "sup-d", NorthNationId, 6, 5,
                new[] { RegularUnit("d", troops: movedTroops), RegularUnit("d2", troops: 100) },
                supplyTons: 5));

        var result = Dispatcher().Dispatch(
            state, new ArmyTransferCommand(NorthNationId, "sup-d", "sup-c", ValueList.Of(0), 1, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.SupplyExceedsCapacity, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_MoneyExactlyAtThePurseCap_IsAccepted()
    {
        var cap = ArmiesTestbed.Ruleset.Economy.PurseCapPerUnit;
        var state = WithArmies(
            InitialState(),
            Army("pur-a", NorthNationId, 5, 5, Units(1), money: cap - 1),
            Army("pur-b", NorthNationId, 6, 5, Units(1, prefix: "v"), money: 10));

        var result = Dispatcher().Dispatch(
            state, new ArmyTransferCommand(NorthNationId, "pur-b", "pur-a", ValueList<int>.Empty, 0, 1));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(cap, result.State.ArmyById("pur-a")!.Money);
    }

    [Fact]
    public void Transfer_MoneyOnePastThePurseCap_IsRejected()
    {
        var cap = ArmiesTestbed.Ruleset.Economy.PurseCapPerUnit;
        var state = WithArmies(
            InitialState(),
            Army("pur-c", NorthNationId, 5, 5, Units(1), money: cap),
            Army("pur-d", NorthNationId, 6, 5, Units(1, prefix: "v"), money: 10));

        var result = Dispatcher().Dispatch(
            state, new ArmyTransferCommand(NorthNationId, "pur-d", "pur-c", ValueList<int>.Empty, 0, 1));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.PurseCapExceeded, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_ThatEmptiesOneArmy_LeavesOneArmyHoldingAllTroopsSupplyAndMoney()
    {
        var state = WithArmies(
            InitialState(),
            Army("empty-a", NorthNationId, 5, 5, Units(1, troops: 700), money: 40, supplyTons: 12),
            Army("empty-b", NorthNationId, 6, 5, new[] { RegularUnit("keep", troops: 800) }, money: 30, supplyTons: 8));
        var before = Totals(state);

        var result = Dispatcher().Dispatch(
            state, new ArmyTransferCommand(NorthNationId, "empty-a", "empty-b", ValueList.Of(0), 5, 10));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Null(result.State.ArmyById("empty-a"));

        var survivor = result.State.ArmyById("empty-b")!;
        Assert.Equal(1500, survivor.TotalTroops);
        Assert.Equal(70, survivor.Money);
        Assert.Equal(20, survivor.SupplyTons);

        var after = Totals(result.State);
        Assert.Equal(before.Troops, after.Troops);
        Assert.Equal(before.Supply, after.Supply);
        Assert.Equal(before.Money, after.Money);
    }

    [Fact]
    public void Transfer_AThirdUninvolvedArmy_IsUntouched()
    {
        var bystander = Army("bystander", NorthNationId, 9, 9, Units(3, prefix: "b"), money: 77, supplyTons: 11);
        var state = WithArmies(
            InitialState(),
            Army("side-a", NorthNationId, 5, 5, Units(2)),
            Army("side-b", NorthNationId, 6, 5, Units(1, prefix: "v")),
            bystander);

        var result = Dispatcher().Dispatch(
            state, new ArmyTransferCommand(NorthNationId, "side-a", "side-b", ValueList.Of(0), 0, 0));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(bystander, result.State.ArmyById("bystander"));
    }

    [Fact]
    public void Transfer_AnUnknownUnitIndex_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(
            InitialState(),
            Army("idx-a", NorthNationId, 5, 5, Units(1)),
            Army("idx-b", NorthNationId, 6, 5, Units(1, prefix: "v")));

        var result = Dispatcher().Dispatch(
            state, new ArmyTransferCommand(NorthNationId, "idx-a", "idx-b", ValueList.Of(5), 0, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.UnknownUnitIndex, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_MoreSupplyThanTheSourceHolds_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(
            InitialState(),
            Army("short-a", NorthNationId, 5, 5, Units(2), supplyTons: 3),
            Army("short-b", NorthNationId, 6, 5, Units(1, prefix: "v"), supplyTons: 1));

        var result = Dispatcher().Dispatch(
            state, new ArmyTransferCommand(NorthNationId, "short-a", "short-b", ValueList.Of(0), 4, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.InsufficientSupply, result.Code);
        Assert.Same(state, result.State);
    }
}
