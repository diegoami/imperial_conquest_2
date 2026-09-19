using IC2.Engine.Model;

namespace IC2.Engine.Recruitment;

/// <summary>
/// Standing recruitment's two cost formulas — <c>docs/task-catalogue.md</c> "T13 Recruitment and
/// mercenaries", Done-when 1.
/// </summary>
/// <remarks>
/// <para>
/// <c>TArmyRecruits_PrintNumbers</c> (<c>0x00454c10</c>) computes both figures the recruit dialog shows
/// from the same shape, over two different tables
/// <strong>[confirmed: decompiled-recruitment-cost-formula.md]</strong>:
/// </para>
/// <code>
/// initialCost   = (troops / troopsPerCostUnit) * priceTable[unitType]           // DAT_00478fd2
/// quarterlyCost = (troops / troopsPerCostUnit) * quarterlyPriceTable[unitType]  // DAT_00478fd4
/// </code>
/// <para>
/// <c>priceTable</c> is <see cref="UnitTypeRules.RecruitCost"/> and <c>quarterlyPriceTable</c> is
/// <see cref="UnitTypeRules.QuarterlyPrice"/> — the same quarterly table
/// <see cref="Economy.ArmyUpkeep"/> (T08/T39) and <see cref="MercenaryHireCost"/> read for their own
/// formulas, confirmed to be one shared table by
/// <c>decompiled-recruitment-cost-formula.md</c>'s own cross-check. Both solved corpus values —
/// 1,400 light cavalry costing 105 initial / 21 quarterly — reproduce exactly through this shape
/// <strong>[confirmed: menu-and-toolbar-inventory.md, tests/fixtures/corpus.json
/// <c>recruitment.lightCavalry1400.*</c>]</strong>, and the DAT's own transcribed table
/// (<c>unit-type-stat-table-in-dat.md</c>) matches the algebraically-solved 15/3 exactly.
/// </para>
/// </remarks>
public static class StandingRecruitmentCost
{
    /// <summary>The one-time cost of placing a standing recruitment order.</summary>
    /// <param name="troops">The order's troop count.</param>
    /// <param name="unitTypeId">Key into <see cref="Ruleset.UnitTypes"/>.</param>
    /// <param name="ruleset">Supplies <see cref="RecruitmentRules.TroopsPerCostUnit"/> and the unit type's <see cref="UnitTypeRules.RecruitCost"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="unitTypeId"/> is not defined in <paramref name="ruleset"/>.</exception>
    public static int InitialCost(int troops, string unitTypeId, Ruleset ruleset)
    {
        var type = ResolveType(unitTypeId, ruleset);
        return (troops / ruleset.Recruitment.TroopsPerCostUnit) * type.RecruitCost;
    }

    /// <summary>The recurring quarterly cost of a standing-recruited (regular) unit of this size and type.</summary>
    /// <remarks>
    /// This is the same shape <see cref="Economy.ArmyUpkeep"/> already computes for a regular unit's
    /// upkeep — kept here too, under its own name, because Done-when 1 asks for the recruitment dialog's
    /// own quarterly figure specifically (<c>recruitment.costFormula.quarterly</c>), not a re-export of
    /// T08/T39's upkeep API. The two must never diverge, since they are one confirmed table read the same
    /// way; <c>StandingRecruitmentCostTests</c> asserts they agree.
    /// </remarks>
    /// <param name="troops">The unit's troop count.</param>
    /// <param name="unitTypeId">Key into <see cref="Ruleset.UnitTypes"/>.</param>
    /// <param name="ruleset">Supplies <see cref="RecruitmentRules.TroopsPerCostUnit"/> and the unit type's <see cref="UnitTypeRules.QuarterlyPrice"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="unitTypeId"/> is not defined in <paramref name="ruleset"/>.</exception>
    public static int QuarterlyCost(int troops, string unitTypeId, Ruleset ruleset)
    {
        var type = ResolveType(unitTypeId, ruleset);
        return (troops / ruleset.Recruitment.TroopsPerCostUnit) * type.QuarterlyPrice;
    }

    private static UnitTypeRules ResolveType(string unitTypeId, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(unitTypeId);
        ArgumentNullException.ThrowIfNull(ruleset);

        return ruleset.UnitTypeById(unitTypeId)
            ?? throw new ArgumentException(
                $"Unit type '{unitTypeId}' is not defined in ruleset '{ruleset.Id}'.", nameof(unitTypeId));
    }
}
