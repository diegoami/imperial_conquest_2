using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Tests.Core;

/// <summary>
/// A third no-op system in a third file, sharing a phase with the beta system but declaring a lower
/// <see cref="GameSystemAttribute.Order"/>, so it runs first within that phase.
/// </summary>
[TestFixtureGroup("phase-order")]
[GameSystem(TurnPhase.SeatStart, "test.phase-order.gamma", Order = 10)]
public sealed class PhaseOrderGammaSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context) => context.State;
}
