using IC2.Engine.Core;
using IC2.Engine.Economy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using IC2.Engine.Recruitment.Commands;
using static IC2.Engine.Ai.AiFormat;

namespace IC2.Engine.Ai;

/// <summary>
/// T157 (#926): the AI's economy phase as the original plays it, selected by
/// <see cref="RulesetFlags.AiEconomy"/> = <see cref="AiEconomyModel.Original"/> (<c>classical-faithful</c>).
/// </summary>
/// <remarks>
/// <para>
/// <strong>Source:</strong> <c>2026-10-07-strategic-ai-turn.md</c> §2.1-2.3 <strong>[confirmed:
/// decompile]</strong> (<c>FUN_0044ffbc</c>, <c>FUN_004504f4</c>, <c>FUN_004502e0</c>,
/// <c>FUN_004501f4</c>), with the battalion cap from issue 515's research comment. Three parts: the
/// threat budget (<see cref="Threat"/>, <see cref="Own"/>), the recruitment it drives
/// (<see cref="PlaceOrders"/>) and the week-11 tax policy (<see cref="NextTaxRate"/>). The designed
/// economy's fortification is not run: <b>the original's AI never orders a fortification</b>.
/// </para>
/// <para>
/// <strong>It runs once per seat turn, before the army phase</strong> (<c>FUN_0044fa20</c>: diplomacy,
/// economy, armies, fleets), called by <see cref="AiTurn"/>, not as scored candidates: the "at most 8
/// orders a turn" cap and the once-a-turn tax policy are per-turn facts a candidate list that is
/// regenerated after every action cannot hold.
/// </para>
/// <para>
/// <strong>The draw stream.</strong> Every order draws twice, on its own stream
/// (<c>ai.faithful-economy:{nation}:{turnIndex}</c>, derived from the seat's generator without advancing
/// it): once for the type (<c>Random(100)</c>), once for the amount (<c>Random(standardSize x 4/5)</c>).
/// A turn costs <c>2 x orders</c> draws, 0 to 16, and none from the seat's own stream, so the army phase's
/// draws are not moved by the economy. The tax policy and the city choice draw nothing.
/// </para>
/// <para>
/// <strong>Readings the report leaves open.</strong> "Foreign armies" is every army not the acting
/// nation's (allies included). "Each sum rounded down" is read as the floor of the sum, not the sum of
/// floors. <c>value(a)</c> sums the army's unit slots, each <c>(troops / 100) x combatPowerWeight</c>. The
/// deficit only gates the phase (<c>own &lt; threat</c>); the order count is bounded by the 8 and 3 caps,
/// the report naming no other stop. The original also gates new orders on mobilisation below 100
/// (<c>decompiled-mobilization-and-mercenary-restock.md</c> §5), kept here.
/// </para>
/// </remarks>
public static class AiFaithfulEconomy
{
    /// <summary>What the economy phase did.</summary>
    /// <param name="State">The state after the phase.</param>
    /// <param name="Issued">Commands dispatched.</param>
    /// <param name="Rejected">Commands refused (must be zero).</param>
    public readonly record struct Result(GameState State, int Issued, int Rejected);

    /// <summary>Runs the phase for <paramref name="nationId"/>: recruitment, then the tax policy.</summary>
    public static Result Run(
        GameState state,
        Ruleset ruleset,
        ICommandDispatch commands,
        IEventSink events,
        IRng rng,
        string nationId,
        List<string> log)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(log);

        var issued = 0;
        var rejected = 0;
        var threat = Threat(state, ruleset, nationId);
        var own = Own(state, ruleset, nationId);
        log.Add(Inv("faithful economy: threat {0}, own {1}", threat, own));

        if (own < threat)
        {
            var stream = rng.ForStream(Inv("ai.faithful-economy:{0}:{1}", nationId, state.Calendar.TurnIndex));
            (state, var placedIssued, var placedRejected) = PlaceOrders(
                state, ruleset, commands, events, stream, nationId, log);
            issued += placedIssued;
            rejected += placedRejected;
        }

