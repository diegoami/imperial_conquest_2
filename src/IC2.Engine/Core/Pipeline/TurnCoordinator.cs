using IC2.Engine.Model;

namespace IC2.Engine.Core;

/// <summary>One system's run, recorded so a caller can see what the pipeline actually did.</summary>
/// <param name="SystemId">The system's declared id.</param>
/// <param name="Phase">The phase it ran in.</param>
public sealed record SystemExecution(string SystemId, TurnPhase Phase);

/// <summary>The outcome of running the pipeline once.</summary>
/// <param name="State">The state after every system has run.</param>
/// <param name="Events">Everything published during the run, in order.</param>
/// <param name="Trace">Which systems ran, in the order they ran.</param>
/// <param name="RoundTickRan">Whether the round-scoped phases ran as part of this turn.</param>
public sealed record TurnResult(
    GameState State,
    ValueList<DomainEvent> Events,
    ValueList<SystemExecution> Trace,
    bool RoundTickRan);

/// <summary>
/// Runs the declared phase pipeline over a <see cref="GameState"/>, threading the state through every
/// registered system in order, and owns the <c>OnQuarterBoundary</c> hook.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The coordinator holds no game state.</strong> Every method takes a state and returns one; the
/// caller (T23's harness, the Godot UI, a test) owns the current state. That keeps save/load a pure
/// serialization problem and keeps replay honest — there is nothing to forget to persist.
/// </para>
/// <para>
/// <strong>Two entry points, because a turn and a round are different things.</strong> The original runs
/// each nation's turn separately and then, after every sixteenth turn, runs one global tick over every
/// city, army and fleet in the game
/// (<see href="https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-turn-and-calendar-sequencing.md">
/// decompiled-turn-and-calendar-sequencing.md</see>). <see cref="RunTurn"/> runs the seat-scoped phases
/// and then the round-scoped ones only if a system asked for them through
/// <see cref="TurnSignals.RequestRoundTick"/>; <see cref="RunRoundTick"/> runs the round-scoped phases on
/// their own. The decision of <em>when</em> a round has completed belongs to T06's seat rotation, not
/// here — which is also why the round phases are reachable with no calendar merged at all.
/// </para>
/// </remarks>
public sealed class TurnCoordinator
{
    private readonly SystemRegistry _registry;
    private readonly Ruleset _ruleset;
    private readonly World _world;
    private readonly IEventSink _sink;
    private readonly ICommandDispatch _commands;

    /// <summary>Creates a coordinator over a scanned registry.</summary>
    /// <param name="registry">The registry supplying systems and quarter-boundary subscribers.</param>
    /// <param name="ruleset">The loaded ruleset handed to every system.</param>
    /// <param name="world">The loaded world handed to every system.</param>
    /// <param name="sink">
    /// Where events are published as they happen. Pass <see cref="NullEventSink.Instance"/> if only
    /// <see cref="TurnResult.Events"/> is wanted; the result carries its own copy either way.
    /// </param>
    /// <param name="commands">
    /// The dispatcher systems may issue commands through — build a <see cref="CommandDispatcher"/> over
    /// the same registry and pass it here. Optional, because most systems do not need one; when it is
    /// absent, a system that tries anyway is refused with
    /// <see cref="CoreRejections.NoDispatcher"/> rather than throwing.
    /// </param>
    public TurnCoordinator(
        SystemRegistry registry,
        Ruleset ruleset,
        World world,
        IEventSink sink,
        ICommandDispatch? commands = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(sink);

        _registry = registry;
        _ruleset = ruleset;
        _world = world;
        _sink = sink;
        _commands = commands ?? UnavailableCommandDispatch.Instance;
    }

    /// <summary>
    /// Runs one seat's turn: every seat-scoped phase in declared order, followed by the round-scoped
    /// phases if a system signalled that the round completed.
    /// </summary>
    /// <param name="state">The state at the start of the seat's turn.</param>
    public TurnResult RunTurn(GameState state) =>
        Run(state, TurnPhases.SeatScoped, mayFollowOnToRoundPhases: true);

    /// <summary>
    /// Runs the round-scoped phases on their own — the original's global weekly tick. Callable directly,
    /// so a system that needs the tick can be tested before the calendar that would normally trigger it
    /// exists.
    /// </summary>
    /// <param name="state">The state at the moment the round completes.</param>
    public TurnResult RunRoundTick(GameState state) =>
        Run(state, TurnPhases.RoundScoped, mayFollowOnToRoundPhases: false);

