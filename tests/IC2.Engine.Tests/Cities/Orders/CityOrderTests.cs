using IC2.Engine.Cities.Orders;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Fixtures;
using Xunit;

namespace IC2.Engine.Tests.Cities.Orders;

/// <summary>
/// City fortification orders: placement, cost, in-progress encoding, siege clearing, and refusal conditions.
/// </summary>
public class CityOrderTests
{
    private readonly GameStateFactory _factory;

    public CityOrderTests()
    {
        _factory = GameStateFactory.FromScenario(ScenarioLoader.LoadScenario("toy-3city"));
    }

    // ---- DoD 1: Cost formula ----
    // An order of N points costs `population(thousands) × N` talents.

    [Fact]
    public void Fortify_costs_population_thousands_times_points()
    {
        // Rome's capital in the toy scenario: Mediolanum at (150, 60), population 5 thousands
        var state = _factory.CreateState();
        var command = new OrderCityCommand(
            IssuingNationId: "rome",
            CityId: "mediolanum",
            OrderId: "fortify",
            Points: 10);

        var beforeTreasury = state.NationById("rome")!.Treasury;
        var expectedCost = 5 * 10; // 5 thousands population × 10 points

        var dispatcher = new CommandDispatcher(state, _factory.CreateRuleset());
        var result = dispatcher.Execute(command);

        Assert.True(result.IsAccepted);
        var newTreasury = result.State.NationById("rome")!.Treasury;
        Assert.Equal(beforeTreasury - expectedCost, newTreasury);
    }

    [Fact]
    public void Fortify_cost_preserves_conservation_of_money()
    {
        // Money is conserved: the debited talent goes somewhere (to be defined by the ruleset's
        // order mechanics; fortification consumes the cost).
        var state = _factory.CreateState();
        var beforeTreasury = state.NationById("rome")!.Treasury;
        var allNationsTreasury = state.Nations.Sum(n => n.Treasury);

        var command = new OrderCityCommand(
            IssuingNationId: "rome",
            CityId: "mediolanum",
            OrderId: "fortify",
            Points: 5);

        var dispatcher = new CommandDispatcher(state, _factory.CreateRuleset());
        var result = dispatcher.Execute(command);

        Assert.True(result.IsAccepted);
        var newAllNationsTreasury = result.State.Nations.Sum(n => n.Treasury);
        // The cost is debited, not transferred to another nation
        Assert.Equal(allNationsTreasury - (5 * 5), newAllNationsTreasury);
    }

    // ---- DoD 2: In-progress encoding and readback ----
    // It reads back as in-progress via the `> 100` encoding and the panel text.

    [Fact]
    public void Fortify_order_stores_in_progress_encoding()
    {
        var state = _factory.CreateState();
        var city = state.CityById("mediolanum")!;
        var ruleset = _factory.CreateRuleset();
        var fortifyOrder = ruleset.CityOrders.Orders.Single();

        var command = new OrderCityCommand(
            IssuingNationId: "rome",
            CityId: "mediolanum",
            OrderId: "fortify",
            Points: 5);

        var dispatcher = new CommandDispatcher(state, ruleset);
        var result = dispatcher.Execute(command);

        Assert.True(result.IsAccepted);
        var updatedCity = result.State.CityById("mediolanum")!;

        // Initial city has 0% fortification, order is 5 points
        // In-progress encoding: 0 + (5 × 100) = 500
        Assert.Equal(500, updatedCity.FortificationCode);
        Assert.True(FortificationCode.IsOrderInProgress(updatedCity.FortificationCode, fortifyOrder));
    }

    [Fact]
    public void Fortify_order_pending_points_decode_correctly()
    {
        var state = _factory.CreateState();
        var ruleset = _factory.CreateRuleset();
        var fortifyOrder = ruleset.CityOrders.Orders.Single();

        var command = new OrderCityCommand(
            IssuingNationId: "rome",
            CityId: "mediolanum",
            OrderId: "fortify",
            Points: 7);

        var dispatcher = new CommandDispatcher(state, ruleset);
        var result = dispatcher.Execute(command);

        var updatedCity = result.State.CityById("mediolanum")!;

        // Finished percent should be 0 (city was at 0%)
        Assert.Equal(0, FortificationCode.FinishedPercent(updatedCity.FortificationCode, fortifyOrder));

        // Pending points should be 7
        Assert.Equal(7, FortificationCode.PendingPoints(updatedCity.FortificationCode, fortifyOrder));
    }

    // ---- DoD 3: Siege clears pending orders ----
    // A siege attempt clears it (`fort %= 100`).

