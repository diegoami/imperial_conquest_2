using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;

namespace IC2.Engine.Presentation;

/// <summary>What established an ordered display boundary.</summary>
public enum StrategicTravelStepKind { Command, System, BattleOpening, BattleCompletion, Reconciliation }

/// <summary>One immutable display boundary; the state references share existing immutable engine data.</summary>
public sealed record StrategicTravelStep(
    long StepId, long ActionId, long LapId, StrategicTravelStepKind Kind, string Identity,
    string ViewerNationId, GameState Before, GameState After,
    ValueList<StrategicMarkerSnapshot> MarkersBefore, ValueList<StrategicMarkerSnapshot> MarkersAfter,
    ValueList<StrategicTravelObservation> Routes, ValueList<DomainEvent> Events);

/// <summary>Transient session-local delivery. Consume once by BatchId; never stored in a save.</summary>
public sealed record StrategicTravelBatch(long BatchId, ValueList<StrategicTravelStep> Steps)
{
    public static StrategicTravelBatch Empty { get; } = new(0, ValueList<StrategicTravelStep>.Empty);
}

// All entry points contain presentation failures. A disabled collector releases its references and
// becomes inert, without changing a command, event, seed or the existing authoritative AI replay.
internal sealed class StrategicTravelTimeline : IStrategicObservationRecorder
{
    private sealed record Entry(string Identity, object? Command, StrategicTravelStepKind Kind,
        GameState Before, GameState After, ValueList<StrategicTravelObservation> Routes,
        ValueList<DomainEvent> Events, long ActionId);

    private readonly List<StrategicTravelStep> _pending = new();
    private List<Entry> _prefix = new();
    private object? _owner;
    private bool _attempt;
    private int _cursor;
    private long _stepId, _actionId, _lapId, _batchId;
    private GameState? _display;
    private string? _viewer;
    private bool _enabled = true;
    private List<Entry>? _checkpoint;
    private int _pendingCheckpoint;
    private GameState? _displayCheckpoint;

    internal bool Enabled => _enabled;
    internal void Guard(Action action)
    {
        if (!_enabled) return;
        try { action(); } catch { Disable(); }
    }

    public void Disable()
    {
        _enabled = false;
        Release();
        _pending.Clear();
        _checkpoint = null;
        _display = _displayCheckpoint = null;
    }

    internal void BeginSubmit(string viewer)
    {
        Guard(() =>
        {
            _viewer = viewer;
            _checkpoint = new(_prefix);
            _pendingCheckpoint = _pending.Count;
            _displayCheckpoint = _display;
        });
    }

    internal void Rollback()
    {
        Guard(() =>
        {
            _pending.Clear();
            _prefix = _checkpoint ?? new();
            _display = _displayCheckpoint;
            _checkpoint = null;
            _attempt = false;
        });
    }

    internal void BeginAttempt(object lap)
    {
        Guard(() =>
        {
            if (!ReferenceEquals(_owner, lap))
            {
                Release();
                _owner = lap;
                _lapId++;
            }
            _cursor = 0;
            _attempt = true;
        });
    }

    internal void EndAttempt(bool completed)
    {
        Guard(() =>
        {
            if (_cursor != _prefix.Count) throw new InvalidOperationException("Strategic replay ended before its recorded prefix.");
            _attempt = false;
            if (completed) Release();
        });
    }

    internal void Battle(ICommand command, GameState before, GameState after, bool completed,
        IEnumerable<DomainEvent>? events = null)
    {
        Guard(() => Record(new(command.Kind, command,
            completed ? StrategicTravelStepKind.BattleCompletion : StrategicTravelStepKind.BattleOpening,
            before, after, ValueList<StrategicTravelObservation>.Empty,
            events is null ? ValueList<DomainEvent>.Empty : ValueList.From(events), 0)));
    }

    public void Accepted(ICommand command, GameState before, GameState after,
        ValueList<StrategicTravelObservation> routes, ValueList<DomainEvent> events) =>
        Guard(() => Record(new(command.Kind, command, StrategicTravelStepKind.Command, before, after, routes, events, 0)));

