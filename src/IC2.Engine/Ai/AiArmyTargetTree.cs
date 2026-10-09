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
            if (distance <= 0)
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
    /// <see cref="MarchToTargetCity"/> and <see cref="MarchToTargetArmy"/> both encode a movement
    /// toward the chosen target. The army phase then issues the matching <see cref="Movement.Commands.MoveArmyCommand"/>
    /// (or, when adjacent, a siege/attack command instead). <see cref="GarrisonFallback"/> is the
    /// post-execution fallback for an army whose chosen command was accepted but did not move it.
    /// </para>
    /// </remarks>
    public enum Kind
    {
        /// <summary>Besiege an adjacent enemy city the tree picked.</summary>
        AttackCity,

        /// <summary>Attack an adjacent enemy army the tree picked.</summary>
        AttackArmy,

        /// <summary>Run to a foreign non-war city to hire mercenaries (<c>FUN_0044e84c</c>).</summary>
        MercenaryRunToCity,

        /// <summary>Move toward the picked resupply/defence city.</summary>
        MoveToResupplyCity,

        /// <summary>No qualifying target; the post-execution garrison fallback picks the destination.</summary>
        NoTarget,
    }

    /// <summary>One army's tree decision.</summary>
    /// <param name="Decision">What the army does this turn.</param>
    /// <param name="TargetCity">
    /// The city to attack / hire at / march at, when one applies. <see langword="null"/> otherwise.
    /// </param>
    /// <param name="TargetArmy">
    /// The army to attack or chase, when one applies. <see langword="null"/> otherwise.
    /// </param>
    /// <param name="Resupply">
    /// The resupply/defence city the tree picked, when the decision reads from it
    /// (<see cref="Kind.MoveToResupplyCity"/> or the <c>move to the resupply/defence city</c> branch).
    /// </param>
    public sealed record Decision(
        Kind Selected,
        CityState? TargetCity,
        ArmyState? TargetArmy,
        ResupplyCity? Resupply);

    /// <summary>
    /// One recorded tree decision, written when <see cref="WithDecisionLog"/> wraps the call. The log
    /// is the Done-when 6 instrumentation: "whenever the tree selected <em>attack the city</em> or
    /// <em>attack the army</em> for an army, an attack or a march toward that target was issued and
    /// accepted that turn." The shape lets a test count the decisions, the targets, and the kind
    /// independently.
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

    private static readonly List<DecisionRecord> _log = new();

    /// <summary>
    /// Clears and returns a fresh <see cref="DecisionLog"/> sink. Tests use this to instrument a run
    /// (Done-when 6: the AI's tree decision log over 16 rounds); production callers leave it alone and
    /// the log stays empty.
    /// </summary>
    public static List<DecisionRecord> NewDecisionLog()
    {
        var sink = new List<DecisionRecord>();
        ActiveLog = sink;
        return sink;
    }

    /// <summary>The current decision log, or <see langword="null"/> when no test has called <see cref="NewDecisionLog"/>.</summary>
    public static List<DecisionRecord>? ActiveLog { get; private set; }

    /// <summary>
    /// The decision tree, verbatim from <c>2026-10-07-strategic-ai-turn.md</c> §3.3, with
    /// <c>2026-10-09-ai-intercept-and-fleet-hunt-in-play.md</c>'s corrections applied at the fleet
    /// boundary (which this file does not touch). Every comparison is exactly as the brief writes it.
    /// </summary>
    public static Decision Decide(
        AiView view,
        ArmyState army,
        bool atWar)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(army);

        var rules = view.Ruleset.Ai;

        var city = ScoreCityTarget(view, army, atWar);
        var armyScore = ScoreArmyTarget(view, army, atWar);
        var resupply = ScoreResupplyCity(view, army, atWar);

        var moves = army.Moves;
        var supplies = army.SupplyTons;
        var morale = army.Morale;

        var armyScoreValue = armyScore.Score;
        var cityScoreValue = city.Score;
        var cityDist = city.Distance;
        var armyDist = armyScore.Distance;

        var demoralisedBranch =
            supplies < rules.DemoralisedSuppliesThreshold
            && morale < rules.DemoralisedMoraleThreshold
            && armyDist > rules.DemoralisedArmyDistanceThreshold;
        var cityIsBetterBuy =
            cityScoreValue > rules.CityScoreThreshold
            && cityDist < moves
            && armyDist > 2 * moves;

        // Outer "city is the better buy or no army target worth taking" branch.
        if (armyScoreValue < rules.ArmyScoreThreshold
            || demoralisedBranch
            || cityIsBetterBuy)
        {
            var isCityOutOfReachForFarAttack =
                cityScoreValue < rules.CityScoreThreshold
                || (supplies < rules.DemoralisedSuppliesThreshold && cityDist > rules.MercenaryRunCityDistanceFar);

            if (isCityOutOfReachForFarAttack)
            {
                var wellSupplied = army.TotalTroops / rules.MercenaryRunTroopsDivisor < supplies;
                var foreignCandidateExists = city.City is not null;

                if (wellSupplied && foreignCandidateExists)
                {
                    // The mercenary run: the brief sends the army to FUN_0044e84c, then either defends
                    // the resupply city (armyScore < 71 and cityScore > 85) or still chases the army
                    // target (armyScore ≥ 71). The clone has no FUN_0044e84c step — the mercenary
                    // hire is automatic via FUN_0044E41C (AiMercenaryHirePass) — so the chosen
                    // destination is the picked foreign non-war city, the army simply moves there, and
                    // AiMercenaryHirePass handles the hire on arrival.
                    return new Decision(Kind.MercenaryRunToCity, city.City, armyScore.Army, resupply);
                }

                return new Decision(Kind.MoveToResupplyCity, null, null, resupply);
            }

            // Otherwise: attack the city.
            return new Decision(Kind.AttackCity, city.City, null, resupply);
        }

        // Outer branch fell through: attack the army target.
        return new Decision(Kind.AttackArmy, null, armyScore.Army, resupply);
    }

    /// <summary>
    /// Records <paramref name="decision"/> against the active <see cref="ActiveLog"/>, if any.
    /// Production callers leave <see cref="ActiveLog"/> <see langword="null"/> and this is a no-op;
    /// tests inject a log via <see cref="NewDecisionLog"/> to drive the Done-when 6 assertions.
    /// </summary>
    private static void RecordDecision(AiView view, ArmyState army, AiArmyTargetTree.Decision decision, long armyScoreValue, long cityScoreValue, int armyDist, int cityDist)
    {
        if (ActiveLog is null)
        {
            return;
        }

        ActiveLog.Add(new DecisionRecord(
            army.Id,
            decision.Selected,
            decision.TargetCity?.Id,
            decision.TargetArmy?.Id,
            cityScoreValue,
            armyScoreValue,
            cityDist,
            armyDist));
    }

    /// <summary>
    /// Wraps <see cref="Decide"/> so every decision lands in <see cref="ActiveLog"/> when one is
    /// installed. Tests use the wrapper; production callers use <see cref="Decide"/> directly to
    /// avoid touching the log they did not install.
    /// </summary>
    public static AiArmyTargetTree.Decision DecideAndLog(
        AiView view,
        ArmyState army,
        bool atWar)
    {
        var rules = view.Ruleset.Ai;
        var city = ScoreCityTarget(view, army, atWar);
        var armyScore = ScoreArmyTarget(view, army, atWar);
        var resupply = ScoreResupplyCity(view, army, atWar);
        var decision = DecideFromScorers(view, army, atWar, city, armyScore, resupply);
        RecordDecision(view, army, decision, armyScore.Score, city.Score, armyScore.Distance, city.Distance);
        return decision;
    }

    /// <summary>The shared decision-tree body, factored out so <see cref="Decide"/> and <see cref="DecideAndLog"/> agree.</summary>
    private static AiArmyTargetTree.Decision DecideFromScorers(
        AiView view,
        ArmyState army,
        bool atWar,
        CityTarget city,
        ArmyTarget armyScore,
        ResupplyCity resupply)
    {
        var rules = view.Ruleset.Ai;

        var moves = army.Moves;
        var supplies = army.SupplyTons;
        var morale = army.Morale;

        var armyScoreValue = armyScore.Score;
        var cityScoreValue = city.Score;
        var cityDist = city.Distance;
        var armyDist = armyScore.Distance;

        var demoralisedBranch =
            supplies < rules.DemoralisedSuppliesThreshold
            && morale < rules.DemoralisedMoraleThreshold
            && armyDist > rules.DemoralisedArmyDistanceThreshold;
        var cityIsBetterBuy =
            cityScoreValue > rules.CityScoreThreshold
            && cityDist < moves
            && armyDist > 2 * moves;

        if (armyScoreValue < rules.ArmyScoreThreshold
            || demoralisedBranch
            || cityIsBetterBuy)
        {
            var isCityOutOfReachForFarAttack =
                cityScoreValue < rules.CityScoreThreshold
                || (supplies < rules.DemoralisedSuppliesThreshold && cityDist > rules.MercenaryRunCityDistanceFar);

            if (isCityOutOfReachForFarAttack)
            {
                var wellSupplied = army.TotalTroops / rules.MercenaryRunTroopsDivisor < supplies;
                var foreignCandidateExists = city.City is not null;

                if (wellSupplied && foreignCandidateExists)
                {
                    return new AiArmyTargetTree.Decision(Kind.MercenaryRunToCity, city.City, armyScore.Army, resupply);
                }

                return new AiArmyTargetTree.Decision(Kind.MoveToResupplyCity, null, null, resupply);
            }

            return new AiArmyTargetTree.Decision(Kind.AttackCity, city.City, null, resupply);
        }

        return new AiArmyTargetTree.Decision(Kind.AttackArmy, null, armyScore.Army, resupply);
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

        var distCap = AiView.Distance(army.X, army.Y, capital.X, capital.Y);
        if (distCap <= view.Ruleset.Ai.GarrisonFallbackCapitalDistance)
        {
            // Near the capital: go to the nearest city of any owner.
            CityState? nearest = null;
            var nearestDist = int.MaxValue;
            foreach (var city in state.Cities)
            {
                var d = AiView.Distance(army.X, army.Y, city.X, city.Y);
                if (d > 0 && d < nearestDist)
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