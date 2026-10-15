using IC2.Engine.Model;

namespace IC2.Engine.Tests.Battle.Tactical.GoldenMaster;

/// <summary>
/// The post-battle strategic fields the write-back touches, compared with the post-battle save imported through
/// T21's importer: which armies remain and each one's units, money, supplies and morale, every nation's unity, and
/// the news log (the news line the write-back adds). No tolerance.
/// </summary>
internal static class PostBattle
{
    /// <summary>A refused or missing write-back cannot bypass the strategic comparison.</summary>
    public static string? FirstDifference(GoldenReplay.Conclusion port, GameState original) =>
        port.WriteBackDifference ?? FirstDifference(port.GameWithNews, original);

    /// <summary>The first difference between <paramref name="port"/> and <paramref name="original"/>, or <see langword="null"/>.</summary>
    public static string? FirstDifference(GameState port, GameState original)
    {
        var portIds = port.Armies.Select(a => a.Id).ToList();
        var originalIds = original.Armies.Select(a => a.Id).ToList();
        if (!portIds.SequenceEqual(originalIds))
        {
            return $"armies: port [{string.Join(",", portIds)}], original [{string.Join(",", originalIds)}]";
        }

        foreach (var o in original.Armies)
        {
            var p = port.ArmyById(o.Id)!;
            if (p.Units.Count != o.Units.Count)
            {
                return $"{o.Id}: unit count port {p.Units.Count}, original {o.Units.Count}";
            }

            for (var i = 0; i < o.Units.Count; i++)
            {
                if (!p.Units[i].Equals(o.Units[i]))
                {
                    return $"{o.Id} unit {i}: port {p.Units[i]}, original {o.Units[i]}";
                }
            }

            if (p.Money != o.Money)
            {
                return $"{o.Id} money: port {p.Money}, original {o.Money}";
            }

            if (p.SupplyTons != o.SupplyTons)
            {
                return $"{o.Id} supplies: port {p.SupplyTons}, original {o.SupplyTons}";
            }

            if (p.Morale != o.Morale)
            {
                return $"{o.Id} morale: port {p.Morale}, original {o.Morale}";
            }
        }

        var portNationIds = port.Nations.Select(n => n.Id);
        var originalNationIds = original.Nations.Select(n => n.Id);
        if (!portNationIds.SequenceEqual(originalNationIds))
        {
            return $"nations: port [{string.Join(",", portNationIds)}], original [{string.Join(",", originalNationIds)}]";
        }

        foreach (var o in original.Nations)
        {
            var p = port.NationById(o.Id)!;
            if (p.Unity != o.Unity)
            {
                return $"{o.Id} unity: port {p.Unity}, original {o.Unity}";
            }
        }

        if (port.NewsLog.MostRecentSlot != original.NewsLog.MostRecentSlot)
        {
            return $"news: most recent slot port {port.NewsLog.MostRecentSlot}, original {original.NewsLog.MostRecentSlot}";
        }

        if (port.NewsLog.Slots.Count != original.NewsLog.Slots.Count)
        {
            return $"news: slot count port {port.NewsLog.Slots.Count}, original {original.NewsLog.Slots.Count}";
        }

        for (var i = 0; i < original.NewsLog.Slots.Count; i++)
        {
            if (!string.Equals(port.NewsLog.Slots[i].Text, original.NewsLog.Slots[i].Text, StringComparison.Ordinal))
            {
                return $"news slot {i}: port '{port.NewsLog.Slots[i].Text}', original '{original.NewsLog.Slots[i].Text}'";
            }
        }

        return null;
    }
}
