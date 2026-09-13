using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Tests.Core;

/// <summary>
/// A system declaring a <see cref="TurnPhase"/> value that does not exist.
/// </summary>
/// <remarks>
/// C# does not constrain an enum argument to the enum's declared members, so <c>(TurnPhase)0</c> compiles
/// happily. In its own group, because building a registry that contains it is supposed to throw.
/// </remarks>
public static class UndeclaredPhaseFixture
{
    /// <summary>The fixture group this system belongs to.</summary>
    public const string Group = "undeclared-phase";
}

/// <summary>Declares a phase outside <see cref="TurnPhases.InOrder"/>.</summary>
[TestFixtureGroup(UndeclaredPhaseFixture.Group)]
[GameSystem((TurnPhase)0, "test.undeclared.phase")]
public sealed class UndeclaredPhaseSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context) => context.State;
}
