using System.Text.Json;
using System.Text.Json.Nodes;
using IC2.Engine.Model;
using IC2.Engine.Serialization;

namespace IC2.Engine.Persistence;

/// <summary>
/// Writes and reads native save files: a versioned envelope (<see cref="SaveFormat"/>) around a
/// <see cref="SaveGame"/> document, with the <c>World</c>/<c>Ruleset</c> a save started from checked
/// against the ones the caller is currently running under (<c>game-design.md</c>
/// §"Original-save compatibility", applied to native saves — <c>docs/tasks/T20.md</c> Done-when 4).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why an envelope around <see cref="SaveGame"/> rather than writing it directly.</strong>
/// <see cref="GameDataLoader.Load{T}"/> already reads a bare <c>SaveGame</c> perfectly well, but it
/// checks the document's <c>schemaVersion</c> for exact equality against
/// <see cref="GameDataSchema.CurrentVersion"/> and rejects anything else — by design, and correctly, for
/// <c>World</c>/<c>Ruleset</c>/<c>Scenario</c>, none of which this task may change
/// (<c>src/IC2.Engine/Model/**</c> and <c>src/IC2.Engine/Serialization/**</c> are outside this task's
/// Owns list). A save format that needs an actual migration path (Done-when 2) therefore cannot live in
/// that field: this class wraps the <c>SaveGame</c> — itself always written and read at
/// <see cref="GameDataSchema.CurrentVersion"/>, exactly as <c>GameDataLoader</c> expects — inside its own
/// envelope, carrying its own <see cref="SaveFormat.VersionField"/> that this class alone governs and
/// that <see cref="SaveMigrations"/> can step forward.
/// </para>
/// <para>
/// The inner <c>save</c> document is still handed to <see cref="GameDataLoader.Load{T}"/>, so a save
/// still gets every check that gives a world/ruleset/scenario file its typed errors: missing or unknown
/// fields (<see cref="SchemaValidator"/>), and every semantic check
/// <see cref="GameDataValidation.Validate"/> runs for a <see cref="SaveGame"/> (id uniqueness, a
/// well-formed relation matrix, every reference resolving, the save's own ids agreeing with the state it
/// carries).
/// </para>
/// </remarks>
/// <summary>
/// A save file's listing metadata, read by <see cref="SaveManager.PeekSummary"/> without touching its
/// nested <see cref="GameState"/> — what a save picker needs to show a list of saves.
/// </summary>
public sealed record SaveSummary(
    string Id, string Label, string ScenarioId, string WorldId, string RulesetId, int TurnIndex);

public static class SaveManager
{
    private static readonly JsonSerializerOptions WriteOptions = new(GameJson.Options) { WriteIndented = true };

    /// <summary>Writes <paramref name="save"/> to <paramref name="path"/> as a current-format save file.</summary>
    public static void WriteFile(string path, SaveGame save) => File.WriteAllText(path, Serialize(save));

    /// <summary>Serializes <paramref name="save"/> to its canonical, current-format envelope text.</summary>
    public static string Serialize(SaveGame save)
    {
        ArgumentNullException.ThrowIfNull(save);

        var envelope = new JsonObject
        {
            [SaveFormat.VersionField] = SaveFormat.CurrentVersion,
            [SaveFormat.TurnIndexField] = save.State.Calendar.TurnIndex,
            [SaveFormat.PayloadField] = JsonSerializer.SerializeToNode(save, GameJson.Options),
        };

        return envelope.ToJsonString(WriteOptions);
    }

