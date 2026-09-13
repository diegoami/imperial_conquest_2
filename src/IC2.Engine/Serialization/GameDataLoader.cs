using System.Text.Json;
using System.Text.Json.Nodes;
using IC2.Engine.Model;

namespace IC2.Engine.Serialization;

/// <summary>
/// Reads one world, ruleset, scenario or save from JSON, failing with a distinct typed error for each
/// distinguishable kind of bad input rather than defaulting anything.
/// </summary>
public static class GameDataLoader
{
    private const string SchemaVersionField = "schemaVersion";

    /// <summary>Loads a document from a file path.</summary>
    /// <typeparam name="T">The document kind to read.</typeparam>
    /// <param name="path">The file to read.</param>
    /// <exception cref="GameDataException">The file is unreadable as <typeparamref name="T"/>.</exception>
    public static T LoadFile<T>(string path)
        where T : IVersionedDocument
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        // Every way File.ReadAllText can fail has to land inside the typed hierarchy, or
        // GameDataRepository.Load's documented "throws GameDataException" contract is not true:
        // a path that is a directory or is ACL-denied throws UnauthorizedAccessException, not IOException.
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new MalformedGameDataException(path, $"the file could not be read: {ex.Message}", ex);
        }

        return Load<T>(path, json);
    }

    /// <summary>Loads a document from JSON text.</summary>
    /// <typeparam name="T">The document kind to read.</typeparam>
    /// <param name="documentPath">The file name or logical document name, used in error messages.</param>
    /// <param name="json">The document text.</param>
    /// <exception cref="MalformedGameDataException">The text is not valid JSON for this kind.</exception>
    /// <exception cref="MissingRequiredFieldException">A field the schema requires is absent.</exception>
    /// <exception cref="UnknownFieldException">A field the schema does not declare is present.</exception>
    /// <exception cref="SchemaVersionMismatchException">The document declares another schema version.</exception>
    public static T Load<T>(string documentPath, string json)
        where T : IVersionedDocument
    {
        ArgumentNullException.ThrowIfNull(json);

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

        if (node is not JsonObject root)
        {
            throw new MalformedGameDataException(documentPath, "the document's root must be a JSON object.");
        }

        CheckSchemaVersion(documentPath, root);
        SchemaValidator.Validate<T>(documentPath, root);

        T document;
        try
        {
            document = root.Deserialize<T>(GameJson.Options)
                       ?? throw new MalformedGameDataException(documentPath, "the document deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new MalformedGameDataException(documentPath, $"a value could not be read: {ex.Message}", ex);
        }
        catch (NotSupportedException ex)
        {
            throw new MalformedGameDataException(documentPath, $"a value could not be read: {ex.Message}", ex);
        }

        GameDataValidation.Validate(documentPath, document);
        return document;
    }

    private static void CheckSchemaVersion(string documentPath, JsonObject root)
    {
        if (!root.TryGetPropertyValue(SchemaVersionField, out var versionNode) || versionNode is null)
        {
            throw new MissingRequiredFieldException(documentPath, SchemaVersionField, "the document root");
        }

        int version;
        try
        {
            version = versionNode.GetValue<int>();
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            throw new MalformedGameDataException(
                documentPath, $"'{SchemaVersionField}' must be an integer.", ex);
        }

        if (version != GameDataSchema.CurrentVersion)
        {
            throw new SchemaVersionMismatchException(documentPath, version, GameDataSchema.CurrentVersion);
        }
    }
}
