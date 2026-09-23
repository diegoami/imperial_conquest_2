namespace IC2.Engine.Serialization;

/// <summary>
/// Base type for every failure to load a world, ruleset, scenario or save.
/// </summary>
/// <remarks>
/// The loader never falls back to a default value: each distinguishable failure mode has its own
/// exception type, so a caller — and a test — can tell a syntactically broken file from a file that is
/// simply missing a field, from a file written for a schema version this build does not read.
/// </remarks>
public abstract class GameDataException : Exception
{
    /// <param name="documentPath">The file (or logical document name) the failure came from.</param>
    /// <param name="message">A message naming the document and what was wrong with it.</param>
    /// <param name="innerException">The underlying failure, when there is one.</param>
    protected GameDataException(string documentPath, string message, Exception? innerException = null)
        : base(message, innerException) => DocumentPath = documentPath;

    /// <summary>The file (or logical document name) the failure came from.</summary>
    public string DocumentPath { get; }
}

/// <summary>
/// The document is not readable as the requested kind: it is not valid JSON, is not an object, or
/// carries a value the model cannot represent (an unknown enum name, a string where a number belongs).
/// </summary>
public sealed class MalformedGameDataException : GameDataException
{
    /// <param name="documentPath">The file the failure came from.</param>
    /// <param name="detail">What was wrong.</param>
    /// <param name="innerException">The underlying parse failure, when there is one.</param>
    public MalformedGameDataException(string documentPath, string detail, Exception? innerException = null)
        : base(documentPath, $"'{documentPath}' is malformed: {detail}", innerException)
    {
    }
}

/// <summary>
/// A field the schema requires is absent. Distinguished from a malformed document because the file
/// parsed cleanly — it is simply incomplete, and the engine defaults nothing.
/// </summary>
public sealed class MissingRequiredFieldException : GameDataException
{
    /// <param name="documentPath">The file the failure came from.</param>
    /// <param name="fieldPath">The dotted path of the missing field, e.g. <c>cities[1].loyalty</c>.</param>
    /// <param name="declaringType">The record type that declares the field.</param>
    public MissingRequiredFieldException(string documentPath, string fieldPath, string declaringType)
        : base(documentPath, $"'{documentPath}' is missing required field '{fieldPath}' of {declaringType}.")
    {
        FieldPath = fieldPath;
        DeclaringType = declaringType;
    }

    /// <summary>The dotted path of the missing field.</summary>
    public string FieldPath { get; }

    /// <summary>The record type that declares the field.</summary>
    public string DeclaringType { get; }
}

/// <summary>
/// The document carries a field the schema does not know. Reported rather than ignored, because a
/// silently-dropped field is how a typo in a hand-authored ruleset turns into a wrong game.
/// </summary>
public sealed class UnknownFieldException : GameDataException
{
    /// <param name="documentPath">The file the failure came from.</param>
    /// <param name="fieldPath">The dotted path of the unrecognised field.</param>
    /// <param name="declaringType">The record type that was being read at that path.</param>
    public UnknownFieldException(string documentPath, string fieldPath, string declaringType)
        : base(documentPath, $"'{documentPath}' carries unknown field '{fieldPath}', which {declaringType} does not declare.")
    {
        FieldPath = fieldPath;
        DeclaringType = declaringType;
    }

    /// <summary>The dotted path of the unrecognised field.</summary>
    public string FieldPath { get; }

    /// <summary>The record type that was being read at that path.</summary>
    public string DeclaringType { get; }
}

/// <summary>The document declares a schema version this build does not read.</summary>
public sealed class SchemaVersionMismatchException : GameDataException
{
    /// <param name="documentPath">The file the failure came from.</param>
    /// <param name="found">The version the document declares.</param>
    /// <param name="supported">The version this build reads.</param>
    public SchemaVersionMismatchException(string documentPath, int found, int supported)
        : base(documentPath, $"'{documentPath}' declares schema version {found}; this build reads version {supported}.")
    {
        Found = found;
        Supported = supported;
    }

    /// <summary>The version the document declares.</summary>
    public int Found { get; }

    /// <summary>The version this build reads.</summary>
    public int Supported { get; }
}

/// <summary>
/// A world's base64 terrain grid names a sidecar file (<c>dataFile</c>, T62) that does not exist next
/// to the world document. Distinguished from <see cref="MalformedGameDataException"/> because the world
/// file itself parsed and validated cleanly -- the failure is a second, missing file -- and named
/// explicitly rather than left to surface as a null terrain grid or an unhandled <see cref="IOException"/>.
/// </summary>
public sealed class MissingTerrainSidecarException : GameDataException
{
    /// <param name="worldPath">The world document that references the sidecar.</param>
    /// <param name="sidecarPath">The sidecar path that was resolved and not found.</param>
    /// <param name="innerException">The underlying file-system failure.</param>
    public MissingTerrainSidecarException(string worldPath, string sidecarPath, Exception? innerException = null)
        : base(
            worldPath,
            $"'{worldPath}' names terrain sidecar file '{sidecarPath}', which does not exist.",
            innerException)
    {
        WorldPath = worldPath;
        SidecarPath = sidecarPath;
    }

    /// <summary>The world document that references the sidecar.</summary>
    public string WorldPath { get; }

    /// <summary>The sidecar path that was resolved and not found.</summary>
    public string SidecarPath { get; }
}

/// <summary>A scenario, save or state refers to a world or ruleset id that is not present.</summary>
public sealed class UnresolvedReferenceException : GameDataException
{
    /// <param name="documentPath">The document holding the dangling reference.</param>
    /// <param name="kind">What kind of thing was referenced, e.g. <c>world</c>.</param>
    /// <param name="id">The id that could not be resolved.</param>
    public UnresolvedReferenceException(string documentPath, string kind, string id)
        : base(documentPath, $"'{documentPath}' references {kind} '{id}', which is not present in the data set.")
    {
        Kind = kind;
        Id = id;
    }

    /// <summary>What kind of thing was referenced.</summary>
    public string Kind { get; }

    /// <summary>The id that could not be resolved.</summary>
    public string Id { get; }
}
