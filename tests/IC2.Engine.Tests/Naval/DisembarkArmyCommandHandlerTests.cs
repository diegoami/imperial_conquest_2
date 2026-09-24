using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval.Commands;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T14 Naval" DoD 15: an embarked army's coordinates travel with the
/// carrying fleet while aboard, and a landing restores its covered cell from the aboard-fleet sentinel to
/// a real one, zeroing its moves.
/// </summary>
public sealed class DisembarkArmyCommandHandlerTests
{
    /// <summary>
    /// (0, 3) is a sea tile in the toy world's grid; (1, 3) is land ("plain"). Building the state
    /// directly with an already-embarked army/fleet pair there, rather than embarking first, isolates
    /// this command's own behaviour from <see cref="EmbarkArmyCommandHandler"/>'s.
    /// </summary>
    private static GameState BuildEmbarkedState(
        SeatControl control, out string armyId, out string fleetId, out string nationId)
    {
        var state = NavalTestbed.InitialState();
        var seatIndex = control == SeatControl.Human ? 0 : 1;
        var nation = state.Nations[seatIndex];
        Assert.Equal(control, nation.Control);
        nationId = nation.Id;

        armyId = "disembark-test-army";
        fleetId = "disembark-test-fleet";

        var fleet = new FleetState(
            fleetId, nationId, X: 0, Y: 3, Moves: 5, Ships: 10, ConditionPercent: 100,
            Money: 0, SupplyTons: 50, ConstructionTicksRemaining: null, BuildCityId: null,
            CarriedArmyId: armyId, CoveredTileCode: null);

        var army = new ArmyState(
            armyId, nationId, X: 0, Y: 3, Moves: 0, Morale: 60, Money: 0, SupplyTons: 10,
            CoveredTileCode: null, AboardFleetId: fleetId,
            Units: ValueList.Of(new UnitSlot(0, "light_infantry", 4000, 6, "Disembark Test Battalion")));

        return state with { Armies = ValueList.Of(army), Fleets = ValueList.Of(fleet), ActiveSeatIndex = seatIndex };
    }

    [Fact]
    public void MoveFleetCommand_CarriesTheEmbarkedArmysCoordinatesAlong()
    {
        var state = BuildEmbarkedState(SeatControl.Human, out var armyId, out var fleetId, out var nationId);

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new MoveFleetCommand(nationId, fleetId, X: 0, Y: 4));

