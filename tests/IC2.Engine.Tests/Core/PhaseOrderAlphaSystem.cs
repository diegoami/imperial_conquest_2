using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Tests.Core;

/// <summary>
/// A no-op system, registered by attribute alone.
/// </summary>
/// <remarks>
/// This file is the entire act of adding a system to the pipeline: no list, no wiring, no shared file.
/// It is declared in a <em>later</em> phase than the two systems in the files beside it, so the order it
/// actually runs in comes from <see cref="TurnPhase"/>'s declared order rather than from the name of
/// this file or the order the reflection scan happened to return types in.
/// </remarks>
[TestFixtureGroup("phase-order")]
[GameSystem(TurnPhase.SeatEnd, "test.phase-order.alpha")]
public sealed class PhaseOrderAlphaSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context) => context.State;
}
