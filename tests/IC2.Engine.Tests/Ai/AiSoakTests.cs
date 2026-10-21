using Xunit;
using Xunit.Abstractions;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// <c>docs/task-catalogue.md</c> T22 Done-when 1, 2 and 4: the fifty-seed soak, its five-minute budget,
/// and the per-seed logs a failing seed is reproduced from.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One test runs all fifty seeds, on purpose.</strong> Done-when 2 asserts a budget for "<em>the
/// whole 50-seed soak</em>", and fifty separate xUnit cases could not measure that — they could each be
/// fast while the suite got slower, which is exactly the drift the line exists to catch. So the soak is
/// one case, it times itself, and it fails on the budget as hard as it fails on a rejected command.
/// </para>
/// <para>
/// <strong>The soak itself runs once per test run, not once per fact (#237 N2).</strong> Both facts below
/// share the one run in <see cref="AiSoakFixture.Report"/>, built once by xUnit's
/// <c>IClassFixture&lt;AiSoakFixture&gt;</c> before either fact runs. See <see cref="AiSoakFixture"/> for
/// how a thrown or partial soak still fails both facts rather than being cached as a false green.
/// </para>
/// <para>
/// <strong>A seed that reaches the turn cap passes.</strong> Done-when 2 says so in as many words. What
/// this test asserts about endings is only that every seed <em>ended</em> — by a victory, by the
/// condition's own hard limit, or by the cap — and never that any seed won. See
/// <see cref="Every_seed_reaches_a_decision_and_the_endings_are_reported"/> for what the shipped toy data
/// actually produces and why.
/// </para>
/// </remarks>
public sealed class AiSoakTests : IClassFixture<AiSoakFixture>
{
    private readonly AiSoakFixture _fixture;
    private readonly ITestOutputHelper _output;

    public AiSoakTests(AiSoakFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public void Fifty_fixed_seeds_run_an_all_ai_game_to_a_decision_with_no_exception_rejection_or_stall()
    {
        var report = _fixture.Report;

        _output.WriteLine(report.Summary);
        _output.WriteLine("per-seed logs: " + AiSoakFixture.LogDirectory);

        // Done-when 1, the three "zero"s. Each is reported with the seeds that broke it, because
        // "reproducible from its number alone" is worthless if the failure does not say the number.
        Assert.True(
            report.RejectedCommands == 0,
            $"the AI must never issue a command the engine refuses; {report.RejectedCommands} were refused. "
            + report.SeedsWith(r => r.CommandsRejected > 0));

        Assert.True(
            report.ProjectionMismatches == 0,
            "an attack's advance probe and the state its own declaration produces must always agree; "
            + $"{report.ProjectionMismatches} disagreed. " + report.SeedsWith(r => r.ProjectionMismatches > 0));

        // #1290 (the user's decision, closing #268): a stall is a full round of live seats all idle, not two
        // idle seat-turns in a row. The longest run is reported above, not failed on.
        Assert.True(
            report.FullRoundStalls == 0,
            "a complete round in which every live seat issues no command and changes no substantive state is "
            + $"a stall; {report.FullRoundStalls} were found. " + report.SeedsWith(r => r.FullRoundStalls > 0));

        // Done-when 2, asserted rather than reported.
        Assert.True(
            report.Elapsed < AiSoakFixture.Budget,
            $"the 50-seed soak must finish inside {AiSoakFixture.Budget.TotalMinutes} minutes so it can run "
            + $"in CI; it took {report.Elapsed.TotalSeconds:F1}s.");

        // Done-when 4: one log per seed, on disk, named by the seed.
        foreach (var result in report.Results)
        {
            var path = AiSoakFixture.LogPathFor(result.Seed);
            Assert.True(File.Exists(path), $"seed {result.Seed} wrote no log at {path}");
            Assert.NotEmpty(File.ReadAllLines(path));
        }
    }

    /// <summary>
    /// #1290: the shipped 16-nation <c>classical-mediterranean</c> scenario, seeds 1–10, under the same rule
    /// and the same zero-rejection, zero-mismatch checks. Every seed reaches its end. Isolated idle seats
    /// (#268's pattern) are reported per seat and pass.
    /// </summary>
    [Fact]
    public void Classical_seeds_1_to_10_have_no_full_round_stall_rejection_or_mismatch()
    {
        var report = _fixture.Classical;
        _output.WriteLine(report.Summary);
        foreach (var result in report.Results)
        {
            _output.WriteLine("  " + result.Summary());
            _output.WriteLine("    idle seat-turns: " + string.Join(", ", result.IdleBySeat.Select(r => $"{r.SeatId} {r.IdleTurns}")));
        }

        Assert.Equal(AiTestbed.ClassicalSeedCount, report.Results.Count);
        Assert.True(report.RejectedCommands == 0, $"{report.RejectedCommands} commands refused. " + report.SeedsWith(r => r.CommandsRejected > 0));
        Assert.True(report.ProjectionMismatches == 0, $"{report.ProjectionMismatches} probe mismatches. " + report.SeedsWith(r => r.ProjectionMismatches > 0));
        Assert.True(report.FullRoundStalls == 0, $"{report.FullRoundStalls} full-round stalls. " + report.SeedsWith(r => r.FullRoundStalls > 0));
        Assert.All(report.Results, r => Assert.True(r.TurnsPlayed > 0 && r.CommandsIssued > 0, $"seed {r.Seed} did nothing"));
    }

    /// <summary>
    /// The soak's own control. Done-when 1 forbids stalls; it does not by itself forbid an AI that issues
    /// one command per game and sleeps. This asserts the soak is actually exercising the engine: every
    /// seed issues commands, every seed reaches the engine's own end, and the fifty games are genuinely
    /// different from one another.
    /// </summary>
    [Fact]
    public void Every_seed_reaches_a_decision_and_the_endings_are_reported()
    {
        var report = _fixture.Report;

        _output.WriteLine(report.Summary);
        foreach (var result in report.Results)
        {
            _output.WriteLine("  " + result.Summary());
        }

        foreach (var result in report.Results)
        {
            Assert.True(
                result.CommandsIssued > 0,
                $"seed {result.Seed} issued no command at all in {result.TurnsPlayed} turns");
            Assert.Equal(0, result.TurnsHittingActionCap);
        }

        // Fifty divergent games, not one game played fifty times: the seed has to reach the world.
        var fingerprints = new List<string>();
        foreach (var result in report.Results)
        {
            fingerprints.Add(IC2.Engine.Core.GameStateHash.Compute(result.FinalState));
        }

        Assert.Equal(AiTestbed.SoakSeedCount, Distinct(fingerprints));
    }

    private static int Distinct(List<string> values)
    {
        values.Sort(StringComparer.Ordinal);
        var distinct = 0;
        for (var i = 0; i < values.Count; i++)
        {
            if (i == 0 || !string.Equals(values[i], values[i - 1], StringComparison.Ordinal))
            {
                distinct++;
            }
        }

        return distinct;
    }
}