    /// <summary>
    /// Loads a save file from <paramref name="path"/>, checking it against the World and Ruleset the
    /// caller is currently running under.
    /// </summary>
    /// <exception cref="GameDataException">
    /// The file is unreadable, malformed, declares a save format version newer than this build supports
    /// (<see cref="UnsupportedSaveFormatException"/>), or records a different World or Ruleset than
    /// <paramref name="expectedWorld"/>/<paramref name="expectedRuleset"/> (<see cref="SaveContextMismatchException"/>).
    /// </exception>
    public static SaveGame LoadFile(string path, World expectedWorld, Ruleset expectedRuleset)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new MalformedGameDataException(path, $"the file could not be read: {ex.Message}", ex);
        }

        return Load(path, json, expectedWorld, expectedRuleset);
    }

    /// <summary>
    /// Loads a save from envelope text, checking it against the World and Ruleset the caller is
    /// currently running under.
    /// </summary>
    /// <param name="documentPath">The file name or logical document name, used in error messages.</param>
    /// <param name="json">The envelope text.</param>
    /// <param name="expectedWorld">The World the game is currently running under.</param>
    /// <param name="expectedRuleset">The Ruleset the game is currently running under.</param>
    /// <exception cref="MalformedGameDataException">The text is not a valid save envelope.</exception>
    /// <exception cref="MissingRequiredFieldException">The envelope is missing a required field.</exception>
    /// <exception cref="UnsupportedSaveFormatException">
    /// The envelope declares a save format version newer than this build supports.
    /// </exception>
    /// <exception cref="SaveContextMismatchException">
    /// The save records a different World or Ruleset id than <paramref name="expectedWorld"/>/
    /// <paramref name="expectedRuleset"/>.
    /// </exception>
    public static SaveGame Load(string documentPath, string json, World expectedWorld, Ruleset expectedRuleset)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(expectedWorld);
        ArgumentNullException.ThrowIfNull(expectedRuleset);

        var envelope = ParseEnvelope(documentPath, json);
        var foundVersion = ReadVersion(documentPath, envelope);

        if (foundVersion > SaveFormat.CurrentVersion)
        {
            throw new UnsupportedSaveFormatException(documentPath, foundVersion, SaveFormat.CurrentVersion);
        }

        var current = foundVersion < SaveFormat.CurrentVersion
            ? SaveMigrations.MigrateToCurrent(documentPath, envelope, foundVersion)
            : envelope;

        if (!current.TryGetPropertyValue(SaveFormat.PayloadField, out var payloadNode) || payloadNode is null)
        {
            throw new MissingRequiredFieldException(documentPath, SaveFormat.PayloadField, "the save envelope");
        }

        var envelopeTurnIndex = ReadTurnIndex(documentPath, current);
        var save = GameDataLoader.Load<SaveGame>(documentPath, payloadNode.ToJsonString());

        // The envelope's own turnIndex (written fresh at save time, or supplied by MigrateV1ToV2 for an
        // older file) has to agree with the nested state it is describing -- otherwise a save picker
        // reading only the envelope, never the state, would show a wrong number. A version-1 fixture
        // whose migration step were deleted or made a no-op would fail the required-field check just
        // above instead of reaching this line at all, so the two checks together cover both "missing"
        // and "present but wrong".
        if (envelopeTurnIndex != save.State.Calendar.TurnIndex)
        {
            throw new MalformedGameDataException(
                documentPath,
                $"the envelope's '{SaveFormat.TurnIndexField}' ({envelopeTurnIndex}) disagrees with "
                + $"the save's own state.calendar.turnIndex ({save.State.Calendar.TurnIndex}).");
        }

        if (!string.Equals(save.WorldId, expectedWorld.Id, StringComparison.Ordinal))
        {
            throw new SaveContextMismatchException(documentPath, "world", expectedWorld.Id, save.WorldId);
        }

        if (!string.Equals(save.RulesetId, expectedRuleset.Id, StringComparison.Ordinal))
        {
            throw new SaveContextMismatchException(documentPath, "ruleset", expectedRuleset.Id, save.RulesetId);
        }

        return save;
    }

    private static JsonObject ParseEnvelope(string documentPath, string json)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json, nodeOptions: null, documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
            });
        }
        catch (JsonException ex)
        {
            throw new MalformedGameDataException(documentPath, "the file is not valid JSON.", ex);
        }

        if (node is not JsonObject envelope)
        {
            throw new MalformedGameDataException(documentPath, "the document's root must be a JSON object.");
        }

        return envelope;
    }

    private static int ReadVersion(string documentPath, JsonObject envelope) =>
        EnvelopeJson.RequireInt(documentPath, envelope, SaveFormat.VersionField, "the save envelope");

    private static int ReadTurnIndex(string documentPath, JsonObject envelope) =>
        EnvelopeJson.RequireInt(documentPath, envelope, SaveFormat.TurnIndexField, "the save envelope");

    /// <summary>
    /// Reads a save file's listing metadata — id, label, which World/Ruleset/Scenario it started from,
    /// and its turn progress — without deserializing or validating its <see cref="SaveGame.State"/>. An
    /// older envelope is migrated first, exactly as <see cref="Load"/> does, so a summary always reflects
    /// the current envelope shape; unlike <see cref="Load"/>, no World or Ruleset is required up front,
    /// since a save picker's whole point is to show saves the caller has not yet chosen to load.
    /// </summary>
    /// <exception cref="UnsupportedSaveFormatException">
    /// The envelope declares a save format version newer than this build supports.
    /// </exception>
    public static SaveSummary PeekSummaryFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new MalformedGameDataException(path, $"the file could not be read: {ex.Message}", ex);
        }

        return PeekSummary(path, json);
    }

    /// <inheritdoc cref="PeekSummaryFile"/>
    public static SaveSummary PeekSummary(string documentPath, string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var envelope = ParseEnvelope(documentPath, json);
        var foundVersion = ReadVersion(documentPath, envelope);

        if (foundVersion > SaveFormat.CurrentVersion)
        {
            throw new UnsupportedSaveFormatException(documentPath, foundVersion, SaveFormat.CurrentVersion);
        }

        var current = foundVersion < SaveFormat.CurrentVersion
            ? SaveMigrations.MigrateToCurrent(documentPath, envelope, foundVersion)
            : envelope;

        var turnIndex = ReadTurnIndex(documentPath, current);
        var save = EnvelopeJson.RequireObject(documentPath, current, SaveFormat.PayloadField, "the save envelope");

        return new SaveSummary(
            Id: EnvelopeJson.RequireString(documentPath, save, "id", "the save"),
            Label: EnvelopeJson.RequireString(documentPath, save, "label", "the save"),
            ScenarioId: EnvelopeJson.RequireString(documentPath, save, "scenarioId", "the save"),
            WorldId: EnvelopeJson.RequireString(documentPath, save, "worldId", "the save"),
            RulesetId: EnvelopeJson.RequireString(documentPath, save, "rulesetId", "the save"),
            TurnIndex: turnIndex);
    }
}
