using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// Quarterly army upkeep, both formulas — <c>docs/task-catalogue.md</c> "T08 Economy, supply,
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

    /// <summary>
    /// Splits every army a nation owns into the regulars' upkeep the treasury is actually charged and its
    /// mercenaries' nominal pay, so the balance-sheet projection (<c>docs/tasks/T104.md</c>) can list the
    /// two sides as its own two lines.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The regulars' side is <see cref="MercenaryDesertion.BillArmy"/>'s
    /// <see cref="MercenaryDesertion.Result.RegularUpkeepCharged"/>, summed over the nation's armies — the
    /// exact function <see cref="QuarterlyEconomySystem"/> charges the treasury with. It is <em>not</em> a
    /// second per-slot sum: when an unfunded mercenary deserts, BillArmy's swap-remove moves the last slot
    /// into the hole and the loop advances past it, so a regular that lands there is not billed that quarter,
    /// and this method must skip it too or the projection would disagree with the treasury change.
    /// </para>
    /// <para>
    /// The mercenaries' side is the nominal per-slot <see cref="ComputeUnit"/> sum over the same armies
    /// before any desertion. It is shown for information only: mercenary pay is charged to each army's own
    /// purse, never the treasury, so it is not part of the projection's expenditure total.
    /// </para>
    /// </remarks>
    /// <param name="state">Supplies the armies.</param>
    /// <param name="nationId">The owning nation to sum for.</param>
    /// <param name="ruleset">
    /// Supplies <see cref="RecruitmentRules.TroopsPerCostUnit"/>,
    /// <see cref="RecruitmentRules.MercenaryUpkeepQualityDivisor"/> and each unit type's
    /// <see cref="UnitTypeRules.QuarterlyPrice"/> — never a C# literal.
    /// </param>
    /// <returns>The nation's total regulars' upkeep and total mercenaries' pay for one quarter.</returns>
    /// <exception cref="ArgumentException">A unit's <see cref="UnitSlot.UnitTypeId"/> is not in <paramref name="ruleset"/>.</exception>
    public static (int Regulars, int Mercenaries) ComputeForNation(GameState state, string nationId, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(nationId);
        ArgumentNullException.ThrowIfNull(ruleset);

        var regulars = 0;
        var mercenaries = 0;
        foreach (var army in state.Armies)
        {
            if (!string.Equals(army.Nation, nationId, StringComparison.Ordinal))
            {
                continue;
            }

            // The same call the quarterly billing makes, so the two can never disagree about which
            // regulars an unfunded mercenary's swap-remove skips.
            regulars += MercenaryDesertion.BillArmy(army, ruleset).RegularUpkeepCharged;

            foreach (var unit in army.Units)
            {
                if (unit.IsMercenary)
                {
                    mercenaries += ComputeUnit(unit, ruleset);
                }
            }
        }

        return (regulars, mercenaries);
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
