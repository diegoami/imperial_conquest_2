using IC2.Engine.Model;
using IC2.Engine.Economy;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Export;
using IC2.Engine.Tests.Import;
using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

public sealed class ArmyAutoSupportPlannerTests
{
    private static (GameState State, ArmyState Army, CityState City) NearOwnCity()
    {
        var state = new GameSession(RealGameData.World, RealGameData.Ruleset, RealGameData.Scenario,
            seedOverride: 7, humanSeatNationId: "rome").State;
        var army = state.Armies.First(a => a.Nation == "rome");
        var city = state.Cities.First(c => c.Owner == "rome");
        army = army with { X = city.X, Y = city.Y, SupplyTons = 0, Money = 0 };
        city = city with { SupplyTons = 250 };
        state = state with
        {
            Armies = ValueList.From(state.Armies.Select(a => a.Id == army.Id ? army : a)),
            Cities = ValueList.From(state.Cities.Select(c => c.Id == city.Id ? city : c)),
        };
        return (state, army, city);
    }

    [Fact]
    public void Both_switches_are_off_by_default_and_planning_changes_nothing()
    {
        var (state, army, _) = NearOwnCity();
        var before = state;
        var settings = new UserSettings();

        Assert.Null(ArmyAutoSupportPlanner.NextSupply(state, army, RealGameData.Ruleset, settings,
            new HashSet<string>()));
        Assert.Null(ArmyAutoSupportPlanner.Fund(state, army, RealGameData.Ruleset, settings));
        Assert.Equal(before, state);
        Assert.Equal(before.RandomSeed, state.RandomSeed);
    }

    [Fact]
    public void Supply_uses_own_city_then_owned_fleet_and_never_pays_a_foreign_city()
    {
        var (state, army, own) = NearOwnCity();
        var settings = new UserSettings { AutoSupplyArmies = true };
        var first = ArmyAutoSupportPlanner.NextSupply(state, army, RealGameData.Ruleset, settings,
            new HashSet<string>());
        Assert.NotNull(first);
        Assert.Equal($"buy {army.Id} {own.Id} {first.Requested}", first.CommandLine);

        var foreign = state.Cities.First(c => c.Owner != "rome") with
        { X = own.X, Y = own.Y, SupplyTons = 900 };
        var fleet = state.Fleets.First() with
        { Nation = "rome", X = own.X, Y = own.Y, SupplyTons = 90, ConstructionTicksRemaining = null };
        state = state with
        {
            Cities = ValueList.From(state.Cities.Select(c => c.Id == own.Id ? own with { SupplyTons = 0 }
                : c.Id == foreign.Id ? foreign : c)),
            Fleets = ValueList.From(state.Fleets.Select(f => f.Id == fleet.Id ? fleet : f)),
            Armies = ValueList.From(state.Armies.Select(a => a.Id == army.Id
                ? army with { AboardFleetId = fleet.Id } : a)),
        };
        army = state.ArmyById(army.Id)!;

        var fromFleet = ArmyAutoSupportPlanner.NextSupply(state, army, RealGameData.Ruleset, settings,
            new HashSet<string>());
        Assert.NotNull(fromFleet);
        Assert.Equal($"buy {army.Id} fleet {fleet.Id} 90", fromFleet.CommandLine);
        Assert.Null(ArmyAutoSupportPlanner.NextSupply(state, army, RealGameData.Ruleset, settings,
            new HashSet<string> { fromFleet.ProviderKey! }));
    }

    [Fact]
    public void Competing_armies_read_the_remaining_provider_stock_and_treasury()
    {
        var (state, first, city) = NearOwnCity();
        var second = first with { Id = "second-army" };
        state = state with
        {
            Armies = ValueList.Of(first, second),
            Cities = ValueList.From(state.Cities.Select(c => c.Id == city.Id
                ? c with { SupplyTons = 50 } : c)),
            Nations = ValueList.From(state.Nations.Select(n => n.Id == "rome"
                ? n with { Treasury = 2_500 } : n)),
        };
        var settings = new UserSettings
        {
            AutoSupplyArmies = true, AutoFundArmies = true,
            AutoPurseTargetTalents = 900, AutoTreasuryReserveTalents = 2_000,
        };
        Assert.Equal(50, ArmyAutoSupportPlanner.NextSupply(state, first, RealGameData.Ruleset, settings,
            new HashSet<string>())?.Requested);
        var demand = Math.Min(ArmyUpkeep.Compute(first.Units, RealGameData.Ruleset), RealGameData.Ruleset.Economy.PurseCapPerUnit);
        Assert.Equal(demand, ArmyAutoSupportPlanner.Fund(state, first, RealGameData.Ruleset, settings)?.Requested);

        state = state with
        {
            Cities = ValueList.From(state.Cities.Select(c => c.Owner == "rome"
                ? c with { SupplyTons = 0 } : c)),
            Nations = ValueList.From(state.Nations.Select(n => n.Id == "rome"
                ? n with { Treasury = 2_000 } : n)),
        };
        Assert.Null(ArmyAutoSupportPlanner.NextSupply(state with { Fleets = ValueList<FleetState>.Empty },
            second, RealGameData.Ruleset, settings, new HashSet<string>()));
        Assert.Equal(demand, ArmyAutoSupportPlanner.Fund(state, second, RealGameData.Ruleset, settings)?.Requested);
    }

