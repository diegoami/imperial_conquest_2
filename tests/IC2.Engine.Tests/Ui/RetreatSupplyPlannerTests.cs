using IC2.Engine.Economy;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Naval;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Import;
using IC2.Slice.UI;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ui;

public sealed class RetreatSupplyPlannerTests
{
    private static (GameState State, ArmyState Army, CityState Capital, CityState Large, CityState Near)
        ArmyStateAt(int x = 100, int y = 40)
    {
        var session = new GameSession(RealGameData.World, RealGameData.Ruleset,
            RealGameData.Scenario, seedOverride: 23, humanSeatNationId: "rome");
        var state = session.State;
        var actor = state.Armies.First(a => a.Nation == "rome") with
        {
            X = x, Y = y, Moves = 5, SupplyTons = 0,
        };
        var capitalId = state.NationById("rome")!.CapitalCityId!;
        var capital = state.CityById(capitalId)! with
        {
            X = 130, Y = 40, SupplyTons = 50, PopulationThousands = 20,
        };
        var others = state.Cities.Where(c => c.Id != capital.Id).Take(2).ToArray();
        var large = others[0] with
        {
            Owner = "rome", X = 105, Y = 40, SupplyTons = 50, PopulationThousands = 100,
        };
        var near = others[1] with
        {
            Owner = "rome", X = 101, Y = 40, SupplyTons = 50, PopulationThousands = 10,
        };
        state = state with
        {
            Armies = ValueList.Of(actor),
            Fleets = ValueList<FleetState>.Empty,
            Cities = ValueList.Of(capital, large, near),
        };
        return (state, actor, capital, large, near);
    }

    private static RetreatSupplyDecision Plan(GameState state, ArmyState army) =>
        RetreatSupplyPlanner.Next(state, RealGameData.World, RealGameData.Ruleset,
            new UserSettings(), new RetreatSupplyPlan(army.Id, army.Nation, IsFleet: false));

    [Fact]
    public void Capital_then_population_then_distance_choose_only_usable_owned_towns()
    {
        var (state, army, capital, large, near) = ArmyStateAt();
        var first = Plan(state, army);
        Assert.Equal(capital.Id, first.DestinationCityId);
        Assert.Contains($"{capital.X} {capital.Y}", first.CommandLine);

        state = state with { Cities = ValueList.Of(capital with { SupplyTons = 0 }, large, near) };
        Assert.Equal(large.Id, Plan(state, army).DestinationCityId);
        state = state with { Cities = ValueList.Of(capital with { SupplyTons = 0 },
            large with { SupplyTons = 0 }, near) };
        Assert.Equal(near.Id, Plan(state, army).DestinationCityId);
    }

    [Fact]
    public void War_threat_at_ten_tiles_cancels_for_either_enemy_unit_type()
    {
        var (state, army, _, _, _) = ArmyStateAt();
        var enemyArmy = army with { Id = "enemy", Nation = "carthage", X = 110, Y = 50 };
        state = state with
        {
            Armies = ValueList.Of(army, enemyArmy),
            Relations = state.Relations.WithRelation("rome", "carthage",
                RealGameData.Ruleset.Diplomacy.StateCodes.War),
        };
        Assert.Contains("enemy army", Plan(state, army).Reason);
        state = state with { Armies = ValueList.Of(army, enemyArmy with { X = 111 }) };
        Assert.False(Plan(state, army).Cancel);

        var fleet = GameStateFactory.CreateInitial(RealGameData.World, RealGameData.Ruleset,
            RealGameData.Scenario).Fleets[0] with
        {
            Id = "enemy-fleet", Nation = "carthage", X = 110, Y = 50,
            ConstructionTicksRemaining = null,
        };
        state = state with { Armies = ValueList.Of(army), Fleets = ValueList.Of(fleet) };
        Assert.Contains("enemy fleet", Plan(state, army).Reason);
        state = state with { Fleets = ValueList.Of(fleet with { ConstructionTicksRemaining = 3 }) };
        Assert.False(Plan(state, army).Cancel);
        state = state with
        {
            Fleets = ValueList.Of(fleet),
            Relations = state.Relations.WithRelation("rome", "carthage",
                RealGameData.Ruleset.Diplomacy.StateCodes.Peace),
        };
        Assert.False(Plan(state, army).Cancel);
    }

