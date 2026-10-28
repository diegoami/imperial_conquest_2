using IC2.Engine.Core;
using IC2.Engine.Model;

namespace IC2.Engine.Presentation;

public enum DialogEditKind { Army, Diplomacy, Fleet, Recruitment }

public sealed record DialogEditResult(bool Accepted, SessionOutput Output);

/// <summary>Transient GUI edits. Production commands run on a private session; no save/bootstrap path is used.</summary>
public sealed class DialogCommandDraft
{
    private readonly GameSession _owner;
    private readonly GameState _baseState;
    private GameSession _preview;
    private readonly List<string> _commands = new();
    private bool _finished;

    internal DialogCommandDraft(GameSession owner, DialogEditKind kind)
    {
        _owner = owner;
        Kind = kind;
        _baseState = owner.State;
        _preview = owner.CopyForDialog(includePending: false);
    }

    public DialogEditKind Kind { get; }
    public GameState WorkingState => _preview.State;
    public IReadOnlyList<string> Commands => _commands.AsReadOnly();
    public bool HasChanges => _commands.Count > 0;

    public DialogEditResult Stage(string line) => StageMany(new[] { line });

    public DialogEditResult StageMany(IEnumerable<string> lines) => ReplacePlan(_commands.Concat(lines).ToArray());

    /// <summary>Validate a complete replacement before changing even the private working copy.</summary>
    public DialogEditResult ReplacePlan(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        if (_finished) return Refuse("These edits have already been closed.");
        if (_owner.State != _baseState) return Refuse("The campaign changed. Reopen this dialog.");
        if (_owner.DialogEditUnavailable is { } unavailable) return Refuse(unavailable);
        var plan = lines.ToArray();
        var candidate = _owner.CopyForDialog(includePending: false);
        var output = new SessionOutput(Array.Empty<string>(), false);
        foreach (var line in plan)
        {
            if (!Allowed(line, Kind)) return Refuse("This action is not available in this editing dialog.");
            output = candidate.Submit(line);
            if (candidate.LastDialogCommandResult?.IsAccepted != true) return new(false, output);
        }
        _commands.Clear(); _commands.AddRange(plan);
        _preview = candidate;
        return new(true, output);
    }

    public DialogEditResult Commit(string? finalCommand = null)
    {
        if (_finished) return Refuse("These edits have already been closed.");
        var plan = finalCommand is null ? _commands.ToArray() : _commands.Append(finalCommand).ToArray();
        return _owner.CommitDialog(this, _baseState, plan);
    }

    public void Cancel()
    {
        _commands.Clear();
        _preview = _owner.CopyForDialog(includePending: false);
        _finished = true;
    }

    internal void Complete(GameSession final, IReadOnlyList<string> commands)
    {
        _preview = final;
        _commands.Clear(); _commands.AddRange(commands);
        _finished = true;
    }

    internal static DialogEditResult Refuse(string reason) =>
        new(false, new SessionOutput(new[] { $"Edits not applied: {reason}" }, false));

    internal static bool Allowed(string line, DialogEditKind kind)
    {
        var verb = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToLowerInvariant();
        return kind switch
        {
            DialogEditKind.Army => verb is "rename-unit" or "split-unit" or "join-units" or "disband-unit" or "army-transfer" or "split-army",
            DialogEditKind.Diplomacy => verb is "declare-war" or "make-peace" or "propose-trade" or "propose-alliance" or "accept-offer",
            DialogEditKind.Fleet => verb is "fleet-transfer" or "split-fleet",
            DialogEditKind.Recruitment => verb is "mobilize" or "disband-slot",
            _ => false,
        };
    }
}

public sealed partial class GameSession
{
    internal CommandResult? LastDialogCommandResult { get; private set; }

    public DialogCommandDraft BeginDialogEdit(DialogEditKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        return new(this, kind);
    }

    internal string? DialogEditUnavailable =>
        IsGameOver || IsWatchModeActive || State.NationById(State.ActiveNationId)?.Control != SeatControl.Human
            || _battle is not null || _pendingTacticalReport is not null || _lap is not null
            ? "Finish the current battle or turn transition first." : null;

