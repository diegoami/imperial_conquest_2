using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Tests.Core;

/// <summary>
/// Two systems in the same phase declaring the <em>same</em> <see cref="GameSystemAttribute.Order"/>, so
/// that only the id tie-break can decide which runs first.
/// </summary>
/// <remarks>
/// <para>
/// They live in their own group, and in one file rather than beside the phase-order fixtures, so that
/// exercising the tie-break does not disturb the execution trace this task's Definition of Done item 2
/// asserts exactly.
/// </para>
/// <para>
/// Note the deliberate inversion: <c>zulu</c> is declared first in this file and <c>aardvark</c> second,
/// so declaration order, file order and id order do not agree. Only the id ordering produces the expected
/// result, which is what makes the tie-break assertion load-bearing rather than incidentally true.
/// </para>
/// </remarks>
public static class OrderTieBreakFixtures
{
    /// <summary>The fixture group these systems belong to.</summary>
    public const string Group = "order-tie-break";
}

/// <summary>Declared first, runs second.</summary>
[TestFixtureGroup(OrderTieBreakFixtures.Group)]
[GameSystem(TurnPhase.SeatStart, "test.tie-break.zulu", Order = 50)]
public sealed class TieBreakZuluSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context) => context.State;
}

/// <summary>Declared second, runs first.</summary>
[TestFixtureGroup(OrderTieBreakFixtures.Group)]
[GameSystem(TurnPhase.SeatStart, "test.tie-break.aardvark", Order = 50)]
public sealed class TieBreakAardvarkSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context) => context.State;
}
