using IC2.Engine.Model;

namespace IC2.Engine.Core;

/// <summary>
/// What <see cref="CommandDispatcher.Dispatch"/> returns. Always carries a usable state, so a caller can
/// thread it forward without branching first.
/// </summary>
/// <remarks>
/// An illegal command produces one of these with <see cref="Rejection"/> set — it never throws, and it
/// never silently does nothing. <see cref="State"/> on a rejection is the state that went in, unchanged
/// and reference-identical, which is the assertion this task's Definition of Done item 3 makes.
/// </remarks>
public sealed record CommandResult
{
    private CommandResult(
        GameState state,
        CommandRejection? rejection,
        string commandKind,
        ValueList<DomainEvent> events)
    {
        State = state;
        Rejection = rejection;
        CommandKind = commandKind;
        Events = events;
    }

    /// <summary>
    /// The state after the command. On a rejection this is the very object that was passed in.
    /// </summary>
    public GameState State { get; }

    /// <summary>The refusal, or <see langword="null"/> when the command was accepted.</summary>
    public CommandRejection? Rejection { get; }

    /// <summary>The <see cref="ICommand.Kind"/> of the command this result answers.</summary>
    public string CommandKind { get; }

    /// <summary>
    /// Everything the handler published while it ran, in order. Also delivered live to the sink the
    /// dispatcher was built with; this copy is here so a caller that only wants "what did that command
    /// do" does not have to own a sink.
    /// </summary>
    public ValueList<DomainEvent> Events { get; }

    /// <summary>Whether the command was accepted.</summary>
    public bool IsAccepted => Rejection is null;

    /// <summary>Whether the command was refused.</summary>
    public bool IsRejected => Rejection is not null;

    /// <summary>The rejection code, or <see langword="null"/> when the command was accepted.</summary>
    public RejectionCode? Code => Rejection?.Code;

    internal static CommandResult Accepted(GameState state, string commandKind, ValueList<DomainEvent> events) =>
        new(state, null, commandKind, events);

    internal static CommandResult Rejected(GameState unchangedState, string commandKind, CommandRejection rejection) =>
        new(unchangedState, rejection, commandKind, ValueList<DomainEvent>.Empty);

    /// <inheritdoc/>
    public override string ToString() =>
        Rejection is null ? $"{CommandKind}: accepted" : $"{CommandKind}: rejected ({Rejection})";
}