        var nation = state.NationById(nationId);
        if (nation is not null && state.Calendar.Week == ruleset.Ai.FaithfulTaxWeek)
        {
            var next = NextTaxRate(nation, ruleset, own, threat);
            if (next != nation.TaxRatePercent)
            {
                var result = commands.Dispatch(state, new SetTaxRateCommand(nationId, next), events);
                issued++;
                if (result.IsRejected)
                {
                    rejected++;
                    log.Add(Inv("REJECTED set tax {0}: {1}", next, result.Rejection!.Message));
                }
                else
                {
                    log.Add(Inv("week {0}: tax {1} -> {2}", state.Calendar.Week, nation.TaxRatePercent, next));
                    state = result.State;
                }
            }
        }

        return new Result(state, issued, rejected);
    }

    /// <summary>The AI's per-type value of an army: <c>sum of (troops / 100) x combatPowerWeight</c> per unit slot.</summary>
    public static long Value(ArmyState army, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(army);
        ArgumentNullException.ThrowIfNull(ruleset);

        long total = 0;
        foreach (var unit in army.Units)
        {
            var type = ruleset.UnitTypeById(unit.UnitTypeId);
            if (type is null)
            {
                continue;
            }

            total += (long)(unit.Troops / ruleset.Ai.FaithfulValueTroopsDivisor) * type.CombatPowerWeight;
        }

        return total;
    }

    /// <summary>§2.1's threat: wars, enemy army values, and the two proximity sums around the capital.</summary>
    public static long Threat(GameState state, Ruleset ruleset, string nationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var weights = ruleset.Ai;
        long wars = 0;
        foreach (var other in state.Nations)
        {
            if (IsLiving(other, nationId) && Relation(state, nationId, other.Id) == ruleset.Diplomacy.StateCodes.War)
            {
                wars++;
            }
        }

        long enemyValue = 0;
        long foreignNear = 0;
        long warNear = 0;
        var capital = CapitalOf(state, nationId);
        foreach (var army in state.Armies)
        {
            if (string.Equals(army.Nation, nationId, StringComparison.Ordinal))
            {
                continue;
            }

            var value = Value(army, ruleset);
            var atWar = Relation(state, nationId, army.Nation) == ruleset.Diplomacy.StateCodes.War;
            if (atWar)
            {
                enemyValue += value;
            }

            if (capital is { } home)
            {
                var distance = AiView.Distance(army.X, army.Y, home.X, home.Y);
                if (distance <= weights.FaithfulForeignNearDistance)
                {
                    foreignNear += value;
                }

                if (atWar && distance <= weights.FaithfulWarNearDistance)
                {
                    warNear += value;
                }
            }
        }

        return ((long)weights.FaithfulThreatPerEnemyNation * wars)
               + enemyValue
               + (foreignNear / weights.FaithfulForeignNearDivisor)
               + (warNear / weights.FaithfulWarNearDivisor);
    }

    /// <summary>§2.1's own strength: allies, own army values, and half of the allied armies' values.</summary>
    public static long Own(GameState state, Ruleset ruleset, string nationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var weights = ruleset.Ai;
        long allies = 0;
        foreach (var other in state.Nations)
        {
            if (IsLiving(other, nationId) && Relation(state, nationId, other.Id) == ruleset.Diplomacy.StateCodes.Alliance)
            {
                allies++;
            }
        }

        long mine = 0;
        long allied = 0;
        foreach (var army in state.Armies)
        {
            if (string.Equals(army.Nation, nationId, StringComparison.Ordinal))
            {
                mine += Value(army, ruleset);
            }
            else if (Relation(state, nationId, army.Nation) == ruleset.Diplomacy.StateCodes.Alliance)
            {
                allied += Value(army, ruleset);
            }
        }

        return ((long)weights.FaithfulOwnPerAlly * allies) + mine + (allied / weights.FaithfulAlliedArmyDivisor);
    }

    /// <summary>
    /// §2.2: places new orders while the caps allow, drawing type then amount for each, then choosing the
    /// city. Stops at the first condition that would make the engine refuse an order.
    /// </summary>
    public static (GameState State, int Issued, int Rejected) PlaceOrders(
        GameState state,
        Ruleset ruleset,
        ICommandDispatch commands,
        IEventSink events,
        IRng rng,
        string nationId,
        List<string> log)
    {
        var weights = ruleset.Ai;
        var issued = 0;
        var rejected = 0;
        for (var placed = 0; placed < weights.FaithfulMaxOrdersPerTurn; placed++)
        {
            var nation = state.NationById(nationId);
            if (nation is null)
            {
                break;
            }

            if (!MayPlaceAnother(nation, placed, ruleset)
                || nation.RecruitmentSlots.Count >= ruleset.Recruitment.MaxSlots
                || nation.MobilizedPercent >= ruleset.Recruitment.MobilizationCapPercent)
            {
                break;
            }

            var typeId = RollType(rng, ruleset);
            var type = ruleset.UnitTypeById(typeId);
            if (type is null)
            {
                break;
            }

            var troops = RollTroops(rng, type, ruleset);
            var city = ChooseCity(state, ruleset, nation);
            if (city is null || !string.Equals(city.Owner, nationId, StringComparison.Ordinal)
                || !RecruitmentEligibility.MayTakeOrder(city, nation, ruleset))
            {
                break;
            }

            var result = commands.Dispatch(
                state, new RecruitStandingUnitCommand(nationId, city.Id, typeId, troops), events);
            issued++;
            if (result.IsRejected)
            {
                rejected++;
                log.Add(Inv("REJECTED recruit {0} {1} at {2}: {3}", typeId, troops, city.Id, result.Rejection!.Message));
                break;
            }

            state = result.State;
            log.Add(Inv("faithful order {0}: {1} {2} troops at {3}", placed + 1, typeId, troops, city.Id));
        }

        return (state, issued, rejected);
    }

    /// <summary>
    /// Whether a further order may be placed after <paramref name="placedThisTurn"/> already were: always
    /// while the treasury is above the floor <c>-wealth / divisor</c>, and only below
    /// <see cref="AiWeightsRules.FaithfulMaxOrdersBelowFloor"/> orders once it is at or below it.
    /// </summary>
    public static bool MayPlaceAnother(NationState nation, int placedThisTurn, Ruleset ruleset)
    {
        var weights = ruleset.Ai;
        if (placedThisTurn >= weights.FaithfulMaxOrdersPerTurn)
        {
            return false;
        }

        var floor = -(nation.Wealth / weights.FaithfulDeficitFloorWealthDivisor);
        return nation.Treasury > floor || placedThisTurn < weights.FaithfulMaxOrdersBelowFloor;
    }

    /// <summary>One draw in <c>[0, FaithfulTypeDrawBound)</c> mapped through the ruleset's buckets.</summary>
    public static string RollType(IRng rng, Ruleset ruleset)
    {
        var roll = rng.NextInt(ruleset.Ai.FaithfulTypeDrawBound);
        foreach (var bucket in ruleset.Ai.FaithfulRecruitTypeBuckets)
        {
            if (roll < bucket.UpperBoundExclusive)
            {
                return bucket.UnitTypeId;
            }
        }

        return ruleset.Ai.FaithfulRecruitTypeBuckets[^1].UnitTypeId;
    }

    /// <summary>
    /// <c>standardSize/3 + Random(standardSize x 4/5)</c>, rounded down to a multiple of 100, capped at the
    /// type's standard battalion size. One draw.
    /// </summary>
    public static int RollTroops(IRng rng, UnitTypeRules type, Ruleset ruleset)
    {
        var weights = ruleset.Ai;
        var size = type.StandardBattalionSize;
        var span = size * weights.FaithfulAmountSpanNumerator / weights.FaithfulAmountSpanDenominator;
        var raw = (size / weights.FaithfulAmountMinDivisor) + rng.NextInt(span);
        var rounded = raw / weights.FaithfulAmountRounding * weights.FaithfulAmountRounding;
        return Math.Min(rounded, size);
    }

    /// <summary>
    /// §2.2's <c>FUN_004502e0</c>: the capital, unless the nation is at war, an own army stands within 15
    /// of the capital and more than a third of all queued orders sit at the capital; then the own city a
    /// recruitment order may go to, with fewer queued orders than the capital, nearest an enemy capital.
    /// </summary>
    public static CityState? ChooseCity(GameState state, Ruleset ruleset, NationState nation)
    {
        var weights = ruleset.Ai;
        var capital = nation.CapitalCityId is { } id ? state.CityById(id) : null;
        if (capital is null)
        {
            return null;
        }

        var atWar = false;
        var enemyCapitals = new List<CityState>();
        foreach (var other in state.Nations)
        {
            if (IsLiving(other, nation.Id)
                && Relation(state, nation.Id, other.Id) == ruleset.Diplomacy.StateCodes.War)
            {
                atWar = true;
                if (other.CapitalCityId is { } enemyId && state.CityById(enemyId) is { } enemyCapital)
                {
                    enemyCapitals.Add(enemyCapital);
                }
            }
        }

        if (!atWar)
        {
            return capital;
        }

        var armyNearCapital = false;
        foreach (var army in state.Armies)
        {
            if (string.Equals(army.Nation, nation.Id, StringComparison.Ordinal)
                && AiView.Distance(army.X, army.Y, capital.X, capital.Y) <= weights.FaithfulCapitalArmyDistance)
            {
                armyNearCapital = true;
                break;
            }
        }

        var atCapital = Queued(nation, capital.Id);
        var total = nation.RecruitmentSlots.Count;
        if (!armyNearCapital || (long)atCapital * weights.FaithfulCapitalQueueShareDivisor <= total)
        {
            return capital;
        }

        CityState? best = null;
        var bestDistance = int.MaxValue;
        foreach (var city in state.Cities)
        {
            if (!string.Equals(city.Owner, nation.Id, StringComparison.Ordinal)
                || string.Equals(city.Id, capital.Id, StringComparison.Ordinal)
                || !RecruitmentEligibility.MayTakeOrder(city, nation, ruleset)
                || Queued(nation, city.Id) >= atCapital)
            {
                continue;
            }

            var distance = int.MaxValue;
            foreach (var enemy in enemyCapitals)
            {
                distance = Math.Min(distance, AiView.Distance(city.X, city.Y, enemy.X, enemy.Y));
            }

            // Equal distances: the lower city id (ordinal) wins, so the choice never depends on the order of
            // GameState.Cities. [designed]: the strategic-AI-turn report (section 2.2, FUN_004502e0) says
            // "nearest to an enemy capital" and nothing about ties, and no other report of the research
            // repository's docs/reports describes FUN_004502e0's loop, so the original's tie rule is unknown.
            if (distance < bestDistance
                || (distance == bestDistance && best is not null
                    && string.CompareOrdinal(city.Id, best.Id) < 0))
            {
                best = city;
                bestDistance = distance;
            }
        }

        return best ?? capital;
    }

    /// <summary>§2.3's three rules, in order, over the evolving rate.</summary>
    public static int NextTaxRate(NationState nation, Ruleset ruleset, long own, long threat)
    {
        ArgumentNullException.ThrowIfNull(nation);
        ArgumentNullException.ThrowIfNull(ruleset);

        var weights = ruleset.Ai;
        var tax = nation.TaxRatePercent;
        if (nation.Unity < weights.FaithfulTaxCutUnityBelow
            || nation.Treasury > nation.Wealth / weights.FaithfulTaxCutTreasuryWealthDivisor)
        {
            tax = Math.Max(weights.FaithfulTaxFloor, tax - weights.FaithfulTaxCut);
        }

        if (nation.Treasury < 0
            || (own <= threat && nation.Treasury < weights.FaithfulTaxRaiseTreasuryBelow))
        {
            tax = Math.Min(weights.FaithfulTaxCeiling, tax + weights.FaithfulTaxRaise);
        }

        if (nation.Treasury > 0 && nation.Unity < weights.FaithfulTaxZeroUnityBelow)
        {
            tax = 0;
        }

        return tax;
    }

    private static int Queued(NationState nation, string cityId)
    {
        var count = 0;
        foreach (var slot in nation.RecruitmentSlots)
        {
            if (string.Equals(slot.TargetCityId, cityId, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsLiving(NationState other, string nationId) =>
        !other.Eliminated && !string.Equals(other.Id, nationId, StringComparison.Ordinal);

    private static int? Relation(GameState state, string a, string b)
    {
        var relations = state.Relations;
        if (relations.IndexOf(a) < 0 || relations.IndexOf(b) < 0)
        {
            return null;
        }

        return relations.Get(a, b);
    }

    private static CityState? CapitalOf(GameState state, string nationId) =>
        state.NationById(nationId)?.CapitalCityId is { } id ? state.CityById(id) : null;
}
