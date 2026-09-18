using IC2.Engine.Core;
using IC2.Engine.Economy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Economy.Commands;

/// <summary>
/// Folded follow-up <see href="https://github.com/diegoami/imperial_conquest_2/issues/147">#147</see>,
/// planned into <c>docs/task-catalogue.md</c> "T46 Fleet-to-fleet transfer, and the supply path that
/// keeps fleets alive" (issue #148) Done-when 11: the army-buys-from-a-fleet direction on
/// <see cref="BuySupplyCommand"/>. Kept in its own file rather than added to
/// <c>BuySupplyCommandHandlerTests</c>, which is T38's existing test file, not this task's to edit.
/// </summary>
public sealed class BuySupplyCommandFleetProviderTests
{
    private static CommandDispatcher Dispatcher(IEventSink? sink = null) => new(
        SystemRegistry.FromEngineAssembly(), CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, sink ?? NullEventSink.Instance);

    private static GameState WithArmy(GameState state, ArmyState updated) =>
        state with
        {
            Armies = ValueList.From(state.Armies.Select(a =>
                string.Equals(a.Id, updated.Id, StringComparison.Ordinal) ? updated : a)),
        };

    private static FleetState Fleet(string id, int x, int y, int ships, int supply, string nation = "north") =>
        new(id, nation, x, y, Moves: 4, ships, ConditionPercent: 90, Money: 0, supply,
            ConstructionTicksRemaining: null, BuildCityId: null, CarriedArmyId: null, CoveredTileCode: null);

    /// <summary>
    /// DoD 11/#147: an army buys supply from one of its own nation's co-located fleets. The dialog's
    /// <c>+1</c> capacity bonus applies here exactly as it does at a city, since it is the same
    /// <c>TUnitMap_SupplyArmy</c> → <c>TAFSupply</c> dialog either way.
    /// </summary>
    [Fact]
    public void ArmyBuysFromOwnFleet_MovesTonsOnly_NoMoneyChangesHands()
    {
        var sink = new RecordingEventSink();
        var dispatcher = Dispatcher(sink);
        var initial = CoreTestbed.InitialState();

        var army = initial.ArmyById("north-army-1")!; // at (3, 2), 18,500 troops -- cap = 186.
        var before = WithArmy(initial, army with { SupplyTons = 100, Money = 50 });
        var providerFleet = Fleet("provider-fleet-for-army", x: 3, y: 2, ships: 10, supply: 60);
        before = before with { Fleets = ValueList.Of(providerFleet) };

        var result = dispatcher.Dispatch(
            before, new BuySupplyCommand(before.ActiveNationId, army.Id, CityId: null, Tons: 50, providerFleet.Id));

        Assert.True(result.IsAccepted, result.ToString());
        var purchased = Assert.IsType<ArmySupplyPurchasedFromFleet>(Assert.Single(sink.Events));
        Assert.Equal(50, purchased.RequestedTons);
        Assert.Equal(50, purchased.AdmittedTons); // 186 - 100 = 86 room, 60 provider stock -- 50 fits both.

        var updatedArmy = result.State.ArmyById(army.Id)!;
        var updatedFleet = result.State.FleetById(providerFleet.Id)!;
        Assert.Equal(150, updatedArmy.SupplyTons);
        Assert.Equal(50, updatedArmy.Money); // no cost on this path (DoD 8/#147's [open] payment leg).
        Assert.Equal(10, updatedFleet.SupplyTons); // 60 - 50.
    }

