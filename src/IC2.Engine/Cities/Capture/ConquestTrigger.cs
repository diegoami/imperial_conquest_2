using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Cities.Capture;

/// <summary>
/// <c>FUN_0044BB18</c>'s own post-cascade checks (<c>decompiled-elimination-cleanup.md</c> §4, :50150,
/// research <c>43a44a1</c>) — called once by <see cref="CityCaptureResolver.Capture"/>, after the
/// besieged city has moved and <see cref="CityCaptureResolver"/>'s own defection cascade has already run,
/// exactly where the original places these checks:
/// </summary>
/// <remarks>
/// <code>
/// if captured city is not a capital:
///     if loser.cityCount &lt; 6:  conquer
/// else:                                          // the loser's capital fell
///     moved = false
///     if loser.unity &gt; 400 and loser.cityCount &gt; 6:
///         unity -= 50                            // always, attempt or not
///         best = the highest-scoring city &gt; 10 tiles from the fallen capital, score &gt;= 1  // review round 2, B4
///         if best exists:
///             capital = best; moved = true; news "have moved their capital to"
///     if not moved: conquer
/// </code>
/// <para>
/// <strong>The destination city.</strong> <c>decompiled-elimination-cleanup.md</c>'s addendum
/// (research <c>dcd8fd7</c>, "<c>FUN_0044BD2C(loser, &amp;moved)</c>, :50234: the capital move (addendum
/// 2026-09-26)", lines 147–214) reads <c>FUN_0044BD2C</c> directly and gives the selection rule the base
/// report's one-line summary above does not: every city the loser still owns (the fallen capital is never
/// a candidate — it already belongs to the capturer by the time this runs), in city-table order, is
/// scored <c>(strength / 10) / distance</c> — <c>strength</c> the city's full
/// <see cref="CompleteDefenderStrength.Compute"/>, <c>distance</c> its Chebyshev distance from the fallen
/// capital's own coordinates, both integer divisions truncating toward zero, taken in that order. A city
/// must be strictly more than <see cref="CaptureRules.CapitalMoveMinDistanceTiles"/> tiles away to be
/// scored at all. The best score must be strictly positive — <c>bestScore</c> starts at 0 and the
/// comparison is strict <c>&gt;</c> — so a city can score 0 and still lose to "no destination"; ties keep
/// the lowest city-table index, which falls out for free from scanning in table order and only replacing
/// the champion on a strictly better score. An earlier revision of this remark claimed the report gave no
/// selection rule at all; that claim was wrong (review round 1, B1) and is replaced by the rule above.
/// </para>
/// <para>
/// <strong>The new capital's boosts.</strong> Plan PR #410 (2026-09-26) brought the addendum's own
/// five-boost table into Scope: on a successful move, the destination's own loyalty, fortification,
/// population, maximum population and tribute are all raised — see <see cref="Evaluate"/>'s own remarks
/// for the exact fields and the fortification word's own raw-versus-decoded distinction. An earlier
/// revision of this remark said these were deliberately out of Scope; that stood only until PR #410.
/// <strong>Still not reproduced.</strong> The map marker and its repaint are presentation, not state, and
/// stay unmodelled.
/// </para>
/// </remarks>
public static class ConquestTrigger
{
    /// <summary>
    /// Decides whether <paramref name="loserId"/> should now be conquered outright, applying the
    /// capital-move attempt's own effects (unity −50, capital reassignment, the
    /// <see cref="NationCapitalMoved"/> news line) when that branch is taken — the two are inseparable in
    /// the original's own control flow (<see cref="Model.CaptureRules.CapitalMoveUnityLoss"/>'s own
    /// remarks: the cost is paid whether or not a destination is actually found).
    /// </summary>
    /// <param name="state">
    /// The state after the single besieged city moved and the regular cascade ran — so
    /// <paramref name="ruleset"/>'s own unity/city-count gate constants are compared against values that
    /// already reflect this capture's own −15 unity and the transferred city no longer counting toward
    /// <paramref name="loserId"/>, exactly as the original tests them after its own :50182/:50185 writes
    /// (research addendum, "State on entry").
    /// </param>
    /// <param name="ruleset">Every constant this trigger uses.</param>
    /// <param name="loserId">The nation whose city just fell.</param>
    /// <param name="formerCapitalId">
    /// <paramref name="loserId"/>'s own <see cref="NationState.CapitalCityId"/> as it stood <em>before</em>
    /// the besieged city's ownership changed — needed because a capital capture already moved that city
    /// away from <paramref name="loserId"/> by the time this runs, so <paramref name="state"/> can no
    /// longer answer "was the captured city the capital" itself.
    /// </param>
    /// <param name="capturedCityWasCapital">Whether the city <see cref="CityCaptureResolver.Capture"/> just transferred was <paramref name="formerCapitalId"/>.</param>
    /// <param name="fortifyOrder">
    /// The ruleset's <c>"fortify"</c> order, forwarded to <see cref="CompleteDefenderStrength.Compute"/>
    /// for every candidate destination's own strength score.
    /// </param>
    /// <param name="events">Where a successful capital move publishes <see cref="NationCapitalMoved"/>.</param>
    /// <returns>
    /// The (possibly capital-moved) state, and whether <see cref="ConquestCascade.Apply"/> should now run
    /// against it. <see langword="false"/> both when the loser survives outright and when a capital move
    /// succeeded — either way, no conquest.
    /// </returns>
    public static (GameState State, bool ShouldConquer) Evaluate(
        GameState state,
        Ruleset ruleset,
        string loserId,
        string? formerCapitalId,
        bool capturedCityWasCapital,
        CityOrderRule fortifyOrder,
        IEventSink events)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentException.ThrowIfNullOrWhiteSpace(loserId);
        ArgumentNullException.ThrowIfNull(fortifyOrder);
        ArgumentNullException.ThrowIfNull(events);

