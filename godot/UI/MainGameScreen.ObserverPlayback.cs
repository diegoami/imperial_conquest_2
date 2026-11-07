using Godot;
using IC2.Engine.Battle;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;

namespace IC2.Slice.UI;

public partial class MainGameScreen
{
    private sealed record ObserverEncounter(ObserverBattleReplay? Tactical, BattleResult? Quick, GameState Campaign);
    public HBoxContainer ObserverViewControls { get; private set; } = null!;
    public Button ObserverWatchButton { get; private set; } = null!;
    public Button ObserverSkipButton { get; private set; } = null!;
    public OptionButton ObserverSpeedChoice { get; private set; } = null!;
    public OptionButton ObserverBattleChoice { get; private set; } = null!;
    public CheckBox ObserverFollowChoice { get; private set; } = null!;
    public bool ObserverPresentationPaused => _observerManuallyPaused;
    public int ObserverTacticalPresentations { get; private set; }
    private double ObserverSpeed => SettingsScreen.Current.ObserverSpeedPercent / 100.0;
    private bool _observerManuallyPaused = true, _observerConfigured, _openingObserverOverlay, _skippingObserverPresentation;
    private bool _observerFollowCurrentRoute;
    private Control? _observerAutomaticOverlay;
    private double _observerReportHold;
    private readonly Dictionary<string, ObserverBattleReplay> _observerCurrentBattles = new(StringComparer.Ordinal);
    private readonly Queue<ObserverEncounter> _observerEncounterQueue = new();
    private readonly List<ObserverEncounter> _observerLatestEncounters = new();
    private bool IsAutomaticObserverOverlay => ActiveOverlay is not null && ReferenceEquals(ActiveOverlay, _observerAutomaticOverlay);

    public bool ObserverWatches(string nation) => SettingsScreen.Current.WatchedNationsFor(Session.World.Id) is not { } selected
        || selected.Contains(nation);

    private void BuildObserverPlaybackControls(VBoxContainer root)
    {
        ObserverViewControls = new HBoxContainer { Name = "ObserverViewControls", Visible = Session.IsObserverMode };
        ObserverViewControls.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        ObserverWatchButton = UiKit.MakeButton("Watch civilizations…", OpenObserverWatchChooser);
        QolCommandIcons.Apply(ObserverWatchButton, "watch", "Watch civilizations");
        ObserverViewControls.AddChild(ObserverWatchButton);
        ObserverViewControls.AddChild(UiKit.MakeLabel("Speed", AppTheme.Note, UiKit.TextColor));
        ObserverSpeedChoice = ObserverPlaybackControls.SpeedChoice();
        ObserverViewControls.AddChild(ObserverSpeedChoice);
        ObserverFollowChoice = new CheckBox { Text = "Follow moves", ButtonPressed = SettingsScreen.Current.ObserverFollowMoves };
        ObserverFollowChoice.Toggled += on => SettingsScreen.Commit(SettingsScreen.Current with { ObserverFollowMoves = on });
        ObserverViewControls.AddChild(ObserverFollowChoice);
        ObserverBattleChoice = new OptionButton { Name = "ObserverBattlePolicy" };
        ObserverBattleChoice.AddItem("Battles: quick results");
        ObserverBattleChoice.AddItem("Battles: tactical (experimental)");
        ObserverBattleChoice.Select(SettingsScreen.Current.ObserverTacticalBattles ? 1 : 0);
        ObserverBattleChoice.TooltipText = "Full tactical uses both computer generals for watched and unwatched fights. Quick results uses instant resolution. Outcomes can differ between these policies.";
        ObserverBattleChoice.ItemSelected += index =>
        {
            if (!CanEditObserverSelection) return;
            SettingsScreen.Commit(SettingsScreen.Current with { ObserverTacticalBattles = index == 1 });
            Session.ObserverTacticalBattles = index == 1;
        };
        ObserverViewControls.AddChild(ObserverBattleChoice);
        ObserverSkipButton = UiKit.MakeButton("Skip current activity", SkipObserverActivity);
        QolCommandIcons.Apply(ObserverSkipButton, "skip", "Skip current activity");
        ObserverViewControls.AddChild(ObserverSkipButton);
        root.AddChild(ObserverViewControls);
    }

