namespace IC2.Engine.Import;

/// <summary>
/// Resolves which fleet carries which army from the two original-save tables' own cross-reference
/// words — <c>FleetRecord.CarriedArmyIndex</c> and <c>ArmyRecord.IsAboardFleet</c> — into the
/// consistent id links <see cref="OriginalSaveImporter"/> needs, dropping a link that would otherwise
/// dangle rather than carrying it through.
/// </summary>
/// <remarks>
/// Extracted from <see cref="OriginalSaveImporter"/> so the delete-sweep hazard
/// (<c>build-process.md</c> §4.2: "a delete that leaves something behind... does anything still
/// <em>reference</em> it") is provable against plain data, in a fast, deterministic unit test — no
/// original save file needed, unlike the rest of the importer.
/// </remarks>
public static class EmbarkationLinker
{
    /// <summary>One fleet-table record's own claim: its table index, and the army-table index it
    /// carries (<c>null</c> for "carries no army").</summary>
    public readonly record struct FleetClaim(int FleetIndex, int? CarriedArmyIndex);

    /// <summary>The resolved links: fleet index → the army index it carries, and its inverse.</summary>
    public sealed record Result(
        IReadOnlyDictionary<int, int> FleetCarriesArmyIndex,
        IReadOnlyDictionary<int, int> ArmyCarriedByFleetIndex);

    /// <summary>
    /// Resolves every surviving fleet's carried-army claim.
    /// </summary>
    /// <param name="fleets">Every surviving (non-tombstoned) fleet's own claim.</param>
    /// <param name="liveArmyIndices">
    /// Army-table indices of every army the import actually creates (<c>SaveArmyTable.Armies</c>'s own
    /// indices). A fleet's claimed index must be one of these or a member of
    /// <paramref name="tombstonedArmyIndices"/> — anything else is corrupt or out-of-range data no
    /// corpus save has shown, and is a hard failure rather than a silently-kept, unverified link
    /// (review N1).
    /// </param>
    /// <param name="tombstonedArmyIndices">
    /// Army-table indices the army parser skipped as owner-<c>0xFFFF</c> tombstones. A fleet naming one
    /// of these no longer really carries anyone — the referenced army was merged or eliminated and
    /// compacted out — so the link is silently dropped rather than carried through as a reference to an
    /// army this import never creates.
    /// </param>
    /// <param name="documentPath">Named in the exception message on a genuine inconsistency.</param>
    /// <exception cref="InvalidDataException">
    /// Two surviving fleets both claim to carry the same surviving army — the one shape this method
    /// cannot resolve by itself, because there is no principled way to prefer one claim over the other —
    /// or a fleet claims an army index that is neither live nor tombstoned.
    /// </exception>
    public static Result Resolve(
        IEnumerable<FleetClaim> fleets,
        IReadOnlySet<int> liveArmyIndices,
        IReadOnlySet<int> tombstonedArmyIndices,
        string documentPath)
    {
        var fleetCarriesArmyIndex = new Dictionary<int, int>();
        var armyCarriedByFleetIndex = new Dictionary<int, int>();

        foreach (var claim in fleets)
        {
            if (claim.CarriedArmyIndex is not { } carriedIndex)
            {
                continue;
            }

            if (tombstonedArmyIndices.Contains(carriedIndex))
            {
                continue;
            }

            if (!liveArmyIndices.Contains(carriedIndex))
            {
                throw new InvalidDataException(
                    $"'{documentPath}': fleet {claim.FleetIndex} claims to carry army {carriedIndex}, " +
                    "which is neither a surviving army nor a tombstoned record.");
            }

            fleetCarriesArmyIndex[claim.FleetIndex] = carriedIndex;
            if (!armyCarriedByFleetIndex.TryAdd(carriedIndex, claim.FleetIndex))
            {
                throw new InvalidDataException(
                    $"'{documentPath}': army {carriedIndex} is claimed as carried by both fleet " +
                    $"{armyCarriedByFleetIndex[carriedIndex]} and fleet {claim.FleetIndex}.");
            }
        }

        return new Result(fleetCarriesArmyIndex, armyCarriedByFleetIndex);
    }

