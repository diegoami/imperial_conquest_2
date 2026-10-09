using IC2.Engine.Battle.Commands;
using IC2.Engine.Cities.Capture;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Strength;
using static IC2.Engine.Ai.AiFormat;

namespace IC2.Engine.Ai;

/// <summary>
/// The original AI's per-army target tree, ported from
/// <c>2026-10-07-strategic-ai-turn.md</c> §3.3 and §3.5 [confirmed: decompile]. For every AI army with
/// moves left, three scorers run and a verbatim decision picks one of four outcomes: attack a city,
/// attack an enemy army, run to hire mercenaries (then defend or chase), or move to a resupply city. If
/// the chosen action produced no movement, the garrison fallback (<c>FUN_0044ebe8</c>, §3.5) chooses a
/// next destination.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Pure functions, never side-effecting.</strong> Every scorer takes the relevant slice of the
/// state and returns a small bundle. <see cref="AiMilitaryPhase"/> is the only thing that turns the
/// decision into a command, and it does so through the existing <see cref="Movement.Commands.MoveArmyCommand"/>,
/// <see cref="Battle.Commands.BesiegeCityCommand"/> and <see cref="Battle.Commands.AttackArmyCommand"/>
/// commands — the "Movement is unchanged" hazard the task entry names. This file never invents a
/// mover or a command.
/// </para>
/// <para>
/// <strong>Where the thresholds live.</strong> Every literal in the scorers and the decision is read off
/// <see cref="AiWeightsRules"/>: the <c>strength×110</c> numerator, the city-scorer's region halving and
/// two doublings, the 1000-cap on the army scorer and its +1000 weaker-within bonus, the 100-threshold
/// and the resupply city's −20/+20 adjustments. The values are ruleset data so a future ruleset can
/// re-tune them, and the file is checked in alongside the only relevant numeric (none).
/// </para>
/// <para>
/// <strong>The region table is per-city, on the ruleset.</strong> <see cref="Model.GameState.Cities"/> is
/// not in T156's Owns, so a per-city region id lives in <see cref="AiWeightsRules.CityRegionById"/> as
/// a list of <see cref="AiCityRegionAssignment"/>. A shipped world ships an empty list — every city is
/// implicitly in region 0 — so the region-halving term never fires in play, exactly as T156's
/// committed data shape intends. Tests populate the list to pin the term.
/// </para>
/// </remarks>
public static class AiArmyTargetTree
{
    /// <summary>
    /// The original's <c>FUN_0044a930</c> — assault strength: <c>Σ troops ×N for archers, else ×1</c>,
    /// divided by the ruleset's <see cref="SiegeRules.PowerDivisor"/>, times <paramref name="morale"/>.
    /// <see cref="SiegeStrength.Attacker"/> reproduces the same arithmetic, reading the archer weight
    /// from <see cref="SiegeRules.ArcherStrengthMultiplier"/>; the two would diverge if the ruleset
    /// ever set the multiplier to something other than 3, which the shipped ruleset does not.
    /// </summary>
    public static int AssaultStrength(ArmyState army, Ruleset ruleset, string archerUnitTypeId) =>
        SiegeStrength.Attacker(army.Units, army.Morale, ruleset, archerUnitTypeId);

    /// <summary>
    /// The original's <c>FUN_0044a98c</c> — city defence, exactly as
    /// <see cref="CompleteDefenderStrength.Compute"/> produces it (the garrison addend included). The
    /// attacker's score = <c>strength×110 / cityDefense</c>, so the same defender strength the siege
    /// resolver reads decides the AI's score.
    /// </summary>
    public static int CityDefense(
        CityState city,
        Ruleset ruleset,
        GameState state)
    {
        ArgumentNullException.ThrowIfNull(city);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(state);

        var fortifyOrder = FortifyOrder(ruleset)
            ?? throw new ArgumentException("Ruleset declares no fortify order; cannot compute city defense.", nameof(ruleset));
        var owner = state.NationById(city.Owner)
            ?? throw new ArgumentException($"City '{city.Id}' has unresolvable owner '{city.Owner}'.", nameof(state));

        return CompleteDefenderStrength.Compute(
            city,
            fortifyOrder,
            CapitalOwnership.IsAnyNationsCapital(state, city.Id),
            !string.Equals(city.Owner, city.Allegiance, StringComparison.Ordinal),
            owner,
            ruleset);
    }

