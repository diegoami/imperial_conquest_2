using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// T87 (<c>#389</c>): rebirth, <c>FUN_0044C360</c>
/// <see href="https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-quarterly-rebellion.md">
/// decompiled-quarterly-rebellion.md</see> §4 (research <c>235af11</c>) <c>[confirmed: listing
/// 0x0044C360-0x0044C525]</c>. Called by <see cref="Rebellion.Run"/>'s own branch (a), when a rebelling
/// city's allegiance names a dead nation (unity ≤ 0) — see that type's own remarks for why this decision
/// is final either way, with no fallback to (c) or (d).
/// </summary>
/// <remarks>
/// <para>
/// <strong>The test and the count.</strong> Every city in <see cref="GameState.Cities"/>, whoever
/// currently owns it, with <see cref="CityState.Allegiance"/> equal to <paramref name="deadNation"/> and
/// <see cref="CityState.Loyalty"/> under <see cref="EconomyRules.RebirthCandidateLoyaltyThreshold"/>, is a
/// candidate — including the calling city itself, other nations' own capitals, and cities this quarter's
/// own loyalty pass has not reached yet (both <c>[confirmed]</c>, the report's own "the count and the
/// transfer cover every city, whoever owns it... no capital test and no ownership test"). Rebirth proceeds
/// only when there are strictly more than <see cref="EconomyRules.RebirthMinimumQualifyingCityCount"/> of
/// them.
/// </para>
/// <para>
/// <strong>Order.</strong> The nation's own reset fields are written first (nothing below reads them),
/// then its own relation row is zeroed in full — <em>before</em> any city moves, because each move's own
/// −8 cooldown (below) writes into that same row and must survive it. Each qualifying city, in
/// <see cref="GameState.Cities"/>' own list order, then gets its own −8 cooldown with its <em>current</em>
/// owner and is defected to the reborn nation through <see cref="CityCaptureResolver.Defect"/> — the same
/// reused path <see cref="Rebellion.Run"/>'s own branches already call, never a second implementation of a
/// defection. Every such city has its own allegiance already equal to the reborn nation (that is what
/// qualified it), so <see cref="CityCaptureResolver.Defect"/> takes its own allegiant-transfer branch for
/// each one, exactly as the report's own "every moved city... is allegiant" expects. Finally, the new
/// capital is chosen from among the cities the reborn nation now owns.
/// </para>
/// <para>
/// <strong>The new leader stays <c>[open]</c> (the user's decision of 2026-09-27), but its draw is now
/// consumed (review round 1, N1, the user's decision of the same date).</strong> The report: "one
/// <c>Random(12)</c> from the nation's 12 names, with no 'differs from the current name' retry" —
/// unconditional, unlike the fall's own retry-until-differs draw (see <see cref="Deposition"/>'s own
/// remarks on why that one stays unmade). The 16 × 12 name pool is in the DAT at <c>0x2089A</c>, but the
/// world data this engine loads carries one <see cref="NationState.LeaderName"/> per nation — the gap
/// T39's own Done-when 7 already recorded (see <see cref="Deposition"/>'s own remarks, which T87
/// re-checked and found still open). Exporting the pool is a later export task's own work;
/// <see cref="NationState.LeaderName"/> is left unchanged here, and no name is invented. But the original
/// still draws once, unconditionally, before doing anything else — every later draw in the same quarter
/// (a later city's own loyalty draws, another rebellion or rebirth) sits at a stream position downstream
/// of this one in the original, so silently skipping it here would desynchronise this engine's own
/// sequence from the original's the very first time a rebirth happens. <see cref="Run"/> therefore makes
/// the one <see cref="IRng.NextInt(int)"/> call the original makes, against <see cref="LeaderNamePoolSize"/>,
/// and discards the result outright — the draw is real, only the name it would have picked is not, which
/// is the "consumed but not decided" shape Done-when 4 asks for, not the invented stream consumption this
/// build's own random-draw rule warns against (nothing here <em>reads</em> the drawn value, so nothing is
/// invented from it). <see cref="LeaderNamePoolSize"/> (12) is the DAT's own fixed name-pool width, not a
/// gameplay balance number a ruleset could tune — no scenario this engine loads carries a pool to draw
/// from in the first place — so it stays a local constant here, the same treatment <c>RngStreams</c>' own
/// algorithm constants get, never a <see cref="EconomyRules"/> field.
/// </para>
/// <para>
/// <strong>Relations are not fully reproduced here — review round 1, N2: unlike deposition and
/// elimination, this is a divergence T87 itself introduces, not an inherited one.</strong> The report:
/// "Only the reborn nation's own row (<c>+0x26</c>) is zeroed. Other nations' entries toward it are left
/// as they are" — an asymmetric write in the original (a raw row memset), and, in the original, a
/// <em>different</em> operation from <c>FUN_00449B40</c>, the setter deposition and elimination both call,
/// which <em>is</em> symmetric there. <see cref="DiplomaticRelations"/> is "symmetric by construction"
/// <c>[confirmed]</c> already, before this task (<c>GameState.cs</c>'s own remarks) — outside this task's
/// Owns list to change — so <see cref="DiplomaticRelations.WithRelation"/> necessarily also clears every
/// other nation's own entry toward the reborn one. For deposition and elimination that costs nothing: the
/// original's own <c>FUN_00449B40</c> is already symmetric, so this engine's uniform symmetric semantics
/// reproduce it exactly (<see cref="Deposition.ResetRelations"/>'s own remarks). Rebirth is the one caller
/// whose original operation is <em>not</em> symmetric, so the same engine-wide choice that costs nothing
/// elsewhere does diverge here — an earlier revision of this remark wrongly folded rebirth into "the same
/// simplification every other symmetric-relation reset already accepts", as if every caller's own original
/// operation were equally symmetric; only <c>FUN_00449B40</c>'s callers are. This is a real, new gap, named
/// rather than smoothed over, and outside this task's Owns list to close (the same <c>DiplomaticRelations</c>
/// constraint above).
/// </para>
/// <para>
/// <strong>This method never creates an army or a fleet — but the reborn nation is not guaranteed to
/// start with none (review round 1, N4).</strong> Neither <see cref="ArmyState"/> nor
/// <see cref="FleetState"/> is ever constructed here; T84's own deletions (<c>EliminationForces</c>, run
/// when the nation first died) stay exactly as they were, matching the report's own "no armies and no
/// fleets, because those were deleted when it died" for the moment of death itself. Rebirth happens later,
/// though, and each qualifying city's own <see cref="CityCaptureResolver.Defect"/> call below can itself
/// trigger <see cref="NationElimination.ApplyIfLastCityLost"/> for that city's own
/// <em>previous</em>, still-live owner (the identical mid-loop divergence <c>QuarterlyCityEconomySystem</c>'s
/// own remarks describe for B10) — and <c>EliminationForces.Dispose</c>'s own rule hands that owner's
/// fleets still under construction to <c>receivingNationId</c>, which is the reborn nation itself when its
/// own defection was the last city that owner had. So a rebirth can, at that one edge, leave the reborn
/// nation owning an under-construction fleet it never asked for and this method never created — an
/// earlier revision of this remark's "starts with none" claim did not account for that path. Only fleets:
/// <em>launched</em> fleets and every army are still deleted outright by that same
/// <c>EliminationForces.Dispose</c> call, never handed to the receiver, so the claim holds for those two.
/// </para>
/// <para>
/// <strong>No decision-making draw.</strong> The report's own "Random draws: none in <c>FUN_0044C204</c>
/// itself" covers the rebellion decision, and this method's own test (the qualifying-city count against a
/// fixed threshold) and every reset field are equally deterministic — the sole draw rebirth itself makes
/// in the original is the leader name, which this method now also draws (see above) but never reads: it
/// decides nothing here and picks no name.
/// </para>
/// </remarks>
public static class Rebirth
{
    /// <summary>
    /// The DAT's own fixed name-pool width (report §4, "the nation's 12 names") — a structural fact of the
    /// export format this engine's own scenarios never carry a pool for, not a gameplay balance number a
    /// ruleset could tune. See this type's own remarks on the leader-name draw.
    /// </summary>
    /// <remarks>
    /// Review round 2, N-a (the user's own decision on #389): stays a cited C# constant here, not an
    /// <see cref="EconomyRules"/> field — this task's own ruleset grant is limited to the six rebirth
    /// keys the reset writes actually use, and a name-pool <em>width</em> with no pool behind it in any
    /// scenario this engine loads is not something a ruleset author could meaningfully tune anyway. It
    /// becomes derivable from the exported pool itself, not ruleset data, once a later export task adds
    /// that pool (see this type's own remarks on why exporting it is outside this task's Owns list).
    /// </remarks>
    private const int LeaderNamePoolSize = 12;

