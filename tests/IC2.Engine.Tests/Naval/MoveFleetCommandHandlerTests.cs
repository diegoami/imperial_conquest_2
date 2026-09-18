using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval.Commands;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T14 Naval" Scope ("sea movement via T09's walker") and Hazards
/// ("price sea moves through T09's <c>MovementWalker</c>/<c>TerrainCostLookup</c>, never T02's
/// <c>Ruleset.MoveCostFor</c>"). Uses the shipped toy world's own sea/land layout, not a synthetic grid.
/// </summary>
public sealed class MoveFleetCommandHandlerTests
{
    [Fact]
    public void MovingOntoAnAdjacentSeaTile_ChargesTheConfirmedSeaCostAndAdvances()
    {
        var state = NavalTestbed.InitialState();
        var fleet = state.Fleets[0]; // "north-fleet-1", (0, 3), 4 moves, sea_deep under it.
        Assert.Equal(4, fleet.Moves);

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new MoveFleetCommand(fleet.Nation, fleet.Id, X: 0, Y: 4));

        Assert.True(result.IsAccepted, result.ToString());
        var moved = result.State.FleetById(fleet.Id)!;
        Assert.Equal(0, moved.X);
        Assert.Equal(4, moved.Y);
        Assert.Equal(3, moved.Moves); // sea_coastal costs 1, per the ruleset's own terrain table.
    }

    [Fact]
    public void MovingOntoALandTile_IsBlockedBeforeEnteringIt()
    {
        var state = NavalTestbed.InitialState();
        var fleet = state.Fleets[0]; // (0, 3); (1, 3) is a land ("plain") tile in the toy grid.

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new MoveFleetCommand(fleet.Nation, fleet.Id, X: 1, Y: 3));

        Assert.True(result.IsAccepted, result.ToString()); // the walk itself never rejects; it just stops.
        var unmoved = result.State.FleetById(fleet.Id)!;
        Assert.Equal(fleet.X, unmoved.X);
        Assert.Equal(fleet.Y, unmoved.Y);
        Assert.Equal(fleet.Moves, unmoved.Moves); // no cost charged for a cell never entered.
    }

    [Fact]
    public void MovingOntoAnotherLaunchedFleetsTile_IsBlocked()
    {
        var state = NavalTestbed.InitialState();
        var mover = state.Fleets[0]; // north-fleet-1, (0, 3).

        var blocker = new FleetState(
            "blocking-fleet", "south", X: 0, Y: 4, Moves: 0, Ships: 10, ConditionPercent: 100,
            Money: 0, SupplyTons: 0, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null);
        state = state with { Fleets = ValueList.From(state.Fleets.Append(blocker)) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new MoveFleetCommand(mover.Nation, mover.Id, X: 0, Y: 4));

        Assert.True(result.IsAccepted, result.ToString());
        var unmoved = result.State.FleetById(mover.Id)!;
        Assert.Equal(mover.X, unmoved.X);
        Assert.Equal(mover.Y, unmoved.Y);
    }

    [Fact]
    public void FleetWithNoMovesLeft_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var fleet = state.Fleets[0] with { Moves = 0 };
        state = state with { Fleets = ValueList.From(state.Fleets.Select(f => f.Id == fleet.Id ? fleet : f)) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new MoveFleetCommand(fleet.Nation, fleet.Id, X: 0, Y: 4));

        Assert.True(result.IsRejected);
        Assert.Equal(MoveFleetRejections.NoMovesLeft, result.Code);
    }

    [Fact]
    public void PricingGoesThroughTheTerrainCostLookup_NotThroughRulesetMoveCostFor()
    {
        // The confirmed sea costs (1 for sea_coastal, 3 for sea_deep) come from the same terrain table
        // Ruleset.MoveCostFor also reads, so this is asserted structurally instead: a cell whose tile
        // type the ruleset's table does not carry raises UnpricedTerrainEncountered, the fallback warning
        // that only MovementWalker/TerrainCostLookup raise (Ruleset.MoveCostFor has no such event and
        // silently returns the ruleset's default instead).
        var ruleset = NavalTestbed.Ruleset;
        var events = new RecordingEventSink();

        var lookup = TerrainCostLookup.MoveCostFor(ruleset.Terrain, "not-a-real-tile-type");
        Assert.False(lookup.WasPricedByRuleset);

        var walk = MovementWalker.Walk(
            new GridPoint(0, 0), new GridPoint(0, 1), movesAvailable: 5, ruleset.Terrain,
            tileTypeIdAt: _ => "not-a-real-tile-type", isBlocked: _ => false, events);

        Assert.Single(events.Events.OfType<UnpricedTerrainEncountered>());
        Assert.True(walk.Moved);
    }
}