    /// <summary>One army's choice: the picked city, the score the scorer produced, and the distance.</summary>
    /// <param name="City">The chosen city, or <see langword="null"/> when nothing qualifies.</param>
    /// <param name="Score">The <c>score + distance</c> term the city scorer returns on a hit.</param>
    /// <param name="Distance">The Chebyshev distance from the army to <paramref name="City"/>.</param>
    public sealed record CityTarget(CityState? City, long Score, int Distance);

    /// <summary>One army's choice: the picked enemy army, the score the scorer produced, and the distance.</summary>
    /// <param name="Army">The chosen army, or <see langword="null"/> when nothing qualifies.</param>
    /// <param name="Score">The capped score the scorer returned on a hit.</param>
    /// <param name="Distance">The Chebyshev distance from the deciding army to <paramref name="Army"/>.</param>
    public sealed record ArmyTarget(ArmyState? Army, long Score, int Distance);

    /// <summary>One army's choice: the picked resupply/defence city, its raw score, and the distance.</summary>
    /// <param name="City">The chosen city, or <see langword="null"/> when nothing qualifies.</param>
    /// <param name="Score">The score the scorer returned on a hit; lower is worse (−20 for own capitals, +20 for foreign cities).</param>
    /// <param name="Distance">The Chebyshev distance from the army to <paramref name="City"/>.</param>
    public sealed record ResupplyCity(CityState? City, long Score, int Distance);

    /// <summary>
    /// The original's <c>FUN_0044ece4</c> — the best enemy city for this army to attack, with the
    /// <c>score + distance</c> return value the decision tree compares against
    /// <see cref="AiWeightsRules.CityScoreThreshold"/>.
    /// </summary>
    /// <remarks>
    /// Cities of nations the acting nation is at war with, reachable by land or because the nation owns
    /// a fleet (the <c>FUN_0044cab4</c> test, which the rule above names). The shipped data does not
    /// model naval reachability per tile, so a nation with no army aboard a fleet is effectively land-
    /// only here; the tree's own "march at the city" command path issues a <c>MoveArmyCommand</c> that
    /// the engine's own walker handles, exactly as the "Movement is unchanged" hazard requires.
    /// </remarks>
    public static CityTarget ScoreCityTarget(
        AiView view,
        ArmyState army,
        bool atWar)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(army);

        var rules = view.Ruleset.Ai;
        var numerator = rules.ArmyTargetStrengthNumerator;
        var halving = rules.CityScoreRegionHalvingDenominator;
        var distThreshold = rules.CityAttackDistanceThreshold;
        var capitalNum = rules.CityAttackCapitalDefenseRatioNumerator;
        var capitalDen = rules.CityAttackCapitalDefenseRatioDenominator;

        var strength = AssaultStrength(army, view.Ruleset, BattleCommandRuleset.ArcherUnitTypeIdIn(view.Ruleset)!);
        var armyRegion = RegionOf(view, army.X, army.Y);

        CityState? best = null;
        long bestScore = long.MinValue;
        var bestDistance = 0;

