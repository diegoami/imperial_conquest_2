using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval.Commands;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T46 Fleet-to-fleet transfer, and the supply path that keeps fleets
/// alive" (issue #148), Done-when 4, 6, 7 and 8 — <see cref="BuyFleetSupplyCommand"/>.
/// </summary>
public sealed class BuyFleetSupplyCommandHandlerTests
{
    private const string NationId = "north";
    private const string OtherNationId = "south";

    private static FleetState Fleet(
        string id, int x, int y, int ships, int supply = 0, int money = 0, string nation = NationId) =>
        new(id, nation, x, y, Moves: 5, ships, ConditionPercent: 90, money, supply,
            ConstructionTicksRemaining: null, BuildCityId: null, CarriedArmyId: null, CoveredTileCode: null);

    private static CityState City(string id, int x, int y, string owner, int supply) =>
        new(id, id, x, y, owner, owner, Loyalty: 80, supply, FortificationCode: 0,
            PopulationThousands: 50, MaxPopulationThousands: 100, Tribute: 0, UnderSiege: false,
            Garrison: ValueList<UnitSlot>.Empty);

    /// <summary>DoD 6/7: an own-city purchase is free and clamped by the fleet's cap (ships × 8, no dialog bonus).</summary>
    [Fact]
    public void CityProvider_OwnCity_IsFree_AndClampedByFleetCapacity()
    {
        var state = NavalTestbed.InitialState();
        var fleet = Fleet("supply-fleet-own", 3, 3, ships: 10, supply: 60, money: 20); // cap = 80.
        var city = City("supply-city-own", 3, 3, owner: NationId, supply: 500);
        state = state with { Fleets = ValueList.Of(fleet), Cities = ValueList.From(state.Cities.Append(city)) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new BuyFleetSupplyCommand(NationId, fleet.Id, city.Id, ProviderFleetId: null, Tons: 100));

        Assert.True(result.IsAccepted, result.ToString());
        var updatedFleet = result.State.FleetById(fleet.Id)!;
        var updatedCity = result.State.CityById(city.Id)!;

        Assert.Equal(80, updatedFleet.SupplyTons); // clamped at ships x 8, not 60+100.
        Assert.Equal(20, updatedFleet.Money); // free -- own city, no talents move.
        Assert.Equal(500 - 20, updatedCity.SupplyTons); // only the admitted 20 tons left the city.
    }

    /// <summary>DoD 7: a foreign, non-hostile city charges one talent per five tons, credited to its owner.</summary>
    [Fact]
    public void CityProvider_ForeignCity_NotAtWar_IsPaid_AndCreditsTheSellersNation()
    {
        var state = NavalTestbed.InitialState();
        var fleet = Fleet("supply-fleet-foreign", 3, 3, ships: 10, supply: 0, money: 100); // cap = 80.
        var city = City("supply-city-foreign", 3, 3, owner: OtherNationId, supply: 500);
        state = state with { Fleets = ValueList.Of(fleet), Cities = ValueList.From(state.Cities.Append(city)) };

        var buyerBefore = state.NationById(NationId)!;
        var sellerBefore = state.NationById(OtherNationId)!;

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new BuyFleetSupplyCommand(NationId, fleet.Id, city.Id, ProviderFleetId: null, Tons: 50));

        Assert.True(result.IsAccepted, result.ToString());
        var updatedFleet = result.State.FleetById(fleet.Id)!;
        Assert.Equal(50, updatedFleet.SupplyTons); // 0 + 50, within the 80-ton cap.

        var buyerAfter = result.State.NationById(NationId)!;
        var sellerAfter = result.State.NationById(OtherNationId)!;

