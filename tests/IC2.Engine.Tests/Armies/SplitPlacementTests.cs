using IC2.Engine.Armies;
using IC2.Engine.Armies.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Naval;
using Xunit;
using static IC2.Engine.Tests.Armies.ArmiesTestbed;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// <c>docs/tasks/T114.md</c> Done-when 8–12: a split's new army or fleet stands one tile from its
/// parent, not on the parent's tile — the <c>FUN_004492C0</c> 3×3 last-qualifying-cell scan, reproduced
/// for a split by <see cref="SplitPlacement"/>.
/// </summary>
/// <remarks>
/// The toy map is the scripted world; the shipped <c>classical-mediterranean</c> terrain is the
/// cross-check against the original's own observations.
/// </remarks>
public sealed class SplitPlacementTests
{
    private static World World => CoreTestbed.Toy.World;

    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static ResolvedScenario Classical => LazyClassical.Value;

    private static CommandDispatcher ClassicalDispatcher() => new(
        SystemRegistry.FromEngineAssembly(), Classical.Ruleset, Classical.World, NullEventSink.Instance);

    private static GameState ClassicalInitial() =>
        GameStateFactory.CreateInitial(Classical.World, Classical.Ruleset, Classical.Scenario);

    private static ArmyState TwoUnitArmy(string id, string nation, int x, int y) =>
        new(id, nation, x, y, Moves: 5, Morale: 68, Money: 0, SupplyTons: 0, CoveredTileCode: null,
            AboardFleetId: null,
            Units: ValueList.Of(
                new UnitSlot(0, "light_infantry", 1000, 6, "1st"),
                new UnitSlot(0, "light_infantry", 1000, 6, "2nd")));

    private static FleetState Fleet(string id, string nation, int x, int y, int ships = 40) =>
        new(id, nation, x, y, Moves: 5, Ships: ships, ConditionPercent: 90, Money: 0, SupplyTons: 0,
            ConstructionTicksRemaining: null, BuildCityId: null, CarriedArmyId: null, CoveredTileCode: null);

    private static int TerrainCode(World world, int x, int y)
    {
        var cells = world.Terrain.Decode(world.Width, world.Height);
        return cells[(y * world.Width) + x];
    }

    // ---- Done-when 8: split army placement ----

    [Fact]
    public void A_parent_with_every_neighbour_free_puts_its_new_army_at_plus_one_plus_one()
    {
        var parent = TwoUnitArmy("army-free", NorthNationId, 4, 0);
        var state = WithArmies(InitialState(), parent);

        var result = Dispatcher().Dispatch(
            state, new SplitArmyCommand(NorthNationId, parent.Id, "army-free-new", ValueList.Of(0)));

        Assert.True(result.IsAccepted, result.ToString());
        var created = result.State.ArmyById("army-free-new")!;
        Assert.Equal(5, created.X); // (+1, +1)
        Assert.Equal(1, created.Y);
    }

    [Fact]
    public void An_army_on_the_south_east_neighbour_pushes_the_new_army_to_zero_plus_one()
    {
        var parent = TwoUnitArmy("army-blocked-se", NorthNationId, 4, 0);
        var blocker = TwoUnitArmy("army-blocker", NorthNationId, 5, 1);
        var state = WithArmies(InitialState(), parent, blocker);

        var cell = SplitPlacement.ArmyCell(state, World, new GridPoint(parent.X, parent.Y));

        Assert.Equal(new GridPoint(4, 1), cell); // (0, +1)
    }

    [Fact]
    public void A_sea_south_east_neighbour_pushes_the_new_army_to_zero_plus_one()
    {
        // Around (5,3) the (+1,+1) cell (6,4) is sea, so the scan falls back to (5,4).
        var parent = TwoUnitArmy("army-blocked-sea", NorthNationId, 5, 3);
        var state = WithArmies(InitialState(), parent);

        var cell = SplitPlacement.ArmyCell(state, World, new GridPoint(parent.X, parent.Y));

        Assert.Equal(new GridPoint(5, 4), cell);
        Assert.False(World.TileTypeByCode(TerrainCode(World, 6, 4))!.PassableByArmies);
    }

    [Fact]
    public void The_new_army_carries_its_new_cells_terrain_code_as_its_covered_tile()
    {
        var parent = TwoUnitArmy("army-covered", NorthNationId, 4, 0);
        var state = WithArmies(InitialState(), parent);

        var result = Dispatcher().Dispatch(
            state, new SplitArmyCommand(NorthNationId, parent.Id, "army-covered-new", ValueList.Of(0)));

        Assert.True(result.IsAccepted, result.ToString());
        var created = result.State.ArmyById("army-covered-new")!;
        Assert.Equal(TerrainCode(World, created.X, created.Y), created.CoveredTileCode);
        Assert.True(World.TileTypeByCode(created.CoveredTileCode!.Value)!.PassableByArmies);
    }

