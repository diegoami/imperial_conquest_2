using IC2.Engine.Model;
using IC2.Engine.Naval.Commands;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/tasks/T141.md</c> Done-when 5: <c>split-fleet</c> carries supply and money as well as ships —
/// each allocation moves exactly, is bounded by what the parent holds (<c>InvalidSupplyAllocation</c> /
/// <c>InvalidMoneyAllocation</c>), and defaults to 0, with no capacity rebalance and no purse bound.
/// </summary>
public sealed class SplitFleetAllocationTests
{
    private const string NationId = "north";

    private static FleetState Fleet(string id, int x, int y, int ships, int supply = 0, int money = 0) =>
        new(id, NationId, x, y, Moves: 5, ships, ConditionPercent: 90, money, supply,
            ConstructionTicksRemaining: null, BuildCityId: null, CarriedArmyId: null, CoveredTileCode: null);

    [Fact]
    public void Split_MovesShipsSupplyAndMoneyExactly()
    {
        var state = NavalTestbed.InitialState();
        var fleet = Fleet("split-alloc", 3, 5, 30, supply: 200, money: 300);
        state = state with { Fleets = ValueList.Of(fleet) };

        var result = NavalTestbed.RealEngineDispatcher().Dispatch(
            state,
            new SplitFleetCommand(
                NationId, fleet.Id, "split-alloc-new", ShipsToNewFleet: 10,
                SupplyTonsToNewFleet: 40, MoneyToNewFleet: 30));

        Assert.True(result.IsAccepted, result.ToString());
        var remaining = result.State.FleetById(fleet.Id)!;
        var created = result.State.FleetById("split-alloc-new")!;
        Assert.Equal(20, remaining.Ships);
        Assert.Equal(160, remaining.SupplyTons);
        Assert.Equal(270, remaining.Money);
        Assert.Equal(10, created.Ships);
        Assert.Equal(40, created.SupplyTons);
        Assert.Equal(30, created.Money);

        // Conserved exactly: nothing created, nothing destroyed.
        Assert.Equal(30, remaining.Ships + created.Ships);
        Assert.Equal(200, remaining.SupplyTons + created.SupplyTons);
        Assert.Equal(300, remaining.Money + created.Money);
    }

    [Theory]
    [InlineData(201, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 301)]
    [InlineData(0, -1)]
    public void Split_AnAllocationBeyondTheParent_IsRefusedAndTheStateIsUnchanged(int supply, int money)
    {
        var state = NavalTestbed.InitialState();
        var fleet = Fleet("split-over", 3, 5, 30, supply: 200, money: 300);
        state = state with { Fleets = ValueList.Of(fleet) };

        var result = NavalTestbed.RealEngineDispatcher().Dispatch(
            state,
            new SplitFleetCommand(
                NationId, fleet.Id, "split-over-new", ShipsToNewFleet: 10,
                SupplyTonsToNewFleet: supply, MoneyToNewFleet: money));

        Assert.True(result.IsRejected);
        Assert.Equal(
            supply < 0 || supply > 200 ? SplitFleetRejections.InvalidSupplyAllocation
                : SplitFleetRejections.InvalidMoneyAllocation,
            result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void Split_WithNoAllocation_LeavesBothAtZeroAsToday()
    {
        var state = NavalTestbed.InitialState();
        var fleet = Fleet("split-zero", 3, 5, 30, supply: 200, money: 300);
        state = state with { Fleets = ValueList.Of(fleet) };

        // SupplyTonsToNewFleet / MoneyToNewFleet omitted -- the command's own declared defaults.
        var result = NavalTestbed.RealEngineDispatcher().Dispatch(
            state, new SplitFleetCommand(NationId, fleet.Id, "split-zero-new", ShipsToNewFleet: 10));

        Assert.True(result.IsAccepted, result.ToString());
        var created = result.State.FleetById("split-zero-new")!;
        Assert.Equal(0, created.SupplyTons);
        Assert.Equal(0, created.Money);
        Assert.Equal(200, result.State.FleetById(fleet.Id)!.SupplyTons);
        Assert.Equal(300, result.State.FleetById(fleet.Id)!.Money);
    }
}