    [Fact]
    public void Legacy_target_percent_is_ignored_and_funding_uses_upkeep_within_the_purse_cap()
    {
        var (state, army, _) = NearOwnCity();
        var settings = new UserSettings
        {
            AutoSupplyArmies = true, AutoFundArmies = true,
            AutoSupplyTargetPercent = 25, AutoPurseTargetTalents = 100_000,
            AutoTreasuryReserveTalents = 0,
        };
        var capacity = IC2.Engine.Economy.SupplyCapacity.ArmyDialogCapacityTons(army.TotalTroops,
            RealGameData.Ruleset);
        var target = Math.Min(capacity, 250);
        Assert.Equal(target, ArmyAutoSupportPlanner.NextSupply(state, army, RealGameData.Ruleset,
            settings, new HashSet<string>())?.Requested);
        var rich = state with { Nations = ValueList.From(state.Nations.Select(n => n.Id == "rome"
            ? n with { Treasury = 1_000_000 } : n)) };
        Assert.Equal(Math.Min(ArmyUpkeep.Compute(army.Units, RealGameData.Ruleset), RealGameData.Ruleset.Economy.PurseCapPerUnit),
            ArmyAutoSupportPlanner.Fund(rich, army, RealGameData.Ruleset, settings)?.Requested);
    }

    [Fact]
    public void Improved_ruleset_uses_the_same_ordinary_owned_provider_and_money_commands()
    {
        var ruleset = GameDataLoader.LoadFile<Ruleset>(ExportedDataPaths.ImprovedRulesetFile);
        var (state, army, city) = NearOwnCity();
        var settings = new UserSettings { AutoSupplyArmies = true, AutoFundArmies = true,
            AutoTreasuryReserveTalents = 0 };
        var supply = ArmyAutoSupportPlanner.NextSupply(state, army, ruleset, settings,
            new HashSet<string>());
        Assert.NotNull(supply);
        Assert.StartsWith($"buy {army.Id} {city.Id} ", supply.CommandLine);
        Assert.StartsWith($"transfer-money {army.Id} ",
            ArmyAutoSupportPlanner.Fund(state, army, ruleset, settings)!.CommandLine);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Planned_commands_are_accepted_by_the_real_session_under_both_rulesets(bool improved)
    {
        var ruleset = improved
            ? GameDataLoader.LoadFile<Ruleset>(ExportedDataPaths.ImprovedRulesetFile)
            : RealGameData.Ruleset;
        var (state, army, _) = NearOwnCity();
        state = state with
        {
            RulesetId = ruleset.Id,
            ActiveSeatIndex = state.TurnOrder.ToList().IndexOf("rome"),
            Nations = ValueList.From(state.Nations.Select(n => n.Id == "rome"
                ? n with { Treasury = 2_500 } : n)),
        };
        var scenario = RealGameData.Scenario with { RulesetId = ruleset.Id };
        var save = new SaveGame(state.SchemaVersion, "auto-support", "auto-support",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(RealGameData.World, ruleset, scenario, save);
        var settings = new UserSettings { AutoSupplyArmies = true, AutoFundArmies = true,
            AutoPurseTargetTalents = 900, AutoTreasuryReserveTalents = 2_000 };

        var supply = ArmyAutoSupportPlanner.NextSupply(session.State, army, ruleset, settings,
            new HashSet<string>());
        Assert.NotNull(supply);
        Assert.DoesNotContain(session.Submit(supply.CommandLine).Lines,
            line => line.StartsWith("Refused:", StringComparison.Ordinal));
        Assert.True(session.State.ArmyById(army.Id)!.SupplyTons > 0);

        var funding = ArmyAutoSupportPlanner.Fund(session.State, session.State.ArmyById(army.Id)!,
            ruleset, settings);
        Assert.NotNull(funding);
        Assert.DoesNotContain(session.Submit(funding.CommandLine).Lines,
            line => line.StartsWith("Refused:", StringComparison.Ordinal));
        var demand = Math.Min(ArmyUpkeep.Compute(army.Units, ruleset), ruleset.Economy.PurseCapPerUnit);
        Assert.Equal(demand, session.State.ArmyById(army.Id)!.Money);
        Assert.Equal(2_500 - demand, session.State.NationById("rome")!.Treasury);
    }

    [Fact]
    public void Funding_ignores_legacy_target_and_reserve_and_keeps_already_sufficient_purses()
    {
        var (state, army, _) = NearOwnCity();
        var nation = state.NationById("rome")!;
        state = state with { Nations = ValueList.From(state.Nations.Select(n => n.Id == nation.Id
            ? n with { Treasury = 2_500 } : n)) };
        var settings = new UserSettings
        {
            AutoFundArmies = true, AutoPurseTargetTalents = 900, AutoTreasuryReserveTalents = 2_000,
        };

        var proposal = ArmyAutoSupportPlanner.Fund(state, army, RealGameData.Ruleset, settings);
        Assert.NotNull(proposal);
        var demand = Math.Min(ArmyUpkeep.Compute(army.Units, RealGameData.Ruleset), RealGameData.Ruleset.Economy.PurseCapPerUnit);
        Assert.Equal($"transfer-money {army.Id} {demand}", proposal.CommandLine);
        Assert.Null(ArmyAutoSupportPlanner.Fund(state, army with { Money = demand }, RealGameData.Ruleset, settings));
        Assert.Equal(2_500, state.NationById("rome")!.Treasury);
    }
}
