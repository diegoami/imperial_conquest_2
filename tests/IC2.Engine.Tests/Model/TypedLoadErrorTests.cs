using System.Text.Json.Nodes;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Model;

/// <summary>
/// DoD 3: "A malformed file, an unknown required field, and a version mismatch each produce a distinct
/// typed error, one negative test each — never a silent default."
/// </summary>
/// <remarks>
/// Four error types are exercised rather than three: "an unknown required field" is covered from both
/// sides — a required field the document does <em>not</em> declare
/// (<see cref="MissingRequiredFieldException"/>) and a field the <em>schema</em> does not declare
/// (<see cref="UnknownFieldException"/>) — because either one silently defaulting is the failure this
/// DoD line exists to prevent.
/// </remarks>
public class TypedLoadErrorTests
{
    [Fact]
    public void A_malformed_file_throws_MalformedGameDataException()
    {
        const string broken = "{ \"schemaVersion\": 1, \"id\": \"toy\", ";

        var error = Assert.Throws<MalformedGameDataException>(
            () => GameDataLoader.Load<Scenario>("broken.json", broken));

        Assert.Equal("broken.json", error.DocumentPath);
        Assert.Contains("not valid JSON", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_required_field_throws_MissingRequiredFieldException()
    {
        var document = LoadToyScenarioNode();
        document.Remove("rulesetId");

        var error = Assert.Throws<MissingRequiredFieldException>(
            () => GameDataLoader.Load<Scenario>("toy-3city.json", document.ToJsonString()));

        Assert.Equal("rulesetId", error.FieldPath);
        Assert.Equal(nameof(Scenario), error.DeclaringType);
    }

    [Fact]
    public void A_missing_required_field_nested_in_a_list_names_its_full_path()
    {
        var document = LoadToyScenarioNode();
        ((JsonObject)document["seats"]![1]!).Remove("control");

        var error = Assert.Throws<MissingRequiredFieldException>(
            () => GameDataLoader.Load<Scenario>("toy-3city.json", document.ToJsonString()));

        Assert.Equal("seats[1].control", error.FieldPath);
        Assert.Equal(nameof(Seat), error.DeclaringType);
    }

    [Fact]
    public void An_unrecognised_field_throws_UnknownFieldException_rather_than_being_dropped()
    {
        var document = LoadToyScenarioNode();
        document["rulesetID"] = "toy-ruleset";

        var error = Assert.Throws<UnknownFieldException>(
            () => GameDataLoader.Load<Scenario>("toy-3city.json", document.ToJsonString()));

        Assert.Equal("rulesetID", error.FieldPath);
        Assert.Equal(nameof(Scenario), error.DeclaringType);
    }

    [Fact]
    public void A_version_mismatch_throws_SchemaVersionMismatchException()
    {
        var document = LoadToyScenarioNode();
        document["schemaVersion"] = GameDataSchema.CurrentVersion + 1;

        var error = Assert.Throws<SchemaVersionMismatchException>(
            () => GameDataLoader.Load<Scenario>("toy-3city.json", document.ToJsonString()));

        Assert.Equal(GameDataSchema.CurrentVersion + 1, error.Found);
        Assert.Equal(GameDataSchema.CurrentVersion, error.Supported);
    }

    [Fact]
    public void A_missing_schema_version_is_reported_as_a_missing_required_field()
    {
        var document = LoadToyScenarioNode();
        document.Remove("schemaVersion");

        var error = Assert.Throws<MissingRequiredFieldException>(
            () => GameDataLoader.Load<Scenario>("toy-3city.json", document.ToJsonString()));

        Assert.Equal("schemaVersion", error.FieldPath);
    }

    [Fact]
    public void The_four_error_types_are_distinct_and_share_one_base()
    {
        var types = new[]
        {
            typeof(MalformedGameDataException),
            typeof(MissingRequiredFieldException),
            typeof(UnknownFieldException),
            typeof(SchemaVersionMismatchException),
        };

        Assert.Equal(types.Length, types.Distinct().Count());
        Assert.All(types, t => Assert.True(typeof(GameDataException).IsAssignableFrom(t)));
    }

    [Fact]
    public void An_unreadable_enum_value_is_malformed_not_a_default()
    {
        var document = LoadToyScenarioNode();
        ((JsonObject)document["seats"]![0]!)["control"] = "chaos";

        var error = Assert.Throws<MalformedGameDataException>(
            () => GameDataLoader.Load<Scenario>("toy-3city.json", document.ToJsonString()));

        Assert.Equal("toy-3city.json", error.DocumentPath);
    }

    [Fact]
    public void A_null_where_the_model_requires_a_value_is_malformed_not_a_default()
    {
        var document = LoadToyScenarioNode();
        document["turnLimit"] = null;   // legal: turnLimit is nullable
        document["blindHotseat"] = null; // not legal: a bool is not nullable

        var error = Assert.Throws<MalformedGameDataException>(
            () => GameDataLoader.Load<Scenario>("toy-3city.json", document.ToJsonString()));

        Assert.Contains("blindHotseat", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_overlong_terrain_run_is_malformed_not_an_untyped_runtime_exception()
    {
        // The run count is chosen to overflow `written + run.Count` if the guard adds rather than
        // subtracts: the sum wraps negative, slips past the check, and the fill loop runs off the end
        // of the array as an IndexOutOfRangeException, which is outside the typed hierarchy DoD 3
        // requires every load failure to stay inside.
        var document = LoadToyWorldNode();
        document["terrain"] = new JsonObject
        {
            ["encoding"] = "runLength",
            ["runs"] = new JsonArray(
                new JsonObject { ["code"] = 2, ["count"] = 1 },
                new JsonObject { ["code"] = 2, ["count"] = int.MaxValue }),
            ["data"] = null,
        };

        var error = Assert.Throws<MalformedGameDataException>(
            () => GameDataLoader.Load<World>("toy-3city.json", document.ToJsonString()));

        Assert.Contains("cells", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_terrain_grid_of_the_wrong_length_is_malformed()
    {
        var document = LoadToyWorldNode();
        document["terrain"] = new JsonObject
        {
            ["encoding"] = "runLength",
            ["runs"] = new JsonArray(new JsonObject { ["code"] = 2, ["count"] = 3 }),
            ["data"] = null,
        };

        Assert.Throws<MalformedGameDataException>(
            () => GameDataLoader.Load<World>("toy-3city.json", document.ToJsonString()));
    }

    [Fact]
    public void A_state_naming_a_nation_that_does_not_exist_does_not_load()
    {
        var save = ToyFixtures.NonTrivialSave();
        var cities = save.State.Cities.ToList();
        cities[0] = cities[0] with { Owner = "atlantis" };
        var broken = save with { State = save.State with { Cities = ValueList.From(cities) } };

        var error = Assert.Throws<UnresolvedReferenceException>(
            () => GameDataLoader.Load<SaveGame>("save.json", GameJson.Serialize(broken)));

        Assert.Equal("nation", error.Kind);
        Assert.Equal("atlantis", error.Id);
    }

    [Fact]
    public void An_army_whose_nation_does_not_exist_does_not_load()
    {
        var save = ToyFixtures.NonTrivialSave();
        var armies = save.State.Armies.ToList();
        armies[0] = armies[0] with { Nation = "atlantis" };
        var broken = save with { State = save.State with { Armies = ValueList.From(armies) } };

        Assert.Throws<UnresolvedReferenceException>(
            () => GameDataLoader.Load<SaveGame>("save.json", GameJson.Serialize(broken)));
    }

    [Fact]
    public void A_recruitment_slot_naming_a_city_that_does_not_exist_does_not_load()
    {
        var save = ToyFixtures.NonTrivialSave();
        var nations = save.State.Nations.ToList();
        var northIndex = nations.FindIndex(n => n.Id == "north");
        var slot = nations[northIndex].RecruitmentSlots[0] with { TargetCityId = "atlantis" };
        nations[northIndex] = nations[northIndex] with { RecruitmentSlots = ValueList.Of(slot) };
        var broken = save with { State = save.State with { Nations = ValueList.From(nations) } };

        var error = Assert.Throws<UnresolvedReferenceException>(
            () => GameDataLoader.Load<SaveGame>("save.json", GameJson.Serialize(broken)));

        Assert.Equal("city", error.Kind);
        Assert.Equal("atlantis", error.Id);
    }

    [Fact]
    public void A_pending_offer_naming_a_nation_that_does_not_exist_does_not_load()
    {
        var save = ToyFixtures.NonTrivialSave();
        var broken = save with
        {
            State = save.State with { PendingOffer = new PendingDiplomaticOffer("atlantis", 1) },
        };

        var error = Assert.Throws<UnresolvedReferenceException>(
            () => GameDataLoader.Load<SaveGame>("save.json", GameJson.Serialize(broken)));

        Assert.Equal("nation", error.Kind);
        Assert.Equal("atlantis", error.Id);
    }

    [Fact]
    public void A_one_sided_embark_link_does_not_load()
    {
        var save = ToyFixtures.NonTrivialSave();
        var fleets = save.State.Fleets.ToList();
        var index = fleets.FindIndex(f => f.IsCarryingArmy);
        fleets[index] = fleets[index] with { CarriedArmyId = null };
        var broken = save with { State = save.State with { Fleets = ValueList.From(fleets) } };

        var error = Assert.Throws<MalformedGameDataException>(
            () => GameDataLoader.Load<SaveGame>("save.json", GameJson.Serialize(broken)));

        Assert.Contains("no army", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_news_log_whose_index_disagrees_with_its_slots_does_not_load()
    {
        var save = ToyFixtures.NonTrivialSave();
        var broken = save with
        {
            State = save.State with { NewsLog = save.State.NewsLog with { MostRecentSlot = 0 } },
        };

        Assert.Throws<MalformedGameDataException>(
            () => GameDataLoader.Load<SaveGame>("save.json", GameJson.Serialize(broken)));
    }

    [Fact]
    public void A_saves_nested_state_version_is_checked_too()
    {
        var save = ToyFixtures.NonTrivialSave();
        var broken = save with { State = save.State with { SchemaVersion = GameDataSchema.CurrentVersion + 98 } };

        var error = Assert.Throws<SchemaVersionMismatchException>(
            () => GameDataLoader.Load<SaveGame>("save.json", GameJson.Serialize(broken)));

        Assert.Equal(GameDataSchema.CurrentVersion + 98, error.Found);
    }

    [Fact]
    public void A_calendar_whose_start_week_can_never_reach_the_season_boundary_does_not_load()
    {
        var document = LoadToyRulesetNode();
        ((JsonObject)document["calendar"]!)["startWeek"] = 2;

        var error = Assert.Throws<MalformedGameDataException>(
            () => GameDataLoader.Load<Ruleset>("toy-ruleset.json", document.ToJsonString()));

        Assert.Contains("never reaches", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Reading_a_directory_instead_of_a_file_is_a_typed_error()
    {
        // File.ReadAllText throws UnauthorizedAccessException here, not IOException; if the loader only
        // caught IOException this would escape the GameDataException hierarchy that
        // GameDataRepository.Load's contract promises.
        Assert.Throws<MalformedGameDataException>(
            () => GameDataLoader.LoadFile<World>(TestPaths.DataRoot));
    }

    private static JsonObject LoadToyScenarioNode() =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(TestPaths.ToyScenarioFile))!;

    private static JsonObject LoadToyWorldNode() =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(TestPaths.ToyWorldFile))!;

    private static JsonObject LoadToyRulesetNode() =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(TestPaths.ToyRulesetFile))!;
}