    [Fact]
    public void Siege_clears_pending_fortification_order()
    {
        var state = _factory.CreateState();
        var ruleset = _factory.CreateRuleset();
        var fortifyOrder = ruleset.CityOrders.Orders.Single();

        // First, place a fortification order
        var orderCommand = new OrderCityCommand(
            IssuingNationId: "rome",
            CityId: "mediolanum",
            OrderId: "fortify",
            Points: 15);

        var dispatcher = new CommandDispatcher(state, ruleset);
        state = dispatcher.Execute(orderCommand).State;

        var cityAfterOrder = state.CityById("mediolanum")!;
        Assert.True(FortificationCode.IsOrderInProgress(cityAfterOrder.FortificationCode, fortifyOrder));

        // Now clear via AfterSiegeAttempt (simulating what CityCaptureResolver does on capture)
        var clearedCode = FortificationCode.AfterSiegeAttempt(cityAfterOrder.FortificationCode, fortifyOrder);

        // Should be back to 0% (no finished fortification)
        Assert.Equal(0, clearedCode);
        Assert.False(FortificationCode.IsOrderInProgress(clearedCode, fortifyOrder));
    }

    [Fact]
    public void Siege_preserves_finished_fortification()
    {
        var ruleset = _factory.CreateRuleset();
        var fortifyOrder = ruleset.CityOrders.Orders.Single();

        // City at 75% with 5 points pending: code = 75 + (5 × 100) = 575
        var codeWithOrder = 75 + (5 * 100);
        var clearedCode = FortificationCode.AfterSiegeAttempt(codeWithOrder, fortifyOrder);

        // Should be back to 75% (preserves finished, clears pending)
        Assert.Equal(75, clearedCode);
    }

    // ---- DoD 4: Refusal at 100% and while under siege ----

    [Fact]
    public void Fortify_refused_at_maximum_percent()
    {
        var state = _factory.CreateState();
        var ruleset = _factory.CreateRuleset();

        // Get a city and fortify it to 100%
        var city = state.CityById("mediolanum")!;
        var fortifyOrder = ruleset.CityOrders.Orders.Single();
        var maxFortifiedCity = city with { FortificationCode = fortifyOrder.MaxPercent };
        var cities = state.Cities.ToList();
        cities[cities.FindIndex(c => c.Id == "mediolanum")] = maxFortifiedCity;
        state = state with { Cities = ValueList.From(cities) };

        // Try to fortify further
        var command = new OrderCityCommand(
            IssuingNationId: "rome",
            CityId: "mediolanum",
            OrderId: "fortify",
            Points: 1);

        var dispatcher = new CommandDispatcher(state, ruleset);
        var result = dispatcher.Execute(command);

        Assert.False(result.IsAccepted);
        Assert.Equal(CityOrderRejections.FortifyRefusedAtMaxPercent, result.Rejection!.Code);
    }

    [Fact]
    public void Fortify_refused_while_under_siege()
    {
        var state = _factory.CreateState();
        var ruleset = _factory.CreateRuleset();

        // Mark city as under siege
        var city = state.CityById("mediolanum")!;
        var besiegedCity = city with { UnderSiege = true };
        var cities = state.Cities.ToList();
        cities[cities.FindIndex(c => c.Id == "mediolanum")] = besiegedCity;
        state = state with { Cities = ValueList.From(cities) };

        // Try to fortify
        var command = new OrderCityCommand(
            IssuingNationId: "rome",
            CityId: "mediolanum",
            OrderId: "fortify",
            Points: 5);

        var dispatcher = new CommandDispatcher(state, ruleset);
        var result = dispatcher.Execute(command);

        Assert.False(result.IsAccepted);
        Assert.Equal(CityOrderRejections.FortifyRefusedWhileUnderSiege, result.Rejection!.Code);
    }

    [Fact]
    public void Fortify_refused_when_order_already_pending()
    {
        var state = _factory.CreateState();
        var ruleset = _factory.CreateRuleset();

        // Place first order
        var command1 = new OrderCityCommand(
            IssuingNationId: "rome",
            CityId: "mediolanum",
            OrderId: "fortify",
            Points: 5);

        var dispatcher = new CommandDispatcher(state, ruleset);
        var result1 = dispatcher.Execute(command1);
        Assert.True(result1.IsAccepted);

        // Try to place second order while first is pending
        var command2 = new OrderCityCommand(
            IssuingNationId: "rome",
            CityId: "mediolanum",
            OrderId: "fortify",
            Points: 3);

        var result2 = dispatcher.Execute(command2, result1.State);
        Assert.False(result2.IsAccepted);
        Assert.Equal(CityOrderRejections.FortifyRefusedOrderPending, result2.Rejection!.Code);
    }

