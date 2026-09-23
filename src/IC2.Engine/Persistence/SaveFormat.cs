namespace IC2.Engine.Persistence;

/// <summary>
/// The save file envelope's own version — distinct from <see cref="Model.GameDataSchema.CurrentVersion"/>,
/// which governs the <em>document</em> contract (<c>World</c>/<c>Ruleset</c>/<c>Scenario</c>/
/// <c>SaveGame</c>/<c>GameState</c>'s own fields, exact-match only, no migration). A save file wraps a
/// <c>SaveGame</c> document in an outer envelope this task owns, so the envelope can evolve — gain a
/// field, reshape itself — independently of, and with an actual migration path that,
/// <see cref="Serialization.GameDataLoader"/>'s exact-version-match contract deliberately does not offer.
/// </summary>
/// <remarks>
/// This is a format identifier, not a gameplay constant (<c>CLAUDE.md</c> rule 11's carve-out, mirrored
/// by <c>GameDataSchema.CurrentVersion</c>'s own).
/// </remarks>
public static class SaveFormat
{
    /// <summary>The envelope field name a save file's format version is written under.</summary>
    public const string VersionField = "saveFormatVersion";

    /// <summary>The envelope field name the wrapped <c>SaveGame</c> document is written under.</summary>
    public const string PayloadField = "save";

    /// <summary>The highest save format version this build writes and reads.</summary>
    public const int CurrentVersion = 1;
}
