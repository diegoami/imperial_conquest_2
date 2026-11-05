using Godot;
using IC2.Engine.Battle;
using IC2.Engine.Model;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

public partial class MainGameScreen
{
    public ObserverRunController ObserverPlayback { get; } = new();
    public HBoxContainer ObserverControls { get; private set; } = null!;
    public Button ObserverPlayButton { get; private set; } = null!;
    public Button ObserverPauseButton { get; private set; } = null!;
    public Button ObserverRoundButton { get; private set; } = null!;
    public Button ObserverTakeControlButton { get; private set; } = null!;
    public OptionButton ObserverNationChoice { get; private set; } = null!;
    private Button _observerReportsButton = null!;
    public Button ObserverReportsButton => _observerReportsButton;
    private Label _observerStatus = null!;
    private readonly List<string> _observerNationIds = new();

    private void BuildObserverControls(VBoxContainer root)
    {
        ObserverControls = new HBoxContainer { Name = "ObserverControls", Visible = Session.IsObserverMode };
        ObserverControls.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        _observerStatus = UiKit.MakeLabel("Observer · paused", AppTheme.Note, UiKit.TextColor);
        _observerStatus.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        ObserverControls.AddChild(_observerStatus);
        ObserverPlayButton = UiKit.MakeButton("Play", ObserverPlay, AppTheme.Button);
        ObserverPauseButton = UiKit.MakeButton("Pause", ObserverPause, AppTheme.Button);
        ObserverRoundButton = UiKit.MakeButton("Next round", ObserverNextRound, AppTheme.Button);
        ObserverRoundButton.TooltipText = "Advance to the next campaign date, then pause.";
        _observerReportsButton = UiKit.MakeButton("Latest battles", ShowObserverReports, AppTheme.Button);
        foreach (var button in new[] { ObserverPlayButton, ObserverPauseButton, ObserverRoundButton, _observerReportsButton })
            ObserverControls.AddChild(button);
        ObserverNationChoice = new OptionButton { Name = "ObserverNationChoice" };
        ObserverControls.AddChild(ObserverNationChoice);
        ObserverTakeControlButton = UiKit.MakeButton("Take control", () => {
            if (ObserverNationChoice.Selected >= 0 && ObserverNationChoice.Selected < _observerNationIds.Count)
                ObserverTakeControl(_observerNationIds[ObserverNationChoice.Selected]);
        }, AppTheme.Button);
        ObserverTakeControlButton.TooltipText = "Join this nation's next turn. Nations ahead of it play normally first.";
        ObserverControls.AddChild(ObserverTakeControlButton);
        root.AddChild(ObserverControls);
        BuildObserverPlaybackControls(root);
        RefreshObserverControls();
    }

    public void ObserverPlay()
    {
        if (!Session.IsObserverMode || ActiveOverlay is not null && !IsAutomaticObserverOverlay) return;
        var resumeRound = _observerManuallyPaused && ObserverPlayback.StopAtTurn is not null;
        _observerManuallyPaused = false;
        if (resumeRound) ObserverPlayback.Resume(); else ObserverPlayback.Play();
        RefreshObserverControls();
    }

    public void ObserverPause()
    {
        _observerManuallyPaused = true;
        ObserverPlayback.Suspend();
        RefreshObserverControls();
    }

    public void ObserverNextRound()
    {
        if (!Session.IsObserverMode || ActiveOverlay is not null && !IsAutomaticObserverOverlay) return;
        _observerManuallyPaused = false;
        ObserverPlayback.NextRound(Session, DisplayState.Calendar.TurnIndex);
        RefreshObserverControls();
    }

    public void ObserverTakeControl(string nationId)
    {
        if (ObserverPlayback.IsRunning || StrategicTravel.IsActive || ActiveOverlay is not null || !Session.IsObserverMode) return;
        SubmitForCheck($"take-control {nationId}");
        RefreshObserverControls();
    }

    public void AdvanceObserverForCheck() => AdvanceObserver();

    private void AdvanceObserver()
    {
        if (_closing || ObserverControls is null) return;
        if (ActiveOverlay is not null && !IsAutomaticObserverOverlay) ObserverPause();
        if (!Session.IsObserverMode) ObserverPlayback.Pause();
        if (ObserverPlayback.IsRunning && ActiveOverlay is null && !StrategicTravel.IsActive && _observerEncounterQueue.Count == 0)
        {
            try
            {
                if (ObserverPlayback.Advance(Session) is { } output) OnCommandIssued(output.Lines);
            }
            catch (Exception ex)
            {
                ObserverPlayback.Pause();
                ShowScreenMessage($"Observation stopped: {ex.Message}");
                GD.PushError(ex.ToString());
            }
        }
        RefreshObserverControls();
    }

    private void RefreshObserverControls()
    {
        if (ObserverControls is null) return;
        ObserverControls.Visible = Session.IsObserverMode;
        ObserverViewControls.Visible = Session.IsObserverMode;
        if (!Session.IsObserverMode) { _observerConfigured = false; return; }
        ConfigureObserverPlayback();
        var running = ObserverPlayback.IsRunning;
        var showing = StrategicTravel.CurrentRoute?.OwnerId ?? StrategicTravel.CurrentStep?.Before.ActiveNationId ?? Session.State.ActiveNationId;
        var action = _observerManuallyPaused ? "paused" : running ? "playing" : StrategicTravel.IsActive || IsAutomaticObserverOverlay ? "finishing round" : "paused";
        _observerStatus.Text = $"Observer · {action} · {Session.State.NationById(showing)?.Name}";
        _observerStatus.TooltipText = ObserverPlayback.StopReason ?? "Space pauses or resumes playback. Finish or skip current activity before changing watched nations or taking control.";
        ObserverPlayButton.Disabled = running;
        ObserverPauseButton.Disabled = !running;
        ObserverRoundButton.Disabled = running;
        _observerReportsButton.Disabled = _observerLatestEncounters.Count == 0;
        var choices = Session.State.Nations.Where(n => !n.Eliminated && n.Id != Session.Scenario.Barbarians?.NationId)
            .OrderBy(n => n.Name, StringComparer.Ordinal).ToArray();
        if (!_observerNationIds.SequenceEqual(choices.Select(n => n.Id)))
        {
            _observerNationIds.Clear();
            ObserverNationChoice.Clear();
            foreach (var nation in choices) { _observerNationIds.Add(nation.Id); ObserverNationChoice.AddItem(nation.Name); }
            var selected = _observerNationIds.IndexOf(Session.State.ActiveNationId);
            if (selected >= 0) ObserverNationChoice.Select(selected);
        }
        ObserverNationChoice.Disabled = running;
        ObserverTakeControlButton.Disabled = running || StrategicTravel.IsActive || _observerNationIds.Count == 0;
        RefreshObserverPlaybackControls();
    }

    private void RememberObserverReports(SessionOutput output) => RememberObserverEncounters(output);

    private void ShowObserverReports()
    {
        ObserverPause();
        ShowObserverHistory();
    }

    private void ConfirmObserverEntry()
    {
        if (Session.IsObserverMode) { ObserverPause(); return; }
        if (ActiveOverlay is not null) { ShowScreenMessage("Close the current dialog before observing."); return; }
        var prompt = new ConfirmPrompt { Question = "Observe this campaign with every nation controlled by the computer? Starts paused; you can take control again." };
        prompt.Confirmed += () => {
            CloseOverlay(prompt);
            SubmitForCheck("observe");
            RefreshObserverControls();
        };
        prompt.Refused += () => CloseOverlay(prompt);
        ShowOverlay(prompt);
    }
}
