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
    /// <exception cref="MissingTerrainSidecarException">
    /// <typeparamref name="T"/> is <see cref="World"/> and its terrain grid's <c>dataFile</c> sidecar
    /// does not exist (T62).
    /// </exception>
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

        // Resolve a world's terrain sidecar (T62) before anything -- including GameDataValidation's own
        // Terrain.Decode call just below -- sees the document, so every caller of GameDataLoader gets a
        // World whose Terrain.Data is already populated, indistinguishable from one that embedded it.
        if (document is World world)
        {
            document = (T)(object)ResolveWorldTerrainSidecar(documentPath, world);
        }

        GameDataValidation.Validate(documentPath, document);
        return document;
    }

    /// <summary>
    /// Reads a base64 terrain grid's <see cref="TerrainGrid.DataFile"/> sidecar, next to
    /// <paramref name="documentPath"/>, into <see cref="TerrainGrid.Data"/>. A grid that already carries
    /// <see cref="TerrainGrid.Data"/> inline, or that is not base64, passes through unchanged -- which is
    /// every call from a test that builds a <see cref="World"/> in memory and loads it through
    /// <see cref="Load{T}"/> with a synthetic <paramref name="documentPath"/>: none of those set
    /// <c>dataFile</c>, so this never touches the disk for them.
    /// </summary>
    /// <exception cref="MalformedGameDataException">The grid names both <c>data</c> and <c>dataFile</c>.</exception>
    /// <exception cref="MissingTerrainSidecarException">The named sidecar file does not exist.</exception>
    private static World ResolveWorldTerrainSidecar(string documentPath, World world)
    {
        var terrain = world.Terrain;
        if (terrain.Encoding != TerrainEncoding.Base64 || terrain.DataFile is null)
        {
            return world;
        }

        if (terrain.Data is not null)
        {
            throw new MalformedGameDataException(
                documentPath, "the terrain grid carries both \"data\" and \"dataFile\"; it must carry exactly one.");
        }

        var worldDirectory = Path.GetDirectoryName(Path.GetFullPath(documentPath))
                              ?? throw new MalformedGameDataException(
                                  documentPath, $"'{documentPath}' has no directory to resolve terrain sidecar '{terrain.DataFile}' against.");
        var sidecarPath = Path.Combine(worldDirectory, terrain.DataFile);

        string sidecarText;
        try
        {
            sidecarText = File.ReadAllText(sidecarPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new MissingTerrainSidecarException(documentPath, sidecarPath, ex);
        }

        // DataFile is cleared, not just Data populated: once resolved, this TerrainGrid must look
        // exactly like one that embedded Data all along (that mutual-exclusion check two lines up,
        // and TerrainGrid.Decode's own, both exist so the two are never both set at once).
        return world with { Terrain = terrain with { Data = sidecarText.Trim(), DataFile = null } };
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
