using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Naval;
using IC2.Engine.Naval.Commands;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T14 Naval", Done-when 1 and 2: a 10-ship order costs 100 talents, has
/// capacity 5,000 troops and quarterly upkeep 30, starts under construction, and launches after exactly
/// 24 ticks (the ruleset's own <see cref="NavalRules.ConstructionTicks"/>, decremented by
/// <see cref="NavalRules.ConstructionTickStep"/> = 2 per turn, i.e. twelve <see cref="FleetTickSystem"/>
/// runs — <c>docs/investigations/thracia-supply-morale.md</c> and
/// <c>decompiled-unit-map-orders-and-record-fields.md</c> both confirm the −2-per-turn decrement).
/// </summary>
public sealed class FleetOrderAndConstructionTests
{
    [Fact]
    public void Order_TenShips_CostsCapacityAndUpkeepMatchTheConfirmedFigures()
    {
        var ruleset = NavalTestbed.Ruleset;
        const int ships = 10;

        Assert.Equal(100, ships * ruleset.Naval.BuildCostPerShip);
        Assert.Equal(5000, ships * ruleset.Naval.TransportTroopsPerShip);
        Assert.Equal(30, ShipUpkeep.Compute(ships, ruleset));
    }

    [Fact]
    public void OrderFleetCommand_AtACoastalCity_DebitsTreasuryAndStartsUnderConstruction()
    {
        var state = NavalTestbed.InitialState();
        var nation = state.Nations[0]; // "north"
        var arx = state.CityById("arx")!; // coastal, owned by north
        Assert.Equal(nation.Id, arx.Owner);

        var treasuryBefore = nation.Treasury;
        var dispatcher = NavalTestbed.RealEngineDispatcher();

        var result = dispatcher.Dispatch(
            state, new OrderFleetCommand(nation.Id, arx.Id, Ships: 10, NewFleetId: "test-new-fleet"));

        Assert.True(result.IsAccepted, result.ToString());
        var fleet = result.State.FleetById("test-new-fleet");
        Assert.NotNull(fleet);
        Assert.True(fleet!.IsUnderConstruction);
        Assert.Equal(NavalTestbed.Ruleset.Naval.ConstructionTicks, fleet.ConstructionTicksRemaining);
        Assert.Equal(10, fleet.Ships);

        var updatedNation = result.State.NationById(nation.Id)!;
        Assert.Equal(treasuryBefore - 100, updatedNation.Treasury);
    }

    [Fact]
    public void OrderFleetCommand_AtANonCoastalCity_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var nation = state.Nations[0]; // "north"
        var portus = state.CityById("portus")!; // owned by north, not coastal in the toy grid
        Assert.False(CoastalCity.IsCoastal(portus, NavalTestbed.Toy.World));

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(
            state, new OrderFleetCommand(nation.Id, portus.Id, Ships: 10, NewFleetId: "test-new-fleet-2"));

        Assert.True(result.IsRejected);
        Assert.Equal(OrderFleetRejections.CityNotCoastal, result.Code);
    }

    [Fact]
    public void OrderFleetCommand_ShipsOutsideTheOrderRange_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var nation = state.Nations[0];
        var arx = state.CityById("arx")!;
        var dispatcher = NavalTestbed.RealEngineDispatcher();

        var tooFew = dispatcher.Dispatch(state, new OrderFleetCommand(nation.Id, arx.Id, Ships: 9, NewFleetId: "x1"));
        Assert.Equal(OrderFleetRejections.ShipsOutOfRange, tooFew.Code);

        var tooMany = dispatcher.Dispatch(state, new OrderFleetCommand(nation.Id, arx.Id, Ships: 101, NewFleetId: "x2"));
        Assert.Equal(OrderFleetRejections.ShipsOutOfRange, tooMany.Code);
    }

    [Fact]
    public void ConstructionCountdown_LaunchesAfterExactlyTwelveTicksOfTheConfirmedMinusTwoStep()
    {
        var ruleset = NavalTestbed.Ruleset;
        var rules = ruleset.Naval;
        Assert.Equal(24, rules.ConstructionTicks);
        Assert.Equal(2, rules.ConstructionTickStep);

        var state = NavalTestbed.InitialState();
        var nation = state.Nations[0];
        var arx = state.CityById("arx")!;
        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var order = dispatcher.Dispatch(state, new OrderFleetCommand(nation.Id, arx.Id, Ships: 10, NewFleetId: "launch-test"));
        Assert.True(order.IsAccepted, order.ToString());

        var coordinator = NavalTestbed.CoordinatorOnly(sink: null, typeof(FleetTickSystem));
        var current = order.State;

        var expectedTicks = new[] { 22, 20, 18, 16, 14, 12, 10, 8, 6, 4, 2, 0 };
        for (var turn = 0; turn < 12; turn++)
        {
            current = coordinator.RunRoundTick(current).State;
            var fleet = current.FleetById("launch-test")!;

            if (turn < 11)
            {
                Assert.True(fleet.IsUnderConstruction, $"turn {turn}: expected still under construction.");
                Assert.Equal(expectedTicks[turn], fleet.ConstructionTicksRemaining);
            }
            else
            {
                // Turn index 11 is the twelfth run: 24 - 12*2 = 0, so this is the launch turn.
                Assert.False(fleet.IsUnderConstruction, "expected the fleet to have launched by the twelfth tick.");
                Assert.Equal(rules.LaunchConditionPercent, fleet.ConditionPercent);
                Assert.Equal(rules.LaunchSupplyTons, fleet.SupplyTons);
                Assert.Equal(0, fleet.Money);
            }
        }
    }

    [Fact]
    public void ConstructionCountdown_LaunchEmitsTheConfirmedFleetFinishedMessage()
    {
        var state = NavalTestbed.InitialState();
        var nation = state.Nations[0];
        var arx = state.CityById("arx")!;
        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var order = dispatcher.Dispatch(state, new OrderFleetCommand(nation.Id, arx.Id, Ships: 10, NewFleetId: "launch-news"));

        var sink = new RecordingEventSink();
        var coordinator = NavalTestbed.CoordinatorOnly(sink, typeof(FleetTickSystem));
        var current = order.State;
        for (var turn = 0; turn < 12; turn++)
        {
            current = coordinator.RunRoundTick(current).State;
        }

        var finished = Assert.Single(sink.Events.OfType<FleetFinished>());
        Assert.Equal(nation.Id, finished.Nation);
        Assert.Equal(arx.Name, finished.CityName);
    }
}
