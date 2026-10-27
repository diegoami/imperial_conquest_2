using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Export;
using IC2.Engine.Tests.Import;
using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

public sealed class FleetAutoSupportPlannerTests
{
    private static (GameState State, FleetState Fleet, CityState Town) NearOwnedTown()
    {
        var state = new GameSession(RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario,
            seedOverride: 11, humanSeatNationId: "rome").State;
        var town = state.Cities.First(c => c.Owner == "rome") with { SupplyTons = 500 };
        var fleet = state.Fleets.First() with
        {
            Nation = "rome", X = town.X, Y = town.Y, Ships = 10,
            SupplyTons = 0, ConditionPercent = 50, Moves = 5,
            ConstructionTicksRemaining = null, CarriedArmyId = null,
        };
        state = state with
        {
            Cities = ValueList.From(state.Cities.Select(c => c.Id == town.Id ? town : c)),
            Fleets = ValueList.Of(fleet),
            Nations = ValueList.From(state.Nations.Select(n => n.Id == "rome"
                ? n with { Treasury = 2_000 } : n)),
        };
        return (state, fleet, town);
    }

    [Fact]
    public void Defaults_are_off_and_planning_leaves_state_and_rng_unchanged()
    {
        var (state, fleet, _) = NearOwnedTown();
        var before = state with { };
        var settings = new UserSettings();
        Assert.Null(FleetAutoSupportPlanner.NextSupply(state, fleet, RealGameData.Ruleset,
            settings, new HashSet<string>()));
        Assert.Null(FleetAutoSupportPlanner.Repair(state, fleet, RealGameData.Ruleset, settings));
        Assert.Equal(before, state);
        Assert.Equal(before.RandomSeed, state.RandomSeed);
    }

    [Fact]
    public void Supply_uses_owned_town_only_and_respects_target_stock_and_attempted_cities()
    {
        var (state, fleet, town) = NearOwnedTown();
        var settings = new UserSettings { AutoSupplyFleets = true, AutoFleetSupplyTargetPercent = 50 };
        var capacity = IC2.Engine.Economy.SupplyCapacity.FleetCapacityTons(fleet.Ships, RealGameData.Ruleset);
        var proposed = FleetAutoSupportPlanner.NextSupply(state, fleet, RealGameData.Ruleset,
            settings, new HashSet<string>());
        Assert.NotNull(proposed);
        Assert.Equal(capacity, proposed.Requested);
        Assert.Equal($"buy-fleet-supply {fleet.Id} {town.Id} {proposed.Requested}", proposed.CommandLine);
        Assert.Null(FleetAutoSupportPlanner.NextSupply(state, fleet, RealGameData.Ruleset,
            settings, new HashSet<string> { town.Id }));
        Assert.Null(FleetAutoSupportPlanner.NextSupply(state,
            fleet with { ConstructionTicksRemaining = 2 }, RealGameData.Ruleset,
            settings, new HashSet<string>()));

        var foreign = state.Cities.First(c => c.Owner != "rome") with
        { X = town.X, Y = town.Y, SupplyTons = 900 };
        var noOwnStock = state with { Cities = ValueList.From(state.Cities.Select(c => c.Id == town.Id
            ? town with { SupplyTons = 0 } : c.Id == foreign.Id ? foreign : c)) };
        Assert.Null(FleetAutoSupportPlanner.NextSupply(noOwnStock, fleet, RealGameData.Ruleset,
            settings, new HashSet<string>()));
    }

    [Fact]
    public void Repair_respects_town_cargo_construction_and_full_condition_without_a_reserve()
    {
        var (state, fleet, _) = NearOwnedTown();
        var settings = new UserSettings { AutoRepairFleets = true,
            AutoFleetRepairTargetPercent = 100, AutoTreasuryReserveTalents = 1_995 };
        var proposal = FleetAutoSupportPlanner.Repair(state, fleet, RealGameData.Ruleset, settings);
        Assert.NotNull(proposal);
        var available = state.NationById("rome")!.Treasury - settings.AutoTreasuryReserveTalents;
        var cost = fleet.Ships * proposal.Requested / RealGameData.Ruleset.Naval.RepairCostDivisor;
        Assert.True(cost > available, "the obsolete reserve does not limit repair");
        Assert.Equal(RealGameData.Ruleset.Naval.MaxConditionPercent - fleet.ConditionPercent, proposal.Requested);
        Assert.Equal($"repair-fleet {fleet.Id} {proposal.Requested}", proposal.CommandLine);
        Assert.Null(FleetAutoSupportPlanner.Repair(state, fleet with { CarriedArmyId = "army-0" },
            RealGameData.Ruleset, settings));
        Assert.Null(FleetAutoSupportPlanner.Repair(state, fleet with { ConstructionTicksRemaining = 2 },
            RealGameData.Ruleset, settings));
        Assert.Null(FleetAutoSupportPlanner.Repair(state, fleet with { ConditionPercent = 100 },
            RealGameData.Ruleset, settings));
        Assert.Null(FleetAutoSupportPlanner.Repair(state with { Cities = ValueList.From(state.Cities
            .Select(c => c.Owner == "rome" ? c with { Owner = "gaul" } : c)) }, fleet,
            RealGameData.Ruleset, settings));
    }