        var rules = ruleset.Capture;
        var loser = state.NationById(loserId)
                    ?? throw new ArgumentException($"'{loserId}' is not a known nation.", nameof(loserId));

        // Defensive: a nation the regular defection cascade already emptied to zero cities (through
        // CityCaptureResolver.Defect's own elimination path -- rare; see NationElimination's own
        // remarks) is already fully handled. Nothing here re-runs against an already-eliminated nation.
        if (loser.Eliminated)
        {
            return (state, false);
        }

        var cityCount = state.CountCitiesOwnedBy(loserId);

        if (!capturedCityWasCapital)
        {
            return (state, cityCount < rules.ConquestCityCountThreshold);
        }

        var attemptsMove = loser.Unity > rules.CapitalMoveUnityThreshold
                            && cityCount > rules.CapitalMoveCityCountThreshold;
        if (!attemptsMove)
        {
            return (state, true);
        }

        // The unity cost is paid unconditionally, before the search, whether or not a destination is
        // found (research addendum: ":50255, FIRST, unconditionally, no floor").
        var movedLoser = loser with { Unity = loser.Unity - rules.CapitalMoveUnityLoss };
        var stateAfterUnityLoss = state with
        {
            Nations = CityCaptureResolver.ReplaceNation(state.Nations, movedLoser),
        };

        var destination = FindCapitalMoveDestination(
            stateAfterUnityLoss, ruleset, loserId, formerCapitalId, rules.CapitalMoveMinDistanceTiles, fortifyOrder);

        if (destination is null)
        {
            return (stateAfterUnityLoss, true);
        }

        // The new capital's own five boosts (plan PR #410, addendum :50286-50295) -- applied only to
        // the destination, never to the fallen capital or any other city. FortificationCode is boosted
        // on the RAW stored word, not the decoded percentage: an order already in progress (a code above
        // the fortify order's own MaxPercent, FortificationCode.IsOrderInProgress) plus this gain always
        // exceeds CapitalMoveNewCapitalStatCap, so the min collapses it to a plain finished value at the
        // cap -- reproducing the addendum's own "the move discards it" remark without a separate branch.
        var boostedDestination = destination with
        {
            Loyalty = Math.Min(rules.CapitalMoveNewCapitalStatCap, destination.Loyalty + rules.CapitalMoveNewCapitalLoyaltyGain),
            FortificationCode = Math.Min(rules.CapitalMoveNewCapitalStatCap, destination.FortificationCode + rules.CapitalMoveNewCapitalFortificationGain),
            PopulationThousands = destination.PopulationThousands + rules.CapitalMoveNewCapitalPopulationGain,
            MaxPopulationThousands = destination.MaxPopulationThousands + rules.CapitalMoveNewCapitalMaxPopulationGain,
            Tribute = destination.Tribute + rules.CapitalMoveNewCapitalTributeGain,
        };

