using System.Text.Json.Nodes;
using IC2.Engine.Ai;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Naval;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Ai;
using IC2.Engine.Tests.Battle.Commands;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Economy;

// T177: research500e521, FUN_0044f7e4/FUN_0044e5dc. Exact code boundaries;
// staged Wine observations remain candidates, not native Windows confirmation.
public sealed class FaithfulFleetSupportTests
{
    private static Ruleset Rules => EconomyTestbed.Ruleset with
    { Flags = EconomyTestbed.Ruleset.Flags with { AiEconomy = AiEconomyModel.Original } };
    private static FleetState Fleet(int purse = 99, int condition = 100, int ships = 90) =>
        new("fleet", "north", 0, 0, 0, ships, condition, purse, ships * 8, null, null, null, null);
    private static CityState City(int stock = 0) =>
        new("city", "City", 0, 0, "north", "north", 80, stock, 100, 10, 10, 0, ValueList<UnitSlot>.Empty);
    private static NationState Nation(int treasury) => AiScriptedStates.AiNation("north", AiScriptedStates.DefaultPersonality, treasury);
    private static AutomaticResupply.FleetResult Support(int purse, int treasury, int condition = 100, int ships = 90, Ruleset? rules = null)
    {
        var nation = Nation(treasury);
        return AutomaticResupply.ForFleet(Fleet(purse, condition, ships), City(), nation, nation, rules ?? Rules);
    }

    [Theory]
    [InlineData(99, 101, 199, 1)]
    [InlineData(100, 101, 100, 101)]
    [InlineData(99, 100, 99, 100)]
    [InlineData(99, 0, 99, 0)]
    [InlineData(1100, 101, 1100, 101)]
    public void Grant_is_additive_strict_and_does_not_sweep(int purse, int treasury, int expectedPurse, int expectedTreasury)
    {
        var result = Support(purse, treasury);
        Assert.Equal(expectedPurse, result.Fleet.Money);
        Assert.Equal(expectedTreasury, result.FleetNation.Treasury);
        Assert.Equal(100, result.Fleet.ConditionPercent);
        Assert.Equal(720, result.Fleet.SupplyTons);
        Assert.Equal(0, result.City.SupplyTons);
        Assert.Equal(0, result.AdmittedTons);
        Assert.Equal(0, result.TalentsPaid);
    }

    [Theory]
    [InlineData(94, 90, 101, 100, -7)]
    [InlineData(94, 91, 101, 100, -8)]
    [InlineData(94, 90, 0, 100, -108)]
    [InlineData(94, 90, -5, 100, -113)]
    [InlineData(95, 90, 101, 95, 101)]
    [InlineData(96, 90, 101, 96, 101)]
    public void Repair_is_independent_truncated_and_may_overdraw(int condition, int ships, int treasury, int expectedCondition, int expectedTreasury)
    {
        var result = Support(100, treasury, condition, ships);
        Assert.Equal(100, result.Fleet.Money);
        Assert.Equal(expectedCondition, result.Fleet.ConditionPercent);
        Assert.Equal(expectedTreasury, result.FleetNation.Treasury);
        Assert.Equal(0, result.AdmittedTons);
        Assert.Equal(0, result.City.SupplyTons);
    }

    [Fact]
    public void Combined_grant_precedes_independent_repair_and_central_mode_omits_only_grant()
    {
        var result = Support(99, 101, 94);
        Assert.Equal(199, result.Fleet.Money);
        Assert.Equal(-107, result.FleetNation.Treasury);
        Assert.Equal(100, result.Fleet.ConditionPercent);
        var central = Support(99, 101, 94, rules: Rules with { Flags = Rules.Flags with { EconomyPurses = EconomyPurseModel.CentralTreasury } });
        Assert.Equal(99, central.Fleet.Money);
        Assert.Equal(-7, central.FleetNation.Treasury);
        Assert.Equal(100, central.Fleet.ConditionPercent);
    }

    [Fact]
    public void Designed_fleet_and_original_army_keep_army_hygiene()
    {
        var designed = Rules with { Flags = Rules.Flags with { AiEconomy = AiEconomyModel.Designed } };
        var result = Support(99, 1, 94, rules: designed);
        Assert.Equal(599, result.Fleet.Money);
        Assert.Equal(-499, result.FleetNation.Treasury);
        Assert.Equal(94, result.Fleet.ConditionPercent);
        var army = new ArmyState("army", "north", 0, 0, 0, 60, 99, 1, null, null,
            ValueList.Of(new UnitSlot(0, "light_infantry", 100, 6, "Unit")));
        var nation = Nation(1);
        var supported = AutomaticResupply.ForArmy(army, City(), nation, nation, Rules);
        Assert.Equal(599, supported.Army.Money);
        Assert.Equal(-499, supported.ArmyNation.Treasury);
        Assert.Equal(1000, Support(1100, 1, rules: designed).Fleet.Money);
    }

