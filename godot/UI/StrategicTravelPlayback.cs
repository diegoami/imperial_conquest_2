using TravelMarkerKind = IC2.Engine.Movement.StrategicMarkerKind;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>One presentation owner for accepted strategic traces. Never used as command state.</summary>
public sealed class StrategicTravelPlayback
{
    // [designed] candidate pacing, pending exact-HEAD visual approval; not original-game timing.
    public const double CandidateCellSeconds = 0.12;
    private readonly Queue<StrategicTravelStep> _steps = new();
    private StrategicTravelStep? _step;
    private StrategicTravelObservation? _route;
    private GameState? _display;
    private string? _viewer;
    private int _routeIndex, _cellIndex;
    private double _elapsed;
    private long _lastBatch;
    private Action? _completed;
    private bool _closed, _draining, _retainArrival;

    public double CellSeconds { get; set; } = CandidateCellSeconds;
    public bool Paused { get; set; }
    public bool Fast { get; set; }
    public bool IsActive => _step is not null || _steps.Count > 0;
    public bool CommandsAllowed => !_closed && !IsActive;
    public GameState? DisplayState => _display;
    public GameState Resolve(GameState live, string viewer) => _display ?? ForViewer(live, viewer);
    public StrategicTravelStep? CurrentStep => _step;
    public StrategicTravelObservation? CurrentRoute => _route;
    public event Action? Changed;
    public event Action<string, GridPoint>? CellEntered;
    public event Action<StrategicTravelStep>? BoundaryCompleted;

    /// <summary>Existing remaining-moves policy plus invalid/hidden identity cleanup; foreign inspection remains allowed.</summary>
    public static bool SelectionSurvives(GameState state, TravelMarkerKind kind, string id) => kind switch
    {
        TravelMarkerKind.Army => state.ArmyById(id) is { Moves: > 0, IsEmbarked: false },
        TravelMarkerKind.Fleet => state.FleetById(id) is { Moves: > 0, IsUnderConstruction: false },
        _ => state.CityById(id) is not null,
    };

    /// <summary>False consumes a wholly offscreen route immediately; camera remains user-owned.</summary>
    public Func<StrategicTravelObservation, bool>? IntersectsViewport { get; set; }

    public bool Begin(StrategicTravelBatch batch, string viewer, Action completed, bool retainArrival = false)
    {
        if (_closed || batch.BatchId <= _lastBatch || IsActive) return false;
        _lastBatch = batch.BatchId;
        _retainArrival = retainArrival;
        _display = null;
        _viewer = viewer;
        _completed = completed;
        foreach (var step in batch.Steps) _steps.Enqueue(step);
        Prepare();
        return true;
    }

    public void Advance(double seconds)
    {
        if (_closed || Paused || !IsActive || _draining) return;
        var remaining = double.IsFinite(seconds) ? Math.Max(0, seconds) * (Fast ? 4 : 1) : 0;
        while (IsActive)
        {
            if (_route is null) { Prepare(); continue; }
            var duration = double.IsFinite(CellSeconds) ? Math.Max(0, CellSeconds) : CandidateCellSeconds;
            if (IntersectsViewport?.Invoke(_route) == false) duration = 0;
            var needed = Math.Max(0, duration - _elapsed);
            if (remaining + 1e-12 < needed)
            {
                _elapsed += remaining;
                Changed?.Invoke();
                return;
            }
            remaining = Math.Max(0, remaining - needed);
            EnterCell();
            Prepare();
            if (remaining == 0 && duration > 0 && _route is not null) return;
        }
    }

    public void Skip()
    {
        if (_closed || _draining) return;
        _draining = true;
        try
        {
            while (IsActive)
            {
                if (_route is not null) EnterCell();
                Prepare();
            }
        }
        finally { _draining = false; }
    }

    /// <summary>A suspended AI replay owns its arrived boundary until the retained completion reconciles it.</summary>
    public void ReleaseArrival()
    {
        if (IsActive) return;
        _display = null;
        _retainArrival = false;
        Changed?.Invoke();
    }

    public void Close()
    {
        _closed = true;
        _retainArrival = false;
        _steps.Clear();
        _step = null; _route = null; _display = null; _completed = null; _viewer = null;
        IntersectsViewport = null;
    }

    /// <summary>Fractional centre only; every information view uses the last fully entered cell.</summary>
    public (double X, double Y)? Position(TravelMarkerKind kind, string id)
    {
        if (_route is not { } route || route.Kind != kind || route.EntityId != id) return null;
        var from = _cellIndex == 0 ? route.Origin : route.EnteredCells[_cellIndex - 1];
        var to = route.EnteredCells[_cellIndex];
        var t = CellSeconds > 0 ? Math.Clamp(_elapsed / CellSeconds, 0, 1) : 0;
        return (from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t);
    }

    private void Prepare()
    {
        while (!_closed)
        {
            if (_step is null)
            {
                if (!_steps.TryDequeue(out _step))
                {
                    if (!_retainArrival) _display = null;
                    _route = null;
                    var completed = _completed;
                    _completed = null; _viewer = null;
                    Changed?.Invoke();
                    completed?.Invoke();
                    return;
                }
                _routeIndex = 0;
                _display = ForViewer(_step.Before);
                Changed?.Invoke();
            }
            if (_route is not null) return;
            if (_routeIndex < _step.Routes.Count)
            {
                var route = _step.Routes[_routeIndex++];
                if (route.EnteredCells.Count == 0 || !_step.MarkersBefore.Any(m =>
                    m.Kind == route.Kind && m.EntityId == route.EntityId && m.Visible)) continue;
                _route = route; _cellIndex = 0; _elapsed = 0;
                Changed?.Invoke();
                return;
            }
            var done = _step;
            _display = ForViewer(done.After);
            _step = null;
            Changed?.Invoke();
            BoundaryCompleted?.Invoke(done);
        }
    }

    private GameState ForViewer(GameState state) => ForViewer(state, _viewer!);

    private static GameState ForViewer(GameState state, string viewer)
    {
        var index = state.TurnOrder.ToList().IndexOf(viewer);
        return index >= 0 && index != state.ActiveSeatIndex ? state with { ActiveSeatIndex = index } : state;
    }

    private void EnterCell()
    {
        var route = _route!;
        var cell = route.EnteredCells[_cellIndex++];
        _display = _display! with
        {
            Armies = ValueList.From(_display!.Armies.Select(a =>
                (route.Kind == TravelMarkerKind.Army && a.Id == route.EntityId)
                || (route.Kind == TravelMarkerKind.Fleet && a.AboardFleetId == route.EntityId)
                    ? a with { X = cell.X, Y = cell.Y } : a)),
            Fleets = ValueList.From(_display.Fleets.Select(f => route.Kind == TravelMarkerKind.Fleet && f.Id == route.EntityId
                ? f with { X = cell.X, Y = cell.Y } : f)),
        };
        _elapsed = 0;
        if (_cellIndex == route.EnteredCells.Count) _route = null;
        CellEntered?.Invoke(route.EntityId, cell);
        Changed?.Invoke();
    }
}
