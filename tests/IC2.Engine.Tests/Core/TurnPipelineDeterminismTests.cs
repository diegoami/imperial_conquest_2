using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;

namespace IC2.Engine.Tests.Core;

/// <summary>
/// Definition of Done item 1: "Two full runs of a scripted N-phase sequence with the same seed produce
/// byte-identical <c>GameState</c> hashes; with different seeds, different hashes."
/// </summary>
/// <remarks>
/// The script runs whole rounds of seats, so the seat-scoped phases, the round-scoped phases, the
/// round-tick signal and the command dispatcher all take part. Both the final state and the whole
/// sequence of intermediate states are hashed: two runs that diverged in the middle and converged at the
/// end would pass a final-state check and fail this one.
/// </remarks>
public class TurnPipelineDeterminismTests
{
    /// <summary>
    /// What one run of the script produced: every state it passed through, and everything it published.
    /// </summary>
    private sealed record ScriptRun(
        IReadOnlyList<GameState> States,
        IReadOnlyList<DomainEvent> Events,
        IReadOnlyList<string> Trace)
    {
        public GameState Final => States[^1];

        public string SequenceHash => GameStateHash.ComputeSequence(States);

        public string FinalHash => GameStateHash.Compute(Final);
    }

    [Fact]
    public void The_same_seed_replays_byte_identically()
    {
        var first = RunScript(CoreTestbed.InitialState());
        var second = RunScript(CoreTestbed.InitialState());

        Assert.Equal(first.FinalHash, second.FinalHash);
        Assert.Equal(first.SequenceHash, second.SequenceHash);

        // The hash is a fingerprint of the canonical bytes, so assert the bytes directly too: a hash
        // match with differing bytes would mean the fingerprint, not the engine, is doing the work.
        Assert.Equal(
            GameStateHash.CanonicalBytes(first.Final),
            GameStateHash.CanonicalBytes(second.Final));

        Assert.Equal(
            first.Events.Select(e => e.Kind).ToArray(),
            second.Events.Select(e => e.Kind).ToArray());

        Assert.Equal(first.Trace, second.Trace);
    }

    [Fact]
    public void A_different_seed_produces_a_different_state()
    {
        var initial = CoreTestbed.InitialState();

        // The alternate seed is derived rather than invented, so no literal seed appears in this file.
        var alternate = initial with { RandomSeed = RngStreams.Advance(initial.RandomSeed) };
        Assert.NotEqual(initial.RandomSeed, alternate.RandomSeed);

        var baseline = RunScript(initial);
        var other = RunScript(alternate);

        Assert.NotEqual(baseline.FinalHash, other.FinalHash);
        Assert.NotEqual(baseline.SequenceHash, other.SequenceHash);

        // ... but the pipeline itself is unchanged: the same systems ran, in the same order. Only the
        // numbers they drew moved.
        Assert.Equal(baseline.Trace, other.Trace);
    }

    [Fact]
    public void Several_different_seeds_all_produce_different_states()
    {
        var initial = CoreTestbed.InitialState();
        var hashes = new List<string>();

        var seed = initial.RandomSeed;
        for (var i = 0; i < CoreTestbed.Toy.World.TurnOrder.Count * CoreTestbed.Toy.Ruleset.Calendar.SeasonsPerYear; i++)
        {
            hashes.Add(RunScript(initial with { RandomSeed = seed }).FinalHash);
            seed = RngStreams.Advance(seed);
        }

        Assert.Equal(hashes.Count, hashes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void The_state_hash_survives_a_json_round_trip()
    {
        var final = RunScript(CoreTestbed.InitialState()).Final;

        var reloaded = GameDataLoader.Load<GameState>("round-tripped-state.json", GameJson.Serialize(final));

        Assert.Equal(final, reloaded);
        Assert.Equal(GameStateHash.Compute(final), GameStateHash.Compute(reloaded));
    }

    [Fact]
    public void Two_states_that_differ_in_one_field_hash_differently()
    {
        var state = CoreTestbed.InitialState();
        var nudged = state with
        {
            ActiveSeatIndex = (state.ActiveSeatIndex + 1) % state.TurnOrder.Count,
        };

        Assert.NotEqual(GameStateHash.Compute(state), GameStateHash.Compute(nudged));
    }

    /// <summary>
    /// The script: a whole year's worth of rounds of seats, with one command dispatched per turn.
    /// </summary>
    /// <remarks>
    /// The turn count is derived from the loaded data — seats per round × seasons per year — rather than
    /// being a number chosen here, so the script scales with the scenario instead of hiding a literal.
    /// </remarks>
    private static ScriptRun RunScript(GameState initial)
    {
        var sink = new RecordingEventSink();
        var coordinator = CoreTestbed.CoordinatorFor(PipelineFixtures.Group, sink);
        var dispatcher = CoreTestbed.DispatcherFor(PipelineFixtures.Group, sink);

        var rounds = CoreTestbed.Toy.Ruleset.Calendar.SeasonsPerYear;
        var seats = initial.TurnOrder.Count;

        var states = new List<GameState> { initial };
        var trace = new List<string>();
        var current = initial;

        for (var turn = 0; turn < rounds * seats; turn++)
        {
            var command = dispatcher.Dispatch(current, new PipelineStockUpCommand(current.ActiveNationId));
            Assert.True(command.IsAccepted, command.ToString());
            current = command.State;
            states.Add(current);

            var result = coordinator.RunTurn(current);
            current = result.State;
            states.Add(current);
            trace.AddRange(result.Trace.Select(execution => $"{execution.Phase}/{execution.SystemId}"));
        }

        return new ScriptRun(states, sink.Events.ToArray(), trace);
    }
}
