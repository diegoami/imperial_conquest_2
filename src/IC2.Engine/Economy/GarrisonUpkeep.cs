using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// Quarterly city-unit (garrison) upkeep — <c>docs/task-catalogue.md</c> "T39 Quarterly upkeep: who
/// pays, mercenary desertion, and deposition for debt", Done-when 5 (T08 follow-up
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/76">#76</see> N3, moved here from
/// T13).
/// </summary>
/// <remarks>
/// <strong>[confirmed: upkeep-payment-and-desertion.md]</strong>: <c>for each nation, each of its
/// recruitment slots with troops &gt; 0: treasury −= (troops / 200) × price[type]</c> — the same
/// regular-unit formula <see cref="ArmyUpkeep.ComputeUnit"/> already implements, over
/// <see cref="NationState.RecruitmentSlots"/> rather than an army's units. Charged against every such
/// slot, "not ready" ones included: the original reads only the troop count and unit type, never the
/// readiness <see cref="RecruitmentSlot.StateCode"/>, so a slot still climbing toward completion is
/// billed exactly like one that finished long ago.
/// </remarks>
public static class GarrisonUpkeep
{
    /// <summary>Computes one quarter's upkeep for a nation's whole recruitment-slot roster.</summary>
    /// <param name="slots">The nation's recruitment slots. An empty roster costs 0.</param>
    /// <param name="ruleset">
    /// Supplies <see cref="RecruitmentRules.TroopsPerCostUnit"/> and each unit type's
    /// <see cref="UnitTypeRules.QuarterlyPrice"/> — never a C# literal.
    /// </param>
    /// <exception cref="ArgumentException">A slot's <see cref="RecruitmentSlot.UnitTypeId"/> is not in <paramref name="ruleset"/>.</exception>
    public static int Compute(IEnumerable<RecruitmentSlot> slots, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(slots);
        ArgumentNullException.ThrowIfNull(ruleset);

        var recruitment = ruleset.Recruitment;
        var total = 0;
        foreach (var slot in slots)
        {
            if (slot.Troops <= 0)
            {
                continue;
            }

            var type = ruleset.UnitTypeById(slot.UnitTypeId)
                ?? throw new ArgumentException(
                    $"Unit type '{slot.UnitTypeId}' is not defined in ruleset '{ruleset.Id}'.", nameof(slots));

            total += (slot.Troops / recruitment.TroopsPerCostUnit) * type.QuarterlyPrice;
        }

        return total;
    }
}
