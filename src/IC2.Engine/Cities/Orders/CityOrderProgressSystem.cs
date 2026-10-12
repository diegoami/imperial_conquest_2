using IC2.Engine.Core;
using IC2.Engine.Economy;
using IC2.Engine.Model;

namespace IC2.Engine.Cities.Orders;

/// <summary>
/// Weekly city order progress tick. Advances pending fortification orders and handles completion.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The [open] discrepancy in per-round progress.</strong>
/// [`city-population-growth.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/city-population-growth.md)
/// transcribes the original's weekly city loop with this pseudocode:
/// <code>
/// if fort > 100:
///     if not threatened: fort += min(10, fort / 100); fort -= min(1000, (fort / 100) × 100)
///     else:              fort = fort % 100
/// </code>
/// Read literally, a city at exactly 100 with fewer than 10 points pending ends at 0, not 100.
/// Worked example: 95 with 5 ordered → 595 → 600 → 0. That is either a bug in the original or
/// a misreading. This task implements <strong>completion at 100</strong> rather than the
/// read-literal zero, with <c>_provenance</c> naming both the transcription and this task's reading
/// explicitly. T67 (#304): the discrepancy is settled, and it is the original that does this --
/// <c>2026-09-29-fortification-orders-cost-rate-and-the-100-bug.md</c> §2-§4 (Ariminum 595 → 0 in one
/// headless turn under Wine). The shipped completion at 100 is therefore a known departure, bug #548;
/// changing it is that bug's task, not this one's.
/// </para>
/// <para>
/// T67 (#304): there is no stored siege flag. The tick tests <see cref="HostileArmyAdjacent"/>
/// (<c>FUN_004497cc</c>) and, when it holds, drops the pending points (<c>word %= 100</c>, no refund) --
/// report §2, seen in one headless turn (Pisae 669 → 69).
/// </para>
/// <para>
/// Runs in <see cref="TurnPhase.CityTick"/> as a round-scope system that updates every city in the game.
/// </para>
/// </remarks>
[GameSystem(TurnPhase.CityTick, "city-orders.progress", Order = 200)]
public sealed class CityOrderProgressSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var ruleset = context.Ruleset;
        var state = context.State;

        // Find the fortify order (currently the only shipped order type)
        var fortifyOrder = ruleset.CityOrders.Orders.FirstOrDefault(o => string.Equals(o.Id, "fortify", StringComparison.Ordinal));
        if (fortifyOrder is null)
        {
            // No fortify order in ruleset, nothing to do
            return state;
        }

        var updatedCities = new List<CityState>(state.Cities.Count);

        foreach (var city in state.Cities)
        {
            if (!FortificationCode.IsOrderInProgress(city.FortificationCode, fortifyOrder))
            {
                updatedCities.Add(city);
                continue;
            }

            // Order is in progress
            var pendingPoints = FortificationCode.PendingPoints(city.FortificationCode, fortifyOrder);
            var finishedPercent = FortificationCode.FinishedPercent(city.FortificationCode, fortifyOrder);

            // Advance the order
            // This implements the interpretation where completion at 100 is the result,
            // not the [open] read-literal zero at (100 + 0-9 remaining points).
            // See the class remarks for the discrepancy.

            int pointsThisWeek;
            if (HostileArmyAdjacent.IsThreatened(city, state, ruleset))
            {
                // A hostile army next to the town drops the pending order via fort % 100
                updatedCities.Add(city with
                {
                    FortificationCode = FortificationCode.AfterSiegeAttempt(city.FortificationCode, fortifyOrder)
                });
                continue;
            }

            // No hostile army adjacent: progress the order, up to FortifyWeeklyPointCap points a week. The pseudocode's own
            // "min(10, fort/100)" reads the RAW stored word divided by the radix -- exactly what
            // FortificationCode.PendingPoints already decoded into pendingPoints above. Dividing
            // pendingPoints by 100 a second time (as this line briefly did, unnoticed for want of a
            // test) always yields 0 for any realistic order and freezes every pending order forever;
            // see this class's remarks for the [open] discrepancy this resolves.
            pointsThisWeek = Math.Min(ruleset.CityOrders.FortifyWeeklyPointCap, pendingPoints);
            var newPendingPoints = pendingPoints - pointsThisWeek;

            int newFortificationCode;
            if (finishedPercent + pointsThisWeek >= fortifyOrder.MaxPercent)
            {
                // Order completes: set to max percent (100)
                // [provenance] implements completion at max, not the [open] discrepancy
                // where a city at 95+5 ending at 0 instead of 100 would imply
                newFortificationCode = fortifyOrder.MaxPercent;
            }
            else
            {
                // Order continues: the points advanced this week move from pending into the finished
                // percentage.
                newFortificationCode = (finishedPercent + pointsThisWeek) + (newPendingPoints * fortifyOrder.InProgressEncodingRadix);
            }

            updatedCities.Add(city with { FortificationCode = newFortificationCode });
        }

        return state with { Cities = ValueList.From(updatedCities) };
    }
}
