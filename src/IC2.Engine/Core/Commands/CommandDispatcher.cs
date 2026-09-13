using IC2.Engine.Model;

namespace IC2.Engine.Core;

/// <summary>
/// The dispatching half of the command seam, as a system sees it.
/// </summary>
/// <remarks>
/// Separated from <see cref="CommandDispatcher"/> so that a system running inside the pipeline can issue
/// commands without the coordinator having to know how they are routed. The case this exists for is
/// T22's AI: an AI seat that edits the state directly would bypass every legality check a human seat goes
/// through, and would not appear in a replay log, so it issues the same commands a human does and gets
/// the same typed rejections back.
/// </remarks>
public interface ICommandDispatch
{
    /// <summary>
    /// Decides one command against one state, publishing an accepted command's events to the dispatcher's
    /// own sink. Never throws for an illegal order.
    /// </summary>
    CommandResult Dispatch(GameState state, ICommand command);

    /// <summary>
    /// Decides one command, publishing an accepted command's events to <paramref name="events"/> instead
    /// of to the dispatcher's own sink.
    /// </summary>
    /// <remarks>
    /// This overload exists so that a command issued from <em>inside</em> a turn lands in that turn's event
    /// stream. <see cref="TurnCoordinator"/> binds it to the run's sink before handing the dispatcher to a
    /// system, which is what makes <see cref="TurnResult.Events"/> mean "everything published during the
    /// run" even when some of it came from an order a system placed.
    /// </remarks>
    CommandResult Dispatch(GameState state, ICommand command, IEventSink events);
}

/// <summary>
/// The dispatcher a coordinator falls back to when none was supplied: it refuses everything, with a code
/// saying so, rather than throwing or silently doing nothing.
/// </summary>
public sealed class UnavailableCommandDispatch : ICommandDispatch
{
    /// <summary>The shared instance; the type is stateless.</summary>
    public static UnavailableCommandDispatch Instance { get; } = new();

    private UnavailableCommandDispatch()
    {
    }

    /// <inheritdoc/>
    public CommandResult Dispatch(GameState state, ICommand command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);

        return CommandResult.Rejected(state, command.Kind, new CommandRejection(
            CoreRejections.NoDispatcher,
            "This coordinator was built without a command dispatcher, so systems cannot issue commands. "
            + "Pass one to the TurnCoordinator constructor."));
    }

    /// <inheritdoc/>
    public CommandResult Dispatch(GameState state, ICommand command, IEventSink events)
    {
        ArgumentNullException.ThrowIfNull(events);
        return Dispatch(state, command);
    }
}

/// <summary>
/// Routes a command to its registered handler and turns the answer into a <see cref="CommandResult"/>.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The contract, stated once so twenty later tasks inherit it.</strong> An illegal command never
/// throws and never silently does nothing: it comes back as a <see cref="CommandResult"/> carrying a
/// <see cref="RejectionCode"/>, with the state it went in with, unchanged and reference-identical, and
/// with nothing published to the event sink. That last part matters — a handler that publishes an event
/// and then refuses would otherwise leave a line in the news log about something that did not happen, so
/// a handler's events are buffered and only released once it accepts.
/// </para>
/// <para>
/// <strong>What does still throw</strong>, deliberately: a structurally invalid command — a null, a blank
/// <see cref="ICommand.Kind"/>, a blank issuing nation. Those are a caller's contract violation, not a
/// move the rules disallow, and turning a programming error into a polite rejection would hide it.
/// </para>
/// </remarks>
public sealed class CommandDispatcher : ICommandDispatch
{
    private readonly SystemRegistry _registry;
    private readonly Ruleset _ruleset;
    private readonly World _world;
    private readonly IEventSink _sink;

    /// <summary>Creates a dispatcher over a registry's command handlers.</summary>
    /// <param name="registry">The scanned registry supplying handlers.</param>
    /// <param name="ruleset">The loaded ruleset handed to every handler.</param>
    /// <param name="world">The loaded world handed to every handler.</param>
    /// <param name="sink">
    /// Where accepted commands' events are delivered. Pass the same sink the
    /// <see cref="TurnCoordinator"/> was built with so the two streams interleave in real order.
    /// </param>
    public CommandDispatcher(SystemRegistry registry, Ruleset ruleset, World world, IEventSink sink)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(sink);