    public void CompletedSystem(string systemId, TurnPhase phase, GameState before, GameState after) =>
        Guard(() => Record(new($"{systemId}@{phase}", null, StrategicTravelStepKind.System,
            before, after, ValueList<StrategicTravelObservation>.Empty, ValueList<DomainEvent>.Empty, 0)));

    private void Record(Entry entry)
    {
        if (_attempt && _cursor < _prefix.Count)
        {
            var previous = _prefix[_cursor++];
            var battle = previous.Kind is StrategicTravelStepKind.BattleOpening or StrategicTravelStepKind.BattleCompletion;
            if (previous.Identity != entry.Identity || !Equals(previous.Command, entry.Command)
                || !previous.Before.Equals(entry.Before) || !previous.Routes.Equals(entry.Routes)
                || (!battle && (previous.Kind != entry.Kind || !previous.After.Equals(entry.After) || !previous.Events.Equals(entry.Events))))
                throw new InvalidOperationException("Strategic replay diverged from its recorded prefix.");
            if (battle && entry.Kind == StrategicTravelStepKind.BattleCompletion)
            {
                if (previous.Kind == StrategicTravelStepKind.BattleCompletion)
                {
                    if (!previous.After.Equals(entry.After) || !previous.Events.Equals(entry.Events))
                        throw new InvalidOperationException("Strategic battle result changed during replay.");
                    return;
                }
                entry = entry with { ActionId = previous.ActionId };
                _prefix[_cursor - 1] = entry;
                Emit(entry);
            }
            return;
        }
        entry = entry with { ActionId = ++_actionId };
        if (_attempt) { _prefix.Add(entry); _cursor++; }
        Emit(entry);
    }

    private void Emit(Entry entry)
    {
        // A system's input predates any commands it ran. Its reconciliation must start at the
        // most recent displayed boundary rather than rewinding to that old system input.
        var before = entry.Kind == StrategicTravelStepKind.System ? _display ?? entry.Before : entry.Before;
        var after = entry.After;
        _display = after;
        if (entry.Kind == StrategicTravelStepKind.System && before.Equals(after)) return;
        _pending.Add(new(++_stepId, entry.ActionId, _attempt ? _lapId : 0, entry.Kind, entry.Identity,
            _viewer ?? entry.Before.ActiveNationId, before, after, Markers(before), Markers(after), entry.Routes, entry.Events));
    }

    internal void Reconcile(string identity, GameState before, GameState after)
    {
        Guard(() => Record(new(identity, null, StrategicTravelStepKind.Reconciliation, before, after,
            ValueList<StrategicTravelObservation>.Empty, ValueList<DomainEvent>.Empty, 0)));
    }

    internal StrategicTravelBatch Complete(GameState state, bool suspended, bool closed)
    {
        StrategicTravelBatch result = StrategicTravelBatch.Empty;
        Guard(() =>
        {
            if (!suspended && _display is { } before && !before.Equals(state)) Reconcile("submit", before, state);
            result = new(++_batchId, ValueList.From(_pending));
            _pending.Clear();
            _checkpoint = null;
            _displayCheckpoint = null;
            // Published batches now own their own references. Only an active replay retains a prefix.
            if (!suspended) _display = null;
            if (closed) { Release(); _display = null; }
        });
        return result;
    }

    private void Release()
    {
        _prefix.Clear();
        _owner = null;
        _attempt = false;
        _cursor = 0;
    }

    private static ValueList<StrategicMarkerSnapshot> Markers(GameState state) => ValueList.From(
        state.Armies.Select(a => new StrategicMarkerSnapshot(StrategicMarkerKind.Army, a.Id, a.Nation,
            new(a.X, a.Y), a.TotalTroops, a.CoveredTileCode is not null && !a.IsEmbarked, a.AboardFleetId, null))
        .Concat(state.Fleets.Select(f => new StrategicMarkerSnapshot(StrategicMarkerKind.Fleet, f.Id, f.Nation,
            new(f.X, f.Y), f.Ships, !f.IsUnderConstruction, null, f.CarriedArmyId)))
        .Concat(state.Cities.Select(c => new StrategicMarkerSnapshot(StrategicMarkerKind.City, c.Id, c.Owner,
            new(c.X, c.Y), c.PopulationThousands, true, null, null))));
}