    [Fact]
    public void Improved_uses_the_same_town_only_commands_and_live_ruleset_limits()
    {
        var ruleset = GameDataLoader.LoadFile<Ruleset>(ExportedDataPaths.ImprovedRulesetFile);
        var (state, fleet, town) = NearOwnedTown();
        var settings = new UserSettings { AutoSupplyFleets = true, AutoRepairFleets = true,
            AutoTreasuryReserveTalents = 0 };
        Assert.StartsWith($"buy-fleet-supply {fleet.Id} {town.Id} ",
            FleetAutoSupportPlanner.NextSupply(state, fleet, ruleset, settings,
                new HashSet<string>())!.CommandLine);
        Assert.StartsWith($"repair-fleet {fleet.Id} ",
            FleetAutoSupportPlanner.Repair(state, fleet, ruleset, settings)!.CommandLine);
    }

    [Fact]
    public void Competing_fleets_read_remaining_town_stock_and_treasury()
    {
        var (state, first, town) = NearOwnedTown();
        var second = first with { Id = "second-fleet" };
        state = state with
        {
            Fleets = ValueList.Of(first, second),
            Cities = ValueList.From(state.Cities.Select(c => c.Owner == "rome"
                ? c with { SupplyTons = c.Id == town.Id ? 50 : 0 } : c)),
        };
        var settings = new UserSettings { AutoSupplyFleets = true, AutoRepairFleets = true,
            AutoTreasuryReserveTalents = 1_900 };
        Assert.Equal(50, FleetAutoSupportPlanner.NextSupply(state, first, RealGameData.Ruleset,
            settings, new HashSet<string>())?.Requested);
        Assert.NotNull(FleetAutoSupportPlanner.Repair(state, first, RealGameData.Ruleset, settings));

        state = state with
        {
            Cities = ValueList.From(state.Cities.Select(c => c.Owner == "rome"
                ? c with { SupplyTons = 0 } : c)),
            Nations = ValueList.From(state.Nations.Select(n => n.Id == "rome"
                ? n with { Treasury = 1_900 } : n)),
        };
        Assert.Null(FleetAutoSupportPlanner.NextSupply(state, second, RealGameData.Ruleset,
            settings, new HashSet<string>()));
        Assert.NotNull(FleetAutoSupportPlanner.Repair(state, second, RealGameData.Ruleset, settings));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Planned_town_commands_are_accepted_under_both_rulesets(bool improved)
    {
        var ruleset = improved
            ? GameDataLoader.LoadFile<Ruleset>(ExportedDataPaths.ImprovedRulesetFile)
            : RealGameData.Ruleset;
        var (state, fleet, _) = NearOwnedTown();
        state = state with
        {
            RulesetId = ruleset.Id,
            ActiveSeatIndex = state.TurnOrder.ToList().IndexOf("rome"),
        };
        var scenario = RealGameData.Scenario with { RulesetId = ruleset.Id };
        var save = new SaveGame(state.SchemaVersion, "fleet-auto", "fleet-auto",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(RealGameData.World, ruleset, scenario, save);
        var settings = new UserSettings { AutoSupplyFleets = true, AutoRepairFleets = true,
            AutoTreasuryReserveTalents = 1_900 };

        var supply = FleetAutoSupportPlanner.NextSupply(session.State, fleet, ruleset, settings,
            new HashSet<string>());
        Assert.NotNull(supply);
        Assert.DoesNotContain(session.Submit(supply.CommandLine).Lines,
            line => line.StartsWith("Refused:", StringComparison.Ordinal));
        Assert.True(session.State.FleetById(fleet.Id)!.SupplyTons > 0);

        var repair = FleetAutoSupportPlanner.Repair(session.State, session.State.FleetById(fleet.Id)!,
            ruleset, settings);
        Assert.NotNull(repair);
        Assert.DoesNotContain(session.Submit(repair.CommandLine).Lines,
            line => line.StartsWith("Refused:", StringComparison.Ordinal));
        Assert.True(session.State.FleetById(fleet.Id)!.ConditionPercent > fleet.ConditionPercent);
        Assert.Equal(0, session.State.FleetById(fleet.Id)!.Moves);
        Assert.True(session.State.NationById("rome")!.Treasury >= settings.AutoTreasuryReserveTalents);
    }
}
