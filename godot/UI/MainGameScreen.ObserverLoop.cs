using Godot;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

public partial class MainGameScreen
{
    public bool AutoStartObserver { get; init; }
    public bool StartObserverLoop { get; init; }
    public string? ObserverRecordingDirectory { get; init; }
    public int? ObserverViewpointSeed { get; init; }
    public event Action? ObserverRestartRequested;
    public event Action<string>? ObserverCampaignFinished;
    public CheckBox ObserverLoopChoice { get; private set; } = null!;
    private ContinuousObserverPolicy? _observerLoop;
    private double _observerSeatDelay, _observerEndingHold;
    private int _observerReportedTurn = -1;
    public string? ObserverLoopNation => _observerLoop?.WatchedNationId;
    private bool ObserverLoopActive => _observerLoop is not null;
    private bool ObserverAtBoundary => !StrategicTravel.IsActive && ActiveOverlay is null && _observerEncounterQueue.Count == 0;

    private void InitializeObserverLaunch()
    {
        if (!Session.IsObserverMode) return;
        if (StartObserverLoop) SetObserverLoop(true);
        if (AutoStartObserver) ObserverPlay();
    }

    public void SetObserverLoop(bool enabled)
    {
        if (!Session.IsObserverMode || !ObserverAtBoundary) return;
        _observerLoop = enabled ? new ContinuousObserverPolicy(ObserverViewpointSeed is { } seed ? new Random(seed).Next : null) : null;
        _observerSeatDelay = 0;
        _observerEndingHold = 0;
        ObserverLoopChoice.SetPressedNoSignal(enabled);
        if (enabled) SelectLoopNation();
        RefreshObserverControls();
    }

    private void SelectLoopNation()
    {
        var previous = _observerLoop?.WatchedNationId;
        var selected = _observerLoop?.SelectSurvivor(Session.State, Session.Scenario);
        if (selected is not null && selected != previous && ObserverRecordingDirectory is not null)
            GD.Print("OBSERVER_VIEW " + System.Text.Json.JsonSerializer.Serialize(new {
                nationId = selected, turn = Session.State.Calendar.TurnIndex, frame = Godot.Engine.GetProcessFrames() }));
        if (selected is not null && (selected != previous || ViewedNationId != selected)) SetViewedNation(selected);
    }

    private void AdvanceContinuousObserver(double delta)
    {
        if (!ObserverLoopActive || _closing || _observerManuallyPaused || !ObserverAtBoundary) return;
        if (ContinuousObserverPolicy.Ending(Session) is { } ending)
        {
            _observerStatus.Text = $"{ending} · {(ObserverCampaignFinished is null ? "next campaign" : "recording ends")} in {Math.Max(0, 3 - _observerEndingHold):0}s";
            _observerEndingHold += delta;
            if (_observerEndingHold >= 3 && (ObserverRestartRequested is not null || ObserverCampaignFinished is not null))
            {
                _closing = true;
                ObserverPlayback.Pause();
                if (ObserverCampaignFinished is { } finish) finish(ending);
                else ObserverRestartRequested!.Invoke();
            }
            return;
        }
        if (!ObserverPlayback.IsRunning) return;
        if (ObserverRecordingDirectory is not null && _observerReportedTurn != Session.State.Calendar.TurnIndex)
        {
            _observerReportedTurn = Session.State.Calendar.TurnIndex;
            GD.Print("OBSERVER_PROGRESS " + System.Text.Json.JsonSerializer.Serialize(new {
                turn = _observerReportedTurn, yearBc = Session.State.Calendar.YearBc,
                nationId = ObserverLoopNation, frame = Godot.Engine.GetProcessFrames() }));
        }
        SelectLoopNation();
        _observerSeatDelay = Math.Max(0, _observerSeatDelay - delta);
    }
}
