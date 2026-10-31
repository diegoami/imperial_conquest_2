using IC2.Engine.Core;
using IC2.Engine.Armies.Commands;
using IC2.Engine.Economy.Commands;
using IC2.Engine.Model;
using IC2.Engine.Naval;
using IC2.Engine.Naval.Commands;
using IC2.Engine.Recruitment;
using IC2.Engine.Recruitment.Commands;
using IC2.Engine.Strength;
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
/// Army values use FUN_0044a8cc, including strategic morale. T192's evaluated source disposition
/// preserves two independent queue accumulators: visited existing entries count toward recruitment,
/// while only successful mobilizations count toward the mobilization target. Existing entries store
/// raw power as a signed WORD; successful new queues add raw int power without morale. Both passes
/// use the initial deficit and allow one overshoot. The allied and capital-proximity terms divide each army's
/// value before summing, and their distances are strict (#1245; FUN_0044ffbc :53505-53535).
/// </para>
/// </remarks>
public static class AiFaithfulEconomy
{
    /// <summary>What the economy phase did.</summary>
    /// <param name="State">The state after the phase.</param>
    /// <param name="Issued">Commands dispatched.</param>
    /// <param name="Rejected">Commands refused (must be zero).</param>
    public readonly record struct Result(GameState State, int Issued, int Rejected);

    /// <summary>
    /// Runs the phase for <paramref name="nationId"/>: recruitment, the tax policy, then (T158) a new fleet
    /// order and the consolidation of army units, in the original's order (<c>FUN_00450768</c>, <c>FUN_00450858</c>).
    /// </summary>
    public static Result Run(
        GameState state,
        Ruleset ruleset,
        World world,
        ICommandDispatch commands,
        IEventSink events,
        IRng rng,
        string nationId,
        List<string> log)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(world);
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
            // T171: FUN_004504f4 mobilises before placing new orders. Both the deficit and the
            // queue-wide gate are snapshots, not recomputed after each successful command.
            var gate = AiEconomyPhase.FaithfulMobilizationGate(new AiView(state, ruleset, world, nationId));
            var deficit = threat - own;
            long existingQueuePower = 0;
            long mobilized = 0;
            for (var firstSlot = 0; firstSlot < state.NationById(nationId)!.RecruitmentSlots.Count; firstSlot++)
            {
                var slot = state.NationById(nationId)!.RecruitmentSlots[firstSlot];
                if (slot.Troops <= 0)
                {
                    continue;
                }

                // FUN_004504f4 :53791–53796. Account before eligibility, even when mobilization
                // is unavailable or its separate target is already met. Compaction still skips
                // the entry shifted into this index, because the indexed scan always advances.
                var type = ruleset.UnitTypeById(slot.UnitTypeId)!;
                var value = unchecked((short)((slot.Troops / ruleset.Ai.FaithfulValueTroopsDivisor) * type.CombatPowerWeight));
                existingQueuePower += value;
                if (!gate || mobilized >= deficit)
                {
                    continue;
                }

                var candidates = new List<AiCandidate>();
                AiEconomyPhase.ProposeMobilization(new AiView(state, ruleset, world, nationId), candidates, gate, firstSlot);
                if (candidates.Count == 0 || ((MobilizeRecruitSlotCommand)candidates[0].Commands[0]).SlotIndex != firstSlot)
                {
                    continue;
                }

                var candidate = candidates[0];
                var command = (MobilizeRecruitSlotCommand)candidate.Commands[0];
                var result = commands.Dispatch(state, command, events);
                issued++;
                if (result.IsRejected)
                {
                    rejected++;
                    log.Add(Inv("REJECTED mobilize slot {0}: {1}", command.SlotIndex, result.Rejection!.Message));
                }
                else
                {
                    state = result.State;
                    mobilized += value;
                    // FUN_0044a610 compacts, but FUN_004504f4 still increments its index:
                    // skip the entry shifted into the just-removed slot.
                    log.Add(candidate.Rationale);
                }
            }