    [Fact]
    public void Fortify_refused_when_insufficient_treasury()
    {
        var state = _factory.CreateState();
        var ruleset = _factory.CreateRuleset();

        // Get Rome's treasury
        var rome = state.NationById("rome")!;
        var mediolanum = state.CityById("mediolanum")!;

        // Calculate the cost: population × points
        var populationThousands = mediolanum.PopulationThousands;
        var costPerPoint = ruleset.CityOrders.Orders.Single().CostPerPointPerPopulationThousand;

        // Order enough points to exceed the treasury
        var pointsNeeded = (rome.Treasury / (populationThousands * costPerPoint)) + 100;

        // Deplete the treasury
        var povertySrickenRome = rome with { Treasury = 0 };
        var nations = state.Nations.ToList();
        nations[nations.FindIndex(n => n.Id == "rome")] = povertySrickenRome;
        state = state with { Nations = ValueList.From(nations) };

        var command = new OrderCityCommand(
            IssuingNationId: "rome",
            CityId: "mediolanum",
            OrderId: "fortify",
            Points: 1);

        var dispatcher = new CommandDispatcher(state, ruleset);
        var result = dispatcher.Execute(command);

        Assert.False(result.IsAccepted);
        Assert.Equal(CityOrderRejections.InsufficientTreasury, result.Rejection!.Code);
    }

    [Fact]
    public void Fortify_order_places_only_when_nation_owns_city()
    {
        var state = _factory.CreateState();
        var ruleset = _factory.CreateRuleset();

        // Try to fortify a city owned by another nation
        var command = new OrderCityCommand(
            IssuingNationId: "rome",
            CityId: "carthago",  // Carthago is owned by carthage, not rome
            OrderId: "fortify",
            Points: 5);

        var dispatcher = new CommandDispatcher(state, ruleset);
        var result = dispatcher.Execute(command);

        Assert.False(result.IsAccepted);
        Assert.Equal(CityOrderRejections.NotCityOwner, result.Rejection!.Code);
    }

    // ---- Additional: both refusals need their own test ----

    [Fact]
    public void Fortify_refusal_at_max_percent_returns_no_state_change()
    {
        var state = _factory.CreateState();
        var ruleset = _factory.CreateRuleset();

        var fortifyOrder = ruleset.CityOrders.Orders.Single();
        var city = state.CityById("mediolanum")!;
        var maxFortifiedCity = city with { FortificationCode = fortifyOrder.MaxPercent };
        var cities = state.Cities.ToList();
        cities[cities.FindIndex(c => c.Id == "mediolanum")] = maxFortifiedCity;
        var modifiedState = state with { Cities = ValueList.From(cities) };

        var command = new OrderCityCommand(
            IssuingNationId: "rome",
            CityId: "mediolanum",
            OrderId: "fortify",
            Points: 1);

        var dispatcher = new CommandDispatcher(modifiedState, ruleset);
        var result = dispatcher.Execute(command);

        Assert.False(result.IsAccepted);
        // Assert no state change (same reference for the state)
        Assert.Same(modifiedState, result.State);
    }

    [Fact]
    public void Fortify_refusal_under_siege_returns_no_state_change()
    {
        var state = _factory.CreateState();
        var ruleset = _factory.CreateRuleset();

        var city = state.CityById("mediolanum")!;
        var besiegedCity = city with { UnderSiege = true };
        var cities = state.Cities.ToList();
        cities[cities.FindIndex(c => c.Id == "mediolanum")] = besiegedCity;
        var besiegedState = state with { Cities = ValueList.From(cities) };

        var command = new OrderCityCommand(
            IssuingNationId: "rome",
            CityId: "mediolanum",
            OrderId: "fortify",
            Points: 5);

        var dispatcher = new CommandDispatcher(besiegedState, ruleset);
        var result = dispatcher.Execute(command);

        Assert.False(result.IsAccepted);
        Assert.Same(besiegedState, result.State);
    }

    // ---- Additional: FortificationCode utility tests ----

    [Fact]
    public void FortificationCode_CanPlaceOrder_respects_maximum()
    {
        var ruleset = _factory.CreateRuleset();
        var fortifyOrder = ruleset.CityOrders.Orders.Single();

        // At maximum, cannot place order
        Assert.False(FortificationCode.CanPlaceOrder(100, fortifyOrder));

        // Below maximum, can place order
        Assert.True(FortificationCode.CanPlaceOrder(99, fortifyOrder));
    }

    [Fact]
    public void FortificationCode_CanPlaceOrder_respects_pending_order()
    {
        var ruleset = _factory.CreateRuleset();
        var fortifyOrder = ruleset.CityOrders.Orders.Single();

        // With pending order (code > 100), cannot place another
        Assert.False(FortificationCode.CanPlaceOrder(150, fortifyOrder));

        // No pending order, not at max, can place order
        Assert.True(FortificationCode.CanPlaceOrder(75, fortifyOrder));
    }

    [Fact]
    public void FortificationCode_MaxOrderablePoints_reflects_available_space()
    {
        var ruleset = _factory.CreateRuleset();
        var fortifyOrder = ruleset.CityOrders.Orders.Single();

        // At 75%, can order up to 25 more (max 100 - current 75)
        Assert.Equal(25, FortificationCode.MaxOrderablePoints(75, fortifyOrder));

        // At maximum, no points available
        Assert.Equal(0, FortificationCode.MaxOrderablePoints(100, fortifyOrder));

        // With pending order, no points available
        Assert.Equal(0, FortificationCode.MaxOrderablePoints(150, fortifyOrder));
    }
}