    private bool CanEditObserverSelection => Session.IsObserverMode && !ObserverPlayback.IsRunning
        && !StrategicTravel.IsActive && ActiveOverlay is null && _observerEncounterQueue.Count == 0;

    private void ConfigureObserverPlayback()
    {
        if (_observerConfigured) return;
        _observerConfigured = true;
        _observerManuallyPaused = true;
        ObserverPlayback.Pause();
        Session.ObserverTacticalBattles = SettingsScreen.Current.ObserverTacticalBattles;
        Session.CaptureObserverBattle = (a, b) => ObserverWatches(a) || ObserverWatches(b);
    }

    private void RefreshObserverPlaybackControls()
    {
        if (!ObserverPlayback.IsRunning && ObserverPlayback.StopAtTurn is { } finished
            && Session.State.Calendar.TurnIndex >= finished && !StrategicTravel.IsActive && ActiveOverlay is null)
            ObserverPlayback.Pause();
        ObserverWatchButton.Disabled = !CanEditObserverSelection;
        ObserverBattleChoice.Disabled = !CanEditObserverSelection;
        ObserverBattleChoice.Select(Session.ObserverTacticalBattles ? 1 : 0);
        ObserverFollowChoice.SetPressedNoSignal(SettingsScreen.Current.ObserverFollowMoves);
        ObserverPlaybackControls.Synchronize(ObserverSpeedChoice);
        var watched = Session.State.Nations.Count(n => ObserverWatches(n.Id));
        QolCommandIcons.Apply(ObserverWatchButton, "watch", $"Watch civilizations ({watched})");
        if (QolCommandIcons.IsIllustrated(ObserverWatchButton.Icon)) ObserverWatchButton.Text = watched.ToString();
        ObserverSkipButton.Disabled = !StrategicTravel.IsActive && !IsAutomaticObserverOverlay;
        ObserverPlayButton.Disabled = ObserverPlayback.IsRunning && !_observerManuallyPaused;
        ObserverPauseButton.Disabled = _observerManuallyPaused || !ObserverPlayback.IsRunning && !StrategicTravel.IsActive && !IsAutomaticObserverOverlay;
        ObserverTakeControlButton.Disabled = !CanEditObserverSelection || _observerNationIds.Count == 0;
    }

    private void OpenObserverWatchChooser()
    {
        if (!CanEditObserverSelection) return;
        var dialog = new ObserverWatchDialog { Nations = Session.State.Nations,
            Selected = SettingsScreen.Current.WatchedNationsFor(Session.World.Id) };
        dialog.Confirmed += selected =>
        {
            SettingsScreen.Commit(SettingsScreen.Current.WithWatchedNations(Session.World.Id, selected));
            CloseOverlay(dialog);
            if (selected is { Count: 1 }) SetViewedNation(selected[0]);
            RefreshObserverControls();
        };
        dialog.Cancelled += () => CloseOverlay(dialog);
        ShowOverlay(dialog);
    }

    public void ToggleObserverPlayback()
    {
        if (_observerManuallyPaused) ObserverPlay(); else ObserverPause();
    }

    private void ObserverRouteStarted(StrategicTravelObservation route)
    {
        _observerFollowCurrentRoute = Session.IsObserverMode && ObserverWatches(route.OwnerId);
        if (_observerFollowCurrentRoute && SettingsScreen.Current.ObserverFollowMoves)
            _mapView.CentreOnTile(route.Origin.X, route.Origin.Y);
    }

