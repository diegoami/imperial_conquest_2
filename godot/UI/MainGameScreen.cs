using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;

namespace IC2.Slice.UI;

/// <summary>
/// The main game screen — <c>docs/game-design.md</c> §"User interface" item 2, "the dominant,
/// near-always-visible view": a top bar (calendar, active seat, End Turn), the map
/// (<see cref="GameMapView"/>), the persistent contextual side panel (<see cref="ContextPanel"/>), the
/// bottom filter toolbar, and the non-modal news log (<see cref="NewsLogPanel"/>).
/// </summary>
/// <remarks>
/// T25 (plan #474): also opens the three screens item 2 leads into —
/// <see cref="Screens.BattleResultScreen"/> after an attack or siege that resolved a battle
/// (<see cref="GameSession.Submit"/>'s own new <see cref="SessionOutput.Battles"/>, read from
/// <see cref="OnCommandIssued"/> so it fires whichever control actually issued the command —
/// <see cref="GameMapView"/>'s own map-click attack, or a future control of this screen's own), the
/// <see cref="Screens.DiplomacyScreen"/> from the bottom toolbar's own "Diplomacy" button, and the
/// <see cref="Screens.HotseatHandoffScreen"/> whenever <see cref="GameState.ActiveNationId"/> passes from
/// one human seat to a different one (<see cref="HotseatHandoffDetector"/>).
/// </remarks>
public partial class MainGameScreen : Control
{
    public required GameSession Session { get; init; }

    public required string RepositoryRoot { get; init; }

    /// <summary>
    /// Where <see cref="OnSavePressed"/> writes — the same <c>user://saves</c> convention
    /// <see cref="LoadGameScreen"/> already reads <c>*.json</c> saves from (its own private constant of
    /// the same name and value; duplicated rather than shared, since neither file may add a third file
    /// this task does not own to hold it in common).
    /// </summary>
    private const string SavesDirectory = "user://saves";

    /// <summary>Exposed (rather than kept private) so <c>godot/Checks/ScreenshotTour.cs</c> can drive a
    /// selection and open the news log for the visual sign-off screenshot without simulating clicks.</summary>
    public GameMapView MapView => _mapView;

    public ContextPanel ContextPanel => _contextPanel;

    public NewsLogPanel NewsLog => _newsLog;

    /// <summary>The currently open modal overlay (a battle result, the diplomacy grid, or the hotseat
    /// handoff), if any — <see langword="null"/> when the map is fully interactive. Exposed for
    /// <c>godot/Screens/Checks/**</c>.</summary>
    public Control? ActiveOverlay { get; private set; }

    private Label _calendarLabel = null!;
    private Label _activeNationLabel = null!;
    private Label _saveConfirmationLabel = null!;
    private GameMapView _mapView = null!;
    private ContextPanel _contextPanel = null!;
    private NewsLogPanel _newsLog = null!;
    private Label _lastCommandLabel = null!;

    private readonly Queue<Engine.Battle.BattleResult> _pendingBattleOverlays = new();
    private string? _lastKnownActiveNationId;
    private bool _lastKnownActiveWasHuman;

    public override void _Ready()
    {
        UiKit.ApplyBackground(this, UiKit.Background);

        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);

        root.AddChild(BuildTopBar());

        // A plain HBoxContainer, not HSplitContainer: SplitContainer's own split_offset sign convention
        // left the context panel with no visible width at all (T24's first pass) -- simpler and
        // predictable is the map taking whatever the panel's own fixed CustomMinimumSize does not.
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(body);