    [Fact]
    public void On_the_shipped_world_an_army_at_one_hundred_thirty_seven_splits_to_one_oh_one_thirty_eight()
    {
        var parent = TwoUnitArmy("classical-army-a", "rome", 100, 37);
        var state = ClassicalInitial() with { Armies = ValueList.Of(parent) };

        var result = ClassicalDispatcher().Dispatch(
            state, new SplitArmyCommand("rome", parent.Id, "classical-army-a-new", ValueList.Of(0)));

        Assert.True(result.IsAccepted, result.ToString());
        var created = result.State.ArmyById("classical-army-a-new")!;
        Assert.Equal(101, created.X);
        Assert.Equal(38, created.Y);
    }

    [Fact]
    public void On_the_shipped_world_an_army_at_ninety_two_twenty_seven_splits_to_ninety_three_twenty_eight()
    {
        var parent = TwoUnitArmy("classical-army-b", "rome", 92, 27);
        var state = ClassicalInitial() with { Armies = ValueList.Of(parent) };

        var result = ClassicalDispatcher().Dispatch(
            state, new SplitArmyCommand("rome", parent.Id, "classical-army-b-new", ValueList.Of(0)));

        Assert.True(result.IsAccepted, result.ToString());
        var created = result.State.ArmyById("classical-army-b-new")!;
        Assert.Equal(93, created.X);
        Assert.Equal(28, created.Y);
    }

    // ---- Done-when 9: split fleet placement ----

    [Fact]
    public void On_the_shipped_world_a_fleet_at_one_oh_one_forty_six_splits_to_one_oh_one_forty_seven()
    {
        var rules = Classical.Ruleset.Naval;
        var parent = Fleet("classical-fleet-1", "rome", 101, 46, ships: rules.SplitMinShips);
        var state = ClassicalInitial() with { Fleets = ValueList.Of(parent) };

        var result = ClassicalDispatcher().Dispatch(
            state, new SplitFleetCommand("rome", parent.Id, "classical-fleet-1-new", ShipsToNewFleet: 5));

        Assert.True(result.IsAccepted, result.ToString());
        var created = result.State.FleetById("classical-fleet-1-new")!;
        Assert.Equal(101, created.X);
        Assert.Equal(47, created.Y);
    }

    [Fact]
    public void The_new_fleet_lands_on_the_last_free_sea_cell_never_on_land_or_an_occupied_cell()
    {
        var parent = Fleet("fleet-free", NorthNationId, 3, 5);
        var state = NavalTestbed.InitialState() with { Fleets = ValueList.Of(parent) };

        var result = NavalTestbed.RealEngineDispatcher().Dispatch(
            state, new SplitFleetCommand(NorthNationId, parent.Id, "fleet-free-new", ShipsToNewFleet: 5));

        Assert.True(result.IsAccepted, result.ToString());
        var created = result.State.FleetById("fleet-free-new")!;
        Assert.Equal(4, created.X); // (4, 5): the last free sea cell of the block.
        Assert.Equal(5, created.Y);
        Assert.True(World.TileTypeByCode(TerrainCode(World, created.X, created.Y))!.PassableByFleets);
    }

    [Fact]
    public void An_occupied_south_east_sea_cell_pushes_the_new_fleet_to_the_next_free_sea_cell()
    {
        var parent = Fleet("fleet-occ", NorthNationId, 3, 5);
        var blocker = Fleet("fleet-occ-blocker", NorthNationId, 4, 5);
        var state = NavalTestbed.InitialState() with { Fleets = ValueList.Of(parent, blocker) };

        var cell = SplitPlacement.FleetCell(state, World, new GridPoint(parent.X, parent.Y));

        Assert.Equal(new GridPoint(2, 5), cell);
    }

    // ---- Done-when 10: a split pair rejoins at once ----

    [Fact]
    public void A_freshly_split_army_rejoins_its_parent_immediately()
    {
        var parent = TwoUnitArmy("rejoin-army", NorthNationId, 4, 0);
        var state = WithArmies(InitialState(), parent);
        var dispatcher = Dispatcher();

        var split = dispatcher.Dispatch(
            state, new SplitArmyCommand(NorthNationId, parent.Id, "rejoin-army-new", ValueList.Of(0)));
        Assert.True(split.IsAccepted, split.ToString());

        var join = dispatcher.Dispatch(
            split.State, new JoinArmiesCommand(NorthNationId, parent.Id, "rejoin-army-new"));
        Assert.True(join.IsAccepted, join.ToString());
    }

