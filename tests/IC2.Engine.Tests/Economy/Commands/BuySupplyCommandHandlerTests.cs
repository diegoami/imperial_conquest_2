using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Economy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Tests.Core;
using Xunit;

namespace IC2.Engine.Tests.Economy.Commands;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T41 Thin CLI demo on the toy world", Done-when 4: "<c>BuySupply</c> is
/// free at an own city and costs <c>amount / 5</c> abroad, using T08's functions. It re-implements no
/// rule." Dispatched through the real <see cref="CommandDispatcher"/> over the real engine assembly —
/// the rule itself is already covered end to end by
/// <c>tests/IC2.Engine.Tests/Economy/SupplyPurchaseTests.cs</c>; these tests cover only the command layer
/// wrapped around it.
/// </summary>
public sealed class BuySupplyCommandHandlerTests
{
    private static CommandDispatcher Dispatcher(IEventSink? sink = null) => new(
        SystemRegistry.FromEngineAssembly(), CoreTestbed.Toy.Ruleset, CoreTestbed.Toy.World, sink ?? NullEventSink.Instance);

    private static GameState WithArmy(GameState state, ArmyState updated) =>
        state with
        {
            Armies = ValueList.From(state.Armies.Select(a =>
                string.Equals(a.Id, updated.Id, StringComparison.Ordinal) ? updated : a)),
        };

    private static GameState WithCity(GameState state, CityState updated) =>
        state with
        {
            Cities = ValueList.From(state.Cities.Select(c =>
                string.Equals(c.Id, updated.Id, StringComparison.Ordinal) ? updated : c)),
        };

    [Fact]
    public void Unknown_army_is_rejected_and_changes_nothing()
    {
        var dispatcher = Dispatcher();
        var before = CoreTestbed.InitialState();

        var result = dispatcher.Dispatch(before, new BuySupplyCommand(before.ActiveNationId, "no-such-army", "arx", 10));

        Assert.Equal(BuySupplyRejections.UnknownArmy, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void Another_nations_army_is_rejected_and_changes_nothing()
    {
        var dispatcher = Dispatcher();
        var before = CoreTestbed.InitialState();
        Assert.Equal("north", before.ActiveNationId);

        var result = dispatcher.Dispatch(before, new BuySupplyCommand(before.ActiveNationId, "south-army-1", "meridia", 10));

        Assert.Equal(BuySupplyRejections.NotYourArmy, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void Unknown_city_is_rejected_and_changes_nothing()
    {
        var dispatcher = Dispatcher();
        var before = CoreTestbed.InitialState();

        var result = dispatcher.Dispatch(before, new BuySupplyCommand(before.ActiveNationId, "north-army-1", "no-such-city", 10));

        Assert.Equal(BuySupplyRejections.UnknownCity, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void A_non_positive_amount_is_rejected_and_changes_nothing()
    {
        var dispatcher = Dispatcher();
        var before = CoreTestbed.InitialState();

        var result = dispatcher.Dispatch(before, new BuySupplyCommand(before.ActiveNationId, "north-army-1", "arx", 0));

        Assert.Equal(BuySupplyRejections.InvalidAmount, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void A_city_without_enough_supply_to_sell_is_rejected_and_changes_nothing()
    {
        var dispatcher = Dispatcher();
        var initial = CoreTestbed.InitialState();
        var city = initial.CityById("arx")!;
        var before = WithCity(initial, city with { SupplyTons = 5 });

        var result = dispatcher.Dispatch(before, new BuySupplyCommand(before.ActiveNationId, "north-army-1", "arx", 100));

        Assert.Equal(BuySupplyRejections.InsufficientCitySupply, result.Code);
        Assert.Same(before, result.State);
    }

    [Fact]
    public void Buying_at_an_own_city_is_free()
    {
        var sink = new RecordingEventSink();
        var dispatcher = Dispatcher(sink);
        var initial = CoreTestbed.InitialState();

        // Give the army plenty of capacity room (troops/100 + 1 = 501) so the whole request is admitted.
        var army = initial.ArmyById("north-army-1")!;
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 50_000, 6, "Test Battalion"));
        var before = WithArmy(initial, army with { SupplyTons = 0, Units = units });
        var city = before.CityById("arx")!; // owned by "north", the buying army's own nation.

        var result = dispatcher.Dispatch(before, new BuySupplyCommand(before.ActiveNationId, army.Id, city.Id, 100));

        Assert.True(result.IsAccepted);
        var purchased = Assert.IsType<ArmySupplyPurchased>(Assert.Single(sink.Events));
        Assert.True(purchased.WasFreeOwnCity);
        Assert.Equal(0, purchased.TalentsPaid);
        Assert.Equal(100, purchased.AdmittedTons);

        var updatedArmy = result.State.ArmyById(army.Id)!;
        var updatedCity = result.State.CityById(city.Id)!;
        Assert.Equal(100, updatedArmy.SupplyTons);
        Assert.Equal(city.SupplyTons - 100, updatedCity.SupplyTons);
        Assert.Equal(army.Money, updatedArmy.Money); // unchanged: free.
    }

    [Fact]
    public void Buying_at_a_foreign_city_costs_amount_divided_by_five()
    {
        var sink = new RecordingEventSink();
        var dispatcher = Dispatcher(sink);
        var initial = CoreTestbed.InitialState();

        var army = initial.ArmyById("north-army-1")!;
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 50_000, 6, "Test Battalion"));
        var startingMoney = 1000;
        var before = WithArmy(initial, army with { SupplyTons = 0, Money = startingMoney, Units = units });
        var city = before.CityById("meridia")!; // owned by "south" -- foreign to the buying "north" army.

        var result = dispatcher.Dispatch(before, new BuySupplyCommand(before.ActiveNationId, army.Id, city.Id, 100));

        Assert.True(result.IsAccepted);
        var purchased = Assert.IsType<ArmySupplyPurchased>(Assert.Single(sink.Events));
        Assert.False(purchased.WasFreeOwnCity);
        Assert.Equal(100, purchased.AdmittedTons);
        Assert.Equal(20, purchased.TalentsPaid); // 100 / SupplyTonsPerTalent(5) = 20.

        var updatedArmy = result.State.ArmyById(army.Id)!;
        Assert.Equal(100, updatedArmy.SupplyTons);
        Assert.Equal(startingMoney - 20, updatedArmy.Money);
    }

    [Fact]
    public void Insufficient_funds_for_a_foreign_purchase_is_rejected_and_changes_nothing()
    {
        var dispatcher = Dispatcher();
        var initial = CoreTestbed.InitialState();

        var army = initial.ArmyById("north-army-1")!;
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 50_000, 6, "Test Battalion"));
        var before = WithArmy(initial, army with { SupplyTons = 0, Money = 0, Units = units });
        var city = before.CityById("meridia")!; // foreign.

        var result = dispatcher.Dispatch(before, new BuySupplyCommand(before.ActiveNationId, army.Id, city.Id, 100));

        Assert.Equal(BuySupplyRejections.InsufficientFunds, result.Code);
        Assert.Same(before, result.State);
    }
}