        _mapView = new GameMapView { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddChild(_mapView);

        _contextPanel = new ContextPanel
        {
            Session = Session,
            MapView = _mapView,
            CustomMinimumSize = new Vector2(340, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        body.AddChild(_contextPanel);

        root.AddChild(BuildBottomToolbar());

        _lastCommandLabel = UiKit.MakeLabel(string.Empty, 12, UiKit.MutedTextColor);
        root.AddChild(_lastCommandLabel);

        _newsLog = new NewsLogPanel { Session = Session, CustomMinimumSize = new Vector2(0, 200) };
        root.AddChild(_newsLog);

        _mapView.CitySelected += id => _contextPanel.ShowCity(id);
        _mapView.ArmySelected += id => _contextPanel.ShowArmy(id);
        _mapView.FleetSelected += id => _contextPanel.ShowFleet(id);
        _mapView.SelectionCleared += () => _contextPanel.ShowNationOverview();
        _mapView.CommandIssued += OnCommandIssued;
        _contextPanel.CommandIssued += OnCommandIssued;

        _mapView.Attach(Session, RepositoryRoot);

        var activeNation = Session.State.NationById(Session.State.ActiveNationId);
        _lastKnownActiveNationId = Session.State.ActiveNationId;
        _lastKnownActiveWasHuman = activeNation?.Control == SeatControl.Human;

        RefreshTopBar();
    }

    private Control BuildTopBar()
    {
        var bar = UiKit.MakePanel(UiKit.PanelColorRaised, cornerRadius: 0);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 24);
        bar.AddChild(row);

        _calendarLabel = UiKit.MakeLabel(string.Empty, 16, UiKit.TextColor);
        row.AddChild(_calendarLabel);

        _activeNationLabel = UiKit.MakeLabel(string.Empty, 16, UiKit.AccentColor);
        row.AddChild(_activeNationLabel);

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(spacer);

        // T95 (#467), Done-when 1: "a Save action in the main game screen." Next to End Turn, the other
        // always-available top-bar action.
        row.AddChild(UiKit.MakeButton("Save", OnSavePressed, 16));
        row.AddChild(UiKit.MakeButton("End Turn", OnEndTurnPressed, 16));

        // T95: the Save action's own confirmation/refusal line. Not routed through OnCommandIssued's own
        // _lastCommandLabel (below the bottom toolbar): that label always shows Submit's own trailing
        // blank separator line (SessionOutput.Lines's last entry, every command's own convention, shared
        // by every control on this screen, none of which this task's narrow "a Save action" grant may
        // change) -- so it never actually displays anything, for Save or for End Turn alike. This label
        // is this task's own, showing HandleSave's real last non-empty line instead.
        _saveConfirmationLabel = UiKit.MakeLabel(string.Empty, 14, UiKit.MutedTextColor);
        row.AddChild(_saveConfirmationLabel);

        return bar;
    }

    private Control BuildBottomToolbar()
    {
        var bar = UiKit.MakePanel(UiKit.PanelColorRaised, cornerRadius: 0);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        bar.AddChild(row);

        row.AddChild(MakeFilterToggle("Cities", true, value => _mapView.ShowCities = value));
        row.AddChild(MakeFilterToggle("Armies", true, value => _mapView.ShowArmies = value));
        row.AddChild(MakeFilterToggle("Fleets", true, value => _mapView.ShowFleets = value));

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(spacer);

        // T25 (plan #474, docs/game-design.md item 4): the diplomacy grid's own control, opening
        // Screens.DiplomacyScreen -- "the diplomacy grid from a control."
        row.AddChild(UiKit.MakeButton("Diplomacy", OpenDiplomacyScreen, 14));
        row.AddChild(UiKit.MakeButton("News", () => _newsLog.Toggle(), 14));

        return bar;
    }

    private Control MakeFilterToggle(string label, bool initial, Action<bool> onToggled)
    {
        var button = new CheckButton { Text = label, ButtonPressed = initial };
        button.Toggled += onToggled.Invoke;
        return button;
    }

    private void OnEndTurnPressed() => SubmitForCheck("end");

    /// <summary>
    /// The "Save" button — <c>docs/tasks/T95.md</c> (#467), Done-when 1: writes the current game to a
    /// file under <c>user://saves</c> <strong>[designed]</strong>, the same per-user directory
    /// <see cref="LoadGameScreen"/> already lists <c>*.json</c> saves from. Routed through
    /// <see cref="GameSession.Submit"/>'s own <c>save &lt;path&gt;</c> command (<c>GameSession.Commands.cs</c>)
    /// — the real path every other command on this screen already goes through — rather than calling
    /// <c>SaveManager.WriteFile</c> directly.
    /// </summary>
    /// <remarks>
    /// Calls <see cref="GameSession.Submit"/> directly, then <see cref="OnCommandIssued"/>, rather than
    /// going through <see cref="SubmitForCheck"/> alone: <see cref="OnCommandIssued"/>'s own
    /// <see cref="_lastCommandLabel"/> update (<c>lines[^1]</c>) always lands on <see cref="SessionOutput"/>'s
    /// own trailing blank separator line, every command's shared convention, so it never actually shows a
    /// save's own outcome (or any command's). This method still runs the identical
    /// <see cref="OnCommandIssued"/> refresh (map, context panel, top bar) that <see cref="SubmitForCheck"/>
    /// would have, and additionally shows the real last non-empty line — <c>"Saved to '...'."</c> or a
    /// refusal — in <see cref="_saveConfirmationLabel"/>, a label this task's own narrow Save-action grant
    /// owns outright.
    /// </remarks>
    private void OnSavePressed() => OnSavePressed(checkUniqueSuffix: null);

    /// <summary>
    /// <paramref name="checkUniqueSuffix"/> (rework round 1, N2, non-blocking): a real player's click
    /// always calls the parameterless overload above, so their own save's file name is unchanged —
    /// <c>&lt;scenario&gt;-turn-&lt;N&gt;.json</c>, exactly as before. <see cref="PressSaveForCheck"/>
    /// passes a fixed, check-specific suffix instead, so a Godot check's own save can never collide with,
    /// overwrite or (after cleanup) delete a real save of the same scenario and turn — see that method's
    /// own remarks.
    /// </summary>
    private void OnSavePressed(string? checkUniqueSuffix)
    {
        var directory = ProjectSettings.GlobalizePath(SavesDirectory);
        Directory.CreateDirectory(directory);

        var fileName = checkUniqueSuffix is null
            ? $"{Session.Scenario.Id}-turn-{Session.State.Calendar.TurnIndex}.json"
            : $"{Session.Scenario.Id}-turn-{Session.State.Calendar.TurnIndex}-{checkUniqueSuffix}.json";
        var path = Path.Combine(directory, fileName);
        LastSavedPath = path;

        var output = Session.Submit($"save {path}");
        OnCommandIssued(output.Lines);
        _saveConfirmationLabel.Text = output.Lines.LastOrDefault(line => line.Length > 0) ?? string.Empty;
    }

    /// <summary>
    /// The path <see cref="OnSavePressed()"/> last wrote to (or attempted to), set just before it submits
    /// the real <c>save &lt;path&gt;</c> command — <see langword="null"/> before any save. Exposed, with
    /// <see cref="PressSaveForCheck"/>, so <c>godot/Checks/SaveResumeCheck.cs</c> and
    /// <c>godot/Checks/SaveResumeScreenshotTour.cs</c> can find the exact file the Save button produced
    /// without duplicating its naming rule.
    /// </summary>
    public string? LastSavedPath { get; private set; }

    /// <summary>
    /// Presses the "Save" button exactly as a real click would — public for the same reason
    /// <see cref="SubmitForCheck"/> and <see cref="LoadGameScreen.ContinueForCheck"/> are: a headless
    /// check drives the real handler, not a simulated mouse click at hardcoded coordinates.
    /// </summary>
    /// <param name="checkUniqueSuffix">
    /// N2 (rework round 1, non-blocking): a fixed, check-specific token appended to the saved file's own
    /// name (<c>&lt;scenario&gt;-turn-&lt;N&gt;-&lt;checkUniqueSuffix&gt;.json</c>), so a check's own save
    /// can never be mistaken for, collide with, overwrite, or — once the check cleans up after itself — a
    /// player's real save of the same scenario and turn. <see langword="null"/> (the default) reproduces
    /// a real click's own file name exactly, for a test that specifically wants that. Every caller under
    /// <c>godot/Checks/**</c> passes a distinct, non-empty value instead.
    /// </param>
    public void PressSaveForCheck(string? checkUniqueSuffix = null) => OnSavePressed(checkUniqueSuffix);

    /// <summary>
    /// T25: submits one raw command line through <see cref="GameSession.Submit"/> and runs the result
    /// through <see cref="OnCommandIssued"/> — exactly what <see cref="OnEndTurnPressed"/> already does for
    /// "end", exposed under its own name so a headless check can also drive an attack/siege command this
    /// way. Exposed rather than only reachable through <see cref="GameMapView"/>'s own private
    /// screen-to-tile click transform (<c>godot/UI/GameMapView.cs</c>, outside this task's Owns list) — the
    /// same reason <see cref="MapView"/>/<see cref="ContextPanel"/>/<see cref="NewsLog"/> are exposed for
    /// <c>godot/Checks/ScreenshotTour.cs</c>. A real click or context-panel button still reaches the
    /// identical <see cref="OnCommandIssued"/> pipeline through <see cref="GameMapView.CommandIssued"/> or
    /// <see cref="ContextPanel.CommandIssued"/> — this is not a second, parallel path, only a second way in.
    /// </summary>
    public void SubmitForCheck(string commandLine) => OnCommandIssued(Session.Submit(commandLine).Lines);

    private void OnCommandIssued(IReadOnlyList<string> lines)
    {
        _lastCommandLabel.Text = lines.Count == 0 ? string.Empty : lines[^1];
        _mapView.Refresh();
        _contextPanel.Refresh();
        RefreshTopBar();
        if (_newsLog.Visible)
        {
            _newsLog.Refresh();
        }

        // T25 (plan #474): every BattleResult this Submit call produced -- a human attack/siege, or any AI
        // seat's own battle played within an "end" -- queues its own BattleResultScreen. Shown one at a
        // time (ShowNextOverlay drains the queue as each is closed), so more than one battle in a single
        // "end" never stacks silently.
        foreach (var battle in Session.LastBattles)
        {
            _pendingBattleOverlays.Enqueue(battle);
        }

        ShowNextOverlay();
    }

    private void ShowNextOverlay()
    {
        if (_pendingBattleOverlays.Count > 0)
        {
            ShowBattleResultOverlay(_pendingBattleOverlays.Dequeue());
            return;
        }

        CheckForHotseatHandoff();
    }

    private void ShowBattleResultOverlay(Engine.Battle.BattleResult battle)
    {
        var screen = new BattleResultScreen { Session = Session, Result = BattleResultViewModel.FromResult(battle) };
        screen.Closed += () =>
        {
            CloseOverlay(screen);
            ShowNextOverlay();
        };
        ShowOverlay(screen);
    }

    /// <summary>
    /// T25 (plan #474, docs/game-design.md item 5): shows <see cref="HotseatHandoffScreen"/> whenever
    /// <see cref="HotseatHandoffDetector.Detect"/> finds that play just passed from one human seat to a
    /// different one. Called once every queued battle overlay has been dismissed (<see cref="ShowNextOverlay"/>),
    /// so a battle a human's own turn just resolved is always seen before the handoff that follows it.
    /// </summary>
    private void CheckForHotseatHandoff()
    {
        var info = HotseatHandoffDetector.Detect(
            Session.State, Session.Scenario, _lastKnownActiveNationId, _lastKnownActiveWasHuman);

        var activeNation = Session.State.NationById(Session.State.ActiveNationId);
        _lastKnownActiveNationId = Session.State.ActiveNationId;
        _lastKnownActiveWasHuman = activeNation?.Control == SeatControl.Human;

        if (info is null)
        {
            return;
        }

        var calendar = Session.State.Calendar;
        var screen = new HotseatHandoffScreen
        {
            Info = info,
            CalendarLine = $"Week {calendar.Week}, {calendar.YearBc} BC (turn {calendar.TurnIndex})",
        };
        screen.Continued += () => CloseOverlay(screen);
        ShowOverlay(screen);
    }

    /// <summary>
    /// T25: opens <see cref="Screens.DiplomacyScreen"/> — the bottom toolbar's own "Diplomacy" button
    /// calls this directly. Public, rather than only reachable through that button, so a headless check
    /// can call it directly too (<c>godot/Screens/Checks/**</c>) — the same convention
    /// <see cref="SubmitForCheck"/> already establishes for driving a command.
    /// </summary>
    public void OpenDiplomacyScreen()
    {
        var screen = new DiplomacyScreen { Session = Session };
        screen.CommandIssued += OnCommandIssued;
        screen.Closed += () => CloseOverlay(screen);
        ShowOverlay(screen);
    }

    private void ShowOverlay(Control overlay)
    {
        if (ActiveOverlay is { } previous)
        {
            RemoveChild(previous);
            previous.QueueFree();
        }

        ActiveOverlay = overlay;
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(overlay);
    }

    private void CloseOverlay(Control overlay)
    {
        if (!ReferenceEquals(ActiveOverlay, overlay))
        {
            return;
        }

        ActiveOverlay = null;
        RemoveChild(overlay);
        overlay.QueueFree();
    }

    private void RefreshTopBar()
    {
        var calendar = Session.State.Calendar;
        _calendarLabel.Text = $"Week {calendar.Week}, {calendar.YearBc} BC (turn {calendar.TurnIndex})";
        var nation = Session.State.NationById(Session.State.ActiveNationId);
        _activeNationLabel.Text = $"Playing: {nation?.Name ?? Session.State.ActiveNationId}";
    }
}
