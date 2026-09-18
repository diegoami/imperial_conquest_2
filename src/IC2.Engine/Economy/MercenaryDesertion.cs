using IC2.Engine.Model;

namespace IC2.Engine.Economy;

/// <summary>
/// One army's quarterly upkeep loop — <c>docs/task-catalogue.md</c> "T39 Quarterly upkeep: who pays,
/// mercenary desertion, and deposition for debt", Done-when 1 and 2. Regulars are charged to the
/// treasury with no balance check; mercenaries are charged to the army's own purse, and a mercenary
/// reached with the purse at or below zero deserts as a whole unit.
/// </summary>
/// <remarks>
/// <para>
/// <strong>[confirmed: upkeep-payment-and-desertion.md]</strong>, transcribed from the report's exact
/// loop over slots <c>0 → 19</c> in order:
/// <code>
/// for k = 0 … 19:
///     s = a.slot[k]
///     if s.troops &lt;= 0: continue
///     if s.marker == 0:                                    // regular
///         treasury[a.owner] -= cost
///     else:                                                 // mercenary
///         if a.money &lt;= 0:
///             a.supplies -= s.troops / 100
///             removeUnit(a, k)                              // FUN_0044ac3c
///         else:
///             a.money -= pay
/// a.money    = max(0, a.money)
/// a.supplies = max(0, a.supplies)
/// </code>
/// </para>
/// <para>
/// <strong><c>removeUnit</c> (<c>FUN_0044ac3c</c>) is a swap-remove, not a shift.</strong> It copies the
/// army's last occupied slot into the hole and drops the (now-duplicate) last slot, and the loop does
/// not go back over the moved unit — the loop variable simply advances to <c>k + 1</c> next iteration,
/// which in this task's gap-free <see cref="ArmyState.Units"/> representation is exactly "swap the last
/// element into the current index, remove the last element, then advance the index anyway". This
/// reproduces the report's own worked Carthaginian example
/// (<c>1_cartago_271_spring_11 → summer_1</c>) exactly: the Numidian heavy-cavalry mercenary in slot 5
/// moves into slot 3 (the deserting Moor's hole) and, because the loop has already advanced past index
/// 3, is never itself checked for payment this quarter — it survives even though the purse it was moved
/// into was already negative. An army left with zero units this way is reported as deleted
/// (<see cref="Result.Army"/> is <see langword="null"/>); the caller drops it from
/// <see cref="Model.GameState.Armies"/>.
/// </para>
/// <para>
/// <strong>The overdraft is forgiven, never a fallback.</strong> The mercenary that drives the purse
/// negative is still paid in full this iteration — the <c>purse &lt;= 0</c> test runs <em>before</em>
/// that unit's own payment, not after — and only the resulting negative balance is floored to zero once
/// the whole army has been processed. There is no "treasury tops up a short purse" path here at all: a
/// rich treasury never stops a mercenary from leaving (the report's own Carthage example: 11,000 talents
/// on hand when its Moor unit deserted anyway).
/// </para>
/// </remarks>
public static class MercenaryDesertion
{
    /// <summary>The outcome of billing one army for one quarter.</summary>
    /// <param name="Army">
    /// The army after billing, with any deserted mercenary slots removed and its purse and supplies
    /// floored at zero — or <see langword="null"/> if every unit deserted and the army is deleted.
    /// </param>
    /// <param name="RegularUpkeepCharged">
    /// The total charged against the owning nation's treasury for this army's regular units this
    /// quarter. Mercenary pay never appears here — it is charged to <see cref="Army"/>'s own purse.
    /// </param>
    /// <param name="DesertedUnitCount">How many mercenary units deserted this quarter.</param>
    public sealed record Result(ArmyState? Army, int RegularUpkeepCharged, int DesertedUnitCount);

    /// <summary>Bills one army for one quarter, in slot order.</summary>
    /// <param name="army">The army to bill.</param>
    /// <param name="ruleset">
    /// Supplies <see cref="EconomyRules.MercenaryDesertionSupplyDivisor"/> and, through
    /// <see cref="ArmyUpkeep.ComputeUnit(UnitSlot, Ruleset)"/>, every other constant either formula
    /// needs — never a C# literal.
    /// </param>
    public static Result BillArmy(ArmyState army, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(army);
        ArgumentNullException.ThrowIfNull(ruleset);

        var units = new List<UnitSlot>(army.Units);
        var purse = army.Money;
        var supplies = army.SupplyTons;
        var regularCharge = 0;
        var desertedCount = 0;

        var index = 0;
        while (index < units.Count)
        {
            var unit = units[index];
            if (unit.IsRegular)
            {
                regularCharge += ArmyUpkeep.ComputeUnit(unit, ruleset);
                index++;
                continue;
            }

            if (purse <= 0)
            {
                supplies -= unit.Troops / ruleset.Economy.MercenaryDesertionSupplyDivisor;
                desertedCount++;

                var lastIndex = units.Count - 1;
                units[index] = units[lastIndex];
                units.RemoveAt(lastIndex);

                // The loop does not revisit the slot it just filled: advance regardless.
                index++;
            }
            else
            {
                purse -= ArmyUpkeep.ComputeUnit(unit, ruleset);
                index++;
            }
        }

        purse = Math.Max(0, purse);
        supplies = Math.Max(0, supplies);

        if (units.Count == 0)
        {
            return new Result(null, regularCharge, desertedCount);
        }

        var updated = army with { Units = ValueList.From(units), Money = purse, SupplyTons = supplies };
        return new Result(updated, regularCharge, desertedCount);
    }
}
