using System.Globalization;
using IC2.Engine.Armies;
using IC2.Engine.Battle.Commands;
using IC2.Engine.Cities.Orders;
using IC2.Engine.Model;
using IC2.Engine.Recruitment;
using IC2.Engine.Recruitment.Commands;

namespace IC2.Engine.Ai;

/// <summary>
/// <c>docs/game-design.md</c> §AI phase 1: "<em>recruit/build up to an affordability threshold scaled by
/// <c>expansionDrive</c>, prioritizing whichever unit types the effectiveness matrix suggests counter a
/// currently-visible threat.</em>"
/// </summary>
/// <remarks>
/// <para>
/// <strong>One declared deviation from that sentence, and the reason for it.</strong> The
/// <em>effectiveness matrix</em> it names is <c>combat.detailedResolver.typeEffectiveness</c>, which
/// belongs to the <strong>reserve</strong> — the optional detailed resolver <c>docs/game-design.md</c>
/// §Combat holds back and the shipped <see cref="Battle.InstantBattleResolver"/> never reads. Two things
/// follow. First, this task is explicitly instructed not to touch the reserve, and making the AI's
/// spending depend on it would make a held-back block load-bearing in shipped play. Second, the matrix's
/// orientation is not settled: <c>toy-ruleset.json</c>'s own provenance for it says the attacker-row
/// versus defender-row assignment "is a reasoned inference, not re-traced index arithmetic", so an AI
/// keyed to it would silently invert if that inference is ever corrected.
/// </para>
/// <para>
/// So the AI ranks unit types by the number the <em>shipped</em> resolver actually fights with:
/// <see cref="UnitTypeRules.CombatPowerWeight"/>, the <c>+0x26</c> table <see cref="Strength.ArmyPower"/>
/// multiplies troops by <strong>[confirmed: unit-type-stat-table-in-dat.md,
/// decompiled-unit-map-orders-and-record-fields.md]</strong>, divided by what a battalion of it costs
/// through <see cref="StandingRecruitmentCost.InitialCost"/>. That is "the most fighting strength per
/// talent, measured the way this engine measures fighting strength" — a weaker rule than countering a
/// specific threat, stated as weaker rather than dressed up. When the detailed resolver is built and its
/// orientation settled, this is the one method that should change.
/// </para>
/// <para>
/// <strong>The threat half of the sentence is kept</strong>, through
/// <see cref="Economy.HostileArmyAdjacent.IsThreatened"/> — the original's confirmed nine-cell test. A
/// threatened city is where the AI recruits and what it fortifies; it is the "currently-visible threat"
/// the design asks the phase to answer, expressed in the only threat predicate the engine confirms.
/// </para>
/// <para>
/// <strong>Every candidate here is gated on its handler's own refusals</strong>, read off
/// <see cref="RecruitStandingUnitCommandHandler"/> and <see cref="OrderCityCommandHandler"/>: the city is
/// ours, the unit type and the order id exist in the ruleset, the troop count and the point count are
/// positive, the order is not already pending, the city is not already at the order's maximum, it is not
/// under siege, and the treasury covers the cost. Nothing is proposed that any of those would refuse.
/// </para>
/// <para>
/// <strong>T57 adds the other half of phase 1's own sentence.</strong> "<em>Recruit/build</em>" names two
/// actions, and only the first had anyone issuing it: <c>docs/task-catalogue.md</c> T57's Scope line
/// records that before this task, <c>grep -rn "Mobiliz" src/IC2.Engine/Ai/</c> returned nothing, so a
/// ready recruit — one that has finished training and reached its permanent quality
/// (<see cref="MobilizationReadiness.QualityFor"/>) — sat in the nation's recruitment table forever.
/// <see cref="ProposeMobilization"/> is gated the same way: read off
/// <see cref="Recruitment.Commands.MobilizeRecruitSlotCommandHandler"/>, never proposed unless the
/// engine will accept it.
/// </para>
/// </remarks>
public static class AiEconomyPhase
{
    /// <summary>Adds every economy candidate this state offers to <paramref name="into"/>.</summary>
    /// <param name="view">The shared reads.</param>
    /// <param name="personality">The acting nation's personality.</param>
    /// <param name="into">The collecting list.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public static void Propose(AiView view, AiPersonalityProfile personality, List<AiCandidate> into)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(personality);
        ArgumentNullException.ThrowIfNull(into);

