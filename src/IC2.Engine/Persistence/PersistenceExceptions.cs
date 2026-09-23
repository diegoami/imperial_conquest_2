using IC2.Engine.Serialization;

namespace IC2.Engine.Persistence;

/// <summary>
/// A save file's <c>saveFormatVersion</c> is higher than this build knows how to read.
/// </summary>
/// <remarks>
/// Distinguished from <see cref="SchemaVersionMismatchException"/> (which governs a document's own
/// <c>schemaVersion</c> field — <c>IC2.Engine.Model</c>'s "World/Ruleset/Scenario/SaveGame/GameState"
/// contract, exact-match only): <c>saveFormatVersion</c> is the outer save-file envelope T20 owns, which
/// supports migrating an <em>older</em> version forward (<see cref="SaveMigrations"/>) but never a
/// <em>future</em> one — a save written by a newer build could carry a field or a re-shaped envelope this
/// build has never seen, and best-effort parsing it would silently drop or misread that, not reject it.
/// </remarks>
public sealed class UnsupportedSaveFormatException : GameDataException
{
    /// <param name="documentPath">The save file the failure came from.</param>
    /// <param name="found">The <c>saveFormatVersion</c> the file declares.</param>
    /// <param name="supported">The highest <c>saveFormatVersion</c> this build reads.</param>
    public UnsupportedSaveFormatException(string documentPath, int found, int supported)
        : base(
            documentPath,
            $"'{documentPath}' was written by a newer build: it declares save format version {found}, "
            + $"but this build reads up to version {supported}.")
    {
        Found = found;
        Supported = supported;
    }

    /// <summary>The <c>saveFormatVersion</c> the file declares.</summary>
    public int Found { get; }

    /// <summary>The highest <c>saveFormatVersion</c> this build reads.</summary>
    public int Supported { get; }
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
