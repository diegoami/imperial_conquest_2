using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// Quarterly army upkeep, both formulas — <c>docs/build-orchestration-plan.md</c> "T08 Economy, supply,
/// and purses", Done-when 3.
/// </summary>
/// <remarks>
/// <para>
/// Regulars: <c>cost = (troops / troopsPerCostUnit) × quarterlyPrice[type]</c>
/// <strong>[confirmed: decompiled-recruitment-cost-formula.md, decompiled-unit-map-orders-and-record-fields.md]</strong>
/// — the same price table standing recruitment's quarterly price uses. Reproduces the published 13-unit
/// Roman roster's <c>442</c> talents/quarter exactly, per-unit, over
/// <c>StrengthTestbed.Roman13UnitRoster</c> in <c>tests/IC2.Engine.Tests/Strength</c> (the same roster
/// T07 uses for <c>ArmyPower</c>, reused here rather than re-transcribed).
/// </para>
/// <para>
/// Mercenaries: <c>cost = regularCost × quality / mercenaryUpkeepQualityDivisor</c>
/// <strong>[confirmed: decompiled-unit-map-orders-and-record-fields.md]</strong> — the unit slot's
/// <c>+0</c> word (<see cref="UnitSlot.MercenaryLabel"/>, <see cref="UnitSlot.IsMercenary"/>) selects
/// which formula applies, exactly as <c>docs/design-audit.md</c> §2.8 describes. The intermediate
/// <c>regularCost</c> is truncated as an integer before being multiplied by quality, not computed as one
/// real-valued expression — the same per-step truncation discipline T07's <c>ArmyPower</c> uses, and the
/// order that reproduces the Felsina mercenary's recorded 51 talents/quarter exactly
/// (<c>tests/fixtures/corpus.json</c> <c>mercenary.felsina.quarterlyCostTalents</c>:
/// <c>(6438/200)×1×8/5 = 32×8/5 = 51</c>).
/// </para>
/// </remarks>
public static class ArmyUpkeep
{
    /// <summary>Computes one quarter's upkeep for an army's full unit roster.</summary>
    /// <param name="units">The army's unit slots. An empty roster costs 0.</param>
    /// <param name="ruleset">
    /// Supplies <see cref="RecruitmentRules.TroopsPerCostUnit"/>,
    /// <see cref="RecruitmentRules.MercenaryUpkeepQualityDivisor"/> and each unit type's
    /// <see cref="UnitTypeRules.QuarterlyPrice"/> — never a C# literal.
    /// </param>
    /// <exception cref="ArgumentException">A unit's <see cref="UnitSlot.UnitTypeId"/> is not in <paramref name="ruleset"/>.</exception>
    public static int Compute(IEnumerable<UnitSlot> units, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(ruleset);

        var recruitment = ruleset.Recruitment;
        var total = 0;
        foreach (var unit in units)
        {
            total += ComputeUnit(unit, ruleset, recruitment);
        }

        return total;
    }

    /// <summary>Computes one quarter's upkeep for a single unit slot.</summary>
    public static int ComputeUnit(UnitSlot unit, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(ruleset);
        return ComputeUnit(unit, ruleset, ruleset.Recruitment);
    }

    private static int ComputeUnit(UnitSlot unit, Ruleset ruleset, RecruitmentRules recruitment)
    {
        var type = ruleset.UnitTypeById(unit.UnitTypeId)
            ?? throw new ArgumentException(
                $"Unit type '{unit.UnitTypeId}' is not defined in ruleset '{ruleset.Id}'.", nameof(unit));

        var regularCost = (unit.Troops / recruitment.TroopsPerCostUnit) * type.QuarterlyPrice;
        return unit.IsMercenary
            ? regularCost * unit.Quality / recruitment.MercenaryUpkeepQualityDivisor
            : regularCost;
    }
}
