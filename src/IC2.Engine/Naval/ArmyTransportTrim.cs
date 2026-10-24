using IC2.Engine.Model;

namespace IC2.Engine.Naval;

/// <summary>
/// F8FC's faithful AI embark capacity branch: move whole unit slots ashore from
/// the first slot that makes the running troop total exceed fleet capacity.
/// The historical class name remains for existing callers; this no longer trims troops.
/// </summary>
public static class ArmyTransportTrim
{
    public sealed record WholeSlotOverflow(IReadOnlyList<UnitSlot> Aboard, IReadOnlyList<UnitSlot> Ashore);

    public static WholeSlotOverflow PartitionWholeUnits(IReadOnlyList<UnitSlot> units, int capacity)
    {
        ArgumentNullException.ThrowIfNull(units);
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        var total = units.Sum(unit => (long)unit.Troops);
        if (total <= capacity)
            throw new ArgumentOutOfRangeException(nameof(capacity),
                "Whole-slot overflow applies only above fleet capacity.");

        long running = 0;
        var firstOverflow = -1;
        for (var i = 0; i < units.Count; i++)
        {
            running += units[i].Troops;
            if (running <= capacity) continue;
            firstOverflow = i;
            break;
        }
        if (firstOverflow < 0) throw new InvalidOperationException("An over-capacity army had no overflow slot.");
        return new(units.Take(firstOverflow).ToArray(), units.Skip(firstOverflow).ToArray());
    }
}
