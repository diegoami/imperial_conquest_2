using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval.Commands;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T14 Naval" DoD 16: <c>TUnitMap_FleetToFleetTransfer</c>, a reciprocal
/// ships/supplies/money transfer, committed on OK, with a source left at zero ships deleted.
/// </summary>
public sealed class FleetToFleetTransferCommandHandlerTests
{
    private const string NationId = "north";

    private static FleetState Fleet(string id, int ships, int supply, int money) =>
        new(id, NationId, X: 3, Y: 3, Moves: 5, ships, ConditionPercent: 90, money, supply,
            ConstructionTicksRemaining: null, BuildCityId: null, CarriedArmyId: null, CoveredTileCode: null);

    [Fact]
    public void PartialTransfer_MovesShipsSuppliesAndMoneyReciprocally()
    {
        var state = NavalTestbed.InitialState();
        var source = Fleet("transfer-source", ships: 30, supply: 40, money: 20);
        var target = Fleet("transfer-target", ships: 10, supply: 5, money: 3);
        state = state with { Fleets = ValueList.Of(source, target) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new FleetToFleetTransferCommand(NationId, source.Id, target.Id, Ships: 10, SupplyTons: 15, Money: 8));

        Assert.True(result.IsAccepted, result.ToString());
        var updatedSource = result.State.FleetById(source.Id)!;
        var updatedTarget = result.State.FleetById(target.Id)!;

        Assert.Equal(20, updatedSource.Ships);
        Assert.Equal(25, updatedSource.SupplyTons);
        Assert.Equal(12, updatedSource.Money);

        Assert.Equal(20, updatedTarget.Ships);
        Assert.Equal(20, updatedTarget.SupplyTons);
        Assert.Equal(11, updatedTarget.Money);

        // Conservation, exactly.
        Assert.Equal(40, updatedSource.Ships + updatedTarget.Ships);
        Assert.Equal(45, updatedSource.SupplyTons + updatedTarget.SupplyTons);
        Assert.Equal(23, updatedSource.Money + updatedTarget.Money);
    }

    [Fact]
    public void RequestExceedingSourceStock_ClampsRatherThanRejecting()
    {
        var state = NavalTestbed.InitialState();
        var source = Fleet("transfer-source-2", ships: 25, supply: 10, money: 5);
        var target = Fleet("transfer-target-2", ships: 10, supply: 0, money: 0);
        state = state with { Fleets = ValueList.Of(source, target) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new FleetToFleetTransferCommand(NationId, source.Id, target.Id, Ships: 0, SupplyTons: 999, Money: 999));

        Assert.True(result.IsAccepted, result.ToString());
        var updatedTarget = result.State.FleetById(target.Id)!;
        Assert.Equal(10, updatedTarget.SupplyTons); // clamped to the source's own stock, not rejected.
        Assert.Equal(5, updatedTarget.Money);
    }

    [Fact]
    public void TransferringAllShips_DeletesTheSourceFleet()
    {
        var state = NavalTestbed.InitialState();
        var source = Fleet("transfer-source-3", ships: 15, supply: 0, money: 0);
        var target = Fleet("transfer-target-3", ships: 20, supply: 0, money: 0);
        state = state with { Fleets = ValueList.Of(source, target) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new FleetToFleetTransferCommand(NationId, source.Id, target.Id, Ships: 15, SupplyTons: 0, Money: 0));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Null(result.State.FleetById(source.Id));
        Assert.Equal(35, result.State.FleetById(target.Id)!.Ships);
    }

    [Fact]
    public void FleetsNotCoLocated_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var source = Fleet("transfer-source-4", ships: 20, supply: 0, money: 0) with { X = 3, Y = 3 };
        var target = Fleet("transfer-target-4", ships: 10, supply: 0, money: 0) with { X = 4, Y = 4 };
        state = state with { Fleets = ValueList.Of(source, target) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new FleetToFleetTransferCommand(NationId, source.Id, target.Id, Ships: 1, SupplyTons: 0, Money: 0));

        Assert.True(result.IsRejected);
        Assert.Equal(FleetToFleetTransferRejections.NotCoLocated, result.Code);
    }
}
