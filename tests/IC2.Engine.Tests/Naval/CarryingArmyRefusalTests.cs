using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval.Commands;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T14 Naval", Done-when 5: "A fleet carrying an army refuses repair,
/// scuttle, split and join — four separate assertions." <c>decompiled-unit-map-orders-and-record-fields.md</c>
/// confirms each refusal independently (<c>TUnitMap_RepairFlt</c>, <c>TUnitMap_ScuttleFleet</c>,
/// <c>TUnitMap_SplitFleet</c>, <c>TUnitMap_JoinFleets</c>).
/// </summary>
public sealed class CarryingArmyRefusalTests
{
    private const string NationId = "north";
    private const string CarryingFleetId = "carrying-fleet";
    private const string OtherFleetId = "other-fleet";
    private const string CarriedArmyId = "carried-army";

    private static GameState BuildState()
    {
        var state = NavalTestbed.InitialState();
        var arx = state.CityById("arx")!; // owned by north

        var carrying = new FleetState(
            CarryingFleetId, NationId, arx.X, arx.Y, Moves: 5, Ships: 30, ConditionPercent: 90,
            Money: 100, SupplyTons: 100, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: CarriedArmyId, CoveredTileCode: null);

        var other = new FleetState(
            OtherFleetId, NationId, arx.X, arx.Y, Moves: 5, Ships: 30, ConditionPercent: 90,
            Money: 0, SupplyTons: 0, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null);

        var army = new ArmyState(
            CarriedArmyId, NationId, arx.X, arx.Y, Moves: 0, Morale: 60, Money: 0, SupplyTons: 10,
            CoveredTileCode: null, AboardFleetId: CarryingFleetId,
            Units: ValueList.Of(new UnitSlot(0, "light_infantry", 5000, 6, "Carried Battalion")));

        return state with
        {
            ActiveSeatIndex = 0,
            Fleets = ValueList.Of(carrying, other),
            Armies = ValueList.Of(army),
        };
    }

    [Fact]
    public void Repair_OnAFleetCarryingAnArmy_IsRefused()
    {
        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(BuildState(), new RepairFleetCommand(NationId, CarryingFleetId, Points: 5));

        Assert.True(result.IsRejected);
        Assert.Equal(RepairFleetRejections.CarryingArmy, result.Code);
    }

    [Fact]
    public void Scuttle_OnAFleetCarryingAnArmy_IsRefused()
    {
        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(BuildState(), new ScuttleFleetCommand(NationId, CarryingFleetId));

        Assert.True(result.IsRejected);
        Assert.Equal(ScuttleFleetRejections.CarryingArmy, result.Code);
    }

    [Fact]
    public void Split_OnAFleetCarryingAnArmy_IsRefused()
    {
        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            BuildState(), new SplitFleetCommand(NationId, CarryingFleetId, "new-split-fleet", ShipsToNewFleet: 10));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitFleetRejections.CarryingArmy, result.Code);
    }

    [Fact]
    public void Join_EitherSideCarryingAnArmy_IsRefused()
    {
        var dispatcher = NavalTestbed.RealEngineDispatcher();

        var survivorCarries = dispatcher.Dispatch(
            BuildState(), new JoinFleetsCommand(NationId, CarryingFleetId, OtherFleetId));
        Assert.True(survivorCarries.IsRejected);
        Assert.Equal(JoinFleetsRejections.CarryingArmy, survivorCarries.Code);

        var absorbedCarries = dispatcher.Dispatch(
            BuildState(), new JoinFleetsCommand(NationId, OtherFleetId, CarryingFleetId));
        Assert.True(absorbedCarries.IsRejected);
        Assert.Equal(JoinFleetsRejections.CarryingArmy, absorbedCarries.Code);
    }
}
