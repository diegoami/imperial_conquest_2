using IC2.Engine.Calendar;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Tests.Import;
using Xunit;

namespace IC2.Engine.Tests.Naval;

/// <summary>
/// T81 Done-when 5: what the weekly rough-sea overlay does to a fleet
/// (<c>decompiled-map-code1-overlay.md</c> §2 and §5). A fleet entering a rough cell pays the ruleset's
/// rough-sea cost (3) against the terrain table's 1 and carries the rough code as its covered cell; the
/// storm pass triples its roll (cap 8) for a fleet on rough sea, after the Winter doubling and before the
/// own-city halving, so the tripling is not applied to the final loss; and that branch is reachable
/// through the real fleet tick. Played figures (<c>2026-10-03-storms-and-losses-at-sea.md</c>,
/// <c>2026-10-12-g3-original-checks.md</c> §5) are the expected numbers where the report gives them.
/// </summary>
public sealed class RoughSeaEffectsTests
{
    private static World World => RealGameData.World;

    private static Ruleset Classical => RealGameData.Ruleset;

    private static GameState OpenSeaState(FleetState fleet, params RoughSeaCell[] rough) =>
        GameStateFactory.CreateInitial(World, Classical, RealGameData.Scenario) with
        {
            Calendar = new CalendarState(Week: 1, SeasonIndex: 0, YearBc: 270, TurnIndex: 0),
            Fleets = ValueList.Of(fleet),
            RoughSeaCells = ValueList.From(rough),
        };

    private static FleetState FleetAt(int x, int y)
    {
        var initial = GameStateFactory.CreateInitial(World, Classical, RealGameData.Scenario);
        return initial.Fleets[0] with
        {
            Id = "test-fleet", Nation = initial.ActiveNationId, X = x, Y = y, Moves = 29, Ships = 30, ConditionPercent = 100, SupplyTons = 500,
            CarriedArmyId = null, CoveredTileCode = 0,
        };
    }

