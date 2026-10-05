using System.Text.Json;
using System.Text.Json.Nodes;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using IC2.Engine.Serialization;

namespace IC2.Engine.Persistence;

/// <summary>
/// Migrates an older save envelope forward to <see cref="SaveFormat.CurrentVersion"/>, one version step
/// at a time.
/// </summary>
/// <remarks>
/// Each step takes the envelope at version <c>N</c> and returns it re-shaped as version <c>N + 1</c>,
/// touching only what that version actually changed — <see cref="MigrateV1ToV2"/>,
/// <see cref="MigrateV2ToV3"/> and <see cref="MigrateV3ToV4"/> are the three steps so far, because
/// <see cref="SaveFormat.CurrentVersion"/> has only ever been 1, 2, 3 or 4.
/// </remarks>
internal static class SaveMigrations
{
    /// <summary>
    /// Steps <paramref name="envelope"/>, declaring <paramref name="foundVersion"/>, forward to
    /// <see cref="SaveFormat.CurrentVersion"/>.
    /// </summary>
    /// <param name="expectedWorld">
    /// The <see cref="World"/> the caller is running under, when one is available — <see cref="MigrateV2ToV3"/>'s
    /// own use (T86, Owns amendment PR #408): a version-2 save's own missing <c>neighbours</c> field is
    /// populated from this world's neighbours (<see cref="NeighbourGeography.InitialAdjacency"/> --
    /// the world's own <c>startingNeighbours</c> field, or the same geometric fallback
    /// <see cref="GameStateFactory.CreateInitial"/> itself uses) rather than left <see langword="null"/>.
    /// <see langword="null"/> here (the default) means no such data is available -- <see cref="SaveManager.PeekSummary"/>'s
    /// own call, which never touches the nested state at all, so leaving the field absent has no
    /// observable effect there.
    /// </param>
    /// <exception cref="UnsupportedSaveFormatException">
    /// <paramref name="foundVersion"/> is older than any version this build knows how to migrate from.
    /// </exception>
    public static JsonObject MigrateToCurrent(
        string documentPath, JsonObject envelope, int foundVersion, World? expectedWorld = null)
    {
        var current = envelope;
        var version = foundVersion;

        while (version < SaveFormat.CurrentVersion)
        {
            current = version switch
            {
                1 => MigrateV1ToV2(documentPath, current),
                2 => MigrateV2ToV3(documentPath, current, expectedWorld),
                3 => MigrateV3ToV4(documentPath, current),

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

    /// <summary>
    /// Version 2 to version 3 (T86): makes the nested state's own <c>neighbours</c> field and every
    /// nation's own <c>conqueredBy</c> field explicit. <c>conqueredBy</c> always writes JSON
    /// <see langword="null"/> where it is missing — a version-2 save never had a conquest to record.
    /// <c>neighbours</c> is populated from <paramref name="expectedWorld"/>'s own neighbours
    /// (<see cref="NeighbourGeography.InitialAdjacency"/>) when one is given — this is what "an older
    /// save migrates by taking its world's neighbours" means literally (<c>docs/tasks/T86.md</c>
    /// Done-when 3), not merely what a query answers as if it had, through
    /// <see cref="NeighbourGeography"/>'s own separate null fallback. When no world is given (only
    /// <see cref="SaveManager.PeekSummary"/>'s own call, which never touches the nested state at all), the
    /// field is written explicit <see langword="null"/> instead, and that fallback is what a caller that
    /// somehow reaches a <see cref="GameState"/> without going through <see cref="SaveManager.Load"/>,
    /// <see cref="GameStateFactory.CreateInitial"/> or <see cref="Import.OriginalSaveImporter.Import"/>
    /// still relies on — chiefly hand-built test fixtures, which construct a <see cref="GameState"/>
    /// directly rather than through any of those three.
    /// </summary>
    /// <exception cref="MissingRequiredFieldException">
    /// The envelope is not actually a well-formed version-2 save (missing <c>save</c>, <c>state</c> or
    /// <c>state.nations</c>).
    /// </exception>
    /// <exception cref="MalformedGameDataException"><c>state.nations</c> is not a JSON array.</exception>
    private static JsonObject MigrateV2ToV3(string documentPath, JsonObject v2, World? expectedWorld)
    {
        var v3 = (JsonObject)v2.DeepClone();
        v3[SaveFormat.VersionField] = 3;

        var save = EnvelopeJson.RequireObject(documentPath, v3, SaveFormat.PayloadField, "the version-2 envelope");
        var state = EnvelopeJson.RequireObject(documentPath, save, "state", "the version-2 save");

        if (!state.ContainsKey("neighbours"))
        {
            state["neighbours"] = expectedWorld is null
                ? null
                : JsonSerializer.SerializeToNode(NeighbourGeography.InitialAdjacency(expectedWorld), GameJson.Options);
        }

        if (!state.TryGetPropertyValue("nations", out var nationsNode) || nationsNode is null)
        {
            throw new MissingRequiredFieldException(documentPath, "nations", "the version-2 save's state");
        }

        if (nationsNode is not JsonArray nations)
        {
            throw new MalformedGameDataException(documentPath, "'state.nations' must be a JSON array.");
        }

        foreach (var nationNode in nations)
        {
            if (nationNode is JsonObject nation && !nation.ContainsKey("conqueredBy"))
            {
                nation["conqueredBy"] = null;
            }
        }

        return v3;
    }

    /// <summary>
    /// Version 3 to version 4 (T76): gives every occupied mercenary-pool slot the <c>x</c>/<c>y</c>
    /// city-tile position <see cref="MercenaryPoolSlot"/> now carries. A pre-T76 save never persisted it,
    /// and no source can recover where an offer stood: searched
    /// <c>decompiled-mercenary-offer-list-and-position.md</c> (its §4 confirms the DAT ships its 50 live
    /// slots <em>empty</em> as <c>(0, 0, …)</c> and that every live offer sits on a city tile) and this
    /// save format's own version-3 schema (which carries neither the tile nor any template the tile could
    /// be derived from), and found no recoverable position for a v3 save's occupied slot. The step
    /// therefore supplies <c>(0, 0)</c> as a <strong>[designed]</strong> fallback — the value the
    /// original's own never-filled slots hold, chosen because it is the one pair the format already uses
    /// for "no offer here". <strong>Consequence:</strong> in every shipped world the pair resolves to a
    /// tile with no city — none of <c>classical-mediterranean</c>, <c>example-tiny-duel</c> and
    /// <c>toy-3city</c> has a city at <c>(0, 0)</c> — so the slot is no reachable offer from a human order
    /// or the AI hire until the quarterly restock (T56) rewrites the pair, and an imported then saved
    /// pre-T76 game has no reachable offer until T56 runs. That is a property of the shipped worlds, not
    /// of the encoding: a scenario with a city at <c>(0, 0)</c> would make every migrated offer live on
    /// that city. An entry that already carries either coordinate is left untouched, so a save written by a
    /// later version that gained another field here re-runs this step harmlessly.
    /// </summary>
    /// <exception cref="MalformedGameDataException">
    /// The envelope is not a well-formed version-3 save (<c>state.mercenaryPool</c> is not a JSON array),
    /// or one of its entries is not a JSON object.
    /// </exception>
    private static JsonObject MigrateV3ToV4(string documentPath, JsonObject v3)
    {
        var v4 = (JsonObject)v3.DeepClone();
        v4[SaveFormat.VersionField] = 4;

        var save = EnvelopeJson.RequireObject(documentPath, v4, SaveFormat.PayloadField, "the version-3 envelope");
        var state = EnvelopeJson.RequireObject(documentPath, save, "state", "the version-3 save");

        if (!state.TryGetPropertyValue("mercenaryPool", out var poolNode) || poolNode is null)
        {
            return v4;
        }

        if (poolNode is not JsonArray pool)
        {
            throw new MalformedGameDataException(
                documentPath, "'state.mercenaryPool' must be a JSON array.");
        }

        foreach (var slotNode in pool)
        {
            if (slotNode is not JsonObject slot)
            {
                throw new MalformedGameDataException(
                    documentPath,
                    "every 'state.mercenaryPool' entry must be a JSON object describing MercenaryPoolSlot.");
            }

            if (!slot.ContainsKey("x"))
            {
                slot["x"] = 0;
            }

            if (!slot.ContainsKey("y"))
            {
                slot["y"] = 0;
            }
        }

        return v4;
    }
}