        // Mobilization is proposed before the budget gate below, and is unaffected by it: collecting an
        // already-paid-for, already-fully-ready recruit spends no further treasury, so a nation in debt
        // still does it -- it needs the army it already bought more than ever, and T39's debt-deposal
        // rule watches the treasury, which this never touches.
        ProposeMobilization(view, into);

        var budget = TurnBudget(view.Nation.Treasury, personality.ExpansionDrivePermille);
        if (budget <= 0)
        {
            // A nation in debt or with nothing to commit places no new SPENDING orders at all. T39
            // deposes an AI nation whose treasury is past the debt line; spending it further under would
            // be the AI working against its own survival.
            return;
        }

        foreach (var city in view.OwnCities())
        {
            var threatened = view.IsThreatened(city);
            ProposeRecruitment(view, personality, city, threatened, budget, into);
            ProposeFortification(view, personality, city, threatened, budget, into);
        }
    }

    /// <summary>
    /// One candidate per ready standing-recruitment slot the acting nation owns — T57, the AI side of
    /// T55's <see cref="MobilizeRecruitSlotCommand"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Readiness is the seat's own threshold, not re-derived.</strong>
    /// <see cref="MobilizationReadiness.IsReady"/> already reads <see cref="NationState.Control"/> and
    /// the ruleset's <see cref="RulesetFlags.SeatAsymmetry"/> flag, exactly as
    /// <see cref="MobilizeRecruitSlotCommandHandler"/> does, so an AI seat under
    /// <see cref="SeatAsymmetryModel.Faithful"/> waits for state <c>24</c>
    /// (<c>[confirmed: decompiled-mobilization-and-mercenary-restock.md §4]</c> — the AI's own
    /// <c>FUN_004504f4</c> gates on <c>state == 0x18</c>, stricter than the player's <c>&gt; 15</c>) and
    /// never earlier, per T57's Done-when 2.
    /// </para>
    /// <para>
    /// <strong>A city under siege is skipped.</strong> A pending slot adds to its training city's
    /// defender strength for as long as it stays pending
    /// (<see cref="Cities.Capture.CompleteDefenderStrength.GarrisonTerm"/>); mobilizing it removes that
    /// addend and replaces it with a field army standing beside the city instead — a trade, not a pure
    /// gain, at exactly the moment the city's defence is being tested. See
    /// <see cref="AiWeights.MobilizeReadyRecruitBaseScore"/>'s remarks for the full reasoning. Nothing in
    /// <c>MobilizeRecruitSlotCommandHandler</c> itself checks this — the original's own
    /// <c>FUN_0044a4e0</c> has no siege test either — so this is this candidate generator's own
    /// tactical restraint, not a legality gate the command would otherwise enforce.
    /// </para>
    /// <para>
    /// <strong>Feasibility is checked with the same functions the handler calls</strong>, never
    /// re-derived: <see cref="MobilizationReceivingArmy.Find"/> for whether an existing army will take
    /// the unit, and <see cref="MobilizationArmyCreation.PlacementCell"/> plus
    /// <see cref="ArmyManagementRules.MaxArmies"/> for whether a new one could be created when none
    /// will. A slot that would hit <c>MobilizeRecruitSlotRejections.NoReceivingArmy</c> is skipped
    /// rather than proposed — T22's Done-when 1 requires zero rejected commands, and T57's Done-when 3
    /// names the same requirement for this command.
    /// </para>
    /// </remarks>
    private static void ProposeMobilization(AiView view, List<AiCandidate> into)
    {
        var nation = view.Nation;
        var recruitment = view.Ruleset.Recruitment;
        var asymmetry = view.Ruleset.Flags.SeatAsymmetry;

        for (var slotIndex = 0; slotIndex < nation.RecruitmentSlots.Count; slotIndex++)
        {
            var slot = nation.RecruitmentSlots[slotIndex];
            if (!MobilizationReadiness.IsReady(slot.StateCode, nation.Control, recruitment, asymmetry))
            {
                continue;
            }

            // Cities are never removed from GameState.Cities (only their owner changes), so this is
            // defensive rather than a case the toy world or the soak can reach -- but a candidate
            // generator must never assume what it can check for free.
            var city = view.State.CityById(slot.TargetCityId);
            if (city is null)
            {
                continue;
            }

            if (city.UnderSiege)
            {
                continue;
            }

            if (!TryChooseReceivingArmy(view, city, nation, slot.Troops, out var newArmyId))
            {
                continue;
            }

            into.Add(AiCandidate.Single(
                AiPhase.Economy,
                "mobilize",
                new MobilizeRecruitSlotCommand(view.NationId, slotIndex, newArmyId),
                AiWeights.MobilizeReadyRecruitBaseScore,
                Inv(
                    "mobilize recruitment slot {0} ({1} {2} troops, state {3}) at {4}",
                    slotIndex, slot.Troops, slot.UnitTypeId, slot.StateCode, city.Id)));
        }
    }

    /// <summary>
    /// Whether mobilizing a <paramref name="incomingTroops"/>-strong recruit at <paramref name="city"/>
    /// would find or be able to create a receiving army, mirroring
    /// <see cref="MobilizeRecruitSlotCommandHandler"/>'s own two-step search exactly so the candidate and
    /// the command can never disagree.
    /// </summary>
    /// <param name="newArmyId">
    /// The id to pass as <see cref="MobilizeRecruitSlotCommand.NewArmyId"/> when a new army would be
    /// created; empty when an existing army will take the unit and the id goes unused.
    /// </param>
    private static bool TryChooseReceivingArmy(
        AiView view, CityState city, NationState nation, int incomingTroops, out string newArmyId)
    {
        newArmyId = string.Empty;

        if (MobilizationReceivingArmy.Find(view.State, city, nation, view.Ruleset, incomingTroops) is not null)
        {
            return true;
        }

        // No existing army will take it -- MobilizeRecruitSlotCommandHandler creates one next exactly as
        // MobilizationArmyCreation.Create does, so its own two failure conditions (no qualifying cell,
        // or the army table already at ArmyManagementRules.MaxArmies) are checked here without calling
        // Create itself, which would build (and discard) an ArmyState for no reason.
        if (view.State.Armies.Count >= view.Ruleset.ArmyManagement.MaxArmies)
        {
            return false;
        }

        if (MobilizationArmyCreation.PlacementCell(view.State, view.World, city) is null)
        {
            return false;
        }

        newArmyId = NextArmyId(view.State);
        return true;
    }

    /// <summary>
    /// The lowest-numbered <c>"army-{n}"</c> id not already in use, so
    /// <see cref="MobilizeRecruitSlotRejections.DuplicateArmyId"/> can never fire. Consumes no
    /// randomness and no clock: two candidates built from the same unchanged state may propose the same
    /// id, which is harmless because <see cref="AiTurn"/> dispatches at most one of them before
    /// regenerating the candidate list from the state the dispatch produced.
    /// </summary>
    private static string NextArmyId(GameState state)
    {
        var index = 0;
        string candidate;
        do
        {
            candidate = Inv("army-{0}", index);
            index++;
        }
        while (state.ArmyById(candidate) is not null);

        return candidate;
    }

    /// <summary>
    /// The affordability threshold: <c>treasury × (floor + expansion·span) / 1000</c>, so an AI with
    /// <c>expansionDrive = 0</c> commits a tenth of its treasury in a turn and one with
    /// <c>expansionDrive = 1</c> commits three fifths. Negative treasuries give a negative budget, which
    /// the caller reads as "spend nothing".
    /// </summary>
    public static long TurnBudget(int treasury, int expansionDrivePermille)
    {
        var sharePermille = AiWeights.TreasuryCommitFloorPermille
                            + (AiWeights.TreasuryCommitExpansionPermille * expansionDrivePermille
                               / AiWeights.PermilleScale);
        return (long)treasury * sharePermille / AiWeights.PermilleScale;
    }

    private static void ProposeRecruitment(
        AiView view,
        AiPersonalityProfile personality,
        CityState city,
        bool threatened,
        long budget,
        List<AiCandidate> into)
    {
        if (OpenOrdersAt(view.Nation, city.Id) >= AiWeights.MaxOpenRecruitmentOrdersPerCity)
        {
            return;
        }

        // T57 Done-when 3. RecruitStandingUnitCommandHandler refuses with recruitment.table-full once
        // the nation's 40-slot table (Ruleset.Recruitment.MaxSlots) is full, the same way
        // MaxOpenRecruitmentOrdersPerCity is pre-checked above rather than discovered. Before this task
        // the AI never mobilized, so nothing ever drained the table and a long enough soak would have
        // filled it and then failed T22's Done-when 1 (zero rejected commands) outright.
        // ProposeMobilization (above, in Propose) is what now drains it.
        if (view.Nation.RecruitmentSlots.Count >= view.Ruleset.Recruitment.MaxSlots)
        {
            return;
        }

        var best = BestAffordableUnitType(view.Ruleset, budget, view.Nation.Treasury);
        if (best is not { } choice)
        {
            return;
        }

        // Ambition scales with expansionDrive; necessity does not. Recruiting because a hostile army is
        // standing next to the city is not an expansionist act, and an unambitious nation still defends
        // itself -- so the threat bonus is added AFTER the multiplication, not before it.
        //
        // Review round 1, F1: it used to be inside, which made the multiplication zero the whole score
        // at expansionDrive = 0 and produce no recruitment candidate at all, threatened or not. The code
        // was the defect and the comment above it was right, on two grounds.
        //
        // The first, and the one that carries it: docs/game-design.md §AI names expansionDrive as what
        // scales the "affordability threshold", not as a veto on the phase, and defending a threatened
        // city is not an expansionist act.
        //
        // The second is narrower than an earlier revision of this comment claimed. It said the old shape
        // "guaranteed it could never spend a talent" of the budget AiWeights.TreasuryCommitFloorPermille
        // grants an expansionDrive = 0 nation -- which is false, because ProposeFortification's own
        // multiplier is x2 at that end and never zero, so the budget was always spendable on walls. The
        // accurate statement is about this method only: it computes an affordable battalion against that
        // budget and then discarded the result unconditionally, so the recruitment path alone was
        // unreachable at one end of a parameter that is supposed to scale it rather than switch it off.
        // Pinned at both ends of the range by AiEconomyPhaseTests.
        var score = (AiWeights.RecruitBaseScore * personality.ExpansionDrivePermille / AiWeights.PermilleScale)
                    + (threatened ? AiWeights.ThreatenedCityBonus : 0);
        if (score < AiWeights.MinimumActionScore)
        {
            return;
        }

        into.Add(AiCandidate.Single(
            AiPhase.Economy,
            "recruit",
            new RecruitStandingUnitCommand(view.NationId, city.Id, choice.UnitTypeId, choice.Troops),
            score,
            Inv(
                "recruit {0} ({1} troops) at {2} for {3} talents: power/talent {4}, budget {5}, "
                + "city threatened {6}",
                choice.UnitTypeId, choice.Troops, city.Id, choice.Cost, choice.PowerPerTalent, budget,
                threatened)));
    }

    private static void ProposeFortification(
        AiView view,
        AiPersonalityProfile personality,
        CityState city,
        bool threatened,
        long budget,
        List<AiCandidate> into)
    {
        if (BattleCommandRuleset.FortificationOrderIdIn(view.Ruleset) is not { } orderId)
        {
            return;
        }

        CityOrderRule? rule = null;
        foreach (var candidate in view.Ruleset.CityOrders.Orders)
        {
            if (string.Equals(candidate.Id, orderId, StringComparison.Ordinal))
            {
                rule = candidate;
                break;
            }
        }

        if (rule is null)
        {
            return;
        }

        // OrderCityCommandHandler's gates, in its own order.
        if (FortificationCode.FinishedPercent(city.FortificationCode, rule) >= rule.MaxPercent)
        {
            return;
        }

        if (FortificationCode.IsOrderInProgress(city.FortificationCode, rule))
        {
            return;
        }

        if (rule.RefusedWhileUnderSiege && city.UnderSiege)
        {
            return;
        }

        var maxPoints = Math.Min(
            FortificationCode.MaxOrderablePoints(city.FortificationCode, rule),
            AiWeights.MaxFortifyPointsPerOrder);
        if (maxPoints <= 0)
        {
            return;
        }

        // cost = costPerPointPerPopulationThousand × points × population, the handler's own expression.
        var costPerPoint = (long)rule.CostPerPointPerPopulationThousand * city.PopulationThousands;
        if (costPerPoint <= 0)
        {
            // A free order would let the AI fortify without limit; a zero-population city is also a
            // degenerate input the handler would happily accept. Declining is the conservative answer.
            return;
        }

        var affordablePoints = (int)Math.Min(maxPoints, budget / costPerPoint);
        if (affordablePoints <= 0)
        {
            return;
        }

        var totalCost = costPerPoint * affordablePoints;
        if (view.Nation.Treasury < totalCost)
        {
            return;
        }

        var score = AiWeights.FortifyBaseScore + (threatened ? AiWeights.ThreatenedCityBonus : 0);

        // The mirror of recruitment's scaling: fortifying is what a nation does instead of expanding, so
        // it is worth most to the least expansionist personality. At expansion 0 the multiplier is 2,
        // at expansion 1 it is 1.
        score = score
                * ((2 * AiWeights.PermilleScale) - personality.ExpansionDrivePermille)
                / AiWeights.PermilleScale;

        into.Add(AiCandidate.Single(
            AiPhase.Economy,
            "fortify",
            new OrderCityCommand(view.NationId, city.Id, orderId, affordablePoints),
            score,
            Inv(
                "{0} {1} by {2} points for {3} talents: budget {4}, city threatened {5}",
                orderId, city.Id, affordablePoints, totalCost, budget, threatened)));
    }

    /// <summary>One recruitable option, already priced.</summary>
    private readonly record struct UnitChoice(string UnitTypeId, int Troops, int Cost, long PowerPerTalent);

    /// <summary>
    /// The unit type giving the most field-battle power per talent, at its own confirmed standard
    /// battalion size, among those the budget and the treasury can both pay for. Ties go to the type
    /// declared first in the ruleset — <see cref="Ruleset.UnitTypes"/> is a <see cref="ValueList{T}"/>,
    /// so "first" is a stable, serialization-preserved order rather than whatever a hash happened to
    /// yield.
    /// </summary>
    private static UnitChoice? BestAffordableUnitType(Ruleset ruleset, long budget, int treasury)
    {
        UnitChoice? best = null;

        foreach (var type in ruleset.UnitTypes)
        {
            var troops = type.StandardBattalionSize;
            if (troops <= 0)
            {
                continue;
            }

            var cost = StandingRecruitmentCost.InitialCost(troops, type.Id, ruleset);
            if (cost <= 0 || cost > budget || cost > treasury)
            {
                // A zero-cost order is refused as well as unaffordable ones: the recruitment cost
                // formula truncates (troops / troopsPerCostUnit × recruitCost), so a type whose
                // battalion is smaller than one cost unit would be free, and free units would end the
                // economy phase's meaning entirely.
                continue;
            }

            var powerPerTalent = (long)type.CombatPowerWeight * troops / cost;
            if (best is null || powerPerTalent > best.Value.PowerPerTalent)
            {
                best = new UnitChoice(type.Id, troops, cost, powerPerTalent);
            }
        }

        return best;
    }

    private static int OpenOrdersAt(NationState nation, string cityId)
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

    /// <summary>See <c>AiMilitaryPhase.Inv</c>: the per-seed log has to be locale-independent.</summary>
    private static string Inv(string format, params object?[] arguments) =>
        string.Format(CultureInfo.InvariantCulture, format, arguments);
}
