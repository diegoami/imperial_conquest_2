using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Calendar;

/// <summary>
/// T119 (issue #602): the original's New Game turn-order shuffle, <c>FUN_00448AA4</c>
/// <strong>[confirmed: code, <c>2026-10-03-new-game-turn-order-shuffle.md</c>, research 871ca91]</strong>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The algorithm.</strong> Start from the state's order (the world's nation order). For
/// <c>i = 0 … n − 1</c> in ascending order, draw <c>j = Random(n)</c> and swap <c>order[i]</c> with
/// <c>order[j]</c>. The original's <c>n</c> is 16, so it always makes exactly 16 draws; a draw of
/// <c>j = i</c> swaps nothing. The first nation of the result is the active seat, at position 0.
/// </para>
/// <para>
/// <strong>Not Fisher-Yates, on purpose.</strong> <c>j</c> is drawn from every slot each time, not from
/// <c>i … n − 1</c>, so the <c>nⁿ</c> equally likely draw sequences do not spread evenly over the
/// <c>n!</c> orders. That bias is the original's and is kept.
/// </para>
/// <para>
/// <strong>The generator is the clone's own</strong> <strong>[designed: the user's decision of
/// 2026-10-03, algorithm parity and not seed-for-seed parity]</strong>:
/// <c>SplitMix64Rng.ForStream(state.RandomSeed, "new-game.turn-order")</c>. The cited research supports
/// the algorithm only. The stream name is <strong>[designed]</strong>: I searched design-audit.md,
/// docs/game-design.md, the shuffle report and the user's decisions of 2026-10-02/03 on #602 for any
/// stream name and found none, so it is this task's own. The <c>fixed</c>/<c>shuffle</c> preset split
/// is the user's decision of 2026-10-02 (PR #601), not a research finding. Deriving a stream does not
/// advance <c>RandomSeed</c>, so no other draw moves.
/// </para>
/// <para>
/// <strong>Called only where a game starts</strong> (<see cref="IC2.Engine.Presentation.GameSession"/>'s
/// New Game constructor and <see cref="IC2.Engine.Ai.AiGameRunner"/>), never on a resume.
/// <see cref="GameStateFactory.CreateInitial"/> stays the scenario's opening state in the world's order.
/// Gated by <see cref="RulesetFlags.NewGameTurnOrder"/>.
/// </para>
/// </remarks>
public static class NewGameTurnOrder
{
    /// <summary>The stream name the New Game shuffle derives from the state's root seed.</summary>
    public const string StreamName = "new-game.turn-order";

    [ThreadStatic]
    private static int _callsOnThisThread;

    /// <summary>
    /// How many times <see cref="Apply"/> has been called on the current thread. A test
    /// seam: a resume must leave it unchanged, which no state comparison can show for a discarded call.
    /// </summary>
    public static int CallsOnThisThread => _callsOnThisThread;

    /// <summary>
    /// Shuffles the state's turn order from its own seed when the ruleset says <c>shuffle</c>; returns the
    /// state unchanged under <c>fixed</c>.
    /// </summary>
    public static GameState Apply(GameState state, Ruleset ruleset)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(ruleset);
        _callsOnThisThread++;
        return ruleset.Flags.NewGameTurnOrder == NewGameTurnOrderPolicy.Shuffle
            ? Shuffle(state, SplitMix64Rng.ForStream(state.RandomSeed, StreamName))
            : state;
    }

    /// <summary>
    /// Applies the original's shuffle with <paramref name="stream"/>, whatever the ruleset says, and puts
    /// the active seat at position 0.
    /// </summary>
    public static GameState Shuffle(GameState state, IRng stream)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(stream);

        var order = state.TurnOrder.ToArray();
        var n = order.Length;
        for (var i = 0; i < n; i++)
        {
            var j = stream.NextInt(n);
            (order[i], order[j]) = (order[j], order[i]);
        }

        return state with { TurnOrder = ValueList.From(order), ActiveSeatIndex = 0 };
    }
}
