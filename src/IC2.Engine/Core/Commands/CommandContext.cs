using IC2.Engine.Model;

namespace IC2.Engine.Core;

/// <summary>Everything a command handler may read while it decides.</summary>
/// <remarks>
/// Shaped like <see cref="SystemContext"/> on purpose — a later task writing a handler and a system on
/// the same afternoon should not have to learn two vocabularies. The differences are the two things a
/// command has and a phase does not: the issuing nation, resolved, and no round-tick signal, because a
/// command cannot end a round.
/// </remarks>
public sealed class CommandContext
{
    internal CommandContext(
        GameState state,
        Ruleset ruleset,
        World world,
        ICommand command,
        IRng rng,
        IEventSink events)
    {
        State = state;
        Ruleset = ruleset;
        World = world;
        Command = command;
        Rng = rng;
        Events = events;
    }

    /// <summary>The state the command is being decided against.</summary>
    public GameState State { get; }

    /// <summary>The loaded ruleset: the source of every gameplay number.</summary>
    public Ruleset Ruleset { get; }

    /// <summary>The loaded world: terrain, and the static definitions behind the live state.</summary>
    public World World { get; }

    /// <summary>The command being handled, as the dispatcher received it.</summary>
    public ICommand Command { get; }

    /// <summary>
    /// This command's own random stream, derived from the state's seed and the command's kind. Two
    /// different command kinds issued against the same state draw independently.
    /// </summary>
    public IRng Rng { get; }

    /// <summary>
    /// Where the handler publishes domain events.
    /// </summary>
    /// <remarks>
    /// Buffered by the dispatcher, not wired straight through: events published by a handler that then
    /// rejects are discarded rather than delivered, so a refused command leaves no trace in the news log
    /// any more than it leaves one in the state.
    /// </remarks>
    public IEventSink Events { get; }

    /// <summary>The live state of the nation issuing the command. Never null: the dispatcher resolved it.</summary>
    public NationState IssuingNation => State.NationById(Command.IssuingNationId)!;
}
