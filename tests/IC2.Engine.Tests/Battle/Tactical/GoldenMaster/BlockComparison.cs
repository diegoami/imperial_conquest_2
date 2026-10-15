using System.Text;
using IC2.Data;
using IC2.Engine.Battle.Tactical;

namespace IC2.Engine.Tests.Battle.Tactical.GoldenMaster;

/// <summary>The first field in which the port's battle differs from the original's block.</summary>
/// <param name="Where">"header", "slot 23" or "cell (4, 7)".</param>
/// <param name="Field">The field's name.</param>
/// <param name="Port">The port's value.</param>
/// <param name="Original">The file's value.</param>
internal sealed record BlockDifference(string Where, string Field, string Port, string Original)
{
    public override string ToString() => $"{Where}, {Field}: port {Port}, original {Original}";
}

/// <summary>
/// Compares the port's <see cref="TacticalBattleState"/> with an original <see cref="SaveBattleBlock"/>: the 9 header
/// bytes, the 40 slots and the 168 cells, with no tolerance and no repair. Each name is compared against all 24
/// original bytes, including padding, using the port string encoded as ASCII and zero-padded.
/// </summary>
internal static class BlockComparison
{
    /// <summary>The first difference in header, slot and cell order, or <see langword="null"/> when they are equal.</summary>
    public static BlockDifference? First(TacticalBattleState port, SaveBattleBlock original) =>
        All(port, original).FirstOrDefault();

    /// <summary>Every difference, in header, slot and cell order.</summary>
    public static IEnumerable<BlockDifference> All(TacticalBattleState port, SaveBattleBlock original)
    {
        if (port.Slots.Count != SaveBattleBlock.SlotCount)
        {
            yield return new BlockDifference("header", "slot count", port.Slots.Count.ToString(), SaveBattleBlock.SlotCount.ToString());
            yield break;
        }

        var found = new List<BlockDifference?>
        {
            Check("header", "attacker army", ArmyIndex(port.AttackerArmyId), original.AttackerArmy),
            Check("header", "defender army", ArmyIndex(port.DefenderArmyId), original.DefenderArmy),
            Check("header", "side", port.SideToMove, original.Side),
            Check("header", "placed", port.Placed ? 1 : 0, original.Placed),
            Check("header", "counter", port.Counter, original.Counter),
        };
        foreach (var d in found.Where(d => d is not null))
        {
            yield return d!;
        }

        for (var i = 0; i < SaveBattleBlock.SlotCount; i++)
        {
            var p = port.Slots[i];
            var o = original.Slots[i];
            var where = $"slot {i}";
            var fields = new[]
            {
                Check(where, "x", p.X, o.X),
                Check(where, "y", p.Y, o.Y),
                Check(where, "origin", p.Origin, o.Origin),
                Check(where, "type", p.Type, o.Type),
                Check(where, "troops", p.Troops, o.Troops),
                Check(where, "quality", p.Quality, o.Quality),
                Check(where, "morale", p.Morale, o.Morale),
                Check(where, "moves", p.Moves, o.Moves),
                Check(where, "shots", p.Shots, o.Shots),
                Check(where, "target", p.Target, o.Target),
                NameDifference(where, p.Name, o),
            };
            foreach (var d in fields.Where(d => d is not null))
            {
                yield return d!;
            }
        }

        for (var c = 0; c < SaveBattleBlock.Width * SaveBattleBlock.Height; c++)
        {
            var cx = c / SaveBattleBlock.Height;
            var cy = c % SaveBattleBlock.Height;
            var d = Check($"cell ({cx}, {cy})", "icon code", port.Grid[c], original.Grid[c]);
            if (d is not null)
            {
                yield return d;
            }
        }
    }

    private static BlockDifference? NameDifference(string where, string port, SaveBattleSlot original)
    {
        if (!string.Equals(port, original.Name, StringComparison.Ordinal))
        {
            return new BlockDifference(where, "name", port, original.Name);
        }

        var encoded = Encoding.ASCII.GetBytes(port);
        for (var i = 0; i < SaveBattleBlock.NameLength; i++)
        {
            var value = i < encoded.Length ? encoded[i] : 0;
            var difference = Check(where, $"name byte {i}", value, original.NameBytes[i]);
            if (difference is not null)
            {
                return difference;
            }
        }

        return null;
    }

    /// <summary>The army-table index in an imported army id (<c>army-12</c> gives 12).</summary>
    public static int ArmyIndex(string armyId) =>
        int.Parse(armyId.AsSpan("army-".Length), System.Globalization.CultureInfo.InvariantCulture);

    private static BlockDifference? Check(string where, string field, int port, int original) =>
        port == original ? null : new BlockDifference(where, field, port.ToString(), original.ToString());
}
