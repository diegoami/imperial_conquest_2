using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Serialization;
using Xunit;
using ModelTestPaths = IC2.Engine.Tests.Model.TestPaths;

namespace IC2.Engine.Tests.Ai;

/// <summary>
/// The end-to-end reproduction of issue
/// <see href="https://github.com/diegoami/imperial_conquest_2/issues/907">#907</see>:
/// "Gaul's army parks next to Rome's Pisae at war and never besieges, attacks or moves on."
/// </summary>
/// <remarks>
/// <para>
/// <strong>Why the test replays a specific board position.</strong> The bug is positional: Gaul's
/// army-9 must reach <c>(92,34)</c> for Tarquinii's Bresenham path to have its first step
/// (<c>(93,35)</c>) on Pisae's tile. Reaching that tile by replay costs seven seat turns
/// (Gaul is seat 6 <c>turnOrder[6]</c>) on the default seed, which is slow and brittle;
/// the test instead places the army at <c>(92,34)</c> directly, on the off-the-shelf
/// initial state, so the only thing left to play is one AI turn.
/// </para>
/// <para>
/// <strong>Before the fix, the assertion failed.</strong> The AI re-proposes the inert
/// march at Tarquinii turn after turn, <see cref="AiTurn.Run"/> ends on "changed nothing
/// substantive", and the army stays at <c>(92,34)</c>. After the fix
/// <see cref="AiMilitaryPhase.IsProposableMove"/> rejects the candidate on
/// path[1]-occupancy grounds and the AI tries the next-best city; Caere (<c>(99,42)</c>) at
/// Chebyshev distance 8 wins the score and the army moves one turn's moves closer to it.
/// </para>
/// </remarks>
public sealed class AiIssue907GaulBlockedMarchTests
{
    /// <summary>
    /// Gaul's army-9 sits one tile from Pisae. Before the fix the AI never moves it
    /// (a fleet issue from the soak: "1 order issued" hides the stall); after the fix the
    /// AI picks the next-best enemy city, and the army moves within one turn.
    /// </summary>
    [Fact]
    public void Gaul_army_9_at_Pisaes_borders_moves_within_one_turn()
    {
        var shipped = GameDataRepository.Load(ModelTestPaths.DataRoot).Resolve("classical-mediterranean");
        var scenario = shipped.Scenario;
        var world = shipped.World;
        var ruleset = shipped.Ruleset;

        // Off-the-shelf initial state, then put army-9 exactly where the bug put it --
        // (92,34), the tile Bresenham lands on first when marching at Tarquinii (98,39).
        var initialState = GameStateFactory.CreateInitial(world, ruleset, scenario) with
        {
            RandomSeed = 1UL,
        };

        var parked = initialState with
        {
            Armies = ValueList.From(initialState.Armies.Select(a =>
                string.Equals(a.Id, "army-9", StringComparison.Ordinal)
                    ? a with { X = 92, Y = 34, Moves = 8 }
                    : a)),
        };

        // The bug fires on Gaul's turn; flip the active seat to Gaul.
        var activeSeatGaul = WithActiveSeat(parked, "gaul");

        var registry = SystemRegistry.FromEngineAssembly();
        var sink = NullEventSink.Instance;
        var dispatcher = new CommandDispatcher(registry, ruleset, world, sink);
        var coordinator = new TurnCoordinator(registry, ruleset, world, sink, dispatcher);

        var afterTurn = coordinator.RunTurn(activeSeatGaul);
        var after = afterTurn.State;

        var army9After = after.Armies.First(a => string.Equals(a.Id, "army-9", StringComparison.Ordinal));

        Assert.False(
            army9After.X == 92 && army9After.Y == 34,
            $"army-9 should have moved out of (92,34); it stayed at ({army9After.X},{army9After.Y}).");

        // Sanity: the engine did not turn on the user. The fix changes AI behaviour only,
        // and a correction would be flagged by the soak's "0 rejected commands" assertion
        // (which runs for every turn in the same fixture).
        Assert.DoesNotContain(
            afterTurn.Events.OfType<AiTurnDecided>(),
            d => d.CommandsRejected > 0);
    }

    private static GameState WithActiveSeat(GameState state, string nationId)
    {
        for (var i = 0; i < state.TurnOrder.Count; i++)
        {
            if (string.Equals(state.TurnOrder[i], nationId, StringComparison.Ordinal))
            {
                return state with { ActiveSeatIndex = i };
            }
        }

        throw new ArgumentException($"'{nationId}' is not in the turn order.", nameof(nationId));
    }
}