    private static GameState Move(GameState state, int x, int y)
    {
        var fleet = state.FleetById("test-fleet")!;
        var dispatcher = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), Classical, World, NullEventSink.Instance);
        var result = dispatcher.Dispatch(state, new MoveFleetCommand(fleet.Nation, fleet.Id, x, y));
        Assert.True(result.IsAccepted, result.ToString());
        return result.State;
    }

    private static GameState Tick(GameState state)
    {
        var coordinator = new TurnCoordinator(
            SystemRegistry.FromAssemblies(new[] { typeof(CalendarSystem).Assembly }, t => t == typeof(FleetTickSystem)),
            Classical,
            World,
            NullEventSink.Instance);
        return coordinator.RunRoundTick(state).State;
    }

    // ------------------------------------------------------------------------------ the 3-move cost

    [Fact]
    public void A_step_onto_a_rough_cell_costs_three_moves_against_one_on_a_calm_cell_and_sets_the_flag()
    {
        // 2026-10-12-g3-original-checks.md §5: one diagonal step onto a rough cell took Rome's fleet from
        // 29 moves to 26, against 29 to 28 on the same cell calm; the covered-cell word became 1.
        var fleet = FleetAt(10, 10);

        var rough = Move(OpenSeaState(fleet, new RoughSeaCell(11, 11)), 11, 11).FleetById("test-fleet")!;
        var calm = Move(OpenSeaState(fleet), 11, 11).FleetById("test-fleet")!;

        Assert.Equal(26, rough.Moves);
        Assert.Equal(1, rough.CoveredTileCode);
        Assert.Equal(28, calm.Moves);
        Assert.Equal(0, calm.CoveredTileCode);
    }

    [Fact]
    public void Only_the_rough_cells_of_a_longer_walk_cost_three_and_leaving_rough_sea_restores_the_calm_code()
    {
        // #1012: the original's fleet step leaves a rough cell for a strictly cheaper calm alternative, so
        // the cells beside (12,10) are rough too; the line then wins the tie.
        var state = OpenSeaState(FleetAt(10, 10), new RoughSeaCell(12, 9), new RoughSeaCell(12, 10), new RoughSeaCell(12, 11));

        // (10,10) -> (13,10): enters (11,10) calm 1, (12,10) rough 3, (13,10) calm 1 = 5 moves.
        var after = Move(state, 13, 10).FleetById("test-fleet")!;

        Assert.Equal(29 - 5, after.Moves);
        Assert.Equal(0, after.CoveredTileCode); // ended on a calm cell: the flag is the terrain's own code again
    }

    [Fact]
    public void A_fleet_that_cannot_afford_a_rough_cell_stops_before_it()
    {
        var state = OpenSeaState(FleetAt(10, 10) with { Moves = 2 }, new RoughSeaCell(11, 10));

        var after = Move(state, 11, 10).FleetById("test-fleet")!;

        Assert.Equal(10, after.X);
        Assert.Equal(2, after.Moves);
    }

    // ------------------------------------------------------------------------------ the storm pass

    [Fact]
    public void The_tripling_comes_before_the_own_city_halving_so_a_fleet_beside_its_city_loses_1_3_or_4_on_rough_sea_against_0_or_1_on_calm()
    {
        // 2026-10-12-g3-original-checks.md §5: Rome's 30 ships at condition 60 beside its own Antium lost
        // 1, 3 or 4 condition on rough sea and 0 or 1 on calm. dmg = max(1, random(40) / 10): 1, 1, 2, 3 for
        // the four tens of the draw; tripled (cap 8) 3, 3, 6, 8; halved 1, 1, 3, 4. Calm: 1, 1, 2, 3 -> 0, 0, 1, 1.
        var roughLosses = new SortedSet<int>();
        var calmLosses = new SortedSet<int>();
        for (var raw = 0; raw < 40; raw++)
        {
            var rough = FleetAttritionRule.ApplyStormPass(
                ships: 30, conditionPercent: 60, isWinter: false, tripleDamageBranchActive: true,
                nearFriendlyCoast: true, new ScriptedRng(raw), Classical);
            var calm = FleetAttritionRule.ApplyStormPass(
                ships: 30, conditionPercent: 60, isWinter: false, tripleDamageBranchActive: false,
                nearFriendlyCoast: true, new ScriptedRng(raw), Classical);

            roughLosses.Add(60 - rough.ConditionPercent);
            calmLosses.Add(60 - calm.ConditionPercent);
        }

        // Tripling the FINAL loss instead (calm 0 or 1, times 3) could never give 4.
        Assert.Equal(new[] { 1, 3, 4 }, roughLosses.ToArray());
        Assert.Equal(new[] { 0, 1 }, calmLosses.ToArray());
    }

    [Fact]
    public void The_tripling_comes_after_the_winter_doubling_and_before_the_away_from_city_doubling()
    {
        // Condition 90, raw 9 -> dmg 1. Winter: min(5, 1 * 2) = 2. Rough: min(8, 2 * 3) = 6. Away from a
        // city: 6 * 2 + 1 = 13. Tripling before the Winter doubling would give min(5, 3 * 2) = 5 -> 11.
        var storm = FleetAttritionRule.ApplyStormPass(
            ships: 50, conditionPercent: 90, isWinter: true, tripleDamageBranchActive: true,
            nearFriendlyCoast: false, new ScriptedRng(new[] { 9 }, new[] { false }), Classical);

        Assert.Equal(13, storm.Damage);
    }

    [Fact]
    public void A_fleet_on_rough_sea_away_from_its_cities_loses_25_ships_in_the_real_fleet_tick_where_a_calm_one_loses_none()
    {
        // 2026-10-03-storms-and-losses-at-sea.md: 90 ships at condition 85, in Spring and away from their
        // cities, on a rough cell lost 25 ships and 24 or 25 condition (dmg 1 -> 3 -> 7, d 86). On calm sea
        // the same fleet takes dmg 1 -> 3 and loses condition only.
        var rough = FleetAt(10, 10) with { Ships = 90, ConditionPercent = 85, CoveredTileCode = 1 };
        var calm = rough with { CoveredTileCode = 0 };

        var roughAfter = Tick(OpenSeaState(rough, new RoughSeaCell(10, 10))).FleetById("test-fleet")!;
        var calmAfter = Tick(OpenSeaState(calm)).FleetById("test-fleet")!;

        Assert.Equal(65, roughAfter.Ships);
        Assert.Equal(61, roughAfter.ConditionPercent); // 85 - 85 * 86 / 300
        Assert.Equal(90, calmAfter.Ships);
        Assert.Equal(82, calmAfter.ConditionPercent);  // 85 - 3
    }

    [Fact]
    public void The_real_fleet_tick_beside_its_own_city_loses_1_3_or_4_on_rough_sea_and_0_or_1_on_calm_across_seeds()
    {
        var fleet = FleetAt(10, 10) with { Ships = 30, ConditionPercent = 60 };
        var template = GameStateFactory.CreateInitial(World, Classical, RealGameData.Scenario);
        var city = template.Cities.First(c => c.Owner == fleet.Nation) with { X = 11, Y = 10 };

        var roughLosses = new SortedSet<int>();
        var calmLosses = new SortedSet<int>();
        for (ulong seed = 1; seed <= 24; seed++)
        {
            var onRough = OpenSeaState(fleet with { CoveredTileCode = 1 }, new RoughSeaCell(10, 10)) with { RandomSeed = seed };
            var onCalm = OpenSeaState(fleet) with { RandomSeed = seed };
            onRough = onRough with { Cities = ValueList.From(onRough.Cities.Append(city)) };
            onCalm = onCalm with { Cities = ValueList.From(onCalm.Cities.Append(city)) };

            roughLosses.Add(60 - Tick(onRough).FleetById("test-fleet")!.ConditionPercent);
            calmLosses.Add(60 - Tick(onCalm).FleetById("test-fleet")!.ConditionPercent);
        }

        Assert.True(roughLosses.IsSubsetOf(new[] { 1, 3, 4 }), string.Join(",", roughLosses));
        Assert.True(roughLosses.Max >= 3, "The tripled branch fires: some rough-sea tick loses 3 or 4.");
        Assert.True(calmLosses.IsSubsetOf(new[] { 0, 1 }), string.Join(",", calmLosses));
    }
}