        foreach (var city in view.State.Cities)
        {
            if (string.Equals(city.Owner, army.Nation, StringComparison.Ordinal))
            {
                continue;
            }

            if (!atWar && !view.IsAtWar(army.Nation, city.Owner))
            {
                // The brief (§3.3) names "cities of nations at war" — a city of a nation this army's
                // nation is at peace with is not a candidate at all, even reachable.
                continue;
            }

            var distance = AiView.Distance(army.X, army.Y, city.X, city.Y);
            if (distance <= 0)
            {
                continue;
            }

            var cityDefense = CityDefense(city, view.Ruleset, view.State);
            if (cityDefense <= 0)
            {
                continue;
            }

            // The verbatim order: numerator × strength / defense − distance, then halve in another region,
            // then double (defense < strength and distance < threshold), then double (capital and
            // cityDefense × num / den < strength). All divisions truncate.
            var score = (numerator * strength) / cityDefense - distance;
            if (RegionOf(view, city.X, city.Y) != armyRegion)
            {
                score -= score / halving;
            }

            if (cityDefense < strength && distance < distThreshold)
            {
                score *= 2;
            }

            var isCapital = CapitalOwnership.IsAnyNationsCapital(view.State, city.Id);
            if (isCapital && (cityDefense * capitalNum) / capitalDen < strength)
            {
                score *= 2;
            }

            if (score > bestScore)
            {
                best = city;
                bestScore = score;
                bestDistance = distance;
            }
        }

