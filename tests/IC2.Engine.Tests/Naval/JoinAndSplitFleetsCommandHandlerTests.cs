using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval.Commands;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T14 Naval", Done-when 6: join caps at 100 combined ships; split
/// requires at least 20 ships.
/// </summary>
public sealed class JoinAndSplitFleetsCommandHandlerTests
{
    private const string NationId = "north";

    private static FleetState Fleet(string id, int x, int y, int ships, int supply = 0, int money = 0) =>
        new(id, NationId, x, y, Moves: 5, ships, ConditionPercent: 90, money, supply,
            ConstructionTicksRemaining: null, BuildCityId: null, CarriedArmyId: null, CoveredTileCode: null);

    [Fact]
    public void Join_CombinedShipsAtExactlyOneHundred_IsAccepted()
    {
        var state = NavalTestbed.InitialState();
        var a = Fleet("join-a", 3, 3, 60, supply: 10, money: 20);
        var b = Fleet("join-b", 3, 3, 40, supply: 5, money: 8);
        state = state with { Fleets = ValueList.Of(a, b) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new JoinFleetsCommand(NationId, a.Id, b.Id));

        Assert.True(result.IsAccepted, result.ToString());
        var survivor = result.State.FleetById(a.Id)!;
        Assert.Equal(100, survivor.Ships);
        Assert.Equal(15, survivor.SupplyTons);
        Assert.Equal(28, survivor.Money);
        Assert.Equal(0, survivor.Moves);
        Assert.Null(result.State.FleetById(b.Id));
    }

    [Fact]
    public void Join_CombinedShipsAboveOneHundred_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var a = Fleet("join-c", 3, 3, 60);
        var b = Fleet("join-d", 3, 3, 41);
        state = state with { Fleets = ValueList.Of(a, b) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new JoinFleetsCommand(NationId, a.Id, b.Id));

        Assert.True(result.IsRejected);
        Assert.Equal(JoinFleetsRejections.CombinedShipsTooLarge, result.Code);
    }

    [Fact]
    public void Split_FleetWithExactlyTwentyShips_IsAccepted()
    {
        var state = NavalTestbed.InitialState();
        var fleet = Fleet("split-a", 3, 3, 20, supply: 8, money: 4);
        state = state with { Fleets = ValueList.Of(fleet) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new SplitFleetCommand(NationId, fleet.Id, "split-a-new", ShipsToNewFleet: 5));

        Assert.True(result.IsAccepted, result.ToString());
        var remaining = result.State.FleetById(fleet.Id)!;
        var created = result.State.FleetById("split-a-new")!;
        Assert.Equal(15, remaining.Ships);
        Assert.Equal(5, created.Ships);
        Assert.Equal(20, remaining.Ships + created.Ships); // ships conserve exactly.
    }

    [Fact]
    public void Split_FleetWithFewerThanTwentyShips_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var fleet = Fleet("split-b", 3, 3, 19);
        state = state with { Fleets = ValueList.Of(fleet) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new SplitFleetCommand(NationId, fleet.Id, "split-b-new", ShipsToNewFleet: 5));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitFleetRejections.TooFewShipsToSplit, result.Code);
    }
}
