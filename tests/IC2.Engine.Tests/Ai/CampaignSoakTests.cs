using Xunit;
using Xunit.Abstractions;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// T204: two all-AI soaks under the soak's existing checks (no rejected command, no projection mismatch, no
/// full-round stall, #1290's rule). The siege soak runs on a separately named siege-capable variant of the toy
/// world (#267), and asserts that sieges and captures really happen. The Italy soak runs the shipped campaign.
/// </summary>
public sealed class CampaignSoakTests(ITestOutputHelper output)
{
    [Fact]
    public void Siege_soak_seeds_capture_cities_with_no_rejection_mismatch_or_full_round_stall()
    {
        var results = Enumerable.Range(1, AiTestbed.SiegeSeedCount).Select(i => AiTestbed.RunSiegeSeed((ulong)i)).ToList();
        foreach (var result in results)
        {
            output.WriteLine(result.Summary());
        }

        Assert.All(results, r => Assert.Equal(0, r.CommandsRejected));
        Assert.All(results, r => Assert.Equal(0, r.ProjectionMismatches));
        Assert.All(results, r => Assert.Equal(0, r.FullRoundStalls));

        // Asserted, not assumed: at least half the seeds take a city, and some seed wins by total conquest.
        Assert.True(
            results.Count(r => r.CitiesCaptured > 0) * 2 >= results.Count,
            $"only {results.Count(r => r.CitiesCaptured > 0)} of {results.Count} siege seeds captured a city");
        Assert.Contains(results, r => r.Ending == IC2.Engine.Ai.AiGameEnding.Won);
        Assert.True(results.Sum(r => r.SiegesFailed + r.CitiesCaptured) > results.Count, "too few sieges were fought");
    }

    [Fact]
    public void Italy_soak_seeds_play_50_rounds_with_captures_and_no_rejection_mismatch_or_full_round_stall()
    {
        var results = Enumerable.Range(1, AiTestbed.ItalySeedCount).Select(i => AiTestbed.RunItalySeed((ulong)i)).ToList();
        foreach (var result in results)
        {
            output.WriteLine(result.Summary());
        }

        Assert.All(results, r => Assert.Equal(0, r.CommandsRejected));
        Assert.All(results, r => Assert.Equal(0, r.ProjectionMismatches));
        Assert.All(results, r => Assert.Equal(0, r.FullRoundStalls));
        Assert.All(results, r => Assert.True(r.CitiesCaptured > 0, $"seed {r.Seed} captured no city"));
    }
}
