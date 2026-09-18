using IC2.Engine.Model;

namespace IC2.Engine.Victory;

/// <summary>
/// The four shipped victory conditions (<c>docs/task-catalogue.md</c> "T12 Victory conditions",
/// <c>docs/game-design.md</c> §"Victory conditions"), each evaluated as a pure function of a
/// <see cref="GameState"/> plus the loaded <see cref="Ruleset"/> — no capture logic, command dispatch
/// or turn pipeline required, so every condition here is directly testable against a
/// <see cref="GameState"/> built by hand.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The default condition is never hardcoded.</strong> <see cref="Evaluate"/> picks which
/// condition to check from <paramref name="scenario"/>'s own <see cref="VictoryCondition.Type"/> when a
/// scenario is given, and otherwise from <see cref="VictoryRules.DefaultCondition"/> — the ruleset field
/// <c>docs/design-audit.md</c> Q5 answers ("classical-faithful" ships <c>totalConquest</c>,
/// "improved" ships a friendlier default), never a C# literal naming one condition as "the" default.
/// </para>
/// <para>
/// <strong>Out of scope, deliberately.</strong> The original's start-of-turn check
/// (<c>FUN_00452034</c>) also deposes a human nation that is in debt and hands its seat to the AI
/// (<c>upkeep-payment-and-desertion.md</c>) — that is T39's correction, not this evaluator's. Nothing
/// here reads a nation's debt state or eliminates a seat for any reason other than the victory
/// conditions themselves, so a second, independent start-of-turn check can be added later without
/// touching this file.
/// </para>
/// </remarks>
public static class VictoryEvaluator
{
    /// <summary>
    /// Evaluates whichever victory condition applies: <paramref name="scenario"/>'s own choice when one
    /// is given, otherwise <paramref name="ruleset"/>'s <see cref="VictoryRules.DefaultCondition"/>.
    /// </summary>
    /// <param name="state">The state to evaluate.</param>
    /// <param name="ruleset">The loaded ruleset — the source of every constant this evaluator reads.</param>
    /// <param name="scenario">
    /// The scenario in play, or <see langword="null"/> to evaluate the ruleset's own default condition
    /// with no scenario-level override (no custom goal, no scenario turn limit).
    /// </param>
    public static VictoryOutcome Evaluate(GameState state, Ruleset ruleset, Scenario? scenario = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var victory = scenario?.Victory ?? new VictoryCondition(ruleset.Victory.DefaultCondition);
        var turnLimit = scenario?.TurnLimit ?? ruleset.Victory.DefaultTurnLimit;

        return victory.Type switch
        {
            VictoryConditionType.TotalConquest => EvaluateTotalConquest(state, ruleset.Victory),
            VictoryConditionType.Domination => EvaluateDomination(state, ruleset),
            VictoryConditionType.ScoreAtTurnLimit => EvaluateScoreAtTurnLimit(state, turnLimit),
            VictoryConditionType.Custom => CustomVictoryGoal.Evaluate(state, victory),
            // Unreachable under VictoryConditionType's closed four members, short of an unsafe cast --
            // named per its actual source (review round 1, non-blocking finding 4) rather than always
            // blaming `scenario`, since an invalid value can equally come from `ruleset`'s own
            // DefaultCondition when no scenario is given at all.
            _ => throw new ArgumentOutOfRangeException(
                scenario is null ? nameof(ruleset) : nameof(scenario),
                victory.Type,
                "Not a declared victory condition."),
        };
    }

    /// <summary>
    /// The original's only win: a nation owns every city on the map
    /// (<c>tests/fixtures/corpus.json</c> <c>victory.allCitiesThreshold</c>, confirmed against
    /// <c>THumanFalls_InitializeForm</c>'s <c>nation[+0x446] &lt; 334</c> test), or the hard end year is
    /// reached with nobody having done so (<c>victory.yearLimitBC</c>, 250 BC).
    /// </summary>
    /// <param name="state">The state to evaluate. <em>The total is <see cref="GameState.Cities"/>'s own
    /// count</em> — never a literal 334 — so this works for any world, toy or shipped.</param>
    /// <param name="rules">The loaded <see cref="VictoryRules"/>.</param>
    /// <exception cref="NotSupportedException">
    /// <see cref="VictoryRules.TotalConquestRequiresEveryCity"/> is <see langword="false"/>. Every
    /// shipped ruleset (<c>classical-faithful</c>, <c>improved</c>, and this repo's own
    /// <c>toy-ruleset.json</c>) sets it <see langword="true"/>, and <c>toy-ruleset.json</c>'s own
    /// provenance for the field says the confirmed rule is "every city", not a fixed count — no report
    /// describes what a <see langword="false"/> setting would mean, so this is left unimplemented
    /// rather than guessed at.
    /// </exception>
    public static VictoryOutcome EvaluateTotalConquest(GameState state, VictoryRules rules)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(rules);

