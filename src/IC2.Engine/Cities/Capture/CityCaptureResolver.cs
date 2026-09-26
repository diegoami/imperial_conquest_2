using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Strength;

namespace IC2.Engine.Cities.Capture;

/// <summary>
/// Everything downstream of a siege's own win/loss decision: <c>FUN_0044bb18</c> (the forced-capture
/// ownership transfer), <c>FUN_0044ba1c</c> (the cascading-defection sweep it triggers) and
/// <c>FUN_0044bed8</c> (the defection routine itself) — <c>docs/task-catalogue.md</c> T17.
/// </summary>
/// <remarks>
/// <para>
/// <strong>What T16 already decided, and what this task starts from.</strong>
/// <see cref="InstantBattleResolver.ResolveSiege"/> is <c>FUN_0044B27C</c>'s own strength comparison: it
/// decides whether the attacker's <see cref="SiegeStrength.Attacker"/> beats the defender's
/// <see cref="SiegeStrength.Defender"/> — scaled by the capital-and-loyalty and owner-vs-allegiance
/// branches, then by this task's own garrison-troops addend (a narrow grant on that file, DoD 7's
/// amendment: the addend is <c>FUN_0044A98C</c>'s own last line, so the initiating siege needs it too, not
/// only the cascade below), then by the siege entry point's own separate
/// <see cref="SiegeRules.AttackerIsAllegianceDefenderReductionPercent"/> ×9/10 reduction when the attacker
/// is the city's own allegiance — and it applies the attacking army's own per-attempt attrition
/// (<see cref="BattleCasualties"/>, DoD 5) — win or lose, every time. <see cref="ResolveOutcome"/> takes
/// that already-resolved, now-<em>complete</em> <see cref="BattleResult"/> and does not re-decide it: it
/// only ever reads <see cref="BattleResult.Winner"/>. This is deliberate and load-bearing — computing the
/// ×9/10 reduction a second time here would violate the task's own brief, which is exactly why
/// <see cref="CompleteDefenderStrength"/> (this task's own reusable "complete defender strength", used by
/// the cascade below) never applies it — see that type's own remarks.
/// </para>
/// <para>
/// <strong>The Known-open item.</strong> <c>FUN_0044bed8</c> (<see cref="Defect"/>) and
/// <c>FUN_0044c528</c> (a wholesale "annex every remaining city" cascade, <em>not</em> implemented here —
/// nothing in any Done-when line needs it) are two ownership writers
/// <c>decompiled-city-capture-resolution.md</c> decompiles without fully establishing their trigger
/// conditions. <c>FUN_0044bed8</c>'s own direct caller was not traced when that report was written;
/// <c>decompiled-defection-and-siege-attrition.md</c> resolves it: <c>FUN_0044ba1c</c>
/// (<see cref="RunCascade"/>), reached from the end of a successful forced capture, calls
/// <c>FUN_0044bed8</c> under the conditions <see cref="RunCascade"/> reproduces, and that report
/// independently corroborates <c>FUN_0044bed8</c> as the defection routine (never writes population or
/// fortification, matching the Modena defection observation exactly). <c>FUN_0044c528</c>'s caller, by
/// contrast, <em>is</em> named — "reached from <c>\"conquer\"</c> and called conditionally from
/// <c>FUN_0044bb18</c> when a losing nation's unity/city-count drop below thresholds"
/// (<c>decompiled-city-capture-resolution.md</c>) — what is not established is its full trigger
/// conditions and effects ("not decompiled in depth this pass," same report); not implementing it is
/// still correct, since nothing in this task's Done-when lines needs a wholesale annex-everything
/// cascade distinct from the single-city capture and cascade this task already implements. This task
/// follows the entry's own instruction and keeps every defection-specific ruleset field
/// (<see cref="CaptureRules.DefectionUnityGain"/>, <see cref="CaptureRules.DefectionUnityLoss"/>,
/// <see cref="CaptureRules.DefectionUnityLossFloor"/>, <see cref="CaptureRules.DefectionTreasuryCreditMultiplier"/>)
/// tagged <c>[derived]</c> rather than <c>[confirmed]</c>, naming this inference explicitly (see each
/// field's own remarks on <see cref="CaptureRules"/>).
/// </para>
/// <para>
/// <strong>The two-entity probe.</strong> Every mutation here touches exactly the two nations and the one
/// (or, through the cascade, several) cities actually involved — a third, untouched nation's own record and
/// a third city's own record are structurally guaranteed unchanged, because <see cref="ReplaceNation"/> and
/// <see cref="ReplaceCity"/> rebuild their lists by matching id, leaving every other element the exact same
/// reference it started as.
/// </para>
/// </remarks>
public static class CityCaptureResolver
{
    /// <summary>
    /// The full outcome of one siege attempt, given T16's own resolved <see cref="BattleResult"/>: the
    /// confirmed <c>"falls to"</c> transfer and cascade when the attacker won, or the confirmed
    /// <c>"fails to capture"</c> message and no other change when the defender held (DoD 6).
    /// </summary>
    /// <param name="state">The state <em>after</em> <see cref="InstantBattleResolver.ResolveSiege"/> ran (its own attrition already applied).</param>
    /// <param name="siegeResult">The siege's own <see cref="BattleResult"/> — must be <see cref="BattleKind.Siege"/>.</param>
    /// <param name="ruleset">Every constant this resolver uses.</param>
    /// <param name="archerUnitTypeId">Forwarded to <see cref="SiegeStrength.Attacker"/> for the cascade's own attacker-strength reuse.</param>
    /// <param name="fortifyOrderId">The ruleset's fortification order, for decoding a candidate city's fortification word.</param>
    /// <param name="events">Where the outcome publishes.</param>
    /// <exception cref="ArgumentException"><paramref name="siegeResult"/> is not a siege, or names an unknown army, city or nation.</exception>
    public static GameState ResolveOutcome(
        GameState state,
        BattleResult siegeResult,
        Ruleset ruleset,
        string archerUnitTypeId,
        string fortifyOrderId,
        IEventSink events)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(siegeResult);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(events);