    private void ObserverCellEntered(string id, GridPoint cell)
    {
        if (Session.IsObserverMode && _observerFollowCurrentRoute && SettingsScreen.Current.ObserverFollowMoves)
            _mapView.CentreOnTile(cell.X, cell.Y);
    }

    private void PrepareObserverPresentation(SessionOutput output)
    {
        _observerCurrentBattles.Clear();
        if (!Session.IsObserverMode) return;
        foreach (var replay in output.ObserverBattles) _observerCurrentBattles["observer:" + replay.BattleId] = replay;
        if (output.StrategicTimeline.Steps.Count == 0 && Session.ObserverTacticalBattles)
        {
            foreach (var replay in output.ObserverBattles.Where(r => r.Frames.Count > 0))
                _observerEncounterQueue.Enqueue(new(replay, null, replay.AtAttack));
            foreach (var report in output.Battles.Where(b => ObserverWatches(b.AttackerNationId) || ObserverWatches(b.DefenderNationId)))
                _observerEncounterQueue.Enqueue(new(null, report, Session.State));
            ShowNextObserverEncounter();
        }
    }

    private IEnumerable<ObserverEncounter> Encounters(SessionOutput output)
    {
        if (output.StrategicTimeline.Steps.Count == 0)
        {
            foreach (var replay in output.ObserverBattles) yield return new(replay, null, replay.AtAttack);
            foreach (var report in output.Battles) yield return new(null, report, Session.State);
            yield break;
        }
        foreach (var step in output.StrategicTimeline.Steps)
        {
            if (_observerCurrentBattles.TryGetValue(step.Identity, out var tactical))
                yield return new(tactical, null, tactical.AtAttack);
            foreach (var quick in step.Events.OfType<BattleResolved>()) yield return new(null, quick.Result, step.Before);
        }
    }

    private void RememberObserverEncounters(SessionOutput output)
    {
        var encounters = Encounters(output).ToArray();
        if (encounters.Length == 0) return;
        _observerLatestEncounters.Clear(); _observerLatestEncounters.AddRange(encounters);
    }

    private void ObserverBoundaryCompleted(StrategicTravelStep step)
    {
        if (!Session.IsObserverMode || _closing || _skippingObserverPresentation) return;
        if (_observerCurrentBattles.TryGetValue(step.Identity, out var replay) && replay.Frames.Count > 0)
            _observerEncounterQueue.Enqueue(new(replay, null, replay.AtAttack));
        if (Session.ObserverTacticalBattles)
            foreach (var item in step.Events.OfType<BattleResolved>())
                if (ObserverWatches(item.Result.AttackerNationId) || ObserverWatches(item.Result.DefenderNationId))
                    _observerEncounterQueue.Enqueue(new(null, item.Result, step.Before));
        ShowNextObserverEncounter();
    }

    private void ShowNextObserverEncounter()
    {
        if (ActiveOverlay is not null || _observerEncounterQueue.Count == 0 || _closing) return;
        var next = _observerEncounterQueue.Dequeue();
        StrategicTravel.Paused = true;
        Control screen;
        if (next.Tactical is { } replay)
        {
            ObserverTacticalPresentations++;
            var board = new TacticalBattleScreen { Session = Session, RepositoryRoot = RepositoryRoot,
                InitialView = replay.Frames[0].Before.View, InitialTimeline = replay.Frames, ObserverReplay = replay,
                ObserverPerspectiveSide = ObserverWatches(replay.Frames[0].Before.View.AttackerNationId) ? 0 : 1,
                ObserverPaused = () => _observerManuallyPaused, ObserverRate = () => ObserverSpeed };
            var audio = new Audio.TacticalPlaybackAudio(Session.Ruleset, () => SettingsScreen.SoundEnabled && !_skippingObserverPresentation, _soundPlayer.PlayTactical);
            board.ExchangeEntered += audio.ExchangeEntered;
            board.ObserverPauseRequested += ToggleObserverPlayback;
            board.ObserverSkipRequested += () => SkipObserverBattle(board);
            board.ObserverReplayFinished += () =>
            {
                if (!_skippingObserverPresentation)
                    _soundPlayer.Play(Audio.SoundCues.ForEvents(replay.Events, replay.After));
                CloseObserverEncounter(board);
            };
            screen = board;
        }
        else
        {
            var result = new BattleResultScreen { Session = Session, Result = BattleResultViewModel.FromResult(next.Quick!) };
            result.Closed += () => CloseObserverEncounter(result);
            screen = result;
        }
        _observerAutomaticOverlay = screen;
        _observerReportHold = 0;
        _openingObserverOverlay = true;
        try { ShowOverlay(screen); }
        finally { _openingObserverOverlay = false; }
    }

