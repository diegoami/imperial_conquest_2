using IC2.Engine.Armies;
using IC2.Engine.Armies.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Armies;

/// <summary>
/// T142 Done-when 1–4: an army aboard a fleet is split exactly as one on land. The new army is placed
/// by <c>FUN_004492C0</c>'s scan centred on the carrying fleet's tile, stands on land, is not aboard,
/// and the parent's remaining units stay aboard the same fleet. The supply rebalance runs as for a land
/// army; with no free land cell (or a dangling fleet link) the split is refused and nothing is written.
/// </summary>
/// <remarks>
/// Done-when 1 is the observed split from the 2026-10-05 report: a Roman fleet at <c>(101, 46)`
/// carrying a three-unit army, the new army on land at <c>(102, 47)</c>. The remaining cases use the
/// toy map, where a deep-sea tile with no land neighbour is a scripted fact.
/// </remarks>
public sealed class SplitAboardAFleetTests
{
    private static readonly Lazy<ResolvedScenario> LazyClassical = new(
        () => GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean"));

    private static ResolvedScenario Classical => LazyClassical.Value;

    private static GameState ClassicalInitial() =>
        GameStateFactory.CreateInitial(Classical.World, Classical.Ruleset, Classical.Scenario);

    private static CommandDispatcher ClassicalDispatcher(Ruleset? ruleset = null) => new(
        SystemRegistry.FromEngineAssembly(),
        ruleset ?? Classical.Ruleset,
        Classical.World,
        NullEventSink.Instance);

    private static UnitSlot Unit(string type, int troops, string name) => new(0, type, troops, 6, name);

    private static ArmyState AboardArmy(
        string id, string nation, string fleetId, int x, int y, int money, int supply, params UnitSlot[] units) =>
        new(id, nation, x, y, Moves: 5, Morale: 68, Money: money, SupplyTons: supply, CoveredTileCode: null,
            AboardFleetId: fleetId, Units: ValueList.From(units));

    private static ArmyState LandArmy(string id, string nation, int x, int y, params UnitSlot[] units) =>
        new(id, nation, x, y, Moves: 5, Morale: 68, Money: 0, SupplyTons: 0, CoveredTileCode: 2,
            AboardFleetId: null, Units: ValueList.From(units));

    private static FleetState Fleet(string id, string nation, int x, int y, string? carried) =>
        new(id, nation, x, y, Moves: 5, Ships: 40, ConditionPercent: 90, Money: 0, SupplyTons: 0,
            ConstructionTicksRemaining: null, BuildCityId: null, CarriedArmyId: carried, CoveredTileCode: null);

    private static GameState WithRomeControl(GameState state, SeatControl control) =>
        state with
        {
            Nations = ValueList.From(state.Nations.Select(n =>
                string.Equals(n.Id, "rome", StringComparison.Ordinal) ? n with { Control = control } : n)),
        };

    private static int TerrainAt(World world, int x, int y)
    {
        var cells = world.Terrain.Decode(world.Width, world.Height);
        return cells[(y * world.Width) + x];
    }

    private static GameState AboardObservedSplit(SeatControl romeControl) =>
        WithRomeControl(
            ClassicalInitial() with
            {
                Armies = ValueList.Of(
                    AboardArmy(
                        "aboard-parent", "rome", "carry-fleet", 101, 46, money: 100, supply: 50,
                        Unit("light_infantry", 5_000, "a"),
                        Unit("light_infantry", 3_000, "b"),
                        Unit("light_infantry", 2_700, "c")),
                    LandArmy("other-1", "rome", 102, 45, Unit("light_infantry", 1_000, "x")),
                    LandArmy("other-2", "rome", 102, 44, Unit("light_infantry", 1_000, "y"))),
                Fleets = ValueList.Of(Fleet("carry-fleet", "rome", 101, 46, "aboard-parent")),
            },
            romeControl);

    [Fact]
    public void An_embarked_army_splits_to_land_next_to_its_fleet()
    {
        var state = AboardObservedSplit(SeatControl.Human);

        var result = ClassicalDispatcher().Dispatch(
            state, new SplitArmyCommand("rome", "aboard-parent", "aboard-new", ValueList.Of(0)));

        Assert.True(result.IsAccepted, result.ToString());
        var created = result.State.ArmyById("aboard-new")!;
        Assert.Equal(102, created.X);
        Assert.Equal(47, created.Y);
        Assert.Null(created.AboardFleetId);
        Assert.Equal(TerrainAt(Classical.World, 102, 47), created.CoveredTileCode);
        Assert.Single(created.Units);
        Assert.Equal(5_000, created.TotalTroops);
        Assert.Equal(0, created.Moves);
        Assert.Equal(0, created.SupplyTons);
        Assert.Equal(0, created.Money);

        var remaining = result.State.ArmyById("aboard-parent")!;
        Assert.Equal(2, remaining.Units.Count);
        Assert.Equal(5_700, remaining.TotalTroops);
        Assert.Equal(50, remaining.SupplyTons);
        Assert.Equal(100, remaining.Money);
        Assert.Equal("carry-fleet", remaining.AboardFleetId);
        Assert.Equal("aboard-parent", result.State.FleetById("carry-fleet")!.CarriedArmyId);
    }

    [Fact]
    public void The_same_split_gives_the_new_army_one_move_for_an_ai_seat()
    {
        var state = AboardObservedSplit(SeatControl.Ai);

        var result = ClassicalDispatcher().Dispatch(
            state, new SplitArmyCommand("rome", "aboard-parent", "aboard-new", ValueList.Of(0)));

        Assert.True(result.IsAccepted, result.ToString());
        Assert.Equal(1, result.State.ArmyById("aboard-new")!.Moves);
    }

    [Fact]
    public void Under_improved_the_new_army_gets_one_move_for_either_seat()
    {
        var improved = Classical.Ruleset with
        {
            Flags = Classical.Ruleset.Flags with { SeatAsymmetry = SeatAsymmetryModel.Normalized },
        };

        foreach (var control in new[] { SeatControl.Human, SeatControl.Ai })
        {
            var state = AboardObservedSplit(control);
            var result = ClassicalDispatcher(improved).Dispatch(
                state, new SplitArmyCommand("rome", "aboard-parent", "aboard-new", ValueList.Of(0)));

            Assert.True(result.IsAccepted, result.ToString());
            Assert.Equal(1, result.State.ArmyById("aboard-new")!.Moves);
        }
    }

    [Fact]
    public void An_embarked_parent_rebalances_its_supply_with_the_new_land_army()
    {
        var state = WithRomeControl(
            ClassicalInitial() with
            {
                Armies = ValueList.Of(
                    AboardArmy(
                        "rebalance-aboard", "rome", "rebalance-fleet", 101, 46, money: 0, supply: 500,
                        Unit("light_infantry", 15_000, "a"),
                        Unit("light_infantry", 15_000, "b"))),
                Fleets = ValueList.Of(Fleet("rebalance-fleet", "rome", 101, 46, "rebalance-aboard")),
            },
            SeatControl.Human);

        var result = ClassicalDispatcher().Dispatch(
            state, new SplitArmyCommand("rome", "rebalance-aboard", "rebalance-new", ValueList.Of(1)));

        Assert.True(result.IsAccepted, result.ToString());
        var remaining = result.State.ArmyById("rebalance-aboard")!;
        var created = result.State.ArmyById("rebalance-new")!;
        Assert.Equal(350, remaining.SupplyTons);
        Assert.Equal(150, created.SupplyTons);
        Assert.Equal(500, remaining.SupplyTons + created.SupplyTons);
        Assert.Equal("rebalance-fleet", remaining.AboardFleetId);
        Assert.Null(created.AboardFleetId);
    }

    [Fact]
    public void An_embarked_army_whose_fleet_has_no_free_land_is_refused_and_the_state_is_unchanged()
    {
        var parent = AboardArmy(
            "marooned-aboard", "north", "marooned-fleet", 7, 5, money: 0, supply: 0,
            Unit("light_infantry", 1_000, "a"), Unit("light_infantry", 1_000, "b"));
        var state = ArmiesTestbed.InitialState() with
        {
            Armies = ValueList.Of(parent),
            Fleets = ValueList.Of(Fleet("marooned-fleet", "north", 7, 5, "marooned-aboard")),
        };

        Assert.Null(SplitPlacement.ArmyCellFor(state, CoreTestbed.Toy.World, parent));

        var result = ArmiesTestbed.Dispatcher().Dispatch(
            state, new SplitArmyCommand("north", parent.Id, "marooned-new", ValueList.Of(0)));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitArmyRejections.NoFreeAdjacentTile, result.Code);
        Assert.Same(state, result.State);
    }

    [Fact]
    public void An_embarked_army_whose_fleet_does_not_exist_is_refused_and_the_state_is_unchanged()
    {
        var parent = AboardArmy(
            "dangling-aboard", "north", "ghost-fleet", 3, 3, money: 0, supply: 0,
            Unit("light_infantry", 1_000, "a"), Unit("light_infantry", 1_000, "b"));
        var state = ArmiesTestbed.InitialState() with { Armies = ValueList.Of(parent) };

        Assert.Null(SplitPlacement.ArmyCellFor(state, CoreTestbed.Toy.World, parent));

        var result = ArmiesTestbed.Dispatcher().Dispatch(
            state, new SplitArmyCommand("north", parent.Id, "dangling-new", ValueList.Of(0)));

        Assert.True(result.IsRejected);
        Assert.Equal(SplitArmyRejections.NoFreeAdjacentTile, result.Code);
        Assert.Same(state, result.State);
    }
}
