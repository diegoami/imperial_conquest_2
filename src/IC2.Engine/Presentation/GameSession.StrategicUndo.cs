using IC2.Engine.Model;
using IC2.Engine.News;
using IC2.Engine.Victory;

namespace IC2.Engine.Presentation;

public sealed partial class GameSession
{
    private sealed record StrategicCheckpoint(long Id, int Generation, string Nation, int Turn,
        GameState Campaign, ValueList<ArmyTravelTarget> Targets, ValueList<RetreatSupplyPlan> Recovery,
        ValueList<NationRallyPoint> Rally, IReadOnlyList<NewsEntry>? NewsBaseline,
        Dictionary<string, PendingPeaceTreatyOffer> Peace, Dictionary<string, int> FallTreasuries,
        bool GameOver, bool SeatLost, GameWon? Win, string? SupportNation, int SupportTurn);

    private readonly Stack<StrategicCheckpoint> _strategicUndo = new();
    private StrategicCheckpoint? _strategicGroupBefore;
    private int _strategicGroupDepth, _strategicUndoGeneration;
    private long _strategicCheckpointId;
    private bool _strategicUndoEnabled;

    /// <summary>Session-only capability. CLI/replay defaults remain unchanged; no save member is added.</summary>
    public bool StrategicUndoEnabled
    {
        get => _strategicUndoEnabled;
        set { if (_strategicUndoEnabled == value) return; _strategicUndoEnabled = value; ClearStrategicUndo(); }
    }

    public int StrategicUndoCount => _strategicUndo.Count;
    public long StrategicUndoRevision { get; private set; }
    public bool CanUndoStrategic => StrategicUndoEnabled && !IsWatchModeActive && _battle is null && _lap is null
        && _strategicUndo.TryPeek(out var entry) && entry.Generation == _strategicUndoGeneration
        && entry.Nation == State.ActiveNationId && entry.Turn == State.Calendar.TurnIndex;

    // Presentation participants retain their own immutable selection/report checkpoints by this ID.
    // They never enter campaign serialization and cannot change the authoritative restore.
    public event Action<long>? StrategicCheckpointCaptured;
    public event Action<long>? StrategicCheckpointDiscarded;
    public event Action<long>? StrategicCheckpointRestored;
    public event Action? StrategicUndoCleared;

    private sealed class StrategicActionScope(GameSession? owner) : IDisposable
    {
        private GameSession? _owner = owner;
        public void Dispose() { var session = _owner; _owner = null; session?.EndStrategicUndoAction(); }
    }

    /// <summary>Group a UI transaction, its engine command and related metadata into one Undo action.</summary>
    public IDisposable BeginStrategicUndoAction()
    {
        if (_strategicGroupDepth > 0) { _strategicGroupDepth++; return new StrategicActionScope(this); }
        if (!StrategicUndoEnabled || _battle is not null || _lap is not null || IsWatchModeActive
            || State.NationById(State.ActiveNationId) is not { Control: SeatControl.Human, Eliminated: false })
            return new StrategicActionScope(null);
        _strategicGroupDepth = 1;
        _strategicGroupBefore = new(++_strategicCheckpointId, _strategicUndoGeneration,
            State.ActiveNationId, State.Calendar.TurnIndex, State, ArmyTravelTargets, RetreatSupplyPlans,
            NationRallyPoints, _pendingNewsBaseline?.ToArray(), new(_pendingPeaceTreatyOffers, StringComparer.Ordinal),
            new(_pendingFallTreasuries, StringComparer.Ordinal), _gameOver, _seatLost, _scenarioWin,
            _lastHumanSupportNation, _lastHumanSupportTurn);
        NotifyStrategic(StrategicCheckpointCaptured, _strategicGroupBefore.Id);
        return new StrategicActionScope(this);
    }

