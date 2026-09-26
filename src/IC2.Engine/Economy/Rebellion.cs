using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Diplomacy;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Naval;

namespace IC2.Engine.Economy;

/// <summary>
/// T89 (<c>#397</c>): the quarterly rebellion, <c>FUN_0044C204</c>
/// <see href="https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-quarterly-rebellion.md">
/// decompiled-quarterly-rebellion.md</see> (research <c>235af11</c>) <c>[confirmed: listing
/// 0x0044C204-0x0044C35D]</c>. Called by <see cref="QuarterlyCityEconomySystem"/> for a non-capital city
/// whose loyalty, after that city's own quarterly draws, is under <see cref="EconomyRules.RebellionLoyaltyThreshold"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The routine writes nothing itself</strong> <c>[confirmed]</c>: every effect is
/// <see cref="CityCaptureResolver.Defect"/>'s own (<c>FUN_0044BED8</c>), reused as-is here — T86 already
/// corrected its loyalty formula and its elimination path, and the report's Hazards note "reuse the path,
/// don't write a second one" is exactly why this type never touches a city's owner, a nation's unity or
/// treasury, or the news log directly.
/// </para>
/// <para>
/// <strong>Live reads.</strong> Every lookup here (<see cref="GameState.NationById"/>,
/// <see cref="GameState.CityById"/>, <see cref="GameState.CountCitiesOwnedBy"/>,
/// <see cref="NeighbourGeography.NeighboursOf"/>) reads <paramref name="state"/> as handed in, never a
/// snapshot taken earlier in the quarter — the report's own "an earlier rebellion in the same quarterly
/// loop has already moved a city and changed the counts" note for branch (d), and
/// <see cref="QuarterlyCityEconomySystem"/>'s own caller threads the returned <see cref="GameState"/>
/// into the next city precisely so this holds.
/// </para>
/// </remarks>
public static class Rebellion
{
    /// <summary>
    /// Runs the rebellion decision for one city already under threshold, in the original's own code
    /// order: (a)/(b) when the owner is not the city's allegiance nation, else (c) then (d)
    /// <c>[confirmed: decompiled-quarterly-rebellion.md "Answer"]</c>.
    /// </summary>
    /// <param name="state">
    /// The state with this city's own quarterly loyalty draws already applied — <paramref name="city"/>
    /// must be <see cref="GameState.CityById"/>'s current record for its id.
    /// </param>
    /// <param name="world">
    /// Passed through to <see cref="NeighbourGeography.NeighboursOf"/>; only used when
    /// <paramref name="state"/> carries no <see cref="GameState.Neighbours"/> of its own, which never
    /// happens on a production path (T86).
    /// </param>
    /// <param name="ruleset">Supplies every rebellion constant; never a C# literal.</param>
    /// <param name="city">The rebelling city — read for its own Id/Owner/Allegiance/X/Y only.</param>
    /// <param name="events">Where <see cref="CityCaptureResolver.Defect"/>'s own defection news is published.</param>
    /// <returns>The resulting state — <paramref name="state"/> itself, unchanged, if nothing happens.</returns>
    public static GameState Run(GameState state, World world, Ruleset ruleset, CityState city, IEventSink events)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(events);