        var relocatedLoser = movedLoser with { CapitalCityId = boostedDestination.Id };
        var stateAfterMove = stateAfterUnityLoss with
        {
            Cities = CityCaptureResolver.ReplaceCity(stateAfterUnityLoss.Cities, boostedDestination),
            Nations = CityCaptureResolver.ReplaceNation(stateAfterUnityLoss.Nations, relocatedLoser),
        };
        events.Publish(new NationCapitalMoved(loser.Name, boostedDestination.Name));
        return (stateAfterMove, false);
    }

    /// <summary>
    /// <c>FUN_0044BD2C</c>'s own destination search (this type's own remarks, "The destination city"):
    /// every one of <paramref name="loserId"/>'s own currently-owned cities, in
    /// <see cref="GameState.Cities"/>' own stable (city-table) order, more than
    /// <paramref name="minDistanceTiles"/> Chebyshev tiles from <paramref name="formerCapitalId"/>,
    /// scored <c>(strength / 10) / distance</c>. The HIGHEST-scoring qualifying city wins outright, not
    /// merely the first one to score above 0 (review round 2, B4: an earlier revision of this summary
    /// read as though the search stopped at the first positive score, which is not this method's own
    /// behaviour — <c>ConquestTriggerTests.CapitalMove_WithALowerScoringCandidateBeforeAHigherScoringOne_PicksTheHigherScore</c>
    /// pins a later, higher-scoring city beating an earlier, lower-scoring one). Only a tie — an exactly
    /// equal score — never displaces the earlier city, which is where "the lowest city-table index"
    /// tie-break actually applies.
    /// </summary>
    private static CityState? FindCapitalMoveDestination(
        GameState state,
        Ruleset ruleset,
        string loserId,
        string? formerCapitalId,
        int minDistanceTiles,
        CityOrderRule fortifyOrder)
    {
        var former = formerCapitalId is not null ? state.CityById(formerCapitalId) : null;
        if (former is null)
        {
            return null;
        }

        var loser = state.NationById(loserId)!;

        CityState? best = null;
        var bestScore = 0;

        foreach (var city in state.Cities)
        {
            if (!string.Equals(city.Owner, loserId, StringComparison.Ordinal))
            {
                continue;
            }

            var distance = ChebyshevDistance(city.X, city.Y, former.X, former.Y);
            if (distance <= minDistanceTiles)
            {
                continue;
            }

            var ownerDiffersFromAllegiance = !string.Equals(city.Owner, city.Allegiance, StringComparison.Ordinal);
            // N7 (review round 2): always false here, a known deviation. The addendum's own
            // "Consequences" note that the original's x5/3 capital bonus would apply to a candidate that
            // still passes FUN_0044B8D0 through ANOTHER nation's stale capital pointer -- something T86's
            // own Defect path can now leave behind. Unreachable in practice until T90 (#409, tracked
            // there as S4) generalises "is a capital" to "any nation's CapitalCityId", and T87 lands
            // rebirth's own stale-pointer cases; not fixed here, and this line's own behaviour is
            // unchanged.
            var strength = CompleteDefenderStrength.Compute(
                city, fortifyOrder, isControllerCapital: false, ownerDiffersFromAllegiance, loser, ruleset);
            // Order matters in the addendum's own pseudocode (":50267, divide by 10 first, then by d"),
            // but for a non-negative strength -- which CompleteDefenderStrength.Compute always produces,
            // its three weighted terms and the garrison addend are all non-negative sums -- the two
            // orders are provably the same result: floor(floor(x / a) / b) == floor(x / (a * b)) for
            // positive integers a, b, regardless of which division runs first. Verified empirically too:
            // swapping this line's own division order and re-running the full suite leaves all 3011+
            // engine tests green (the coordinator's own round-1 suggestion, not round-0's B1 -- the
            // "swapping the division order" mutation is not observable here). Kept in the addendum's own
            // order for fidelity to the decompile, not because a swap would be catchable.
            var score = (strength / ruleset.Capture.CapitalMoveStrengthDivisor) / distance;

            if (score > bestScore)
            {
                bestScore = score;
                best = city;
            }
        }

        return best;
    }

    /// <summary>Chebyshev (chessboard) distance — the same convention <see cref="CityCaptureResolver"/>'s own cascade uses.</summary>
    private static int ChebyshevDistance(int x1, int y1, int x2, int y2) =>
        Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));
}