    private void CloseObserverEncounter(Control screen)
    {
        if (_closing || !ReferenceEquals(_observerAutomaticOverlay, screen)) return;
        CloseOverlay(screen); _observerAutomaticOverlay = null;
        ShowNextObserverEncounter();
        if (_observerAutomaticOverlay is null) StrategicTravel.Paused = false;
    }

    private void SkipObserverBattle(TacticalBattleScreen board)
    {
        _skippingObserverPresentation = true;
        try { _soundPlayer.ClearTactical("observer-skip"); board.FinishObserverReplay(); }
        finally { _skippingObserverPresentation = false; }
    }

    public void SkipObserverActivity()
    {
        if (!Session.IsObserverMode) return;
        _skippingObserverPresentation = true;
        try
        {
            _soundPlayer.ClearTactical("observer-skip"); _observerEncounterQueue.Clear();
            if (_observerAutomaticOverlay is TacticalBattleScreen board) board.FinishObserverReplay();
            else if (_observerAutomaticOverlay is { } report) CloseObserverEncounter(report);
            StrategicTravel.Paused = false; StrategicTravel.Skip();
        }
        finally { _skippingObserverPresentation = false; }
        RefreshObserverControls();
    }

    private void AdvanceObserverPresentation(double delta)
    {
        if (!Session.IsObserverMode || !IsAutomaticObserverOverlay || _observerManuallyPaused) return;
        if (_observerAutomaticOverlay is TacticalBattleScreen) return;
        _observerReportHold += delta * ObserverSpeed;
        if (_observerReportHold >= 1.5) CloseObserverEncounter(_observerAutomaticOverlay!);
    }

    private IReadOnlyList<DomainEvent> FilterObserverBattleAudio(IReadOnlyList<DomainEvent> events)
    {
        if (!Session.IsObserverMode || Session.LastObserverBattles.Count == 0) return events;
        var deferred = new HashSet<DomainEvent>(ReferenceEqualityComparer.Instance);
        foreach (var replay in Session.LastObserverBattles) foreach (var item in replay.Events) deferred.Add(item);
        return events.Where(item => !deferred.Contains(item)).ToArray();
    }

    private void ShowObserverHistory()
    {
        var remaining = new Queue<ObserverEncounter>(_observerLatestEncounters);
        void Next()
        {
            if (!remaining.TryDequeue(out var encounter) || _closing) return;
            if (encounter.Tactical is { } tactical)
            {
                var screen = new BattleEndedScreen { Model = BattleEndedViewModel.FromReport(tactical.Report, tactical.After, Session.Ruleset) };
                screen.Closed += () => { CloseOverlay(screen); Next(); };
                ShowOverlay(screen);
            }
            else
            {
                var screen = new BattleResultScreen { Session = Session, Result = BattleResultViewModel.FromResult(encounter.Quick!) };
                screen.Closed += () => { CloseOverlay(screen); Next(); };
                ShowOverlay(screen);
            }
        }
        Next();
    }

    private void CancelObserverPresentation()
    {
        Session.CaptureObserverBattle = null; _observerCurrentBattles.Clear(); _observerEncounterQueue.Clear();
        _observerAutomaticOverlay = null; _soundPlayer?.ClearTactical("observer-closed");
    }
}