        // Confirmed: SupplyPurchase.BuyForFleet debits the buyer's own purse (per-unit purses under
        // classical-faithful), one talent per five tons, and credits the selling city's owner's treasury.
        Assert.Equal(sellerBefore.Treasury + 10, sellerAfter.Treasury); // 50 / 5 = 10.
        Assert.Equal(buyerBefore.Treasury, buyerAfter.Treasury); // treasury untouched -- the fleet's own purse paid.
    }

    /// <summary>DoD 7: the city provider's confirmed "not at war" gate refuses a hostile foreign city.</summary>
    [Fact]
    public void CityProvider_ForeignCity_AtWar_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var warCode = NavalTestbed.Ruleset.Diplomacy.StateCodes.War;
        state = state with { Relations = state.Relations.WithRelation(NationId, OtherNationId, warCode) };

        var fleet = Fleet("supply-fleet-war", 3, 3, ships: 10, supply: 0, money: 100);
        var city = City("supply-city-war", 3, 3, owner: OtherNationId, supply: 500);
        state = state with { Fleets = ValueList.Of(fleet), Cities = ValueList.From(state.Cities.Append(city)) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new BuyFleetSupplyCommand(NationId, fleet.Id, city.Id, ProviderFleetId: null, Tons: 10));

        Assert.True(result.IsRejected);
        Assert.Equal(BuyFleetSupplyRejections.CityOwnerAtWar, result.Code);
    }

    /// <summary>A provider city more than one tile away is refused.</summary>
    [Fact]
    public void CityProvider_NotWithinRange_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var fleet = Fleet("supply-fleet-far", 0, 0, ships: 10);
        var city = City("supply-city-far", 5, 5, owner: NationId, supply: 500);
        state = state with { Fleets = ValueList.Of(fleet), Cities = ValueList.From(state.Cities.Append(city)) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new BuyFleetSupplyCommand(NationId, fleet.Id, city.Id, ProviderFleetId: null, Tons: 10));

        Assert.True(result.IsRejected);
        Assert.Equal(BuyFleetSupplyRejections.CityNotWithinRange, result.Code);
    }

    /// <summary>
    /// DoD 6/8: a fleet provider moves tons only -- no talents change hands, matching the confirmed
    /// free-path shape rather than inventing a recipient for TAFSupply_TransferSupply's uncredited case.
    /// </summary>
    [Fact]
    public void FleetProvider_OrdinaryTransfer_MovesTonsOnly_NoMoneyChangesHands()
    {
        var state = NavalTestbed.InitialState();
        var buyer = Fleet("supply-buyer", 3, 3, ships: 10, supply: 0, money: 20); // cap = 80.
        var provider = Fleet("supply-provider", 3, 3, ships: 10, supply: 60, money: 5);
        state = state with { Fleets = ValueList.Of(buyer, provider) };

        var buyerNationBefore = state.NationById(NationId)!;

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new BuyFleetSupplyCommand(NationId, buyer.Id, ProviderCityId: null, provider.Id, Tons: 30));

        Assert.True(result.IsAccepted, result.ToString());
        var updatedBuyer = result.State.FleetById(buyer.Id)!;
        var updatedProvider = result.State.FleetById(provider.Id)!;

        Assert.Equal(30, updatedBuyer.SupplyTons);
        Assert.Equal(20, updatedBuyer.Money); // untouched -- no cost on this path.
        Assert.Equal(30, updatedProvider.SupplyTons); // 60 - 30.
        Assert.Equal(5, updatedProvider.Money); // untouched.

        var buyerNationAfter = result.State.NationById(NationId)!;
        Assert.Equal(buyerNationBefore.Treasury, buyerNationAfter.Treasury); // no treasury movement either.

        // Conservation across the pair.
        Assert.Equal(buyer.SupplyTons + provider.SupplyTons, updatedBuyer.SupplyTons + updatedProvider.SupplyTons);
    }

    /// <summary>DoD 6: the buyer's room clamp is not floored at 0, and is capped by the provider's own stock.</summary>
    [Fact]
    public void FleetProvider_ClampsByBuyerCapacity_AndProviderStock()
    {
        var state = NavalTestbed.InitialState();
        var buyer = Fleet("supply-buyer-clamp", 3, 3, ships: 10, supply: 75); // cap = 80, room = 5.
        var provider = Fleet("supply-provider-clamp", 3, 3, ships: 10, supply: 3); // less than the room.
        state = state with { Fleets = ValueList.Of(buyer, provider) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new BuyFleetSupplyCommand(NationId, buyer.Id, ProviderCityId: null, provider.Id, Tons: 100));

        Assert.True(result.IsAccepted, result.ToString());
        var updatedBuyer = result.State.FleetById(buyer.Id)!;
        var updatedProvider = result.State.FleetById(provider.Id)!;

        Assert.Equal(78, updatedBuyer.SupplyTons); // 75 + 3 -- capped by the provider's stock, not the 5-ton room.
        Assert.Equal(0, updatedProvider.SupplyTons);
    }

    /// <summary>DoD 4 (T14 round-2 review, B9): a fleet cannot buy supply from itself.</summary>
    [Fact]
    public void FleetProvider_SameFleetAsBuyer_IsRefused_AndCreatesNoSupplyFromNothing()
    {
        var state = NavalTestbed.InitialState();
        var fleet = Fleet("supply-self", 3, 3, ships: 10, supply: 20, money: 10);
        state = state with { Fleets = ValueList.Of(fleet) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new BuyFleetSupplyCommand(NationId, fleet.Id, ProviderCityId: null, fleet.Id, Tons: 50));

        Assert.True(result.IsRejected);
        Assert.Equal(BuyFleetSupplyRejections.SelfSupply, result.Code);

        // Proves the exact repro the first attempt let through: repeatable self-supply must not move the
        // fleet's own stock at all, let alone grow it toward the cap.
        Assert.Equal(20, result.State.FleetById(fleet.Id)!.SupplyTons);
    }

    /// <summary>A fleet provider must belong to the buyer's own nation -- unlike a city, never foreign.</summary>
    [Fact]
    public void FleetProvider_BelongingToAnotherNation_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var buyer = Fleet("supply-buyer-foreign-provider", 3, 3, ships: 10);
        var foreignProvider = Fleet("supply-foreign-provider", 3, 3, ships: 10, supply: 40, nation: OtherNationId);
        state = state with { Fleets = ValueList.Of(buyer, foreignProvider) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new BuyFleetSupplyCommand(NationId, buyer.Id, ProviderCityId: null, foreignProvider.Id, Tons: 10));

        Assert.True(result.IsRejected);
        Assert.Equal(BuyFleetSupplyRejections.ProviderFleetNotYours, result.Code);
    }

    /// <summary>A provider fleet more than one tile away is refused.</summary>
    [Fact]
    public void FleetProvider_NotWithinRange_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var buyer = Fleet("supply-buyer-far", 0, 0, ships: 10);
        var provider = Fleet("supply-provider-far", 9, 9, ships: 10, supply: 40);
        state = state with { Fleets = ValueList.Of(buyer, provider) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new BuyFleetSupplyCommand(NationId, buyer.Id, ProviderCityId: null, provider.Id, Tons: 10));

        Assert.True(result.IsRejected);
        Assert.Equal(BuyFleetSupplyRejections.ProviderFleetNotWithinRange, result.Code);
    }

    /// <summary>Naming neither, or both, of the two provider kinds is refused.</summary>
    [Fact]
    public void NeitherOrBothProviders_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var buyer = Fleet("supply-buyer-provider-shape", 3, 3, ships: 10);
        var city = City("supply-city-provider-shape", 3, 3, owner: NationId, supply: 100);
        var otherFleet = Fleet("supply-other-fleet-provider-shape", 3, 3, ships: 5, supply: 20);
        state = state with
        {
            Fleets = ValueList.Of(buyer, otherFleet),
            Cities = ValueList.From(state.Cities.Append(city)),
        };

        var dispatcher = NavalTestbed.RealEngineDispatcher();

        var neither = dispatcher.Dispatch(state, new BuyFleetSupplyCommand(NationId, buyer.Id, null, null, Tons: 10));
        Assert.Equal(BuyFleetSupplyRejections.InvalidProvider, neither.Code);

        var both = dispatcher.Dispatch(state, new BuyFleetSupplyCommand(NationId, buyer.Id, city.Id, otherFleet.Id, Tons: 10));
        Assert.Equal(BuyFleetSupplyRejections.InvalidProvider, both.Code);
    }
}