        if (siegeResult.Kind != BattleKind.Siege)
        {
            throw new ArgumentException(
                $"'{nameof(siegeResult)}' must be a siege result, not {siegeResult.Kind}.", nameof(siegeResult));
        }

        var city = state.CityById(siegeResult.DefenderId)
                   ?? throw new ArgumentException($"'{siegeResult.DefenderId}' is not a known city.", nameof(siegeResult));

        // T63 Decision 3: in the original, a besieger emptied by its OWN casualties still captures the
        // city, which ends up with owner -1. CityState.Owner is a non-null nation id, so that result
        // cannot be represented here. [designed, departs from the original on purpose] -- both presets
        // treat this as a failed attempt instead, after InstantBattleResolver.ResolveSiege's erosion has
        // already been applied to `state`. `state` (not siegeResult.AttackerId's pre-battle strength) is
        // consulted because ResolveSiege's own attrition and deletion pass (#289) already ran; an army
        // the deletion pass emptied entirely is no longer present in state.Armies at all.
        var attackerArmy = state.ArmyById(siegeResult.AttackerId);
        if (siegeResult.AttackerWon && attackerArmy is { TotalTroops: > 0 })
        {
            return Capture(state, siegeResult.AttackerId, city.Id, ruleset, archerUnitTypeId, fortifyOrderId, events);
        }

