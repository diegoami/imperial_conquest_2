using System.Text.Json.Nodes;
using IC2.Engine.Serialization;

namespace IC2.Engine.Persistence;

/// <summary>
/// Migrates an older save envelope forward to <see cref="SaveFormat.CurrentVersion"/>, one version step
/// at a time.
/// </summary>
/// <remarks>
/// Each step takes the envelope at version <c>N</c> and returns it re-shaped as version <c>N + 1</c>,
/// touching only what that version actually changed — <see cref="MigrateV1ToV2"/> is the only step so
/// far, because <see cref="SaveFormat.CurrentVersion"/> has only ever been 1 or 2.
/// </remarks>
internal static class SaveMigrations
{
    /// <summary>
    /// Steps <paramref name="envelope"/>, declaring <paramref name="foundVersion"/>, forward to
    /// <see cref="SaveFormat.CurrentVersion"/>.
    /// </summary>
    /// <exception cref="UnsupportedSaveFormatException">
    /// <paramref name="foundVersion"/> is older than any version this build knows how to migrate from.
    /// </exception>
    public static JsonObject MigrateToCurrent(string documentPath, JsonObject envelope, int foundVersion)
    {
        var current = envelope;
        var version = foundVersion;

        while (version < SaveFormat.CurrentVersion)
        {
            current = version switch
            {
                1 => MigrateV1ToV2(documentPath, current),

                // Nothing this build can step forward from -- either a version below
                // SaveFormat.MinimumSupportedVersion (zero, negative, or otherwise never shipped), or
                // (defensively) a version this switch has not been taught a step for. Same exception
                // type DoD 3 uses for "too new", but its own message tells the two apart (found <
                // MinimumSupportedVersion is never "a newer build").
                _ => throw new UnsupportedSaveFormatException(documentPath, foundVersion, SaveFormat.CurrentVersion),
            };

            version++;
        }

        return current;
    }

    /// <summary>
    /// Version 1 to version 2: adds <see cref="SaveFormat.TurnIndexField"/> at the envelope's root,
    /// mirrored from the nested state's own calendar — the only shape change between the two versions
    /// (<see cref="SaveFormat.CurrentVersion"/>'s remarks).
    /// </summary>
    /// <exception cref="MalformedGameDataException">
    /// The envelope is not actually a well-formed version-1 save (missing <c>save</c>, <c>state</c> or
    /// <c>calendar</c>, or a non-integer <c>turnIndex</c>) — reported here rather than left to surface
    /// as a confusing failure two steps later, in <see cref="GameDataLoader"/> itself.
    /// </exception>
    private static JsonObject MigrateV1ToV2(string documentPath, JsonObject v1)
    {
        var save = EnvelopeJson.RequireObject(documentPath, v1, SaveFormat.PayloadField, "the version-1 envelope");
        var state = EnvelopeJson.RequireObject(documentPath, save, "state", "the version-1 save");
        var calendar = EnvelopeJson.RequireObject(documentPath, state, "calendar", "the version-1 save's state");
        var turnIndex = EnvelopeJson.RequireInt(documentPath, calendar, "turnIndex", "the version-1 save's calendar");

        var v2 = (JsonObject)v1.DeepClone();
        v2[SaveFormat.VersionField] = 2;
        v2[SaveFormat.TurnIndexField] = turnIndex;
        return v2;
    }
}
