using IC2.Engine.Model;

namespace IC2.Engine.Recruitment;

/// <summary>
/// The one-time cost of hiring a mercenary pool offer — <c>docs/task-catalogue.md</c> "T13 Recruitment
/// and mercenaries", Done-when 2.
/// </summary>
/// <remarks>
/// <c>TRecruitMercs_RecruitMercUnit</c> (<c>0x00441360</c>), against the 12-byte pool record's <c>+6</c>
/// quality word <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md]</strong>:
/// <code>
/// hireCost = (troops * quarterlyPriceTable[type]) / mercenaryHireTroopDivisor * quality;
/// </code>
/// It reads the <em>quarterly</em> price table (<see cref="UnitTypeRules.QuarterlyPrice"/>) — the same
/// table <see cref="StandingRecruitmentCost.QuarterlyCost"/> and <see cref="Economy.ArmyUpkeep"/> read —
/// and is paid from the <strong>hiring army's own money purse</strong>, never the national treasury. The
/// Felsina hire (6,438 "Gallic" light infantry, quality 8 "very good") gives
/// <c>(6438 * 1) / 1000 * 8 = 6 * 8 = 48</c> talents through this shape; no report independently observed
/// a concrete hire-cost number to check that total against (<c>tests/fixtures/corpus.json</c>
/// <c>mercenary.hireCostFormula</c>'s own note: "confirmed in code, but NOT independently verified
/// against a concrete observed hire-cost number in any report" — the 51-talent Felsina figure that
/// <em>is</em> independently observed is the recurring <em>quarterly upkeep</em>,
/// <see cref="Economy.ArmyUpkeep"/>'s concern, not this one-time cost).
/// </remarks>
public static class MercenaryHireCost
{
    /// <summary>Computes the one-time hire cost of a mercenary pool offer.</summary>
    /// <param name="troops">The offer's troop count (pool record <c>+4</c>).</param>
    /// <param name="unitTypeId">Key into <see cref="Ruleset.UnitTypes"/> (pool record <c>+2</c>).</param>
    /// <param name="quality">The offer's quality tier (pool record <c>+6</c>).</param>
    /// <param name="ruleset">
    /// Supplies <see cref="RecruitmentRules.MercenaryHireTroopDivisor"/> and the unit type's
    /// <see cref="UnitTypeRules.QuarterlyPrice"/>.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="unitTypeId"/> is not defined in <paramref name="ruleset"/>.</exception>
    public static int Compute(int troops, string unitTypeId, int quality, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(unitTypeId);
        ArgumentNullException.ThrowIfNull(ruleset);

        var type = ruleset.UnitTypeById(unitTypeId)
            ?? throw new ArgumentException(
                $"Unit type '{unitTypeId}' is not defined in ruleset '{ruleset.Id}'.", nameof(unitTypeId));

        return (troops * type.QuarterlyPrice) / ruleset.Recruitment.MercenaryHireTroopDivisor * quality;
    }
}
