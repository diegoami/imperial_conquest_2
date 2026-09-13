using IC2.Engine.Model;

namespace IC2.Engine.Core;

/// <summary>
/// What a handler returns: the state it produced, or a typed refusal.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from <see cref="CommandResult"/> on purpose. A handler can only express one of two things —
/// "here is the new state" or "no, because" — and it is structurally unable to hand back a rejection
/// <em>and</em> a modified state. The dispatcher then builds the <see cref="CommandResult"/>, filling the
/// unchanged input state in on the rejection path itself. That is what makes this task's Definition of
/// Done item 3 ("no state changed") a property of the seam rather than of each of twenty handlers
/// remembering to be careful.
/// </para>
/// </remarks>
public readonly record struct CommandOutcome
{
    private CommandOutcome(GameState? state, CommandRejection? rejection)
    {
        AcceptedState = state;
        Rejection = rejection;
    }

    /// <summary>The state the handler produced, or <see langword="null"/> when it refused.</summary>
    public GameState? AcceptedState { get; }

    /// <summary>The refusal, or <see langword="null"/> when the command was accepted.</summary>
    public CommandRejection? Rejection { get; }

    /// <summary>Whether the handler accepted the command.</summary>
    public bool IsAccepted => Rejection is null;

    /// <summary>Accepts the command, with the state it produced.</summary>
    /// <param name="state">The resulting state. Returning the context's own state is a legal no-op.</param>
    public static CommandOutcome Accept(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new CommandOutcome(state, null);
    }

    /// <summary>Refuses the command.</summary>
    /// <param name="code">The machine-readable reason.</param>
    /// <param name="message">
    /// A human-readable explanation naming the actual values involved, for a log or a UI. Never the
    /// thing a caller branches on — that is <paramref name="code"/>.
    /// </param>
    public static CommandOutcome Reject(RejectionCode code, string message) =>
        new(null, new CommandRejection(code, message));
}

/// <summary>A refusal: the code a caller branches on, and the sentence a human reads.</summary>
/// <param name="Code">The stable, machine-readable reason.</param>
/// <param name="Message">A human-readable explanation. Not a contract; the code is.</param>
public sealed record CommandRejection(RejectionCode Code, string Message)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Code}: {Message}";
}