        if (!rules.TotalConquestRequiresEveryCity)
        {
            throw new NotSupportedException(
                "VictoryRules.TotalConquestRequiresEveryCity=false has no defined rule to evaluate. "
                + "Every shipped ruleset sets it true (\"the rule is 'every city', not a fixed count\" — "
                + "toy-ruleset.json's own provenance for this field); no research report describes a "
                + "total-conquest variant against fewer than every city, so none is invented here.");
        }

        var totalCities = state.Cities.Count;
        if (totalCities > 0)
        {
            foreach (var nation in state.Nations)
            {
                if (nation.Eliminated)
                {
                    continue;
                }

                if (CountCitiesOwnedBy(state, nation.Id) == totalCities)
                {
                    return VictoryOutcome.Won(VictoryConditionType.TotalConquest, nation.Id);
                }
            }
        }

        if (state.Calendar.YearBc <= rules.HardEndYearBc)
        {
            return VictoryOutcome.Expired(VictoryConditionType.TotalConquest);
        }

        return VictoryOutcome.Undecided(VictoryConditionType.TotalConquest);
    }

    /// <summary>
    /// <c>docs/game-design.md</c> §"Victory conditions" — <strong>[designed]</strong>: "control every
    /// city, or every city belonging to nations still at war with you." Read as one rule with two
    /// framings, not two independent options: a nation wins once every city belonging to a nation it is
    /// currently at war with has been captured — which reduces to literal total conquest exactly when it
    /// is at war with everyone else.
    /// </summary>
    /// <remarks>
    /// <strong>What was searched, and what came up empty (review round 1, blocking finding 2).</strong>
    /// <c>game-design.md</c> and <c>design-audit.md</c> — the only two documents that discuss this
    /// condition at all, the same two <see cref="EvaluateScoreAtTurnLimit"/> searches — state the one
    /// sentence quoted above and nothing else about it: neither says whether a nation at war with
    /// nobody should win vacuously, nor whether "control every city" is a wholly separate alternative
    /// clause rather than the special case of the war-based rule reducing to it. Both are silent because
    /// domination-over-hostiles has no original analogue to check against at all (the original's only
    /// win is total conquest — <c>design-audit.md</c> Q5); this evaluator's reading of "at least one
    /// currently-live war" as required, not merely absent, was chosen specifically because the
    /// alternative reading — vacuously true the moment nobody happens to be at war — would make this
    /// condition win at scenario start, before a single order is issued: every shipped scenario starts
    /// every relation at peace (<see cref="Model.GameStateFactory.CreateInitial"/>), so a "no
    /// requirement" reading is not merely undocumented but actively degenerate. No report or design
    /// document was found that settles this either way; this is the one reading that does not produce a
    /// trivial win.
    /// </remarks>
    public static VictoryOutcome EvaluateDomination(GameState state, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var totalCities = state.Cities.Count;
        var warCode = ruleset.Diplomacy.StateCodes.War;

        // One upfront pass (review round 1, non-blocking finding 5) rather than rescanning
        // GameState.Cities once per nation and again per hostile pair. Looked up by key only, never
        // enumerated, so this introduces no order-dependence for the determinism guard to catch --
        // the same contract IC2.Engine.Core.DomainEventCatalog's own cache documents for itself.
        var cityCountByNation = CountCitiesByNation(state);

        foreach (var nation in state.Nations)
        {
            if (nation.Eliminated)
            {
                continue;
            }

            cityCountByNation.TryGetValue(nation.Id, out var ownedByNation);

            // Literal total conquest is the special case of "every hostile city taken" where every
            // other nation happens to be a hostile.
            if (totalCities > 0 && ownedByNation == totalCities)
            {
                return VictoryOutcome.Won(VictoryConditionType.Domination, nation.Id);
            }

            var hasLiveHostile = false;
            var everyHostileDominated = true;
            foreach (var other in state.Nations)
            {
                if (string.Equals(other.Id, nation.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                // Review round 1, non-blocking finding 3: an eliminated nation is never a "live"
                // hostile, matching the outer loop's own elimination check. Currently unreachable under
                // GameStateFactory's invariant (Eliminated <=> owns 0 cities, which the city-count check
                // below already treats as dominated) but no longer merely an accident of that
                // invariant holding -- a state built to violate it directly (this evaluator's whole
                // point, per the task's Scope) now gets the same answer either way.
                if (other.Eliminated)
                {
                    continue;
                }

                if (state.Relations.Get(nation.Id, other.Id) != warCode)
                {
                    continue;
                }

                hasLiveHostile = true;
                cityCountByNation.TryGetValue(other.Id, out var ownedByOther);
                if (ownedByOther > 0)
                {
                    everyHostileDominated = false;
                    break;
                }
            }

            if (hasLiveHostile && everyHostileDominated)
            {
                return VictoryOutcome.Won(VictoryConditionType.Domination, nation.Id);
            }
        }

        return VictoryOutcome.Undecided(VictoryConditionType.Domination);
    }

    /// <summary>
    /// <c>docs/game-design.md</c> §"Victory conditions" — <strong>[designed]</strong>: "a weighted sum
    /// of cities held, treasury, and unity, highest wins", fired once <paramref name="turnLimit"/> is
    /// reached. Neither <c>game-design.md</c> nor <c>design-audit.md</c> — the only two documents that
    /// discuss this condition — names a weight for any of the three terms, so
    /// <see cref="ScoreFor"/> adds them unweighted: a literal [1, 1, 1] weighting is the one reading
    /// that introduces no invented numeric constant at all, rather than guessing at one. A tie for the
    /// highest score resolves as undecided rather than picking an arbitrary "winner", since nothing
    /// says how the original (which has no analogue for this condition) would break one.
    /// </summary>
    public static VictoryOutcome EvaluateScoreAtTurnLimit(GameState state, int? turnLimit)
    {
        ArgumentNullException.ThrowIfNull(state);

        if (turnLimit is null || state.Calendar.TurnIndex < turnLimit.Value)
        {
            return VictoryOutcome.Undecided(VictoryConditionType.ScoreAtTurnLimit);
        }

        string? bestNationId = null;
        var bestScore = 0;
        var tied = false;

        foreach (var nation in state.Nations)
        {
            if (nation.Eliminated)
            {
                continue;
            }

            var score = ScoreFor(state, nation);
            if (bestNationId is null || score > bestScore)
            {
                bestNationId = nation.Id;
                bestScore = score;
                tied = false;
            }
            else if (score == bestScore)
            {
                tied = true;
            }
        }

        if (bestNationId is null || tied)
        {
            return VictoryOutcome.Undecided(VictoryConditionType.ScoreAtTurnLimit);
        }

        return VictoryOutcome.Won(VictoryConditionType.ScoreAtTurnLimit, bestNationId);
    }

    private static int ScoreFor(GameState state, NationState nation) =>
        CountCitiesOwnedBy(state, nation.Id) + nation.Treasury + nation.Unity;

    private static int CountCitiesOwnedBy(GameState state, string nationId)
    {
        var count = 0;
        foreach (var city in state.Cities)
        {
            if (string.Equals(city.Owner, nationId, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Every nation's owned-city count, in one pass over <see cref="GameState.Cities"/>. Keyed lookup
    /// only (<see cref="Dictionary{TKey,TValue}.TryGetValue"/>) — never enumerated — so this carries no
    /// order-dependence for the determinism guard to catch, the same contract
    /// <c>IC2.Engine.Core.DomainEventCatalog</c>'s own cache documents for itself.
    /// </summary>
    private static Dictionary<string, int> CountCitiesByNation(GameState state)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var city in state.Cities)
        {
            counts.TryGetValue(city.Owner, out var count);
            counts[city.Owner] = count + 1;
        }

        return counts;
    }
}
