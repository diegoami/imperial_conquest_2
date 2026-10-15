using System.Globalization;
using System.Text.RegularExpressions;

namespace IC2.Engine.Tests.Battle.Tactical.GoldenMaster;

/// <summary>A hooked trial: its series, its draw log and its post-battle save.</summary>
/// <param name="Series">The series to replay.</param>
/// <param name="HookLog">The draw log, relative to <c>battles/</c>.</param>
/// <param name="Cell">The B5 cell, or <c>mix-rg</c>.</param>
/// <param name="Seed">The trial's seed.</param>
internal sealed record HookedTrial(SeriesSpec Series, string HookLog, string Cell, uint Seed);

/// <summary>
/// The golden master's series, found in the fixtures' <c>battles/</c> folder by the names the task's table gives
/// (<c>docs/tasks/T129.md</c>, "The fixtures"). A series is every <c>BATTLEnn</c> file of its name in order; nothing
/// is dropped.
/// </summary>
internal static class SeriesCatalogue
{
    private static readonly Regex TrialPattern = new(@"^(?<cell>.+)_s(?<seed>\d+)_r5_hook_post\.SAV$", RegexOptions.Compiled);

    /// <summary>The start save of the gate series: Rome's army 0 next to Gaul's army 10.</summary>
    public const string RomeGaulStart = "starts/FLD-RG_0743_rome_army0_at_86_28.SAV";

    /// <summary>The <c>BATTLEnn</c> files <c>folder/prefix_BATTLEnn.SAV</c>, in order.</summary>
    public static IReadOnlyList<string> Files(string folder, string prefix)
    {
        var dir = BattleFixtures.Path_(folder);
        return Directory.GetFiles(dir, prefix + "_BATTLE*.SAV")
            .Select(Path.GetFileName)
            .Where(n => Regex.IsMatch(n!, @"_BATTLE\d+\.SAV$"))
            .OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => folder + "/" + n)
            .ToList();
    }

    /// <summary>The gate series <c>gate2_a</c> (seed 1) and <c>gate2_s2a</c> (seed 2).</summary>
    public static SeriesSpec Gate(string name, uint seed) =>
        new(name, RomeGaulStart, seed, Files("probe", name), $"probe/{name}_post.SAV");

    /// <summary><c>hi-hi-one_s{seed}_r1</c>.</summary>
    public static SeriesSpec HiHiOne(int seed) =>
        new($"hi-hi-one_s{seed}_r1", "starts/hi-hi-one_start.SAV", (uint)seed,
            Files("hi-hi-one", $"hi-hi-one_s{seed}_r1"), $"hi-hi-one/hi-hi-one_s{seed}_r1_post.SAV");

    /// <summary><c>resume05_gate2_a_r1</c>: <c>gate2_a_BATTLE05</c> resumed under seed 1.</summary>
    public static SeriesSpec Resume05() =>
        new("resume05_gate2_a_r1", "probe/gate2_a_BATTLE05.SAV", 1, Files("probe", "resume05_gate2_a_r1"), null, ReplayStart.Resume);

    /// <summary>A crafted block save's series (<c>b3_&lt;name&gt;</c>), replayed from its own block under seed 1.</summary>
    public static SeriesSpec Crafted(string name) =>
        new($"CRAFT_{name}", $"crafted/CRAFT_{name}.SAV", 1, Files("crafted", $"b3_{name}"), $"crafted/b3_{name}_post.SAV", ReplayStart.Resume);

    /// <summary>The crafted saves whose series are replayed as the table's "as hi-hi-one" rows.</summary>
    public static readonly string[] CraftedPlayed = { "c0_control", "c2_troops", "c4_slot_only", "c7_type", "d0_control", "d3_adjacent", "d8_far" };

    /// <summary>The crafted saves with stale or doubled grid cells.</summary>
    public static readonly string[] CraftedGrid = { "c5_stale_grid", "c6_same_cell", "d5_stale_grid", "d6_same_cell" };

    /// <summary>Every hooked trial in the fixtures (the B5 grid, the size matrix and <c>mix-rg</c>), by name.</summary>
    public static IReadOnlyList<HookedTrial> HookedTrials() => HookedCache.Value;

    private static readonly Lazy<IReadOnlyList<HookedTrial>> HookedCache = new(FindHookedTrials);

    private static IReadOnlyList<HookedTrial> FindHookedTrials()
    {
        var trials = new List<HookedTrial>();
        foreach (var post in Directory.GetFiles(BattleFixtures.Path_("hook"), "*_r5_hook_post.SAV").OrderBy(f => f, StringComparer.Ordinal))
        {
            var m = TrialPattern.Match(Path.GetFileName(post));
            if (!m.Success)
            {
                continue;
            }

            var cell = m.Groups["cell"].Value;
            var seed = uint.Parse(m.Groups["seed"].Value, CultureInfo.InvariantCulture);
            var trial = $"{cell}_s{seed}_r5_hook";
            var start = string.Equals(cell, "mix-rg", StringComparison.Ordinal) ? RomeGaulStart : $"starts/{cell}_start.SAV";
            trials.Add(new HookedTrial(
                new SeriesSpec(trial, start, seed, Files("hook", trial), $"hook/{trial}_post.SAV"),
                $"hook/hooklog-{trial}.csv",
                cell,
                seed));
        }

        return trials;
    }
}

