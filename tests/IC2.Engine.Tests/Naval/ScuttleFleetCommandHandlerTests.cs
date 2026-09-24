using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval.Commands;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T14 Naval", Done-when 7: scuttle near an owned city returns money to
/// the treasury and supplies to that city.
/// </summary>
public sealed class ScuttleFleetCommandHandlerTests
{
    [Fact]
    public void ScuttleNearAnOwnedCity_ReturnsMoneyAndSuppliesAndRemovesTheFleet()
    {
        var state = NavalTestbed.InitialState();
        var nationId = state.Nations[0].Id; // "north"
        var arx = state.CityById("arx")!;

        var fleet = new FleetState(
            "scuttle-test", nationId, arx.X, arx.Y, Moves: 3, Ships: 15, ConditionPercent: 80,
            Money: 60, SupplyTons: 40, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null);
        state = state with { Fleets = ValueList.Of(fleet) };

        var treasuryBefore = state.NationById(nationId)!.Treasury;
        var citySupplyBefore = arx.SupplyTons;

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new ScuttleFleetCommand(nationId, fleet.Id));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Null(result.State.FleetById(fleet.Id));
        Assert.Equal(treasuryBefore + 60, result.State.NationById(nationId)!.Treasury);
        Assert.Equal(citySupplyBefore + 40, result.State.CityById(arx.Id)!.SupplyTons);
    }

    [Fact]
    public void ScuttleFarFromAnyOwnedCity_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var nationId = state.Nations[0].Id;

        var fleet = new FleetState(
            "scuttle-test-2", nationId, X: 0, Y: 5, Moves: 3, Ships: 15, ConditionPercent: 80,
            Money: 60, SupplyTons: 40, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null);
        state = state with { Fleets = ValueList.Of(fleet) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new ScuttleFleetCommand(nationId, fleet.Id));

        Assert.True(result.IsRejected);
        Assert.Equal(ScuttleFleetRejections.NotNearOwnedCity, result.Code);
    }

    /// <summary>
    /// T70 review round 1, N1: a fleet at exactly the shipped ruleset radius (distance 1) from an owned
    /// city can scuttle -- kills the accept-side boundary mutation at this site.
    /// </summary>
    [Fact]
    public void ScuttleAtExactlyTheRulesetRadius_IsAccepted()
    {
        var state = NavalTestbed.InitialState();
        var nationId = state.Nations[0].Id;
        var arx = state.CityById("arx")!; // (2, 1), owned by north.

        var fleet = new FleetState(
            "scuttle-boundary", nationId, X: arx.X - 1, Y: arx.Y, Moves: 3, Ships: 15, ConditionPercent: 80,
            Money: 60, SupplyTons: 40, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null); // distance 1.
        state = state with { Fleets = ValueList.Of(fleet) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new ScuttleFleetCommand(nationId, fleet.Id));

        Assert.True(result.IsAccepted, result.ToString());
    }

    /// <summary>
    /// T70 review round 1, N1: a fleet one tile beyond the shipped ruleset radius (distance 2) from every
    /// owned city is refused -- kills the reject-side boundary mutation at this site, which
    /// <see cref="ScuttleFarFromAnyOwnedCity_IsRefused"/>'s far-away fixture does not.
    /// </summary>
    [Fact]
    public void ScuttleOneTileBeyondTheRulesetRadius_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var nationId = state.Nations[0].Id;
        var arx = state.CityById("arx")!; // (2, 1), owned by north.

        var fleet = new FleetState(
            "scuttle-boundary-far", nationId, X: arx.X - 2, Y: arx.Y, Moves: 3, Ships: 15, ConditionPercent: 80,
            Money: 60, SupplyTons: 40, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: null, CoveredTileCode: null); // distance 2.
        state = state with { Fleets = ValueList.Of(fleet) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new ScuttleFleetCommand(nationId, fleet.Id));

        Assert.True(result.IsRejected);
        Assert.Equal(ScuttleFleetRejections.NotNearOwnedCity, result.Code);
    }
}
