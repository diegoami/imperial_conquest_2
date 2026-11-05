using Godot;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Screens;

public partial class TacticalBattleScreen
{
    public ObserverBattleReplay? ObserverReplay { get; init; }
    public int ObserverPerspectiveSide { get; init; }
    public Func<bool>? ObserverPaused { get; init; }
    public Func<double>? ObserverRate { get; init; }
    public event Action? ObserverPauseRequested;
    public event Action? ObserverSkipRequested;
    public event Action? ObserverReplayFinished;
    public Button ObserverPauseButton { get; private set; } = null!;
    public Button ObserverSkipButton { get; private set; } = null!;
    public OptionButton ObserverSpeedChoice { get; private set; } = null!;
    private bool _observerReplayComplete, _observerReplayFinished;
    private double _observerResultHold;

    private void BuildObserverReplayControls(HBoxContainer controls)
    {
        ObserverPauseButton = UiKit.MakeButton("Pause", () => ObserverPauseRequested?.Invoke());
        controls.AddChild(ObserverPauseButton);
        ObserverSpeedChoice = ObserverPlaybackControls.SpeedChoice();
        controls.AddChild(ObserverSpeedChoice);
        ObserverSkipButton = UiKit.MakeButton("Skip", () => ObserverSkipRequested?.Invoke());
        ObserverSkipButton.TooltipText = "Skip battle playback";
        controls.AddChild(ObserverSkipButton);
    }

    private void AdvanceObserverReplay(double delta)
    {
        var paused = ObserverPaused?.Invoke() == true || _dialogKind != DialogKind.None;
        ObserverPlaybackControls.Synchronize(ObserverSpeedChoice);
        ObserverPauseButton.Text = paused ? "Play" : "Pause";
        if (paused || _observerReplayFinished) return;
        var rate = ObserverRate?.Invoke() ?? 1;
        if (!_observerReplayComplete) AdvancePlayback(delta * rate);
        else
        {
            var report = ObserverReplay!.Report;
            PhaseLabel.Text = $"Battle ended · {ObserverReplay.After.NationById(report.WinnerNationId)?.Name ?? report.WinnerNationId} wins · {report.WinnerFinishTotal:N0} troops remain";
            _observerResultHold += delta * rate;
            if (_observerResultHold >= 0.8) FinishObserverReplay();
        }
    }

    public void FinishObserverReplay()
    {
        if (_observerReplayFinished) return;
        _observerReplayFinished = true;
        SkipPlayback();
        ObserverReplayFinished?.Invoke();
    }
}