        Assert.True(result.IsAccepted, result.ToString());
        var fleet = result.State.FleetById(fleetId)!;
        var army = result.State.ArmyById(armyId)!;
        Assert.Equal(0, fleet.X);
        Assert.Equal(4, fleet.Y);
        Assert.Equal(fleet.X, army.X);
        Assert.Equal(fleet.Y, army.Y); // the bug the first review found: this used to stay at (0, 3).
        Assert.Null(army.CoveredTileCode); // still off-map while aboard.
        Assert.Equal(fleetId, army.AboardFleetId);
    }

    [Fact]
    public void HumanSeat_DisembarksAtAnExplicitlyNamedAdjacentLandTile()
    {
        var state = BuildEmbarkedState(SeatControl.Human, out var armyId, out var fleetId, out var nationId);

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new DisembarkArmyCommand(nationId, armyId, X: 1, Y: 3));

        Assert.True(result.IsAccepted, result.ToString());
        var army = result.State.ArmyById(armyId)!;
        Assert.Null(army.AboardFleetId);
        Assert.Equal(1, army.X);
        Assert.Equal(3, army.Y);
        Assert.NotNull(army.CoveredTileCode); // army[+8] restored from the aboard-fleet sentinel.
        Assert.Equal(0, army.Moves); // army[+6] = 0.

        var fleet = result.State.FleetById(fleetId)!;
        Assert.Null(fleet.CarriedArmyId);
    }

    [Fact]
    public void HumanSeat_OmittingTheLandingTile_IsRefused()
    {
        var state = BuildEmbarkedState(SeatControl.Human, out var armyId, out _, out var nationId);

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new DisembarkArmyCommand(nationId, armyId, X: null, Y: null));

        Assert.True(result.IsRejected);
        Assert.Equal(DisembarkArmyRejections.LandingTileRequired, result.Code);
    }

    [Fact]
    public void HumanSeat_NamingANonAdjacentTile_IsRefused()
    {
        var state = BuildEmbarkedState(SeatControl.Human, out var armyId, out _, out var nationId);

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new DisembarkArmyCommand(nationId, armyId, X: 5, Y: 5));

        Assert.True(result.IsRejected);
        Assert.Equal(DisembarkArmyRejections.LandingTileTooFar, result.Code);
    }

    /// <summary>
    /// T70 review round 1, N1: a landing tile one tile beyond the shipped ruleset radius (distance 2 from
    /// the fleet's (0, 3)) is refused -- kills the reject-side boundary mutation at this site, which
    /// <see cref="HumanSeat_NamingANonAdjacentTile_IsRefused"/>'s distance-5 fixture does not. The
    /// accept-side boundary (distance exactly 1) is already pinned by
    /// <see cref="HumanSeat_DisembarksAtAnExplicitlyNamedAdjacentLandTile"/>'s (1, 3) fixture, so only the
    /// reject side needs a new case here.
    /// </summary>
    [Fact]
    public void HumanSeat_NamingATileOneBeyondTheRulesetRadius_IsRefused()
    {
        var state = BuildEmbarkedState(SeatControl.Human, out var armyId, out _, out var nationId);

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new DisembarkArmyCommand(nationId, armyId, X: 2, Y: 3)); // distance 2.

        Assert.True(result.IsRejected);
        Assert.Equal(DisembarkArmyRejections.LandingTileTooFar, result.Code);
    }

    [Fact]
    public void HumanSeat_NamingASeaTile_IsRefused()
    {
        var state = BuildEmbarkedState(SeatControl.Human, out var armyId, out var fleetId, out var nationId);

        // (0, 4) is adjacent to the fleet's (0, 3) but is itself sea, not land.
        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new DisembarkArmyCommand(nationId, armyId, X: 0, Y: 4));

        Assert.True(result.IsRejected);
        Assert.Equal(DisembarkArmyRejections.LandingTileNotPassable, result.Code);
    }

    [Fact]
    public void AiSeat_OmittingTheLandingTile_AutoPicksAPassableAdjacentTile()
    {
        var state = BuildEmbarkedState(SeatControl.Ai, out var armyId, out var fleetId, out var nationId);

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new DisembarkArmyCommand(nationId, armyId, X: null, Y: null));

        Assert.True(result.IsAccepted, result.ToString());
        var army = result.State.ArmyById(armyId)!;
        Assert.Null(army.AboardFleetId);
        Assert.NotNull(army.CoveredTileCode);
        // The fixed, deterministic scan (LandingTile.FirstAdjacentLandTile: the point itself, then its
        // eight neighbours row-major, north-west first) finds (1, 2) first: (-1, 2) is out of bounds,
        // (0, 2) is sea, (1, 2) is the toy grid's first land tile in that order.
        Assert.Equal(1, army.X);
        Assert.Equal(2, army.Y);
    }

    [Fact]
    public void ArmyNotEmbarked_IsRefused()
    {
        var state = NavalTestbed.InitialState();
        var nationId = state.Nations[0].Id;
        var army = new ArmyState(
            "not-embarked-army", nationId, X: 3, Y: 3, Moves: 5, Morale: 60, Money: 0, SupplyTons: 0,
            CoveredTileCode: 2, AboardFleetId: null,
            Units: ValueList.Of(new UnitSlot(0, "light_infantry", 1000, 6, "X")));
        state = state with { Armies = ValueList.Of(army) };

        var dispatcher = NavalTestbed.RealEngineDispatcher();
        var result = dispatcher.Dispatch(state, new DisembarkArmyCommand(nationId, army.Id, X: 3, Y: 3));

        Assert.True(result.IsRejected);
        Assert.Equal(DisembarkArmyRejections.ArmyNotEmbarked, result.Code);
    }
}
