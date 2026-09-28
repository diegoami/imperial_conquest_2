using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Persistence;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/tasks/T93.md</c> Done-when 3 (bug #453): a single army embarks from land, the fleet then moves
/// with it, and it disembarks onto a coastal tile — all three through the real commands, so the path no
/// play could ever take before this task (the army and the fleet could never share a tile) is exercised
/// end to end, not only per-handler. The final state also round-trips through <see cref="SaveManager"/>,
/// proving it validates and reloads.
/// </summary>
public sealed class EmbarkAndDisembarkEndToEndTests
{
    [Fact]
    public void ArmyEmbarksFromLand_FleetMoves_AndItDisembarksOntoACoastalTile()
    {
        var state = NavalTestbed.InitialState();
        var north = state.Nations[0];
        Assert.Equal("north", north.Id); // the toy scenario's own human seat.
        Assert.Equal(SeatControl.Human, north.Control);

        // The toy grid, from the world's own terrain runs: (1, 3) is plain and adjacent to (0, 3), sea;
        // the column (0, 2), (0, 1) is sea too, and (1, 1) is plain — a landing tile for the second step.
        var army = new ArmyState(
            Id: "e2e-army",
            Nation: north.Id,
            X: 1,
            Y: 3,
            Moves: 5,
            Morale: 60,
            Money: 0,
            SupplyTons: 10,
            CoveredTileCode: 2, // a real land cell while ashore, cleared on embark, restored on landing.
            AboardFleetId: null,
            Units: ValueList.Of(new UnitSlot(0, "light_infantry", 1000, 6, "E2E Battalion")));

        var fleet = state.Fleets[0]; // north-fleet-1, at (0, 3), 10 ships, 4 moves.
        Assert.Equal("north-fleet-1", fleet.Id);
        Assert.Equal(0, fleet.X);
        Assert.Equal(3, fleet.Y);

        state = state with { Armies = ValueList.Of(army), ActiveSeatIndex = 0 };

        var dispatcher = NavalTestbed.RealEngineDispatcher();

        // 1. Embark, through the real command: the army stands on land next door, not on the fleet's tile.
        var embarked = dispatcher.Dispatch(state, new EmbarkArmyCommand(north.Id, army.Id, fleet.Id));
        Assert.True(embarked.IsAccepted, embarked.ToString());
        state = embarked.State;
        Assert.Equal(fleet.Id, state.ArmyById(army.Id)!.AboardFleetId);
        Assert.Equal(fleet.X, state.ArmyById(army.Id)!.X); // moved onto the fleet's tile, per the user's rule.
        Assert.Equal(fleet.Y, state.ArmyById(army.Id)!.Y);

        // The original's embark zeroes the fleet's moves (FUN_0044B79C); a fresh turn would restore them.
        // They are restored here rather than running a whole turn of weather and attrition alongside the
        // move this test is about.
        state = state with
        {
            Fleets = ValueList.From(state.Fleets.Select(f => f.Id == fleet.Id ? f with { Moves = 4 } : f)),
        };

        // 2. Move the fleet up the sea column, through the real command: the carried army travels with it.
        var moved = dispatcher.Dispatch(state, new MoveFleetCommand(north.Id, fleet.Id, X: 0, Y: 1));
        Assert.True(moved.IsAccepted, moved.ToString());
        state = moved.State;
        Assert.Equal(0, state.FleetById(fleet.Id)!.X);
        Assert.Equal(1, state.FleetById(fleet.Id)!.Y);
        Assert.Equal(0, state.ArmyById(army.Id)!.X);
        Assert.Equal(1, state.ArmyById(army.Id)!.Y);

        // 3. Disembark onto (1, 1), plain and adjacent to the fleet's new (0, 1). A human seat names its
        //    own landing tile (DisembarkArmyCommandHandler's own gate), which is the form this uses.
        var landed = dispatcher.Dispatch(state, new DisembarkArmyCommand(north.Id, army.Id, X: 1, Y: 1));
        Assert.True(landed.IsAccepted, landed.ToString());
        state = landed.State;

        var disembarked = state.ArmyById(army.Id)!;
        Assert.Null(disembarked.AboardFleetId);
        Assert.Equal(1, disembarked.X);
        Assert.Equal(1, disembarked.Y);
        Assert.Equal(2, disembarked.CoveredTileCode); // the plain cell it landed on, restored from the sentinel.
        Assert.Equal(0, disembarked.Moves);
        Assert.Null(state.FleetById(fleet.Id)!.CarriedArmyId);

        // 4. The end state survives a SaveManager round trip; Load runs GameDataValidation.ValidateState.
        var save = new SaveGame(
            SchemaVersion: GameDataSchema.CurrentVersion,
            Id: "t93-e2e",
            Label: "T93 end to end",
            ScenarioId: state.ScenarioId,
            WorldId: state.WorldId,
            RulesetId: state.RulesetId,
            State: state);

        var reloaded = SaveManager.Load(
            "t93-e2e.json", SaveManager.Serialize(save), NavalTestbed.Toy.World, NavalTestbed.Toy.Ruleset);

        Assert.Equal(state, reloaded.State);
    }
}