    /// <summary>
    /// Runs rebirth for <paramref name="deadNation"/> if it now qualifies, reading every constant from
    /// <paramref name="ruleset"/>'s <see cref="EconomyRules"/> block — never a C# literal.
    /// </summary>
    /// <param name="state">The current working state — every read is live, per this type's own remarks.</param>
    /// <param name="ruleset">Supplies every rebirth constant and the new capital's own stat-gain fields (T86's).</param>
    /// <param name="deadNation">The nation named by the rebelling city's own allegiance; must have <c>Unity &lt;= 0</c>.</param>
    /// <param name="events">Where <see cref="CityCaptureResolver.Defect"/>'s own per-city defection news is published.</param>
    /// <param name="rng">
    /// Review round 1, N1: the one <see cref="IRng.NextInt(int)"/> call this method makes and discards, for
    /// the leader-name draw the original always makes here — see this type's own remarks.
    /// </param>
    /// <returns><paramref name="state"/> itself, unchanged, if fewer than the required count of cities qualify.</returns>
    public static GameState Run(GameState state, Ruleset ruleset, NationState deadNation, IEventSink events, IRng rng)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(deadNation);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(rng);

        var economy = ruleset.Economy;

        var qualifyingIds = new List<string>();
        foreach (var city in state.Cities)
        {
            if (string.Equals(city.Allegiance, deadNation.Id, StringComparison.Ordinal)
                && city.Loyalty < economy.RebirthCandidateLoyaltyThreshold)
            {
                qualifyingIds.Add(city.Id);
            }
        }