/// <summary>The member data of the golden master's theories: series names, or one placeholder row when the fixtures are absent.</summary>
internal static class GoldenData
{
    /// <summary>The fixed (non-hooked) series of the table's first rows.</summary>
    public static readonly string[] FixedSeries =
        { "gate2_a", "gate2_s2a", "hi-hi-one_s1_r1", "hi-hi-one_s2_r1", "hi-hi-one_s3_r1" };

    /// <summary>The expected file count of each fixed series (the table's "Files" column).</summary>
    public static readonly Dictionary<string, int> FixedFileCounts = new()
    {
        ["gate2_a"] = 15, ["gate2_s2a"] = 18, ["hi-hi-one_s1_r1"] = 19, ["hi-hi-one_s2_r1"] = 18, ["hi-hi-one_s3_r1"] = 15,
    };

    /// <summary>The series with the given name (a fixed row, or a crafted <c>CRAFT_…</c> row).</summary>
    public static SeriesSpec Named(string name) => name switch
    {
        "gate2_a" => SeriesCatalogue.Gate("gate2_a", 1),
        "gate2_s2a" => SeriesCatalogue.Gate("gate2_s2a", 2),
        "hi-hi-one_s1_r1" => SeriesCatalogue.HiHiOne(1),
        "hi-hi-one_s2_r1" => SeriesCatalogue.HiHiOne(2),
        "hi-hi-one_s3_r1" => SeriesCatalogue.HiHiOne(3),
        "resume05_gate2_a_r1" => SeriesCatalogue.Resume05(),
        _ when name.StartsWith("CRAFT_", StringComparison.Ordinal) => SeriesCatalogue.Crafted(name["CRAFT_".Length..]),
        _ => throw new ArgumentException($"Unknown series '{name}'.", nameof(name)),
    };

    /// <summary>Rows for a theory over <paramref name="names"/>; one "(fixtures absent)" row when there is nothing to run.</summary>
    public static IEnumerable<object[]> Rows(IEnumerable<string> names)
    {
        var list = names.ToList();
        return list.Count == 0 ? new[] { new object[] { "(fixtures absent)" } } : list.Select(n => new object[] { n });
    }

    public static IEnumerable<object[]> Fixed() => Rows(FixedSeries);

    public static IEnumerable<object[]> CraftedPlayedRows() => Rows(SeriesCatalogue.CraftedPlayed.Select(c => "CRAFT_" + c));

    public static IEnumerable<object[]> CraftedGridRows() => Rows(SeriesCatalogue.CraftedGrid.Select(c => "CRAFT_" + c));

    public static IEnumerable<object[]> Hooked() =>
        Rows(BattleFixtures.SkipReason.Length == 0 ? SeriesCatalogue.HookedTrials().Select(t => t.Series.Name) : Array.Empty<string>());

    /// <summary>The hooked trial with the given name.</summary>
    public static HookedTrial Trial(string name) => ByName.Value[name];

    private static readonly Lazy<Dictionary<string, HookedTrial>> ByName =
        new(() => SeriesCatalogue.HookedTrials().ToDictionary(t => t.Series.Name, StringComparer.Ordinal));
}