        _registry = registry;
        _ruleset = ruleset;
        _world = world;
        _sink = sink;
    }

    /// <summary>Decides one command against one state, publishing to this dispatcher's own sink.</summary>
    /// <param name="state">The state to decide against.</param>
    /// <param name="command">The request.</param>
    /// <returns>
    /// An accepted result carrying the new state, or a rejected one carrying a code and
    /// <paramref name="state"/> itself.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="state"/> or <paramref name="command"/> is null.</exception>
    /// <exception cref="ArgumentException">The command's kind or issuing nation id is blank.</exception>
    public CommandResult Dispatch(GameState state, ICommand command) => Dispatch(state, command, _sink);

    /// <inheritdoc cref="Dispatch(GameState, ICommand)"/>
    /// <param name="state">The state to decide against.</param>
    /// <param name="command">The request.</param>
    /// <param name="events">Where an accepted command's events are published.</param>
    public CommandResult Dispatch(GameState state, ICommand command, IEventSink events)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.Kind, $"{nameof(command)}.{nameof(ICommand.Kind)}");
        ArgumentException.ThrowIfNullOrWhiteSpace(
            command.IssuingNationId, $"{nameof(command)}.{nameof(ICommand.IssuingNationId)}");

        var kind = command.Kind;

        // Exact-type lookup, not assignability: a handler claims one command type, and a command type
        // has one handler. Anything looser would make "which handler runs" depend on the order the scan
        // happened to see two candidates in.
        if (_registry.HandlerFor(command.GetType()) is not { } registration)
        {
            return CommandResult.Rejected(state, kind, new CommandRejection(
                CoreRejections.NoHandler,
                $"No handler is registered for command '{kind}' ({command.GetType().FullName})."));
        }

        if (state.NationById(command.IssuingNationId) is not { } issuer)
        {
            return CommandResult.Rejected(state, kind, new CommandRejection(
                CoreRejections.UnknownNation,
                $"Command '{kind}' was issued by '{command.IssuingNationId}', which is not a nation in this game."));
        }

        if (issuer.Eliminated)
        {
            return CommandResult.Rejected(state, kind, new CommandRejection(
                CoreRejections.NationEliminated,
                $"Command '{kind}' was issued by '{issuer.Id}', which has been eliminated."));
        }

        // Checked here rather than in every handler: "did you remember the seat check?" is a whole class
        // of bug that should not be re-fought in twenty tasks.
        if (!string.Equals(issuer.Id, state.ActiveNationId, StringComparison.Ordinal))
        {
            return CommandResult.Rejected(state, kind, new CommandRejection(
                CoreRejections.NotActiveSeat,
                $"Command '{kind}' was issued by '{issuer.Id}', but it is '{state.ActiveNationId}'s turn."));
        }

        var buffer = new RecordingEventSink();
        var context = new CommandContext(
            state,
            _ruleset,
            _world,
            command,
            SplitMix64Rng.ForStream(state.RandomSeed, StreamNameFor(kind)),
            buffer);

        var outcome = registration.Instance.Handle(command, context);

        if (outcome.Rejection is { } rejection)
        {
            // The buffer is dropped on the floor: a refused command leaves no trace in the news log, for
            // the same reason it leaves none in the state.
            return CommandResult.Rejected(state, kind, rejection);
        }

        var accepted = outcome.AcceptedState
                       ?? throw new InvalidOperationException(
                           $"Handler '{registration.ImplementationType.FullName}' accepted command '{kind}' "
                           + "without returning a state.");

        // One of the two points where the engine's single persisted random value moves; see RngStreams.
        // It happens after the handler has run, so the handler's own stream was derived from the value
        // the command was issued against, and the next command sees a different one.
        var next = accepted with { RandomSeed = RngStreams.Advance(accepted.RandomSeed) };

        var published = buffer.Events.ToArray();
        foreach (var domainEvent in published)
        {
            events.Publish(domainEvent);
        }

        return CommandResult.Accepted(next, kind, ValueList<DomainEvent>.Of(published));
    }

    /// <summary>
    /// The random stream name a command of this kind draws from. Prefixed so a command kind and a system
    /// id that happen to read the same never share a stream.
    /// </summary>
    public static string StreamNameFor(string commandKind) => $"command:{commandKind}";
}
