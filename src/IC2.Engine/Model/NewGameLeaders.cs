using IC2.Engine.Core;

namespace IC2.Engine.Model;

/// <summary>
/// T146: the original's New Game leader draw — <c>FUN_00448aa4</c>'s
/// <c>for (n = 0; n &lt; 16; n++) leader[n] = pool[n][Random(12)]</c>, confirmed by
/// <c>2026-10-03-new-game-turn-order-shuffle.md</c> (research <c>871ca91</c>) §4 and its code excerpt.
/// </summary>
/// <remarks>
/// <para>
/// <strong>One draw per nation that carries a pool, in the world's nation order.</strong> The classical
/// world's 16 nations each carry a 12-name pool, so a New Game makes exactly 16 draws, each with bound
/// 12. A nation without a pool (the toy and example worlds) is left with its fixed
/// <see cref="NationDefinition.LeaderName"/> and draws nothing, so those worlds are unchanged.
/// </para>
/// <para>
/// <strong>Its own stream.</strong> The draws come from
/// <c>SplitMix64Rng.ForStream(state.RandomSeed, "new-game.leaders")</c> — a name of this task's own,
/// after the turn-order stream the same report describes — so the draw does not move any other
/// seeded outcome. Algorithm parity with the original, not seed-for-seed parity.
/// </para>
/// <para>
/// <strong>Called only where a game starts.</strong> <see cref="IC2.Engine.Presentation.GameSession"/>'s
/// New Game constructor and <see cref="IC2.Engine.Ai.AiGameRunner"/> call this;
/// <see cref="GameStateFactory.CreateInitial"/>
/// stays draw-free, so a resume or an original-save import never redraws.
/// </para>
/// </remarks>
public static class NewGameLeaders
{
    /// <summary>The stream name the New Game draw derives from the state's root seed.</summary>
    public const string StreamName = "new-game.leaders";

    /// <summary>Draws every pool-backed nation's New Game leader from the state's own seed.</summary>
    public static GameState Apply(GameState state, World world)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        return Apply(state, world, SplitMix64Rng.ForStream(state.RandomSeed, StreamName));
    }

    /// <summary>Draws every pool-backed nation's New Game leader from <paramref name="stream"/>.</summary>
    /// <param name="state">The starting state, before any seat has played.</param>
    /// <param name="world">The world carrying each nation's <see cref="NationDefinition.LeaderNames"/>.</param>
    /// <param name="stream">The draw's own stream; a nation without a pool consumes none of it.</param>
    public static GameState Apply(GameState state, World world, IRng stream)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(stream);

        var updated = new List<NationState>(state.Nations.Count);
        foreach (var nation in state.Nations)
        {
            var pool = world.NationById(nation.Id)?.LeaderNames;
            updated.Add(pool is null
                ? nation
                : nation with { LeaderName = pool[stream.NextInt(pool.Count)] });
        }

        return state with { Nations = ValueList.From(updated) };
    }
}
