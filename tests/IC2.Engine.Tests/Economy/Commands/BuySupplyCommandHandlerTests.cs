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
/// rule." Moved to the new clamping API by "T38 Supply dialog follow-ups, treasury ↔ purse transfers, and
/// automatic resupply" (issue #78). Dispatched through the real <see cref="CommandDispatcher"/> over the
/// real engine assembly — the rule itself is already covered end to end by
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

    /// <summary>
    /// T50 Done-when 5 (issue #167): <c>TAFSupply_FindProviders</c> offers "every city within one tile" --
    /// this path never enforced it, unlike the naval twin
    /// (<c>Naval.Commands.BuyFleetSupplyCommandHandler.HandleCityProvider</c>) and this same command's own
    /// fleet-provider branch. north-army-1 sits at (3,2); meridia sits at (3,4) -- two tiles away.
    /// </summary>
    [Fact]
    public void A_city_more_than_one_tile_away_is_rejected_and_changes_nothing()
    {
        var dispatcher = Dispatcher();
        var before = CoreTestbed.InitialState();

        var result = dispatcher.Dispatch(before, new BuySupplyCommand(before.ActiveNationId, "north-army-1", "meridia", 10));

        Assert.Equal(BuySupplyRejections.CityNotWithinRange, result.Code);
        Assert.Same(before, result.State);
    }

    /// <summary>
    /// T50 Done-when 5 (issue #167): the same confirmed <c>TAFSupply_FindProviders</c> gate refuses a
    /// city whose owner is at war with the buyer, regardless of range -- the army is moved onto meridia's
    /// own tile so only the war gate is under test, not adjacency.
    /// </summary>
    [Fact]
    public void A_foreign_city_at_war_is_rejected_and_changes_nothing()
    {
        var dispatcher = Dispatcher();
        var initial = CoreTestbed.InitialState();
        var warCode = CoreTestbed.Toy.Ruleset.Diplomacy.StateCodes.War;
        var army = initial.ArmyById("north-army-1")!;
        var before = WithArmy(initial with { Relations = initial.Relations.WithRelation("north", "south", warCode) },
            army with { X = 3, Y = 4 });

        var result = dispatcher.Dispatch(before, new BuySupplyCommand(before.ActiveNationId, army.Id, "meridia", 10));

        Assert.Equal(BuySupplyRejections.CityOwnerAtWar, result.Code);
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

    /// <summary>
    /// T50 Done-when 3 (issue #165 item 2): pins the decided rejection precedence. T46 hoisted the
    /// <c>Tons &lt;= 0</c> check above the city lookup with nothing depending on the old order, so this is
    /// a decision, recorded here and in <see cref="BuySupplyCommandHandler"/>'s own remarks -- not merely
    /// an incidental consequence of that hoist. A request that is invalid two ways at once (an unknown
    /// city *and* a non-positive amount) must report the amount, never the city.
    /// </summary>
    [Fact]
    public void An_unknown_city_and_a_non_positive_amount_together_reject_as_invalid_amount()
    {
        var dispatcher = Dispatcher();
        var before = CoreTestbed.InitialState();

        var result = dispatcher.Dispatch(before, new BuySupplyCommand(before.ActiveNationId, "north-army-1", "no-such-city", 0));

        Assert.Equal(BuySupplyRejections.InvalidAmount, result.Code);
        Assert.Same(before, result.State);
    }

    /// <summary>
    /// T38 (#78, Done-when 1): a request for more than the city holds is no longer rejected -- it is
    /// admitted at whatever the smallest cap allows. Here that is the army's own room (186 - 185 = 1 t,
    /// north-army-1's default 18,500 troops and 185 t), smaller than even the city's reduced 5-t stock.
    /// </summary>
    [Fact]
    public void A_city_without_enough_supply_to_sell_clamps_the_purchase_instead_of_rejecting_it()
    {
        var sink = new RecordingEventSink();
        var dispatcher = Dispatcher(sink);
        var initial = CoreTestbed.InitialState();
        var city = initial.CityById("arx")!;
        var before = WithCity(initial, city with { SupplyTons = 5 });

        var result = dispatcher.Dispatch(before, new BuySupplyCommand(before.ActiveNationId, "north-army-1", "arx", 100));

        Assert.True(result.IsAccepted);
        var purchased = Assert.IsType<ArmySupplyPurchased>(Assert.Single(sink.Events));
        Assert.Equal(100, purchased.RequestedTons);
        Assert.Equal(1, purchased.AdmittedTons); // room = troops/100 + 1 - supplies = 186 - 185 = 1.
        Assert.Equal(0, purchased.TalentsPaid); // arx is north-army-1's own city: free.

        var updatedArmy = result.State.ArmyById("north-army-1")!;
        var updatedCity = result.State.CityById("arx")!;
        Assert.Equal(186, updatedArmy.SupplyTons);
        Assert.Equal(4, updatedCity.SupplyTons);
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

        // T50 Done-when 5 (issue #167): the city-provider path now enforces the one-tile adjacency gate
        // TAFSupply_FindProviders always had, so the buying army must actually be within range of the
        // foreign city -- moved to (3,3), Chebyshev distance 1 from meridia (3,4), rather than its unmoved
        // world position (3,2), two tiles away. T70 (#190 N5): adjacent, not parked on meridia's own
        // tile -- an army never occupies a city tile.
        var before = WithArmy(
            initial, army with { X = 3, Y = 3, SupplyTons = 0, Money = startingMoney, Units = units });
        var city = before.CityById("meridia")!; // owned by "south" -- foreign to the buying "north" army.
        var southBefore = before.NationById("south")!;

        var result = dispatcher.Dispatch(before, new BuySupplyCommand(before.ActiveNationId, army.Id, city.Id, 100));

        Assert.True(result.IsAccepted);
        var purchased = Assert.IsType<ArmySupplyPurchased>(Assert.Single(sink.Events));
        Assert.False(purchased.WasFreeOwnCity);
        Assert.Equal(100, purchased.AdmittedTons);
        Assert.Equal(20, purchased.TalentsPaid); // 100 / SupplyTonsPerTalent(5) = 20.

        var updatedArmy = result.State.ArmyById(army.Id)!;
        Assert.Equal(100, updatedArmy.SupplyTons);
        Assert.Equal(startingMoney - 20, updatedArmy.Money);

        // T38 (#78, Done-when 5): the seller (south, meridia's owner) is credited too.
        var southAfter = result.State.NationById("south")!;
        Assert.Equal(southBefore.Treasury + 20, southAfter.Treasury);
    }

    /// <summary>
    /// T38 (#78, Done-when 1): a foreign purchase the buyer's purse cannot afford at all (money = 0, so
    /// the money cap is 0 tons) is no longer rejected -- it clamps to 0, moving nothing, and still
    /// succeeds.
    /// </summary>
    [Fact]
    public void A_foreign_purchase_the_purse_cannot_afford_clamps_to_zero_instead_of_rejecting_it()
    {
        var sink = new RecordingEventSink();
        var dispatcher = Dispatcher(sink);
        var initial = CoreTestbed.InitialState();

        var army = initial.ArmyById("north-army-1")!;
        var units = ValueList.Of(new UnitSlot(0, "light_infantry", 50_000, 6, "Test Battalion"));

        // T50 Done-when 5: moved to (3,3), Chebyshev distance 1 from meridia (3,4), within the one-tile
        // adjacency gate this path now enforces -- see the sibling test above. T70 (#190 N5): adjacent,
        // not parked on meridia's own tile.
        var before = WithArmy(initial, army with { X = 3, Y = 3, SupplyTons = 0, Money = 0, Units = units });
        var city = before.CityById("meridia")!; // foreign.

        var result = dispatcher.Dispatch(before, new BuySupplyCommand(before.ActiveNationId, army.Id, city.Id, 100));

        Assert.True(result.IsAccepted);
        var purchased = Assert.IsType<ArmySupplyPurchased>(Assert.Single(sink.Events));
        Assert.Equal(0, purchased.AdmittedTons); // money(0) * SupplyTonsPerTalent(5) = 0 t affordable.
        Assert.Equal(0, purchased.TalentsPaid);

        var updatedArmy = result.State.ArmyById(army.Id)!;
        Assert.Equal(0, updatedArmy.SupplyTons);
        Assert.Equal(0, updatedArmy.Money);
    }

    /// <summary>
    /// T70 Done-when 6b (bug #345): the one-tile adjacency this path enforces is ruleset data
    /// (<see cref="EconomyRules.CommandAdjacencyRadiusTiles"/>), not a <c>&gt; 1</c> literal. Widened to 2
    /// tiles in a test ruleset, a purchase from distance 2 -- rejected by
    /// <see cref="A_city_more_than_one_tile_away_is_rejected_and_changes_nothing"/> under the shipped
    /// radius of 1 -- is admitted instead.
    /// </summary>
    [Fact]
    public void A_wider_ruleset_adjacency_radius_admits_a_purchase_at_the_wider_distance()
    {
        var widenedRuleset = CoreTestbed.Toy.Ruleset with
        {
            Economy = CoreTestbed.Toy.Ruleset.Economy with { CommandAdjacencyRadiusTiles = 2 },
        };
        var dispatcher = new CommandDispatcher(
            SystemRegistry.FromEngineAssembly(), widenedRuleset, CoreTestbed.Toy.World, NullEventSink.Instance);
        var before = CoreTestbed.InitialState(); // north-army-1 at (3,2); meridia at (3,4): distance 2.

        var result = dispatcher.Dispatch(before, new BuySupplyCommand(before.ActiveNationId, "north-army-1", "meridia", 10));

        Assert.True(result.IsAccepted, result.ToString());
    }
}