    [Fact]
    public void Army_full_supply_finishes_despite_empty_purse_and_zero_moves_can_still_use_a_town()
    {
        var (state, army, capital, large, near) = ArmyStateAt();
        var capacity = SupplyCapacity.ArmyDialogCapacityTons(army.TotalTroops, RealGameData.Ruleset);
        var full = army with { SupplyTons = capacity, Money = 0, Moves = 0 };
        var completeState = state with { Armies = ValueList.Of(full) };
        Assert.True(Plan(completeState, full).Completed);

        var empty = army with { Moves = 0 };
        var besideTown = state with
        {
            Armies = ValueList.Of(empty),
            Cities = ValueList.Of(capital with { SupplyTons = 0 },
                large with { SupplyTons = 0 }, near),
        };
        var decision = Plan(besideTown, empty);
        Assert.True(decision.AtProvider);
        Assert.Equal(near.Id, decision.DestinationCityId);
    }

    [Fact]
    public void Fleet_requires_full_condition_and_can_repair_without_moves_or_town_supply()
    {
        var (state, army, _, _, _) = ArmyStateAt();
        var initial = GameStateFactory.CreateInitial(RealGameData.World, RealGameData.Ruleset,
            RealGameData.Scenario);
        var coastal = initial.Cities.First(c => c.Owner == army.Nation
            && CoastalCity.IsCoastal(c, RealGameData.World)) with { SupplyTons = 0 };
        var fleet = initial.Fleets[0] with
        {
            Id = "recovery-fleet", Nation = army.Nation, X = coastal.X + 1, Y = coastal.Y,
            ConstructionTicksRemaining = null, CarriedArmyId = null, Moves = 0,
            ConditionPercent = 90,
        };
        fleet = fleet with
        {
            SupplyTons = SupplyCapacity.FleetCapacityTons(fleet.Ships, RealGameData.Ruleset),
        };
        state = state with
        {
            Fleets = ValueList.Of(fleet),
            Cities = ValueList.Of(coastal),
            Nations = ValueList.From(state.Nations.Select(n => n.Id == army.Nation
                ? n with { Treasury = 10_000 } : n)),
        };
        var plan = new RetreatSupplyPlan(fleet.Id, fleet.Nation, IsFleet: true);
        var before = GameStateHash.Compute(state);
        var decision = RetreatSupplyPlanner.Next(state, RealGameData.World,
            RealGameData.Ruleset, new UserSettings(), plan);
        Assert.True(decision.AtProvider);
        Assert.False(decision.Completed);
        Assert.Equal(coastal.Id, decision.DestinationCityId);
        Assert.Equal(before, GameStateHash.Compute(state));
        Assert.True(RetreatSupplyPlanner.Next(state, RealGameData.World,
            RealGameData.Ruleset, new UserSettings { AutoTreasuryReserveTalents = 10_000 }, plan).AtProvider);

        state = state with { Fleets = ValueList.Of(fleet with
            { ConditionPercent = RealGameData.Ruleset.Naval.MaxConditionPercent }) };
        Assert.True(RetreatSupplyPlanner.Next(state, RealGameData.World,
            RealGameData.Ruleset, new UserSettings(), plan).Completed);
    }

    [Fact]
    public void No_usable_owned_town_cancels_instead_of_repeating_a_destination()
    {
        var (state, army, capital, large, near) = ArmyStateAt();
        state = state with { Cities = ValueList.Of(capital with { SupplyTons = 0 },
            large with { SupplyTons = 0 }, near with { SupplyTons = 0 }) };
        var decision = Plan(state, army);
        Assert.True(decision.Cancel);
        Assert.Contains("no owned town", decision.Reason);
        Assert.Null(decision.CommandLine);
    }

