using System.Globalization;

namespace IC2.Engine.Tests.Battle.Tactical.GoldenMaster;

/// <summary>One record of the hook's draw log (<c>hooklog-&lt;trial&gt;.csv</c>, the conquest repository's <c>state/hook_log.py</c> layout).</summary>
/// <param name="Seq">The record's sequence number.</param>
/// <param name="Kind">start, random, marker, flag_clear or reseed.</param>
/// <param name="Site">The call site's address.</param>
/// <param name="Range">The register <c>eax</c>: for a <c>random</c> record, the bound of <c>Random(n)</c>.</param>
/// <param name="SeedBefore"><c>RandSeed</c> before the record.</param>
/// <param name="SeedAfter"><c>RandSeed</c> after it.</param>
/// <param name="Result">A <c>random</c> record's returned value.</param>
/// <param name="Counter">The half-round counter at the record.</param>
/// <param name="Side">The side at the record.</param>
internal sealed record HookRecord(
    int Seq, string Kind, int Site, uint Range, uint SeedBefore, uint SeedAfter, uint Result, int Counter, int Side);

/// <summary>A hooked trial's draw log.</summary>
internal sealed class HookLog
{
    /// <summary>The site of the peace dialog's <c>Random(5)</c>.</summary>
    public const int PeaceSite = 0x45951C;

    /// <summary>The site of the last promotion draw.</summary>
    public const int PromotionSite = 0x4592BD;

    public HookLog(IReadOnlyList<HookRecord> records) => Records = records;

    public IReadOnlyList<HookRecord> Records { get; }

    /// <summary>The <c>random</c> records, in order.</summary>
    public IReadOnlyList<HookRecord> Draws => Records.Where(r => r.Kind == "random").ToList();

    /// <summary>The peace-site record, or <see langword="null"/> when the original did not take that draw.</summary>
    public HookRecord? PeaceDraw => Records.FirstOrDefault(r => r.Kind == "random" && r.Site == PeaceSite);

    public static HookLog Load(string relative)
    {
        var lines = File.ReadAllLines(BattleFixtures.Path_(relative));
        var records = new List<HookRecord>();
        for (var i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
            {
                continue;
            }

            var f = lines[i].Split(',');
            records.Add(new HookRecord(
                int.Parse(f[0], CultureInfo.InvariantCulture),
                f[1],
                ParseHex(f[2]),
                Wrap(f[3]),
                Wrap(f[6]),
                Wrap(f[7]),
                Wrap(f[8]),
                int.Parse(f[9], CultureInfo.InvariantCulture),
                int.Parse(f[10], CultureInfo.InvariantCulture)));
        }

        return new HookLog(records);
    }

    /// <summary>
    /// The pairing check, run before a series is replayed: the battle-start record carries <paramref name="seed"/> as
    /// <c>RandSeed</c>, the seed chain is unbroken (each record's seed-before is the previous record's seed-after), and
    /// the highest half-round counter equals the series' last <c>BATTLEnn</c> number. A failure is a fixture error.
    /// </summary>
    /// <returns>The reason it failed, or <see langword="null"/>.</returns>
    public string? PairingError(uint seed, int lastBattleNumber)
    {
        var start = Records.FirstOrDefault(r => r.Kind == "start");
        if (start is null)
        {
            return "the log has no battle-start record";
        }

        if (start.SeedBefore != seed)
        {
            return $"the battle-start record's RandSeed is {start.SeedBefore}, the series' seed is {seed}";
        }

        for (var i = 1; i < Records.Count; i++)
        {
            if (Records[i].SeedBefore != Records[i - 1].SeedAfter)
            {
                return $"the seed chain breaks at record {Records[i].Seq}: seed-before {Records[i].SeedBefore}, previous seed-after {Records[i - 1].SeedAfter}";
            }
        }

        var highest = Records.Max(r => r.Counter);
        return highest == lastBattleNumber
            ? null
            : $"the highest half-round counter is {highest}, the last BATTLE file is {lastBattleNumber}";
    }

    private static int ParseHex(string text) =>
        int.Parse(text.AsSpan(text.StartsWith("0x", StringComparison.Ordinal) ? 2 : 0), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    /// <summary>The log's registers are 32-bit; a negative or large value is read as the unsigned word it is.</summary>
    private static uint Wrap(string text)
    {
        var v = long.Parse(text, CultureInfo.InvariantCulture);
        return unchecked((uint)v);
    }
}

/// <summary>Compares the port's draws with a hook log's <c>random</c> records.</summary>
internal static class DrawComparison
{
    /// <summary>
    /// The first difference between <paramref name="port"/> and the log's draws, in order, range and result, or
    /// <see langword="null"/> when they are equal one for one (none extra, none missing). A bound is compared as the
    /// unsigned register the log holds.
    /// </summary>
    public static string? First(IReadOnlyList<PortDraw> port, HookLog log)
    {
        var original = log.Draws;
        var common = Math.Min(port.Count, original.Count);
        for (var i = 0; i < common; i++)
        {
            var p = port[i];
            var o = original[i];
            if (unchecked((uint)p.Bound) != o.Range || unchecked((uint)p.Value) != o.Result)
            {
                return $"draw {i}: half-round counter {o.Counter} (port {p.Counter}), original site 0x{o.Site:X} Random({o.Range}) = {o.Result}, "
                       + $"port Random({p.Bound}) = {p.Value}";
            }
        }

        if (port.Count > original.Count)
        {
            var p = port[original.Count];
            return $"draw {original.Count}: the port takes an extra Random({p.Bound}) in half-round {p.Counter} after the log's last draw "
                   + $"(site 0x{original[^1].Site:X})";
        }

        if (original.Count > port.Count)
        {
            var o = original[port.Count];
            return $"draw {port.Count}: the port takes none; the original draws at site 0x{o.Site:X} Random({o.Range}) = {o.Result} in half-round {o.Counter}";
        }

        return null;
    }
}
