using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Tests.Core;

/// <summary>
/// The second no-op system, added in a second file. Nothing outside this file changed to add it — which
/// is the property <c>docs/build-orchestration-plan.md</c> §2.3 needs and this task's Definition of Done
/// item 2 asserts.
/// </summary>
/// <remarks>
/// Declared with a higher <see cref="GameSystemAttribute.Order"/> than the gamma system in the file
/// beside it, in the same phase, so the within-phase ordering is exercised too.
/// </remarks>
[TestFixtureGroup("phase-order")]
[GameSystem(TurnPhase.SeatStart, "test.phase-order.beta", Order = 20)]
public sealed class PhaseOrderBetaSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context) => context.State;
}
