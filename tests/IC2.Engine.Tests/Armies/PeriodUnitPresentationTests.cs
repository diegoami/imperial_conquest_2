using System.Text.Json;
using System.Text.Json.Nodes;
using IC2.Engine.Armies;
using IC2.Engine.Armies.Commands;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Recruitment.Commands;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;
using Xunit;

namespace IC2.Engine.Tests.Armies;

public sealed class PeriodUnitPresentationTests
{
    internal static Ruleset PeriodRules() => ToyFixtures.Toy.Ruleset with
    {
        UnitTypes = new(ToyFixtures.Toy.Ruleset.UnitTypes.Select(t => t.Id == "light_cavalry"
            ? t with { Name = "Light chariots", Abbreviation = "Ch", BattalionLabel = "Chariots", VisualTypeId = "light_chariot" } : t)),
    };

    [Fact]
    public void Names_reuse_the_existing_type_specific_highest_ordinal_scan_and_split_spacing()
    {
        var f = ToyFixtures.Toy;
        var state = GameStateFactory.CreateInitial(f.World, f.Ruleset, f.Scenario);
        var army = state.Armies[0] with { Units = ValueList.Of(
            new UnitSlot(0, "light_cavalry", 5000, 6, "7th Chariots Battalion"),
            new UnitSlot(0, "light_cavalry", 1000, 6, "2nd Lancers Battalion")) };
        state = state with { Armies = ValueList.Of(army), ActiveSeatIndex = state.TurnOrder.ToList().IndexOf(army.Nation) };
        var rules = PeriodRules();
        Assert.Equal("8th Chariots Battalion", ArmyNaming.NextName(state, army.Nation, "light_cavalry", rules));
        Assert.Equal("8th Chariots  Battalion", ArmyNaming.NextSplitUnitName(state, army.Nation, "light_cavalry", rules));
        Assert.Equal("3rd Lancers Battalion", ArmyNaming.NextName(state, army.Nation, "light_cavalry"));
        var dispatch = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), rules, f.World, new RecordingEventSink());
        var split = dispatch.Dispatch(state, new SplitUnitCommand(army.Nation, army.Id, 0, 2000));
        Assert.True(split.IsAccepted, split.Rejection?.Message);
        Assert.Contains(split.State.ArmyById(army.Id)!.Units, u => u.Name == "8th Chariots  Battalion");
        Assert.Equal(army.TotalTroops, split.State.ArmyById(army.Id)!.TotalTroops);
    }

    [Fact]
    public void Mobilization_uses_the_period_label_through_the_real_command()
    {
        var f = ToyFixtures.Toy;
        var rules = PeriodRules();
        var state = GameStateFactory.CreateInitial(f.World, rules, f.Scenario);
        var nation = state.NationById("north")!;
        state = state with { Nations = new(state.Nations.Select(n => n.Id == nation.Id ? n with
            { RecruitmentSlots = ValueList.Of(new RecruitmentSlot(nation.CapitalCityId!, "light_cavalry", 1400, rules.Calendar.CityUnitStateCodeCap)) } : n)) };
        var dispatch = new CommandDispatcher(SystemRegistry.FromEngineAssembly(), rules, f.World, new RecordingEventSink());
        var mobilized = dispatch.Dispatch(state, new MobilizeRecruitSlotCommand("north", 0, "new-chariot-army"));
        Assert.True(mobilized.IsAccepted, mobilized.Rejection?.Message);
        Assert.Contains(mobilized.State.Armies.SelectMany(a => a.Units), u => u.UnitTypeId == "light_cavalry" && u.Name == "1st Chariots Battalion");
    }

    [Fact]
    public void Optional_metadata_roundtrips_without_changing_any_numeric_unit_rule_or_default_JSON()
    {
        var original = ToyFixtures.Toy.Ruleset;
        var rules = PeriodRules();
        var old = original.UnitTypeById("light_cavalry")!;
        var revised = rules.UnitTypeById(old.Id)!;
        Assert.Equal(old, revised with { Name = old.Name, Abbreviation = old.Abbreviation, BattalionLabel = null, VisualTypeId = null });
        var legacy = GameJson.Serialize(original);
        Assert.DoesNotContain("battalionLabel", legacy);
        Assert.DoesNotContain("visualTypeId", legacy);
        var json = GameJson.Serialize(rules);
        SchemaValidator.Validate<Ruleset>("period", JsonNode.Parse(json));
        var roundtrip = JsonSerializer.Deserialize<Ruleset>(json, GameJson.Options)!;
        GameDataValidation.Validate("period", roundtrip);
        Assert.Equal(rules, roundtrip);
    }

    [Theory]
    [InlineData("", "light_chariot")]
    [InlineData(" Chariots", "light_chariot")]
    [InlineData("Chariots\n", "light_chariot")]
    [InlineData("Chariots", "../horse")]
    [InlineData("Chariots", "")]
    [InlineData("Chariots", "Uppercase")]
    public void Invalid_optional_presentation_is_a_typed_data_error(string label, string visual)
    {
        var rules = PeriodRules();
        rules = rules with { UnitTypes = new(rules.UnitTypes.Select(t => t.Id == "light_cavalry" ? t with { BattalionLabel = label, VisualTypeId = visual } : t)) };
        Assert.Throws<MalformedGameDataException>(() => GameDataValidation.Validate("period", rules));
    }
}
