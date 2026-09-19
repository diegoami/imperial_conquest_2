using System.Globalization;
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

        var budget = TurnBudget(view.Nation.Treasury, personality.ExpansionDrivePermille);
        if (budget <= 0)
        {
            // A nation in debt or with nothing to commit places no orders at all. T39 deposes an AI
            // nation whose treasury is past the debt line; spending it further under would be the AI
            // working against its own survival.
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
