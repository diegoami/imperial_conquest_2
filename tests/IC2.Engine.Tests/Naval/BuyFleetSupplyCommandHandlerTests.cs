using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval.Commands;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T14 Naval" DoD 17: a fleet can be resupplied, and can supply others,
/// with the provider being a city or one of the buyer's own fleets within one tile. Calls T38's
/// <c>SupplyPurchase.BuyForFleet</c>/<c>SupplyCapacity.FleetCapacityTons</c> for the city-provider path;
/// this is what closes the starvation hole B4 found (no production caller of either existed at all).
/// </summary>
public sealed class BuyFleetSupplyCommandHandlerTests
{
    [Fact]
    public void CityProvider_OwnCity_IsFreeAndAdmitsTheFullRequest()
    {
        var state = NavalTestbed.InitialState();
        var nationId = state.Nations[0].Id; // "north"
        var arx = state.CityById("arx")!; // (2, 1), owned by north.

        var fleet = new FleetState(
            "resupply-fleet", nationId, arx.X, arx.Y, Moves: 3, Ships: 10, ConditionPercent: 90,
            Money: 50, SupplyTons: 0, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null);
        state = state with { Fleets = ValueList.Of(fleet) };

        var moneyBefore = fleet.Money;
        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new BuyFleetSupplyCommand(nationId, fleet.Id, ProviderCityId: arx.Id, ProviderFleetId: null, Tons: 40));

