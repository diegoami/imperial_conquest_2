using System.Diagnostics;
using IC2.Engine.Ai;
using IC2.Engine.MapGen;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Ai;
using IC2.Engine.Tests.Model;
using Xunit;
using Xunit.Abstractions;

namespace IC2.Engine.Tests.MapGen;

/// <summary>
/// T294g (#1848): AI play, performance and packaging on generated worlds. The engine audit's rule
/// ("generated terrain is still unmeasured: disconnected landmasses, one-tile straits and landlocked
/// starts are generator failure modes the authored maps never produce") makes real AI rounds the
/// acceptance: every style plays a measured round window with zero rejected commands, zero projection
/// mismatches and zero full-round stalls, on a validated final state. Packaging: generated content
/// never becomes shipped data — the worlds and scenarios exist only in memory and in saves.
/// </summary>
public sealed class GeneratedWorldAiTests
{
    private readonly ITestOutputHelper _output;

    public GeneratedWorldAiTests(ITestOutputHelper output) => _output = output;

    public static TheoryData<string, ulong> MeasuredRuns => new()
    {
        { "mediterranean", 1UL },
        { "middle-east", 1UL },
        { "central-asia", 1UL },
        { "india", 1UL },
        { "china", 1UL },
        { "mediterranean", 2UL },
    };

    [Theory]
    [MemberData(nameof(MeasuredRuns))]
    public void Generated_worlds_play_measured_ai_rounds(string styleId, ulong seed)
    {
        var repository = GameDataRepository.Load(TestPaths.DataRoot);
        var ruleset = repository.RulesetById("improved")
                      ?? throw new InvalidOperationException("the shipped data set has no improved ruleset");
        var chosen = GeneratedGame.Previews(styleId, seed)[0];
        var scenario = GeneratedGame.ScenarioFor(chosen.World, ruleset.Id, chosen.Seed);

        var clock = Stopwatch.StartNew();
        var result = AiGameRunner.Run(chosen.World, ruleset, scenario, chosen.Seed, turnCap: 60);
        clock.Stop();
        _output.WriteLine($"{styleId} seed {seed}: {clock.Elapsed.TotalSeconds:F1}s for 60 seat-turns — {result.Summary()}");

        Assert.Equal(0, result.CommandsRejected);
        Assert.Equal(0, result.ProjectionMismatches);
        Assert.Equal(0, result.FullRoundStalls);
        GameDataValidation.Validate($"generated checkpoint ({styleId} seed {seed})", result.FinalState);
    }

    [Fact]
    public void A_generated_round_replays_identically()
    {
        var repository = GameDataRepository.Load(TestPaths.DataRoot);
        var ruleset = repository.RulesetById("improved")!;
        var chosen = GeneratedGame.Previews("mediterranean", 1)[0];
        var scenario = GeneratedGame.ScenarioFor(chosen.World, ruleset.Id, chosen.Seed);
        var first = AiGameRunner.Run(chosen.World, ruleset, scenario, chosen.Seed, turnCap: 60);
        var second = AiGameRunner.Run(chosen.World, ruleset, scenario, chosen.Seed, turnCap: 60);
        Assert.Equal(first.FinalState, second.FinalState);
        Assert.Equal(first.Transcript, second.Transcript);
    }

    [Fact]
    public void Generated_content_never_becomes_shipped_data()
    {
        // Packaging: generated worlds and scenarios exist only in memory and in saves. The shipped
        // data set carries none of them, and the shipped ids never collide with generated ones.
        var repository = GameDataRepository.Load(TestPaths.DataRoot);
        Assert.DoesNotContain(repository.Worlds, w => w.Id.StartsWith(GeneratedGame.WorldIdPrefix, StringComparison.Ordinal));
        Assert.DoesNotContain(repository.Scenarios, s => s.Id.StartsWith(GeneratedGame.WorldIdPrefix, StringComparison.Ordinal));
        Assert.Null(repository.WorldById("random-mediterranean-1"));
        Assert.Null(repository.ScenarioById("random-mediterranean-1"));
    }
}