        var attackerNation = RequireNation(state, siegeResult.AttackerNationId);
        var defenderNation = RequireNation(state, siegeResult.DefenderNationId);
        events.Publish(new CityFailsToBeCaptured(attackerNation.Name, city.Name, defenderNation.Name));
        return state;
    }

    /// <summary>
    /// <c>FUN_0044bb18</c>: the forced-capture ownership transfer, called once the attacker is already
    /// known to have won the siege. Transfers tax base and wealth (T35's helper), credits the new owner's
    /// treasury (DoD 9), moves unity +9/−15 (DoD 3), moves loyalty to its floor or recapture target
    /// (DoD 2), clears the old owner's recruitment slots at this city, checks the old owner for
    /// elimination (DoD 4), runs the cascading-defection sweep (<see cref="RunCascade"/>), and publishes
    /// <c>"falls to"</c> — plus <c>nation.conquered</c> when the old owner was just eliminated.
    /// </summary>
    /// <param name="state">The state to transfer against.</param>
    /// <param name="attackerArmyId">The besieging army whose nation becomes the new owner.</param>
    /// <param name="cityId">The captured city.</param>
    /// <param name="ruleset">Every constant this resolver uses.</param>
    /// <param name="archerUnitTypeId">Forwarded to <see cref="SiegeStrength.Attacker"/> for the cascade's attacker-strength term.</param>
    /// <param name="fortifyOrderId">The ruleset's fortification order, for the cascade's own defender-strength evaluations.</param>
    /// <param name="events">Where this publishes.</param>
    /// <exception cref="ArgumentException">An id names no army, city or nation.</exception>
    public static GameState Capture(
        GameState state,
        string attackerArmyId,
        string cityId,
        Ruleset ruleset,
        string archerUnitTypeId,
        string fortifyOrderId,
        IEventSink events)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(events);

        var attackerArmy = state.ArmyById(attackerArmyId)
                            ?? throw new ArgumentException($"'{attackerArmyId}' is not a known army.", nameof(attackerArmyId));
        var city = state.CityById(cityId)
                   ?? throw new ArgumentException($"'{cityId}' is not a known city.", nameof(cityId));
        var newOwner = RequireNation(state, attackerArmy.Nation);
        var oldOwner = RequireNation(state, city.Owner);

        // T86: captured before any mutation below, since ConquestCascade's post-cascade trigger check
        // needs to know what the loser's capital was *at the moment it fell*, not whatever
        // CapitalCityId happens to read after this method's own city/nation transfers run.
        var wasCapital = oldOwner.CapitalCityId is { } capitalId
                          && string.Equals(capitalId, city.Id, StringComparison.Ordinal);
        var formerCapitalId = oldOwner.CapitalCityId;

        var rules = ruleset.Capture;
        var contribution = CityTaxContribution.Compute(city);
        var (transferredNewOwner, transferredOldOwner) = CityOwnershipTaxTransfer.Transfer(city, newOwner, oldOwner, ruleset);

        transferredNewOwner = transferredNewOwner with
        {
            Treasury = transferredNewOwner.Treasury + (contribution * rules.CaptureTreasuryCreditMultiplier),
            Unity = Math.Min(ruleset.Economy.UnityCap, transferredNewOwner.Unity + rules.CaptureUnityGain),
        };

        // No floor on the loser's side: the report gives none for the forced-capture -15, unlike the
        // defection -20 (DefectionUnityLossFloor).
        transferredOldOwner = transferredOldOwner with
        {
            Unity = transferredOldOwner.Unity - rules.CaptureUnityLoss,
            RecruitmentSlots = WithoutSlotsTargeting(transferredOldOwner.RecruitmentSlots, city.Id),
        };

        var transferredCity = city with
        {
            Owner = newOwner.Id,
            Loyalty = LoyaltyAfterCapture(city, newOwner.Id, ruleset),
        };

        var newState = state with { Cities = ReplaceCity(state.Cities, transferredCity) };
        newState = newState with
        {
            Nations = ReplaceNation(ReplaceNation(newState.Nations, transferredNewOwner), transferredOldOwner),
        };

        events.Publish(new CityFallsToNation(city.Name, oldOwner.Name, newOwner.Name));

        var attackerStrength = SiegeStrength.Attacker(attackerArmy.Units, attackerArmy.Morale, ruleset, archerUnitTypeId);
        var fortifyOrder = RequireCityOrder(ruleset, fortifyOrderId);

        var stateAfterCascade = RunCascade(
            newState, transferredCity, oldOwner.Id, newOwner.Id, attackerStrength, attackerArmy, fortifyOrder, ruleset, events);

        // T86: the conquest trigger reads the loser's city count *after* this single capture and the
        // regular cascade have both already run, exactly as FUN_0044BB18's own checks do ("These checks
        // run after the city has moved and the cascade FUN_0044BA1C has run" -- decompiled-elimination-
        // cleanup.md §4). A defection reaching the old owner's last city during the cascade above (rare;
        // see NationElimination's own remarks) already fully eliminated it through Defect's own path, so
        // ConquestTrigger.Evaluate below is a no-op for an already-eliminated nation.
        var (stateAfterTrigger, shouldConquer) = ConquestTrigger.Evaluate(
            stateAfterCascade, ruleset, oldOwner.Id, formerCapitalId, wasCapital, fortifyOrder, events);

        return shouldConquer
            ? ConquestCascade.Apply(stateAfterTrigger, ruleset, oldOwner.Id, newOwner.Id, events)
            : stateAfterTrigger;
    }

    /// <summary>
    /// <c>FUN_0044bed8</c>: a city changes hands without a siege. Transfers tax base and wealth, credits
    /// the new owner's treasury at its own (different) multiplier, moves unity +3/−20 floored at 250, moves
    /// loyalty, clears the old owner's recruitment slots at this city that still hold troops (a 0-troop
    /// slot stays -- <c>decompiled-quarterly-rebellion.md</c> §"The engine removes every slot at the
    /// city", corrected by T86), and checks the old owner for elimination — but never touches
    /// <see cref="CityState.PopulationThousands"/> or <see cref="CityState.FortificationCode"/>
    /// (<c>defection.neverChangesPopOrFort</c>). Public so it is independently testable, and so
    /// <see cref="RunCascade"/> can call it for each qualifying candidate.
    /// </summary>
    /// <remarks>
    /// <strong>T86: matches the original's own defection elimination block exactly</strong>
    /// (<c>decompiled-elimination-cleanup.md</c> §4, <c>FUN_0044BED8</c> — "does less" than conquest): no
    /// conquest news, no treasury change, no capital sentinel (<see cref="NationElimination.ApplyIfLastCityLost"/>
    /// leaves <see cref="NationState.CapitalCityId"/> exactly as it stood — see that method's own
    /// remarks), and <see cref="NationState.ConqueredBy"/> set to the receiver. This method never calls
    /// <see cref="ConquestCascade"/>: a defection can only take a nation's literal last city, never the
    /// <c>&lt; 6 cities</c> conquest threshold, because <see cref="RunCascade"/>'s candidates are always a
    /// live nation's non-capital cities in the original (a defection reaching the capital, or reaching
    /// zero cities other than through this exact path, is the pre-existing, out-of-scope cascade gap
    /// <see cref="RunCascade"/>'s own remarks do not claim to close — see that method's remarks).
    /// </remarks>
    /// <param name="state">The state to transfer against.</param>
    /// <param name="cityId">The defecting city.</param>
    /// <param name="newOwnerId">The nation the city defects to.</param>
    /// <param name="ruleset">Every constant this resolver uses.</param>
    /// <param name="events">Where this publishes <c>city.defects-to</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="cityId"/> or <paramref name="newOwnerId"/> is not known.</exception>
    public static GameState Defect(GameState state, string cityId, string newOwnerId, Ruleset ruleset, IEventSink events)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(events);

        var city = state.CityById(cityId)
                   ?? throw new ArgumentException($"'{cityId}' is not a known city.", nameof(cityId));
        var newOwner = RequireNation(state, newOwnerId);
        var oldOwner = RequireNation(state, city.Owner);

        var rules = ruleset.Capture;
        var contribution = CityTaxContribution.Compute(city);
        var (transferredNewOwner, transferredOldOwner) = CityOwnershipTaxTransfer.Transfer(city, newOwner, oldOwner, ruleset);

        transferredNewOwner = transferredNewOwner with
        {
            Treasury = transferredNewOwner.Treasury + (contribution * rules.DefectionTreasuryCreditMultiplier),
            Unity = Math.Min(ruleset.Economy.UnityCap, transferredNewOwner.Unity + rules.DefectionUnityGain),
        };

        transferredOldOwner = transferredOldOwner with
        {
            Unity = Math.Max(rules.DefectionUnityLossFloor, transferredOldOwner.Unity - rules.DefectionUnityLoss),
            RecruitmentSlots = WithoutTroopSlotsTargeting(transferredOldOwner.RecruitmentSlots, city.Id),
        };

        // Population and fortification are deliberately absent from this `with`: FUN_0044bed8 never
        // writes either (defection.neverChangesPopOrFort), unlike a forced capture's own siege attrition.
        var transferredCity = city with
        {
            Owner = newOwner.Id,
            Loyalty = LoyaltyAfterDefection(city, newOwner.Id, ruleset),
        };

        var newState = state with { Cities = ReplaceCity(state.Cities, transferredCity) };

        var (oldOwnerAfterElimination, oldOwnerEliminated) =
            NationElimination.ApplyIfLastCityLost(newState, transferredOldOwner, ruleset, conquerorId: newOwnerId);

        newState = newState with
        {
            Nations = ReplaceNation(ReplaceNation(newState.Nations, transferredNewOwner), oldOwnerAfterElimination),
        };
        // Both run only when this defection just eliminated the old owner -- see each method's own
        // remarks (EliminationForces: T84; RelationTransitions.ResetAllOnElimination: rework round 1, B1).
        // T86: no NationConquered here -- the original's own defection elimination block writes no
        // conquest news (see this method's own remarks).
        if (oldOwnerEliminated) newState = EliminationForces.Dispose(newState, oldOwner.Id, newOwner.Id);
        if (oldOwnerEliminated) newState = RelationTransitions.ResetAllOnElimination(newState, ruleset, oldOwner.Id);

        events.Publish(new CityDefectsToNation(city.Name, oldOwner.Name, newOwner.Name));

        return newState;
    }

    /// <summary>
    /// <c>FUN_0044ba1c</c>: after a forced capture, every other city that shared the just-captured city's
    /// old owner is checked, in <see cref="GameState.Cities"/>'s own stable order, against the confirmed
    /// gate — not <see cref="NationState.CapitalCityId"/> of <em>any</em> of the sixteen nation records
    /// (<c>FUN_0044b8d0</c>, checked before anything else the pseudocode does; T90/#409, correcting T17's
    /// own misreading of that gate as "not already contested", <see cref="CityState.UnderSiege"/> — see
    /// this method's own remark at the gate below), within <see cref="CaptureRules.CascadeDistanceMax"/> of
    /// the besieging army (Chebyshev; see <see cref="CaptureRules"/>'s own remarks on that field), the new
    /// owner's unity still under <see cref="CaptureRules.CascadeUnityThreshold"/>, the candidate's
    /// <see cref="CompleteDefenderStrength"/> (halved by <see cref="CaptureRules.CascadeAllegiantDefenseDivisor"/>
    /// when the candidate's own allegiance already matches the new owner) below the besieging army's own
    /// <see cref="SiegeStrength.Attacker"/> strength, and the candidate's own loyalty under
    /// <see cref="CaptureRules.CascadeLoyaltyThreshold"/>. Each qualifying candidate is passed to
    /// <see cref="Defect"/> before the next candidate is evaluated, so a candidate later in iteration order
    /// sees the new owner's unity <em>after</em> every earlier defection in the same sweep already raised
    /// it — matching the decompiled loop reading the nation record live, not a snapshot taken once at the
    /// top of the loop.
    /// </summary>
    private static GameState RunCascade(
        GameState state,
        CityState justCapturedCity,
        string oldOwnerId,
        string newOwnerId,
        int attackerStrength,
        ArmyState attackerArmy,
        CityOrderRule fortifyOrder,
        Ruleset ruleset,
        IEventSink events)
    {
        var rules = ruleset.Capture;
        var currentState = state;

        // Snapshot the candidate set once, before any defection in this sweep can change who "shares the
        // just-captured city's old owner" -- the just-captured city itself is excluded because its own
        // Owner already changed to the new owner in `state`, so it can never match `oldOwnerId` again.
        foreach (var candidate in state.Cities)
        {
            if (string.Equals(candidate.Id, justCapturedCity.Id, StringComparison.Ordinal)
                || !string.Equals(candidate.Owner, oldOwnerId, StringComparison.Ordinal))
            {
                continue;
            }

            // T90/#409 (bug #407): FUN_0044ba1c's real gate is FUN_0044b8d0(city) -- "is this city any of
            // the sixteen nations' own capital", read live off every NationState.CapitalCityId in
            // currentState, not T17's CityState.UnderSiege ("if not already contested"), which the
            // original's own sweep never checks at all. The read walks every nation record regardless of
            // NationState.Eliminated -- FUN_0044b8d0 has no liveness check of its own, and T86's corrected
            // Defect leaves an eliminated nation's CapitalCityId exactly as it stood (NationElimination's
            // own remarks), so a stale capital pointer into a city the loser now owns is still reachable
            // here and still gates it. CityState.UnderSiege itself is untouched -- other callers
            // (AiEconomyPhase, CityOrderProgressSystem, OrderCityCommandHandler) still read it for their
            // own, unrelated purposes; only this sweep's gate changes.
            //
            // The decompile checks FUN_0044b8d0 twice: here, and again just before FUN_0044bed8. The
            // second check can never change the outcome -- no defection in this sweep writes a capital
            // (NationElimination leaves CapitalCityId alone, and ConquestCascade's own capital-sentinel
            // write is a different code path entirely, not reached from here) -- so only the first is
            // reproduced.
            if (currentState.Nations.Any(n =>
                    n.CapitalCityId is { } capitalId && string.Equals(capitalId, candidate.Id, StringComparison.Ordinal)))
            {
                continue;
            }

            if (ChebyshevDistance(candidate.X, candidate.Y, attackerArmy.X, attackerArmy.Y) >= rules.CascadeDistanceMax)
            {
                continue;
            }

            var currentCandidate = currentState.CityById(candidate.Id)!;
            var currentNewOwner = currentState.NationById(newOwnerId)!;
            if (currentNewOwner.Unity >= rules.CascadeUnityThreshold || currentCandidate.Loyalty >= rules.CascadeLoyaltyThreshold)
            {
                continue;
            }

            var candidateOwner = currentState.NationById(currentCandidate.Owner)!;
            var isCandidateCapital = candidateOwner.CapitalCityId is { } capitalId
                                      && string.Equals(capitalId, currentCandidate.Id, StringComparison.Ordinal);
            var candidateOwnerDiffers = !string.Equals(currentCandidate.Owner, currentCandidate.Allegiance, StringComparison.Ordinal);

            var otherDefense = CompleteDefenderStrength.Compute(
                currentCandidate, fortifyOrder, isCandidateCapital, candidateOwnerDiffers, candidateOwner, ruleset);

            if (string.Equals(currentCandidate.Allegiance, newOwnerId, StringComparison.Ordinal))
            {
                otherDefense /= rules.CascadeAllegiantDefenseDivisor;
            }

            if (otherDefense < attackerStrength)
            {
                currentState = Defect(currentState, currentCandidate.Id, newOwnerId, ruleset, events);
            }
        }

        return currentState;
    }

    /// <summary>
    /// T86 (<c>decompiled-quarterly-rebellion.md</c> §3, research <c>235af11</c>): a forced capture's
    /// loyalty is a formula, not the flat floor DoD 2 originally assigned. When the city's allegiance
    /// already matches the new owner: <c>min(AllegiantRecaptureTarget, AllegiantRecaptureBase − L′)</c>
    /// (90, 140). Otherwise: <c>max(ForcedCaptureFloor, min(ForcedCaptureCap, NonAllegiantTransferBase −
    /// L′))</c> (40, 60, 100). <c>L′</c> is <paramref name="city"/>'s own <see cref="CityState.Loyalty"/>
    /// at the point this runs — already post-siege-erosion, since <see cref="InstantBattleResolver.ResolveSiege"/>
    /// applied that to <c>state</c> before <see cref="Capture"/> ever reads <paramref name="city"/> from it.
    /// The engine's old flat 40 was wrong for 5 of the report's save captures (Mediolanum, Felsina,
    /// Brixia, Byblos, Gordium); the corrected formula reproduces all 7 (the two allegiant captures,
    /// Laranda and Caere, both land on 90 either way).
    /// </summary>
    private static int LoyaltyAfterCapture(CityState city, string newOwnerId, Ruleset ruleset)
    {
        var loyalty = ruleset.Loyalty;
        if (string.Equals(city.Allegiance, newOwnerId, StringComparison.Ordinal))
        {
            return Math.Min(loyalty.AllegiantRecaptureTarget, loyalty.AllegiantRecaptureBase - city.Loyalty);
        }

        var target = loyalty.NonAllegiantTransferBase - city.Loyalty;
        return Math.Max(loyalty.ForcedCaptureFloor, Math.Min(loyalty.ForcedCaptureCap, target));
    }

    /// <summary>
    /// T86 (<c>decompiled-quarterly-rebellion.md</c> §3): a defection's loyalty is likewise a formula.
    /// Allegiant case: the same <c>min(AllegiantRecaptureTarget, AllegiantRecaptureBase − L)</c> capture
    /// uses (Synnada 62 → 78, Tarquinii/Ariminum → 90). Otherwise:
    /// <c>min(DefectionFloor, max(DefectionFormulaFloor, NonAllegiantTransferBase − L))</c> (65, 50, 100)
    /// — note the outer/inner clamp order is swapped from the capture formula's own (max-of-min there,
    /// min-of-max here), exactly as the report gives each. <c>L</c> is pre-transfer loyalty; a defection
    /// has no siege erosion. The engine's old flat 65 was wrong for 5 of the report's 8 cascade
    /// defections (Aradus, Hemesa, Palmyra, Modena, Acroinon all actually land on 50, this formula's
    /// floor).
    /// </summary>
    private static int LoyaltyAfterDefection(CityState city, string newOwnerId, Ruleset ruleset)
    {
        var loyalty = ruleset.Loyalty;
        if (string.Equals(city.Allegiance, newOwnerId, StringComparison.Ordinal))
        {
            return Math.Min(loyalty.AllegiantRecaptureTarget, loyalty.AllegiantRecaptureBase - city.Loyalty);
        }

        var target = loyalty.NonAllegiantTransferBase - city.Loyalty;
        return Math.Min(loyalty.DefectionFloor, Math.Max(loyalty.DefectionFormulaFloor, target));
    }

    private static ValueList<RecruitmentSlot> WithoutSlotsTargeting(ValueList<RecruitmentSlot> slots, string cityId)
    {
        var kept = new List<RecruitmentSlot>(slots.Count);
        foreach (var slot in slots)
        {
            if (!string.Equals(slot.TargetCityId, cityId, StringComparison.Ordinal))
            {
                kept.Add(slot);
            }
        }

        return ValueList.From(kept);
    }

    /// <summary>
    /// T86 (<c>decompiled-quarterly-rebellion.md</c> §"Nothing is written before the transfer" / the
    /// slot-removal note): a defection removes only the old owner's recruitment slots at
    /// <paramref name="cityId"/> that still hold troops — a 0-troop slot stays, unlike
    /// <see cref="WithoutSlotsTargeting"/> (a forced capture's own rule, which removes every matching
    /// slot regardless of troop count; the report does not correct that one).
    /// </summary>
    private static ValueList<RecruitmentSlot> WithoutTroopSlotsTargeting(ValueList<RecruitmentSlot> slots, string cityId)
    {
        var kept = new List<RecruitmentSlot>(slots.Count);
        foreach (var slot in slots)
        {
            var targetsThisCityWithTroops = slot.Troops > 0
                                             && string.Equals(slot.TargetCityId, cityId, StringComparison.Ordinal);
            if (!targetsThisCityWithTroops)
            {
                kept.Add(slot);
            }
        }

        return ValueList.From(kept);
    }

    internal static ValueList<CityState> ReplaceCity(ValueList<CityState> cities, CityState updated)
    {
        var replaced = new List<CityState>(cities.Count);
        foreach (var city in cities)
        {
            replaced.Add(string.Equals(city.Id, updated.Id, StringComparison.Ordinal) ? updated : city);
        }

        return ValueList.From(replaced);
    }

    internal static ValueList<NationState> ReplaceNation(ValueList<NationState> nations, NationState updated)
    {
        var replaced = new List<NationState>(nations.Count);
        foreach (var nation in nations)
        {
            replaced.Add(string.Equals(nation.Id, updated.Id, StringComparison.Ordinal) ? updated : nation);
        }

        return ValueList.From(replaced);
    }

    private static NationState RequireNation(GameState state, string nationId) =>
        state.NationById(nationId)
        ?? throw new ArgumentException($"'{nationId}' is not a known nation.", nameof(nationId));

    private static CityOrderRule RequireCityOrder(Ruleset ruleset, string orderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);

        foreach (var order in ruleset.CityOrders.Orders)
        {
            if (string.Equals(order.Id, orderId, StringComparison.Ordinal))
            {
                return order;
            }
        }

        throw new ArgumentException(
            $"Ruleset '{ruleset.Id}' declares no '{orderId}' city order, which the cascade's defender-strength "
            + "term decodes through.",
            nameof(orderId));
    }

    /// <summary>
    /// Chebyshev (chessboard) distance — <see cref="Battle.ScatterPlacement"/>'s own map-distance
    /// convention, reused here since the report gives the cascade's distance threshold but not the metric.
    /// </summary>
    private static int ChebyshevDistance(int x1, int y1, int x2, int y2) =>
        Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));
}
