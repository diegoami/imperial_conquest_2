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
    /// Plays <paramref name="seatTurnCount"/> seat-turns forward from the toy scenario's initial state,
    /// through every system this build registers (<see cref="SystemRegistry.FromEngineAssembly"/>) —
    /// the human seat passing with no orders, the AI seat deciding its own, exactly as
    /// <see cref="Presentation.GameSession.Submit"/>'s "end" command drives play turn by turn.
    /// </summary>
    /// <remarks>
    /// Named in <em>seat</em>-turns deliberately: <see cref="TurnCoordinator.RunTurn"/> runs one seat's
    /// turn, not one calendar turn (review round 1, B1) — <see cref="CalendarState.TurnIndex"/> advances
    /// once per full round, which on this two-seat toy scenario is two calls to this method's loop. A
    /// caller that needs a specific <em>calendar</em> turn reached wants <see cref="PlayUntilTurnIndex"/>
    /// instead.
    /// </remarks>
    public static GameState PlayTurns(int seatTurnCount)
    {
        var coordinator = BuildCoordinator();
        var state = InitialState();
        for (var i = 0; i < seatTurnCount; i++)
        {
            state = coordinator.RunTurn(state).State;
        }

        return state;
    }

    /// <summary>
    /// Plays seat-turns forward from the toy scenario's initial state until
    /// <see cref="CalendarState.TurnIndex"/> reaches at least <paramref name="minTurnIndex"/> — what
    /// Done-when 1's "N turns" actually means (a calendar turn, not a seat-turn; review round 1, B1).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// <paramref name="minTurnIndex"/> was not reached within <paramref name="seatTurnGuard"/> seat-turns.
    /// A stall this deep into the toy scenario is a defect in the played systems, not a reason to loop
    /// forever or to weaken this method's guarantee — a caller that hits this should stop and report it
    /// (build-process.md §4.6), not raise the guard to make the failure go away.
    /// </exception>
    public static GameState PlayUntilTurnIndex(int minTurnIndex, int seatTurnGuard = 500)
    {
        var coordinator = BuildCoordinator();
        var state = InitialState();
        var seatTurns = 0;
        while (state.Calendar.TurnIndex < minTurnIndex)
        {
            if (seatTurns >= seatTurnGuard)
            {
                throw new InvalidOperationException(
                    $"The toy scenario did not reach calendar turn {minTurnIndex} within {seatTurnGuard} "
                    + $"seat-turns (stopped at turn {state.Calendar.TurnIndex}).");
            }

            state = coordinator.RunTurn(state).State;
            seatTurns++;
        }

        return state;
    }

    private static GameState InitialState()
    {
        var toy = Toy;
        return GameStateFactory.CreateInitial(toy.World, toy.Ruleset, toy.Scenario);
    }

    private static TurnCoordinator BuildCoordinator()
    {
        var toy = Toy;
        var registry = SystemRegistry.FromEngineAssembly();
        var dispatcher = new CommandDispatcher(registry, toy.Ruleset, toy.World, NullEventSink.Instance);
        return new TurnCoordinator(registry, toy.Ruleset, toy.World, NullEventSink.Instance, dispatcher);
    }
}
