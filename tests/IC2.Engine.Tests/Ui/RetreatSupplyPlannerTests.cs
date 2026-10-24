using IC2.Engine.Economy;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Tests.Import;
using IC2.Slice.UI;
using Xunit;

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
        var (state, army, capital, _, _) = ArmyStateAt();
        var initial = GameStateFactory.CreateInitial(RealGameData.World, RealGameData.Ruleset,
            RealGameData.Scenario);
        var fleet = initial.Fleets[0] with
        {
            Id = "recovery-fleet", Nation = army.Nation, X = capital.X + 1, Y = capital.Y,
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
            Cities = ValueList.Of(capital with { SupplyTons = 0 }),
            Nations = ValueList.From(state.Nations.Select(n => n.Id == army.Nation
                ? n with { Treasury = 10_000 } : n)),
        };
        var plan = new RetreatSupplyPlan(fleet.Id, fleet.Nation, IsFleet: true);
        var before = GameStateHash.Compute(state);
        var decision = RetreatSupplyPlanner.Next(state, RealGameData.World,
            RealGameData.Ruleset, new UserSettings(), plan);
        Assert.True(decision.AtProvider);
        Assert.False(decision.Completed);
        Assert.Equal(capital.Id, decision.DestinationCityId);
        Assert.Equal(before, GameStateHash.Compute(state));

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
}
