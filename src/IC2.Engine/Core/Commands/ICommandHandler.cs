namespace IC2.Engine.Core;

/// <summary>
/// The non-generic face of a command handler, used by <see cref="CommandDispatcher"/> so that dispatch
/// is a dictionary lookup and a virtual call rather than reflection per command.
/// </summary>
/// <remarks>
/// Implement <see cref="ICommandHandler{TCommand}"/>, not this. Its default implementations supply
/// both members here.
/// </remarks>
public interface ICommandHandler
{
    /// <summary>The command type this handler answers.</summary>
    Type CommandType { get; }

    /// <summary>Handles a command already known to be of <see cref="CommandType"/>.</summary>
    CommandOutcome Handle(ICommand command, CommandContext context);
}

/// <summary>
/// The rule behind one command type.
/// </summary>
/// <remarks>
/// <para>
/// Registered by <see cref="CommandHandlerAttribute"/> and found by assembly scan, so a task adding a
/// command adds two files inside its own directory and edits nothing shared.
/// </para>
/// <para>
/// Handlers are stateless and are instantiated once, for the same reason systems are: an instance field
/// would be game state the save file does not contain.
/// </para>
/// </remarks>
/// <typeparam name="TCommand">The command type handled.</typeparam>
public interface ICommandHandler<in TCommand> : ICommandHandler
    where TCommand : ICommand
{
    /// <summary>
    /// Decides the command: returns <see cref="CommandOutcome.Accept"/> with the resulting state, or
    /// <see cref="CommandOutcome.Reject"/> with a typed reason. It must not throw for an illegal
    /// request — illegality is an outcome, not an error.
    /// </summary>
    CommandOutcome Handle(TCommand command, CommandContext context);

    /// <inheritdoc/>
    Type ICommandHandler.CommandType => typeof(TCommand);

    /// <inheritdoc/>
    CommandOutcome ICommandHandler.Handle(ICommand command, CommandContext context) =>
        Handle((TCommand)command, context);
}

/// <summary>
/// Marks an <see cref="ICommandHandler{TCommand}"/> implementation for assembly-scanned registration.
/// </summary>
/// <remarks>
/// Carries no arguments: the command type is already in the interface's type argument, and there is no
/// ordering to declare because exactly one handler may claim a command type. Two handlers claiming the
/// same command is a hard failure when the registry is built, not a last-one-wins surprise at runtime.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class CommandHandlerAttribute : Attribute
{
}
