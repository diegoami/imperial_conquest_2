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
/// <c>docs/tasks/T93.md</c> (bug #453) adds the adjacency cases at the end of this file: the user's rule
/// of 2026-09-27 is that an army embarks from a tile adjacent to its fleet and then moves onto the
/// fleet's tile, "adjacent" being <c>Economy.CommandAdjacencyRadiusTiles</c> measured with
/// <c>LandingTile.ChebyshevDistance</c>.
/// </summary>
public sealed class EmbarkArmyCommandHandlerTests
{
    private const int Ships = 10;
    private const int Capacity = Ships * 500; // 5,000

    /// <summary>
    /// Builds a state with one army and one fleet, both belonging to <paramref name="control"/>'s seat
    /// ("north" is human, "south" is AI in the shipped toy scenario), with that seat active. Both start at
    /// (3, 3) — distance 0, within the radius — unless the T93 adjacency tests move them apart.
    /// </summary>
    private static GameState BuildState(
        int troops, SeatControl control, out string armyId, out string fleetId, out string nationId,
        int armyX = 3, int armyY = 3, int fleetX = 3, int fleetY = 3)
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
            X: armyX,
            Y: armyY,
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
            X: fleetX,
            Y: fleetY,
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

    /// <summary>
    /// A real dispatcher over the shipped toy world, with the seat-asymmetry model chosen and, when
    /// <paramref name="adjacencyRadius"/> is given, T70's radius overridden — the same override shape
    /// <c>Economy.Commands.BuySupplyCommandHandlerTests</c> uses to prove a distance check reads the
    /// ruleset rather than a literal.
    /// </summary>
    private static CommandDispatcher DispatcherFor(SeatAsymmetryModel seatAsymmetry, int? adjacencyRadius = null)
    {
        var ruleset = NavalTestbed.Ruleset with { Flags = NavalTestbed.Ruleset.Flags with { SeatAsymmetry = seatAsymmetry } };
        if (adjacencyRadius is { } radius)
        {
            ruleset = ruleset with { Economy = ruleset.Economy with { CommandAdjacencyRadiusTiles = radius } };
        }

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

    // ---- T93 (bug #453): an army embarks from a tile adjacent to its fleet ----

    /// <summary>
    /// T93 DoD 1's accept side: an army exactly <c>Economy.CommandAdjacencyRadiusTiles</c> from a
    /// friendly fleet embarks and ends on the fleet's tile — the user's rule of 2026-09-27 (bug #453).
    /// The shipped toy ruleset's radius is read from the fixture, not assumed: 1, so army (2, 3) against
    /// fleet (3, 3) is the boundary.
    /// </summary>
    [Fact]
    public void ArmyAtTheRulesetRadius_EmbarksAndEndsOnTheFleetsTile()
    {
        Assert.Equal(1, NavalTestbed.Ruleset.Economy.CommandAdjacencyRadiusTiles);
        var state = BuildState(
            Capacity, SeatControl.Human, out var armyId, out var fleetId, out var nationId,
            armyX: 2, armyY: 3, fleetX: 3, fleetY: 3);

        var result = DispatcherFor(SeatAsymmetryModel.Faithful)
            .Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsAccepted, result.ToString());
        var army = result.State.ArmyById(armyId)!;
        var fleet = result.State.FleetById(fleetId)!;
        Assert.Equal(3, army.X); // the handler moves the army onto the fleet, per the user's rule.
        Assert.Equal(3, army.Y);
        Assert.Equal(fleetId, army.AboardFleetId);
        Assert.Equal(armyId, fleet.CarriedArmyId);
        Assert.Equal(0, army.Moves);
        Assert.Equal(0, fleet.Moves);
    }

    /// <summary>
    /// "Adjacent" is Chebyshev distance, the metric disembark already uses: diagonal (2, 2) is distance 1
    /// from (3, 3), where Manhattan distance would be 2 and would wrongly refuse it.
    /// </summary>
    [Fact]
    public void ArmyDiagonallyAdjacent_Embarks()
    {
        var state = BuildState(
            Capacity, SeatControl.Human, out var armyId, out var fleetId, out var nationId,
            armyX: 2, armyY: 2, fleetX: 3, fleetY: 3);

        var result = DispatcherFor(SeatAsymmetryModel.Faithful)
            .Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(fleetId, result.State.ArmyById(armyId)!.AboardFleetId);
    }

    /// <summary>
    /// Distance 0 (the previous rule's own accepted case) is within the radius and stays accepted — not
    /// turned into a special case that only an adjacent-and-not-same tile can pass.
    /// </summary>
    [Fact]
    public void ArmyOnTheFleetsTile_StillEmbarks()
    {
        var state = BuildState(Capacity, SeatControl.Human, out var armyId, out var fleetId, out var nationId);

        var result = DispatcherFor(SeatAsymmetryModel.Faithful)
            .Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(fleetId, result.State.ArmyById(armyId)!.AboardFleetId);
    }

    /// <summary>
    /// T93 DoD 1's reject side: Chebyshev distance 2 is radius + 1 against the shipped radius of 1. The
    /// refused command leaves both link fields unwritten and neither position moved.
    /// </summary>
    [Fact]
    public void ArmyOneBeyondTheRulesetRadius_IsRejectedAndChangesNothing()
    {
        var state = BuildState(
            Capacity, SeatControl.Human, out var armyId, out var fleetId, out var nationId,
            armyX: 1, armyY: 3, fleetX: 3, fleetY: 3);

        var result = DispatcherFor(SeatAsymmetryModel.Faithful)
            .Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsRejected);
        Assert.Equal(EmbarkArmyRejections.NotAdjacent, result.Code);
        var army = result.State.ArmyById(armyId)!;
        Assert.Null(army.AboardFleetId);
        Assert.Equal(1, army.X); // a refused embark does not snap the army onto the fleet.
        Assert.Equal(3, army.Y);
        Assert.Null(result.State.FleetById(fleetId)!.CarriedArmyId);
    }

    /// <summary>
    /// The radius is ruleset data, not the shipped 1: at radius 2 (T70's own field overridden), distance 2
    /// is accepted instead of refused.
    /// </summary>
    [Fact]
    public void RulesetRadiusTwo_TwoTilesAway_Embarks()
    {
        var state = BuildState(
            Capacity, SeatControl.Human, out var armyId, out var fleetId, out var nationId,
            armyX: 1, armyY: 3, fleetX: 3, fleetY: 3); // distance 2.

        var result = DispatcherFor(SeatAsymmetryModel.Faithful, adjacencyRadius: 2)
            .Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(fleetId, result.State.ArmyById(armyId)!.AboardFleetId);
    }

    /// <summary>At that same radius 2, distance 3 (radius + 1) is the reject side.</summary>
    [Fact]
    public void RulesetRadiusTwo_ThreeTilesAway_IsRejected()
    {
        var state = BuildState(
            Capacity, SeatControl.Human, out var armyId, out var fleetId, out var nationId,
            armyX: 0, armyY: 3, fleetX: 3, fleetY: 3); // distance 3.

        var result = DispatcherFor(SeatAsymmetryModel.Faithful, adjacencyRadius: 2)
            .Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsRejected);
        Assert.Equal(EmbarkArmyRejections.NotAdjacent, result.Code);
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
