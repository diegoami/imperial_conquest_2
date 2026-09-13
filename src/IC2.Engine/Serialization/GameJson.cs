using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IC2.Engine.Serialization;

/// <summary>
/// The single <see cref="JsonSerializerOptions"/> instance every world, ruleset, scenario and save is
/// read and written with.
/// </summary>
/// <remarks>
/// <para>
/// Shared deliberately: round-trip equality ("serialize → deserialize → serialize produces identical
/// JSON") only holds if one set of options is used everywhere. The settings that make it hold are
/// property order (System.Text.Json writes members in declaration order, which for a record is its
/// primary-constructor order), camelCase naming, string-named enums, and no reordering or dropping of
/// anything.
/// </para>
/// <para>
/// <see cref="JsonSerializerOptions.WriteIndented"/> is on because these files are hand-authored and
/// hand-reviewed; indentation is part of the canonical form, so a re-serialized file is diff-clean
/// against the one on disk.
/// </para>
/// </remarks>
public static class GameJson
{
    /// <summary>The canonical options for reading and writing game data.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>Serializes a document to its canonical JSON form.</summary>
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = false,
            WriteIndented = true,
            // The loader validates structure itself (see SchemaValidator) so that a missing or unknown
            // field is a typed, path-carrying error rather than a System.Text.Json message; leaving the
            // serializer permissive here keeps exactly one place responsible for those diagnostics.
            NumberHandling = JsonNumberHandling.Strict,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            // The model's records are immutable and never null-defaulted; keeping nulls in the output is
            // what makes an absent optional value (an unbuilt fleet's countdown, a regular unit's lack of
            // a mercenary label) survive a round-trip as itself rather than vanishing.
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
        return options;
    }
}