            var stream = rng.ForStream(Inv("ai.faithful-economy:{0}:{1}", nationId, state.Calendar.TurnIndex));
            (state, var placedIssued, var placedRejected) = PlaceOrders(
                state, ruleset, commands, events, stream, nationId, log, deficit - existingQueuePower);
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

        var fleet = OrderFleet(state, ruleset, world, commands, events, nationId, log);
        state = fleet.State;
        issued += fleet.Issued;
        rejected += fleet.Rejected;

        var consolidated = ConsolidateUnits(state, ruleset, commands, events, nationId, log);
        state = consolidated.State;
        issued += consolidated.Issued;
        rejected += consolidated.Rejected;

        // T159 (#959): the original's last two economy-phase sub-phases, FUN_004509f0 then FUN_00450b30:
        // merge two armies, then merge two fleets, each at most once per seat per round.
        var armyMerge = MergeArmies(state, ruleset, commands, events, nationId, log);
        state = armyMerge.State;
        issued += armyMerge.Issued;
        rejected += armyMerge.Rejected;

        var fleetMerge = MergeFleets(state, ruleset, commands, events, nationId, log);
        state = fleetMerge.State;
        issued += fleetMerge.Issued;
        rejected += fleetMerge.Rejected;

        return new Result(state, issued, rejected);
    }

    /// <summary>FUN_0044a8cc army power, including per-slot truncation and strategic morale.</summary>
    public static long Value(ArmyState army, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(army);
        ArgumentNullException.ThrowIfNull(ruleset);

        // Shared call at :53501 feeds own, allied, enemy and proximity terms. Queue values
        // deliberately do not use this helper: :53791 and :53813 have distinct raw formulas.
        return ArmyPower.Compute(army.Units, army.Morale, ruleset);
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
                // #1245: strict distances, and each army's share truncated on its own.
                if (distance < weights.FaithfulForeignNearDistance)
                {
                    foreignNear += value / weights.FaithfulForeignNearDivisor;
                }

                if (atWar && distance < weights.FaithfulWarNearDistance)
                {
                    warNear += value / weights.FaithfulWarNearDivisor;
                }
            }
        }

        return ((long)weights.FaithfulThreatPerEnemyNation * wars)
               + enemyValue
               + foreignNear
               + warNear;
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
                // #1245: each allied army's half is truncated on its own.
                allied += Value(army, ruleset) / weights.FaithfulAlliedArmyDivisor;
            }
        }

        return ((long)weights.FaithfulOwnPerAlly * allies) + mine + allied;
    }

    /// <summary>
    /// §2.2: chooses the order city once, then places orders while the caps allow, drawing type then
    /// amount for each. Reads the live city/nation before each command; an invalid city stops the pass.
    /// </summary>
    public static (GameState State, int Issued, int Rejected) PlaceOrders(
        GameState state,
        Ruleset ruleset,
        ICommandDispatch commands,
        IEventSink events,
        IRng rng,
        string nationId,
        List<string> log,
        long remainingTarget)
    {
        var weights = ruleset.Ai;
        var issued = 0;
        var rejected = 0;
        long queuedPower = 0;
        // FUN_004504F4 calls 4502E0 at 450664, outside the 450672–45075A order loop.
        // Cache identity only: queue changes must not select another town, and command validation
        // must still see the latest owner, fortification and nation state.
        var cityId = state.NationById(nationId) is { } initialNation
            ? ChooseCity(state, ruleset, initialNation)?.Id : null;
        for (var placed = 0; placed < weights.FaithfulMaxOrdersPerTurn && queuedPower < remainingTarget; placed++)
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
            var city = cityId is { } id ? state.CityById(id) : null;
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
            queuedPower += (long)(troops / weights.FaithfulValueTroopsDivisor) * type.CombatPowerWeight;
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

    /// <summary>
    /// The fleet score <c>FUN_00450768</c> gates on: <see cref="AiWeightsRules.FaithfulFleetScoreLaunched"/> for each
    /// own launched fleet and <see cref="AiWeightsRules.FaithfulFleetScoreUnderConstruction"/> for each own fleet
    /// under construction (never both) [confirmed: decompile, :53838]
    /// </summary>
    public static int FleetScore(GameState state, Ruleset ruleset, string nationId)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);

        var score = 0;
        foreach (var fleet in state.Fleets)
        {
            if (!string.Equals(fleet.Nation, nationId, StringComparison.Ordinal))
            {
                continue;
            }

            score += fleet.IsUnderConstruction
                ? ruleset.Ai.FaithfulFleetScoreUnderConstruction
                : ruleset.Ai.FaithfulFleetScoreLaunched;
        }

        return score;
    }

    /// <summary>
    /// <c>FUN_00450768</c>: at most one fleet order a turn, when the nation owns fewer than
    /// <see cref="AiWeightsRules.FaithfulFleetCountBelow"/> fleets, its treasury is above
    /// <see cref="AiWeightsRules.FaithfulFleetTreasuryAbove"/> and its fleet score is 0, or 1 with more than
    /// 50 cities, or 2 with more than 100. Ships <c>min(cap, cities + offset)</c>, at the port
    /// <see cref="ChoosePort"/> names, through <see cref="OrderFleetCommand"/>. Draws nothing.
    /// </summary>
    public static (GameState State, int Issued, int Rejected) OrderFleet(
        GameState state,
        Ruleset ruleset,
        World world,
        ICommandDispatch commands,
        IEventSink events,
        string nationId,
        List<string> log)
    {
        var weights = ruleset.Ai;
        var nation = state.NationById(nationId);
        if (nation is null || nation.Eliminated)
        {
            return (state, 0, 0);
        }

        // The < 99 gate counts the whole fleet table, every nation's fleets (DAT_004a0326), and with them, under
        // unitTableCompaction = swapRemoveSweep, the dead records not yet swept (T244, #1606).
        var fleets = IC2.Engine.Armies.ArmyTable.FleetTableCount(state);

        var cities = 0;
        foreach (var city in state.Cities)
        {
            if (string.Equals(city.Owner, nationId, StringComparison.Ordinal))
            {
                cities++;
            }
        }

        var score = FleetScore(state, ruleset, nationId);
        var scoreAllows = score == 0
                          || (score == 1 && cities > weights.FaithfulFleetScoreOneCitiesAbove)
                          || (score == 2 && cities > weights.FaithfulFleetScoreTwoCitiesAbove);
        if (fleets >= weights.FaithfulFleetCountBelow
            || nation.Treasury <= weights.FaithfulFleetTreasuryAbove
            || !scoreAllows)
        {
            return (state, 0, 0);
        }

        var ships = Math.Min(weights.FaithfulFleetShipsCap, cities + weights.FaithfulFleetShipsCityOffset);
        if (ships < ruleset.Naval.OrderMinShips || ships > ruleset.Naval.OrderMaxShips)
        {
            return (state, 0, 0);
        }

        var port = ChoosePort(state, world, nation);
        if (port is null)
        {
            return (state, 0, 0);
        }

        var fleetId = Inv("{0}-ai-fleet-{1}", nationId, state.Calendar.TurnIndex);
        for (var suffix = 2; state.FleetById(fleetId) is not null; suffix++)
        {
            fleetId = Inv("{0}-ai-fleet-{1}-{2}", nationId, state.Calendar.TurnIndex, suffix);
        }

        var result = commands.Dispatch(state, new OrderFleetCommand(nationId, port.Id, ships, fleetId), events);
        if (result.IsRejected)
        {
            log.Add(Inv("REJECTED order fleet {0} ships at {1}: {2}", ships, port.Id, result.Rejection!.Message));
            return (state, 1, 1);
        }

        log.Add(Inv("faithful fleet order: {0} ships at {1} ({2})", ships, port.Id, fleetId));
        return (result.State, 1, 0);
    }

    /// <summary>
    /// <c>FUN_004496e0</c>: the own city <see cref="CoastalCity.IsCoastal"/> accepts (a sea cell in its 3x3),
    /// nearest the capital by Chebyshev distance, that hosts no construction order already. No test that the
    /// water cell is free is made: the report gives no predicate for the decompile's "free".
    /// Equal distances: the lower city id (ordinal) wins. [designed]: the report names no loop order or
    /// tie rule for FUN_004496e0 (searched 2026-10-07-strategic-ai-turn.md and
    /// 2026-10-09-fleet-build-city-nearest-capital.md, which did not play a tie). Since T241 the rule is
    /// <see cref="CoastalCity.FleetBuildCity"/>, shared with the human's Build fleet, with this method's
    /// any-nation free test (<c>busyWhenAnyNationBuilds</c>) and its refusal to choose without a capital.
    /// </summary>
    public static CityState? ChoosePort(GameState state, World world, NationState nation)
    {
        if (nation.CapitalCityId is not { } id || state.CityById(id) is null)
        {
            return null;
        }

        return CoastalCity.FleetBuildCity(state, world, nation, busyWhenAnyNationBuilds: true);
    }

    /// <summary>
    /// <c>FUN_00450858</c>: slot by slot over every own army, in the army's slot order. A regular unit below
    /// the regular fraction of its type's standard size is joined with the next same-type regular slot below
    /// the partner fraction (<see cref="JoinUnitsCommand"/>); a mercenary below the mercenary fraction is
    /// disbanded (<see cref="DisbandUnitCommand"/>) unless it is the army's last unit. [designed]: the last
    /// unit is kept because the command refuses it by design (#565) and the report names no such case. After
    /// a join the pass moves on to the next slot. Draws nothing.
    /// </summary>
    public static (GameState State, int Issued, int Rejected) ConsolidateUnits(
        GameState state,
        Ruleset ruleset,
        ICommandDispatch commands,
        IEventSink events,
        string nationId,
        List<string> log)
    {
        var weights = ruleset.Ai;
        var issued = 0;
        var rejected = 0;
        var armyIds = new List<string>();
        foreach (var army in state.Armies)
        {
            if (string.Equals(army.Nation, nationId, StringComparison.Ordinal))
            {
                armyIds.Add(army.Id);
            }
        }

        foreach (var armyId in armyIds)
        {
            var slot = 0;
            while (state.ArmyById(armyId) is { } army && slot < army.Units.Count)
            {
                var unit = army.Units[slot];
                var type = ruleset.UnitTypeById(unit.UnitTypeId);
                if (type is null)
                {
                    slot++;
                    continue;
                }

                var size = (long)type.StandardBattalionSize;
                ICommand? command = null;
                if (unit.IsRegular
                    && unit.Troops * (long)weights.FaithfulConsolidateRegularBelowDenominator
                    < size * weights.FaithfulConsolidateRegularBelowNumerator)
                {
                    for (var next = slot + 1; next < army.Units.Count; next++)
                    {
                        var other = army.Units[next];
                        if (other.IsRegular
                            && string.Equals(other.UnitTypeId, unit.UnitTypeId, StringComparison.Ordinal)
                            && other.Troops * (long)weights.FaithfulConsolidatePartnerBelowDenominator
                            < size * weights.FaithfulConsolidatePartnerBelowNumerator)
                        {
                            command = new JoinUnitsCommand(nationId, armyId, slot, next);
                            break;
                        }
                    }
                }
                else if (unit.IsMercenary
                         && army.Units.Count > 1
                         && unit.Troops * (long)weights.FaithfulConsolidateMercenaryBelowDenominator
                         < size * weights.FaithfulConsolidateMercenaryBelowNumerator)
                {
                    command = new DisbandUnitCommand(nationId, armyId, slot);
                }

                if (command is null)
                {
                    slot++;
                    continue;
                }

                var result = commands.Dispatch(state, command, events);
                issued++;
                if (result.IsRejected)
                {
                    rejected++;
                    log.Add(Inv("REJECTED consolidate {0} slot {1}: {2}", armyId, slot, result.Rejection!.Message));
                    slot++;
                    continue;
                }

                state = result.State;
                if (command is DisbandUnitCommand)
                {
                    log.Add(Inv("faithful consolidate: disbanded {0} slot {1}", armyId, slot));
                }
                else
                {
                    log.Add(Inv("faithful consolidate: joined {0} slot {1}", armyId, slot));
                    slot++;
                }
            }
        }

        return (state, issued, rejected);
    }

    /// <summary>
    /// <c>FUN_004509f0</c> (T159): two nested table-order loops. The donor is the first own army, from table
    /// index 0, that passes the donor gates and has a valid partner; the partner is the first other own army
    /// from index 0 that passes the partner gates (<see cref="AiAbsorbArmyCommandHandler"/>). The pair goes
    /// through <see cref="AiAbsorbArmyCommand"/>; the pass stops after the first merge. Draws nothing.
    /// </summary>
    public static (GameState State, int Issued, int Rejected) MergeArmies(
        GameState state,
        Ruleset ruleset,
        ICommandDispatch commands,
        IEventSink events,
        string nationId,
        List<string> log)
    {
        var weights = ruleset.Ai;
        foreach (var donor in state.Armies)
        {
            if (!string.Equals(donor.Nation, nationId, StringComparison.Ordinal)
                || !AiAbsorbArmyCommandHandler.IsValidDonor(donor, weights))
            {
                continue;
            }

            foreach (var partner in state.Armies)
            {
                if (string.Equals(partner.Id, donor.Id, StringComparison.Ordinal)
                    || !string.Equals(partner.Nation, nationId, StringComparison.Ordinal)
                    || !AiAbsorbArmyCommandHandler.IsValidPartner(donor, partner, weights))
                {
                    continue;
                }

                var result = commands.Dispatch(state, new AiAbsorbArmyCommand(nationId, partner.Id, donor.Id), events);
                if (result.IsRejected)
                {
                    log.Add(Inv("REJECTED merge armies {0} into {1}: {2}", donor.Id, partner.Id, result.Rejection!.Message));
                    return (state, 1, 1);
                }

                log.Add(Inv("faithful merge: army {0} into {1}", donor.Id, partner.Id));
                return (result.State, 1, 0);
            }
        }

        return (state, 0, 0);
    }

    /// <summary>
    /// <c>FUN_00450b30</c> (T159): the same two loops over the fleet table. The survivor is the first own fleet
    /// that passes the survivor gates and has a valid partner; the absorbed fleet is the first other own fleet
    /// that passes the partner gates (<see cref="AiAbsorbFleetCommandHandler"/>). Stops after the first merge.
    /// Draws nothing.
    /// </summary>
    public static (GameState State, int Issued, int Rejected) MergeFleets(
        GameState state,
        Ruleset ruleset,
        ICommandDispatch commands,
        IEventSink events,
        string nationId,
        List<string> log)
    {
        var weights = ruleset.Ai;
        foreach (var survivor in state.Fleets)
        {
            if (!string.Equals(survivor.Nation, nationId, StringComparison.Ordinal)
                || !AiAbsorbFleetCommandHandler.IsValidSurvivor(survivor, weights))
            {
                continue;
            }

            foreach (var partner in state.Fleets)
            {
                if (string.Equals(partner.Id, survivor.Id, StringComparison.Ordinal)
                    || !string.Equals(partner.Nation, nationId, StringComparison.Ordinal)
                    || !AiAbsorbFleetCommandHandler.IsValidPartner(survivor, partner, weights))
                {
                    continue;
                }

                var result = commands.Dispatch(state, new AiAbsorbFleetCommand(nationId, survivor.Id, partner.Id), events);
                if (result.IsRejected)
                {
                    log.Add(Inv("REJECTED merge fleets {0} into {1}: {2}", partner.Id, survivor.Id, result.Rejection!.Message));
                    return (state, 1, 1);
                }

                log.Add(Inv("faithful merge: fleet {0} into {1}", partner.Id, survivor.Id));
                return (result.State, 1, 0);
            }
        }

        return (state, 0, 0);
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
