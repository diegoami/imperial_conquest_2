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
///         if a city &gt; 10 tiles from the fallen capital exists:
///             capital = that city; moved = true; news "have moved their capital to"
///     if not moved: conquer
/// </code>
/// <para>
/// <strong>The destination city.</strong> The report confirms a capital move fires when "a city more
/// than 10 tiles away exists" but does not say which one is chosen when more than one qualifies —
/// <c>decompiled-elimination-cleanup.md</c> §4 and §6 give the trigger and the unity cost, not a
/// selection rule among several candidates. <c>[designed]</c>: this picks the first qualifying city in
/// <see cref="GameState.Cities"/>' own stable order (the same "first candidate in stable order wins"
/// determinism convention <see cref="CityCaptureResolver.RunCascade"/> already uses for its own
/// candidate sweep), since nothing in the report or in T86's Done-when lines needs a specific city among
/// several ties — only the conquer-vs-move boundary itself, which does not depend on which qualifying
/// city is picked.
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
    /// <param name="state">The state after the single besieged city moved and the regular cascade ran.</param>
    /// <param name="ruleset">Every constant this trigger uses.</param>
    /// <param name="loserId">The nation whose city just fell.</param>
    /// <param name="formerCapitalId">
    /// <paramref name="loserId"/>'s own <see cref="NationState.CapitalCityId"/> as it stood <em>before</em>
    /// the besieged city's ownership changed — needed because a capital capture already moved that city
    /// away from <paramref name="loserId"/> by the time this runs, so <paramref name="state"/> can no
    /// longer answer "was the captured city the capital" itself.
    /// </param>
    /// <param name="capturedCityWasCapital">Whether the city <see cref="CityCaptureResolver.Capture"/> just transferred was <paramref name="formerCapitalId"/>.</param>
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
        IEventSink events)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentException.ThrowIfNullOrWhiteSpace(loserId);
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

        var movedLoser = loser with { Unity = loser.Unity - rules.CapitalMoveUnityLoss };
        var destination = FindCapitalMoveDestination(state, loserId, formerCapitalId, rules.CapitalMoveMinDistanceTiles);

        if (destination is null)
        {
            var stateAfterFailedAttempt = state with
            {
                Nations = CityCaptureResolver.ReplaceNation(state.Nations, movedLoser),
            };
            return (stateAfterFailedAttempt, true);
        }

        var relocatedLoser = movedLoser with { CapitalCityId = destination.Id };
        var stateAfterMove = state with
        {
            Nations = CityCaptureResolver.ReplaceNation(state.Nations, relocatedLoser),
        };
        events.Publish(new NationCapitalMoved(loser.Name, destination.Name));
        return (stateAfterMove, false);
    }

    /// <summary>
    /// The first of <paramref name="loserId"/>'s own currently-owned cities, in
    /// <see cref="GameState.Cities"/>' own stable order, more than <paramref name="minDistanceTiles"/>
    /// Chebyshev tiles from <paramref name="formerCapitalId"/> — see this type's own remarks on why "the
    /// first in stable order" and not some other tie-break.
    /// </summary>
    private static CityState? FindCapitalMoveDestination(
        GameState state, string loserId, string? formerCapitalId, int minDistanceTiles)
    {
        var former = formerCapitalId is not null ? state.CityById(formerCapitalId) : null;
        if (former is null)
        {
            return null;
        }

        foreach (var city in state.Cities)
        {
            if (!string.Equals(city.Owner, loserId, StringComparison.Ordinal)
                || string.Equals(city.Id, formerCapitalId, StringComparison.Ordinal))
            {
                continue;
            }

            if (ChebyshevDistance(city.X, city.Y, former.X, former.Y) > minDistanceTiles)
            {
                return city;
            }
        }

        return null;
    }

    /// <summary>Chebyshev (chessboard) distance — the same convention <see cref="CityCaptureResolver"/>'s own cascade uses.</summary>
    private static int ChebyshevDistance(int x1, int y1, int x2, int y2) =>
        Math.Max(Math.Abs(x1 - x2), Math.Abs(y1 - y2));
}
