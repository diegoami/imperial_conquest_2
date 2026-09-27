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
/// <strong>The new leader stays <c>[open]</c> (the user's decision of 2026-09-27).</strong> The report:
/// "one <c>Random(12)</c> from the nation's 12 names, with no 'differs from the current name' retry". The
/// 16 × 12 name pool is in the DAT at <c>0x2089A</c>, but the world data this engine loads carries one
/// <see cref="NationState.LeaderName"/> per nation — the gap T39's own Done-when 7 already recorded (see
/// <see cref="Deposition"/>'s own remarks, which T87 re-checked and found still open). Exporting the pool
/// is a later export task's own work; <see cref="NationState.LeaderName"/> is left unchanged here, and no
/// name is invented. Because nothing here reads or decides anything from that draw, no placeholder
/// <see cref="IRng"/> call is made for it either — an unused draw would be exactly the kind of invented
/// stream consumption this build's own random-draw rule warns against, not a way to preserve fidelity.
/// </para>
/// <para>
/// <strong>Relations are not fully reproduced, by this engine's own prior design.</strong> The report:
/// "Only the reborn nation's own row (<c>+0x26</c>) is zeroed. Other nations' entries toward it are left
/// as they are" — an asymmetric write in the original (a raw row memset, not the symmetric setter
/// <c>FUN_00449B40</c> deposition and elimination both use). <see cref="DiplomaticRelations"/> is
/// "symmetric by construction" <c>[confirmed]</c> already, before this task (<c>GameState.cs</c>'s own
/// remarks) — outside this task's Owns list to change — so <see cref="DiplomaticRelations.WithRelation"/>
/// necessarily also clears every other nation's own entry toward the reborn one, which the original does
/// not. This is the same simplification every other symmetric-relation reset in this engine already
/// accepts (<see cref="Deposition.ResetRelations"/>'s own remarks), not a new gap T87 introduces.
/// </para>
/// <para>
/// <strong>No armies or fleets are added.</strong> This method never creates an <see cref="ArmyState"/> or
/// <see cref="FleetState"/> record; T84's own deletions (<c>EliminationForces</c>, run when the nation
/// first died) stay exactly as they were. The reborn nation starts with none, matching the report's own
/// "no armies and no fleets, because those were deleted when it died".
/// </para>
/// <para>
/// <strong>No random draw.</strong> The report's own "Random draws: none in <c>FUN_0044C204</c> itself"
/// covers the rebellion decision, and this method's own test (the qualifying-city count against a fixed
/// threshold) and every reset field are equally deterministic — the sole draw rebirth itself makes in the
/// original is the leader name, not implemented here (see above).
/// </para>
/// </remarks>
public static class Rebirth
{
    /// <summary>
    /// Runs rebirth for <paramref name="deadNation"/> if it now qualifies, reading every constant from
    /// <paramref name="ruleset"/>'s <see cref="EconomyRules"/> block — never a C# literal.
    /// </summary>
    /// <param name="state">The current working state — every read is live, per this type's own remarks.</param>
    /// <param name="ruleset">Supplies every rebirth constant and the new capital's own stat-gain fields (T86's).</param>
    /// <param name="deadNation">The nation named by the rebelling city's own allegiance; must have <c>Unity &lt;= 0</c>.</param>
    /// <param name="events">Where <see cref="CityCaptureResolver.Defect"/>'s own per-city defection news is published.</param>
    /// <returns><paramref name="state"/> itself, unchanged, if fewer than the required count of cities qualify.</returns>
    public static GameState Run(GameState state, Ruleset ruleset, NationState deadNation, IEventSink events)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(deadNation);
        ArgumentNullException.ThrowIfNull(events);

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