    [Fact]
    public void A_freshly_split_fleet_rejoins_its_parent_immediately()
    {
        var parent = Fleet("rejoin-fleet", NorthNationId, 3, 5);
        var state = NavalTestbed.InitialState() with { Fleets = ValueList.Of(parent) };
        var dispatcher = NavalTestbed.RealEngineDispatcher();

        var split = dispatcher.Dispatch(
            state, new SplitFleetCommand(NorthNationId, parent.Id, "rejoin-fleet-new", ShipsToNewFleet: 5));
        Assert.True(split.IsAccepted, split.ToString());

        var join = dispatcher.Dispatch(
            split.State, new JoinFleetsCommand(NorthNationId, parent.Id, "rejoin-fleet-new"));
        Assert.True(join.IsAccepted, join.ToString());
    }

    [Fact]
    public void A_freshly_split_fleet_receives_a_transfer_immediately()
    {
        var parent = Fleet("rejoin-xfer", NorthNationId, 3, 5);
        var state = NavalTestbed.InitialState() with { Fleets = ValueList.Of(parent) };
        var dispatcher = NavalTestbed.RealEngineDispatcher();

        var split = dispatcher.Dispatch(
            state, new SplitFleetCommand(NorthNationId, parent.Id, "rejoin-xfer-new", ShipsToNewFleet: 5));
        Assert.True(split.IsAccepted, split.ToString());

        var transfer = dispatcher.Dispatch(
            split.State, new FleetToFleetTransferCommand(NorthNationId, parent.Id, "rejoin-xfer-new", Ships: 3, SupplyTons: 0, Money: 0));
        Assert.True(transfer.IsAccepted, transfer.ToString());
    }

    // ---- Done-when 11: no free cell ----

    [Fact]
    public void An_army_with_all_eight_neighbours_blocked_is_refused_and_the_state_is_unchanged()
    {
        // (6,3): (5,2) is the city Portus, four neighbours are sea, and three blocker armies hold the
        // rest, so no cell qualifies.
        var parent = TwoUnitArmy("army-marooned", NorthNationId, 6, 3);
        var state = WithArmies(
            InitialState(),
            parent,
            TwoUnitArmy("army-block-1", NorthNationId, 6, 2),
            TwoUnitArmy("army-block-2", NorthNationId, 5, 3),
            TwoUnitArmy("army-block-3", NorthNationId, 5, 4));

        Assert.Null(SplitPlacement.ArmyCell(state, World, new GridPoint(parent.X, parent.Y)));

        var result = Dispatcher().Dispatch(
            state, new SplitArmyCommand(NorthNationId, parent.Id, "army-marooned-new", ValueList.Of(0)));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitArmyRejections.NoFreeAdjacentTile, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void A_fleet_with_all_eight_neighbours_blocked_is_refused_and_the_state_is_unchanged()
    {
        // (3,5) has land to the north and out-of-bounds to the south; the only two other sea cells are
        // held by blocker fleets, so no cell qualifies.
        var parent = Fleet("fleet-marooned", NorthNationId, 3, 5);
        var state = NavalTestbed.InitialState() with
        {
            Fleets = ValueList.Of(
                parent,
                Fleet("fleet-block-1", NorthNationId, 2, 5),
                Fleet("fleet-block-2", NorthNationId, 4, 5)),
        };

        Assert.Null(SplitPlacement.FleetCell(state, World, new GridPoint(parent.X, parent.Y)));

        var result = NavalTestbed.RealEngineDispatcher().Dispatch(
            state, new SplitFleetCommand(NorthNationId, parent.Id, "fleet-marooned-new", ShipsToNewFleet: 5));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitFleetRejections.NoFreeAdjacentTile, result.Code);
        Assert.Same(state, result.State);
    }

    // ---- Done-when 12: the two scans agree ----

    [Fact]
    public void Centred_on_a_city_cell_the_split_scan_agrees_with_the_mobilisation_scan()
    {
        var arx = InitialState().CityById("arx")!;
        // Block the south-east cell so neither scan can simply return (+1, +1).
        var blocker = TwoUnitArmy("mob-blocker", NorthNationId, arx.X + 1, arx.Y + 1);
        var state = WithArmies(InitialState(), blocker);

        var splitCell = SplitPlacement.ArmyCell(state, World, new GridPoint(arx.X, arx.Y));
        var mobilisationCell = MobilizationArmyCreation.PlacementCell(state, World, arx);

        Assert.NotNull(splitCell);
        Assert.Equal(mobilisationCell, splitCell);
    }
}
