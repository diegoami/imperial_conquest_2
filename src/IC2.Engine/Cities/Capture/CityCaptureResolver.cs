using IC2.Engine.Battle;
using IC2.Engine.Core;
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
/// <see cref="SiegeStrength.Defender"/> (already scaled by the capital-and-loyalty and
/// owner-vs-allegiance branches, and by the siege entry point's own separate
/// <see cref="SiegeRules.AttackerIsAllegianceDefenderReductionPercent"/> ×9/10 reduction when the attacker
/// is the city's own allegiance), and it applies the attacking army's own per-attempt attrition
/// (<see cref="BattleCasualties"/>, DoD 5) — win or lose, every time. <see cref="ResolveOutcome"/> takes
/// that already-resolved <see cref="BattleResult"/> and does not re-decide it: it only ever reads
/// <see cref="BattleResult.Winner"/>. This is deliberate and load-bearing — see
/// <see cref="CompleteDefenderStrength"/>'s own remarks for exactly where this task's "complete" defender
/// strength (garrison term included) is used instead, and why it is never used to second-guess T16's own
/// decision: doing so would mean computing the ×9/10 reduction a second time, which the task's own brief
/// forbids.
/// </para>
/// <para>
/// <strong>The Known-open item.</strong> <c>FUN_0044bed8</c> (<see cref="Defect"/>) and
/// <c>FUN_0044c528</c> (a wholesale "annex every remaining city" cascade, <em>not</em> implemented here —
/// nothing in any Done-when line needs it, and its own trigger conditions are not established) are two
/// ownership writers whose direct callers were not traced when <c>decompiled-city-capture-resolution.md</c>
/// was written. <c>decompiled-defection-and-siege-attrition.md</c> resolves the call chain for the first
/// one: <c>FUN_0044ba1c</c> (<see cref="RunCascade"/>), reached from the end of a successful forced
/// capture, calls <c>FUN_0044bed8</c> under the conditions <see cref="RunCascade"/> reproduces, and that
/// report independently corroborates <c>FUN_0044bed8</c> as the defection routine (never writes population
/// or fortification, matching the Modena defection observation exactly). This task follows the entry's own
/// instruction and keeps every defection-specific ruleset field (<see cref="CaptureRules.DefectionUnityGain"/>,
/// <see cref="CaptureRules.DefectionUnityLoss"/>, <see cref="CaptureRules.DefectionUnityLossFloor"/>,
/// <see cref="CaptureRules.DefectionTreasuryCreditMultiplier"/>) tagged <c>[derived]</c> rather than
/// <c>[confirmed]</c>, naming this inference explicitly (see each field's own remarks on
/// <see cref="CaptureRules"/>).
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

        if (siegeResult.AttackerWon)
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
            Loyalty = LoyaltyAfterTransfer(city, newOwner.Id, ruleset, forcedCapture: true),
        };

        var newState = state with { Cities = ReplaceCity(state.Cities, transferredCity) };

        var (oldOwnerAfterElimination, oldOwnerEliminated) =
            NationElimination.ApplyIfLastCityLost(newState, transferredOldOwner, ruleset);

        newState = newState with
        {
            Nations = ReplaceNation(ReplaceNation(newState.Nations, transferredNewOwner), oldOwnerAfterElimination),
        };

        events.Publish(new CityFallsToNation(city.Name, oldOwner.Name, newOwner.Name));
        if (oldOwnerEliminated)
        {
            events.Publish(new NationConquered(newOwner.Name, oldOwner.Name));
        }

        var attackerStrength = SiegeStrength.Attacker(attackerArmy.Units, attackerArmy.Morale, ruleset, archerUnitTypeId);
        var fortifyOrder = RequireCityOrder(ruleset, fortifyOrderId);

        return RunCascade(newState, transferredCity, oldOwner.Id, newOwner.Id, attackerStrength, attackerArmy, fortifyOrder, ruleset, events);
    }

    /// <summary>
    /// <c>FUN_0044bed8</c>: a city changes hands without a siege. Transfers tax base and wealth, credits
    /// the new owner's treasury at its own (different) multiplier, moves unity +3/−20 floored at 250, moves
    /// loyalty, clears the old owner's recruitment slots at this city, and checks the old owner for
    /// elimination — but never touches <see cref="CityState.PopulationThousands"/> or
    /// <see cref="CityState.FortificationCode"/> (<c>defection.neverChangesPopOrFort</c>). Public so it is
    /// independently testable, and so <see cref="RunCascade"/> can call it for each qualifying candidate.
    /// </summary>
    /// <param name="state">The state to transfer against.</param>
    /// <param name="cityId">The defecting city.</param>
    /// <param name="newOwnerId">The nation the city defects to.</param>
    /// <param name="ruleset">Every constant this resolver uses.</param>
    /// <param name="events">Where this publishes <c>city.defects-to</c> (and <c>nation.conquered</c> on elimination).</param>
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
            RecruitmentSlots = WithoutSlotsTargeting(transferredOldOwner.RecruitmentSlots, city.Id),
        };

        // Population and fortification are deliberately absent from this `with`: FUN_0044bed8 never
        // writes either (defection.neverChangesPopOrFort), unlike a forced capture's own siege attrition.
        var transferredCity = city with
        {
            Owner = newOwner.Id,
            Loyalty = LoyaltyAfterTransfer(city, newOwner.Id, ruleset, forcedCapture: false),
        };

        var newState = state with { Cities = ReplaceCity(state.Cities, transferredCity) };

        var (oldOwnerAfterElimination, oldOwnerEliminated) =
            NationElimination.ApplyIfLastCityLost(newState, transferredOldOwner, ruleset);

        newState = newState with
        {
            Nations = ReplaceNation(ReplaceNation(newState.Nations, transferredNewOwner), oldOwnerAfterElimination),
        };

        events.Publish(new CityDefectsToNation(city.Name, oldOwner.Name, newOwner.Name));
        if (oldOwnerEliminated)
        {
            events.Publish(new NationConquered(newOwner.Name, oldOwner.Name));
        }

        return newState;
    }

    /// <summary>
    /// <c>FUN_0044ba1c</c>: after a forced capture, every other city that shared the just-captured city's
    /// old owner is checked, in <see cref="GameState.Cities"/>'s own stable order, against the confirmed
    /// gate — within <see cref="CaptureRules.CascadeDistanceMax"/> of the besieging army (Chebyshev; see
    /// <see cref="CaptureRules"/>'s own remarks on that field), the new owner's unity still under
    /// <see cref="CaptureRules.CascadeUnityThreshold"/>, the candidate's <see cref="CompleteDefenderStrength"/>
    /// (halved by <see cref="CaptureRules.CascadeAllegiantDefenseDivisor"/> when the candidate's own
    /// allegiance already matches the new owner) below the besieging army's own
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
    /// DoD 2: loyalty moves to <see cref="LoyaltyRules.AllegiantRecaptureTarget"/> (90) when the city's
    /// allegiance already matches the new owner, or otherwise to
    /// <see cref="LoyaltyRules.ForcedCaptureFloor"/> (40, <c>[confirmed]</c> — the Sidon example's exact
    /// 90 → 40) for a forced capture and <see cref="LoyaltyRules.DefectionFloor"/> (65, <c>[derived]</c> by
    /// the same structural pattern) for a defection. Every observed example is an exact assignment, not a
    /// partial move, so this assigns directly.
    /// </summary>
    private static int LoyaltyAfterTransfer(CityState city, string newOwnerId, Ruleset ruleset, bool forcedCapture)
    {
        if (string.Equals(city.Allegiance, newOwnerId, StringComparison.Ordinal))
        {
            return ruleset.Loyalty.AllegiantRecaptureTarget;
        }

        return forcedCapture ? ruleset.Loyalty.ForcedCaptureFloor : ruleset.Loyalty.DefectionFloor;
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

    private static ValueList<CityState> ReplaceCity(ValueList<CityState> cities, CityState updated)
    {
        var replaced = new List<CityState>(cities.Count);
        foreach (var city in cities)
        {
            replaced.Add(string.Equals(city.Id, updated.Id, StringComparison.Ordinal) ? updated : city);
        }

        return ValueList.From(replaced);
    }

    private static ValueList<NationState> ReplaceNation(ValueList<NationState> nations, NationState updated)
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
