using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Assets;
using IC2.Slice.Screens;

namespace IC2.Slice.UI;

/// <summary>
/// The main game screen — <c>docs/game-design.md</c> §"User interface" item 2, "the dominant,
/// near-always-visible view": the original's menu bar (<see cref="GameMenuBar"/>) and main toolbar
/// (<see cref="CommandToolbar"/>), a top bar (calendar, active seat), the map
/// (<see cref="GameMapView"/>), the persistent contextual side panel (<see cref="ContextPanel"/>), the
/// bottom filter toolbar, and the non-modal news log (<see cref="NewsLogPanel"/>).
/// </summary>
/// <remarks>
/// T25 (plan #474): also opens the three screens item 2 leads into —
/// <see cref="Screens.BattleResultScreen"/> after an attack or siege that resolved a battle
/// (<see cref="GameSession.Submit"/>'s own new <see cref="SessionOutput.Battles"/>, read from
/// <see cref="OnCommandIssued"/> so it fires whichever control actually issued the command —
/// <see cref="GameMapView"/>'s own map-click attack, or a future control of this screen's own), the
/// <see cref="Screens.DiplomacyScreen"/> from the Strategy menu's International relations entry (or its
/// toolbar button), and the
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

    /// <summary>
    /// T96: how many lines of the last command's outcome the label shows — its <em>last</em> lines, not
    /// its first (see <see cref="UpdateLastCommandLinesSkipped"/>), with the full block in the tooltip.
    /// Three lines are also the label's explicit minimum height: a Godot <see cref="Label"/> with
    /// autowrap plus clipping or an overrun behaviour reports a minimum size of (1, 1), so without the
    /// floor the root VBox lays it out 1&#160;px tall and it shows nothing (bug #484's symptom again;
    /// PR #521's re-review measured it as R1-B1).
    /// </summary>
    private const int LastCommandVisibleLineCount = 3;

    /// <summary>Exposed (rather than kept private) so <c>godot/Checks/ScreenshotTour.cs</c> can drive a
    /// selection and open the news log for the visual sign-off screenshot without simulating clicks.</summary>
    public GameMapView MapView => _mapView;

    public ContextPanel ContextPanel => _contextPanel;

    public NewsLogPanel NewsLog => _newsLog;

    /// <summary>
    /// T100: the one table the menu bar and the toolbar are built from. Exposed so
    /// <c>godot/Checks/MenuBarCheck.cs</c> can read its handler counter
    /// (<see cref="GameCommandTable.IssuedCount"/>) without re-deriving which commands were pressed.
    /// </summary>
    public GameCommandTable CommandTable { get; } = new();

    /// <summary>T100: the original's seven-menu menu bar. Exposed for <c>godot/Checks/MenuBarCheck.cs</c>.</summary>
    public GameMenuBar MenuBar { get; private set; } = null!;

    /// <summary>T100: the main shortcut toolbar. Exposed for the checks, and for
    /// <c>godot/Checks/MapClipCheck.cs</c> to read the moved Save/End turn buttons.</summary>
    public CommandToolbar Toolbar { get; private set; } = null!;

    /// <summary>Raised after any command issued through this screen, with the session's own output lines.
    /// The menu bar and toolbar raise it from <see cref="OnCommandIssued"/>, so a check can assert a
    /// disabled entry issues none (T100 Done-when 7).</summary>
    public event Action<IReadOnlyList<string>>? CommandIssued;

    /// <summary>
    /// When set, the Save handler appends this token to the file it writes — the same check-unique
    /// suffix seam <see cref="PressSaveForCheck"/> already gives (<c>godot/Checks/MenuBarCheck.cs</c>
    /// sets it before pressing the real File → Save menu item, so its save can never collide with a
    /// player's own).
    /// </summary>
    public string? CheckSaveSuffix { get; set; }

    /// <summary>The currently open modal overlay (a battle result, the diplomacy grid, the hotseat
    /// handoff, or T100's help/about), if any — <see langword="null"/> when the map is fully
    /// interactive. Exposed for <c>godot/Screens/Checks/**</c> and <c>godot/Checks/MenuBarCheck.cs</c>.</summary>
    public Control? ActiveOverlay { get; private set; }

    private bool _hintsEnabled = true;

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

        // T100: the original's menu bar and main toolbar sit above everything else, the way the
        // original's form puts them. Every entry and button is one row of CommandTable.
        MenuBar = new GameMenuBar { Table = CommandTable };
        root.AddChild(MenuBar);

        Toolbar = new CommandToolbar
        {
            Table = CommandTable,
            World = Session.World,
            AssetLoader = AssetPackTextureLoader.TryLoadPack(
                RepositoryRoot,
                SettingsScreen.SelectedPackId,
                onFailure: key => GD.PushWarning(
                    $"T100 toolbar: asset pack could not resolve or load '{key}'; falling back to the caption.")),
        };
        root.AddChild(Toolbar);

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

        // Fix #484 (review N1): a command's whole own outcome can carry a long rejection reason or a
        // Save path. Wrapping at word boundaries, a three-line ceiling with an ellipsis, and the full
        // text in the tooltip keep a long result from widening this full-rect root or pushing the
        // context panel off-screen.
        _lastCommandLabel = UiKit.MakeLabel(string.Empty, 12, UiKit.MutedTextColor);
        _lastCommandLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _lastCommandLabel.MaxLinesVisible = LastCommandVisibleLineCount;
        _lastCommandLabel.ClipText = true;
        _lastCommandLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        root.AddChild(_lastCommandLabel);

        // T96 (R1-B1): the wrap/clip/overrun combination above reports a (1, 1) minimum size, so the
        // root VBox used to lay the label out 1 px tall with zero visible lines -- #484's own symptom.
        // Three lines' height from the label's own theme font is the floor that keeps it visible. The
        // pitch is the glyph height plus the Label theme's own "line_spacing" constant (3 in Godot's
        // default theme): Label.GetLineHeight() returns only the former, and a floor of three glyph
        // heights lays out three lines but lets GetVisibleLineCount() count just two. The explicit size
        // also keeps the block's last-lines view (below) from ever collapsing.
        var lineSpacing = _lastCommandLabel.GetThemeConstant("line_spacing");
        var linePitch = _lastCommandLabel.GetLineHeight() + lineSpacing;
        _lastCommandLabel.CustomMinimumSize = new Vector2(
            0, Mathf.Ceil(LastCommandVisibleLineCount * linePitch));

        // T96 (B1 as displayed): the label shows the block's *last* lines, so an end's
        // "Now: Week …, Active seat: …" footer is what the player sees rather than the first AI-turn
        // lines. LinesSkipped is recomputed on every command and whenever a layout pass changes the
        // label's width (which is what decides how many lines the text wraps to).
        _lastCommandLabel.Resized += UpdateLastCommandLinesSkipped;

        _newsLog = new NewsLogPanel { Session = Session, CustomMinimumSize = new Vector2(0, 200) };
        root.AddChild(_newsLog);

        _mapView.CitySelected += id => _contextPanel.ShowCity(id);
        _mapView.ArmySelected += id => _contextPanel.ShowArmy(id);
        _mapView.FleetSelected += id => _contextPanel.ShowFleet(id);
        _mapView.SelectionCleared += () => _contextPanel.ShowNationOverview();
        _mapView.CommandIssued += OnCommandIssued;
        _contextPanel.CommandIssued += OnCommandIssued;

        // T99, the right-click route: the map's right button opens the clicked marker's unit list in
        // the panel (a city's garrison, an army's units, a fleet's ships and any army aboard) — a
        // panel view that is not a selection, so the map's own selection and any armed order are
        // untouched.
        _mapView.UnitListRequested += (entity, id) => _contextPanel.ShowUnitList(entity, id);

        // T99, the original's attack prompt (audit §2.1, confirmed: a click against a nation the
        // seat is not at war with asks before it orders): the map raises it, this screen owns the
        // prompt, and the answer goes back to the map — Yes submits the order, No drops the
        // selection. The engine composes the declaration of war itself, so nothing here submits one.
        _mapView.AttackConfirmationRequested += ShowAttackPrompt;

        BindCommands();

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

        // T100: the top bar keeps only the calendar and the active seat; T95's Save and End Turn buttons
        // moved into the toolbar above (File -> Save, Game -> End turn), which run the same handlers.
        // The confirmation/refusal line stays here (T95's own narrow "a Save action" grant owns it, and
        // fix #484 keeps it even though the shared last-command label now shows the same text).
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

        // T100: the bottom Cities/Armies/Fleets layer toggles stay until T110 replaces them. The
        // Diplomacy and News buttons that used to sit here moved into the toolbar's Strategy group
        // (Strategy -> International relations, Strategy -> News), which run the same handlers.
        row.AddChild(MakeFilterToggle("Cities", true, value => _mapView.ShowCities = value));
        row.AddChild(MakeFilterToggle("Armies", true, value => _mapView.ShowArmies = value));
        row.AddChild(MakeFilterToggle("Fleets", true, value => _mapView.ShowFleets = value));

        return bar;
    }

    private Control MakeFilterToggle(string label, bool initial, Action<bool> onToggled)
    {
        var button = new CheckButton { Text = label, ButtonPressed = initial };
        button.Toggled += onToggled.Invoke;
        return button;
    }

    /// <summary>
    /// T100: binds each <see cref="GameCommandRow.Wired"/> row to its handler, so the menu bar's entry
    /// and the toolbar's icon for one command run the identical code. The rows T100 does not wire
    /// (Taxation, Balance sheet, Recruit unit, Build fleet, the whole Area map and Unit map except
    /// Cancel selection, the 17 nations) stay unbound and are shown disabled by the two controls.
    /// </summary>
    private void BindCommands()
    {
        // File: New/Open/Close leave the game, each after a confirmation [designed]; Save and Save As
        // save. The seat commands are not in the table at all (the user's decision, T100's Scope).
        CommandTable.Bind("file.new", () => ConfirmLeavingGame("start a new game", () => OwningAppRoot?.ShowNewGameFlow()));
        CommandTable.Bind("file.open", () => ConfirmLeavingGame("open a saved game", () => OwningAppRoot?.ShowLoadGame()));
        CommandTable.Bind("file.save", () => OnSavePressed(checkUniqueSuffix: null));
        CommandTable.Bind("file.save_as", ShowSaveAsPrompt);
        CommandTable.Bind("file.close", () => ConfirmLeavingGame("return to the main menu", () => OwningAppRoot?.ShowMainMenu()));

        // Game: the one command, the same handler the old top-bar button called.
        CommandTable.Bind("game.end_turn", OnEndTurnPressed);

        // Strategy: News and International relations are the two whose screens already exist.
        CommandTable.Bind("strategy.news", () => _newsLog.Toggle());
        CommandTable.Bind("strategy.relations", OpenDiplomacyScreen);

        // Unit map: Cancel selection is T99's own handler (Shift+X already reaches it too).
        CommandTable.Bind("unit_map.cancel_selection", () => _mapView.ClearSelection());

        // Help: the new page, the hints toggle, and the about box.
        CommandTable.Bind("help.topics", ShowHelpPage);
        CommandTable.Bind("help.show_hints", ToggleHints);
        CommandTable.Bind("help.about", ShowAbout);
    }

    /// <summary>The <see cref="AppRoot"/> this screen was swapped into, or <see langword="null"/> when
    /// it is driven directly (a headless check). The three File commands that leave the game need it.</summary>
    private AppRoot? OwningAppRoot => GetParent() as AppRoot;

    /// <summary>Asks before leaving the current game (T100 Scope: New, Open and Close confirm; Save does
    /// not). [designed]: the original's own prompts are in the form stream and unread.</summary>
    private void ConfirmLeavingGame(string action, Action onConfirmed)
    {
        var prompt = new ConfirmPrompt { Question = $"The current game will be left to {action}. Continue?" };
        prompt.Confirmed += () =>
        {
            CloseOverlay(prompt);
            onConfirmed();
        };
        prompt.Refused += () => CloseOverlay(prompt);
        ShowOverlay(prompt);
    }

    /// <summary>Save As: a name prompt, then a save under <c>user://saves</c>. [designed]: the original
    /// used a file dialog; a Godot file dialog cannot run under the headless checks, and the task's own
    /// wording is "a name prompt, then a save".</summary>
    private void ShowSaveAsPrompt()
    {
        var prompt = new Control { Name = "SaveAsPrompt" };

        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.6f), MouseFilter = MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        prompt.AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        prompt.AddChild(center);

        var panel = UiKit.MakePanel(UiKit.PanelColor);
        panel.CustomMinimumSize = new Vector2(440, 0);
        center.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 12);
        panel.AddChild(column);

        column.AddChild(UiKit.MakeLabel("Save As", 18, UiKit.AccentColor));
        var nameEdit = new LineEdit { Text = $"{Session.Scenario.Id}-save" };
        column.AddChild(nameEdit);

        var buttons = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        buttons.AddThemeConstantOverride("separation", 10);
        column.AddChild(buttons);

        buttons.AddChild(UiKit.MakeButton("Cancel", () => CloseOverlay(prompt)));
        buttons.AddChild(UiKit.MakeButton("Save", () =>
        {
            CloseOverlay(prompt);
            SaveToName(nameEdit.Text);
        }));

        ShowOverlay(prompt);
    }

    private void SaveToName(string name)
    {
        var cleaned = name.Trim();
        if (cleaned.Length == 0)
        {
            cleaned = $"{Session.Scenario.Id}-save";
        }

        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            cleaned = cleaned.Replace(invalid, '_');
        }

        if (!cleaned.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            cleaned += ".json";
        }

        var directory = ProjectSettings.GlobalizePath(SavesDirectory);
        Directory.CreateDirectory(directory);
        SaveToFile(Path.Combine(directory, cleaned));
    }

    /// <summary>Show hints: flips the menu bar's and toolbar's tooltips and the menu check mark together
    /// (T100 Done-when 6). On by default and kept for the session.</summary>
    private void ToggleHints()
    {
        _hintsEnabled = !_hintsEnabled;
        MenuBar.SetHintsEnabled(_hintsEnabled);
        Toolbar.SetHintsEnabled(_hintsEnabled);
    }

    private void ShowHelpPage()
    {
        var page = new HelpPage();
        page.Closed += () => CloseOverlay(page);
        ShowOverlay(page);
    }

    private void ShowAbout()
    {
        var about = new AboutDialog();
        about.Closed += () => CloseOverlay(about);
        ShowOverlay(about);
    }

    private void OnEndTurnPressed() => SubmitForCheck("end");

    /// <summary>
    /// The Save command (File → Save, and the toolbar's Save button) — <c>docs/tasks/T95.md</c> (#467), Done-when 1: writes the current game to a
    /// file under <c>user://saves</c> <strong>[designed]</strong>, the same per-user directory
    /// <see cref="LoadGameScreen"/> already lists <c>*.json</c> saves from. Routed through
    /// <see cref="GameSession.Submit"/>'s own <c>save &lt;path&gt;</c> command (<c>GameSession.Commands.cs</c>)
    /// — the real path every other command on this screen already goes through — rather than calling
    /// <c>SaveManager.WriteFile</c> directly.
    /// </summary>
    /// <remarks>
    /// Calls <see cref="GameSession.Submit"/> directly, then <see cref="OnCommandIssued"/>, rather than
    /// going through <see cref="SubmitForCheck"/> alone: <see cref="OnCommandIssued"/>'s own
    /// <see cref="_lastCommandLabel"/> update now shows the command's whole own outcome (fix #484's
    /// <see cref="CommandOutcomeText.OutcomeBlock"/>), and this method additionally writes that same
    /// outcome — <c>"Saved to '...'."</c> or a refusal — into <see cref="_saveConfirmationLabel"/>, the
    /// dedicated label next to the top bar's Save button that T95 owns outright.
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

        var suffix = checkUniqueSuffix ?? CheckSaveSuffix;
        var fileName = suffix is null
            ? $"{Session.Scenario.Id}-turn-{Session.State.Calendar.TurnIndex}.json"
            : $"{Session.Scenario.Id}-turn-{Session.State.Calendar.TurnIndex}-{suffix}.json";
        SaveToFile(Path.Combine(directory, fileName));
    }

    /// <summary>
    /// Writes the current game to <paramref name="path"/> through the real <c>save &lt;path&gt;</c>
    /// command and shows its outcome, the one path both Save and Save As use.
    /// </summary>
    private void SaveToFile(string path)
    {
        LastSavedPath = path;

        var output = Session.Submit($"save {path}");
        OnCommandIssued(output.Lines);
        _saveConfirmationLabel.Text = CommandOutcomeText.OutcomeBlock(output.Lines);
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
    /// The text the shared last-command label (<see cref="_lastCommandLabel"/>, under the bottom
    /// toolbar) currently carries: fix #484's <see cref="CommandOutcomeText.OutcomeBlock"/> of the most
    /// recent <see cref="GameSession.Submit"/> call — the order's own acceptance or refusal lines (a
    /// composed declaration of war included), or an <c>end</c> round's closing summary up to its news.
    /// The label displays its last <see cref="LastCommandVisibleLineCount"/> wrapped lines and keeps the
    /// whole block in its tooltip; this property is the whole block, so a check that wants what is
    /// visible reads the label's own <see cref="Label.LinesSkipped"/> and <see cref="Label.GetVisibleLineCount"/>.
    /// Exposed for the same reason
    /// <see cref="LastSavedPath"/> is: <c>godot/Checks/CommandFeedbackCheck.cs</c> reads the real label
    /// after driving real commands, rather than re-deriving its text from a second copy of the rule.
    /// </summary>
    public string LastCommandText => _lastCommandLabel.Text;

    /// <summary>
    /// T95's own Save confirmation label's text, kept by fix #484 as its own dedicated slot even though
    /// the shared rule now gives <see cref="LastCommandText"/> the same text. Exposed for
    /// <c>godot/Checks/CommandFeedbackCheck.cs</c>, so the check can assert T95's label still shows the
    /// save's real outcome after the fix.
    /// </summary>
    public string SaveConfirmationText => _saveConfirmationLabel.Text;

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
    /// way. Returns the real <see cref="SessionOutput.Lines"/> so a check can pin the label against the
    /// engine's own output (for example the round footer's exact <c>"Now: Week …"</c> line) instead of
    /// re-deriving it. Exposed rather than only reachable through <see cref="GameMapView"/>'s own private
    /// screen-to-tile click transform (<c>godot/UI/GameMapView.cs</c>, outside this task's Owns list) — the
    /// same reason <see cref="MapView"/>/<see cref="ContextPanel"/>/<see cref="NewsLog"/> are exposed for
    /// <c>godot/Checks/ScreenshotTour.cs</c>. A real click or context-panel button still reaches the
    /// identical <see cref="OnCommandIssued"/> pipeline through <see cref="GameMapView.CommandIssued"/> or
    /// <see cref="ContextPanel.CommandIssued"/> — this is not a second, parallel path, only a second way in.
    /// </summary>
    public IReadOnlyList<string> SubmitForCheck(string commandLine)
    {
        var lines = Session.Submit(commandLine).Lines;
        OnCommandIssued(lines);
        return lines;
    }

    private void OnCommandIssued(IReadOnlyList<string> lines)
    {
        // Fix #484: Submit always ends its output with a blank separator line, so lines[^1] was always
        // empty and this label never showed a command's result. CommandOutcomeText.OutcomeBlock is the
        // screen's one shared rule (T95's Save label uses it too): the command's whole own outcome, up to
        // the round footer's News: section, trimmed, with whitespace-only spacer lines dropped. Every
        // input path -- map click, context-panel button, End Turn, SubmitForCheck, Save -- funnels
        // through here.
        CommandIssued?.Invoke(lines);

        var outcome = CommandOutcomeText.OutcomeBlock(lines);
        _lastCommandLabel.Text = outcome;
        _lastCommandLabel.TooltipText = outcome;
        UpdateLastCommandLinesSkipped();
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

    /// <summary>
    /// T99: the original's own attack prompt — a click that resolves to an attack, besiege or naval
    /// attack against a nation the active seat is not at war with asks before it orders. The prompt
    /// is the entry's own <see cref="ConfirmPrompt"/>; its answers go back to the map
    /// (<see cref="GameMapView.AnswerAttackConfirmation"/>), which holds the confirmed order.
    /// </summary>
    private void ShowAttackPrompt(MapClickOutcome outcome)
    {
        var prompt = new ConfirmPrompt { Question = outcome.ConfirmationText ?? string.Empty };
        prompt.Confirmed += () =>
        {
            CloseOverlay(prompt);
            _mapView.AnswerAttackConfirmation(yes: true);
        };
        prompt.Refused += () =>
        {
            CloseOverlay(prompt);
            _mapView.AnswerAttackConfirmation(yes: false);
        };
        ShowOverlay(prompt);
    }

    /// <summary>
    /// T99, cancel selection: <strong>Shift+X</strong> [confirmed:
    /// <c>ptolemy-run-ui-inventory-and-leader-draw.md</c> §4] and <strong>Esc</strong> [designed] clear
    /// the map's selection. Shift+X is the Unit map → Cancel selection table row's own shortcut, so the
    /// key runs the table's one handler — the same path the menu item and its toolbar icon take, and
    /// the same <see cref="GameCommandTable.IssuedCount"/> a check reads. The menu shows the shortcut
    /// in its tooltip but deliberately registers no live accelerator (rework B4): an accelerator acts
    /// in Godot's shortcut-input pass, before this guarded path, and would fire through a modal. An
    /// open overlay is modal: while one is up its own keys rule (<see cref="ConfirmPrompt"/>'s Esc
    /// answers its No), so this does nothing — the unhandled path only, so a focused control's own keys
    /// (a text field's, a button's) are never stolen.
    /// </summary>
    /// <remarks>
    /// Esc is [designed] because the audit's §1.8 ("Keyboard shortcuts found in the code") records that
    /// among the menu-item shortcuts <em>only Shift+X is observed</em> — the rest are [open] in the form
    /// stream — so no Esc cancel binding was found in the investigation
    /// (<c>original-ui-command-audit.md</c> §1.8; Shift+X itself is the §1.6 "Cancel selection" row's
    /// [confirmed: <c>ptolemy-run-ui-inventory-and-leader-draw.md</c> §4] shortcut). The task entry tags
    /// Esc [designed] as well.
    /// </remarks>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (ActiveOverlay is not null)
        {
            return;
        }

        // Shift+X is the Unit map → Cancel selection row's own shortcut, so the key goes through the
        // table's one handler — the same path the menu item and its toolbar icon take, and the same
        // IssuedCount the check reads (T100 Done-when 5 and rework N5).
        if (@event is InputEventKey { Pressed: true, Keycode: Key.X, ShiftPressed: true })
        {
            if (CommandTable.TryInvoke("unit_map.cancel_selection"))
            {
                GetViewport().SetInputAsHandled();
            }

            return;
        }

        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            _mapView.ClearSelection();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>
    /// T96 (B1 as displayed): keeps the label's window on the <em>last</em> <see
    /// cref="LastCommandVisibleLineCount"/> lines of its wrapped text. <see cref="Label.MaxLinesVisible"/>
    /// alone caps how many lines are drawn but draws the <em>first</em> ones, which hid an <c>end</c>'s
    /// <c>Now: Week …</c> footer; <see cref="Label.LinesSkipped"/> drops the lines above the tail.
    /// <see cref="Label.GetLineCount"/> shapes the text at the label's current width, so this runs after
    /// every text change and on every resize: a command issued before the first layout pass is
    /// recomputed once the label gets its real width.
    /// </summary>
    private void UpdateLastCommandLinesSkipped()
    {
        var lineCount = _lastCommandLabel.GetLineCount();
        _lastCommandLabel.LinesSkipped = Mathf.Max(0, lineCount - LastCommandVisibleLineCount);
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
    /// T25: opens <see cref="Screens.DiplomacyScreen"/> — the Strategy menu's International relations entry and its toolbar button
    /// call this. Public, rather than only reachable through them, so a headless check
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