        return new CityTarget(best, bestScore == long.MinValue ? 0 : bestScore + bestDistance, bestDistance);
    }

    /// <summary>
    /// The original's <c>FUN_0044ee60</c> — the best enemy field army for this army to attack. The
    /// score the decision tree compares against <see cref="AiWeightsRules.ArmyScoreThreshold"/> is the
    /// cap-respecting term returned here.
    /// </summary>
    public static ArmyTarget ScoreArmyTarget(
        AiView view,
        ArmyState army,
        bool atWar)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(army);

        var rules = view.Ruleset.Ai;
        var numerator = rules.ArmyTargetStrengthNumerator;
        var halving = rules.CityScoreRegionHalvingDenominator;
        var cap = rules.ArmyScoreCap;
        var weakerBonus = rules.ArmyScoreWeakerWithinBonus;
        var weakerDistThreshold = rules.ArmyScoreWeakerDistanceThreshold;

        var strength = AssaultStrength(army, view.Ruleset, BattleCommandRuleset.ArcherUnitTypeIdIn(view.Ruleset)!);
        var armyRegion = RegionOf(view, army.X, army.Y);

        ArmyState? best = null;
        long bestScore = long.MinValue;
        var bestDistance = 0;

        foreach (var target in view.State.Armies)
        {
            if (string.Equals(target.Nation, army.Nation, StringComparison.Ordinal) || target.IsEmbarked)
            {
                continue;
            }

            if (!atWar && !view.IsAtWar(army.Nation, target.Nation))
            {
                continue;
            }

            // T84 (bug #366): eliminate the eliminated. Mirrors AiMilitaryPhase.ProposeArmyAttacks.
            if (view.State.NationById(target.Nation)?.Eliminated == true)
            {
                continue;
            }

            var distance = AiView.Distance(army.X, army.Y, target.X, target.Y);
            if (distance <= 0)
            {
                continue;
            }

            var theirStrength = AssaultStrength(target, view.Ruleset, BattleCommandRuleset.ArcherUnitTypeIdIn(view.Ruleset)!);
            if (theirStrength <= 0)
            {
                continue;
            }

            var score = (numerator * strength) / theirStrength - distance;
            if (RegionOf(view, target.X, target.Y) != armyRegion)
            {
                score -= score / halving;
            }

            var clamped = Math.Min(cap, score);
            if (theirStrength < strength && distance < weakerDistThreshold)
            {
                clamped += weakerBonus;
            }

            if (clamped > bestScore)
            {
                best = target;
                bestScore = clamped;
                bestDistance = distance;
            }
        }

        return new ArmyTarget(best, bestScore == long.MinValue ? 0 : bestScore, bestDistance);
    }

    /// <summary>
    /// The original's <c>FUN_0044e670</c> — the resupply or defence city for this army, with the
    /// beyond-15 foreign fallback to the nearest own city. The return value's <see cref="ResupplyCity.City"/>
    /// is <see langword="null"/> only when no city qualifies at all; the caller treats that as "do nothing".
    /// </summary>
    public static ResupplyCity ScoreResupplyCity(
        AiView view,
        ArmyState army,
        bool atWar)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(army);

        var rules = view.Ruleset.Ai;
        var strength = army.TotalTroops;
        var troopsDivisor = rules.ResupplyStrengthTroopsDivisor;
        var supplyMargin = rules.ResupplyForeignSupplyMargin;
        var moneyDivisor = rules.ResupplyForeignMoneyDivisor;
        var capitalPenalty = rules.ResupplyCapitalPenalty;
        var foreignBonus = rules.ResupplyForeignBonus;
        var maxForeignDist = rules.ResupplyMaxForeignDistance;

        // The brief: own cities score −20 if they are capitals; foreign non-war cities score +20. We
        // collect own candidates and foreign candidates separately so the "beyond 15 → nearest own
        // city" fallback can replace the foreign pick cleanly.
        var ownCandidates = new List<(CityState City, long Score, int Distance)>();
        var foreignCandidates = new List<(CityState City, long Score, int Distance)>();
        (CityState City, int Distance)? nearestOwn = null;

        foreach (var city in view.State.Cities)
        {
            var distance = AiView.Distance(army.X, army.Y, city.X, city.Y);
            if (IsInsideCity(army, city))
            {
                continue;
            }

            var isOwn = string.Equals(city.Owner, army.Nation, StringComparison.Ordinal);
            var isForeignAtWar = !isOwn && view.IsAtWar(army.Nation, city.Owner);

            if (isOwn)
            {
                // Own cities qualify when their stock is below the army's strength. The capital's −20
                // is the only adjustment.
                if (city.SupplyTons >= strength / troopsDivisor)
                {
                    if (nearestOwn is null || distance < nearestOwn.Value.Distance)
                    {
                        nearestOwn = (city, distance);
                    }

                    continue;
                }

                long ownScore = -distance;
                if (CapitalOwnership.IsAnyNationsCapital(view.State, city.Id))
                {
                    ownScore -= capitalPenalty;
                }

                ownCandidates.Add((city, ownScore, distance));
                if (nearestOwn is null || distance < nearestOwn.Value.Distance)
                {
                    nearestOwn = (city, distance);
                }
            }
            else if (!isForeignAtWar)
            {
                // Foreign non-war cities qualify only while at war, when their stock exceeds
                // strength + supplyMargin AND the army has the money. The +20 is the only adjustment.
                if (!atWar)
                {
                    continue;
                }

                if (city.SupplyTons <= strength + supplyMargin)
                {
                    continue;
                }

                if (army.Money <= strength / moneyDivisor)
                {
                    continue;
                }

                foreignCandidates.Add((city, distance + foreignBonus, distance));
            }
        }

        // Pick the best foreign candidate by score (closer first, +20 the tiebreaker), then fall back
        // to the nearest own city when it is beyond maxForeignDist.
        (CityState City, long Score, int Distance)? bestForeign = null;
        foreach (var candidate in foreignCandidates)
        {
            if (bestForeign is null || candidate.Score > bestForeign.Value.Score
                || (candidate.Score == bestForeign.Value.Score && candidate.Distance < bestForeign.Value.Distance))
            {
                bestForeign = candidate;
            }
        }

        if (bestForeign is { } foreign)
        {
            if (foreign.Distance > maxForeignDist)
            {
                if (nearestOwn is null)
                {
                    return new ResupplyCity(null, 0, 0);
                }

                return new ResupplyCity(nearestOwn.Value.City, long.MinValue, nearestOwn.Value.Distance);
            }

            return new ResupplyCity(foreign.City, foreign.Score, foreign.Distance);
        }

        // No foreign qualified (or the war branch was off); pick the best own candidate.
        (CityState City, long Score, int Distance)? bestOwn = null;
        foreach (var candidate in ownCandidates)
        {
            if (bestOwn is null || candidate.Score > bestOwn.Value.Score
                || (candidate.Score == bestOwn.Value.Score && candidate.Distance < bestOwn.Value.Distance))
            {
                bestOwn = candidate;
            }
        }

        if (bestOwn is { } own)
        {
            return new ResupplyCity(own.City, own.Score, own.Distance);
        }

        // Nothing qualified at all; the brief's last-ditch fallback is "the nearest own city" — which
        // may have been tracked above as nearestOwn.
        if (nearestOwn is { } nearest)
        {
            return new ResupplyCity(nearest.City, long.MinValue, nearest.Distance);
        }

        return new ResupplyCity(null, 0, 0);
    }

    /// <summary>What the tree decided an army should do.</summary>
    /// <remarks>
    /// <para>
    /// The attack kinds encode a movement toward the chosen target when it is not adjacent: the army
    /// phase issues the matching <see cref="Movement.Commands.MoveArmyCommand"/>, or the siege/attack
    /// command when adjacent. The garrison fallback (<c>FUN_0044ebe8</c>) is not a tree outcome: it runs
    /// for an army whose chosen command produced no movement.
    /// </para>
    /// </remarks>
    public enum Kind
    {
        /// <summary>Attack the best enemy city (besiege it when adjacent, march at it otherwise).</summary>
        AttackCity,

        /// <summary>Attack the best enemy field army (attack it when adjacent, march at it otherwise).</summary>
        AttackArmy,

        /// <summary>The mercenary run (<c>FUN_0044e84c</c>), then the <see cref="Continuation"/>.</summary>
        MercenaryRun,

        /// <summary>Move to the resupply/defence city (<c>FUN_0044e670</c>).</summary>
        MoveToResupplyCity,
    }

    /// <summary>What follows the mercenary run (<c>FUN_0044e84c</c>) in the decision.</summary>
    public enum Continuation
    {
        /// <summary>Neither continuation's condition holds; the run is the whole action.</summary>
        None,

        /// <summary>Defend the resupply city (<c>armyScore &lt; 71 and cityScore &gt; 85</c>).</summary>
        DefendResupplyCity,

        /// <summary>Still chase the army target (<c>armyScore &gt;= 71</c>).</summary>
        ChaseArmy,
    }

    /// <summary>One army's tree decision.</summary>
    /// <param name="Selected">What the army does this turn.</param>
    /// <param name="TargetCity">The best enemy city (the scorer's pick), or <see langword="null"/>.</param>
    /// <param name="TargetArmy">The best enemy field army (the scorer's pick), or <see langword="null"/>.</param>
    /// <param name="Resupply">The resupply/defence city the scorer picked.</param>
    /// <param name="After">The continuation after the mercenary run; <see cref="Continuation.None"/> for the other kinds.</param>
    public sealed record Decision(
        Kind Selected,
        CityState? TargetCity,
        ArmyState? TargetArmy,
        ResupplyCity? Resupply,
        Continuation After = Continuation.None);

    /// <summary>
    /// One recorded tree decision, written while a test holds a log from <see cref="NewDecisionLog"/>.
    /// The log is the Done-when 6 instrumentation: whenever the tree selected <em>attack the city</em> or
    /// <em>attack the army</em> for an army, an attack or a march toward that target was issued and
    /// accepted that turn.
    /// </summary>
    public sealed record DecisionRecord(
        string ArmyId,
        Kind Kind,
        string? TargetCityId,
        string? TargetArmyId,
        long CityScore,
        long ArmyScore,
        int CityDistance,
        int ArmyDistance);

    /// <summary>
    /// Installs and returns a fresh decision log for the calling thread. A game runs on one thread, so a
    /// test that plays a game on its own thread sees exactly its own decisions; production code never
    /// installs one and the log stays <see langword="null"/>.
    /// </summary>
    public static List<DecisionRecord> NewDecisionLog()
    {
        var sink = new List<DecisionRecord>();
        _activeLog = sink;
        return sink;
    }

    /// <summary>Removes the calling thread's decision log.</summary>
    public static void ClearDecisionLog() => _activeLog = null;

    [ThreadStatic]
    private static List<DecisionRecord>? _activeLog;

    /// <summary>
    /// The decision tree, verbatim from <c>2026-10-07-strategic-ai-turn.md</c> §3.3, over the three
    /// scorers' results. Records the decision in the calling thread's log when one is installed.
    /// </summary>
    public static Decision Decide(AiView view, ArmyState army, bool atWar)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(army);

        var city = ScoreCityTarget(view, army, atWar);
        var armyScore = ScoreArmyTarget(view, army, atWar);
        var resupply = ScoreResupplyCity(view, army, atWar);
        var decision = DecideFromScores(view.Ruleset.Ai, army, city, armyScore, resupply);
        _activeLog?.Add(new DecisionRecord(
            army.Id,
            decision.Selected,
            decision.TargetCity?.Id,
            decision.TargetArmy?.Id,
            city.Score,
            armyScore.Score,
            city.Distance,
            armyScore.Distance));
        return decision;
    }

    /// <summary>
    /// The decision over already-computed scorer results. Public so a table-driven test can walk every
    /// branch and boundary without building a state for each. Every comparison is exactly as the report
    /// writes it (<c>&lt; 100</c> and <c>&gt; 100</c> differ at 100).
    /// </summary>
    public static Decision DecideFromScores(
        AiWeightsRules rules,
        ArmyState army,
        CityTarget city,
        ArmyTarget armyScore,
        ResupplyCity resupply)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(army);

        var moves = army.Moves;
        var supplies = army.SupplyTons;
        var morale = army.Morale;
        var armyScoreValue = armyScore.Score;
        var cityScoreValue = city.Score;
        var cityDist = city.Distance;
        var armyDist = armyScore.Distance;

        var demoralised =
            supplies < rules.DemoralisedSuppliesThreshold
            && morale < rules.DemoralisedMoraleThreshold
            && armyDist > rules.DemoralisedArmyDistanceThreshold;
        var cityIsBetterBuy =
            cityScoreValue > rules.CityScoreThreshold
            && cityDist < moves
            && armyDist > 2 * moves;

        if (armyScoreValue < rules.ArmyScoreThreshold || demoralised || cityIsBetterBuy)
        {
            var cityOutOfReach =
                cityScoreValue < rules.CityScoreThreshold
                || (supplies < rules.DemoralisedSuppliesThreshold && cityDist > rules.MercenaryRunCityDistanceFar);

            if (cityOutOfReach)
            {
                var wellSupplied = army.TotalTroops / rules.MercenaryRunTroopsDivisor < supplies;
                if (wellSupplied && city.City is not null)
                {
                    var after = Continuation.None;
                    if (armyScoreValue < rules.DefendResupplyArmyScoreThreshold
                        && cityScoreValue > rules.DefendResupplyCityScoreThreshold)
                    {
                        after = Continuation.DefendResupplyCity;
                    }
                    else if (armyScoreValue >= rules.DefendResupplyArmyScoreThreshold)
                    {
                        after = Continuation.ChaseArmy;
                    }

                    return new Decision(Kind.MercenaryRun, city.City, armyScore.Army, resupply, after);
                }

                return new Decision(Kind.MoveToResupplyCity, city.City, armyScore.Army, resupply);
            }

            return new Decision(Kind.AttackCity, city.City, armyScore.Army, resupply);
        }

        return new Decision(Kind.AttackArmy, city.City, armyScore.Army, resupply);
    }

    /// <summary>
    /// The mercenary run's destination (<c>FUN_0044e84c</c>, §3.4): the nearest live pool offer within
    /// <see cref="AiWeightsRules.MercenaryRunOfferRange"/> of the army whose nearest city's owner is not at
    /// war, as that offer's city. <see langword="null"/> when there is none. An offer in a city the army is
    /// already next to is not a destination: the turn-start hire pass takes it from there.
    /// </summary>
    public static CityState? MercenaryRunDestination(AiView view, ArmyState army)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(army);

        CityState? best = null;
        var bestDistance = int.MaxValue;
        foreach (var slot in view.State.MercenaryPool)
        {
            var distance = AiView.Distance(army.X, army.Y, slot.X, slot.Y);
            if (distance > view.Ruleset.Ai.MercenaryRunOfferRange || distance >= bestDistance)
            {
                continue;
            }

            CityState? slotCity = null;
            foreach (var city in view.State.Cities)
            {
                if (city.X == slot.X && city.Y == slot.Y)
                {
                    slotCity = city;
                    break;
                }
            }

            if (slotCity is null
                || IsInsideCity(army, slotCity)
                || view.IsAtWar(army.Nation, slotCity.Owner))
            {
                continue;
            }

            best = slotCity;
            bestDistance = distance;
        }

        return best;
    }

    /// <summary>
    /// The original's <c>FUN_0044ebe8</c> — if any other own army is already within
    /// <see cref="AiWeightsRules.GarrisonFallbackCapitalDistance"/> tiles of the capital, head for the
    /// nearest city of any owner; otherwise head for the capital. Returns <see langword="null"/> when
    /// no own capital exists (the brief's defence city fallback cannot run).
    /// </summary>
    public static (int X, int Y)? GarrisonFallback(
        AiView view,
        ArmyState army,
        GameState state)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(army);
        ArgumentNullException.ThrowIfNull(state);

        var capital = CapitalCity(view, state, army.Nation);
        if (capital is null)
        {
            return null;
        }

        var someoneAtCapital = false;
        foreach (var own in state.Armies)
        {
            if (string.Equals(own.Nation, army.Nation, StringComparison.Ordinal)
                && AiView.Distance(own.X, own.Y, capital.X, capital.Y) <= view.Ruleset.Ai.GarrisonFallbackCapitalDistance)
            {
                someoneAtCapital = true;
                break;
            }
        }

        if (someoneAtCapital)
        {
            // Near the capital: go to the nearest city of any owner.
            CityState? nearest = null;
            var nearestDist = int.MaxValue;
            foreach (var city in state.Cities)
            {
                var d = AiView.Distance(army.X, army.Y, city.X, city.Y);
                if (!IsInsideCity(army, city) && d < nearestDist)
                {
                    nearest = city;
                    nearestDist = d;
                }
            }

            if (nearest is not null)
            {
                return (nearest.X, nearest.Y);
            }
        }

        return (capital.X, capital.Y);
    }

    /// <summary>
    /// True when the army is "in" <paramref name="city"/> for the destination scorers. The original's
    /// armies stand on a city's tile (distance 0) and the resupply and fallback scorers skip that city;
    /// the clone's walker never lets an army onto a city tile (<c>MoveArmyCommandHandler.IsBlocked</c>),
    /// so the nearest an army can get is the adjacent tile, and a march at an adjacent city has no
    /// first step. The adjacent tile is therefore the clone's "arrived", and that city is skipped too.
    /// </summary>
    private static bool IsInsideCity(ArmyState army, CityState city) =>
        AiView.Distance(army.X, army.Y, city.X, city.Y) <= 1;

    /// <summary>Own-capital lookup that never throws when a nation's capital pointer is stale.</summary>
    private static CityState? CapitalCity(AiView view, GameState state, string nationId)
    {
        var nation = state.NationById(nationId);
        if (nation is null || nation.CapitalCityId is null)
        {
            return null;
        }

        return state.CityById(nation.CapitalCityId);
    }

    /// <summary>
    /// The region id for a coordinate, looked up in <see cref="AiWeightsRules.CityRegionById"/> by the
    /// nearest city. Empty list means every coordinate sits in region <c>0</c>, the shipped-world
    /// behaviour.
    /// </summary>
    private static int RegionOf(AiView view, int x, int y)
    {
        foreach (var assignment in view.Ruleset.Ai.CityRegionById)
        {
            foreach (var city in view.State.Cities)
            {
                if (string.Equals(city.Id, assignment.CityId, StringComparison.Ordinal)
                    && city.X == x && city.Y == y)
                {
                    return assignment.Region;
                }
            }
        }

        return 0;
    }

    /// <summary>Own-capital predicate, delegated to <see cref="CapitalOwnership.IsAnyNationsCapital"/>.</summary>
    private static CityOrderRule? FortifyOrder(Ruleset ruleset)
    {
        if (BattleCommandRuleset.FortificationOrderIdIn(ruleset) is not { } orderId)
        {
            return null;
        }

        foreach (var order in ruleset.CityOrders.Orders)
        {
            if (string.Equals(order.Id, orderId, StringComparison.Ordinal))
            {
                return order;
            }
        }

        return null;
    }
}