    /// <summary>
    /// Resolves one army's <c>AboardFleetId</c> from the link <see cref="Resolve"/> built and the
    /// army's own <c>IsAboardFleet</c> flag (the covered-cell sentinel), checking the two sides agree.
    /// </summary>
    /// <param name="armyIndex">The army's own table index.</param>
    /// <param name="isAboardFleet">The army record's own claim (covered-cell sentinel).</param>
    /// <param name="armyCarriedByFleetIndex">
    /// <see cref="Result.ArmyCarriedByFleetIndex"/> from a prior <see cref="Resolve"/> call.
    /// </param>
    /// <param name="fleetId">Renders a fleet-table index as the fleet's own id.</param>
    /// <param name="documentPath">Named in the exception message on a genuine inconsistency.</param>
    /// <param name="armiesClaimedByTombstonedFleets">
    /// Follow-up <see href="https://github.com/diegoami/imperial_conquest_2/issues/340">#340</see> N1:
    /// army-table indices that a <em>tombstoned</em> fleet (<c>SaveFleetTable.SkippedRecords</c>, a
    /// fleet absorbed into another during the turn, bug #276) still names as its
    /// <c>SkippedFleetRecord.CarriedArmyIndex</c>. Such an army can survive its carrier: the fleet that
    /// claimed it was compacted out, but the army's own covered-cell sentinel still says "aboard". When
    /// <paramref name="armyIndex"/> is in this set, that is the resolved explanation rather than a
    /// genuine inconsistency, so this returns <see langword="null"/> (unlinked) instead of throwing.
    /// Review round 1, N1: the army's own <c>CoveredCell</c> record still holds that sentinel — nothing
    /// in the original ever had a reason to rewrite it once the carrying fleet was gone — so the caller
    /// must not read it verbatim; it derives the real covered tile from the world's terrain at the
    /// army's own (X, Y) instead, which is how it keeps its position. Defaults to empty, so a caller
    /// that does not yet pass tombstoned fleets' own claims keeps today's behaviour (a hard failure)
    /// rather than silently changing under it.
    /// </param>
    /// <returns>The carrying fleet's id, or <see langword="null"/> when the army is not embarked.</returns>
    /// <exception cref="InvalidDataException">
    /// The army claims to be embarked but no surviving fleet's own claim names it and no tombstoned
    /// fleet's own claim explains it either, or the army claims not to be embarked while some surviving
    /// fleet claims to carry it anyway.
    /// </exception>
    public static string? ResolveArmyAboardFleet(
        int armyIndex,
        bool isAboardFleet,
        IReadOnlyDictionary<int, int> armyCarriedByFleetIndex,
        Func<int, string> fleetId,
        string documentPath,
        IReadOnlySet<int>? armiesClaimedByTombstonedFleets = null)
    {
        if (isAboardFleet)
        {
            if (armyCarriedByFleetIndex.TryGetValue(armyIndex, out var fleetIndex))
            {
                return fleetId(fleetIndex);
            }

            if (armiesClaimedByTombstonedFleets?.Contains(armyIndex) == true)
            {
                // #340 N1: the only fleet that ever claimed this army was itself tombstoned. The army
                // survived; its carrier didn't. Unlink it rather than fail the import -- its X/Y are
                // untouched (assigned unconditionally elsewhere), and once this returns null the
                // caller derives CoveredTileCode from the world's terrain at that position rather than
                // reading the record's own CoveredCell, which still holds the stale AboardFleetSentinel
                // (review round 1, N1).
                return null;
            }

            throw new InvalidDataException(
                $"'{documentPath}': army {armyIndex} is marked aboard a fleet (covered-cell sentinel), " +
                "but no surviving fleet's carried-army index names it.");
        }

        if (armyCarriedByFleetIndex.TryGetValue(armyIndex, out var claimingFleetIndex))
        {
            throw new InvalidDataException(
                $"'{documentPath}': fleet {claimingFleetIndex} claims to carry army {armyIndex}, but that " +
                "army's covered-cell is not the aboard-fleet sentinel.");
        }

        return null;
    }
}
