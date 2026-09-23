using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using IC2.Engine.Tests.Model;

namespace IC2.Engine.Tests.Persistence;

/// <summary>
/// The shipped toy scenario, played forward for real through every registered
/// <c>[GameSystem]</c> — never a hand-built mid-game state — so Done-when 1's round trip is over a
/// state the pipeline actually produced.
/// </summary>
public static class PersistenceTestbed
{
    private static readonly Lazy<ResolvedScenario> LazyToy = new(
        () => GameDataRepository.Load(TestPaths.DataRoot).Resolve("toy-3city"));

    /// <summary>The toy scenario with its world and ruleset resolved.</summary>
    public static ResolvedScenario Toy => LazyToy.Value;

    /// <summary>
    /// Plays <paramref name="turnCount"/> seat-turns forward from the toy scenario's initial state,
    /// through every system this build registers (<see cref="SystemRegistry.FromEngineAssembly"/>) —
    /// the human seat passing with no orders, the AI seat deciding its own, exactly as
    /// <see cref="Presentation.GameSession.Submit"/>'s "end" command drives play turn by turn.
    /// </summary>
    public static GameState PlayTurns(int turnCount)
    {
        var toy = Toy;
        var registry = SystemRegistry.FromEngineAssembly();
        var dispatcher = new CommandDispatcher(registry, toy.Ruleset, toy.World, NullEventSink.Instance);
        var coordinator = new TurnCoordinator(registry, toy.Ruleset, toy.World, NullEventSink.Instance, dispatcher);

        var state = GameStateFactory.CreateInitial(toy.World, toy.Ruleset, toy.Scenario);
        for (var i = 0; i < turnCount; i++)
        {
            state = coordinator.RunTurn(state).State;
        }

        return state;
    }
}
