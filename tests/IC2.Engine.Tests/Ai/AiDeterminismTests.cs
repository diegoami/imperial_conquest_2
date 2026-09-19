using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Tests.Core.Determinism;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// Reproducibility, asserted directly rather than inferred: the same seed gives the same game, twice,
/// exactly.
/// </summary>
/// <remarks>
/// <para>
/// <c>docs/build-process.md</c> §4.2 gate 3 asks every task for "<em>a seeded test [that] proves
/// reproducibility</em>", and this is the task where that bar is highest: fifty fixed seeds means one
/// irreproducible draw shows up as one flaky seed in fifty, the worst failure mode in this codebase to
/// debug. So the comparison here is not a summary or a hash of the final state alone — it is the
/// <em>whole transcript</em>, every decision line of every turn, plus the final state's
/// <see cref="GameStateHash"/>. Two runs that agreed on the outcome but disagreed about which army
/// marched where on turn 300 would fail this.
/// </para>
/// <para>
/// The second half of the gate is structural: <c>src/IC2.Engine/Ai/**</c> is inside the tree
/// <see cref="DeterminismScanner"/> already walks, so the standing guard in
/// <c>DeterminismGuardTests</c> covers this task's sources for <c>System.Random</c>, the wall clock,
/// <c>Guid.NewGuid</c> <em>and</em> enumeration of an unordered collection.
/// <see cref="The_ai_sources_are_covered_by_the_standing_determinism_guard"/> asserts the AI directory
/// is genuinely in that scan rather than trusting that it is, and that it needs no suppression.
/// </para>
/// </remarks>
public sealed class AiDeterminismTests
{
    [Theory]
    [InlineData(1UL)]
    [InlineData(17UL)]
    [InlineData(50UL)]
    public void The_same_seed_produces_an_identical_transcript_twice(ulong seed)
    {
        var first = AiTestbed.RunSeed(seed);
        var second = AiTestbed.RunSeed(seed);

        Assert.Equal(first.Transcript, second.Transcript);
        Assert.Equal(
            GameStateHash.Compute(first.FinalState), GameStateHash.Compute(second.FinalState));
        Assert.Equal(first.Summary(), second.Summary());
    }

    /// <summary>
    /// The control for the test above: two different seeds must produce different games, or "identical
    /// twice" would be true of an AI that ignored the seed entirely.
    /// </summary>
    [Fact]
    public void Two_different_seeds_produce_different_games()
    {
        var first = AiTestbed.RunSeed(1);
        var second = AiTestbed.RunSeed(2);

        Assert.NotEqual(first.Transcript, second.Transcript);
        Assert.NotEqual(
            GameStateHash.Compute(first.FinalState), GameStateHash.Compute(second.FinalState));
    }

    /// <summary>
    /// Done-when 4's actual promise: a failing seed is reproducible <em>from its number alone</em>. Given
    /// only the number, <see cref="AiTestbed.RunSeed"/> reproduces the run byte for byte, and the log on
    /// disk is that run's own transcript rather than a summary of it.
    /// </summary>
    [Fact]
    public void A_seed_number_alone_reproduces_the_run_that_wrote_its_log()
    {
        const ulong seed = 7;
        Directory.CreateDirectory(AiSoakTests.LogDirectory);
        var path = AiSoakTests.LogPathFor(seed);

        File.WriteAllLines(path, AiTestbed.RunSeed(seed).Transcript);

        var reproduced = AiTestbed.RunSeed(seed);
        Assert.Equal(File.ReadAllLines(path), reproduced.Transcript.ToArray());

        // The log is a transcript of decisions, not a summary: the seed header and at least one decision
        // line have to be in it, or "reproducible from its number" would mean re-running blind.
        Assert.Contains(reproduced.Transcript, line => line.StartsWith("seed 7 |", StringComparison.Ordinal));
        Assert.Contains(reproduced.Transcript, line => line.Contains("chose [", StringComparison.Ordinal));
    }

