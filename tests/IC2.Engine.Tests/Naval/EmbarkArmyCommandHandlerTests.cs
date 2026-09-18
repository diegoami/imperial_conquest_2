using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval.Commands;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T14 Naval", Done-when 3: an army of more than <c>ships × 500</c>
/// troops is refused embarkation with a typed rejection under both rulesets; exactly <c>ships × 500</c>
/// is accepted. See <see cref="EmbarkArmyCommand"/>'s remarks for why no seat-asymmetry-gated trim is
/// implemented.
/// </summary>
public sealed class EmbarkArmyCommandHandlerTests
{
    private static GameState BuildStateWithArmyAndFleet(int troops, int ships, out string armyId, out string fleetId, out string nationId)
    {
        var state = NavalTestbed.InitialState();
        nationId = state.Nations[0].Id; // "north"
        armyId = "embark-test-army";
        fleetId = "embark-test-fleet";

        var army = new ArmyState(
            Id: armyId,
            Nation: nationId,
            X: 3,
            Y: 3,
            Moves: 5,
            Morale: 60,
            Money: 0,
            SupplyTons: 10,
            CoveredTileCode: 2,
            AboardFleetId: null,
            Units: ValueList.Of(new UnitSlot(0, "light_infantry", troops, 6, "Embark Test Battalion")));

        var fleet = new FleetState(
            Id: fleetId,
            Nation: nationId,
            X: 3,
            Y: 3,
            Moves: 4,
            Ships: ships,
            ConditionPercent: 100,
            Money: 0,
            SupplyTons: 50,
            ConstructionTicksRemaining: null,
            BuildCityId: null,
            CarriedArmyId: null,
            CoveredTileCode: null);

        return state with { Armies = ValueList.Of(army), Fleets = ValueList.Of(fleet) };
    }

    [Theory]
    [InlineData(SeatAsymmetryModel.Faithful)]
    [InlineData(SeatAsymmetryModel.Normalized)]
    public void ArmyOverCapacity_IsRefusedUnderBothRulesets(SeatAsymmetryModel seatAsymmetry)
    {
        const int ships = 10;
        const int capacity = ships * 500; // 5,000
        var state = BuildStateWithArmyAndFleet(troops: capacity + 1, ships: ships, out var armyId, out var fleetId, out var nationId);

        var ruleset = NavalTestbed.Ruleset with { Flags = NavalTestbed.Ruleset.Flags with { SeatAsymmetry = seatAsymmetry } };
        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), ruleset, NavalTestbed.Toy.World, NullEventSink.Instance);

        var result = dispatcher.Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsRejected);
        Assert.Equal(EmbarkArmyRejections.ArmyTooLarge, result.Code);
        // Nothing changed: still not aboard, ship count and fleet's carried-army link both untouched.
        Assert.Null(result.State.ArmyById(armyId)!.AboardFleetId);
        Assert.Null(result.State.FleetById(fleetId)!.CarriedArmyId);
    }

    [Theory]
    [InlineData(SeatAsymmetryModel.Faithful)]
    [InlineData(SeatAsymmetryModel.Normalized)]
    public void ArmyExactlyAtCapacity_IsAcceptedUnderBothRulesets(SeatAsymmetryModel seatAsymmetry)
    {
        const int ships = 10;
        const int capacity = ships * 500; // 5,000
        var state = BuildStateWithArmyAndFleet(troops: capacity, ships: ships, out var armyId, out var fleetId, out var nationId);

        var ruleset = NavalTestbed.Ruleset with { Flags = NavalTestbed.Ruleset.Flags with { SeatAsymmetry = seatAsymmetry } };
        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), ruleset, NavalTestbed.Toy.World, NullEventSink.Instance);

        var result = dispatcher.Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsAccepted, result.ToString());
        var army = result.State.ArmyById(armyId)!;
        var fleet = result.State.FleetById(fleetId)!;
        Assert.Equal(fleetId, army.AboardFleetId);
        Assert.Equal(armyId, fleet.CarriedArmyId);
        Assert.Equal(0, army.Moves);
        Assert.Equal(0, fleet.Moves);
        Assert.Null(army.CoveredTileCode);
    }
}
