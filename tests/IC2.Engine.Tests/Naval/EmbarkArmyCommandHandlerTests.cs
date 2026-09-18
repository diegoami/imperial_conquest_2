using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval;
using IC2.Engine.Naval.Commands;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T14 Naval", Done-when 3 (rewritten 2026-09-18, commit <c>87bec66</c>):
/// exactly <c>ships × 500</c> troops is always accepted; above it, <c>classical-faithful</c> trims an AI
/// seat and refuses a human one, and <c>improved</c> refuses every seat. See
/// <see cref="EmbarkArmyCommand"/>'s remarks for the full history and evidence.
/// </summary>
public sealed class EmbarkArmyCommandHandlerTests
{
    private const int Ships = 10;
    private const int Capacity = Ships * 500; // 5,000

    /// <summary>
    /// Builds a state with one army and one co-located fleet, both belonging to <paramref name="control"/>'s
    /// seat ("north" is human, "south" is AI in the shipped toy scenario), with that seat active.
    /// </summary>
    private static GameState BuildState(
        int troops, SeatControl control, out string armyId, out string fleetId, out string nationId)
    {
        var state = NavalTestbed.InitialState();
        var seatIndex = control == SeatControl.Human ? 0 : 1; // "north" = human, "south" = ai.
        var nation = state.Nations[seatIndex];
        Assert.Equal(control, nation.Control); // guards the toy scenario's own seat assignment.
        nationId = nation.Id;

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
            Units: ValueList.Of(
                new UnitSlot(0, "light_infantry", troops / 2, 6, "Embark Test Battalion 1"),
                new UnitSlot(0, "archers", troops - (troops / 2), 7, "Embark Test Battalion 2")));

        var fleet = new FleetState(
            Id: fleetId,
            Nation: nationId,
            X: 3,
            Y: 3,
            Moves: 4,
            Ships: Ships,
            ConditionPercent: 100,
            Money: 0,
            SupplyTons: 50,
            ConstructionTicksRemaining: null,
            BuildCityId: null,
            CarriedArmyId: null,
            CoveredTileCode: null);

        return state with { Armies = ValueList.Of(army), Fleets = ValueList.Of(fleet), ActiveSeatIndex = seatIndex };
    }

    private static CommandDispatcher DispatcherFor(SeatAsymmetryModel seatAsymmetry)
    {
        var ruleset = NavalTestbed.Ruleset with { Flags = NavalTestbed.Ruleset.Flags with { SeatAsymmetry = seatAsymmetry } };
        return new CommandDispatcher(SystemRegistry.FromEngineAssembly(), ruleset, NavalTestbed.Toy.World, NullEventSink.Instance);
    }

    [Fact]
    public void Faithful_HumanSeat_OverCapacity_IsRefused()
    {
        var state = BuildState(Capacity + 1, SeatControl.Human, out var armyId, out var fleetId, out var nationId);
        var result = DispatcherFor(SeatAsymmetryModel.Faithful).Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsRejected);
        Assert.Equal(EmbarkArmyRejections.ArmyTooLarge, result.Code);
        Assert.Null(result.State.ArmyById(armyId)!.AboardFleetId);
        Assert.Null(result.State.FleetById(fleetId)!.CarriedArmyId);
    }

    [Fact]
    public void Faithful_AiSeat_OverCapacity_EmbarksTrimmedToExactlyCapacity()
    {
        var state = BuildState(Capacity + 1234, SeatControl.Ai, out var armyId, out var fleetId, out var nationId);
        var result = DispatcherFor(SeatAsymmetryModel.Faithful).Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsAccepted, result.ToString());
        var army = result.State.ArmyById(armyId)!;
        Assert.Equal(fleetId, army.AboardFleetId);
        Assert.Equal(Capacity, army.TotalTroops); // "trimmed to ships x 500" -- exactly, not merely at most.
        Assert.Equal(0, army.Moves);

        var fleet = result.State.FleetById(fleetId)!;
        Assert.Equal(armyId, fleet.CarriedArmyId);
        Assert.Equal(0, fleet.Moves);
    }

    [Fact]
    public void Normalized_HumanSeat_OverCapacity_IsRefused()
    {
        var state = BuildState(Capacity + 1, SeatControl.Human, out var armyId, out var fleetId, out var nationId);
        var result = DispatcherFor(SeatAsymmetryModel.Normalized).Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsRejected);
        Assert.Equal(EmbarkArmyRejections.ArmyTooLarge, result.Code);
    }

    [Fact]
    public void Normalized_AiSeat_OverCapacity_IsAlsoRefused()
    {
        // The opposite direction from T09's own seatAsymmetry case: "improved" here generalises
        // refusal to every seat, not the AI's trim -- see EmbarkArmyCommand's remarks for why.
        var state = BuildState(Capacity + 1234, SeatControl.Ai, out var armyId, out var fleetId, out var nationId);
        var result = DispatcherFor(SeatAsymmetryModel.Normalized).Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsRejected);
        Assert.Equal(EmbarkArmyRejections.ArmyTooLarge, result.Code);
        Assert.Null(result.State.ArmyById(armyId)!.AboardFleetId);
    }

    [Theory]
    [InlineData(SeatAsymmetryModel.Faithful, SeatControl.Human)]
    [InlineData(SeatAsymmetryModel.Faithful, SeatControl.Ai)]
    [InlineData(SeatAsymmetryModel.Normalized, SeatControl.Human)]
    [InlineData(SeatAsymmetryModel.Normalized, SeatControl.Ai)]
    public void ArmyExactlyAtCapacity_IsAcceptedUnchangedInEveryCase(SeatAsymmetryModel seatAsymmetry, SeatControl control)
    {
        var state = BuildState(Capacity, control, out var armyId, out var fleetId, out var nationId);
        var result = DispatcherFor(seatAsymmetry).Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsAccepted, result.ToString());
        var army = result.State.ArmyById(armyId)!;
        var fleet = result.State.FleetById(fleetId)!;
        Assert.Equal(fleetId, army.AboardFleetId);
        Assert.Equal(armyId, fleet.CarriedArmyId);
        Assert.Equal(Capacity, army.TotalTroops); // unchanged -- no trim needed exactly at capacity.
        Assert.Equal(0, army.Moves);
        Assert.Equal(0, fleet.Moves);
        Assert.Null(army.CoveredTileCode);
    }

    [Fact]
    public void ArmyTransportTrim_DistributesTheShortfallDeterministically()
    {
        // Direct unit test of the [derived] trim helper itself, independent of the command handler.
        var units = new[]
        {
            new UnitSlot(0, "light_infantry", 4000, 6, "A"),
            new UnitSlot(0, "archers", 3000, 7, "B"),
        };

        var trimmed = ArmyTransportTrim.TrimToCapacity(units, totalTroopsBeforeTrim: 7000, capacity: 5000);

        var totalAfter = trimmed.Sum(u => u.Troops);
        Assert.Equal(5000, totalAfter); // exact, never a few troops short from truncation.
        Assert.All(trimmed, u => Assert.True(u.Troops > 0));
    }
}