    // Only the bounded strategic edit verbs above may use this copy. They do not
    // advance turns or enter battles. Keep pending messages out of UI previews,
    // but copy them for the final rehearsal so the normal outputs are preserved.
    private GameSession(GameSession source, bool includePending)
    {
        World = source.World;
        Ruleset = source.Ruleset;
        Scenario = source.Scenario;
        State = source.State;
        _registry = source._registry;
        _dispatcher = new CommandDispatcher(_registry, Ruleset, World, NullEventSink.Instance) { StrategicObserver = _strategicTimeline };
        _coordinator = new TurnCoordinator(_registry, Ruleset, World, NullEventSink.Instance, _dispatcher, Scenario) { StrategicObserver = _strategicTimeline };
        _humanSeatNationId = source._humanSeatNationId;
        _isWatchMode = source._isWatchMode;
        _seatLost = source._seatLost;
        _gameOver = source._gameOver;
        _scenarioWin = source._scenarioWin;
        HumanBattles = source.HumanBattles;
        ArmyTravelTargets = source.ArmyTravelTargets;
        RetreatSupplyPlans = source.RetreatSupplyPlans;
        NationRallyPoints = source.NationRallyPoints;
        _pendingNewsBaseline = source._pendingNewsBaseline?.ToArray();
        if (!includePending) return;
        _pendingPrelude = source._pendingPrelude?.ToList();
        foreach (var pair in source._pendingPeaceTreatyOffers) _pendingPeaceTreatyOffers.Add(pair.Key, pair.Value);
        foreach (var pair in source._pendingFallTreasuries) _pendingFallTreasuries.Add(pair.Key, pair.Value);
        _pendingBattleResults.AddRange(source._pendingBattleResults);
        _pendingSeatFalls.AddRange(source._pendingSeatFalls);
        _eventsThisCall.AddRange(source._eventsThisCall);
    }

    internal GameSession CopyForDialog(bool includePending) => new(this, includePending);

    internal DialogEditResult CommitDialog(DialogCommandDraft draft, GameState expected, IReadOnlyList<string> commands)
    {
        if (State != expected) return DialogCommandDraft.Refuse("The campaign changed. Reopen the dialog to edit its current state.");
        if (DialogEditUnavailable is { } unavailable) return DialogCommandDraft.Refuse(unavailable);
        if (commands.Count == 0)
        {
            draft.Complete(CopyForDialog(false), commands);
            return new(true, new SessionOutput(Array.Empty<string>(), false));
        }

        var trial = CopyForDialog(includePending: true);
        var events = new List<DomainEvent>();
        trial.EventsPublished += batch => events.AddRange(batch);
        var outputs = new List<SessionOutput>();
        foreach (var command in commands)
        {
            if (!DialogCommandDraft.Allowed(command, draft.Kind)) return DialogCommandDraft.Refuse("This action cannot be committed here.");
            var output = trial.Submit(command);
            if (trial.LastDialogCommandResult?.IsAccepted != true) return new(false, new SessionOutput(output.Lines, false));
            outputs.Add(output);
        }

        // No authoritative mutation occurs until every command has passed.
        // Adopt the validated state/bookkeeping once; don't replay a possibly
        // failing prefix against the live campaign or publish intermediate states.
        _strategicTimeline.BeginSubmit(_humanSeatNationId ?? State.ActiveNationId);
        State = trial.State;
        ArmyTravelTargets = trial.ArmyTravelTargets;
        RetreatSupplyPlans = trial.RetreatSupplyPlans;
        NationRallyPoints = trial.NationRallyPoints;
        _pendingPrelude = trial._pendingPrelude;
        _pendingNewsBaseline = trial._pendingNewsBaseline;
        _pendingPeaceTreatyOffers.Clear();
        foreach (var pair in trial._pendingPeaceTreatyOffers) _pendingPeaceTreatyOffers.Add(pair.Key, pair.Value);
        _pendingFallTreasuries.Clear();
        foreach (var pair in trial._pendingFallTreasuries) _pendingFallTreasuries.Add(pair.Key, pair.Value);
        _pendingBattleResults.Clear(); _pendingBattleResults.AddRange(trial._pendingBattleResults);
        _pendingSeatFalls.Clear(); _pendingSeatFalls.AddRange(trial._pendingSeatFalls);
        _eventsThisCall.Clear();
        _gameOver = trial._gameOver;
        _seatLost = trial._seatLost;
        _isWatchMode = trial._isWatchMode;
        _scenarioWin = trial._scenarioWin;
        LastBattles = outputs.SelectMany(o => o.Battles).ToArray();
        LastSeatFalls = outputs.SelectMany(o => o.SeatFalls).ToArray();
        LastTacticalTimeline = ValueList<TacticalBattleFrame>.Empty;
        LastStrategicTimeline = _strategicTimeline.Complete(State, false, outputs.Any(o => o.ShouldExit));
        LastDialogCommandResult = trial.LastDialogCommandResult;
        draft.Complete(trial, commands);
        var published = events.ToArray();
        if (EventsPublished is { } subscribers)
            foreach (Action<IReadOnlyList<DomainEvent>> subscriber in subscribers.GetInvocationList())
                try { subscriber(published); } catch { /* A presentation listener cannot undo an applied commit. */ }
        if (StrategicTimelinePublished is { } timelineSubscribers)
            foreach (Action<StrategicTravelBatch> subscriber in timelineSubscribers.GetInvocationList())
                try { subscriber(LastStrategicTimeline); } catch { }
        var combined = new SessionOutput(outputs.SelectMany(o => o.Lines).ToArray(), outputs.Any(o => o.ShouldExit),
            LastBattles, LastSeatFalls, IsGameOver) { StrategicTimeline = LastStrategicTimeline };
        return new(true, combined);
    }
}
