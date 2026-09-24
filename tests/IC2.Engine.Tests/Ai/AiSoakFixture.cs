using System.Diagnostics;
using System.Globalization;
using System.Text;
using IC2.Engine.Ai;
using Xunit;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// Runs the 50-seed soak exactly once and shares the result with every <c>[Fact]</c> in
/// <see cref="AiSoakTests"/> (<c>docs/task-catalogue.md</c> T22 Done-when 2; follow-up
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/237">#237</see> N2).
/// </summary>
/// <remarks>
/// xUnit constructs one <see cref="AiSoakFixture"/> per test class that declares
/// <c>IClassFixture&lt;AiSoakFixture&gt;</c> and reuses that single instance across every fact in the
/// class, so the 50-seed run — and the per-seed log files it writes — happens once per test run rather
/// than once per fact. If the constructor throws, xUnit fails every fact that depends on this fixture
/// with that same exception; nothing here catches or swallows it, so a broken soak cannot be cached as
/// a false green.
/// </remarks>
public sealed class AiSoakFixture
{
    /// <summary>The Done-when 2 budget: five minutes of wall clock for the whole soak, asserted.</summary>
    public static readonly TimeSpan Budget = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Where the per-seed logs land: a directory beside the test assembly, so a developer or a CI job can
    /// collect it as an artifact without the tests needing to know anything about the build layout.
    /// </summary>
    public static string LogDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "ai-soak-logs");

    /// <summary>The one soak run every fact in <see cref="AiSoakTests"/> reads.</summary>
    public SoakReport Report { get; }

    public AiSoakFixture()
    {
        Report = RunSoak();
    }

    /// <summary>The log file one seed writes — <c>seed-7.log</c> for seed 7, and nothing else.</summary>
    public static string LogPathFor(ulong seed) =>
        Path.Combine(LogDirectory, string.Format(CultureInfo.InvariantCulture, "seed-{0}.log", seed));

    private static SoakReport RunSoak()
    {
        Directory.CreateDirectory(LogDirectory);

        var results = new List<AiGameResult>(AiTestbed.SoakSeedCount);
        var stopwatch = Stopwatch.StartNew();
        foreach (var seed in AiTestbed.SoakSeeds())
        {
            results.Add(AiTestbed.RunSeed(seed));
        }

        stopwatch.Stop();

        // Written after the clock stops: Done-when 2's budget is the soak's own cost, not the cost of
        // producing Done-when 4's artifacts alongside it.
        foreach (var result in results)
        {
            File.WriteAllLines(LogPathFor(result.Seed), result.Transcript, Encoding.UTF8);
        }

        return new SoakReport(results, stopwatch.Elapsed);
    }

    public sealed class SoakReport
    {
        public SoakReport(List<AiGameResult> results, TimeSpan elapsed)
        {
            Results = results;
            Elapsed = elapsed;

            var won = 0;
            var expired = 0;
            var capped = 0;
            foreach (var result in results)
            {
                RejectedCommands += result.CommandsRejected;
                ProjectionMismatches += result.ProjectionMismatches;
                IssuedCommands += result.CommandsIssued;
                CommandlessTurns += result.CommandlessTurns;
                TurnsPlayed += result.TurnsPlayed;
                WorstStallRun = Math.Max(WorstStallRun, result.LongestStallRun);
                switch (result.Ending)
                {
                    case AiGameEnding.Won: won++; break;
                    case AiGameEnding.Expired: expired++; break;
                    default: capped++; break;
                }
            }

            Summary = string.Format(
                CultureInfo.InvariantCulture,
                "{0} seeds, cap {1}: {2} won, {3} expired at the hard end year, {4} reached the turn cap | "
                + "{5} turns, {6} commands ({7} turns issued none), {8} rejected, {9} probe mismatches, "
                + "worst stall run {10} | {11:F2}s of a {12:F0}s budget",
                results.Count, AiTestbed.SoakTurnCap, won, expired, capped,
                TurnsPlayed, IssuedCommands, CommandlessTurns, RejectedCommands, ProjectionMismatches,
                WorstStallRun, elapsed.TotalSeconds, Budget.TotalSeconds);
        }

        public List<AiGameResult> Results { get; }

        public TimeSpan Elapsed { get; }

        public int RejectedCommands { get; }

        public int ProjectionMismatches { get; }

        public int IssuedCommands { get; }

        public int CommandlessTurns { get; }

        public int TurnsPlayed { get; }

        public int WorstStallRun { get; }

        public string Summary { get; }

        /// <summary>The seed numbers matching a predicate, for a failure message.</summary>
        public string SeedsWith(Func<AiGameResult, bool> predicate)
        {
            var seeds = new List<string>();
            foreach (var result in Results)
            {
                if (predicate(result))
                {
                    seeds.Add(result.Seed.ToString(CultureInfo.InvariantCulture));
                }
            }

            return seeds.Count == 0
                ? string.Empty
                : "Seeds: " + string.Join(", ", seeds) + ". Reproduce one with AiTestbed.RunSeed(<seed>); "
                  + "its log is in " + LogDirectory + ".";
        }
    }
}
