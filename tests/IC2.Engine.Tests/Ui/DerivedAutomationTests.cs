using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Core;
using IC2.Engine.Tests.Export;
using IC2.Slice.UI;
using Xunit;

namespace IC2.Engine.Tests.Ui;

public sealed class DerivedAutomationTests
{
    [Theory]
    [InlineData(0, 50, 50)]
    [InlineData(1000, 1000, 0)]
    [InlineData(1500, 1000, 0)]
    public void High_upkeep_respects_loaded_purse_limits_and_never_pulls_back_existing_money(int money, int cap, int expected)
    {
        var rules = CoreTestbed.Toy.Ruleset;
        rules = rules with { Economy = rules.Economy with { PurseCapPerUnit = cap },
            UnitTypes = ValueList.From(rules.UnitTypes.Select(t => t.Id == "light_infantry" ? t with { QuarterlyPrice = 100 } : t)) };
        var state = CoreTestbed.InitialState();
        var army = state.Armies.First(a => a.Nation == state.ActiveNationId) with { Money = money,
            Units = ValueList.Of(new UnitSlot(0, "light_infantry", 10000, 6, "Regulars")) };
        Assert.Equal(5000, ArmyAutoSupportPlanner.QuarterlyPurseTarget(army, rules));
        var proposal = ArmyAutoSupportPlanner.Fund(state, army, rules, new UserSettings { AutoFundArmies = true });
        if (expected == 0) Assert.Null(proposal); else Assert.Equal(expected, proposal?.Requested);
        Assert.Equal(money, army.Money);
    }

    [Theory]
    [InlineData(false, -500, 116)]
    [InlineData(false, 0, 116)]
    [InlineData(false, 50, 116)]
    [InlineData(true, -500, 0)]
    [InlineData(true, 0, 0)]
    [InlineData(true, 50, 50)]
    public void Army_funding_uses_regular_and_mercenary_quarterly_cost_and_existing_economy_policy(bool improved, int treasury, int amount)
    {
        var rules = GameDataLoader.LoadFile<Ruleset>(improved ? ExportedDataPaths.ImprovedRulesetFile : ExportedDataPaths.RulesetFile);
        // Pin known prices independently of the billing helper: 10,000 HI ->100;
        // 2,000 archers at quality8 ->16. Total target116, not mercenary pay alone.
        rules = rules with { UnitTypes = ValueList.From(rules.UnitTypes.Select(t => t.Id == "heavy_infantry"
            ? t with { QuarterlyPrice = 2 } : t.Id == "archers" ? t with { QuarterlyPrice = 1 } : t)) };
        var original = CoreTestbed.InitialState();
        var army = original.Armies.First(a => a.Nation == original.ActiveNationId) with { Money = 0,
            Units = ValueList.Of(new UnitSlot(0, "heavy_infantry", 10000, 6, "Regulars"),
                new UnitSlot(1, "archers", 2000, 8, "Mercenaries")) };
        var state = original with { RulesetId = rules.Id, Armies = ValueList.Of(army),
            Nations = ValueList.From(original.Nations.Select(n => n.Id == army.Nation
                ? n with { Treasury = treasury, Unity = 900, Wealth = 1000000 } : n)) };
        var settings = new UserSettings { AutoFundArmies = true, AutoPurseTargetTalents = 1, AutoTreasuryReserveTalents = 100000 };
        var proposal = ArmyAutoSupportPlanner.Fund(state, army, rules, settings);
        if (amount == 0) Assert.Null(proposal);
        else
        {
            Assert.NotNull(proposal);
            Assert.Equal(amount, proposal.Requested);
            var save = new SaveGame(state.SchemaVersion, "derived-fund", "derived-fund", state.ScenarioId, state.WorldId, state.RulesetId, state);
            var session = new GameSession(CoreTestbed.Toy.World, rules, CoreTestbed.Toy.Scenario with { RulesetId = rules.Id }, save);
            Assert.DoesNotContain(session.Submit(proposal.CommandLine).Lines, l => l.Contains("rejected", StringComparison.Ordinal));
            Assert.Equal(amount, session.State.ArmyById(army.Id)!.Money);
            Assert.Equal(treasury - amount, session.State.NationById(army.Nation)!.Treasury);
        }
        Assert.Equal(0, army.Money);
        Assert.Equal(treasury, state.NationById(army.Nation)!.Treasury);
    }

    [Fact]
    public void Supply_and_repair_fill_to_maximum_even_with_legacy_zero_targets_and_debt()
    {
        var data = CoreTestbed.Toy;
        var state = CoreTestbed.InitialState();
        var city = state.Cities.First(c => c.Owner == state.ActiveNationId) with { X = 2, Y = 1, SupplyTons = 1000 };
        var army = state.Armies.First(a => a.Nation == state.ActiveNationId) with { X = 3, Y = 2, SupplyTons = 0,
            Units = ValueList.Of(new UnitSlot(0, "light_infantry", 10000, 6, "Regulars")) };
        var fleet = state.Fleets.First(f => f.Nation == state.ActiveNationId) with { X = 3, Y = 2, Ships = 20,
            SupplyTons = 0, ConditionPercent = 50, CarriedArmyId = null, ConstructionTicksRemaining = null };
        state = state with { Cities = ValueList.From(state.Cities.Select(c => c.Id == city.Id ? city : c)),
            Armies = ValueList.Of(army), Fleets = ValueList.Of(fleet),
            Nations = ValueList.From(state.Nations.Select(n => n.Id == army.Nation
                ? n with { Treasury = -500, Unity = 900, Wealth = 1000000 } : n)) };
        var settings = new UserSettings { AutoSupplyArmies = true, AutoSupplyFleets = true, AutoRepairFleets = true,
            AutoSupplyTargetPercent = 0, AutoFleetSupplyTargetPercent = 0, AutoFleetRepairTargetPercent = 0,
            AutoTreasuryReserveTalents = 100000 };
        Assert.Equal(101, ArmyAutoSupportPlanner.NextSupply(state, army, data.Ruleset, settings, new HashSet<string>())?.Requested);
        Assert.Equal(160, FleetAutoSupportPlanner.NextSupply(state, fleet, data.Ruleset, settings, new HashSet<string>())?.Requested);
        var recoveryState = state with { Fleets = ValueList.Of(fleet with { SupplyTons = 160 }) };
        Assert.True(RetreatSupplyPlanner.Next(recoveryState, data.World, data.Ruleset, settings,
            new RetreatSupplyPlan(fleet.Id, fleet.Nation, true)).AtProvider);
        var repair = FleetAutoSupportPlanner.Repair(state, fleet, data.Ruleset, settings);
        Assert.NotNull(repair);
        Assert.Equal(50, repair.Requested);
        var save = new SaveGame(state.SchemaVersion, "derived-repair", "derived-repair", state.ScenarioId, state.WorldId, state.RulesetId, state);
        var session = new GameSession(data.World, data.Ruleset, data.Scenario, save);
        Assert.DoesNotContain(session.Submit(repair.CommandLine).Lines, l => l.Contains("rejected", StringComparison.Ordinal));
        Assert.Equal(100, session.State.FleetById(fleet.Id)!.ConditionPercent);
        Assert.Equal(0, session.State.FleetById(fleet.Id)!.Moves);
        Assert.Equal(-700, session.State.NationById(army.Nation)!.Treasury);
    }
}
