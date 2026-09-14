using IC2.Engine.Model;

namespace IC2.Engine.Core;

/// <summary>
/// Everything a system may read while it runs, and the two channels through which it may speak: the
/// returned state, and <see cref="Events"/>.
/// </summary>
/// <remarks>
/// Deliberately wide from day one. The T02 review's lesson applies to seams as much as to models: a
/// context that later has to be widened forces every already-written system to be touched, which is
/// exactly the cross-task edit the Owns lists exist to prevent. <see cref="World"/> is here because
/// movement and battle need terrain; <see cref="Phase"/> is here because a system that logs, traces or
/// publishes an event should be able to say which phase it was in without hard-coding its own declaration.
/// </remarks>
public sealed class SystemContext
{
    internal SystemContext(
        GameState state,
        Ruleset ruleset,
        World world,
        TurnPhase phase,
        string systemId,
        IRng rng,
        IEventSink events,
        IQuarterBoundaryHook quarterBoundary,
        ICommandDispatch commands,
        TurnSignals signals,
        ValueList<PublishedEvent> publishedEvents)
    {
        State = state;
        Ruleset = ruleset;
        World = world;
        Phase = phase;
        SystemId = systemId;
        Rng = rng;
        Events = events;
        QuarterBoundary = quarterBoundary;
        Commands = commands;
        Signals = signals;
        PublishedEvents = publishedEvents;
    }

    /// <summary>The state as the previous system in the pipeline left it.</summary>
    public GameState State { get; }

    /// <summary>The loaded ruleset: the source of every gameplay number, never a C# literal.</summary>
    public Ruleset Ruleset { get; }

    /// <summary>The loaded world: terrain, and the static definitions behind the live state.</summary>
    public World World { get; }

    /// <summary>The phase currently running.</summary>
    public TurnPhase Phase { get; }

    /// <summary>The running system's declared id, which is also the name of <see cref="Rng"/>'s stream.</summary>
    public string SystemId { get; }

    /// <summary>
    /// This system's own random stream for this phase of this turn. Independent of every other system's,
    /// so the number of draws made here can never move anybody else's rolls — see <see cref="RngStreams"/>.
    /// </summary>
    public IRng Rng { get; }

    /// <summary>Where the system publishes domain events for the news log and the UI.</summary>
    public IEventSink Events { get; }

    /// <summary>
    /// The quarter-boundary hook. Only the calendar (T06) is expected to fire it, from inside
    /// <see cref="TurnPhase.CalendarAdvance"/> and before it advances the season counter.
    /// </summary>
    public IQuarterBoundaryHook QuarterBoundary { get; }

    /// <summary>
    /// Lets a system issue commands instead of editing the state directly.
    /// </summary>
    /// <remarks>
    /// The seat that needs this is the AI one (T22): an AI that wrote to the state directly would skip
    /// every legality check a human seat's order goes through, and would leave no command log for a
    /// replay to follow. If the coordinator was built without a dispatcher, every call is refused with
    /// <see cref="CoreRejections.NoDispatcher"/> rather than throwing.
    /// </remarks>
    public ICommandDispatch Commands { get; }

    /// <summary>What this system can ask the coordinator to do once the phase finishes.</summary>
    public TurnSignals Signals { get; }

    /// <summary>
    /// Everything published earlier in the current run, in publication order, each tagged with the phase
    /// it was published in and the id of the system that published it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The T40 seam: a stateless system that needs to see what earlier systems in the same run already
    /// published reads this, instead of keeping state of its own, reaching into another sink's private
    /// list by reflection, or sharing a mutable static. T10's news writer is the motivating case: running
    /// in <see cref="TurnPhase.SeatEnd"/> it sees the events of <see cref="TurnPhase.SeatStart"/>,
    /// <see cref="TurnPhase.Orders"/> and the systems before it in <see cref="TurnPhase.SeatEnd"/>;
    /// running in <see cref="TurnPhase.RoundEnd"/> it sees every round-scoped phase's events, and — when
    /// <see cref="TurnCoordinator.RunTurn"/> follows a seat's turn on into the round tick — the
    /// seat-scoped phases' events too, told apart from the round-scoped ones by their <see cref="PublishedEvent.Phase"/> tag.
    /// </para>
    /// <para>
    /// Covers events published directly by an earlier system, by a command that system issued through
    /// <see cref="Commands"/>, and by a quarter-boundary handler a system's own phase fired through
    /// <see cref="QuarterBoundary"/> — all three publish through the same run-bound sink, so all three are
    /// recorded here. It never includes anything <em>this</em> system is about to publish during its own
    /// <see cref="IGameSystem.Execute"/>: the view is a snapshot taken before this system runs, not a live
    /// one. A fresh run starts this list empty; nothing carries over from one turn to the next.
    /// </para>
    /// </remarks>
    public ValueList<PublishedEvent> PublishedEvents { get; }

    /// <summary>The nation id whose seat is currently active.</summary>
    public string ActiveNationId => State.ActiveNationId;

    /// <summary>The active seat's live nation state, or <see langword="null"/> if the id does not resolve.</summary>
    public NationState? ActiveNation => State.NationById(State.ActiveNationId);
}

/// <summary>
/// The one thing a system can tell the coordinator that is not expressible as a state change: that the
/// round of seats just completed and the global tick is due.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately an explicit signal rather than something the coordinator infers. Seat rotation is T06's
/// rule — it follows the save's 16-entry turn-order table — so the coordinator must not reimplement it
/// by watching <see cref="GameState.ActiveSeatIndex"/> wrap. Inference would also have meant that with
/// no calendar merged, the round phases could never run and no wave-3 task could test them.
/// </para>
/// <para>
/// This object is per turn, not per system, and is deliberately not part of
/// <see cref="GameState"/>: it carries no information that survives the turn it was raised in.
/// </para>
/// </remarks>
public sealed class TurnSignals
{
    /// <summary>Whether a system has asked for the round-scoped phases to run after this turn.</summary>
    public bool RoundTickRequested { get; private set; }

    /// <summary>
    /// Declares that the round of seats has completed, so <see cref="TurnCoordinator.RunTurn"/> should go
    /// on to the round-scoped phases. Raising it twice in one turn is the same as raising it once.
    /// </summary>
    public void RequestRoundTick() => RoundTickRequested = true;
}
