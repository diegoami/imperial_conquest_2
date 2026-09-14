using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// The real consequence of unpaid upkeep — <c>docs/task-catalogue.md</c> "T08 Economy, supply,
/// and purses", Done-when 7: "an army whose upkeep cannot be paid loses troops (a real consequence, not a
/// debt counter)."
/// </summary>
/// <remarks>
/// <strong>[confirmed: decompiled-quarterly-billing-and-economy.md]</strong>: "army units lose troops,
/// reduced by troops/100, when available upkeep funds drop below 1"
/// (<c>tests/fixtures/corpus.json</c> <c>economy.unpaidUpkeepConsequence</c>). The report states the
/// consequence's shape and the divisor but not the exact per-army/per-nation bookkeeping of "available
/// upkeep funds", so <see cref="NationCanPayUpkeep"/> implements the plainest reading consistent with the
/// confirmed shape (the nation's treasury, after this quarter's income, net of this quarter's total
/// upkeep, must not drop below 1) rather than inventing finer detail the report does not give.
/// </remarks>
public static class UpkeepEnforcement
{
    /// <summary>
    /// Whether a nation's treasury can cover a quarter's total upkeep: the confirmed threshold is that
    /// the funds available for upkeep must not drop below 1.
    /// </summary>
    /// <param name="treasuryBeforeUpkeep">The treasury after this quarter's income, before upkeep is charged.</param>
    /// <param name="totalUpkeep">This quarter's total ship and army upkeep.</param>
    public static bool NationCanPayUpkeep(int treasuryBeforeUpkeep, int totalUpkeep) =>
        treasuryBeforeUpkeep - totalUpkeep >= 1;

    /// <summary>
    /// Applies the mutiny consequence to one unit: troops reduced by <c>troops / UnpaidUpkeepTroopLossDivisor</c>,
    /// floored at 0.
    /// </summary>
    /// <param name="ruleset">Supplies <see cref="EconomyRules.UnpaidUpkeepTroopLossDivisor"/> — never a C# literal.</param>
    public static UnitSlot ApplyMutiny(UnitSlot unit, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(unit);
        ArgumentNullException.ThrowIfNull(ruleset);

        var loss = unit.Troops / ruleset.Economy.UnpaidUpkeepTroopLossDivisor;
        return unit with { Troops = Math.Max(0, unit.Troops - loss) };
    }

    /// <summary>Applies <see cref="ApplyMutiny"/> to every unit slot in an army.</summary>
    public static ArmyState ApplyMutinyToArmy(ArmyState army, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(army);
        ArgumentNullException.ThrowIfNull(ruleset);

        var units = army.Units.Select(u => ApplyMutiny(u, ruleset));
        return army with { Units = ValueList.From(units) };
    }
}
