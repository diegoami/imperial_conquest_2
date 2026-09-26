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

    /// <summary>
    /// The envelope field name <see cref="Model.GameState.Calendar"/>'s turn index is mirrored under
    /// (version 2+) — a convenience so a save picker can list a save's turn progress without
    /// deserializing (and validating) its entire nested <c>GameState</c>.
    /// </summary>
    public const string TurnIndexField = "turnIndex";

    /// <summary>
    /// The highest save format version this build writes and reads. Version 1 wrapped a bare
    /// <c>SaveGame</c> with no other envelope field; version 2 adds <see cref="TurnIndexField"/>;
    /// version 3 (T86) makes the nested state's own <c>nations[].conqueredBy</c> and
    /// <c>state.neighbours</c> fields explicit on every migrated envelope, rather than relying on their
    /// being optional at the <c>GameState</c>/<c>NationState</c> level (both are — see each field's own
    /// remarks) to carry an older save through unmodified. <see cref="SaveMigrations"/> carries the real
    /// version-1-to-2 and version-2-to-3 steps; the version-1 step is exercised by
    /// <c>tests/fixtures/saves/toy-3city-turn-10.v1.json</c> — a save written by this task's own
    /// version-1 code, committed before this constant became 2.
    /// </summary>
    public const int CurrentVersion = 3;

    /// <summary>
    /// The lowest save format version this build has ever shipped and can migrate from
    /// (<see cref="SaveMigrations"/> has no step below it). A version below this — zero, negative, or
    /// simply never issued — gets a different rejection message than a version above
    /// <see cref="CurrentVersion"/>: <see cref="UnsupportedSaveFormatException"/>'s "written by a newer
    /// build" would be false for a file that predates every version this build knows about.
    /// </summary>
    public const int MinimumSupportedVersion = 1;
}
