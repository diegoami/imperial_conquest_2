using IC2.Engine.Serialization;

namespace IC2.Engine.Persistence;

/// <summary>
/// A save file's <c>saveFormatVersion</c> is outside the range this build can read: newer than
/// <see cref="SaveFormat.CurrentVersion"/>, or older than <see cref="SaveFormat.MinimumSupportedVersion"/>
/// (zero, negative, or otherwise a version this build never shipped and has no migration step for).
/// </summary>
/// <remarks>
/// Distinguished from <see cref="SchemaVersionMismatchException"/> (which governs a document's own
/// <c>schemaVersion</c> field — <c>IC2.Engine.Model</c>'s "World/Ruleset/Scenario/SaveGame/GameState"
/// contract, exact-match only): <c>saveFormatVersion</c> is the outer save-file envelope T20 owns, which
/// supports migrating an <em>older</em>, previously-shipped version forward (<see cref="SaveMigrations"/>)
/// but never a <em>future</em> one — a save written by a newer build could carry a field or a re-shaped
/// envelope this build has never seen, and best-effort parsing it would silently drop or misread that, not
/// reject it. The two directions get different messages (below), because "too new" and "never existed"
/// are different mistakes for whoever reads the error.
/// </remarks>
public sealed class UnsupportedSaveFormatException : GameDataException
{
    /// <param name="documentPath">The save file the failure came from.</param>
    /// <param name="found">The <c>saveFormatVersion</c> the file declares.</param>
    /// <param name="supported">The highest <c>saveFormatVersion</c> this build reads.</param>
    public UnsupportedSaveFormatException(string documentPath, int found, int supported)
        : base(documentPath, BuildMessage(documentPath, found, supported))
    {
        Found = found;
        Supported = supported;
    }

    /// <summary>The <c>saveFormatVersion</c> the file declares.</summary>
    public int Found { get; }

    /// <summary>The highest <c>saveFormatVersion</c> this build reads.</summary>
    public int Supported { get; }

    private static string BuildMessage(string documentPath, int found, int supported) =>
        found < SaveFormat.MinimumSupportedVersion
            ? $"'{documentPath}' declares save format version {found}, which this build has never "
              + $"shipped and has no migration step for (the earliest is version {SaveFormat.MinimumSupportedVersion})."
            : $"'{documentPath}' was written by a newer build: it declares save format version {found}, "
              + $"but this build reads up to version {supported}.";
}

/// <summary>
/// A save's recorded <c>World</c> or <c>Ruleset</c> id does not match the one the game is currently
/// running under.
/// </summary>
/// <remarks>
/// Applies <c>game-design.md</c> §"Original-save compatibility" ("an imported original <c>.sav</c>
/// always maps onto the shipped <c>classical-mediterranean</c> World and the <c>classical-faithful</c>
/// Ruleset... attempting to load an original save into a game already using a different Ruleset or World
/// is rejected with a clear message, not silently reinterpreted") to native saves: a save is balanced
/// against, and only meaningful under, the exact World and Ruleset it started from.
/// </remarks>
public sealed class SaveContextMismatchException : GameDataException
{
    /// <param name="documentPath">The save file the failure came from.</param>
    /// <param name="kind">Which reference disagreed — <c>"world"</c> or <c>"ruleset"</c>.</param>
    /// <param name="expectedId">The id of the World/Ruleset the game is currently running under.</param>
    /// <param name="foundId">The id the save itself records.</param>
    public SaveContextMismatchException(string documentPath, string kind, string expectedId, string foundId)
        : base(
            documentPath,
            $"'{documentPath}' was saved under {kind} '{foundId}', but the game is currently running "
            + $"under {kind} '{expectedId}'. Load it under the {kind} it started from instead.")
    {
        Kind = kind;
        ExpectedId = expectedId;
        FoundId = foundId;
    }

    /// <summary>Which reference disagreed — <c>"world"</c> or <c>"ruleset"</c>.</summary>
    public string Kind { get; }

    /// <summary>The id of the World/Ruleset the game is currently running under.</summary>
    public string ExpectedId { get; }

    /// <summary>The id the save itself records.</summary>
    public string FoundId { get; }
}

/// <summary>
/// A save file could not be written to disk.
/// </summary>
/// <remarks>
/// Review round 2 (R2a): <see cref="SaveManager.WriteFile"/> used to report this as
/// <see cref="MalformedGameDataException"/>, whose message reads "is malformed: the file could not be
/// written" — wrong on its face, since nothing was read or parsed to be malformed; the write simply
/// didn't happen. A distinct type both fixes the wording and lets a caller distinguish a bad write from
/// a bad read without parsing the message. What "didn't happen" leaves at <c>documentPath</c> itself
/// depends on which step failed — see <see cref="SaveManager.WriteFile"/>'s own remarks for exactly what
/// is, and is not, covered by a test.
/// </remarks>
public sealed class SaveWriteException : GameDataException
{
    /// <param name="documentPath">The path the write was attempted at.</param>
    /// <param name="detail">What went wrong.</param>
    /// <param name="innerException">The underlying IO failure, when there is one.</param>
    public SaveWriteException(string documentPath, string detail, Exception? innerException = null)
        : base(documentPath, $"'{documentPath}' could not be written: {detail}", innerException)
    {
    }
}
