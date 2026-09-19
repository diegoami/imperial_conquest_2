using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Ai;

/// <summary>
/// What one AI seat decided, published so that a harness outside the pipeline can write it to a log
/// without the AI having to own a file, a clock or a sink of its own.
/// </summary>
/// <remarks>
/// <strong>Not news-worthy.</strong> This is instrumentation, not narration: the news log carries what
/// happened in the world (a city fell, an army was destroyed), and the individual commands the AI issued
/// already publish those through their own handlers. A rendered line per AI decision would drown the
/// original's own news format, which <c>news-log-format-and-messages.md</c> pins exactly.
/// <c>docs/task-catalogue.md</c> T22 Done-when 4 asks for per-seed logs "<em>as a test artifact</em>",
/// and this event is how the turn's decisions get out to the test that writes them.
/// </remarks>
/// <param name="NationId">The seat that acted.</param>
/// <param name="Lines">One line per decision, in order.</param>
/// <param name="CommandsIssued">How many commands were dispatched.</param>
/// <param name="CommandsRejected">How many came back refused. Required to be zero.</param>
/// <param name="ProjectionMismatches">How many attacks were abandoned after their declaration landed. Required to be zero.</param>
/// <param name="HitActionCap">Whether the turn stopped at <see cref="AiWeights.MaxActionsPerTurn"/>.</param>
[DomainEvent("ai.turn-decided")]
public sealed record AiTurnDecided(
    string NationId,
    ValueList<string> Lines,
    int CommandsIssued,
    int CommandsRejected,
    int ProjectionMismatches,
    bool HitActionCap) : DomainEvent;

/// <summary>
/// The AI seat, registered into <see cref="TurnPhase.Orders"/> — the phase whose own declaration names
/// "<em>T22 AI</em>" as a consumer and describes it as "<em>where commands are issued and where an AI
/// seat decides</em>".
/// </summary>
/// <remarks>
/// <para>
/// <strong>This class is a wrapper and nothing else.</strong> Every decision is
/// <see cref="AiTurn.Run"/>'s, which takes plain arguments rather than a <see cref="SystemContext"/> so
/// that a test can drive one AI turn over a hand-built state without standing up a pipeline — the same
/// shape <see cref="Diplomacy.PendingOfferSystem.Apply"/> already uses, and for the same reason.
/// </para>
/// <para>
/// <strong>It runs for every seat and returns immediately for most of them.</strong> A system registered
/// in a seat-scoped phase runs on every seat's turn; the seat check lives inside
/// <see cref="AiTurn.Run"/> so that there is exactly one place that decides "is this an AI seat", and so
/// that a human seat's turn is a documented no-op rather than an absence.
/// </para>
/// <para>
/// <strong>Order within the phase.</strong> The default (<c>Order = 0</c>, ties broken on the ordinal
/// system id) is deliberate: nothing else registers into <see cref="TurnPhase.Orders"/> today — the phase
/// is empty, as T54's own entry notes — so there is no ordering relationship to declare, and inventing a
/// number would assert one that does not exist.
/// </para>
/// </remarks>
[GameSystem(TurnPhase.Orders, "ai.turn")]
public sealed class AiTurnSystem : IGameSystem
{
    /// <inheritdoc/>
    public GameState Execute(SystemContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var outcome = AiTurn.Run(
            context.State, context.Ruleset, context.World, context.Commands, context.Rng, context.Events);

        context.Events.Publish(new AiTurnDecided(
            outcome.NationId,
            ValueList.From(outcome.Log),
            outcome.CommandsIssued,
            outcome.CommandsRejected,
            outcome.ProjectionMismatches,
            outcome.HitActionCap));

        return outcome.State;
    }
}
