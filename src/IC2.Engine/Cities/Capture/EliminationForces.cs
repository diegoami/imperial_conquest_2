using IC2.Engine.Model;

namespace IC2.Engine.Cities.Capture;

/// <summary>
/// T84 (bug <see href="https://github.com/diegoami/imperial_conquest_2/issues/366">#366</see>): what
/// <see cref="CityCaptureResolver"/> calls once <see cref="NationElimination.ApplyIfLastCityLost"/> reports
/// <c>JustEliminated</c> — the disposal the original always ran alongside elimination, so a dead nation's
/// armies and fleets do not linger on the map to become permanent, untouchable blockers now that a
/// declaration of war against it is refused (T69). Every rule below is
/// <c>RE-imperial-conquest-2/docs/reports/decompiled-elimination-cleanup.md</c> (research commit
/// <c>43a44a1</c>), §2, §4 and §6.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Armies first, then fleets</strong> — report §2 "Order inside elimination": <c>FUN_0044AB90</c>
/// (:49372) deletes an army and, if it was aboard a fleet, clears that fleet's carried-army link
/// <c>[confirmed: decompile]</c>. <c>FUN_0044AD38</c> (:49493) then deletes every <em>launched</em> fleet
/// the nation owns, together with any army still aboard it, whoever owns that army — confirmed that the
/// fleet is deleted with its carried army, but whether a third nation's army can be the one aboard is
/// <c>[hypothesis]</c> in the report, since cross-nation embarkation itself is unconfirmed (§2, "An army of
/// another nation aboard an eliminated fleet dies with it, if cross-nation embarkation is possible"; the
/// dev engine's own <c>TUnitMap_SelectUnit</c> parity is not established either way, so this reproduction
/// simply lets the model decide: if <see cref="ArmyState.AboardFleetId"/> ever names another nation's
/// fleet, this method treats it exactly like any other carried army). Running the army loop first means an
/// eliminated nation's own army aboard its own fleet has already cleared that link by the time the fleet
/// loop reaches it, so nothing is ever deleted twice — matching the report's own "its +22 is already -1 by
/// then" note.
/// </para>
/// <para>
/// <strong>Fleets under construction change owner; they are not deleted</strong> — report §2 and §6 item 3:
/// the fleet loop's own <c>+10 == -1</c> ("is it launched") test is what separates the two outcomes
/// <c>[confirmed]</c>. The receiving nation is the capturer in a conquest and the nation that took the last
/// city in a defection — the same <c>receivingNationId</c> both <see cref="CityCaptureResolver.Capture"/>
/// and <see cref="CityCaptureResolver.Defect"/> already compute as their own new owner, so this method asks
/// for it rather than re-deriving it. The countdown (<see cref="FleetState.ConstructionTicksRemaining"/>)
/// and the build city (<see cref="FleetState.BuildCityId"/>) are left exactly as they were.
/// </para>
/// <para>
/// <strong>Nothing is refunded.</strong> Report §2 ("nothing else is written" for the army delete) and §6
/// item 4: a deleted record's units, supplies and money are destroyed by rule, not credited to any
/// treasury or city <c>[confirmed]</c>. This method's return value never touches
/// <see cref="NationState.Treasury"/>, <see cref="CityState.SupplyTons"/> or any other nation's or city's
/// field for exactly that reason — the deleted records simply stop existing.
/// </para>
/// <para>
/// <strong>No mutable map to restore.</strong> The original's army delete restores the covered map cell,
/// and its fleet delete writes 0 to the tile instead (report §2). <see cref="GameState"/> keeps
/// <see cref="ArmyState.CoveredTileCode"/> and <see cref="FleetState.CoveredTileCode"/> on the record
/// itself and has no separate mutable grid anywhere in the model (confirmed by grep: no map/grid-shaped
/// field on <see cref="GameState"/> or elsewhere in <c>src/IC2.Engine/Model</c>), so deleting the record is
/// the whole of "restoring" the cell here — there is nothing else to write.
/// </para>
/// <para>
/// <strong>The one cross-reference, keyed the safe way.</strong> <see cref="ArmyState.AboardFleetId"/> and
/// <see cref="FleetState.CarriedArmyId"/> are the only fields anywhere in <see cref="GameState"/> that hold
/// another army's or fleet's id as persisted state (confirmed by grep of <c>src</c> for
/// <c>ArmyId</c>/<c>FleetId</c>-shaped fields: every other hit is a command parameter or a method-local
/// value, never a collection <see cref="GameState"/> carries — there is no order queue, AI memory or
/// mercenary record that caches either kind of id). A cleared carrier link is found by matching
/// <em>the fleet's own <see cref="FleetState.CarriedArmyId"/></em> against the set of ids just deleted, not
/// by trusting the deleted army's own <see cref="ArmyState.AboardFleetId"/> — the same choice, for the same
/// reason, as <see cref="Economy.QuarterlyEconomySystem"/>'s own desertion cleanup already makes (see that
/// class's remarks). For <em>that one step</em> — clearing a carrier's link once its army is known deleted —
/// the two fields agree in any well-formed state, and keying on the fleet's own claim stays correct even if
/// they ever disagreed. The fleet loop below does not carry the same guarantee: it finds every army that
/// dies with a deleted launched fleet by reading <em>only</em> that fleet's own <see cref="FleetState.CarriedArmyId"/>,
/// so a state where an army's <see cref="ArmyState.AboardFleetId"/> names a fleet that does not name it back
/// is already malformed before <see cref="Dispose"/> ever runs — <see cref="IC2.Engine.Serialization.GameDataValidation"/>
/// is what rules that state out, not this method.
/// </para>
/// </remarks>
public static class EliminationForces
{
    /// <summary>
    /// Deletes every army and every launched fleet <paramref name="eliminatedNationId"/> owns, hands its
    /// fleets still under construction to <paramref name="receivingNationId"/> unchanged but for the new
    /// owner, and leaves no dangling <see cref="ArmyState.AboardFleetId"/> or
    /// <see cref="FleetState.CarriedArmyId"/> behind. Called once, only when
    /// <see cref="NationElimination.ApplyIfLastCityLost"/> just returned <c>JustEliminated: true</c> for
    /// <paramref name="eliminatedNationId"/> — see <see cref="CityCaptureResolver.Capture"/> and
    /// <see cref="CityCaptureResolver.Defect"/>'s own single call each.
    /// </summary>
    /// <param name="state">The state after the ownership change and the elimination flag are both already applied.</param>
    /// <param name="eliminatedNationId">The nation whose forces are disposed of.</param>
    /// <param name="receivingNationId">Who gets its fleets still under construction.</param>
    public static GameState Dispose(GameState state, string eliminatedNationId, string receivingNationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrWhiteSpace(eliminatedNationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(receivingNationId);

        // Armies first (report §2's own ordering): every army the eliminated nation owns is deleted,
        // wherever it stands -- on land, in a city, or aboard a fleet. Units, supplies and money are not
        // carried forward anywhere: the record is simply dropped.
        var survivingArmies = new List<ArmyState>(state.Armies.Count);
        var deletedArmyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var army in state.Armies)
        {
            if (string.Equals(army.Nation, eliminatedNationId, StringComparison.Ordinal))
            {
                deletedArmyIds.Add(army.Id);
            }
            else
            {
                survivingArmies.Add(army);
            }
        }

        // The carrier forgets it (FUN_0044AB90's own step): any fleet -- whoever owns it -- whose
        // CarriedArmyId names one of the armies just deleted has that link cleared, keyed on the fleet's
        // own claim rather than the dead army's AboardFleetId (see this class's remarks).
        var fleetsWithLinksCleared = deletedArmyIds.Count == 0
            ? state.Fleets
            : ValueList.From(state.Fleets.Select(fleet =>
                fleet.CarriedArmyId is { } carriedId && deletedArmyIds.Contains(carriedId)
                    ? fleet with { CarriedArmyId = null }
                    : fleet));

        // Then fleets: every launched fleet the eliminated nation owns is deleted, together with any army
        // still aboard it, whoever owns that army (report §2's FUN_0044AD38). An eliminated nation's own
        // army aboard its own fleet was already unlinked above by the army loop, so only another nation's
        // army -- if cross-nation embarkation is even possible -- can still be found carried here. A fleet
        // still under construction changes owner instead of being deleted (report §6 item 3).
        var survivingFleets = new List<FleetState>(fleetsWithLinksCleared.Count);
        var armiesLostWithTheirFleet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var fleet in fleetsWithLinksCleared)
        {
            if (!string.Equals(fleet.Nation, eliminatedNationId, StringComparison.Ordinal))
            {
                survivingFleets.Add(fleet);
                continue;
            }

            if (fleet.IsUnderConstruction)
            {
                survivingFleets.Add(fleet with { Nation = receivingNationId });
                continue;
            }

            // Launched and owned by the eliminated nation: deleted outright (not re-added), and whatever
            // army it still carries dies with it.
            if (fleet.CarriedArmyId is { } stillCarried)
            {
                armiesLostWithTheirFleet.Add(stillCarried);
            }
        }

        var finalArmies = armiesLostWithTheirFleet.Count == 0
            ? (IEnumerable<ArmyState>)survivingArmies
            : survivingArmies.Where(a => !armiesLostWithTheirFleet.Contains(a.Id));

        return state with
        {
            Armies = ValueList.From(finalArmies),
            Fleets = ValueList.From(survivingFleets),
        };
    }
}