        Assert.True(result.IsAccepted, result.ToString());
        var updatedFleet = result.State.FleetById(fleet.Id)!;
        Assert.Equal(40, updatedFleet.SupplyTons);
        Assert.Equal(moneyBefore, updatedFleet.Money); // free at an owned city.
    }

    [Fact]
    public void CityProvider_ForeignCity_ChargesOneTalentPerFiveTons()
    {
        var state = NavalTestbed.InitialState();
        var buyerNationId = "south";
        var arx = state.CityById("arx")!; // owned by "north" -- a foreign purchase for "south".

        var fleet = new FleetState(
            "resupply-fleet-2", buyerNationId, arx.X, arx.Y, Moves: 3, Ships: 10, ConditionPercent: 90,
            Money: 100, SupplyTons: 0, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null);
        state = state with { Fleets = ValueList.Of(fleet), ActiveSeatIndex = 1 };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new BuyFleetSupplyCommand(buyerNationId, fleet.Id, ProviderCityId: arx.Id, ProviderFleetId: null, Tons: 40));

        Assert.True(result.IsAccepted, result.ToString());
        var updatedFleet = result.State.FleetById(fleet.Id)!;
        Assert.Equal(40, updatedFleet.SupplyTons);
        Assert.Equal(92, updatedFleet.Money); // 100 - 40/5.
    }

    [Fact]
    public void FleetProvider_OwnNationsFleet_TransfersTonsFreely()
    {
        var state = NavalTestbed.InitialState();
        var nationId = state.Nations[0].Id;

        var buyer = new FleetState(
            "resupply-buyer", nationId, X: 3, Y: 3, Moves: 4, Ships: 10, ConditionPercent: 90,
            Money: 10, SupplyTons: 0, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null);
        var provider = new FleetState(
            "resupply-provider", nationId, X: 3, Y: 3, Moves: 4, Ships: 20, ConditionPercent: 90,
            Money: 0, SupplyTons: 60, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null);
        state = state with { Fleets = ValueList.Of(buyer, provider) };

        var moneyBefore = buyer.Money;
        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new BuyFleetSupplyCommand(nationId, buyer.Id, ProviderCityId: null, ProviderFleetId: provider.Id, Tons: 45));

        Assert.True(result.IsAccepted, result.ToString());
        var updatedBuyer = result.State.FleetById(buyer.Id)!;
        var updatedProvider = result.State.FleetById(provider.Id)!;

        Assert.Equal(45, updatedBuyer.SupplyTons);
        Assert.Equal(15, updatedProvider.SupplyTons); // 60 - 45.
        Assert.Equal(moneyBefore, updatedBuyer.Money); // same-nation transfer: no talents change hands.
        Assert.Equal(0, updatedProvider.Money);
    }

    [Fact]
    public void FleetProvider_ClampsToTheBuyersCapacity()
    {
        var state = NavalTestbed.InitialState();
        var nationId = state.Nations[0].Id;
        var ruleset = NavalTestbed.Ruleset;

        var buyer = new FleetState(
            "resupply-buyer-2", nationId, X: 3, Y: 3, Moves: 4, Ships: 5, ConditionPercent: 90,
            Money: 0, SupplyTons: 30, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null); // capacity = 5 * 8 = 40, room = 10.
        var provider = new FleetState(
            "resupply-provider-2", nationId, X: 3, Y: 3, Moves: 4, Ships: 20, ConditionPercent: 90,
            Money: 0, SupplyTons: 200, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null);
        state = state with { Fleets = ValueList.Of(buyer, provider) };
        Assert.Equal(40, buyer.Ships * ruleset.Economy.FleetSupplyTonsPerShip); // sanity-check the capacity assumed above.

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new BuyFleetSupplyCommand(nationId, buyer.Id, ProviderCityId: null, ProviderFleetId: provider.Id, Tons: 999));

        Assert.True(result.IsAccepted, result.ToString());
        var updatedBuyer = result.State.FleetById(buyer.Id)!;
        Assert.Equal(40, updatedBuyer.SupplyTons); // clamped to capacity, not 30 + 999.
    }

    [Fact]
    public void FleetProvider_ForeignFleet_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var buyer = new FleetState(
            "resupply-buyer-3", "north", X: 3, Y: 3, Moves: 4, Ships: 10, ConditionPercent: 90,
            Money: 0, SupplyTons: 0, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null);
        var foreignProvider = new FleetState(
            "resupply-foreign", "south", X: 3, Y: 3, Moves: 4, Ships: 20, ConditionPercent: 90,
            Money: 0, SupplyTons: 100, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null);
        state = state with { Fleets = ValueList.Of(buyer, foreignProvider) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new BuyFleetSupplyCommand("north", buyer.Id, ProviderCityId: null, ProviderFleetId: foreignProvider.Id, Tons: 10));

        Assert.True(result.IsRejected);
        Assert.Equal(BuyFleetSupplyRejections.ProviderFleetNotOwnNation, result.Code);
    }

    [Fact]
    public void BothProvidersNamed_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var nationId = state.Nations[0].Id;
        var arx = state.CityById("arx")!;
        var fleet = new FleetState(
            "resupply-both", nationId, arx.X, arx.Y, Moves: 3, Ships: 10, ConditionPercent: 90,
            Money: 0, SupplyTons: 0, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null);
        state = state with { Fleets = ValueList.Of(fleet) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new BuyFleetSupplyCommand(nationId, fleet.Id, ProviderCityId: arx.Id, ProviderFleetId: fleet.Id, Tons: 10));

        Assert.True(result.IsRejected);
        Assert.Equal(BuyFleetSupplyRejections.ExactlyOneProviderRequired, result.Code);
    }

    [Fact]
    public void NeitherProviderNamed_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var nationId = state.Nations[0].Id;
        var fleet = new FleetState(
            "resupply-neither", nationId, X: 3, Y: 3, Moves: 3, Ships: 10, ConditionPercent: 90,
            Money: 0, SupplyTons: 0, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null);
        state = state with { Fleets = ValueList.Of(fleet) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new BuyFleetSupplyCommand(nationId, fleet.Id, ProviderCityId: null, ProviderFleetId: null, Tons: 10));

        Assert.True(result.IsRejected);
        Assert.Equal(BuyFleetSupplyRejections.ExactlyOneProviderRequired, result.Code);
    }

    [Fact]
    public void ProviderMoreThanOneTileAway_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var nationId = state.Nations[0].Id;
        var fleet = new FleetState(
            "resupply-far", nationId, X: 3, Y: 3, Moves: 3, Ships: 10, ConditionPercent: 90,
            Money: 0, SupplyTons: 0, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null);
        var farProvider = new FleetState(
            "resupply-far-provider", nationId, X: 6, Y: 6, Moves: 4, Ships: 20, ConditionPercent: 90,
            Money: 0, SupplyTons: 100, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null);
        state = state with { Fleets = ValueList.Of(fleet, farProvider) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new BuyFleetSupplyCommand(nationId, fleet.Id, ProviderCityId: null, ProviderFleetId: farProvider.Id, Tons: 10));

        Assert.True(result.IsRejected);
        Assert.Equal(BuyFleetSupplyRejections.ProviderTooFar, result.Code);
    }
}
