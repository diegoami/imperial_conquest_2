using IC2.Engine.Armies.Commands;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using Xunit;
using static IC2.Engine.Tests.Armies.ArmiesTestbed;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// <c>docs/tasks/T106.md</c> "Army-to-army transfer of units, supply and money", corrected by
/// <c>docs/tasks/T117.md</c> to the original's one dialog <c>OK</c>: one command carries both directions
/// and commits them at once, then <c>TArmyToArmy_OK</c> rebalances supply and merges an emptied army
/// uncapped. Every T106 boundary still stands: distance 0, distance 2 and a foreign army are refused with
/// the whole state untouched; and each limit — 20 units, 100,000 troops and the purse cap — accepts at the
/// boundary and refuses one past it. The two supply-capacity tests and the merge's purse-spill test are
/// replaced by the rebalancing tests T117 requires.
/// </summary>
public sealed class ArmyTransferCommandTests
{
    private static IEnumerable<UnitSlot> Units(int count, int troops = 1000, string prefix = "u") =>
        Enumerable.Range(0, count).Select(i => RegularUnit($"{prefix}{i}", troops: troops));

    /// <summary>
    /// Troops and supply summed over every army, and money summed over every army's purse <em>and</em> every
    /// nation's treasury. The treasury is where T106's emptied-army branch credited a pooled purse's excess;
    /// T117's uncapped merge never does, so a test that still finds the treasury moved catches a regression
    /// to the capped spill.
    /// </summary>
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

        foreach (var nation in state.Nations)
        {
            money += nation.Treasury;
        }