    /// <summary>
    /// Fires the <c>OnQuarterBoundary</c> hook directly: every registered subscriber runs, in declared
    /// order, with the state threaded through them.
    /// </summary>
    /// <param name="state">The state at the moment of the boundary.</param>
    /// <param name="endingSeasonIndex">
    /// The zero-based index of the season that is <em>finishing</em>. See
    /// <see cref="IQuarterBoundaryHandler"/> for why the ending season is the one passed.
    /// </param>
    /// <returns>The state after every subscriber has run; with no subscribers, <paramref name="state"/>.</returns>
    /// <remarks>
    /// This does not advance the root random value and does not run any phase. It is the same call T06's
    /// calendar makes from inside <see cref="TurnPhase.CalendarAdvance"/> through
    /// <see cref="SystemContext.QuarterBoundary"/>, exposed so that a subscriber — T08's quarterly
    /// billing, T19's thaw — is testable with no calendar implementation present.
    /// </remarks>
    public GameState FireQuarterBoundary(GameState state, int endingSeasonIndex)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new QuarterBoundaryHook(this, state.RandomSeed, _sink).Fire(state, endingSeasonIndex);
    }

    /// <summary>
    /// The random stream a system draws from in a given phase. Includes the phase so that a system
    /// registered in two phases draws independently in each.
    /// </summary>
    public static string StreamNameFor(string systemId, TurnPhase phase) => $"system:{systemId}@{phase}";

    /// <summary>The random stream a quarter-boundary subscriber draws from.</summary>
    public static string QuarterStreamNameFor(string handlerId) => $"quarter:{handlerId}";

    /// <summary>
    /// Moves the engine's one persisted random value on by a step. Done once per pipeline run, so two
    /// turns never replay the same rolls, while the number of systems and the number of draws inside them
    /// stay irrelevant to it. See <see cref="RngStreams"/>.
    /// </summary>
    private static GameState AdvanceRootSeed(GameState state) =>
        state with { RandomSeed = RngStreams.Advance(state.RandomSeed) };

    /// <summary>
    /// One pipeline run: advance the root value once, run the given phases, and optionally follow on to
    /// the round-scoped ones if a system asked for them.
    /// </summary>
    /// <param name="state">The state to run against.</param>
    /// <param name="phases">The phases to run, in declared order.</param>
    /// <param name="mayFollowOnToRoundPhases">
    /// <see langword="true"/> for a seat's turn, which goes on to the round-scoped phases when the round
    /// completes; <see langword="false"/> when the caller <em>is</em> running the round tick.
    /// </param>
    private TurnResult Run(GameState state, IReadOnlyList<TurnPhase> phases, bool mayFollowOnToRoundPhases)
    {
        ArgumentNullException.ThrowIfNull(state);

        var recorder = new RecordingEventSink();
        var events = new CompositeEventSink(recorder, _sink);
        var signals = new TurnSignals();
        var trace = new List<SystemExecution>();

        var current = AdvanceRootSeed(state);
        var turnSeed = current.RandomSeed;

        current = RunPhases(current, turnSeed, phases, events, signals, trace);

        // A direct round tick is itself the round tick, so it reports one without being signalled.
        var roundTickRan = !mayFollowOnToRoundPhases;
        if (mayFollowOnToRoundPhases && signals.RoundTickRequested)
        {
            current = RunPhases(current, turnSeed, TurnPhases.RoundScoped, events, signals, trace);
            roundTickRan = true;
        }

        return new TurnResult(current, ValueList.From(recorder.Events), ValueList.From(trace), roundTickRan);
    }

    private GameState RunPhases(
        GameState state,
        ulong turnSeed,
        IReadOnlyList<TurnPhase> phases,
        IEventSink events,
        TurnSignals signals,
        List<SystemExecution> trace)
    {
        var current = state;
        foreach (var phase in phases)
        {
            foreach (var system in _registry.InPhase(phase))
            {
                // Streams are derived from the seed captured at the start of the run, never from the
                // state a previous system returned. A system that writes to RandomSeed (it should not)
                // therefore cannot shift anybody else's rolls.
                var context = new SystemContext(
                    current,
                    _ruleset,
                    _world,
                    phase,
                    system.Id,
                    SplitMix64Rng.ForStream(turnSeed, StreamNameFor(system.Id, phase)),
                    events,
                    new QuarterBoundaryHook(this, turnSeed, events),
                    _commands,
                    signals);

                GameState? produced = system.Instance.Execute(context);
                if (produced is null)
                {
                    throw new InvalidOperationException(
                        $"System '{system.Id}' ({system.ImplementationType.FullName}) returned no state. "
                        + "Return the context's own state to do nothing.");
                }

                current = produced;
                trace.Add(new SystemExecution(system.Id, phase));
            }
        }

        return current;
    }

    /// <summary>
    /// The firing end of the quarter-boundary hook, bound to one run's seed and event sink.
    /// </summary>
    private sealed class QuarterBoundaryHook : IQuarterBoundaryHook
    {
        private readonly TurnCoordinator _coordinator;
        private readonly ulong _seed;
        private readonly IEventSink _events;

        public QuarterBoundaryHook(TurnCoordinator coordinator, ulong seed, IEventSink events)
        {
            _coordinator = coordinator;
            _seed = seed;
            _events = events;
        }

        public GameState Fire(GameState state, int endingSeasonIndex)
        {
            ArgumentNullException.ThrowIfNull(state);

            var current = state;
            foreach (var handler in _coordinator._registry.QuarterBoundaryHandlers)
            {
                // The ending season is folded into the stream so that the four boundaries of one year
                // draw independently even where the root seed has not moved between them.
                var rng = new SplitMix64Rng(
                    RngStreams.DeriveSeed(_seed, QuarterStreamNameFor(handler.Id), endingSeasonIndex));

                var context = new QuarterBoundaryContext(
                    current,
                    _coordinator._ruleset,
                    _coordinator._world,
                    endingSeasonIndex,
                    rng,
                    _events);

                GameState? produced = handler.Instance.OnQuarterBoundary(context);
                if (produced is null)
                {
                    throw new InvalidOperationException(
                        $"Quarter-boundary handler '{handler.Id}' "
                        + $"({handler.ImplementationType.FullName}) returned no state.");
                }

                current = produced;
            }

            return current;
        }
    }
}