    private void EndStrategicUndoAction()
    {
        if (--_strategicGroupDepth != 0) return;
        var entry = _strategicGroupBefore;
        _strategicGroupBefore = null;
        if (entry is null) return;
        if (entry.Generation != _strategicUndoGeneration || entry.Nation != State.ActiveNationId
            || entry.Turn != State.Calendar.TurnIndex || Unchanged(entry))
        {
            NotifyStrategic(StrategicCheckpointDiscarded, entry.Id);
            return;
        }
        _strategicUndo.Push(entry);
    }

    private bool Unchanged(StrategicCheckpoint entry) => State == entry.Campaign
        && ArmyTravelTargets.SequenceEqual(entry.Targets) && RetreatSupplyPlans.SequenceEqual(entry.Recovery)
        && NationRallyPoints.SequenceEqual(entry.Rally) && _gameOver == entry.GameOver && _seatLost == entry.SeatLost
        && Equals(_scenarioWin, entry.Win) && SamePairs(_pendingPeaceTreatyOffers, entry.Peace)
        && SamePairs(_pendingFallTreasuries, entry.FallTreasuries);

    private static bool SamePairs<T>(Dictionary<string, T> left, Dictionary<string, T> right) =>
        left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var value)
            && EqualityComparer<T>.Default.Equals(pair.Value, value));

    public void ClearStrategicUndo()
    {
        _strategicUndo.Clear();
        _strategicGroupBefore = null;
        _strategicUndoGeneration++;
        if (StrategicUndoCleared is { } listeners)
            foreach (Action listener in listeners.GetInvocationList()) try { listener(); } catch { }
    }

    private IDisposable? StrategicUndoForCommand(string raw)
    {
        var verb = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
        return verb is null or "undo" or "battle" or "end" or "load" or "observe" or "observer-step" or "take-control"
            or "help" or "quit" or "save" or "status" or "armies" or "cities" or "map" or "news" or "balance"
            ? null : BeginStrategicUndoAction();
    }

    public SessionOutput StrategicUndo() => Submit("undo");

    private IReadOnlyList<string> UndoStrategicAction()
    {
        if (!CanUndoStrategic) return ["No strategic action from your current turn can be undone."];
        if (_strategicGroupBefore is { } abandoned) NotifyStrategic(StrategicCheckpointDiscarded, abandoned.Id);
        _strategicGroupBefore = null; // Undo is never itself retained by an enclosing UI transaction.
        var entry = _strategicUndo.Pop();
        State = entry.Campaign;
        ArmyTravelTargets = entry.Targets;
        RetreatSupplyPlans = entry.Recovery;
        NationRallyPoints = entry.Rally;
        _pendingNewsBaseline = entry.NewsBaseline;
        _pendingPeaceTreatyOffers.Clear();
        foreach (var pair in entry.Peace) _pendingPeaceTreatyOffers.Add(pair.Key, pair.Value);
        _pendingFallTreasuries.Clear();
        foreach (var pair in entry.FallTreasuries) _pendingFallTreasuries.Add(pair.Key, pair.Value);
        _gameOver = entry.GameOver;
        _seatLost = entry.SeatLost;
        _scenarioWin = entry.Win;
        _lastHumanSupportNation = entry.SupportNation;
        _lastHumanSupportTurn = entry.SupportTurn;
        _pendingPrelude = null;
        _pendingBattleResults.Clear(); _pendingSeatFalls.Clear(); _eventsThisCall.Clear();
        _pendingTacticalTimeline.Clear(); _pendingTacticalReport = null;
        LastBattles = Array.Empty<Battle.BattleResult>(); LastSeatFalls = Array.Empty<SeatFall>();
        LastTacticalTimeline = ValueList<TacticalBattleFrame>.Empty;
        LastDialogCommandResult = null;
        ResetTacticalUndo();
        _strategicTimeline.Reset(); LastStrategicTimeline = StrategicTravelBatch.Empty;
        StrategicUndoRevision++;
        NotifyStrategic(StrategicCheckpointRestored, entry.Id);
        return ["Your last strategic action was undone."];
    }

    private static void NotifyStrategic(Action<long>? listeners, long id)
    {
        if (listeners is null) return;
        foreach (Action<long> listener in listeners.GetInvocationList()) try { listener(id); } catch { }
    }
}