        if (qualifyingIds.Count <= economy.RebirthMinimumQualifyingCityCount)
        {
            return state;
        }

        // Review round 1, N1: the one leader-name draw the original always makes once rebirth actually
        // proceeds (report §4, "one Random(12) from the nation's 12 names") -- made here, against
        // LeaderNamePoolSize, and discarded outright. See this type's own remarks on why the draw itself
        // is kept (to hold this engine's own random sequence in step with the original's) while the name
        // it would pick is not (NationState.LeaderName stays [open], per DoD 1).
        _ = rng.NextInt(LeaderNamePoolSize);

        // The reset fields (report §4): unity, conquered-by, treasury, tax base, tax rate, mobilization,
        // every recruitment slot's own troops, and Eliminated cleared. City count needs no field of its
        // own -- this engine's own city count is always derived live from GameState.Cities, never stored,
        // so it is already 0 the moment every one of the dead nation's cities is gone, with nothing left
        // to reset. LeaderName is deliberately left unchanged (see this type's own remarks). Wealth is
        // deliberately left as the quarterly loop already zeroed it (report: "does not reset wealth...
        // which the moves then refill").
        var reborn = deadNation with
        {
            Unity = economy.RebirthUnity,
            ConqueredBy = null,
            Treasury = 0,
            TaxBase = 0,
            TaxRatePercent = economy.RebirthTaxRatePercent,
            MobilizedPercent = economy.RebirthMobilizedPercent,
            RecruitmentSlots = ValueList.From(deadNation.RecruitmentSlots.Select(slot => slot with { Troops = 0 })),
            Eliminated = false,
        };

