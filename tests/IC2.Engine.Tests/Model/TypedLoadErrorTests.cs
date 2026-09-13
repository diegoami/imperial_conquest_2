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

    private static JsonObject LoadToyScenarioNode() =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(TestPaths.ToyScenarioFile))!;
}