    private static GameState State(FleetState fleet, CityState city, int treasury = 101) =>
        BattleCommandTestbed.StateWith(new[] { Nation(treasury) }, new[] { city }, fleets: new[] { fleet });

    [Theory]
    [InlineData(3, 1)]
    [InlineData(4, 0)]
    public void Pass_uses_strict_fleet_range_even_at_zero_moves(int distance, int transfers)
    {
        var state = State(Fleet(condition: 94), City() with { X = distance });
        var result = AiResupplyPass.Run(state, Rules, "north");
        Assert.Equal(transfers, result.FleetTransfers);
        Assert.Equal(0, result.ArmyTransfers);
        Assert.Equal(0, result.TonsMoved);
        Assert.Equal(0, result.TalentsPaid);
        Assert.Equal(transfers == 1 ? 199 : 99, result.State.Fleets[0].Money);
        Assert.Equal(transfers == 1 ? 100 : 94, result.State.Fleets[0].ConditionPercent);
        Assert.Equal(transfers == 1 ? -107 : 101, result.State.Nations[0].Treasury);
        Assert.Equal(0, result.State.Fleets[0].Moves);
        Assert.Equal(720, result.State.Fleets[0].SupplyTons);
        Assert.Equal(state.Cities, result.State.Cities);
        Assert.Equal(result, AiResupplyPass.Run(state, Rules, "north"));
    }

    [Theory]
    [InlineData(99, 100, 1, 199, 1)]
    [InlineData(100, 94, 1, 100, -7)]
    [InlineData(100, 100, 0, 100, 101)]
    public void Pass_writes_grant_only_repair_only_and_skips_unchanged(int purse, int condition, int count, int money, int treasury)
    {
        var result = AiResupplyPass.Run(State(Fleet(purse, condition), City()), Rules, "north");
        Assert.Equal(count, result.FleetTransfers);
        Assert.Equal(money, result.State.Fleets[0].Money);
        Assert.Equal(100, result.State.Fleets[0].ConditionPercent);
        Assert.Equal(treasury, result.State.Nations[0].Treasury);
        Assert.Equal(720, result.State.Fleets[0].SupplyTons);
        Assert.Equal(0, result.State.Cities[0].SupplyTons);
        Assert.Equal(0, result.TonsMoved);
        Assert.Equal(0, result.TalentsPaid);
    }