        return string.Equals(city.Owner, city.Allegiance, StringComparison.Ordinal)
            ? RunOwnerIsAllegiance(state, world, ruleset, city, events)
            : RunOwnerNotAllegiance(state, ruleset, city, events);
    }

    /// <summary>
    /// (a)/(b) <c>[confirmed]</c>: the owner is not the city's allegiance nation.
    /// </summary>
    private static GameState RunOwnerNotAllegiance(GameState state, Ruleset ruleset, CityState city, IEventSink events)
    {
        var allegiance = state.NationById(city.Allegiance);

        // (a) [confirmed: decompiled-quarterly-rebellion.md "Answer", 'the test is unity <= 0, the same
        // "dead" the turn loop and the diplomacy use']: "dead" is unity <= 0 -- the same bare comparison
        // the rest of the engine already uses for it (QuarterlyNationEconomySystem.cs,
        // AiDepositionHandler.cs), not a ruleset field of its own. FUN_0044C360 (rebirth) is T87's own
        // call: rebirth is final [confirmed: report "(a) is final... There is no fallback to (c) or (d)"]
        // (no fallback to (c)/(d) when it declines), so this branch does nothing here but leave the
        // decision, and this remark, for T87. allegiance is null only if city.Allegiance names no known
        // nation, which never happens on a production path (every nation record persists for the
        // scenario's life); treated the same as "dead" defensively rather than throwing.
        if (allegiance is null || allegiance.Unity <= 0)
        {
            return state;
        }

        // (b): no war, no distance, no AI check -- the allegiance nation may be human.
        return CityCaptureResolver.Defect(state, city.Id, allegiance.Id, ruleset, events);
    }

    /// <summary>
    /// (c) then (d) <c>[confirmed]</c>: the owner already is the city's allegiance nation.
    /// </summary>
    private static GameState RunOwnerIsAllegiance(GameState state, World world, Ruleset ruleset, CityState city, IEventSink events)
    {
        var receiverId = FindAttacker(state, ruleset, city) ?? FindBestNeighbour(state, world, ruleset, city);

        // Otherwise [confirmed]: no write, no news.
        return receiverId is null ? state : CityCaptureResolver.Defect(state, city.Id, receiverId, ruleset, events);
    }

    /// <summary>
    /// (c) <c>[confirmed]</c>: scans <see cref="GameState.Armies"/> in list order without breaking, so a
    /// later match overwrites an earlier one and the last matching army's owner wins — the report's own
    /// "the highest-indexed matching army decides". T89 Hazards "Army order": the engine's own
    /// <see cref="GameState.Armies"/> order is its own, not the original's creation-with-compaction order
    /// (<c>decompiled-elimination-cleanup.md</c> §2); this reproduces "the last match in the engine's own
    /// list", tested against that order, not the original's.
    /// </summary>
    private static string? FindAttacker(GameState state, Ruleset ruleset, CityState city)
    {
        var cityPoint = new GridPoint(city.X, city.Y);
        var warCode = ruleset.Diplomacy.StateCodes.War;
        string? pick = null;

        foreach (var army in state.Armies)
        {
            var distance = LandingTile.ChebyshevDistance(new GridPoint(army.X, army.Y), cityPoint);
            if (distance >= ruleset.Capture.RebellionArmyDistanceMax)
            {
                continue;
            }

            if (state.Relations.Get(city.Owner, army.Nation) != warCode)
            {
                continue;
            }

            pick = army.Nation;
        }

        return pick;
    }

    /// <summary>
    /// (d) <c>[confirmed]</c>: every live (<c>Unity &gt; 0</c>) neighbour of the owner scores
    /// <c>cities(n) − RebellionNeighbourScoreDistanceWeight × cheb(city, capital(n))</c>, both terms read
    /// live off <paramref name="state"/> — T89 Hazards "Live reads": an earlier rebellion in the same
    /// quarter already changed a later city's candidates' counts. <see cref="Ruleset.Capture"/>'s
    /// <c>RebellionNeighbourScoreFloor</c> starts the running best, and only a strictly greater score ever
    /// replaces it, so scanning neighbours in ascending nation-index order (the order
    /// <see cref="NeighbourGeography.NeighboursOf"/> returns for a production <see cref="GameState.Neighbours"/>,
    /// itself built off <see cref="World.Nations"/>' own stable order) gives the lowest index the tie.
    /// There is no relation test: an ally, a trade partner or a human neighbour can receive the city.
    /// <para>
    /// Review round 1, N6: the original tests bit <c>owner</c> of <em>each candidate <c>n</c>'s own</em>
    /// neighbour mask (<c>nation[n] + 0x46</c>) — the transpose of what
    /// <see cref="NeighbourGeography.NeighboursOf"/> reads here (every <c>n</c> in the <em>owner's own</em>
    /// entry). The two agree because the mask is symmetric
    /// (<c>dat-neighbour-mask.md</c> §2) and every merge that changes it adds bits in pairs
    /// (<c>dat-neighbour-mask.md</c> §4; <see cref="ConquestCascade"/>'s own neighbour-merge step) —
    /// <c>[derived]</c>.
    /// </para>
    /// </summary>
    private static string? FindBestNeighbour(GameState state, World world, Ruleset ruleset, CityState city)
    {
        var cityPoint = new GridPoint(city.X, city.Y);
        var best = ruleset.Capture.RebellionNeighbourScoreFloor;
        string? pick = null;

        foreach (var candidateId in NeighbourGeography.NeighboursOf(state, world, city.Owner))
        {
            var candidate = state.NationById(candidateId);
            if (candidate is null || candidate.Unity <= 0 || candidate.CapitalCityId is not { } capitalCityId)
            {
                continue;
            }

            var capitalCity = state.CityById(capitalCityId);
            if (capitalCity is null)
            {
                continue;
            }

            var distance = LandingTile.ChebyshevDistance(cityPoint, new GridPoint(capitalCity.X, capitalCity.Y));
            var score = state.CountCitiesOwnedBy(candidate.Id)
                        - (ruleset.Capture.RebellionNeighbourScoreDistanceWeight * distance);

            if (score > best)
            {
                best = score;
                pick = candidate.Id;
            }
        }

        return pick;
    }
}
