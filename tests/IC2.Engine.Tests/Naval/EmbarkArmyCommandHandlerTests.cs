using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Armies;
using IC2.Engine.Movement;
using IC2.Engine.Naval;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Persistence;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// <c>docs/task-catalogue.md</c> "T14 Naval", Done-when 3 (rewritten 2026-09-18, commit <c>87bec66</c>):
/// exactly <c>ships × 500</c> troops is always accepted; above it, <c>classical-faithful</c> moves whole overflow slots ashore for an AI
/// seat; T221 pins the human truncated-division boundary separately. <c>improved</c> refuses every seat above exact capacity. See
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
    private static CommandDispatcher DispatcherFor(SeatAsymmetryModel seatAsymmetry,
        int? adjacencyRadius = null, int? maxArmies = null, int? transportDivisor = null)
    {
        var ruleset = NavalTestbed.Ruleset with { Flags = NavalTestbed.Ruleset.Flags with { SeatAsymmetry = seatAsymmetry } };
        if (adjacencyRadius is { } radius)
        {
            ruleset = ruleset with { Economy = ruleset.Economy with { CommandAdjacencyRadiusTiles = radius } };
        }
        if (maxArmies is { } limit)
            ruleset = ruleset with { ArmyManagement = ruleset.ArmyManagement with { MaxArmies = limit } };
        if (transportDivisor is { } divisor)
            ruleset = ruleset with { Naval = ruleset.Naval with { TransportTroopsPerShip = divisor } };

        return new CommandDispatcher(SystemRegistry.FromEngineAssembly(), ruleset, NavalTestbed.Toy.World, NullEventSink.Instance);
    }

    [Fact]
    public void Faithful_HumanSeat_OverCapacity_IsRefused()
    {
        var state = BuildState(Capacity + 500, SeatControl.Human, out var armyId, out var fleetId, out var nationId);
        var result = DispatcherFor(SeatAsymmetryModel.Faithful).Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsRejected);
        Assert.Equal(EmbarkArmyRejections.ArmyTooLarge, result.Code);
        Assert.Null(result.State.ArmyById(armyId)!.AboardFleetId);
        Assert.Null(result.State.FleetById(fleetId)!.CarriedArmyId);
    }

    [Theory]
    [InlineData(5000, false, 500)]
    [InlineData(5001, false, 500)]
    [InlineData(5499, false, 500)]
    [InlineData(5500, true, 500)]
    [InlineData(10999, false, 1000)]
    [InlineData(11000, true, 1000)]
    public void T221_Faithful_human_embark_truncates_before_comparing_ships(int troops, bool refused, int divisor)
    {
        var state = BuildState(troops, SeatControl.Human, out var armyId, out var fleetId, out var nationId);
        var result = DispatcherFor(SeatAsymmetryModel.Faithful, transportDivisor: divisor).Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));
        Assert.Equal(refused, result.IsRejected);
        if (refused) Assert.Same(state, result.State);
        else
        {
            var army = result.State.ArmyById(armyId)!;
            var fleet = result.State.FleetById(fleetId)!;
            Assert.Equal(troops, army.TotalTroops);
            Assert.Equal(fleetId, army.AboardFleetId);
            Assert.Equal(armyId, fleet.CarriedArmyId);
            Assert.Equal(0, army.Moves);
            Assert.Equal(0, fleet.Moves);
        }
    }

    [Fact]
    public void Faithful_AiSeat_OverCapacity_Embarks_only_whole_fitting_slots()
    {
        var state = BuildState(Capacity + 1234, SeatControl.Ai, out var armyId, out var fleetId, out var nationId);
        var result = DispatcherFor(SeatAsymmetryModel.Faithful).Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsAccepted, result.ToString());
        var army = result.State.ArmyById(armyId)!;
        Assert.Equal(fleetId, army.AboardFleetId);
        Assert.True(army.TotalTroops <= Capacity);
        Assert.Equal(Capacity + 1234, result.State.Armies.Sum(a => a.TotalTroops));
        Assert.Equal(2, result.State.Armies.Count);
        Assert.Equal(0, army.Moves);

        var fleet = result.State.FleetById(fleetId)!;
        Assert.Equal(armyId, fleet.CarriedArmyId);
        Assert.Equal(0, fleet.Moves);
    }

    [Fact]
    public void T216_faithful_AI_embark_lands_whole_overflow_slot_and_conserves_everything()
    {
        var state = BuildState(6_000, SeatControl.Ai, out var armyId, out var fleetId, out var nationId);
        var source = state.ArmyById(armyId)! with
        {
            Money = 100, SupplyTons = 90,
            Units = ValueList.Of(
                new UnitSlot(0, "light_infantry", 4_000, 6, "Light"),
                new UnitSlot(0, "heavy_infantry", 2_000, 7, "Heavy")),
        };
        state = state with { Armies = ValueList.Of(source) };
        var expectedLand = SplitPlacement.ArmyCellFor(state, NavalTestbed.Toy.World, source);
        Assert.NotNull(expectedLand);

        var result = DispatcherFor(SeatAsymmetryModel.Faithful)
            .Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsAccepted, result.ToString());
        var aboard = result.State.ArmyById(armyId)!;
        var overflow = Assert.Single(result.State.Armies, a => a.Id != armyId);
        Assert.Equal(4_000, Assert.Single(aboard.Units).Troops);
        Assert.Equal("Light", aboard.Units[0].Name);
        Assert.Equal(2_000, Assert.Single(overflow.Units).Troops);
        Assert.Equal("Heavy", overflow.Units[0].Name);
        Assert.Equal((100, 90, 6_000),
            (aboard.Money + overflow.Money, aboard.SupplyTons + overflow.SupplyTons,
                aboard.TotalTroops + overflow.TotalTroops));
        Assert.Equal((67, 60), (aboard.Money, aboard.SupplyTons));
        Assert.Equal((33, 30), (overflow.Money, overflow.SupplyTons));
        Assert.Equal(59, overflow.Morale);
        Assert.Equal(1, overflow.Moves);
        Assert.Equal((expectedLand.Value.X, expectedLand.Value.Y), (overflow.X, overflow.Y));
        Assert.Null(overflow.AboardFleetId);
        Assert.NotNull(overflow.CoveredTileCode);
        Assert.Equal(fleetId, aboard.AboardFleetId);
        Assert.Equal(armyId, result.State.FleetById(fleetId)!.CarriedArmyId);

        var repeated = DispatcherFor(SeatAsymmetryModel.Faithful)
            .Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));
        Assert.Equal(GameStateHash.Compute(result.State), GameStateHash.Compute(repeated.State));
        var atCapacity = source with
        {
            Units = ValueList.Of(source.Units[0] with { Troops = Capacity }),
        };
        var ordinary = DispatcherFor(SeatAsymmetryModel.Faithful).Dispatch(
            state with { Armies = ValueList.Of(atCapacity) },
            new EmbarkArmyCommand(nationId, armyId, fleetId));
        Assert.Equal(ordinary.State.RandomSeed, result.State.RandomSeed);
        var save = new SaveGame(result.State.SchemaVersion, "t216-overflow", "T216 overflow",
            result.State.ScenarioId, result.State.WorldId, result.State.RulesetId, result.State);
        var loaded = SaveManager.Load("t216-overflow.json", SaveManager.Serialize(save),
            NavalTestbed.Toy.World, NavalTestbed.Ruleset);
        Assert.Equal(result.State, loaded.State);
    }

    [Fact]
    public void T216_middle_overflow_lands_every_later_slot_even_when_it_would_fit()
    {
        var state = BuildState(6_500, SeatControl.Ai, out var armyId, out var fleetId, out var nationId);
        var source = state.ArmyById(armyId)! with
        {
            Money = 100, SupplyTons = 90,
            Units = ValueList.Of(
                new UnitSlot(0, "light_infantry", 4_000, 6, "Aboard"),
                new UnitSlot(0, "heavy_infantry", 2_000, 7, "Overflow"),
                new UnitSlot(0, "archers", 500, 8, "Would fit")),
        };
        state = state with { Armies = ValueList.Of(source) };
        var result = DispatcherFor(SeatAsymmetryModel.Faithful)
            .Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));
        Assert.True(result.IsAccepted, result.ToString());
        var aboard = result.State.ArmyById(armyId)!;
        var ashore = Assert.Single(result.State.Armies, a => a.Id != armyId);
        Assert.Equal(new[] { source.Units[0] }, aboard.Units);
        Assert.Equal(new[] { source.Units[1], source.Units[2] }, ashore.Units);
        Assert.Equal((67, 60, 33, 30), (aboard.Money, aboard.SupplyTons, ashore.Money, ashore.SupplyTons));
        Assert.Equal((100, 90, 6_500), (aboard.Money + ashore.Money,
            aboard.SupplyTons + ashore.SupplyTons, aboard.TotalTroops + ashore.TotalTroops));
        Assert.Equal(fleetId, aboard.AboardFleetId);
        Assert.Null(ashore.AboardFleetId);
        Assert.Equal(armyId, result.State.FleetById(fleetId)!.CarriedArmyId);
        var save = new SaveGame(result.State.SchemaVersion, "middle-overflow", "Middle overflow",
            result.State.ScenarioId, result.State.WorldId, result.State.RulesetId, result.State);
        Assert.Equal(result.State, SaveManager.Load("middle.json", SaveManager.Serialize(save),
            NavalTestbed.Toy.World, NavalTestbed.Ruleset).State);
    }

    [Fact]
    public void T216_single_oversized_slot_stays_whole_ashore_and_does_not_leave_a_carrier_link()
    {
        var state = BuildState(6_000, SeatControl.Ai, out var armyId, out var fleetId, out var nationId);
        var source = state.ArmyById(armyId)! with
        {
            Money = 100, SupplyTons = 90,
            Units = ValueList.Of(new UnitSlot(0, "light_infantry", 6_000, 6, "Whole slot")),
        };
        state = state with { Armies = ValueList.Of(source) };

        var result = DispatcherFor(SeatAsymmetryModel.Faithful)
            .Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Null(result.State.ArmyById(armyId));
        var ashore = Assert.Single(result.State.Armies);
        Assert.Equal((6_000, 100, 90), (ashore.TotalTroops, ashore.Money, ashore.SupplyTons));
        Assert.Null(ashore.AboardFleetId);
        Assert.NotNull(ashore.CoveredTileCode);
        Assert.Null(result.State.FleetById(fleetId)!.CarriedArmyId);
        Assert.Equal(0, result.State.FleetById(fleetId)!.Moves);
    }

    [Fact]
    public void T216_resource_third_is_fixed_even_when_overflow_is_only_one_sixth_of_troops()
    {
        var state = BuildState(6_000, SeatControl.Ai, out var armyId, out var fleetId, out var nationId);
        var source = state.ArmyById(armyId)! with
        {
            Money = 100, SupplyTons = 90,
            Units = ValueList.Of(
                new UnitSlot(0, "light_infantry", 2_000, 6, "A"),
                new UnitSlot(0, "heavy_infantry", 3_000, 7, "B"),
                new UnitSlot(0, "archers", 1_000, 8, "C")),
        };
        state = state with { Armies = ValueList.Of(source) };
        var result = DispatcherFor(SeatAsymmetryModel.Faithful)
            .Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsAccepted, result.ToString());
        var aboard = result.State.ArmyById(armyId)!;
        var ashore = Assert.Single(result.State.Armies, a => a.Id != armyId);
        Assert.Equal(new[] { 2_000, 3_000 }, aboard.Units.Select(u => u.Troops));
        Assert.Equal(1_000, Assert.Single(ashore.Units).Troops);
        Assert.Equal((67, 60, 33, 30),
            (aboard.Money, aboard.SupplyTons, ashore.Money, ashore.SupplyTons));
    }

    [Fact]
    public void T216_one_troop_whole_slot_overflow_is_not_dropped_or_scaled()
    {
        var state = BuildState(5_001, SeatControl.Ai, out var armyId, out var fleetId, out var nationId);
        var source = state.ArmyById(armyId)! with
        {
            Money = 100, SupplyTons = 90,
            Units = ValueList.Of(
                new UnitSlot(0, "light_infantry", 5_000, 6, "Full"),
                new UnitSlot(0, "heavy_infantry", 1, 7, "One")),
        };
        state = state with { Armies = ValueList.Of(source) };
        var result = DispatcherFor(SeatAsymmetryModel.Faithful)
            .Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(5_000, result.State.ArmyById(armyId)!.TotalTroops);
        var ashore = Assert.Single(result.State.Armies, a => a.Id != armyId);
        Assert.Equal(1, Assert.Single(ashore.Units).Troops);
        Assert.Equal((33, 30), (ashore.Money, ashore.SupplyTons));
        Assert.Equal(5_001, result.State.Armies.Sum(a => a.TotalTroops));
    }

    [Fact]
    public void T216_no_land_receiver_and_full_army_table_refuse_without_mutation()
    {
        var state = BuildState(6_000, SeatControl.Ai, out var armyId, out var fleetId, out var nationId);
        var source = state.ArmyById(armyId)!;
        var world = NavalTestbed.Toy.World;
        var blockers = new List<ArmyState>();
        for (var x = source.X - 1; x <= source.X + 1; x++)
        for (var y = source.Y - 1; y <= source.Y + 1; y++)
        {
            var point = new GridPoint(x, y);
            if (x == source.X && y == source.Y || !LandingTile.IsPassableForArmy(point, world)
                || state.Cities.Any(c => c.X == x && c.Y == y)
                || state.Fleets.Any(f => f.X == x && f.Y == y)) continue;
            blockers.Add(source with { Id = $"blocker-{x}-{y}", X = x, Y = y });
        }
        var noLand = state with { Armies = ValueList.From(state.Armies.Concat(blockers)) };
        Assert.Null(SplitPlacement.ArmyCellFor(noLand, world, source));
        foreach (var (input, dispatcher) in new[]
        {
            (noLand, DispatcherFor(SeatAsymmetryModel.Faithful)),
            (state, DispatcherFor(SeatAsymmetryModel.Faithful, maxArmies: state.Armies.Count)),
        })
        {
            var before = GameStateHash.Compute(input);
            var result = dispatcher.Dispatch(input, new EmbarkArmyCommand(nationId, armyId, fleetId));
            Assert.True(result.IsRejected);
            Assert.Equal(EmbarkArmyRejections.NoSafeOverflowReceiver, result.Code);
            Assert.Equal(before, GameStateHash.Compute(result.State));
            Assert.Equal(input.RandomSeed, result.State.RandomSeed);
        }
    }

    [Fact]
    public void T216_a_zero_capacity_fleet_refuses_without_throwing_or_losing_troops()
    {
        var state = BuildState(6_000, SeatControl.Ai, out var armyId, out var fleetId, out var nationId);
        state = state with { Fleets = ValueList.Of(state.FleetById(fleetId)! with { Ships = 0 }) };
        var before = GameStateHash.Compute(state);

        var result = DispatcherFor(SeatAsymmetryModel.Faithful)
            .Dispatch(state, new EmbarkArmyCommand(nationId, armyId, fleetId));

        Assert.True(result.IsRejected);
        Assert.Equal(EmbarkArmyRejections.ArmyTooLarge, result.Code);
        Assert.Equal(before, GameStateHash.Compute(result.State));
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
    public void ArmyTransportTrim_PartitionsWholeSlots_without_scaling_troops()
    {
        // F8FC: the first slot that would exceed capacity and all later slots go ashore whole.
        var units = new[]
        {
            new UnitSlot(0, "light_infantry", 4000, 6, "A"),
            new UnitSlot(0, "archers", 3000, 7, "B"),
        };

        var partition = ArmyTransportTrim.PartitionWholeUnits(units, capacity: 5000);

        Assert.Equal(4000, Assert.Single(partition.Aboard).Troops);
        Assert.Equal(3000, Assert.Single(partition.Ashore).Troops);
        Assert.Equal(7000, partition.Aboard.Sum(u => u.Troops) + partition.Ashore.Sum(u => u.Troops));
    }
}
