using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Tests.Core;

/// <summary>
/// A stand-in for T22's AI seat: a system that plays its turn by <em>issuing commands</em> rather than by
/// writing to the state.
/// </summary>
/// <remarks>
/// This is the shape the AI has to have if an AI seat is to be held to the same rules as a human one. It
/// exists in T03 because the seam it needs — <see cref="SystemContext.Commands"/> — would otherwise be
/// something T22 had to invent, and inventing it late means either a cross-task edit to
/// <see cref="SystemContext"/> or an AI that quietly bypasses command validation.
/// </remarks>
[TestFixtureGroup(CommandFixtures.Group)]
[GameSystem(TurnPhase.Orders, "test.seat-agent")]
public sealed class SeatAgentSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        var levy = context.Ruleset.Economy.PurseCapPerUnit;
        var result = context.Commands.Dispatch(
            context.State, new LevyCommand(context.ActiveNationId, levy));

        if (result.IsRejected)
        {
            context.Events.Publish(new TestSeatAgentRefused(result.Rejection!.Code.Value));
        }

        return result.State;
    }
}

/// <summary>An AI seat's order was refused.</summary>
[DomainEvent("test.seat-agent-refused")]
public sealed record TestSeatAgentRefused(string Code) : DomainEvent;