        return (troops, supply, money);
    }

    private static ArmyTransferCommand OneWay(
        string from, string to, ValueList<int> units, int supply, int money) =>
        new(NorthNationId, from, to, units, supply, money, ValueList<int>.Empty, 0, 0);

    private static ArmyTransferCommand TwoWay(
        string from, string to,
        ValueList<int> units, int supply, int money,
        ValueList<int> backUnits, int backSupply, int backMoney) =>
        new(NorthNationId, from, to, units, supply, money, backUnits, backSupply, backMoney);

    [Fact]
    public void Transfer_MovesOneUnitSupplyAndMoney_AndConservesAllThree()
    {
        var moving = RegularUnit("rome-1st", troops: 1500);
        var state = WithArmies(
            InitialState(),
            Army("rome-a", NorthNationId, 5, 5, new[] { moving, RegularUnit("rome-keep") }, money: 100, supplyTons: 50),
            Army("rome-b", NorthNationId, 6, 5, new[] { RegularUnit("rome-b-1", troops: 5000) }, money: 30, supplyTons: 0));
        var before = Totals(state);

        var result = Dispatcher().Dispatch(state, OneWay("rome-a", "rome-b", ValueList.Of(0), 10, 20));

        Assert.True(result.IsAccepted, result.ToString());

        var source = result.State.ArmyById("rome-a")!;
        var target = result.State.ArmyById("rome-b")!;
        Assert.Contains(target.Units, u => string.Equals(u.Name, "rome-1st", StringComparison.Ordinal));
        Assert.DoesNotContain(source.Units, u => string.Equals(u.Name, "rome-1st", StringComparison.Ordinal));
        Assert.Equal(1500, target.Units.Single(u => u.Name == "rome-1st").Troops);
        Assert.Equal(80, source.Money);
        Assert.Equal(50, target.Money);

        // The move leaves rome-a at 1,000 troops (capA 10) and 40 tons, so OK's step 1 pushes its 30-ton
        // excess to rome-b, which ends at 40 (within its capB of 65).
        Assert.Equal(10, source.SupplyTons);
        Assert.Equal(40, target.SupplyTons);

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

        var result = Dispatcher().Dispatch(state, OneWay("rome-a", "rome-b", ValueList.Of(0), 0, 0));

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

        var result = Dispatcher().Dispatch(state, OneWay("rome-a", "rome-b", ValueList.Of(0), 0, 0));

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

        var result = Dispatcher().Dispatch(state, OneWay("rome-a", "rome-b", ValueList.Of(0), 0, 0));

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

        var result = Dispatcher().Dispatch(state, OneWay("rome-a", "rome-b", ValueList.Of(0), 0, 0));

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

        var result = Dispatcher().Dispatch(state, OneWay("cap-b", "cap-a", ValueList.Of(0), 0, 0));

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

        var result = Dispatcher().Dispatch(state, OneWay("cap-d", "cap-c", ValueList.Of(0), 0, 0));

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

        var result = Dispatcher().Dispatch(state, OneWay("troop-b", "troop-a", ValueList.Of(0), 0, 0));

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

        var result = Dispatcher().Dispatch(state, OneWay("troop-d", "troop-c", ValueList.Of(0), 0, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.CombinedTroopsTooLarge, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_SupplyExactlyAtTheReceiverCapacity_IsAcceptedAndRebalanced()
    {
        // T117 replaces T106's SupplyExceedsCapacity refusal: the receiver's dialog capacity is its
        // general capacity plus the dialog's +1, and OK refuses nothing -- the rebalance places the ton,
        // leaving the receiver at its general capacity (troops div 100) and the giver with the rest.
        const int receiverTroops = 10_000;
        const int movedTroops = 500;
        var capacity = SupplyCapacity.ArmyDialogCapacityTons(receiverTroops + movedTroops, ArmiesTestbed.Ruleset);
        var generalCapacity = SupplyCapacity.ArmyCapacityTons(receiverTroops + movedTroops, ArmiesTestbed.Ruleset);
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
        var before = Totals(state);

        var result = Dispatcher().Dispatch(state, OneWay("sup-b", "sup-a", ValueList.Of(0), 1, 0));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(generalCapacity, result.State.ArmyById("sup-a")!.SupplyTons);
        Assert.Equal(5, result.State.ArmyById("sup-b")!.SupplyTons);
        Assert.Equal(before.Supply, Totals(result.State).Supply);
    }

    [Fact]
    public void Transfer_MoreSupplyThanTheReceiverDialogCapacity_IsAcceptedAndRebalanced()
    {
        // Done-when 7: a transfer of more supply than the receiver's troops div 100 + 1 is accepted, and
        // the rebalance places it. 200 tons against a dialog room of 106.
        const int receiverTroops = 10_000;
        const int movedTroops = 500;
        var generalCapacity = SupplyCapacity.ArmyCapacityTons(receiverTroops + movedTroops, ArmiesTestbed.Ruleset);
        var state = WithArmies(
            InitialState(),
            Army("no-refuse-a", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: receiverTroops) }, supplyTons: 0),
            Army(
                "no-refuse-b", NorthNationId, 6, 5,
                new[] { RegularUnit("b", troops: movedTroops), RegularUnit("b2", troops: 100) },
                supplyTons: 1000));
        var before = Totals(state);

        var result = Dispatcher().Dispatch(state, OneWay("no-refuse-b", "no-refuse-a", ValueList.Of(0), 200, 0));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(generalCapacity, result.State.ArmyById("no-refuse-a")!.SupplyTons);
        Assert.Equal(before.Supply, Totals(result.State).Supply);
    }

    [Fact]
    public void Transfer_MoneyExactlyAtThePurseCap_IsAccepted()
    {
        var cap = ArmiesTestbed.Ruleset.Economy.PurseCapPerUnit;
        var state = WithArmies(
            InitialState(),
            Army("pur-a", NorthNationId, 5, 5, Units(1), money: cap - 1),
            Army("pur-b", NorthNationId, 6, 5, Units(1, prefix: "v"), money: 10));

        var result = Dispatcher().Dispatch(state, OneWay("pur-b", "pur-a", ValueList<int>.Empty, 0, 1));

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

        var result = Dispatcher().Dispatch(state, OneWay("pur-d", "pur-c", ValueList<int>.Empty, 0, 1));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.PurseCapExceeded, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_BackMoneyExactlyAtThePurseCap_IsAccepted()
    {
        var cap = ArmiesTestbed.Ruleset.Economy.PurseCapPerUnit;
        var state = WithArmies(
            InitialState(),
            Army("bq-a", NorthNationId, 5, 5, Units(1), money: cap - 1),
            Army("bq-b", NorthNationId, 6, 5, Units(1, prefix: "v"), money: 10));

        var result = Dispatcher().Dispatch(state, TwoWay("bq-a", "bq-b", ValueList<int>.Empty, 0, 0, ValueList<int>.Empty, 0, 1));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(cap, result.State.ArmyById("bq-a")!.Money);
    }

    [Fact]
    public void Transfer_BackMoneyOnePastThePurseCap_IsRejected()
    {
        var cap = ArmiesTestbed.Ruleset.Economy.PurseCapPerUnit;
        var state = WithArmies(
            InitialState(),
            Army("bp-a", NorthNationId, 5, 5, Units(1), money: cap),
            Army("bp-b", NorthNationId, 6, 5, Units(1, prefix: "v"), money: 10));

        var result = Dispatcher().Dispatch(state, TwoWay("bp-a", "bp-b", ValueList<int>.Empty, 0, 0, ValueList<int>.Empty, 0, 1));

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

        var result = Dispatcher().Dispatch(state, OneWay("empty-a", "empty-b", ValueList.Of(0), 5, 10));

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
    public void Transfer_ThatEmptiesOneArmy_KeepsTheWholePooledSupplyAndPurseUncapped()
    {
        // Done-when 8, replacing T106's SpillsTheExcessPurseToTheTreasury: TArmyToArmy_OK's merge
        // (FUN_0044ab90) adds a pooled purse with no cap and a pooled supply uncapped, leaving the
        // treasury untouched.
        var cap = ArmiesTestbed.Ruleset.Economy.PurseCapPerUnit;
        var state = WithArmies(
            InitialState(),
            Army("uncapped-a", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 700) }, money: cap, supplyTons: 50),
            Army("uncapped-b", NorthNationId, 6, 5, new[] { RegularUnit("b", troops: 800) }, money: cap, supplyTons: 50));
        var treasuryBefore = state.NationById(NorthNationId)!.Treasury;
        var before = Totals(state);

        var result = Dispatcher().Dispatch(state, OneWay("uncapped-a", "uncapped-b", ValueList.Of(0), 0, 0));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Null(result.State.ArmyById("uncapped-a"));

        var survivor = result.State.ArmyById("uncapped-b")!;
        Assert.Equal(1500, survivor.TotalTroops);
        Assert.Equal(2 * cap, survivor.Money);
        Assert.Equal(100, survivor.SupplyTons);
        Assert.Equal(treasuryBefore, result.State.NationById(NorthNationId)!.Treasury);

        var after = Totals(result.State);
        Assert.Equal(before.Supply, after.Supply);
        Assert.Equal(before.Money, after.Money);
    }

    [Fact]
    public void Transfer_ThatEmptiesAnEmbarkedArmy_ClearsTheCarryingFleetsBackReference()
    {
        // Deletion sweep (build-process.md §4.2 gate 5): the original has no observed rule for an army
        // aboard a fleet, so the transfer is governed only by the distance rule; but deleting the army
        // must not leave the fleet's own CarriedArmyId pointing at nothing.
        var carrier = new FleetState(
            "carrier", NorthNationId, 5, 5, Moves: 5, Ships: 10, ConditionPercent: 100,
            Money: 0, SupplyTons: 0, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: "boarding", CoveredTileCode: 5);
        var state = WithArmies(
            InitialState(),
            Army("boarding", NorthNationId, 5, 5, Units(1, troops: 700), coveredTileCode: null, aboardFleetId: "carrier"),
            Army("landing", NorthNationId, 6, 5, Units(1, troops: 800, prefix: "v"))) with
        {
            Fleets = ValueList.Of(carrier),
        };

        var result = Dispatcher().Dispatch(state, OneWay("boarding", "landing", ValueList.Of(0), 0, 0));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Null(result.State.ArmyById("boarding"));
        Assert.Null(result.State.FleetById("carrier")!.CarriedArmyId);
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

        var result = Dispatcher().Dispatch(state, OneWay("side-a", "side-b", ValueList.Of(0), 0, 0));

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

        var result = Dispatcher().Dispatch(state, OneWay("idx-a", "idx-b", ValueList.Of(5), 0, 0));

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

        var result = Dispatcher().Dispatch(state, OneWay("short-a", "short-b", ValueList.Of(0), 4, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.InsufficientSupply, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_MoreBackSupplyThanThePartnerHolds_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(
            InitialState(),
            Army("bs-a", NorthNationId, 5, 5, Units(2), supplyTons: 500),
            Army("bs-b", NorthNationId, 6, 5, Units(1, prefix: "v"), supplyTons: 10));

        var result = Dispatcher().Dispatch(state, TwoWay("bs-a", "bs-b", ValueList<int>.Empty, 0, 0, ValueList<int>.Empty, 11, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.InsufficientSupply, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_MoreMoneyThanTheSourceHolds_IsRejectedAndTheStateIsUntouched()
    {
        // The giver can never go negative: `money=` larger than its own purse is refused (the entry's
        // min(otherArmyAmount, ...) rule; review B1, M1).
        var state = WithArmies(
            InitialState(),
            Army("rich-a", NorthNationId, 5, 5, Units(2), money: 30),
            Army("rich-b", NorthNationId, 6, 5, Units(1, prefix: "v"), money: 1));

        var result = Dispatcher().Dispatch(state, OneWay("rich-a", "rich-b", ValueList<int>.Empty, 0, 31));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.InsufficientMoney, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_MoreBackMoneyThanThePartnerHolds_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(
            InitialState(),
            Army("bm-a", NorthNationId, 5, 5, Units(2), money: 0),
            Army("bm-b", NorthNationId, 6, 5, Units(1, prefix: "v"), money: 10));

        var result = Dispatcher().Dispatch(state, TwoWay("bm-a", "bm-b", ValueList<int>.Empty, 0, 0, ValueList<int>.Empty, 0, 11));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.InsufficientMoney, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_NegativeUnitIndex_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(
            InitialState(),
            Army("neg-a", NorthNationId, 5, 5, Units(2)),
            Army("neg-b", NorthNationId, 6, 5, Units(1, prefix: "v")));

        var result = Dispatcher().Dispatch(state, OneWay("neg-a", "neg-b", ValueList.Of(-1), 0, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.UnknownUnitIndex, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_BackNegativeUnitIndex_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(
            InitialState(),
            Army("neg2-a", NorthNationId, 5, 5, Units(2)),
            Army("neg2-b", NorthNationId, 6, 5, Units(1, prefix: "v")));

        var result = Dispatcher().Dispatch(state, TwoWay("neg2-a", "neg2-b", ValueList<int>.Empty, 0, 0, ValueList.Of(-1), 0, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.UnknownUnitIndex, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_NegativeSupply_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(
            InitialState(),
            Army("neg-a", NorthNationId, 5, 5, Units(2), supplyTons: 3),
            Army("neg-b", NorthNationId, 6, 5, Units(1, prefix: "v")));

        var result = Dispatcher().Dispatch(state, OneWay("neg-a", "neg-b", ValueList<int>.Empty, -1, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.InvalidAmount, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_NegativeBackSupply_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(
            InitialState(),
            Army("neg3-a", NorthNationId, 5, 5, Units(2), supplyTons: 3),
            Army("neg3-b", NorthNationId, 6, 5, Units(1, prefix: "v"), supplyTons: 3));

        var result = Dispatcher().Dispatch(state, TwoWay("neg3-a", "neg3-b", ValueList<int>.Empty, 0, 0, ValueList<int>.Empty, -1, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.InvalidAmount, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_NegativeMoney_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(
            InitialState(),
            Army("neg-c", NorthNationId, 5, 5, Units(2), money: 3),
            Army("neg-d", NorthNationId, 6, 5, Units(1, prefix: "v")));

        var result = Dispatcher().Dispatch(state, OneWay("neg-c", "neg-d", ValueList<int>.Empty, 0, -1));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.InvalidAmount, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_NegativeBackMoney_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(
            InitialState(),
            Army("neg4-a", NorthNationId, 5, 5, Units(2), money: 3),
            Army("neg4-b", NorthNationId, 6, 5, Units(1, prefix: "v"), money: 3));

        var result = Dispatcher().Dispatch(state, TwoWay("neg4-a", "neg4-b", ValueList<int>.Empty, 0, 0, ValueList<int>.Empty, 0, -1));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.InvalidAmount, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_DuplicateUnitIndexes_IsRejectedAndTheStateIsUntouched()
    {
        // The same index twice is refused, not silently de-duplicated by the handler's HashSet
        // (review B1, M2).
        var state = WithArmies(
            InitialState(),
            Army("dup-a", NorthNationId, 5, 5, Units(2)),
            Army("dup-b", NorthNationId, 6, 5, Units(1, prefix: "v")));

        var result = Dispatcher().Dispatch(state, OneWay("dup-a", "dup-b", ValueList.Of(0, 0), 0, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.DuplicateUnitIndex, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_DuplicateBackUnitIndexes_IsRejectedAndTheStateIsUntouched()
    {
        var state = WithArmies(
            InitialState(),
            Army("dup2-a", NorthNationId, 5, 5, Units(2)),
            Army("dup2-b", NorthNationId, 6, 5, Units(2, prefix: "v")));

        var result = Dispatcher().Dispatch(state, TwoWay("dup2-a", "dup2-b", ValueList<int>.Empty, 0, 0, ValueList.Of(0, 0), 0, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.DuplicateUnitIndex, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_WithNoUnitsSupplyOrMoney_IsRejectedAndTheStateIsUntouched()
    {
        // An empty order moves nothing, so it is refused before any state is touched (review B1, M3).
        var state = WithArmies(
            InitialState(),
            Army("noop-a", NorthNationId, 5, 5, Units(2)),
            Army("noop-b", NorthNationId, 6, 5, Units(1, prefix: "v")));

        var result = Dispatcher().Dispatch(state, TwoWay("noop-a", "noop-b", ValueList<int>.Empty, 0, 0, ValueList<int>.Empty, 0, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.InvalidAmount, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_ToItself_IsRejectedWithTheSameArmyCode()
    {
        // The same id on both sides is its own rejection, not merely `not-adjacent` at distance 0
        // (review N5, M14).
        var state = WithArmies(
            InitialState(),
            Army("self-a", NorthNationId, 5, 5, Units(2)));

        var result = Dispatcher().Dispatch(state, OneWay("self-a", "self-a", ValueList.Of(0), 0, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.SameArmy, result.Code);
        Assert.Same(state, result.State);
    }

    // ---- T117: the original's one dialog OK, both ways, and its supply rebalance ----

    [Fact]
    public void Transfer_MoneyOnly_RebalancesSupplyToTheSelectedArmy()
    {
        // Done-when 1, the report's worked example: 500 tons each, capA = capB = 300; A's excess goes to B,
        // then B's goes back to A, so B ends at capB and A keeps the rest. A money-only order triggers it
        // (the rebalance runs on every accepted transfer).
        var state = WithArmies(
            InitialState(),
            Army("reb-a", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 30_000) }, money: 500, supplyTons: 500),
            Army("reb-b", NorthNationId, 6, 5, new[] { RegularUnit("b", troops: 30_000) }, money: 500, supplyTons: 500));
        var before = Totals(state);

        var result = Dispatcher().Dispatch(state, OneWay("reb-a", "reb-b", ValueList<int>.Empty, 0, 10));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(700, result.State.ArmyById("reb-a")!.SupplyTons);
        Assert.Equal(300, result.State.ArmyById("reb-b")!.SupplyTons);
        Assert.Equal(490, result.State.ArmyById("reb-a")!.Money);
        Assert.Equal(510, result.State.ArmyById("reb-b")!.Money);

        var after = Totals(result.State);
        Assert.Equal(before.Supply, after.Supply);
        Assert.Equal(before.Money, after.Money);
    }

    [Fact]
    public void Transfer_MoneyOnly_SwappedRoles_RebalancesToTheSelectedArmy()
    {
        // Done-when 1, the swapped half: with B named selected, B holds the remainder and A ends at capA.
        var state = WithArmies(
            InitialState(),
            Army("swap-a", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 30_000) }, money: 500, supplyTons: 500),
            Army("swap-b", NorthNationId, 6, 5, new[] { RegularUnit("b", troops: 30_000) }, money: 500, supplyTons: 500));

        var result = Dispatcher().Dispatch(state, OneWay("swap-b", "swap-a", ValueList<int>.Empty, 0, 10));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(700, result.State.ArmyById("swap-b")!.SupplyTons);
        Assert.Equal(300, result.State.ArmyById("swap-a")!.SupplyTons);
    }

    [Fact]
    public void Transfer_TwoWayOrder_ExchangesOneUnitAndSupply_TheReviewR4Example()
    {
        // Done-when 2 (PR 632's review, R4): A 30,000 troops and 500 tons; B 70,000, among them a
        // 10,000-troop unit, and no supply. One order sends 450 tons A -> B and the 10,000-troop unit
        // B -> A; both end within capacity (400 and 600), so the rebalance moves nothing.
        var state = WithArmies(
            InitialState(),
            Army("two-a", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 30_000) }, supplyTons: 500),
            Army("two-b", NorthNationId, 6, 5, new[]
            {
                RegularUnit("b-big", troops: 60_000),
                RegularUnit("b-small", troops: 10_000),
            }, supplyTons: 0));

        var result = Dispatcher().Dispatch(state, TwoWay("two-a", "two-b", ValueList<int>.Empty, 450, 0, ValueList.Of(1), 0, 0));

        Assert.True(result.IsAccepted, result.ToString());
        var selected = result.State.ArmyById("two-a")!;
        var partner = result.State.ArmyById("two-b")!;
        Assert.Equal(40_000, selected.TotalTroops);
        Assert.Equal(60_000, partner.TotalTroops);
        Assert.Equal(2, selected.Units.Count);
        Assert.Single(partner.Units);
        Assert.Equal(50, selected.SupplyTons);
        Assert.Equal(450, partner.SupplyTons);
    }

    [Fact]
    public void Transfer_TheReviewR4Example_AsTwoOneWayOrders_IsRefusedForSupply()
    {
        // Done-when 2's pin: submitted as two one-way commands, unit first, the supply command is refused
        // -- the single two-way order is not.
        var sourceState = WithArmies(
            InitialState(),
            Army("r4-a", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 30_000) }, supplyTons: 500),
            Army("r4-b", NorthNationId, 6, 5, new[]
            {
                RegularUnit("b-big", troops: 60_000),
                RegularUnit("b-small", troops: 10_000),
            }, supplyTons: 0));

        var first = Dispatcher().Dispatch(sourceState, OneWay("r4-b", "r4-a", ValueList.Of(1), 0, 0));
        Assert.True(first.IsAccepted, first.ToString());
        Assert.Equal(400, first.State.ArmyById("r4-a")!.SupplyTons);

        var second = Dispatcher().Dispatch(first.State, OneWay("r4-a", "r4-b", ValueList<int>.Empty, 450, 0));
        Assert.True(second.IsRejected);
        Assert.Equal(ArmyTransferRejections.InsufficientSupply, second.Code);
    }

    [Fact]
    public void Transfer_BackUnitsThatEmptiesThePartner_PoolsItsSupplyAndMoneyUncapped()
    {
        // Done-when 3: a back-units order that moves all of B's units into A leaves A with all of B's
        // supply and money, uncapped, and deletes B.
        var state = WithArmies(
            InitialState(),
            Army("pool-a", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 30_000) }, money: 700, supplyTons: 100),
            Army("pool-b", NorthNationId, 6, 5, new[] { RegularUnit("b1", troops: 40_000), RegularUnit("b2", troops: 30_000) }, money: 800, supplyTons: 50));
        var treasuryBefore = state.NationById(NorthNationId)!.Treasury;
        var before = Totals(state);

        var result = Dispatcher().Dispatch(state, TwoWay("pool-a", "pool-b", ValueList<int>.Empty, 0, 0, ValueList.Of(0, 1), 0, 0));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Null(result.State.ArmyById("pool-b"));

        var survivor = result.State.ArmyById("pool-a")!;
        Assert.Equal(100_000, survivor.TotalTroops);
        Assert.Equal(1500, survivor.Money);
        Assert.Equal(150, survivor.SupplyTons);
        Assert.Equal(treasuryBefore, result.State.NationById(NorthNationId)!.Treasury);

        var after = Totals(result.State);
        Assert.Equal(before.Troops, after.Troops);
        Assert.Equal(before.Supply, after.Supply);
        Assert.Equal(before.Money, after.Money);
    }

    [Fact]
    public void Transfer_TwoWayOrder_FailingOneDirection_IsRefusedWhole()
    {
        // Done-when 3: a two-way order in which one direction fails a check is refused, and neither
        // direction is applied -- a dialog never commits in part.
        var state = WithArmies(
            InitialState(),
            Army("ref-a", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 30_000) }, supplyTons: 500),
            Army("ref-b", NorthNationId, 6, 5, new[] { RegularUnit("b", troops: 30_000) }, supplyTons: 100));

        var result = Dispatcher().Dispatch(state, TwoWay("ref-a", "ref-b", ValueList<int>.Empty, 10, 0, ValueList<int>.Empty, 200, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.InsufficientSupply, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_TwoWayOrder_UnitCapOnTheFinalComposition_IsRefusedWhole()
    {
        // Done-when 3: the 20-unit cap applies to each composition after both directions, so a back-unit
        // that pushes the selected army past 20 refuses the whole order.
        var twentyUnits = Enumerable.Range(0, 20).Select(i => RegularUnit($"full{i}", troops: 10)).ToArray();
        var state = WithArmies(
            InitialState(),
            Army("cap2-a", NorthNationId, 5, 5, twentyUnits, money: 1),
            Army("cap2-b", NorthNationId, 6, 5, new[] { RegularUnit("b", troops: 1000) }));

        var result = Dispatcher().Dispatch(state, TwoWay("cap2-a", "cap2-b", ValueList<int>.Empty, 0, 1, ValueList.Of(0), 0, 0));

        Assert.True(result.IsRejected);
        Assert.Equal(ArmyTransferRejections.CombinedUnitsTooLarge, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Transfer_TwoWayUnitExchange_RebalancesOnceNotOncePerDirection()
    {
        // Done-when 4 (PR 641's review, R8): 30,000 troops and 250 tons each, a 10,000-troop unit each
        // way. One OK rebalance after both directions moves nothing, since neither ends over its
        // capacity of 300. A per-direction rebalance, A selected both times, would leave A 200 / B 300.
        var state = WithArmies(
            InitialState(),
            Army("once-a", NorthNationId, 5, 5, new[] { RegularUnit("a-big", troops: 20_000), RegularUnit("a-small", troops: 10_000) }, supplyTons: 250),
            Army("once-b", NorthNationId, 6, 5, new[] { RegularUnit("b-big", troops: 20_000), RegularUnit("b-small", troops: 10_000) }, supplyTons: 250));

        var result = Dispatcher().Dispatch(state, TwoWay("once-a", "once-b", ValueList.Of(1), 0, 0, ValueList.Of(1), 0, 0));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(250, result.State.ArmyById("once-a")!.SupplyTons);
        Assert.Equal(250, result.State.ArmyById("once-b")!.SupplyTons);
    }

    [Fact]
    public void Transfer_ThatLeavesOnlyTheSelectedArmyOverCapacity_PushesExactlyTheExcessToThePartner()
    {
        // Done-when 6: A ends at 20,000 troops (cap 200) with 500 tons; its 300-ton excess goes to B.
        var state = WithArmies(
            InitialState(),
            Army("over-a", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 20_000) }, money: 1, supplyTons: 500),
            Army("over-b", NorthNationId, 6, 5, new[] { RegularUnit("b", troops: 30_000) }, supplyTons: 0));
        var before = Totals(state);

        var result = Dispatcher().Dispatch(state, OneWay("over-a", "over-b", ValueList<int>.Empty, 0, 1));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(200, result.State.ArmyById("over-a")!.SupplyTons);
        Assert.Equal(300, result.State.ArmyById("over-b")!.SupplyTons);
        Assert.Equal(before.Supply, Totals(result.State).Supply);
    }

    [Fact]
    public void Transfer_ThatLeavesOnlyThePartnerOverCapacity_PushesExactlyTheExcessBack()
    {
        // Done-when 6: B ends at 20,000 troops (cap 200) with 500 tons; step 2 pushes its 300-ton excess
        // back to A.
        var state = WithArmies(
            InitialState(),
            Army("over-c", NorthNationId, 5, 5, new[] { RegularUnit("c", troops: 30_000) }, money: 1, supplyTons: 0),
            Army("over-d", NorthNationId, 6, 5, new[] { RegularUnit("d", troops: 20_000) }, supplyTons: 500));
        var before = Totals(state);

        var result = Dispatcher().Dispatch(state, OneWay("over-c", "over-d", ValueList<int>.Empty, 0, 1));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(300, result.State.ArmyById("over-c")!.SupplyTons);
        Assert.Equal(200, result.State.ArmyById("over-d")!.SupplyTons);
        Assert.Equal(before.Supply, Totals(result.State).Supply);
    }

    [Fact]
    public void Transfer_ThatLeavesBothWithinCapacity_MovesNothingExtra()
    {
        // Done-when 6: both at 30,000 troops (cap 300) and 100 tons, so the rebalance moves nothing.
        var state = WithArmies(
            InitialState(),
            Army("within-a", NorthNationId, 5, 5, new[] { RegularUnit("a", troops: 30_000) }, money: 1, supplyTons: 100),
            Army("within-b", NorthNationId, 6, 5, new[] { RegularUnit("b", troops: 30_000) }, supplyTons: 100));
        var before = Totals(state);

        var result = Dispatcher().Dispatch(state, OneWay("within-a", "within-b", ValueList<int>.Empty, 0, 1));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(100, result.State.ArmyById("within-a")!.SupplyTons);
        Assert.Equal(100, result.State.ArmyById("within-b")!.SupplyTons);
        Assert.Equal(before.Supply, Totals(result.State).Supply);
    }
}
