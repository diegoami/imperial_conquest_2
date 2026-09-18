using IC2.Engine.Model;

namespace IC2.Engine.Naval;

/// <summary>
/// Trims an over-capacity army's unit slots down to exactly a fleet's transport capacity, for the
/// AI-only embarkation trim <c>docs/task-catalogue.md</c> "T14 Naval" Done-when 3 requires under
/// <c>classical-faithful</c> (<c>design-audit.md</c> Q6, <c>FUN_0044F8FC</c>).
/// </summary>
/// <remarks>
/// <c>FUN_0044F8FC</c> itself was never decompiled — <c>decompiled-unit-map-orders-and-record-fields.md</c>'s
/// own "What this does not establish" section lists it explicitly. So while the trimmed <em>total</em>
/// (<c>ships × 500</c>, confirmed at the same report's refusal-check threshold) is not in question, how
/// the original spreads that reduction across an army's several unit slots is unknown. What was searched
/// and came up empty: the same report's Part 2 order table (gives only the ship-count caps and the
/// refusal message, no per-slot detail) and <c>mobilization-movement-and-city-capture-modes.md</c> (silent
/// on fleet embarkation of any kind). This implements the simplest defensible reading — every slot's
/// troops scaled down by the same ratio, with the rounding remainder from truncating each slot handed to
/// the first slot so the total lands on the capacity exactly rather than a few troops under it — and is
/// tagged <c>[derived]</c> throughout, not <c>[confirmed]</c>.
/// </remarks>
public static class ArmyTransportTrim
{
    /// <summary>
    /// Returns <paramref name="units"/> with troops scaled down so their total is exactly
    /// <paramref name="capacity"/>. Each slot's quality, type and name are unchanged; only
    /// <see cref="UnitSlot.Troops"/> moves. A slot that would scale to 0 troops is dropped (a slot with 0
    /// troops is not a meaningful unit to keep aboard).
    /// </summary>
    /// <param name="units">The over-capacity army's unit slots, in order.</param>
    /// <param name="totalTroopsBeforeTrim">The army's total troops before trimming. Must exceed <paramref name="capacity"/>.</param>
    /// <param name="capacity">The fleet's transport capacity (<c>ships × <see cref="NavalRules.TransportTroopsPerShip"/></c>).</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="totalTroopsBeforeTrim"/> does not exceed <paramref name="capacity"/>, or
    /// <paramref name="capacity"/> is not positive.
    /// </exception>
    public static IReadOnlyList<UnitSlot> TrimToCapacity(IReadOnlyList<UnitSlot> units, int totalTroopsBeforeTrim, int capacity)
    {
        ArgumentNullException.ThrowIfNull(units);
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
        }

        if (totalTroopsBeforeTrim <= capacity)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalTroopsBeforeTrim), totalTroopsBeforeTrim,
                $"Trimming only applies when the total ({totalTroopsBeforeTrim}) exceeds the capacity ({capacity}).");
        }

        var scaled = new int[units.Count];
        var scaledSum = 0;
        for (var i = 0; i < units.Count; i++)
        {
            scaled[i] = (units[i].Troops * capacity) / totalTroopsBeforeTrim;
            scaledSum += scaled[i];
        }

        // Truncation loses a few troops per slot; hand the shortfall to the first slot so the trimmed
        // total lands on the capacity exactly, matching "trimmed to ships x 500" verbatim rather than
        // "trimmed to at most ships x 500".
        var shortfall = capacity - scaledSum;
        if (shortfall > 0 && scaled.Length > 0)
        {
            scaled[0] += shortfall;
        }

        var trimmed = new List<UnitSlot>(units.Count);
        for (var i = 0; i < units.Count; i++)
        {
            if (scaled[i] > 0)
            {
                trimmed.Add(units[i] with { Troops = scaled[i] });
            }
        }

        return trimmed;
    }
}