    /// <summary>
    /// One AI turn, driven twice over the same hand-built state and the same stream, decides the same
    /// way — the unit-level half of the same claim, so a regression is localised to a turn rather than
    /// to a thousand-turn game.
    /// </summary>
    [Fact]
    public void One_scripted_turn_decides_identically_twice()
    {
        var state = AiScriptedStates.TwoArmiesInContact(AiScriptedStates.DefaultPersonality);

        var first = AiScriptedStates.DriveOneTurn(state);
        var second = AiScriptedStates.DriveOneTurn(state);

        Assert.Equal(first.Outcome.Log, second.Outcome.Log);
        Assert.Equal(first.IssuedKinds, second.IssuedKinds);
        Assert.Equal(
            GameStateHash.Compute(first.Outcome.State), GameStateHash.Compute(second.Outcome.State));
    }

    /// <summary>
    /// The AI's sources are inside the standing guard's scan, and need no suppression to pass it. The
    /// guard itself is <c>DeterminismGuardTests</c>; this asserts the coverage rather than assuming it.
    /// </summary>
    [Fact]
    public void The_ai_sources_are_covered_by_the_standing_determinism_guard()
    {
        var aiRoot = Path.Combine(ModelTestPaths.RepositoryRoot, "src", "IC2.Engine", "Ai");
        Assert.True(Directory.Exists(aiRoot), aiRoot);

        var violations = DeterminismScanner.Scan(aiRoot, ModelTestPaths.RepositoryRoot);
        Assert.True(
            violations.Count == 0,
            "src/IC2.Engine/Ai must contain no nondeterministic construct. Found:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations));

        var suppressions = DeterminismScanner.Suppressions(aiRoot, ModelTestPaths.RepositoryRoot);
        Assert.True(
            suppressions.Count == 0,
            "the AI suppresses the ordering rule somewhere: "
            + string.Join(Environment.NewLine, suppressions));

        // And the scan really is looking at this task's files, not at an empty directory.
        Assert.NotEmpty(Directory.GetFiles(aiRoot, "*.cs", SearchOption.AllDirectories));
    }

    /// <summary>
    /// The fleet-strength estimate draws from a <em>derived</em> stream, so asking for it does not move
    /// the seat's own generator. If it did, how many enemy fleets happened to be adjacent would silently
    /// shift every later draw in the turn.
    /// </summary>
    [Fact]
    public void Sampling_a_fleet_strength_estimate_does_not_advance_the_seats_own_stream()
    {
        var rng = SplitMix64Rng.ForStream(1, "ai.turn");
        var before = rng.State;

        rng.ForStream("ai.fleet-estimate:a:b").NextUInt64();

        Assert.Equal(before, rng.State);
    }

    /// <summary>
    /// Personality is read once, as an integer, and the conversion is exact and stable — so a score
    /// comparison can never be decided by a floating-point rounding difference.
    /// </summary>
    [Theory]
    [InlineData(0.0, 0)]
    [InlineData(0.1, 100)]
    [InlineData(0.5, 500)]
    [InlineData(0.9, 900)]
    [InlineData(1.0, 1000)]
    [InlineData(0.0005, 1)]
    public void Personality_converts_to_permille_exactly_once(double value, int expected)
    {
        var nation = AiScriptedStates.AiNation(
            "n", new AiPersonality(value, ExpansionDrive: value, LoyaltyToAlliances: value));

        var profile = AiPersonalityProfile.For(nation);

        Assert.Equal(expected, profile.AggressionPermille);
        Assert.Equal(expected, profile.ExpansionDrivePermille);
        Assert.Equal(expected, profile.LoyaltyToAlliancesPermille);
    }

    /// <summary>An AI seat with no personality block plays at the declared midpoint rather than failing.</summary>
    [Fact]
    public void An_ai_seat_with_no_personality_block_plays_at_the_declared_default()
    {
        var nation = AiScriptedStates
            .AiNation("n", AiScriptedStates.DefaultPersonality) with { Personality = null };

        var profile = AiPersonalityProfile.For(nation);

        Assert.Equal(AiWeights.DefaultPersonalityPermille, profile.AggressionPermille);
        Assert.Equal(AiWeights.DefaultPersonalityPermille, profile.ExpansionDrivePermille);
        Assert.Equal(AiWeights.DefaultPersonalityPermille, profile.LoyaltyToAlliancesPermille);
    }
}