    /// <summary>The clamp is capped by the provider fleet's own stock, not just the army's room.</summary>
    [Fact]
    public void ArmyBuysFromOwnFleet_ClampsByProviderStock()
    {
        var sink = new RecordingEventSink();
        var dispatcher = Dispatcher(sink);
        var initial = CoreTestbed.InitialState();

        var army = initial.ArmyById("north-army-1")!;
        var before = WithArmy(initial, army with { SupplyTons = 0 }); // room = 186.
        var providerFleet = Fleet("provider-fleet-scarce", x: 3, y: 2, ships: 10, supply: 12); // less than the room.
        before = before with { Fleets = ValueList.Of(providerFleet) };

        var result = dispatcher.Dispatch(
            before, new BuySupplyCommand(before.ActiveNationId, army.Id, CityId: null, Tons: 100, providerFleet.Id));

        Assert.True(result.IsAccepted, result.ToString());
        var updatedArmy = result.State.ArmyById(army.Id)!;
        var updatedFleet = result.State.FleetById(providerFleet.Id)!;
        Assert.Equal(12, updatedArmy.SupplyTons); // clamped by the provider's own 12-ton stock.
        Assert.Equal(0, updatedFleet.SupplyTons);
    }

    /// <summary>A fleet provider belonging to another nation is refused -- unlike a city, never foreign.</summary>
    [Fact]
    public void ProviderFleetBelongingToAnotherNation_IsRefused()
    {
        var dispatcher = Dispatcher();
        var initial = CoreTestbed.InitialState();

        var army = initial.ArmyById("north-army-1")!;
        var foreignFleet = Fleet("foreign-provider-fleet", x: 3, y: 2, ships: 10, supply: 40, nation: "south");
        var before = initial with { Fleets = ValueList.Of(foreignFleet) };

        var result = dispatcher.Dispatch(
            before, new BuySupplyCommand(before.ActiveNationId, army.Id, CityId: null, Tons: 10, foreignFleet.Id));

        Assert.Equal(BuySupplyRejections.ProviderFleetNotYours, result.Code);
        Assert.Same(before, result.State);
    }

    /// <summary>A fleet provider more than one tile away is refused.</summary>
    [Fact]
    public void ProviderFleetNotWithinRange_IsRefused()
    {
        var dispatcher = Dispatcher();
        var initial = CoreTestbed.InitialState();

        var army = initial.ArmyById("north-army-1")!; // at (3, 2).
        var farFleet = Fleet("far-provider-fleet", x: 9, y: 9, ships: 10, supply: 40);
        var before = initial with { Fleets = ValueList.Of(farFleet) };

        var result = dispatcher.Dispatch(
            before, new BuySupplyCommand(before.ActiveNationId, army.Id, CityId: null, Tons: 10, farFleet.Id));

        Assert.Equal(BuySupplyRejections.ProviderFleetNotWithinRange, result.Code);
        Assert.Same(before, result.State);
    }

    /// <summary>Naming neither, or both, of the two provider kinds is refused.</summary>
    [Fact]
    public void NeitherOrBothProviders_IsRefused()
    {
        var dispatcher = Dispatcher();
        var initial = CoreTestbed.InitialState();
        var army = initial.ArmyById("north-army-1")!;
        var fleet = Fleet("shape-provider-fleet", x: 3, y: 2, ships: 10, supply: 40);
        var before = initial with { Fleets = ValueList.Of(fleet) };

        var neither = dispatcher.Dispatch(before, new BuySupplyCommand(before.ActiveNationId, army.Id, CityId: null, 10, ProviderFleetId: null));
        Assert.Equal(BuySupplyRejections.InvalidProvider, neither.Code);

        var both = dispatcher.Dispatch(before, new BuySupplyCommand(before.ActiveNationId, army.Id, "arx", 10, fleet.Id));
        Assert.Equal(BuySupplyRejections.InvalidProvider, both.Code);
    }

    /// <summary>An unknown provider fleet id is refused.</summary>
    [Fact]
    public void UnknownProviderFleet_IsRefused()
    {
        var dispatcher = Dispatcher();
        var initial = CoreTestbed.InitialState();
        var army = initial.ArmyById("north-army-1")!;

        var result = dispatcher.Dispatch(
            initial, new BuySupplyCommand(initial.ActiveNationId, army.Id, CityId: null, 10, "no-such-fleet"));

        Assert.Equal(BuySupplyRejections.UnknownProviderFleet, result.Code);
        Assert.Same(initial, result.State);
    }
}