    [Fact]
    public void A_fleet_skips_an_inland_capital_for_a_usable_coastal_town()
    {
        var (state, army, _, _, _) = ArmyStateAt();
        var world = RealGameData.World;
        var inland = state.Cities.Concat(GameStateFactory.CreateInitial(world,
                RealGameData.Ruleset, RealGameData.Scenario).Cities)
            .First(city => !CoastalCity.IsCoastal(city, world)) with
        {
            Owner = army.Nation, SupplyTons = 1_000, PopulationThousands = 1_000,
        };
        var coast = GameStateFactory.CreateInitial(world, RealGameData.Ruleset,
            RealGameData.Scenario).Cities.First(city => city.Id != inland.Id
                && CoastalCity.IsCoastal(city, world)) with
        {
            Owner = army.Nation, SupplyTons = 1_000, PopulationThousands = 1,
        };
        var fleet = GameStateFactory.CreateInitial(world, RealGameData.Ruleset,
            RealGameData.Scenario).Fleets[0] with
        {
            Id = "coastal-recovery", Nation = army.Nation,
            ConstructionTicksRemaining = null, CarriedArmyId = null,
            SupplyTons = 0, ConditionPercent = 100, Moves = 5,
        };
        state = state with
        {
            Cities = ValueList.Of(inland, coast),
            Fleets = ValueList.Of(fleet),
            Nations = ValueList.From(state.Nations.Select(n => n.Id == army.Nation
                ? n with { CapitalCityId = inland.Id } : n)),
        };
        var decision = RetreatSupplyPlanner.Next(state, world, RealGameData.Ruleset,
            new UserSettings(), new RetreatSupplyPlan(fleet.Id, fleet.Nation, IsFleet: true));
        Assert.Equal(coast.Id, decision.DestinationCityId);
    }

    [Fact]
    public void An_adjacent_owned_fleet_can_supply_an_army_before_it_seeks_another_town()
    {
        var (state, army, capital, large, near) = ArmyStateAt();
        var template = GameStateFactory.CreateInitial(RealGameData.World, RealGameData.Ruleset,
            RealGameData.Scenario).Fleets[0];
        var provider = template with
        {
            Id = "provider", Nation = army.Nation, X = army.X + 1, Y = army.Y,
            ConstructionTicksRemaining = null, CarriedArmyId = null, SupplyTons = 100,
        };
        state = state with
        {
            Fleets = ValueList.Of(provider),
            Cities = ValueList.Of(capital with { SupplyTons = 0 },
                large with { SupplyTons = 0 }, near with { SupplyTons = 0 }),
        };
        var decision = Plan(state, army);
        Assert.True(decision.AtProvider);
        Assert.Null(decision.CommandLine);
    }

    [Fact]
    public void Italy_uses_its_own_world_and_towns_without_classical_ids()
    {
        var resolved = GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("italy-500bc");
        var initial = GameStateFactory.CreateInitial(resolved.World, resolved.Ruleset, resolved.Scenario);
        var actor = initial.Armies.First(a => !a.IsEmbarked && a.TotalTroops > 0
            && initial.Cities.Any(c => c.Owner == a.Nation)) with { SupplyTons = 0, Moves = 5 };
        var state = initial with
        {
            ActiveSeatIndex = Array.IndexOf(initial.TurnOrder.ToArray(), actor.Nation),
            Armies = ValueList.Of(actor),
            Fleets = ValueList<FleetState>.Empty,
            Cities = ValueList.From(initial.Cities.Select(c => c.Owner == actor.Nation
                ? c with { SupplyTons = 1_000 } : c)),
            Nations = ValueList.From(initial.Nations.Select(n => n.Id == actor.Nation
                ? n with { Control = SeatControl.Human } : n)),
        };
        var decision = RetreatSupplyPlanner.Next(state, resolved.World, resolved.Ruleset,
            new UserSettings(), new RetreatSupplyPlan(actor.Id, actor.Nation, IsFleet: false));
        Assert.False(decision.Cancel, decision.Reason);
        Assert.Contains(state.Cities, city => city.Id == decision.DestinationCityId
            && city.Owner == actor.Nation);
    }
}
