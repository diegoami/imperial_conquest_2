using Godot;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

public partial class MainGameScreen
{
    public bool AutoStartObserver { get; init; }
    public bool StartObserverLoop { get; init; }
    public event Action? ObserverRestartRequested;
    public CheckBox ObserverLoopChoice { get; private set; } = null!;
    private ContinuousObserverPolicy? _observerLoop;
    private double _observerSeatDelay, _observerEndingHold;
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
        _observerLoop = enabled ? new ContinuousObserverPolicy() : null;
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
        if (selected is not null && (selected != previous || ViewedNationId != selected)) SetViewedNation(selected);
    }

    private void AdvanceContinuousObserver(double delta)
    {
        if (!ObserverLoopActive || _closing || _observerManuallyPaused || !ObserverAtBoundary) return;
        if (ContinuousObserverPolicy.Ending(Session) is { } ending)
        {
            _observerStatus.Text = $"{ending} · next campaign in {Math.Max(0, 3 - _observerEndingHold):0}s";
            _observerEndingHold += delta;
            if (_observerEndingHold >= 3 && ObserverRestartRequested is not null)
            {
                _closing = true;
                ObserverPlayback.Pause();
                ObserverRestartRequested.Invoke();
            }
            return;
        }
        if (!ObserverPlayback.IsRunning) return;
        SelectLoopNation();
        _observerSeatDelay = Math.Max(0, _observerSeatDelay - delta);
    }
}