    [Fact]
    public void Multiple_noncoastal_providers_read_updated_support_and_supply()
    {
        var city = City(50) with { X = 4, Y = 3 };
        Assert.False(CoastalCity.IsCoastal(city, EconomyTestbed.Toy.World));
        var state = State(Fleet(condition: 94) with { SupplyTons = 0, X = 4, Y = 3 }, city, 1000);
        state = state with { Cities = ValueList.Of(city, city with { Id = "second" }) };
        var result = AiResupplyPass.Run(state, Rules, "north");
        Assert.Equal(2, result.FleetTransfers);
        Assert.Equal(100, result.TonsMoved);
        Assert.Equal(0, result.TalentsPaid);
        Assert.Equal(199, result.State.Fleets[0].Money);
        Assert.Equal(100, result.State.Fleets[0].ConditionPercent);
        Assert.Equal(100, result.State.Fleets[0].SupplyTons);
        Assert.Equal(792, result.State.Nations[0].Treasury);
        Assert.All(result.State.Cities, c => Assert.Equal(0, c.SupplyTons));
        var again = AiResupplyPass.Run(result.State, Rules, "north");
        Assert.Equal(0, again.FleetTransfers);
        Assert.Equal(result.State, again.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Construction_and_hostile_cities_do_not_support(bool construction)
    {
        var state = State(Fleet(condition: 94) with { ConstructionTicksRemaining = construction ? 1 : null }, City());
        if (!construction)
        {
            var south = Nation(500) with { Id = "south" };
            state = state with
            {
                Nations = ValueList.Of(state.Nations[0], south),
                Cities = ValueList.Of(state.Cities[0] with { Owner = "south" }),
                Relations = DiplomaticRelations.Uniform(ValueList.Of("north", "south"), Rules.Diplomacy.StateCodes.War),
            };
        }
        var result = AiResupplyPass.Run(state, Rules, "north");
        Assert.Equal(state, result.State);
        Assert.Equal(0, result.FleetTransfers);
        Assert.Equal(0, result.TonsMoved);
        Assert.Equal(0, result.TalentsPaid);
    }

    [Fact]
    public void Each_new_knob_is_consumed_and_naval_ceiling_divisor_are_reused()
    {
        var rules = Rules with { Economy = Rules.Economy with
        { FaithfulFleetPurseTopUpThreshold = 110, FaithfulFleetPurseTopUpAmount = 37, FaithfulFleetTreasuryAbove = 120,
          FaithfulFleetRepairConditionBelow = 97, FaithfulFleetResupplyDistanceBelow = 5 },
          Naval = Rules.Naval with { MaxConditionPercent = 110, RepairCostDivisor = 7 } };
        Assert.Equal(136, Support(99, 121, rules: rules).Fleet.Money);
        Assert.Equal(99, Support(99, 120, rules: rules).Fleet.Money);
        Assert.Equal(146, Support(109, 121, rules: rules).Fleet.Money);
        Assert.Equal(110, Support(110, 121, rules: rules).Fleet.Money);
        var repaired = Support(110, 0, 96, 91, rules);
        Assert.Equal(110, repaired.Fleet.ConditionPercent);
        Assert.Equal(-182, repaired.FleetNation.Treasury);
        Assert.Equal(97, Support(110, 0, 97, rules: rules).Fleet.ConditionPercent);
        var state = State(Fleet(), City() with { X = 4 }, treasury: 121);
        Assert.Equal(1, AiResupplyPass.Run(state, rules, "north").FleetTransfers);
        Assert.Equal(0, AiResupplyPass.Run(state, Rules, "north").FleetTransfers);
    }

    public static IEnumerable<object[]> Keys => new[] { "faithfulFleetResupplyDistanceBelow", "faithfulFleetPurseTopUpThreshold",
        "faithfulFleetPurseTopUpAmount", "faithfulFleetTreasuryAbove", "faithfulFleetRepairConditionBelow" }.Select(k => new object[] { k });
    [Theory]
    [MemberData(nameof(Keys))]
    public void Every_new_field_is_required_positive_and_typed(string key)
    {
        var json = File.ReadAllText(TestPaths.ToyRulesetFile);
        foreach (var value in new[] { 0, -1 })
        {
            var node = JsonNode.Parse(json)!;
            node["economy"]![key] = value;
            Assert.Contains(key, Assert.Throws<MalformedGameDataException>(() => GameDataLoader.Load<Ruleset>("invalid.json", node.ToJsonString())).Message);
        }
        var missing = JsonNode.Parse(json)!;
        missing["economy"]!.AsObject().Remove(key);
        Assert.Contains(key, Assert.Throws<MissingRequiredFieldException>(() => GameDataLoader.Load<Ruleset>("missing.json", missing.ToJsonString())).Message);
        var text = JsonNode.Parse(json)!;
        text["economy"]![key] = "100";
        Assert.Contains(key, Assert.Throws<MalformedGameDataException>(() => GameDataLoader.Load<Ruleset>("typed.json", text.ToJsonString())).Message);
    }

    [Theory]
    [InlineData("toy-ruleset")]
    [InlineData("classical-faithful")]
    [InlineData("improved")]
    public void Presets_load_explicit_defaults(string preset)
    {
        var rules = GameDataLoader.LoadFile<Ruleset>(Path.Combine(TestPaths.DataRoot, "rulesets", preset + ".json"));
        Assert.Equal(4, rules.Economy.FaithfulFleetResupplyDistanceBelow);
        Assert.Equal(100, rules.Economy.FaithfulFleetPurseTopUpThreshold);
        Assert.Equal(100, rules.Economy.FaithfulFleetPurseTopUpAmount);
        Assert.Equal(100, rules.Economy.FaithfulFleetTreasuryAbove);
        Assert.Equal(95, rules.Economy.FaithfulFleetRepairConditionBelow);
    }

    [Fact]
    public void Cross_field_limits_reject_overflow_and_above_ceiling_but_accept_exact_native_bound()
    {
        Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate("ceiling", Rules with
        { Economy = Rules.Economy with { FaithfulFleetRepairConditionBelow = 101 } }));
        Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate("overflow", Rules with
        { Economy = Rules.Economy with { FaithfulFleetPurseTopUpThreshold = int.MaxValue, FaithfulFleetPurseTopUpAmount = int.MaxValue } }));
        var exact = Rules with { Economy = Rules.Economy with
        { FaithfulFleetPurseTopUpThreshold = 32700, FaithfulFleetPurseTopUpAmount = 68 } };
        GameDataValidation.Validate("exact", exact);
        Assert.Equal(32767, Support(32699, 1000, rules: exact).Fleet.Money);
        Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate("one-too-many", exact with
        { Economy = exact.Economy with { FaithfulFleetPurseTopUpAmount = 69 } }));
        Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate("zero-divisor", Rules with
        { Naval = Rules.Naval with { RepairCostDivisor = 0 } }));
    }
}
