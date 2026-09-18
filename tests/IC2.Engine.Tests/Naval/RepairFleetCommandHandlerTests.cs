using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval.Commands;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T14 Naval", Done-when 4: repair of N points costs
/// <c>ships × N / 5</c> and zeroes the fleet's moves; refused away from an owned city.
/// </summary>
public sealed class RepairFleetCommandHandlerTests
{
    private static FleetState DamagedFleet(string id, string nation, int x, int y, int ships, int condition, int moves) =>
        new(id, nation, x, y, moves, ships, condition, Money: 100, SupplyTons: 50,
            ConstructionTicksRemaining: null, BuildCityId: null, CarriedArmyId: null, CoveredTileCode: null);

    [Fact]
    public void RepairNearAnOwnedCity_CostsShipsTimesPointsOverFiveAndZeroesMoves()
    {
        var state = NavalTestbed.InitialState();
        var nationId = state.Nations[0].Id; // "north"
        var arx = state.CityById("arx")!; // (2, 1), owned by north

        var fleet = DamagedFleet("repair-test", nationId, arx.X, arx.Y, ships: 20, condition: 70, moves: 5);
        state = state with { Fleets = ValueList.Of(fleet) };

        var treasuryBefore = state.NationById(nationId)!.Treasury;
        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new RepairFleetCommand(nationId, fleet.Id, Points: 10));

        Assert.True(result.IsAccepted, result.ToString());
        var repaired = result.State.FleetById(fleet.Id)!;
        Assert.Equal(80, repaired.ConditionPercent);
        Assert.Equal(0, repaired.Moves);

        var expectedCost = (20 * 10) / NavalTestbed.Ruleset.Naval.RepairCostDivisor; // 40
        Assert.Equal(40, expectedCost);
        Assert.Equal(treasuryBefore - expectedCost, result.State.NationById(nationId)!.Treasury);
    }

    [Fact]
    public void RepairAwayFromAnOwnedCity_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var nationId = state.Nations[0].Id;

        // Far from every city in the toy world.
        var fleet = DamagedFleet("repair-test-2", nationId, x: 0, y: 5, ships: 10, condition: 90, moves: 3);
        state = state with { Fleets = ValueList.Of(fleet) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new RepairFleetCommand(nationId, fleet.Id, Points: 5));

        Assert.True(result.IsRejected);
        Assert.Equal(RepairFleetRejections.NotAtOwnedCity, result.Code);
    }

    [Fact]
    public void RepairRequestBeyondTheConditionCap_ClampsRatherThanRejecting()
    {
        var state = NavalTestbed.InitialState();
        var nationId = state.Nations[0].Id;
        var arx = state.CityById("arx")!;

        var fleet = DamagedFleet("repair-test-3", nationId, arx.X, arx.Y, ships: 10, condition: 95, moves: 2);
        state = state with { Fleets = ValueList.Of(fleet) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new RepairFleetCommand(nationId, fleet.Id, Points: 50));

        Assert.True(result.IsAccepted, result.ToString());
        var repaired = result.State.FleetById(fleet.Id)!;
        Assert.Equal(100, repaired.ConditionPercent); // clamped to the 100% cap, not 145%.
    }
}