        state = state with { Nations = CityCaptureResolver.ReplaceNation(state.Nations, reborn) };

        // The relation row, zeroed in full before any city moves -- see this type's own remarks on order
        // and on why this is symmetric where the original is not.
        var relations = state.Relations;
        foreach (var otherNationId in relations.NationIds)
        {
            if (!string.Equals(otherNationId, reborn.Id, StringComparison.Ordinal))
            {
                relations = relations.WithRelation(reborn.Id, otherNationId, 0);
            }
        }

        state = state with { Relations = relations };

        foreach (var cityId in qualifyingIds)
        {
            var city = state.CityById(cityId);
            // Defensive, not reachable on a production path: nothing between the count above and this
            // loop removes a city or changes another city's own allegiance/loyalty (this type's own
            // remarks, "the transfer loop re-tests each city... nothing earlier in the loop changes
            // another city's loyalty or allegiance" — Rebellion.cs's own caller passes one city at a
            // time, and neither the reset above nor an earlier iteration of this same loop touches any
            // other city's Allegiance or Loyalty).
            if (city is null)
            {
                continue;
            }

            state = state with
            {
                Relations = state.Relations.WithRelation(reborn.Id, city.Owner, economy.RebirthDefectionRelationPenalty),
            };
            state = CityCaptureResolver.Defect(state, cityId, reborn.Id, ruleset, events);
        }

        // The new capital: the strongest city the reborn nation now owns, first by (city-list) index on
        // a tie -- the same "best = 0, strict >" convention ConquestTrigger's own capital-move search
        // uses, reading the ruleset's own fortify order the same way that search's own caller does
        // (CityOrderProgressSystem's own convention: looked up by id, never a C# literal for the order
        // itself, only its own string id -- not a gameplay constant).
        var fortifyOrder = ruleset.CityOrders.Orders.First(order => string.Equals(order.Id, "fortify", StringComparison.Ordinal));

        CityState? bestCity = null;
        var bestStrength = 0;
        foreach (var cityId in qualifyingIds)
        {
            var city = state.CityById(cityId);
            if (city is null || !string.Equals(city.Owner, reborn.Id, StringComparison.Ordinal))
            {
                continue;
            }

            var ownerDiffersFromAllegiance = !string.Equals(city.Owner, city.Allegiance, StringComparison.Ordinal);
            var strength = CompleteDefenderStrength.Compute(
                city, fortifyOrder, CapitalOwnership.IsAnyNationsCapital(state, city.Id), ownerDiffersFromAllegiance, reborn, ruleset);

            if (strength > bestStrength)
            {
                bestStrength = strength;
                bestCity = city;
            }
        }

        if (bestCity is null)
        {
            return state;
        }

        var captureRules = ruleset.Capture;
        var newCapital = bestCity with
        {
            Loyalty = Math.Min(captureRules.CapitalMoveNewCapitalStatCap, bestCity.Loyalty + captureRules.CapitalMoveNewCapitalLoyaltyGain),
            FortificationCode = Math.Min(captureRules.CapitalMoveNewCapitalStatCap, bestCity.FortificationCode + captureRules.CapitalMoveNewCapitalFortificationGain),
            PopulationThousands = bestCity.PopulationThousands + captureRules.CapitalMoveNewCapitalPopulationGain,
            MaxPopulationThousands = bestCity.MaxPopulationThousands + captureRules.CapitalMoveNewCapitalMaxPopulationGain,
            Tribute = bestCity.Tribute + captureRules.CapitalMoveNewCapitalTributeGain,
        };

        state = state with { Cities = CityCaptureResolver.ReplaceCity(state.Cities, newCapital) };

        var rebornWithCapital = state.NationById(reborn.Id)! with { CapitalCityId = newCapital.Id };
        state = state with { Nations = CityCaptureResolver.ReplaceNation(state.Nations, rebornWithCapital) };

        return state;
    }
}
