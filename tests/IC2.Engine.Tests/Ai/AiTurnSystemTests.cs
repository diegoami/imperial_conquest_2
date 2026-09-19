using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using Xunit;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// The wiring: the AI is a registered <see cref="TurnPhase.Orders"/> system, it speaks only through the
/// command seam, and the runner that plays whole games cannot be started without a turn cap.
/// </summary>
public sealed class AiTurnSystemTests
{
    [Fact]
    public void The_ai_is_registered_into_the_orders_phase()
    {
        var registry = SystemRegistry.FromEngineAssembly();

        var registration = registry.Systems
            .FirstOrDefault(s => s.ImplementationType == typeof(AiTurnSystem));

        Assert.NotNull(registration);
        Assert.Equal("ai.turn", registration!.Id);
        Assert.Equal(TurnPhase.Orders, registration.Phase);
    }

    /// <summary>
    /// A whole turn through the real <see cref="TurnCoordinator"/> publishes the AI's decision record, so
    /// a harness outside the pipeline can write Done-when 4's log without the AI owning a file.
    /// </summary>
    [Fact]
    public void A_pipeline_turn_publishes_the_ai_decision_record()
    {
        var toy = AiTestbed.Toy;
        var registry = SystemRegistry.FromEngineAssembly();
        var sink = new RecordingEventSink();
        var dispatcher = new CommandDispatcher(registry, toy.Ruleset, toy.World, sink);
        var coordinator = new TurnCoordinator(registry, toy.Ruleset, toy.World, sink, dispatcher);
        var state = GameStateFactory.CreateInitial(toy.World, toy.Ruleset, AiTestbed.AllAiScenario());

        var result = coordinator.RunTurn(state);

        var decided = result.Events.OfType<AiTurnDecided>().Single();
        Assert.Equal(state.ActiveNationId, decided.NationId);
        Assert.Equal(0, decided.CommandsRejected);
        Assert.Equal(0, decided.ProjectionMismatches);
        Assert.NotEmpty(decided.Lines);

        // Instrumentation, not narration: a rendered news line per AI decision would drown the original's
        // own confirmed news format.
        Assert.False(decided.IsNewsWorthy);
    }

    /// <summary>
    /// The non-termination hazard, closed structurally: there is no overload and no default that lets a
    /// caller start an all-AI game without a bound on it.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_game_cannot_be_started_without_a_positive_turn_cap(int turnCap)
    {
        var toy = AiTestbed.Toy;

        var error = Assert.Throws<ArgumentOutOfRangeException>(
            () => AiGameRunner.Run(toy.World, toy.Ruleset, AiTestbed.AllAiScenario(), seed: 1, turnCap));

        Assert.Equal("turnCap", error.ParamName);
    }

    /// <summary>
    /// And the cap really binds: a one-turn cap stops after one turn rather than playing on to a
    /// decision. Without this, "the cap is mandatory" would be a claim about a parameter nobody reads.
    /// </summary>
    [Fact]
    public void The_turn_cap_stops_a_game_that_has_not_finished()
    {
        var toy = AiTestbed.Toy;

        var result = AiGameRunner.Run(toy.World, toy.Ruleset, AiTestbed.AllAiScenario(), seed: 1, turnCap: 1);

        Assert.Equal(AiGameEnding.TurnCapReached, result.Ending);
        Assert.Equal(1, result.TurnsPlayed);
        Assert.Null(result.WinningNationId);
        Assert.Contains(result.Transcript, l => l.Contains("turn cap 1 reached", StringComparison.Ordinal));
    }

    /// <summary>
    /// The AI never writes to the state behind the command seam. Driven with a dispatcher that refuses
    /// everything (<see cref="UnavailableCommandDispatch"/>, the coordinator's own no-dispatcher
    /// fallback), the only thing that may change is the resupply pass — which is a pure T38 function with
    /// no command of its own, and is declared as the single exception in <see cref="AiTurn"/>'s remarks.
    /// </summary>
    [Fact]
    public void Without_a_dispatcher_the_ai_changes_nothing_but_its_resupply_pass()
    {
        var state = AiScriptedStates.TwoArmiesInContact(AiScriptedStates.DefaultPersonality);
        var sink = new RecordingEventSink();
        var rng = SplitMix64Rng.ForStream(1, "ai.turn");

        var outcome = AiTurn.Run(
            state, AiScriptedStates.Ruleset, AiScriptedStates.World,
            UnavailableCommandDispatch.Instance, rng, sink);

        var afterResupplyOnly = AiResupplyPass.Run(
            state, AiScriptedStates.Ruleset, AiScriptedStates.Attacker).State;

        Assert.True(
            AiSubstantiveState.AreEquivalent(afterResupplyOnly, outcome.State),
            "with every command refused, only the resupply pass may have changed the state");

        // ...and it reports the refusals rather than hiding them. This is the one situation in which the
        // AI's rejection count is allowed to be non-zero: the wiring, not the scorer, is at fault.
        Assert.True(outcome.CommandsRejected > 0);
    }
}
