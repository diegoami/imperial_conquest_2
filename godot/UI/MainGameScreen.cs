using IC2.Slice.Diagnostics;
using Godot;
using IC2.Engine.Armies;
using IC2.Engine.Import;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Assets;
using IC2.Slice.Screens;

namespace IC2.Slice.UI;

/// <summary>
/// The main game screen — <c>docs/game-design.md</c> §"User interface" item 2, "the dominant,
/// near-always-visible view": the original's menu bar (<see cref="GameMenuBar"/>) and main toolbar
/// (<see cref="CommandToolbar"/>), a top bar (calendar, active seat), the map
/// (<see cref="GameMapView"/>), the persistent contextual side panel (<see cref="ContextPanel"/>), and
/// the non-modal news log (<see cref="NewsLogPanel"/>).
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

    /// <summary>Bug #919: the save file this game was opened from (File -> Open), or null for a new game.
    /// Save writes back to it until a Save As names another file.</summary>
    public string? OpenedPath { get; init; }

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

    public StrategicTravelPlayback StrategicTravel { get; } = new();
    public GameState DisplayState => StrategicTravel.Resolve(Session.State, _displayViewer);
    private bool _closing;
    private string _displayViewer = string.Empty;
    private HBoxContainer _travelControls = null!;

    public override void _Process(double delta) => StrategicTravel.Advance(delta);

    public override void _Input(InputEvent @event)
    {
        if (_mapView is not null && _mapView.TravelTargetPickArmed
            && @event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        {
            _mapView.CancelTravelTargetPick();
            GetViewport().SetInputAsHandled();
            return;
        }
        if (StrategicTravel.IsActive && @event is InputEventKey { Pressed: true, Echo: false } key
            && (key.Keycode == Key.Escape || (key.Keycode == Key.X && key.ShiftPressed)))
        {
            SkipTravelAndClearSelection();
            GetViewport().SetInputAsHandled();
        }
    }

    private void ReconcileDisplaySelection() =>
        _mapView.ReconcileSelection(_selectedArmyId, _selectedFleetId, _selectedCityId);

    private void OnStrategicBoundaryCompleted(StrategicTravelStep step) => ReconcileDisplaySelection();

    private bool AllowStrategicInput()
    {
        if (!_closing && StrategicTravel.CommandsAllowed) return true;
        if (!_closing) ShowScreenMessage("Movement playing · orders locked · Skip or Fast to continue.");
        return false;
    }

    private void BindStrategicCommand(string id, Action handler)
    {
        CommandTable.Bind(id, () =>
        {
            if (id == "unit_map.cancel_selection") { SkipTravelAndClearSelection(); return; }
            if (id is "file.close" or "game.settings" || id.StartsWith("help.", StringComparison.Ordinal)
                || id is "file.save" or "file.save_as" || AllowStrategicInput())
            {
                if (id != "unit_map.army_set_target" && _mapView.TravelTargetPickArmed)
                {
                    _mapView.CancelTravelTargetPick();
                    UpdateCommandStrip();
                }
                handler();
            }
        });
    }

    private void SkipTravelAndClearSelection()
    {
        AppDiagnostics.Mark(DiagnosticEvent.Skip, "travel");
        StrategicTravel.Skip();
        _mapView.CancelTravelTargetPick();
        _mapView.ClearSelection();
    }

    private void RefreshTravelDisplay()
    {
        if (_closing || _contextPanel is null || _areaMapView is null) return;
        _mapView.Refresh(); _contextPanel.Refresh(); _areaMapView.Refresh();
        RefreshTopBar(); UpdateCommandStrip();
        _travelControls.Visible = StrategicTravel.IsActive;
    }


    /// <summary>
    /// T100: the one table the menu bar and the toolbar are built from. Exposed so
    /// <c>godot/Checks/MenuBarCheck.cs</c> can read its handler counter
    /// (<see cref="GameCommandTable.IssuedCount"/>) without re-deriving which commands were pressed.
    /// </summary>
    public GameCommandTable CommandTable => _commandTable ??= new GameCommandTable(Session.World);

    private GameCommandTable? _commandTable;

    /// <summary>T100: the original's seven-menu menu bar. Exposed for <c>godot/Checks/MenuBarCheck.cs</c>.</summary>
    public GameMenuBar MenuBar { get; private set; } = null!;

    /// <summary>T100: the main shortcut toolbar. Exposed for the checks, and for
    /// <c>godot/Checks/MapClipCheck.cs</c> to read the moved Save/End turn buttons.</summary>
    public CommandToolbar Toolbar { get; private set; } = null!;

    /// <summary>T112: the Unit map's context strip. Exposed for <c>godot/Checks/FleetCityOrdersCheck.cs</c>.</summary>
    public UnitCommandStrip CommandStrip => _commandStrip;
    public Button RetreatSupplyButtonForCheck => _retreatSupplyButton;
    public Button RetreatSplitButtonForCheck => _retreatSplitButton;
    public string RetreatSupplyStatusForCheck => _retreatSupplyLabel.Text;
    public Button RallyPointSetButtonForCheck => _rallyPointSetButton;
    public Button RallyPointClearButtonForCheck => _rallyPointClearButton;

    /// <summary>T110: the overview mini-map, with its highlight layer. Exposed so
    /// <c>godot/Checks/NationsAreaMapCheck.cs</c> can read the highlight set.</summary>
    public AreaMapView AreaMapView => _areaMapView;

    /// <summary>
    /// T110: the viewed nation — one of the 16, or <see langword="null"/> for All nations (the original's
    /// index 16, audit §1.4). It scopes the status panel and the Area-map highlights. Starts as the active
    /// seat's nation and follows the active seat when a turn starts [designed].
    /// </summary>
    public string? ViewedNationId { get; private set; }

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

    /// <summary>
    /// T134: the last screen-level message shown when Supply army opened nothing — the "select an army"
    /// or "no provider" line. Distinct from a command's own outcome (it is not a command), so the
    /// headless check can read it without a <see cref="CommandIssued"/> firing.
    /// </summary>
    public string LastMessageForCheck => _lastCommandLabel.Text;

    private bool _hintsEnabled = true;

    // T132: the right-hand column and the narrow button on its left edge that hides and shows it. The
    // shown/hidden choice is SidePanelToggle's static, so a New Game or Load in the same run keeps it.
    private readonly SidePanelToggle _sidePanelToggle = new();
    private VBoxContainer _sideColumn = null!;
    private Button _sidePanelButton = null!;

    /// <summary>T132: the whole right-hand column (Area-map strip, mini-map, context panel) — exposed
    /// for <c>godot/Checks/SidePanelToggleCheck.cs</c>.</summary>
    public Control SideColumn => _sideColumn;

    /// <summary>T132: the button that hides and shows <see cref="SideColumn"/> — exposed for
    /// <c>godot/Checks/SidePanelToggleCheck.cs</c>.</summary>
    public Button SidePanelButton => _sidePanelButton;

    private Label _calendarLabel = null!;
    private Label _activeNationLabel = null!;
    private Label _saveConfirmationLabel = null!;
    private GameMapView _mapView = null!;
    private AreaMapView _areaMapView = null!;
    private ContextPanel _contextPanel = null!;
    private NewsLogPanel _newsLog = null!;
    private Label _lastCommandLabel = null!;

    // #1286: a refused order's message over the map; dialogs show their own reply instead.
    private readonly OrderNotice _orderNotice = new();
    private bool _submittingFromDialog;

    // T147 (bug #781 point 4): the NewsLog slots after the previous command, so the output area can show
    // the entries this command added (the peace treaty's "have agreed to end their war." line among
    // them). Every command path funnels through OnCommandIssued, which updates it. The entries compare
    // by reference, not by count: NewsLog.Append evicts once the ring buffer is full, so a full log's
    // count never grows and a count-based range would hide every later order's news (Sol's review R1).
    private IReadOnlyList<NewsEntry> _newsEntriesBeforeCommand = Array.Empty<NewsEntry>();

    private readonly Queue<Engine.Battle.BattleResult> _pendingBattleOverlays = new();

    // T127: the army pairs (attacker, defender) the human fought on the board in this screen's life.
    private readonly HashSet<(string Attacker, string Defender)> _boardBattleArmies = new();

    /// <summary>Fix #917: the turn-start offer notices the engine announced, shown one at a time by
    /// <see cref="ShowNextOverlay"/> once any battle, fall, hand-off and peace window has had its turn.</summary>
    private readonly Queue<string> _pendingOfferNotices = new();

    // T138: one window per human seat that fell on the last Submit call, in the order the engine recorded
    // them. Drained by ShowNextOverlay after the call's battle windows and before a hotseat hand-off.
    private readonly Queue<SeatFall> _pendingGameEndOverlays = new();

    // T116: the rule that decides which of a Submit call's battles are shown now and which are held,
    // per seat, until that seat's own turn start. Holds only battle results; the human-seat list is read
    // from Session.State on each call (see OnCommandIssued), so a seat deposed mid-game is dropped.
    private readonly BattleReportRouter _battleReportRouter = new();
    private readonly ButtonGroup _nationSwatchGroup = new();
    private string? _lastKnownActiveNationId;
    private int _lastKnownTurnIndex;
    private bool _lastKnownActiveWasHuman;

    // T134: the current map selection, so the Army menu's Supply army entry can act on the selected army
    // (or the army a selected fleet carries). The context panel owns the view; this is only the id pair
    // the command needs, kept in step with the map's own selection events.
    private string? _selectedArmyId;
    private string? _selectedFleetId;

    // T112: the selected city, so the City menu's Fortify city entry and the command strip can act on it.
    private string? _selectedCityId;

    // T112: the Unit map's context strip, placed below the main toolbar.
    private UnitCommandStrip _commandStrip = null!;
    private Label _travelTargetLabel = null!;
    private HBoxContainer _retreatSupplyRow = null!;
    private Label _retreatSupplyLabel = null!;
    private Button _retreatSupplyButton = null!;
    private Button _retreatSplitButton = null!;
    private HBoxContainer _rallyPointRow = null!;
    private Label _rallyPointLabel = null!;
    private Button _rallyPointSetButton = null!;
    private Button _rallyPointClearButton = null!;
    private string? _rallyPointPickNationId;

    // T149: the cue player, built once in _Ready, parented to the root, and wired to one
    // subscription on Session.EventsPublished (below). The only Owns slice this screen carries
    // for the cue list: the player's creation and that one subscription. Every other Owns line
    // (godot/Audio/SoundPlayer.cs, godot/Audio/SoundCues.cs) lives in their own files.
    private Audio.SoundPlayer _soundPlayer = null!;

    public override void _Ready()
    {
        AppDiagnostics.Detail(DiagnosticEvent.Settings, "Travel", paused: StrategicTravel.Paused, fast: StrategicTravel.Fast);
        UiKit.ApplyBackground(this, UiKit.Background);

        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);

        // T100: the original's menu bar and main toolbar sit above everything else, the way the
        // original's form puts them. Every entry and button is one row of CommandTable.
        MenuBar = new GameMenuBar { Table = CommandTable };
        var menuRow = new HBoxContainer { Name = "MenuAndCampaignStatus" };
        menuRow.AddChild(MenuBar);
        menuRow.AddChild(BuildTopBar());
        root.AddChild(menuRow);

        // One pack loader for the main toolbar and T110's Area-map strip, so both draw the same keys and
        // a missing texture falls back the same way in each.
        var assetLoader = AssetPackTextureLoader.TryLoadPack(
            RepositoryRoot,
            SettingsScreen.SelectedPackId,
            onFailure: key => GD.PushWarning(
                $"T100 toolbar: asset pack could not resolve or load '{key}'; falling back to the caption."));

        // T149: the sound player reads the same pack the toolbar and map read, so a "key the pack
        // lacks plays nothing" stays true when the player changes packs. Created before the
        // subscription below so OnSessionEventsPublished never sees a null player.
        _soundPlayer = new Audio.SoundPlayer(
            Path.Combine(RepositoryRoot, "assets", "packs", SettingsScreen.SelectedPackId ?? Assets.AssetPackManifestLoader.DefaultPackId));
        AddChild(_soundPlayer);
        _soundPlayer.SoundEnabled = () => SettingsScreen.SoundEnabled;
        _soundPlayer.Muted = !SettingsScreen.SoundEnabled;
        Session.EventsPublished += OnSessionEventsPublished;

        Toolbar = new CommandToolbar
        {
            Table = CommandTable,
            World = Session.World,
            AssetLoader = assetLoader,
        };
        root.AddChild(Toolbar);

        // T112: the Unit map's context strip — the selected unit's own group, shown below the main
        // toolbar. It is hidden while nothing (or a foreign unit) is selected.
        _commandStrip = new UnitCommandStrip { Table = CommandTable, AssetLoader = assetLoader };
        root.AddChild(_commandStrip);
        _travelTargetLabel = UiKit.MakeLabel(string.Empty, AppTheme.Note, UiKit.MutedTextColor);
        _travelTargetLabel.ClipText = true;
        _travelTargetLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        var travelTargetRow = new HBoxContainer { Visible = false };
        travelTargetRow.AddChild(_travelTargetLabel);
        root.AddChild(travelTargetRow);

        _retreatSupplyRow = new HBoxContainer { Visible = false };
        _retreatSupplyLabel = UiKit.MakeLabel(string.Empty, AppTheme.Note, UiKit.MutedTextColor);
        _retreatSupplyLabel.Visible = false;
        _retreatSupplyLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _retreatSupplyRow.AddChild(_retreatSupplyLabel);
        _retreatSupplyButton = new Button { FocusMode = FocusModeEnum.None, ToggleMode = true };
        QolCommandIcons.Apply(_retreatSupplyButton, "recover", "Retreat and supply");
        _retreatSupplyButton.Pressed += ToggleRetreatSupplyMode;
        _retreatSupplyRow.AddChild(_retreatSupplyButton);
        _retreatSplitButton = new Button { Text = "Suggest split", FocusMode = FocusModeEnum.None };
        QolCommandIcons.Apply(_retreatSplitButton, "split", "Suggest a balanced split for supply");
        _retreatSplitButton.Pressed += () => OpenSplitArmyDialog(suggestBalanced: true);
        _retreatSupplyRow.AddChild(_retreatSplitButton);
        Toolbar.ExtraActions.AddChild(_retreatSupplyRow);

        _rallyPointRow = new HBoxContainer();
        _rallyPointLabel = UiKit.MakeLabel(string.Empty, AppTheme.Note, UiKit.MutedTextColor);
        _rallyPointLabel.Visible = false;
        _rallyPointLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _rallyPointRow.AddChild(_rallyPointLabel);
        _rallyPointSetButton = new Button { Text = "Set rally point", FocusMode = FocusModeEnum.None };
        QolCommandIcons.Apply(_rallyPointSetButton, "rally", "Set rally point");
        _rallyPointSetButton.Pressed += ArmNationRallyPointPick;
        _rallyPointRow.AddChild(_rallyPointSetButton);
        _rallyPointClearButton = new Button { Text = "Clear rally point", FocusMode = FocusModeEnum.None };
        QolCommandIcons.Apply(_rallyPointClearButton, "rally_clear", "Clear rally point");
        _rallyPointClearButton.Pressed += ClearNationRallyPoint;
        _rallyPointRow.AddChild(_rallyPointClearButton);
        Toolbar.ExtraActions.AddChild(_rallyPointRow);

        // A plain HBoxContainer, not HSplitContainer: SplitContainer's own split_offset sign convention
        // left the context panel with no visible width at all (T24's first pass) -- simpler and
        // predictable is the map taking whatever the panel's own fixed CustomMinimumSize does not.
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        root.AddChild(body);

        _displayViewer = Session.State.ActiveNationId;
        _mapView = new GameMapView
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill,
            DisplayStateSource = () => DisplayState, TravelPlayback = StrategicTravel,
            StrategicInputAllowed = AllowStrategicInput,
        };
        body.AddChild(_mapView);
        _mapView.AddChild(_orderNotice);

        // T102/T110: the right-hand column is the Area-map strip above the overview mini-map above the
        // context panel. The mini-map's own 320 px width stays inside the context panel's 340 px floor, and
        // the strip wraps into six columns, so the column -- and the screen -- is never widened. The panel
        // keeps ExpandFill, so it takes the rest of the column below.
        // T132: a narrow button in the body row, between the map and the column, so it stays on screen
        // while the column is hidden. The column hides with Visible = false (not a zero width, which
        // would leave its 340 px floor in the row).
        _sidePanelButton = new Button
        {
            CustomMinimumSize = new Vector2(24, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.None,
        };
        _sidePanelButton.Pressed += ToggleSidePanel;
        body.AddChild(_sidePanelButton);

        var sideColumn = new VBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        _sideColumn = sideColumn;
        body.AddChild(sideColumn);
        ApplySidePanelState();

        // T132 (R3): every live screen follows the shared state; _ExitTree unsubscribes.
        SidePanelToggle.Changed += ApplySidePanelState;

        _areaMapView = new AreaMapView
        {
            DisplayStateSource = () => DisplayState,
            Table = CommandTable,
            AssetLoader = assetLoader,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        sideColumn.AddChild(_areaMapView.BuildStrip());
        sideColumn.AddChild(_areaMapView);

        _contextPanel = new ContextPanel
        {
            DisplayStateSource = () => DisplayState,
            Session = Session,
            MapView = _mapView,
            CustomMinimumSize = new Vector2(340, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        sideColumn.AddChild(_contextPanel);

        // Fix #484 (review N1): a command's whole own outcome can carry a long rejection reason or a
        // Save path. Wrapping at word boundaries, a three-line ceiling with an ellipsis, and the full
        // text in the tooltip keep a long result from widening this full-rect root or pushing the
        // context panel off-screen.
        _lastCommandLabel = UiKit.MakeLabel(string.Empty, AppTheme.Note, UiKit.TextColor);
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
        // T169: negative spacing compacts gaps, but the floor still needs three glyph heights
        // for GetVisibleLineCount() to retain the final Now: Week line.
        var linePitch = _lastCommandLabel.GetLineHeight() + Mathf.Max(0, lineSpacing);
        _lastCommandLabel.CustomMinimumSize = new Vector2(
            0, Mathf.Ceil(LastCommandVisibleLineCount * linePitch));

        // T96 (B1 as displayed): the label shows the block's *last* lines, so an end's
        // "Now: Week …, Active seat: …" footer is what the player sees rather than the first AI-turn
        // lines. LinesSkipped is recomputed on every command and whenever a layout pass changes the
        // label's width (which is what decides how many lines the text wraps to).
        _lastCommandLabel.Resized += UpdateLastCommandLinesSkipped;

        _newsLog = new NewsLogPanel { Session = Session, CustomMinimumSize = new Vector2(0, 200) };
        root.AddChild(_newsLog);

        _mapView.CitySelected += id =>
        {
            _selectedArmyId = null;
            _selectedFleetId = null;
            _selectedCityId = id;
            _contextPanel.ShowCity(id);
            UpdateCommandStrip();
        };
        _mapView.ArmySelected += id =>
        {
            _selectedArmyId = id;
            _selectedFleetId = null;
            _selectedCityId = null;
            _contextPanel.ShowArmy(id);
            UpdateCommandStrip();
        };
        _mapView.FleetSelected += id =>
        {
            _selectedArmyId = null;
            _selectedFleetId = id;
            _selectedCityId = null;
            _contextPanel.ShowFleet(id);
            UpdateCommandStrip();
        };
        _mapView.SelectionCleared += () =>
        {
            _selectedArmyId = null;
            _selectedFleetId = null;
            _selectedCityId = null;
            _contextPanel.ShowNationOverview();
            UpdateCommandStrip();
        };
        _mapView.CommandIssued += OnCommandIssued;
        _contextPanel.CommandIssued += OnCommandIssued;

        // T99, the right-click route: the map's right button opens the clicked marker's unit list in
        // the panel (a city's garrison, an army's units, a fleet's ships and any army aboard) — a
        // panel view that is not a selection, so the map's own selection and any armed order are
        // untouched.
        // T113 (the 2026-10-05 amendment, also brief Done-when 6): a city's right click lists the
        // mercenary offers on its tile rather than its garrison, matching the original's own
        // `TInformation_ShowCityUnits` literal "Mercenaries at <city>" [Wine candidate: feature
        // inventory row UM07; derived: code, the literals]. An army's or fleet's right click still
        // lists its own units. The view and the dialog share `MercenaryDialogModel`, so the listing
        // and the dialog cannot disagree.
        _mapView.UnitListRequested += (entity, id) =>
        {
            if (entity == MapEntityKind.City)
            {
                _contextPanel.ShowMercenariesAtCity(id);
            }
            else
            {
                _contextPanel.ShowUnitList(entity, id);
            }
        };

        // T99, the original's attack prompt (audit §2.1, confirmed: a click against a nation the
        // seat is not at war with asks before it orders): the map raises it, this screen owns the
        // prompt, and the answer goes back to the map — Yes submits the order, No drops the
        // selection. The engine composes the declaration of war itself, so nothing here submits one.
        _mapView.AttackConfirmationRequested += ShowAttackPrompt;
        _mapView.HintRequested += ShowScreenMessage;
        _mapView.TravelTargetPicked += HandlePickedMapTarget;
        _mapView.TravelTargetPickChanged += UpdateCommandStrip;
        _mapView.TravelTargetPickCancelled += () =>
        {
            _rallyPointPickNationId = null;
            UpdateCommandStrip();
        };

        BindCommands();
        WireNationSwatches();

        // T147 (bug #781 point 4): the news log as the first command finds it; a command's own additions
        // are the entries not among these (compared by reference, see the field).
        _newsEntriesBeforeCommand = Session.State.NewsLog.Slots;

        _contextPanel.DisplayStateSource = () => DisplayState;
        _areaMapView.DisplayStateSource = () => DisplayState;
        StrategicTravel.IntersectsViewport = _mapView.RouteIntersectsViewport;
        StrategicTravel.Changed += RefreshTravelDisplay;
        StrategicTravel.BoundaryCompleted += OnStrategicBoundaryCompleted;
        _mapView.Attach(Session, RepositoryRoot);
        _areaMapView.Attach(Session, _mapView);

        var activeNation = Session.State.NationById(Session.State.ActiveNationId);
        _lastKnownActiveNationId = Session.State.ActiveNationId;
        _lastKnownTurnIndex = Session.State.Calendar.TurnIndex;
        _lastKnownActiveWasHuman = activeNation?.Control == SeatControl.Human;

        // T110 [designed]: a new game starts with the active seat's nation viewed, and a turn's start
        // follows the new active seat (OnCommandIssued). This also paints the panel's first contents.
        SetViewedNation(Session.State.ActiveNationId);
        RefreshTopBar();

        // T127: a loaded game that holds an open tactical battle reopens the battle screen at once.
        if (Session.OpenTacticalBattle is not null)
        {
            ShowNextOverlay();
        }
    }

    private Control BuildTopBar()
    {
        var row = new HBoxContainer { Name = "CampaignStatus", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", AppTheme.DialogGap);

        _calendarLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.TextColor);
        _calendarLabel.Name = "CampaignDate";
        row.AddChild(_calendarLabel);

        _activeNationLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.AccentColor);
        row.AddChild(_activeNationLabel);

        _travelControls = new HBoxContainer { Visible = false };
        _travelControls.AddChild(UiKit.MakeLabel("Movement · orders locked", AppTheme.Body, UiKit.TextColor));
        var skip = new Button { Text = "Skip" };
        skip.Pressed += () => { AppDiagnostics.Mark(DiagnosticEvent.Skip, "travel"); StrategicTravel.Skip(); };
        var fast = new Button { Text = "Fast", ToggleMode = true };
        fast.Toggled += pressed => { StrategicTravel.Fast = pressed; AppDiagnostics.Detail(DiagnosticEvent.Settings, "Travel", paused: StrategicTravel.Paused, fast: pressed); };
        _travelControls.AddChild(skip); _travelControls.AddChild(fast);
        row.AddChild(_travelControls);
        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(spacer);

        // T100: the top bar keeps only the calendar and the active seat; T95's Save and End Turn buttons
        // moved into the toolbar above (File -> Save, Game -> End turn), which run the same handlers.
        // The confirmation/refusal line stays here (T95's own narrow "a Save action" grant owns it, and
        // fix #484 keeps it even though the shared last-command label now shows the same text).
        //
        // Fix #537: a Save's confirmation carries the whole user:// path; as a plain label it set the
        // row's -- and so the root's -- minimum width to ~1730px, past the viewport, cropping the
        // context panel. It now ellipsizes like _lastCommandLabel (clip + TrimEllipsis, full text in
        // the tooltip) and expands into the free space the spacer leaves, so it is clipped to the bar
        // rather than collapsing to its near-zero clipped minimum.
        _saveConfirmationLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.MutedTextColor);
        _saveConfirmationLabel.ClipText = true;
        _saveConfirmationLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _saveConfirmationLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _saveConfirmationLabel.HorizontalAlignment = HorizontalAlignment.Right;
        row.AddChild(_saveConfirmationLabel);

        // Keep the date at the far right beside the menu instead of spending a separate map row.
        // Settings remains available from Game -> Settings.
        row.MoveChild(_calendarLabel, row.GetChildCount() - 1);
        return row;
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
        BindStrategicCommand("file.new", () => ConfirmLeavingGame(NewGameAction, () => OwningAppRoot?.ShowNewGameFlow()));
        BindStrategicCommand("file.open", () => ConfirmLeavingGame("open a saved game", () => OwningAppRoot?.ShowLoadGame()));
        BindStrategicCommand("file.save", () => OnSavePressed(checkUniqueSuffix: null));
        BindStrategicCommand("file.save_as", ShowSaveAsPrompt);
        BindStrategicCommand("file.close", () => ConfirmLeavingGame("return to the main menu", () => OwningAppRoot?.ShowMainMenu()));

        // Game: the one command, the same handler the old top-bar button called.
        BindStrategicCommand("game.end_turn", OnEndTurnPressed);

        // Strategy: News and International relations are the two whose screens already exist; the
        // other four (Taxation, Balance sheet, Recruit unit, Build fleet) open T109's dialogs.
        BindStrategicCommand("strategy.news", () => _newsLog.Toggle());
        BindStrategicCommand("strategy.relations", OpenDiplomacyScreen);
        BindStrategicCommand("strategy.taxation", OpenTaxationDialog);
        BindStrategicCommand("strategy.balance_sheet", OpenBalanceSheetDialog);
        BindStrategicCommand("strategy.recruit_unit", OpenRecruitUnitDialog);
        BindStrategicCommand("strategy.build_fleet", OpenBuildFleetDialog);

        // Unit map: Cancel selection is T99's own handler (Shift+X already reaches it too).
        BindStrategicCommand("unit_map.cancel_selection", () => { _mapView.CancelTravelTargetPick(); _mapView.ClearSelection(); });

        // T134: Army -> Supply army opens the SupplyDialog for the selected army (or the army a selected
        // fleet carries), or shows one of the two messages and opens nothing.
        BindStrategicCommand("unit_map.army_supply", OpenSupplyArmyDialog);

        // T111: the rest of the Army submenu. Transfer unit and Split army open their dialogs; Join
        // armies submits at once with the game's own partner; Change units opens its dialog; Disband army
        // asks first. Recruit mercenaries is T113 (now wired): opens the dialog for the chosen city.
        BindStrategicCommand("unit_map.army_transfer_unit", OpenArmyTransferDialog);
        BindStrategicCommand("unit_map.army_split", OpenSplitArmyDialog);
        BindStrategicCommand("unit_map.army_split_two", () => OpenSplitArmyDialog(suggestBalanced: true));
        BindStrategicCommand("unit_map.army_set_target", ArmArmyTravelTarget);
        BindStrategicCommand("unit_map.army_cancel_target", CancelArmyTravelTarget);
        BindStrategicCommand("unit_map.army_join", JoinArmiesFromMenu);
        BindStrategicCommand("unit_map.army_change_units", OpenChangeUnitsDialog);
        BindStrategicCommand("unit_map.army_disband", ConfirmDisbandArmy);
        BindStrategicCommand("unit_map.army_recruit_mercenaries", OpenRecruitMercenariesDialog);

        // T112: the Fleet submenu. Supply fleet and the three dialogs open overlays; Join fleets submits at
        // once with the game's own partner; Scuttle fleet asks first.
        BindStrategicCommand("unit_map.fleet_supply", OpenSupplyFleetDialog);
        BindStrategicCommand("unit_map.fleet_repair", OpenRepairFleetDialog);
        BindStrategicCommand("unit_map.fleet_transfer_ships", OpenFleetTransferDialog);
        BindStrategicCommand("unit_map.fleet_split", OpenSplitFleetDialog);
        BindStrategicCommand("unit_map.fleet_join", JoinFleetsFromMenu);
        BindStrategicCommand("unit_map.fleet_scuttle", ConfirmScuttleFleet);

        // T112: the City submenu's one entry.
        BindStrategicCommand("unit_map.city_fortify", OpenFortifyCityDialog);

        // Help: the new page, the hints toggle, and the about box.
        BindStrategicCommand("help.topics", ShowHelpPage);
        BindStrategicCommand("help.show_hints", ToggleHints);
        BindStrategicCommand("help.about", ShowAbout);
        BindStrategicCommand("game.settings", ShowSettings);

        // T110: the Nations menu and its toolbar swatches set the viewed nation. None of these submits a
        // command (Done-when 7) — only the panel and the highlight scope change.
        foreach (var row in CommandTable.MenuRows.Where(
            r => string.Equals(r.Menu, "Nations", StringComparison.Ordinal)))
        {
            var nationId = string.Equals(row.Id, "nations.all", StringComparison.Ordinal)
                ? null
                : row.Id["nations.".Length..];
            BindStrategicCommand(row.Id, () => SetViewedNation(nationId));
        }

        // T110: the Area map's five Show entries toggle their own highlight layer and its check marks;
        // Find a city opens the TFindCity dialog. T113: the six Show mercenaries rows join them —
//        each type toggles its own layer, "All mercenaries" toggles the five mercenary layers
//        together (mirroring the audit's own "All mercenaries" union of the type bitmaps).
        BindStrategicCommand("area_map.show_cities", () =>
        {
            _areaMapView.ToggleHighlight(AreaMapHighlightKind.Cities);
            SyncShowChecks();
        });
        BindStrategicCommand("area_map.show_capital", () =>
        {
            _areaMapView.ToggleHighlight(AreaMapHighlightKind.Capital);
            SyncShowChecks();
        });
        BindStrategicCommand("area_map.show_armies", () =>
        {
            _areaMapView.ToggleHighlight(AreaMapHighlightKind.Armies);
            SyncShowChecks();
        });
        BindStrategicCommand("area_map.show_fleets", () =>
        {
            _areaMapView.ToggleHighlight(AreaMapHighlightKind.Fleets);
            SyncShowChecks();
        });
        BindStrategicCommand("area_map.show_all", () =>
        {
            _areaMapView.ToggleShowAll();
            SyncShowChecks();
        });
        BindStrategicCommand("area_map.show_mercs_light_infantry", () =>
        {
            _areaMapView.ToggleHighlight(AreaMapHighlightKind.MercenariesLightInfantry);
            SyncShowChecks();
        });
        BindStrategicCommand("area_map.show_mercs_heavy_infantry", () =>
        {
            _areaMapView.ToggleHighlight(AreaMapHighlightKind.MercenariesHeavyInfantry);
            SyncShowChecks();
        });
        BindStrategicCommand("area_map.show_mercs_archers", () =>
        {
            _areaMapView.ToggleHighlight(AreaMapHighlightKind.MercenariesArchers);
            SyncShowChecks();
        });
        BindStrategicCommand("area_map.show_mercs_light_cavalry", () =>
        {
            _areaMapView.ToggleHighlight(AreaMapHighlightKind.MercenariesLightCavalry);
            SyncShowChecks();
        });
        BindStrategicCommand("area_map.show_mercs_heavy_cavalry", () =>
        {
            _areaMapView.ToggleHighlight(AreaMapHighlightKind.MercenariesHeavyCavalry);
            SyncShowChecks();
        });
        BindStrategicCommand("area_map.show_mercs_all", () =>
        {
            _areaMapView.ToggleShowAllMercenaries();
            SyncShowChecks();
        });
        BindStrategicCommand("area_map.find_city", () => OpenFindCityDialog());
    }

    /// <summary>
    /// T110: makes <paramref name="nationId"/> the viewed nation (or <see langword="null"/> for All
    /// nations): the context panel shows its status panel, the Area-map highlights re-scope to it, its
    /// Nations menu item is checked and its toolbar swatch is pressed (a radio group). Choosing All
    /// nations shows no status panel.
    /// </summary>
    public void SetViewedNation(string? nationId)
    {
        if (StrategicTravel.IsActive) return;
        ViewedNationId = nationId;

        // N7: the Find a city highlight belongs to the view it was chosen under; changing the viewed
        // nation (or All nations) clears it, so a city found under one nation does not stay lit while
        // another nation's public facts are shown.
        _areaMapView.SetFindCityHighlight(null);
        _contextPanel.SetViewedNation(nationId);
        _areaMapView.SetViewedNation(nationId);
        SyncNationChecksAndSwatches();
    }

    /// <summary>
    /// T110: enables and wires the main toolbar's 17 nation swatches. <c>CommandToolbar</c> builds them
    /// disabled and with no handler (T100 left them for this task); the screen enables them, makes them a
    /// radio group and routes each press through the table, so a swatch and its menu entry run the same
    /// handler.
    /// </summary>
    private void WireNationSwatches()
    {
        foreach (var row in CommandTable.MenuRows.Where(
            r => string.Equals(r.Menu, "Nations", StringComparison.Ordinal)))
        {
            if (Toolbar.ButtonFor(row.Id) is not { } button)
            {
                continue;
            }

            button.Disabled = false;
            button.ToggleMode = true;
            button.ButtonGroup = _nationSwatchGroup;

            // N4: CommandToolbar gives "pressed" the same stylebox as "normal", so a radio-pressed
            // swatch looked identical to an unpressed one. Override "pressed" with the nation's own
            // normal stylebox plus a thicker accent border, so the viewed nation's swatch is visibly
            // distinct. Done here (inside T110's Owns) rather than in CommandToolbar.cs (T100's).
            if (button.GetThemeStylebox("normal") is StyleBoxFlat normal)
            {
                var pressed = (StyleBoxFlat)normal.Duplicate();
                pressed.BorderColor = UiKit.AccentColor;
                pressed.BorderWidthTop = 4;
                pressed.BorderWidthBottom = 4;
                pressed.BorderWidthLeft = 4;
                pressed.BorderWidthRight = 4;
                button.AddThemeStyleboxOverride("pressed", pressed);
            }

            var commandId = row.Id;
            button.Pressed += () => CommandTable.TryInvoke(commandId);
        }

        SyncNationChecksAndSwatches();
    }

    /// <summary>The table row a viewed nation's swatch and menu item answer to.</summary>
    private static string NationRowId(string? nationId) =>
        nationId is null ? "nations.all" : "nations." + nationId;

    /// <summary>
    /// Moves the Nations menu's check mark and the toolbar's radio group to the viewed nation. The menu
    /// bar's item indices are read from the live popup by caption, so this never depends on the table's
    /// own order duplicating the menu's.
    /// </summary>
    private void SyncNationChecksAndSwatches()
    {
        var currentRow = NationRowId(ViewedNationId);
        foreach (var row in CommandTable.MenuRows.Where(
            r => string.Equals(r.Menu, "Nations", StringComparison.Ordinal)))
        {
            if (Toolbar.ButtonFor(row.Id) is { } button)
            {
                button.ButtonPressed = string.Equals(row.Id, currentRow, StringComparison.Ordinal);
            }
        }

        if (MenuBar.MenuForCheck("Nations") is not { } popup)
        {
            return;
        }

        for (var i = 0; i < popup.ItemCount; i++)
        {
            var row = CommandTable.MenuRows.FirstOrDefault(r =>
                string.Equals(r.Menu, "Nations", StringComparison.Ordinal)
                && string.Equals(r.Caption, popup.GetItemText(i), StringComparison.Ordinal));
            if (row is not null)
            {
                // B3 (#614): GameMenuBar adds every item plain (PopupMenu.add_item rather than
                // add_check_item), so a check mark is never drawn until the item is made
                // checkable — `SetItemChecked` on an item that's not checkable is a Godot 4
                // silent no-op, not a state to draw later. The 17 Nations entries
                // (16 nations + All nations) are a radio group, so exactly one is checked
                // (the viewed nation). `SetItemAsRadioCheckable` is the call that both marks an
                // item radio-checkable and resets its checked flag — it must run before
                // `SetItemChecked`, in this order, otherwise the previous checked item stays
                // drawn alongside the new one.
                popup.SetItemAsRadioCheckable(i, true);
                popup.SetItemChecked(i, string.Equals(row.Id, currentRow, StringComparison.Ordinal));
            }
        }
    }

    /// <summary>
    /// T110: moves the Area map menu's check marks to match the highlight layers that are on. Read by
    /// caption from the live popup because the Show mercenaries submenu item sits between the direct
    /// entries.
    /// </summary>
    private void SyncShowChecks()
    {
        if (MenuBar.MenuForCheck("Area map") is not { } popup)
        {
            return;
        }

        for (var i = 0; i < popup.ItemCount; i++)
        {
            var row = CommandTable.MenuRows.FirstOrDefault(r =>
                string.Equals(r.Menu, "Area map", StringComparison.Ordinal)
                && r.Submenu is null
                && string.Equals(r.Caption, popup.GetItemText(i), StringComparison.Ordinal));
            if (row is null)
            {
                continue;
            }

            // B3: only the eleven Show entries (five stock + six Show mercenaries) are check items;
            // Find a city stays a plain item so no check box is drawn beside it. Again the checkable
            // flag resets the mark, so set it first.
            if (IsShowEntry(row.Id))
            {
                popup.SetItemAsCheckable(i, true);
                popup.SetItemChecked(i, IsShowLayerOn(row.Id));
            }
        }
    }

    /// <summary>
    /// The Area map Show entries that are check items — the five stock (T110) and the six Show
    /// mercenaries (T113). <c>Find a city</c> stays a plain item so no check box is drawn beside it.
    /// </summary>
    private static bool IsShowEntry(string commandId) =>
        commandId is "area_map.show_cities"
            or "area_map.show_capital"
            or "area_map.show_armies"
            or "area_map.show_fleets"
            or "area_map.show_all"
            or "area_map.show_mercs_light_infantry"
            or "area_map.show_mercs_heavy_infantry"
            or "area_map.show_mercs_archers"
            or "area_map.show_mercs_light_cavalry"
            or "area_map.show_mercs_heavy_cavalry"
            or "area_map.show_mercs_all";

    private bool IsShowLayerOn(string commandId)
    {
        var active = _areaMapView.ActiveHighlightsForCheck;
        return commandId switch
        {
            "area_map.show_cities" => active.Contains(AreaMapHighlightKind.Cities),
            "area_map.show_capital" => active.Contains(AreaMapHighlightKind.Capital),
            "area_map.show_armies" => active.Contains(AreaMapHighlightKind.Armies),
            "area_map.show_fleets" => active.Contains(AreaMapHighlightKind.Fleets),
            "area_map.show_all" => AreaMapHighlights.StockShowAllKinds().All(active.Contains),
            "area_map.show_mercs_light_infantry" => active.Contains(AreaMapHighlightKind.MercenariesLightInfantry),
            "area_map.show_mercs_heavy_infantry" => active.Contains(AreaMapHighlightKind.MercenariesHeavyInfantry),
            "area_map.show_mercs_archers" => active.Contains(AreaMapHighlightKind.MercenariesArchers),
            "area_map.show_mercs_light_cavalry" => active.Contains(AreaMapHighlightKind.MercenariesLightCavalry),
            "area_map.show_mercs_heavy_cavalry" => active.Contains(AreaMapHighlightKind.MercenariesHeavyCavalry),
            "area_map.show_mercs_all" => AreaMapHighlights.MercenaryTypeKinds().All(active.Contains),
            _ => false,
        };
    }

    /// <summary>The <see cref="AppRoot"/> this screen was swapped into, or <see langword="null"/> when
    /// it is driven directly (a headless check). The three File commands that leave the game need it.</summary>
    private AppRoot? OwningAppRoot => GetParent() as AppRoot;

    private const string NewGameAction = "start a new game";

    /// <summary>Asks before leaving the current game (T100 Scope: New, Open and Close confirm; Save does
    /// not). New asks the original's own question (#801 P10); the original asks nothing for Open or Close,
    /// so those keep this app's [designed] line.</summary>
    private void ConfirmLeavingGame(string action, Action onConfirmed)
    {
        var question = action == NewGameAction
            ? "Are you sure you want to start a new game ?"
            : $"The current game will be left to {action}. Continue?";
        var prompt = new ConfirmPrompt { Question = question };
        prompt.Confirmed += () =>
        {
            CloseOverlay(prompt);
            onConfirmed();
        };
        prompt.Refused += () => CloseOverlay(prompt);
        ShowOverlay(prompt);
    }

    /// <summary>Bug #919: the window's close button while this game is open — the same question File ->
    /// Close asks, and <paramref name="onConfirmed"/> runs only on Yes.</summary>
    public void ConfirmQuit(Action onConfirmed) => ConfirmLeavingGame("quit", onConfirmed);

    /// <summary>Fix #1081: the folder the last Save As chose this session; the dialog starts there,
    /// otherwise in the saves folder.</summary>
    private static string? _lastSaveAsFolder;
    private static string? _lastSavTemplateFolder;
    private string? _pendingSavOutputPath;
    /// <summary>Check-only override for the private SAV template config.</summary>
    public string? SavConfigPathForCheck { private get; set; }

    private FileDialog? _saveAsDialog;

    /// <summary>The Save As dialog, created on first use: a native-path file dialog over the whole file
    /// system in save mode, offering native JSON and original-compatible SAV exports.</summary>
    public FileDialog SaveAsDialog => _saveAsDialog ??= CreateSaveAsDialog();

    private FileDialog CreateSaveAsDialog()
    {
        var dialog = new FileDialog
        {
            Name = "SaveAsDialog",
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.SaveFile,
            Title = "Save the game as",
            Filters = new[] { "*.json ; Native saved game", "*.sav ; Original-compatible SAV" },
            Size = new Vector2I(760, 520),
        };
        dialog.FileSelected += SaveToChosenPath;
        AddChild(dialog);
        return dialog;
    }

    /// <summary>Save As: a system file dialog (fix #1081), starting in the last folder used or the saves
    /// folder. The chosen file becomes the current file, so a later Save writes back to it. Cancel
    /// writes nothing.</summary>
    private void ShowSaveAsPrompt()
    {
        var dialog = SaveAsDialog;
        var folder = _lastSaveAsFolder is { } last && Directory.Exists(last)
            ? last
            : ProjectSettings.GlobalizePath(SavesDirectory);
        Directory.CreateDirectory(folder);
        dialog.CurrentDir = folder;
        dialog.CurrentFile = SuggestedSaveFileName(folder);
        dialog.PopupCentered();
    }

    /// <summary>Check hook: Save As as if the dialog had chosen <paramref name="path"/>.</summary>
    public void SaveAsForCheck(string path) => SaveToChosenPath(path);

    private FileDialog? _savTemplateDialog;

    /// <summary>The separate original-save template picker used only after a .sav output is chosen.</summary>
    public FileDialog SavTemplateDialog => _savTemplateDialog ??= CreateSavTemplateDialog();

    private FileDialog CreateSavTemplateDialog()
    {
        var dialog = new FileDialog
        {
            Name = "SavTemplateDialog",
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Title = "Choose an original .sav for interface settings",
            Filters = new[] { "*.sav ; Original saved game" },
            Size = new Vector2I(760, 520),
        };
        dialog.FileSelected += ExportSavWithTemplate;
        dialog.Canceled += () => _pendingSavOutputPath = null;
        AddChild(dialog);
        return dialog;
    }

    /// <summary>Check hook: completes a pending .sav export as the template dialog would.</summary>
    public void SavTemplateForCheck(string path) => ExportSavWithTemplate(path);

    /// <summary>Check hook: whether a chosen .sav output is waiting for its private template.</summary>
    public bool SavTemplatePendingForCheck => _pendingSavOutputPath is not null;

    /// <summary>Check hook: the text of the save confirmation line.</summary>
    public string SaveMessageForCheck => _saveConfirmationLabel.Text;

    private void SaveToChosenPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (path.EndsWith(".sav", StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(path) || Directory.Exists(path))
            {
                ShowSaveAsMessage($"Not exported: '{path}' already exists. Choose a new name.");
                return;
            }

            _pendingSavOutputPath = path;
            try
            {
                var config = SavConfigPathForCheck ?? Path.Combine(RepositoryRoot, "assets.local.ini");
                if (OriginalSaveExportTemplateResolver.ConfiguredTemplate(config) is { } configured)
                {
                    ExportSavWithTemplate(configured);
                    return;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
                or ArgumentException or NotSupportedException)
            {
                _pendingSavOutputPath = null;
                ShowSaveAsMessage($"Not exported: {ex.Message}");
                return;
            }

            var dialog = SavTemplateDialog;
            var folder = _lastSavTemplateFolder is { } last && Directory.Exists(last)
                ? last : OS.GetSystemDir(OS.SystemDir.Documents);
            dialog.CurrentDir = folder;
            dialog.PopupCentered();
            return;
        }

        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            ShowSaveAsMessage($"Not saved: '{path}' must end in .json or .sav.");
            return;
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException)
            {
                ShowSaveAsMessage($"Not saved: the folder '{directory}' cannot be used ({ex.Message}).");
                return;
            }
        }

        if (SaveToFile(path) && !string.IsNullOrEmpty(directory))
        {
            _lastSaveAsFolder = directory;
        }
    }

    private void ExportSavWithTemplate(string templatePath)
    {
        var outputPath = _pendingSavOutputPath;
        _pendingSavOutputPath = null;
        if (outputPath is null) return;

        try
        {
            var result = OriginalSaveExporter.ExportFile(Session, templatePath, outputPath);
            _lastSavTemplateFolder = Path.GetDirectoryName(templatePath);
            _lastSaveAsFolder = Path.GetDirectoryName(outputPath);
            var aliases = result.Export.Report.Aliases;
            var omitted = result.Export.Report.Omitted;
            var details = $"Exported original-compatible SAV to '{result.OutputPath}'. "
                + $"Template: '{templatePath}' (SHA-256 {result.TemplateSha256})."
                + string.Concat(aliases.Select(a => $"\n{a.ArmyId} slot {a.Slot}: '{a.NativeName}' → '{a.ExportedName}'"))
                + string.Concat(omitted.Select(item => $"\nNot in original format: {item}"));
            ShowSaveAsMessage($"Exported original-compatible SAV to '{result.OutputPath}'. "
                + $"{aliases.Count} name aliases; {omitted.Count} native metadata omissions.", details);
        }
        catch (OriginalSaveExportException ex)
        {
            var details = string.Join("\n", ex.Refusals.Select(r => $"{r.Code} {r.Field}: {r.Detail}"));
            ShowSaveAsMessage($"Not exported: {ex.Refusals[0].Code} {ex.Refusals[0].Field}: {ex.Refusals[0].Detail}", details);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException
            or ArgumentException or NotSupportedException)
        {
            ShowSaveAsMessage($"Not exported: {ex.Message}");
        }
    }

    private void ShowSaveAsMessage(string message, string? details = null)
    {
        _saveConfirmationLabel.Text = message;
        _saveConfirmationLabel.TooltipText = details ?? message;
    }

    /// <summary>Show hints: flips the menu bar's and toolbar's tooltips and the menu check mark together
    /// (T100 Done-when 6). On by default and kept for the session.</summary>
    private void ToggleHints()
    {
        _hintsEnabled = !_hintsEnabled;
        MenuBar.SetHintsEnabled(_hintsEnabled);
        Toolbar.SetHintsEnabled(_hintsEnabled);
        _areaMapView.SetHintsEnabled(_hintsEnabled);
    }

    /// <summary>
    /// T110: opens <c>TFindCity</c> — a nation dropdown and that nation's cities, with capitals marked.
    /// Choosing a city centres the order map on it and highlights it on the mini-map (audit §1.5:
    /// <c>TFindCity_FillListBox</c>/<c>ChangeCity</c>). Public so <c>NationsAreaMapCheck</c> can drive
    /// the real dialog rather than a hand-built mirror.
    /// </summary>
    public FindCityDialog OpenFindCityDialog()
    {
        var dialog = new FindCityDialog { Session = Session, InitialNationId = ViewedNationId };
        dialog.CityChosen += cityId =>
        {
            CloseOverlay(dialog);
            OnCityFound(cityId);
        };
        dialog.Closed += () => CloseOverlay(dialog);
        ShowOverlay(dialog);
        return dialog;
    }

    private void OnCityFound(string cityId)
    {
        if (Session.State.CityById(cityId) is not { } city)
        {
            return;
        }

        _mapView.CentreOnTile(city.X, city.Y);
        _areaMapView.SetFindCityHighlight(cityId);
    }

    /// <summary>
    /// T134: selects an army exactly as a map click's <see cref="GameMapView.ArmySelected"/> does — the
    /// state <see cref="OpenSupplyArmyDialog"/> reads — so <c>godot/Checks/SupplyArmyCheck.cs</c> can
    /// drive the dialog from the menu without a map click's foreign-army attack prompt. The same
    /// check-seam convention <see cref="SubmitForCheck"/> establishes.
    /// </summary>
    public void SelectArmyForCheck(string armyId)
    {
        if (!AllowStrategicInput()) return;
        _mapView.SelectStrategicForCheck(MapEntityKind.Army, armyId);
    }

    /// <summary>
    /// T111: selects a fleet exactly as a map click's <see cref="GameMapView.FleetSelected"/> does, so
    /// <c>godot/Checks/ArmyOrdersCheck.cs</c> can prove the Army entries act on the army a selected
    /// fleet carries.
    /// </summary>
    public void SelectFleetForCheck(string fleetId)
    {
        if (!AllowStrategicInput()) return;
        _mapView.SelectStrategicForCheck(MapEntityKind.Fleet, fleetId);
    }

    /// <summary>
    /// T112: selects a city exactly as a map click's <see cref="GameMapView.CitySelected"/> does, so
    /// <c>godot/Checks/FleetCityOrdersCheck.cs</c> can drive Fortify city from the menu.
    /// </summary>
    public void SelectCityForCheck(string cityId)
    {
        if (!AllowStrategicInput()) return;
        _mapView.SelectStrategicForCheck(MapEntityKind.City, cityId);
    }

    /// <summary>
    /// T134: the Army menu's Supply army entry. Acts on the selected army, or on the army a selected fleet
    /// carries [confirmed: code, the prologue of each <c>TUnitMap_*</c> army order]. With no own army
    /// selected it shows the select-an-army message; with no provider within one tile it shows the
    /// no-provider message; either way it opens nothing and issues nothing.
    /// </summary>
    private void OpenSupplyArmyDialog()
    {
        var army = SelectedArmyOrCarried();
        if (army is null || !string.Equals(army.Nation, Session.State.ActiveNationId, StringComparison.Ordinal))
        {
            ShowScreenMessage("Select one of your armies first.");
            return;
        }

        if (IC2.Engine.Economy.SupplyProviders.ForArmy(Session.State, army.Id, Session.Ruleset).Count == 0)
        {
            ShowScreenMessage("No city or fleet of yours, or of a nation at peace with you, within one tile.");
            return;
        }

        var dialog = new SupplyDialog
        {
            Session = Session,
            ArmyId = army.Id,
            Submit = SubmitFromDialog,
        };
        dialog.Closed += () => CloseOverlay(dialog);
        ShowOverlay(dialog);
    }

    /// <summary>
    /// T111: the Army menu's Transfer unit entry. Acts on the selected army (or the army a selected fleet
    /// carries); the partner is the game's own pick, <see cref="AdjacentPartner.Army"/>. With no partner
    /// it shows <see cref="ArmyDialogModels.NoPartnerMessage"/> and opens nothing.
    /// </summary>
    private void OpenArmyTransferDialog()
    {
        var army = OwnSelectedArmy();
        if (army is null)
        {
            ShowScreenMessage(SelectArmyMessage);
            return;
        }

        if (AdjacentPartner.Army(Session.State, army.Id) is not { } partner)
        {
            ShowScreenMessage(ArmyDialogModels.NoPartnerMessage);
            return;
        }

        var dialog = new ArmyTransferDialog
        {
            Session = Session,
            ArmyId = army.Id,
            PartnerId = partner.Id,
            Submit = SubmitFromDialog,
        };
        dialog.Closed += () => CloseOverlay(dialog);
        ShowOverlay(dialog);
    }

    /// <summary>
    /// T111/T142: the Army menu's Split army entry. The dialog needs two or more units and the nation
    /// below its army cap; it composes one <c>split-army</c> (units, supply and money) and the engine
    /// places the new army. An army aboard a fleet is offered the same split as one on land; when a
    /// splittable army has no free land cell for the new army the screen opens no dialog and says why.
    /// </summary>
    private void OpenSplitArmyDialog()
        => OpenSplitArmyDialog(suggestBalanced: false);

    private void OpenSplitArmyDialog(bool suggestBalanced)
    {
        var army = OwnSelectedArmy();
        if (army is null)
        {
            ShowScreenMessage(SelectArmyMessage);
            return;
        }

        // The original checks the unit count (and T111 its army cap) before it scans for a tile, so
        // T111's own refusal is shown inside the dialog for a one-unit or over-cap army even when no land
        // is free; only a splittable army gets the no-free-tile pre-check.
        var newArmyId = NextSplitArmyId(army.Id);
        var split = SplitArmyModel.ForArmy(Session.State, army, Session.Ruleset, newArmyId);
        if (split.CanSplit
            && SplitPlacement.ArmyCellFor(Session.State, Session.World, army) is null)
        {
            // The engine places the new army on the scan's cell, centred on the army's own tile or, aboard
            // a fleet, on the carrying fleet's tile (SplitPlacement.ArmyCellFor). With no free land cell it
            // creates nothing and opens no dialog; the screen says why [designed, the user's decision of
            // 2026-10-05].
            ShowScreenMessage(ArmyDialogModels.SplitNoFreeTileMessage);
            return;
        }

        var dialog = new SplitArmyDialog
        {
            Session = Session,
            ArmyId = army.Id,
            NewArmyId = newArmyId,
            SuggestBalanced = suggestBalanced,
            Submit = SubmitFromDialog,
        };
        dialog.Closed += () => CloseOverlay(dialog);
        ShowOverlay(dialog);
    }

    /// <summary>
    /// T111: the Army menu's Join armies entry — no dialog. It takes <see cref="AdjacentPartner.Army"/>
    /// and submits <c>join-armies</c>; with no partner it shows the same message Transfer unit does.
    /// </summary>
    private void JoinArmiesFromMenu()
    {
        var army = OwnSelectedArmy();
        if (army is null)
        {
            ShowScreenMessage(SelectArmyMessage);
            return;
        }

        var partner = AdjacentPartner.Army(Session.State, army.Id);
        if (ArmyDialogModels.JoinArmiesLine(army.Id, partner) is not { } line)
        {
            ShowScreenMessage(ArmyDialogModels.NoPartnerMessage);
            return;
        }

        SubmitForCheck(line);
    }

    /// <summary>T111: the Army menu's Change units entry.</summary>
    private void OpenChangeUnitsDialog()
    {
        var army = OwnSelectedArmy();
        if (army is null)
        {
            ShowScreenMessage(SelectArmyMessage);
            return;
        }

        var dialog = new ChangeUnitsDialog
        {
            Session = Session,
            ArmyId = army.Id,
            Submit = SubmitFromDialog,
        };
        dialog.Closed += () => CloseOverlay(dialog);
        ShowOverlay(dialog);
    }

    /// <summary>
    /// T111: the Army menu's Disband army entry — T99's <see cref="ConfirmPrompt"/> first, then
    /// <c>disband-army</c> only on Yes. The engine keeps its own rule that this works only next to one
    /// of the nation's own cities.
    /// </summary>
    private void ConfirmDisbandArmy()
    {
        var army = OwnSelectedArmy();
        if (army is null)
        {
            ShowScreenMessage(SelectArmyMessage);
            return;
        }

        var prompt = new ConfirmPrompt { Question = "Are you sure you want to disband this army ?" };
        prompt.Confirmed += () =>
        {
            CloseOverlay(prompt);
            SubmitForCheck(ArmyDialogModels.DisbandArmyLine(army.Id));
        };
        prompt.Refused += () => CloseOverlay(prompt);
        ShowOverlay(prompt);
    }

    /// <summary>
    /// T113: the Army menu's Recruit mercenaries entry. Opens the dialog for the chosen city (T76's
    /// adjacency rule, read by the model's <see cref="MercenaryDialogModel.ForArmy"/>), or shows the
    /// engine's pre-open refusal message and opens nothing when the dialog's own dialog-opens gate
    /// is closed. With no offer city in reach (the original's first refusal — nothing, no message,
    /// derived: <c>TUnitMap_RecruitMercenaries</c> @ <c>0x00446FF4</c>) this entry silently returns:
    /// the model decides it from the live pool, the menu never gets a status line, and the dialog
    /// stays closed. With at least one offer but one of the other pre-open refusals (20 units,
    /// 100,000 troops, enemy city, supplies, fleet capacity), the matching engine message is shown
    /// and the dialog stays closed. The dialog itself adds no refusal of its own — the engine's
    /// per-hire gates report theirs through the dialog's reply line.
    /// </summary>
    private void OpenRecruitMercenariesDialog()
    {
        var army = OwnSelectedArmy();
        if (army is null)
        {
            ShowScreenMessage(SelectArmyMessage);
            return;
        }

        var model = MercenaryDialogModel.ForArmy(Session.State, army.Id, Session.Ruleset);

        if (model.Offers.Count == 0)
        {
            // T113 R3: the original's "no offer in reach" refusal is a silent no-op — derived from
            // the order's handler as the brief's Hazards pin it. The menu shows no message, the
            // dialog stays closed, and the player's own army/orders are unchanged.
            return;
        }

        if (!model.DialogOpens)
        {
            ShowScreenMessage(model.PreOpenRefusalMessage ?? "This army cannot recruit mercenaries.");
            return;
        }

        var dialog = new RecruitMercenariesDialog
        {
            Session = Session,
            ArmyId = army.Id,
            Submit = SubmitFromDialog,
        };
        dialog.Closed += () => CloseOverlay(dialog);
        ShowOverlay(dialog);
    }

    /// <summary>
    /// T109: the Strategy menu's Taxation entry — opens the <see cref="TaxationDialog"/> overlay.
    /// The dialog is the original's <c>TChangeTax</c>; its OK writes the rate through
    /// <c>set-tax</c>, the same path every other dialog's command takes.
    /// </summary>
    private void OpenTaxationDialog()
    {
        var dialog = new TaxationDialog
        {
            Session = Session,
            Submit = SubmitFromDialog,
        };
        dialog.Closed += () => CloseOverlay(dialog);
        ShowOverlay(dialog);
    }

    /// <summary>
    /// T109: the Strategy menu's Balance sheet entry — opens the <see cref="BalanceSheetDialog"/>
    /// overlay. Read-only, OK only; the dialog renders <see cref="IC2.Engine.Economy.BalanceSheet.For"/>'s
    /// own figures with the original's row captions.
    /// </summary>
    private void OpenBalanceSheetDialog()
    {
        var dialog = new BalanceSheetDialog
        {
            Session = Session,
        };
        dialog.Closed += () => CloseOverlay(dialog);
        ShowOverlay(dialog);
    }

    /// <summary>
    /// T109: the Strategy menu's Recruit unit entry — opens the <see cref="RecruitUnitDialog"/>
    /// overlay. The dialog is the original's <c>TArmyRecruits</c> with Mobilize moved in by the
    /// user's 2026-10-01 decision. Cities, type, troops, the three action buttons and the
    /// disband confirmation all live in the dialog itself.
    /// </summary>
    private void OpenRecruitUnitDialog()
    {
        var dialog = new RecruitUnitDialog
        {
            Session = Session,
            Submit = SubmitFromDialog,
        };
        dialog.Closed += () => CloseOverlay(dialog);
        ShowOverlay(dialog);
    }

    /// <summary>
    /// T109: the Strategy menu's Build fleet entry — opens the <see cref="BuildFleetDialog"/>
    /// overlay. The dialog is the original's <c>TBuildFleet</c>; the clone closes on OK
    /// (one order per OK, the user's 2026-10-02 decision) where the original stays open.
    /// </summary>
    private void OpenBuildFleetDialog()
    {
        var dialog = new BuildFleetDialog
        {
            Session = Session,
            Submit = SubmitFromDialog,
        };
        dialog.Closed += () => CloseOverlay(dialog);
        ShowOverlay(dialog);
    }

    /// <summary>The message the Army entries show when no own army (or carried army) is selected.</summary>
    private const string SelectArmyMessage = "Select one of your armies first.";

    /// <summary>T112: the message the Fleet entries show when no own fleet is selected.</summary>
    private const string SelectFleetMessage = "Select one of your fleets first.";

    /// <summary>T112: the message the City entry shows when no own city is selected.</summary>
    private const string SelectCityMessage = "Select one of your cities first.";

    /// <summary>T112: the Fleet menu's Supply fleet entry — TAFSupply's fleet half.</summary>
    private void OpenSupplyFleetDialog()
    {
        var fleet = OwnSelectedFleet();
        if (fleet is null)
        {
            ShowScreenMessage(SelectFleetMessage);
            return;
        }

        var dialog = new SupplyDialog
        {
            Session = Session,
            FleetId = fleet.Id,
            Submit = SubmitFromDialog,
        };
        dialog.Closed += () => CloseOverlay(dialog);
        ShowOverlay(dialog);
    }

    /// <summary>T112: the Fleet menu's Repair fleet entry.</summary>
    private void OpenRepairFleetDialog()
    {
        var fleet = OwnSelectedFleet();
        if (fleet is null)
        {
            ShowScreenMessage(SelectFleetMessage);
            return;
        }

        var dialog = new RepairDialog
        {
            Session = Session,
            FleetId = fleet.Id,
            Submit = SubmitFromDialog,
        };
        dialog.Closed += () => CloseOverlay(dialog);
        ShowOverlay(dialog);
    }

    /// <summary>
    /// T112: the Fleet menu's Transfer ships entry. The partner is the game's own pick,
    /// <see cref="AdjacentPartner.Fleet"/>; with none it opens no dialog, submits nothing and shows the
    /// clone's no-partner message.
    /// </summary>
    private void OpenFleetTransferDialog()
    {
        var fleet = OwnSelectedFleet();
        if (fleet is null)
        {
            ShowScreenMessage(SelectFleetMessage);
            return;
        }

        if (AdjacentPartner.Fleet(Session.State, fleet.Id) is not { } partner)
        {
            ShowScreenMessage(FleetCityDialogModels.NoPartnerMessage);
            return;
        }

        var dialog = new FleetTransferDialog
        {
            Session = Session,
            FleetId = fleet.Id,
            PartnerId = partner.Id,
            Submit = SubmitFromDialog,
        };
        dialog.Closed += () => CloseOverlay(dialog);
        ShowOverlay(dialog);
    }

    /// <summary>T112: the Fleet menu's Split fleet entry.</summary>
    private void OpenSplitFleetDialog()
    {
        var fleet = OwnSelectedFleet();
        if (fleet is null)
        {
            ShowScreenMessage(SelectFleetMessage);
            return;
        }

        var dialog = new SplitFleetDialog
        {
            Session = Session,
            FleetId = fleet.Id,
            NewFleetId = NextSplitFleetId(fleet.Id),
            Submit = SubmitFromDialog,
        };
        dialog.Closed += () => CloseOverlay(dialog);
        ShowOverlay(dialog);
    }

    /// <summary>
    /// T112: the Fleet menu's Join fleets entry — no dialog. It takes <see cref="AdjacentPartner.Fleet"/>
    /// and submits <c>join-fleets</c> when the combined ships are within the cap; otherwise it shows the
    /// no-partner or combined-ships message.
    /// </summary>
    private void JoinFleetsFromMenu()
    {
        var fleet = OwnSelectedFleet();
        if (fleet is null)
        {
            ShowScreenMessage(SelectFleetMessage);
            return;
        }

        var model = FleetJoinModel.ForFleet(
            fleet, AdjacentPartner.Fleet(Session.State, fleet.Id), Session.Ruleset);
        if (model.ComposeOk() is not { } line)
        {
            ShowScreenMessage(model.RefusalMessage ?? FleetCityDialogModels.NoPartnerMessage);
            return;
        }

        SubmitForCheck(line);
    }

    /// <summary>T112: the Fleet menu's Scuttle fleet entry — T99's confirmation prompt first, then
    /// <c>scuttle-fleet</c> only on Yes.</summary>
    private void ConfirmScuttleFleet()
    {
        var fleet = OwnSelectedFleet();
        if (fleet is null)
        {
            ShowScreenMessage(SelectFleetMessage);
            return;
        }

        var prompt = new ConfirmPrompt { Question = FleetCityDialogModels.ScuttlePromptText };
        prompt.Confirmed += () =>
        {
            CloseOverlay(prompt);
            SubmitForCheck(FleetCityDialogModels.ScuttleFleetLine(fleet.Id));
        };
        prompt.Refused += () => CloseOverlay(prompt);
        ShowOverlay(prompt);
    }

    /// <summary>T112: the City menu's Fortify city entry.</summary>
    private void OpenFortifyCityDialog()
    {
        var city = OwnSelectedCity();
        if (city is null)
        {
            ShowScreenMessage(SelectCityMessage);
            return;
        }

        var dialog = new FortifyDialog
        {
            Session = Session,
            CityId = city.Id,
            Submit = SubmitFromDialog,
        };
        dialog.Closed += () => CloseOverlay(dialog);
        ShowOverlay(dialog);
    }

    /// <summary>A new fleet id that does not collide with an existing one, for the split dialog.</summary>
    private string NextSplitFleetId(string fleetId)
    {
        var candidate = $"{fleetId}-split";
        var suffix = 2;
        while (Session.State.FleetById(candidate) is not null)
        {
            candidate = $"{fleetId}-split-{suffix++}";
        }

        return candidate;
    }

    /// <summary>The selected fleet when it is the active seat's own, or <see langword="null"/>.</summary>
    private FleetState? OwnSelectedFleet()
    {
        if (_selectedFleetId is null)
        {
            return null;
        }

        var fleet = Session.State.FleetById(_selectedFleetId);
        return fleet is not null
            && string.Equals(fleet.Nation, Session.State.ActiveNationId, StringComparison.Ordinal)
            ? fleet
            : null;
    }

    /// <summary>The selected city when it is the active seat's own, or <see langword="null"/>.</summary>
    private CityState? OwnSelectedCity()
    {
        if (_selectedCityId is null)
        {
            return null;
        }

        var city = Session.State.CityById(_selectedCityId);
        return city is not null
            && string.Equals(city.Owner, Session.State.ActiveNationId, StringComparison.Ordinal)
            ? city
            : null;
    }

    /// <summary>
    /// T112: re-reads the map selection and tells the command strip which group to show — the selected
    /// own army, own fleet (with the army group too when it carries one), own city, or nothing for a
    /// foreign unit, a missing one, or no selection.
    /// </summary>
    private void UpdateCommandStrip()
    {
        var selection = UnitStripSelection.None;
        if (_selectedArmyId is { } armyId && DisplayState.ArmyById(armyId) is { } army
            && string.Equals(army.Nation, DisplayState.ActiveNationId, StringComparison.Ordinal))
        {
            selection = UnitStripSelection.OwnArmy;
        }
        else if (_selectedFleetId is { } fleetId && DisplayState.FleetById(fleetId) is { } fleet
            && string.Equals(fleet.Nation, DisplayState.ActiveNationId, StringComparison.Ordinal))
        {
            selection = fleet.IsCarryingArmy
                ? UnitStripSelection.OwnFleetCarryingArmy
                : UnitStripSelection.OwnFleet;
        }
        else if (_selectedCityId is { } cityId && DisplayState.CityById(cityId) is { } city
            && string.Equals(city.Owner, DisplayState.ActiveNationId, StringComparison.Ordinal))
        {
            selection = UnitStripSelection.OwnCity;
        }

        _commandStrip.SetSelection(selection);
        if (_travelTargetLabel is null) return;
        var target = _selectedArmyId is { } selected ? Session.TravelTargetFor(selected) : null;
        var current = _selectedArmyId is { } selectedArmyId ? Session.State.ArmyById(selectedArmyId) : null;
        var remaining = target is null || current is null ? 0
            : Math.Max(0, Math.Max(Math.Abs(current.X - target.X), Math.Abs(current.Y - target.Y))
                - (target.CityId is null ? 0 : 1));
        _travelTargetLabel.Visible = target is not null || _mapView.TravelTargetPickArmed;
        _travelTargetLabel.Text = _mapView.TravelTargetPickArmed
            ? "Travel target: click an open land cell or city; right-click or Esc to cancel."
            : target is null ? string.Empty
            : $"Travel target for {target.ArmyId}: {(target.CityId is null ? $"cell ({target.X}, {target.Y})" : $"city {target.CityId}")} · {remaining} cells away. Use Army → Cancel travel target to stop.";
        if (_commandStrip.ButtonFor("unit_map.army_set_target") is { } travelButton)
            travelButton.TooltipText = "Set travel target" + (string.IsNullOrEmpty(_travelTargetLabel.Text) ? "" : "\n" + _travelTargetLabel.Text);

        var selectedArmy = OwnSelectedArmy();
        var selectedFleet = selectedArmy is null || selectedArmy.IsEmbarked ? OwnSelectedFleet() : null;
        var selectedUnit = selectedArmy is { IsEmbarked: false } ? selectedArmy.Id : selectedFleet?.Id;
        var isFleet = selectedArmy is null || selectedArmy.IsEmbarked;
        var recovery = selectedUnit is null ? null : Session.RetreatSupplyPlanFor(selectedUnit, isFleet);
        _retreatSupplyRow.Visible = selectedUnit is not null;
        _retreatSupplyButton.SetPressedNoSignal(recovery is not null);
        _retreatSupplyLabel.Text = recovery is null ? string.Empty
            : $"Recovering {selectedUnit}" + (recovery.DestinationCityId is null
                ? ": choosing an owned town." : $": heading to {recovery.DestinationCityId}.");
        _retreatSupplyButton.TooltipText = recovery is null ? "Retreat and supply" : "Cancel recovery\n" + _retreatSupplyLabel.Text;
        _retreatSplitButton.Visible = recovery is { IsFleet: false }
            && selectedArmy is { IsEmbarked: false } && selectedArmy.Units.Count >= 2;

        var activeNation = Session.State.NationById(Session.State.ActiveNationId);
        var rally = Session.RallyPointFor(Session.State.ActiveNationId);
        _rallyPointRow.Visible = activeNation?.Control == SeatControl.Human;
        _rallyPointLabel.Text = _rallyPointPickNationId is not null
            ? "Rally point: click a land cell or owned city; right-click or Esc to cancel."
            : rally is null ? "Rally point for new armies: none."
            : $"Rally point for {rally.NationId}: {(rally.CityId is null ? $"cell ({rally.X}, {rally.Y})" : $"city {rally.CityId}")}.";
        _rallyPointSetButton.TooltipText = (rally is null ? "Set rally point" : "Change rally point") + "\n" + _rallyPointLabel.Text;
        _rallyPointClearButton.Visible = rally is not null;
    }

    private void ArmNationRallyPointPick()
    {
        if (Session.State.NationById(Session.State.ActiveNationId)?.Control != SeatControl.Human)
        {
            ShowScreenMessage("Only the active human nation can set its rally point.");
            return;
        }
        if (Session.RallyPointFor(Session.State.ActiveNationId) is { } existing)
        {
            var prompt = new ConfirmPrompt
            {
                Question = $"Replace {existing.NationId}'s rally point?"
            };
            prompt.Confirmed += () =>
            {
                CloseOverlay(prompt);
                BeginNationRallyPointPick();
            };
            prompt.Refused += () => CloseOverlay(prompt);
            ShowOverlay(prompt);
            return;
        }
        BeginNationRallyPointPick();
    }

    private void BeginNationRallyPointPick()
    {
        _rallyPointPickNationId = Session.State.ActiveNationId;
        _mapView.ArmTravelTargetPick();
        UpdateCommandStrip();
        ShowScreenMessage("Click an open land cell or your city for newly mobilized armies.");
    }

    private void ClearNationRallyPoint()
    {
        _mapView.CancelTravelTargetPick();
        _rallyPointPickNationId = null;
        var nationId = Session.State.ActiveNationId;
        ShowScreenMessage(Session.ClearRallyPoint(nationId)
            ? $"Rally point cleared for future {nationId} mobilizations."
            : $"{nationId} has no rally point.");
        UpdateCommandStrip();
    }

    private void HandlePickedMapTarget(int x, int y, string? cityId)
    {
        if (_rallyPointPickNationId is { } nationId)
        {
            _rallyPointPickNationId = null;
            var city = cityId is null ? null : Session.State.CityById(cityId);
            if (cityId is not null && (city is null || city.Owner != nationId))
            {
                UpdateCommandStrip();
                ShowScreenMessage("A rally city must be owned by the active nation.");
                return;
            }
            if (cityId is null)
            {
                var cells = Session.World.Terrain.Decode(Session.World.Width, Session.World.Height);
                if ((uint)x >= (uint)Session.World.Width || (uint)y >= (uint)Session.World.Height
                    || Session.World.TileTypeByCode(cells[y * Session.World.Width + x])?.PassableByArmies != true)
                {
                    UpdateCommandStrip();
                    ShowScreenMessage("Choose a land cell or an owned city for the rally point.");
                    return;
                }
            }
            Session.SetRallyPoint(new NationRallyPoint(nationId, x, y, cityId));
            UpdateCommandStrip();
            ShowScreenMessage($"Rally point set for new {nationId} armies.");
            return;
        }
        SetPickedArmyTravelTarget(x, y, cityId);
    }

    private void ToggleRetreatSupplyMode()
    {
        var army = OwnSelectedArmy();
        var fleet = army is null || army.IsEmbarked ? OwnSelectedFleet() : null;
        var unitId = army is { IsEmbarked: false } ? army.Id : fleet?.Id;
        var isFleet = army is null || army.IsEmbarked;
        if (unitId is null || fleet is { IsUnderConstruction: true })
        {
            ShowScreenMessage("Select one of your unembarked armies or launched fleets first.");
            return;
        }
        if (Session.ClearRetreatSupplyPlan(unitId, isFleet))
        {
            UpdateCommandStrip();
            ShowScreenMessage($"Retreat and supply cancelled for {unitId}.");
            return;
        }

        void Enable()
        {
            Session.SetRetreatSupplyPlan(new RetreatSupplyPlan(unitId, Session.State.ActiveNationId, isFleet));
            UpdateCommandStrip();
            ShowScreenMessage($"Retreat and supply enabled for {unitId}; it will act before End turn.");
        }

        if (!isFleet && Session.TravelTargetFor(unitId) is not null)
        {
            var prompt = new ConfirmPrompt
            {
                Question = $"Replace {unitId}'s fixed travel target with Retreat and supply?",
            };
            prompt.Confirmed += () =>
            {
                CloseOverlay(prompt);
                Session.ClearArmyTravelTarget(unitId);
                Enable();
            };
            prompt.Refused += () => CloseOverlay(prompt);
            ShowOverlay(prompt);
            return;
        }
        Enable();
    }

    private void ArmArmyTravelTarget()
    {
        var army = OwnSelectedArmy();
        if (army is null || army.IsEmbarked)
        {
            ShowScreenMessage("Select one of your unembarked armies first.");
            return;
        }
        _mapView.ArmTravelTargetPick();
        UpdateCommandStrip();
        ShowScreenMessage("Click an open land cell or city to set this army's travel target.");
    }

    private void SetPickedArmyTravelTarget(int x, int y, string? cityId)
    {
        var army = OwnSelectedArmy();
        if (army is null || army.IsEmbarked) return;
        void SetTarget()
        {
            Session.SetArmyTravelTarget(new ArmyTravelTarget(army.Id, army.Nation, x, y, cityId));
            UpdateCommandStrip();
            ShowScreenMessage($"Travel target set for {army.Id}.");
        }
        if (Session.RetreatSupplyPlanFor(army.Id, isFleet: false) is null)
        {
            SetTarget();
            return;
        }
        var prompt = new ConfirmPrompt
        {
            Question = $"Replace {army.Id}'s Retreat and supply mode with a fixed travel target?",
        };
        prompt.Confirmed += () =>
        {
            CloseOverlay(prompt);
            Session.ClearRetreatSupplyPlan(army.Id, isFleet: false);
            SetTarget();
        };
        prompt.Refused += () => CloseOverlay(prompt);
        ShowOverlay(prompt);
    }

    private void CancelArmyTravelTarget()
    {
        _mapView.CancelTravelTargetPick();
        var army = OwnSelectedArmy();
        if (army is null) { ShowScreenMessage(SelectArmyMessage); return; }
        ShowScreenMessage(Session.ClearArmyTravelTarget(army.Id)
            ? $"Travel target cancelled for {army.Id}." : $"{army.Id} has no travel target.");
        UpdateCommandStrip();
    }

    /// <summary>The selected army when it is the active seat's own, or <see langword="null"/> — a foreign
    /// or missing army disables T111's entries.</summary>
    private ArmyState? OwnSelectedArmy()
    {
        var army = SelectedArmyOrCarried();
        return army is not null
            && string.Equals(army.Nation, Session.State.ActiveNationId, StringComparison.Ordinal)
            ? army
            : null;
    }

    /// <summary>A new army id that does not collide with an existing one, for the split dialog.</summary>
    private string NextSplitArmyId(string armyId) => ArmyDialogModels.NextArmyId(Session.State);

    /// <summary>The selected army, or the army the selected fleet carries, or <see langword="null"/>.</summary>
    private ArmyState? SelectedArmyOrCarried()
    {
        if (_selectedArmyId is not null)
        {
            return Session.State.ArmyById(_selectedArmyId);
        }

        if (_selectedFleetId is not null
            && Session.State.FleetById(_selectedFleetId)?.CarriedArmyId is { } carried)
        {
            return Session.State.ArmyById(carried);
        }

        return null;
    }

    /// <summary>
    /// Submits one line an Army dialog composed through <see cref="GameSession.Submit"/> and
    /// <see cref="OnCommandIssued"/> — the same path every other control's command takes, so
    /// <see cref="CommandIssued"/> counts it exactly once.
    /// </summary>
    private IReadOnlyList<string> SubmitFromDialog(string line)
    {
        if (!AllowStrategicInput()) return Array.Empty<string>();
        var lines = AppDiagnostics.Submit(Session, line, "dialog").Lines;
        _submittingFromDialog = true;
        try
        {
            OnCommandIssued(lines);
        }
        finally
        {
            _submittingFromDialog = false;
        }

        // #1280 F3: the dialog shows its reply in plain words, as the screen's message line does.
        return lines.Select(CommandOutcomeWording.ReadableLine).ToList();
    }

    /// <summary>
    /// #1286: a refused order's plain message over the map, unless a dialog gave the order (it shows its own
    /// reply) or the battle screen covers the map (it shows the message itself).
    /// </summary>
    private void ShowRefusalNotice(IReadOnlyList<string> lines)
    {
        if (_submittingFromDialog || ActiveOverlay is TacticalBattleScreen)
        {
            return;
        }

        var refusals = lines.Select(CommandOutcomeWording.RefusalMessage).OfType<string>().ToList();
        if (refusals.Count > 0)
        {
            _orderNotice.ShowMessage(string.Join('\n', refusals));
        }
        else
        {
            // A later order that went through replaces an old refusal rather than leaving it in view.
            _orderNotice.Close();
        }
    }

    /// <summary>#1286: the message the refusal notice over the map shows, or empty when it is closed.</summary>
    public string OrderNoticeForCheck => _orderNotice.Message;

    /// <summary>Shows a screen-level message without issuing a command — see <see cref="LastMessageForCheck"/>.</summary>
    private void ShowScreenMessage(string message)
    {
        _lastCommandLabel.Text = message;
        _lastCommandLabel.TooltipText = message;
        UpdateLastCommandLinesSkipped();
    }

    private void ShowHelpPage()
    {
        var page = new HelpPage { Ruleset = Session.Ruleset };
        page.Closed += () => CloseOverlay(page);
        ShowOverlay(page);
    }

    /// <summary>#1293: the Settings screen over the game; Close (or Esc) returns to it.</summary>
    private void ShowSettings()
    {
        var settings = new SettingsScreen { InGame = true };
        settings.BackRequested += () => CloseOverlay(settings);
        ShowOverlay(settings);
    }

    private void ShowAbout()
    {
        var about = new AboutDialog();
        about.Closed += () => CloseOverlay(about);
        ShowOverlay(about);
    }

    private void OnEndTurnPressed()
    {
        if (!AllowStrategicInput()) return;
        var warnings = EndTurnWarnings.For(
            Session.State,
            Session.World,
            Session.Ruleset,
            Session.State.ActiveNationId);
        if (!warnings.OpensBox)
        {
            SubmitConfirmedEndTurn();
            return;
        }

        var box = new EndTurnBox { WarningLines = warnings.Lines };
        box.EndTurnRequested += () =>
        {
            CloseOverlay(box);
            SubmitConfirmedEndTurn();
        };
        box.MakeMoreMovesRequested += () => CloseOverlay(box);
        ShowOverlay(box);
    }

    private void SubmitConfirmedEndTurn()
    {
        var summaries = new[] { RunAutoMobilize(), RunAutoRecruit(), RunArmyTravelTargets(),
                RunRetreatSupplyPlans(), RunArmyAutoSupport(), RunFleetAutoSupport() }
            .Where(s => !string.IsNullOrEmpty(s)).ToArray();
        SubmitForCheck("end");
        if (summaries.Length > 0) ShowScreenMessage(string.Join("\n", summaries));
    }

    private string? RunAutoMobilize()
    {
        if (!SettingsScreen.Current.AutoMobilizeReady) return null;
        var nationId = Session.State.ActiveNationId;
        if (Session.State.NationById(nationId)?.Control != SeatControl.Human) return null;

        var initialCount = Session.State.NationById(nationId)!.RecruitmentSlots.Count;
        var attempted = 0;
        var index = 0;
        var mobilized = 0;
        var skipped = new List<string>();
        while (attempted < initialCount && Session.State.NationById(nationId) is { } nation
               && index < nation.RecruitmentSlots.Count)
        {
            var proposal = AutoMobilizePlanner.Next(Session.State, Session.Ruleset, index);
            attempted++;
            if (proposal is null) { index++; continue; }

            var before = nation.RecruitmentSlots.Count;
            var response = SubmitForCheck(proposal.CommandLine);
            var after = Session.State.NationById(nationId)?.RecruitmentSlots.Count ?? before;
            if (after < before)
                mobilized++; // The queue compacted; inspect this index again.
            else
            {
                skipped.Add($"slot {index} at {proposal.CityId}: {AutoRefusalText(response, "mobilization refused")}");
                index++;
            }
        }

        var line = $"Auto mobilize: {mobilized} ready units mobilized.";
        return skipped.Count == 0 ? line : line + " Skipped: " + string.Join("; ", skipped);
    }

    private string? RunAutoRecruit()
    {
        var settings = SettingsScreen.Current.Normalized();
        if (!settings.AutoRecruitEnabled) return null;
        var nationId = Session.State.ActiveNationId;
        if (Session.State.NationById(nationId)?.Control != SeatControl.Human) return null;

        var recruited = 0;
        var cost = 0;
        var skipped = new List<string>();
        foreach (var city in Session.World.Cities)
        {
            var plan = settings.RecruitPlanFor(Session.World.Id, city.Id);
            if (plan is null) continue;
            var decision = AutoRecruitPlanner.ForCity(Session.State, Session.World, Session.Ruleset, settings, plan);
            if (decision.CommandLine is null)
            {
                if (decision.Reason != "city queue target reached") skipped.Add($"{city.Name}: {decision.Reason}");
                continue;
            }

            var before = Session.State.NationById(nationId)!.RecruitmentSlots.Count;
            var response = SubmitForCheck(decision.CommandLine);
            var after = Session.State.NationById(nationId)?.RecruitmentSlots.Count ?? before;
            if (after > before) { recruited++; cost += decision.CostTalents; }
            else skipped.Add($"{city.Name}: {AutoRefusalText(response, "recruitment refused")}");
        }

        foreach (var plan in settings.AutoRecruitPlans.Where(plan => plan.WorldId == Session.World.Id
                     && Session.World.Cities.All(city => city.Id != plan.CityId)))
            skipped.Add($"{plan.CityId}: city is not in this world");
        var line = $"Auto recruit: {recruited} units queued ({cost} talents).";
        return skipped.Count == 0 ? line : line + " Skipped: " + string.Join("; ", skipped);
    }

    private bool _runningArmyTravel;
    private bool _runningRetreatSupply;

    private string? RunArmyTravelTargets()
    {
        if (Session.ArmyTravelTargets.Count == 0) return null;
        var nationId = Session.State.ActiveNationId;
        if (Session.State.NationById(nationId)?.Control != SeatControl.Human) return null;
        var moved = 0;
        var arrived = 0;
        var waiting = 0;
        var stopped = new List<string>();
        var targets = Session.ArmyTravelTargets.ToArray();
        var ordered = Session.State.Armies.Select(army => army.Id)
            .Concat(targets.Select(target => target.ArmyId)).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var armyId in ordered)
        {
            var target = targets.FirstOrDefault(t => t.ArmyId == armyId);
            if (target is null || target.NationId != nationId) continue;
            if (Session.RetreatSupplyPlanFor(armyId, isFleet: false) is not null)
            {
                Session.ClearArmyTravelTarget(armyId);
                stopped.Add($"{armyId}: conflicting fixed target cleared for Retreat and supply");
                continue;
            }
            var decision = ArmyTravelPlanner.ForTarget(Session.State, Session.World, Session.Ruleset, target);
            if (decision.WaitForMoves) { waiting++; continue; }
            if (decision.Arrived)
            {
                Session.ClearArmyTravelTarget(armyId);
                arrived++;
                continue;
            }
            if (decision.CommandLine is null)
            {
                Session.ClearArmyTravelTarget(armyId);
                stopped.Add($"{armyId}: {decision.Reason}");
                continue;
            }

            var before = Session.State.ArmyById(armyId)!;
            IReadOnlyList<string> response;
            _runningArmyTravel = true;
            try
            {
                response = SubmitForCheck(decision.CommandLine);
                if (StrategicTravel.IsActive) StrategicTravel.Skip();
            }
            finally { _runningArmyTravel = false; }
            var after = Session.State.ArmyById(armyId);
            if (after is null || after.X == before.X && after.Y == before.Y)
            {
                Session.ClearArmyTravelTarget(armyId);
                var refusal = response.FirstOrDefault(line => line.StartsWith("Refused:", StringComparison.OrdinalIgnoreCase));
                stopped.Add($"{armyId}: {refusal ?? "route blocked or no progress"}");
                continue;
            }
            moved++;
            var next = ArmyTravelPlanner.ForTarget(Session.State, Session.World, Session.Ruleset, target);
            if (next.Arrived) { Session.ClearArmyTravelTarget(armyId); arrived++; }
            else if (next.Reason is { } reason && !next.WaitForMoves)
            {
                Session.ClearArmyTravelTarget(armyId);
                stopped.Add($"{armyId}: {reason}");
            }
        }
        UpdateCommandStrip();
        var line = $"Army targets: {moved} moved, {arrived} arrived, {waiting} waiting.";
        return stopped.Count == 0 ? line : line + " Stopped: " + string.Join("; ", stopped);
    }

    private string? RunRetreatSupplyPlans()
    {
        if (Session.RetreatSupplyPlans.Count == 0) return null;
        var nationId = Session.State.ActiveNationId;
        if (Session.State.NationById(nationId)?.Control != SeatControl.Human) return null;
        var settings = SettingsScreen.Current.Normalized();
        var planned = Session.RetreatSupplyPlans.ToArray();
        var ordered = Session.State.Armies.Select(a => (a.Id, IsFleet: false))
            .Concat(Session.State.Fleets.Select(f => (f.Id, IsFleet: true)))
            .Concat(planned.Select(p => (p.UnitId, p.IsFleet))).Distinct().ToArray();
        var summaries = new List<string>();
        _runningRetreatSupply = true;
        try
        {
            foreach (var (unitId, isFleet) in ordered)
            {
                var plan = planned.FirstOrDefault(p => p.UnitId == unitId && p.IsFleet == isFleet);
                if (plan is null || plan.NationId != nationId) continue;
                var summary = new List<string>();
                var decision = RetreatSupplyPlanner.Next(Session.State, Session.World,
                    Session.Ruleset, settings, plan);
                if (decision.Cancel || decision.Completed)
                {
                    Session.ClearRetreatSupplyPlan(unitId, isFleet);
                    summaries.Add($"{unitId}: {(decision.Completed ? "fully recovered" : decision.Reason)}");
                    continue;
                }

                if (decision.AtProvider)
                {
                    var gained = ApplyRecoverySupport(plan, decision.DestinationCityId, settings, summary);
                    decision = RetreatSupplyPlanner.Next(Session.State, Session.World,
                        Session.Ruleset, settings, plan);
                    if (!gained && decision.AtProvider)
                    {
                        Session.ClearRetreatSupplyPlan(unitId, isFleet);
                        summaries.Add($"{unitId}: provider could not improve recovery");
                        continue;
                    }
                }

                if (decision.CommandLine is { } line)
                {
                    var beforeX = isFleet ? Session.State.FleetById(unitId)?.X : Session.State.ArmyById(unitId)?.X;
                    var beforeY = isFleet ? Session.State.FleetById(unitId)?.Y : Session.State.ArmyById(unitId)?.Y;
                    var response = SubmitForCheck(line);
                    if (StrategicTravel.IsActive) StrategicTravel.Skip();
                    var afterX = isFleet ? Session.State.FleetById(unitId)?.X : Session.State.ArmyById(unitId)?.X;
                    var afterY = isFleet ? Session.State.FleetById(unitId)?.Y : Session.State.ArmyById(unitId)?.Y;
                    if (afterX is null || afterX == beforeX && afterY == beforeY)
                    {
                        Session.ClearRetreatSupplyPlan(unitId, isFleet);
                        summary.Add(AutoRefusalText(response, "route blocked or no progress"));
                    }
                    else
                    {
                        summary.Add($"moved toward {decision.DestinationCityId} ({afterX},{afterY})");
                        plan = plan with { DestinationCityId = decision.DestinationCityId };
                        Session.SetRetreatSupplyPlan(plan);
                        decision = RetreatSupplyPlanner.Next(Session.State, Session.World,
                            Session.Ruleset, settings, plan);
                        if (decision.AtProvider)
                        {
                            ApplyRecoverySupport(plan, decision.DestinationCityId, settings, summary);
                            decision = RetreatSupplyPlanner.Next(Session.State, Session.World,
                                Session.Ruleset, settings, plan);
                        }
                    }
                }

                if (Session.RetreatSupplyPlanFor(unitId, isFleet) is not null)
                {
                    if (decision.Cancel || decision.Completed)
                    {
                        Session.ClearRetreatSupplyPlan(unitId, isFleet);
                        summary.Add(decision.Completed ? "fully recovered" : decision.Reason ?? "stopped");
                    }
                    else
                    {
                        Session.SetRetreatSupplyPlan(plan with
                            { DestinationCityId = decision.DestinationCityId ?? plan.DestinationCityId });
                        if (decision.Waiting) summary.Add("waiting for moves");
                        else if (decision.CommandLine is not null) summary.Add($"next: {decision.DestinationCityId}");
                    }
                }
                summaries.Add($"{unitId}: {string.Join("; ", summary)}");
            }
        }
        finally { _runningRetreatSupply = false; }
        UpdateCommandStrip();
        return summaries.Count == 0 ? null : "Retreat and supply: " + string.Join(" | ", summaries);
    }

    private bool ApplyRecoverySupport(RetreatSupplyPlan plan, string? chosenCityId,
        UserSettings settings, List<string> summary)
    {
        var nationId = plan.NationId;
        var gainedAny = false;
        if (!plan.IsFleet && Session.State.ArmyById(plan.UnitId) is { } army)
        {
            var local = settings with { AutoSupplyArmies = true, AutoSupplyTargetPercent = 100,
                AutoFundArmies = true };
            var attempted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var city in Session.State.Cities)
                if (city.Id != chosenCityId) attempted.Add($"City:{city.Id}");
            while (Session.State.ArmyById(plan.UnitId) is { } current)
            {
                var proposal = ArmyAutoSupportPlanner.NextSupply(Session.State, current,
                    Session.Ruleset, local, attempted);
                if (proposal is null) break;
                attempted.Add(proposal.ProviderKey!);
                var before = current.SupplyTons;
                var response = SubmitForCheck(proposal.CommandLine);
                var gained = Math.Max(0, (Session.State.ArmyById(plan.UnitId)?.SupplyTons ?? before) - before);
                if (gained > 0) { gainedAny = true; summary.Add($"+{gained} t supply (0 talents)"); }
                else summary.Add(AutoRefusalText(response, "supply refused"));
            }
            if (Session.State.ArmyById(plan.UnitId) is { } funded
                && ArmyAutoSupportPlanner.Fund(Session.State, funded, Session.Ruleset, local) is { } money)
            {
                var before = funded.Money;
                var treasury = Session.State.NationById(nationId)?.Treasury ?? 0;
                var response = SubmitForCheck(money.CommandLine);
                var gained = Math.Max(0, (Session.State.ArmyById(plan.UnitId)?.Money ?? before) - before);
                if (gained > 0)
                {
                    summary.Add($"+{gained} talents (treasury -{Math.Max(0, treasury - (Session.State.NationById(nationId)?.Treasury ?? treasury))})");
                }
                else summary.Add(AutoRefusalText(response, "funding refused"));
            }
        }
        else if (plan.IsFleet && Session.State.FleetById(plan.UnitId) is { } fleet)
        {
            var local = settings with { AutoSupplyFleets = true, AutoFleetSupplyTargetPercent = 100,
                AutoRepairFleets = true, AutoFleetRepairTargetPercent = Session.Ruleset.Naval.MaxConditionPercent };
            var attempted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var city in Session.State.Cities)
                if (city.Id != chosenCityId) attempted.Add(city.Id);
            while (Session.State.FleetById(plan.UnitId) is { } current)
            {
                var proposal = FleetAutoSupportPlanner.NextSupply(Session.State, current,
                    Session.Ruleset, local, attempted);
                if (proposal is null) break;
                attempted.Add(proposal.CityId!);
                var before = current.SupplyTons;
                var response = SubmitForCheck(proposal.CommandLine);
                var gained = Math.Max(0, (Session.State.FleetById(plan.UnitId)?.SupplyTons ?? before) - before);
                if (gained > 0) { gainedAny = true; summary.Add($"+{gained} t supply (0 talents)"); }
                else summary.Add(AutoRefusalText(response, "fleet supply refused"));
            }
            if (Session.State.FleetById(plan.UnitId) is { } repairing
                && FleetAutoSupportPlanner.Repair(Session.State, repairing, Session.Ruleset, local) is { } repair)
            {
                var before = repairing.ConditionPercent;
                var treasury = Session.State.NationById(nationId)?.Treasury ?? 0;
                var response = SubmitForCheck(repair.CommandLine);
                var gained = Math.Max(0, (Session.State.FleetById(plan.UnitId)?.ConditionPercent ?? before) - before);
                if (gained > 0)
                {
                    gainedAny = true;
                    summary.Add($"+{gained}% condition, {Math.Max(0, treasury - (Session.State.NationById(nationId)?.Treasury ?? treasury))} talents");
                }
                else summary.Add(AutoRefusalText(response, "repair refused"));
            }
        }
        return gainedAny;
    }

    private string? RunArmyAutoSupport()
    {
        var settings = SettingsScreen.Current.Normalized();
        if (!settings.AutoSupplyArmies && !settings.AutoFundArmies) return null;
        var nationId = Session.State.ActiveNationId;
        if (Session.State.NationById(nationId)?.Control != SeatControl.Human) return null;

        var supplied = 0;
        var suppliedTons = 0;
        var funded = 0;
        var fundedTalents = 0;
        var skipped = new List<string>();
        var armyIds = Session.State.Armies.Where(a => a.Nation == nationId).Select(a => a.Id).ToArray();
        foreach (var armyId in armyIds)
        {
            if (settings.AutoSupplyArmies)
            {
                var attempted = new HashSet<string>(StringComparer.Ordinal);
                while (Session.State.ArmyById(armyId) is { } army)
                {
                    var proposal = ArmyAutoSupportPlanner.NextSupply(
                        Session.State, army, Session.Ruleset, settings, attempted);
                    if (proposal is null) break;
                    attempted.Add(proposal.ProviderKey!);
                    var before = army.SupplyTons;
                    var response = SubmitForCheck(proposal.CommandLine);
                    var gained = Math.Max(0, (Session.State.ArmyById(armyId)?.SupplyTons ?? before) - before);
                    if (gained > 0) { supplied++; suppliedTons += gained; }
                    else skipped.Add($"{armyId}: {AutoRefusalText(response, "supply refused")}");
                }

                if (Session.State.ArmyById(armyId) is { } afterSupply)
                {
                    var capacity = Engine.Economy.SupplyCapacity.ArmyDialogCapacityTons(
                        afterSupply.TotalTroops, Session.Ruleset);
                    var target = (int)Math.Ceiling((double)capacity * settings.AutoSupplyTargetPercent / 100);
                    if (afterSupply.SupplyTons < target)
                        skipped.Add($"{armyId}: no eligible owned supply provider has enough stock");
                }
            }

            if (settings.AutoFundArmies && Session.State.ArmyById(armyId) is { } moneyArmy)
            {
                var proposal = ArmyAutoSupportPlanner.Fund(Session.State, moneyArmy, Session.Ruleset, settings);
                if (proposal is { })
                {
                    var before = moneyArmy.Money;
                    var response = SubmitForCheck(proposal.CommandLine);
                    var gained = Math.Max(0, (Session.State.ArmyById(armyId)?.Money ?? before) - before);
                    if (gained > 0) { funded++; fundedTalents += gained; }
                    else skipped.Add($"{armyId}: {AutoRefusalText(response, "funding refused")}");
                }
                else if (moneyArmy.Money < Math.Min(settings.AutoPurseTargetTalents,
                             Session.Ruleset.Economy.PurseCapPerUnit))
                    skipped.Add($"{armyId}: treasury reserve prevents funding");
            }
        }

        var line = $"Auto support: {supplied} supply transfers ({suppliedTons} t), "
            + $"{funded} purse transfers ({fundedTalents} talents).";
        return skipped.Count == 0 ? line : line + " Skipped: " + string.Join("; ", skipped);
    }

    private string? RunFleetAutoSupport()
    {
        var settings = SettingsScreen.Current.Normalized();
        if (!settings.AutoSupplyFleets && !settings.AutoRepairFleets) return null;
        var nationId = Session.State.ActiveNationId;
        if (Session.State.NationById(nationId)?.Control != SeatControl.Human) return null;

        var supplied = 0;
        var suppliedTons = 0;
        var repaired = 0;
        var repairedPoints = 0;
        var repairCost = 0;
        var skipped = new List<string>();
        var fleetIds = Session.State.Fleets.Where(f => f.Nation == nationId).Select(f => f.Id).ToArray();
        foreach (var fleetId in fleetIds)
        {
            if (settings.AutoSupplyFleets)
            {
                var attempted = new HashSet<string>(StringComparer.Ordinal);
                while (Session.State.FleetById(fleetId) is { } fleet)
                {
                    var proposal = FleetAutoSupportPlanner.NextSupply(
                        Session.State, fleet, Session.Ruleset, settings, attempted);
                    if (proposal is null) break;
                    attempted.Add(proposal.CityId!);
                    var before = fleet.SupplyTons;
                    var response = SubmitForCheck(proposal.CommandLine);
                    var gained = Math.Max(0, (Session.State.FleetById(fleetId)?.SupplyTons ?? before) - before);
                    if (gained > 0) { supplied++; suppliedTons += gained; }
                    else skipped.Add($"{fleetId}: {AutoRefusalText(response, "supply refused")}");
                }

                if (Session.State.FleetById(fleetId) is { } afterSupply)
                {
                    if (afterSupply.IsUnderConstruction)
                        skipped.Add($"{fleetId}: supply skipped (under construction)");
                    else
                    {
                        var capacity = Engine.Economy.SupplyCapacity.FleetCapacityTons(afterSupply.Ships, Session.Ruleset);
                        var target = (int)Math.Ceiling((double)capacity * settings.AutoFleetSupplyTargetPercent / 100);
                        if (afterSupply.SupplyTons < target)
                            skipped.Add($"{fleetId}: no owned town has eligible supply stock");
                    }
                }
            }

            if (settings.AutoRepairFleets && Session.State.FleetById(fleetId) is { } repairFleet)
            {
                var proposal = FleetAutoSupportPlanner.Repair(Session.State, repairFleet, Session.Ruleset, settings);
                if (proposal is { })
                {
                    var beforeCondition = repairFleet.ConditionPercent;
                    var beforeTreasury = Session.State.NationById(nationId)?.Treasury ?? 0;
                    var response = SubmitForCheck(proposal.CommandLine);
                    var gained = Math.Max(0, (Session.State.FleetById(fleetId)?.ConditionPercent ?? beforeCondition)
                        - beforeCondition);
                    if (gained > 0)
                    {
                        repaired++;
                        repairedPoints += gained;
                        repairCost += Math.Max(0, beforeTreasury - (Session.State.NationById(nationId)?.Treasury ?? beforeTreasury));
                    }
                    else skipped.Add($"{fleetId}: {AutoRefusalText(response, "repair refused")}");
                }
                else if (repairFleet.ConditionPercent < settings.AutoFleetRepairTargetPercent)
                {
                    var reason = repairFleet.IsUnderConstruction ? "under construction"
                        : repairFleet.IsCarryingArmy ? "carrying an army"
                        : "no eligible owned town or treasury above reserve";
                    skipped.Add($"{fleetId}: repair skipped ({reason})");
                }
            }
        }

        var line = $"Auto fleets: {supplied} supply transfers ({suppliedTons} t), "
            + $"{repaired} repairs ({repairedPoints} points, {repairCost} talents).";
        return skipped.Count == 0 ? line : line + " Skipped: " + string.Join("; ", skipped);
    }

    private static string AutoRefusalText(IReadOnlyList<string> response, string fallback) =>
        response.FirstOrDefault(line => line.StartsWith("Refused:", StringComparison.OrdinalIgnoreCase))
        ?? response.LastOrDefault(line => !string.IsNullOrWhiteSpace(line))
        ?? fallback;

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

    /// <summary>A short player-nation/campaign-turn suggestion; existing writeback paths are unchanged.</summary>
    private string SuggestedSaveFileName(string directory)
    {
        var humans = Session.State.Nations.Where(n => n.Control == SeatControl.Human).ToList();
        var nation = humans.Count == 1 ? humans[0]
            : Session.State.NationById(_displayViewer) ?? Session.State.NationById(Session.State.ActiveNationId);
        var name = nation?.Id == "ptolemaic" ? "ptolemeans" : nation?.Name ?? "campaign";
        var safe = new string(name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        var stem = string.Join("_", safe.Split('_', StringSplitOptions.RemoveEmptyEntries));
        if (stem.Length > 24) stem = stem[..24].TrimEnd('_');
        if (stem.Length == 0) stem = "campaign";
        var basis = stem + "_" + Session.State.Calendar.TurnIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var file = basis + ".json";
        for (var suffix = 2; (File.Exists(Path.Combine(directory, file)) || Directory.Exists(Path.Combine(directory, file))); suffix++)
            file = basis + "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".json";
        return file;
    }

    /// <summary>
    /// <paramref name="checkUniqueSuffix"/> (rework round 1, N2, non-blocking): a real player's click
    /// calls the parameterless overload above, using a concise nation/turn suggestion for the first Save.
    /// Existing files retain their explicit writeback path. <see cref="PressSaveForCheck"/>
    /// passes a fixed, check-specific suffix instead, so a Godot check's own save can never collide with,
    /// overwrite or (after cleanup) delete a real save of the same scenario and turn — see that method's
    /// own remarks.
    /// </summary>
    private void OnSavePressed(string? checkUniqueSuffix)
    {
        var directory = ProjectSettings.GlobalizePath(SavesDirectory);
        Directory.CreateDirectory(directory);

        var suffix = checkUniqueSuffix ?? CheckSaveSuffix;
        if (suffix is null && (LastSavedPath ?? OpenedPath) is { } existing)
        {
            SaveToFile(existing);
            return;
        }

        var fileName = suffix is null
            ? SuggestedSaveFileName(directory)
            : $"{Session.Scenario.Id}-turn-{Session.State.Calendar.TurnIndex}-{suffix}.json";
        SaveToFile(Path.Combine(directory, fileName));
    }

    /// <summary>
    /// Writes the current game to <paramref name="path"/> through the real <c>save &lt;path&gt;</c>
    /// command and shows its outcome, the one path both Save and Save As use.
    /// </summary>
    private bool SaveToFile(string path)
    {
        // T139 [designed wording]: in a hotseat game an offer of peace held for a seat that is not the
        // active one refuses the save, as the engine's `save` does. Say whose answer is owed, and submit
        // nothing. The active seat's own offer is shown as a window before the player can reach Save, so
        // that case is left to the engine's own refusal line.
        if (Session.HasPendingPeaceOffers
            && Session.PendingPeaceOfferFor(Session.State.ActiveNationId) is null
            && Session.State.Nations.FirstOrDefault(
                n => Session.PendingPeaceOfferFor(n.Id) is not null) is { } owing)
        {
            var refusal =
                $"{owing.Name} must answer an offer of peace at its turn before the game can be saved.";
            _saveConfirmationLabel.Text = refusal;
            _saveConfirmationLabel.TooltipText = refusal;
            (ActiveOverlay as TacticalBattleScreen)?.ShowMessage(refusal);
            return false;
        }

        var output = AppDiagnostics.Submit(Session, $"save {path}", "save", path);
        OnCommandIssued(output.Lines);
        var confirmation = CommandOutcomeText.OutcomeBlock(output.Lines);
        _saveConfirmationLabel.Text = confirmation;
        _saveConfirmationLabel.TooltipText = confirmation;
        (ActiveOverlay as TacticalBattleScreen)?.ShowMessage(confirmation);
        var saved = output.Lines.Any(l => l.StartsWith("Saved to", StringComparison.Ordinal));
        if (saved)
        {
            LastSavedPath = path;
        }

        return saved;
    }

    /// <summary>
    /// The path <see cref="OnSavePressed()"/> last wrote to, set only after the real <c>save &lt;path&gt;</c> command succeeded (fix #1081) — <see langword="null"/> before any save. Exposed, with
    /// <see cref="PressSaveForCheck"/>, so <c>godot/Checks/SaveResumeCheck.cs</c> and
    /// <c>godot/Checks/SaveResumeScreenshotTour.cs</c> can find the exact file the Save button produced
    /// without duplicating its naming rule.
    /// </summary>
    public string? LastSavedPath { get; private set; }

    /// <summary>
    /// The text the shared last-command label (<see cref="_lastCommandLabel"/>, under the map and the
    /// side column) currently carries: fix #484's <see cref="CommandOutcomeText.OutcomeBlock"/> of the most
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
    /// Fix #537: T95's Save confirmation label's own laid-out size, and the minimum width the top bar
    /// row derives from it. Exposed so <c>godot/Checks/CommandFeedbackCheck.cs</c> can record exactly
    /// which label a Save widens the root with, rather than only the root's combined minimum.
    /// </summary>
    public Vector2 SaveConfirmationLabelSize => _saveConfirmationLabel.Size;

    /// <summary>Fix #537: the minimum width <see cref="_saveConfirmationLabel"/> asks of its parent row,
    /// the value that used to widen the whole screen past the viewport. Exposed for the same check.</summary>
    public Vector2 SaveConfirmationLabelMinimumSize => _saveConfirmationLabel.GetCombinedMinimumSize();

    /// <summary>Fix #537: the tooltip on <see cref="_saveConfirmationLabel"/>, which carries the full,
    /// un-ellipsized Save outcome once the label ellipsizes. Exposed for the same check.</summary>
    public string SaveConfirmationTooltip => _saveConfirmationLabel.TooltipText;

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
        if (!AllowStrategicInput()) return Array.Empty<string>();
        var lines = AppDiagnostics.Submit(Session, commandLine, "check").Lines;
        OnCommandIssued(lines);
        return lines;
    }

    /// <summary>
    /// T149 (Sol's review of PR #886, R4): the screen's own cue player, read-only, so a headless
    /// check reads the production player the production subscription writes to — never a private
    /// player with a check-made subscription beside it, which could pass while this screen's own
    /// wiring was broken. The check-seam convention <see cref="SubmitForCheck"/> establishes,
    /// applied to the audio slice this task owns.
    /// </summary>
    public Audio.SoundPlayer SoundPlayerForCheck => _soundPlayer;

    /// <summary>
    /// T149: the one subscription on <see cref="GameSession.EventsPublished"/>. Reads the
    /// Godot-free <see cref="Audio.SoundCues"/> for the events one <see cref="GameSession.Submit"/>
    /// published, plays the resulting keys, and does nothing else — never a state read, never a
    /// label update, never a news line; the cue list's contract is "no listener state change". A
    /// <c>Submit</c> that produced no events raises the handler with an empty list, which the
    /// player already treats as a no-op.
    /// </summary>
    private void OnSessionEventsPublished(IReadOnlyList<Engine.Core.DomainEvent> events)
    {
        // Fix #917: the original's turn-start box. The engine's text is shown verbatim; the notice is
        // never a news line and changes no relation. The Accept Offer button stays the way to accept.
        foreach (var domainEvent in events)
        {
            if (domainEvent is Engine.Diplomacy.PendingDiplomaticOfferAnnounced announced)
            {
                _pendingOfferNotices.Enqueue(announced.DialogText);
            }
            else if (domainEvent is Engine.Recruitment.Commands.RecruitMobilized
                     { ArmyWasCreated: true } mobilized)
            {
                AssignRallyPointToNewArmy(mobilized);
            }
        }

        if (_soundPlayer is null)
        {
            return;
        }

        _soundPlayer.Muted = !SettingsScreen.SoundEnabled;
        var cues = Audio.SoundCues.ForEvents(events, Session.State, Session.LastTacticalTimeline);
        _soundPlayer.Play(cues);
    }

    private void AssignRallyPointToNewArmy(Engine.Recruitment.Commands.RecruitMobilized mobilized)
    {
        var rally = Session.RallyPointFor(mobilized.NationId);
        var army = Session.State.ArmyById(mobilized.ArmyId);
        if (rally is null || army is null || army.Nation != mobilized.NationId) return;
        if (rally.CityId is { } cityId)
        {
            if (Session.State.CityById(cityId) is not { } city || city.Owner != rally.NationId)
            {
                Session.ClearRallyPoint(rally.NationId);
                ShowScreenMessage($"Rally point cleared: city {cityId} is no longer owned by {rally.NationId}.");
                UpdateCommandStrip();
                return;
            }
        }
        else if ((uint)rally.X >= (uint)Session.World.Width || (uint)rally.Y >= (uint)Session.World.Height)
        {
            Session.ClearRallyPoint(rally.NationId);
            ShowScreenMessage($"Rally point cleared: cell ({rally.X}, {rally.Y}) is outside this world.");
            UpdateCommandStrip();
            return;
        }

        Session.SetArmyTravelTarget(new ArmyTravelTarget(army.Id, army.Nation,
            rally.X, rally.Y, rally.CityId, FromRally: true));
        UpdateCommandStrip();
    }

    private void OnCommandIssued(IReadOnlyList<string> lines)
    {
        if (!_runningArmyTravel && !_runningRetreatSupply && lines.FirstOrDefault() is { } command)
        {
            var parts = command.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3 && parts[0] == ">"
                && parts[1] is "move" or "attack-army" or "besiege-city" or "embark-army" or "disband-army" or "split-army")
            {
                Session.ClearArmyTravelTarget(parts[2]);
                Session.ClearRetreatSupplyPlan(parts[2], isFleet: false);
                UpdateCommandStrip();
            }
            else if (parts.Length >= 3 && parts[0] == ">"
                     && parts[1] is "move-fleet" or "attack-fleet" or "scuttle-fleet" or "split-fleet")
            {
                Session.ClearRetreatSupplyPlan(parts[2], isFleet: true);
                UpdateCommandStrip();
            }
        }
        var exact = _tacticalCompletionRouting ?? new SessionOutput(lines, false, Session.LastBattles, Session.LastSeatFalls)
        { TacticalTimeline = Session.LastTacticalTimeline, StrategicTimeline = Session.LastStrategicTimeline };
        _tacticalCompletionRouting = null;
        Session.ConsumeStrategicTimeline();
        // Save/help callbacks may replace Last*, but cannot replace the retained movement completion.
        if (StrategicTravel.IsActive) { CommandIssued?.Invoke(lines); return; }
        if (Session.OpenTacticalBattle is not null)
            _nextTacticalOpening = _nextTacticalOpening.Concat(exact.TacticalTimeline).Distinct().ToArray();
        var batch = exact.StrategicTimeline;
        if (batch.Steps.Count > 0)
        {
            StrategicTravel.Begin(batch, _displayViewer, () =>
            {
                if (_closing) return;
                ReconcileDisplaySelection();
                PresentCommand(lines, exact);
            }, retainArrival: Session.OpenTacticalBattle is not null);
            RefreshTravelDisplay();
            if (StrategicTravel.CellSeconds <= 0) StrategicTravel.Skip();
            return;
        }
        if (Session.OpenTacticalBattle is null) StrategicTravel.ReleaseArrival();
        PresentCommand(lines, exact);
    }

    private int _commandRoutingDepth;

    private void PresentCommand(IReadOnlyList<string> lines, SessionOutput retained)
    {
        _commandRoutingDepth++;
        try { PresentCommandCore(lines, retained); }
        finally
        {
            _commandRoutingDepth--;
            if (_commandRoutingDepth == 0) ShowNextOverlay();
        }
    }

    private void PresentCommandCore(IReadOnlyList<string> lines, SessionOutput retained)
    {
        // Consume the tactical completion's own retained facts before any callback can submit Save/help
        // and replace the session's Last* properties. Ordinary command callbacks keep their existing source.
        var tacticalCompletion = retained;
        // Fix #484: Submit always ends its output with a blank separator line, so lines[^1] was always
        // empty and this label never showed a command's result. CommandOutcomeText.OutcomeBlock is the
        // screen's one shared rule (T95's Save label uses it too): the command's whole own outcome, up to
        // the round footer's News: section, trimmed, with whitespace-only spacer lines dropped. Every
        // input path -- map click, context-panel button, End Turn, SubmitForCheck, Save -- funnels
        // through here.
        CommandIssued?.Invoke(lines);
        if (_closing) return;

        // Reconcile removal/exhaustion before panel and strip refresh, regardless of issuing control.
        ReconcileDisplaySelection();
        UpdateCommandStrip();

        // T147 (bug #781 point 3): the active human seat's pending offer is the one its window answers, so
        // the offer's own dialog lines and the CLI prompt that follows them are left out of the output
        // area; the window shows them instead.
        var pendingOffers = new List<IReadOnlyList<string>>();
        foreach (var nation in Session.State.Nations)
        {
            if (Session.PendingPeaceOfferFor(nation.Id) is { } offer)
            {
                pendingOffers.Add(offer.Lines);
            }
        }

        var rawOutcome = CommandOutcomeText.OutcomeBlock(lines, pendingOffers);
        var outcome = CommandOutcomeWording.Apply(rawOutcome, Session.State);

        // T147 (bug #781 point 4): below an accepted order's readable line, the news entries the order
        // added to State.NewsLog. Only an order whose acceptance the app rewrote gets them; an "end"
        // round and a rejected order keep today's block, and nothing else moves.
        if (!string.Equals(outcome, rawOutcome, StringComparison.Ordinal))
        {
            var added = CommandOutcomeText.NewsAddedSince(
                _newsEntriesBeforeCommand, Session.State.NewsLog.Slots);

            if (added.Count > 0)
            {
                outcome = outcome.Length == 0
                    ? string.Join('\n', added)
                    : outcome + "\n" + string.Join('\n', added);
            }
        }

        _newsEntriesBeforeCommand = Session.State.NewsLog.Slots;

        _lastCommandLabel.Text = outcome;
        _lastCommandLabel.TooltipText = outcome;
        UpdateLastCommandLinesSkipped();
        ShowRefusalNotice(lines);

        // T127: the battle screen covers the map, so it shows the engine's answer itself (a refused Save during an
        // AI-phase battle shows the engine's own message and nothing else).
        if (ActiveOverlay is TacticalBattleScreen openBattle)
        {
            openBattle.ShowMessage(outcome);
        }

        // T110 [designed]: at every turn start the viewed nation is the active seat's (Scope), even when
        // the active seat itself is unchanged — in a one-human game "end" plays every AI seat and comes
        // back to the same seat, so a seat comparison alone missed the turn boundary and a viewed
        // Carthage carried into Rome's next turn. The calendar's turn index is the boundary; the active
        // seat id changing mid-turn (a hotseat pass) is the other. _lastKnownActiveNationId is still the
        // prior turn's until CheckForHotseatHandoff updates it later in this method.
        var activeId = Session.State.ActiveNationId;
        // A suspended AI lap is still being viewed by the human who ended the turn.
        // Advance viewer only at an actual human handoff, or in the existing no-human watch mode.
        if (Session.State.NationById(activeId)?.Control == SeatControl.Human
            || !Session.State.Nations.Any(n => n.Control == SeatControl.Human)) _displayViewer = activeId;
        var turnIndex = Session.State.Calendar.TurnIndex;
        var turnStarted = turnIndex != _lastKnownTurnIndex;
        if (turnStarted) AppDiagnostics.Mark(DiagnosticEvent.Turn, "main", turnIndex, activeId);
        _lastKnownTurnIndex = turnIndex;
        if (turnStarted || !string.Equals(activeId, _lastKnownActiveNationId, StringComparison.Ordinal))
        {
            // Only re-view when it actually differs: an unconditional SetViewedNation on every "end"
            // would clear a Find-a-city highlight chosen this turn for a view that did not change.
            if (!string.Equals(ViewedNationId, _displayViewer, StringComparison.Ordinal))
            {
                SetViewedNation(_displayViewer);
            }
        }

        // B1: every issued command can move an army, capture a city or end a turn, which changes the
        // tiles the active Show layers mark. Re-queue the mini-map's own redraw so the painted
        // highlights follow the live state rather than going stale until something else redraws.
        _areaMapView.Refresh();
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
        //
        // T116: which of those battles are shown now is the router's call. A battle fought against a
        // human seat that is not active once this call returns is held for that seat and released at its
        // own turn start; a battle with no human side, a human's own attack, or one against the seat this
        // call returns to, is shown now. The human seats come from the live state, never the scenario's
        // static seats, so a seat deposed or eliminated mid-call T87-style is already gone.
        var humanSeatIds = Session.State.Nations
            .Where(n => n.Control == SeatControl.Human)
            .Select(n => n.Id)
            .ToList();
        // T126/T127: a battle the human fought on the board has been seen, so it is shown now, never held.
        // A tactical battle publishes no BattleResult of its own (the Battle ended window is its report), so
        // this holds only a result between the very armies a board battle was fought by.
        var commandBattles = tacticalCompletion?.Battles ?? Session.LastBattles;
        var foughtOnBoard = commandBattles
            .Where(b => _boardBattleArmies.Contains((b.AttackerId, b.DefenderId)))
            .ToList();
        _battleReportRouter.ShowOtherNationsBattles = SettingsScreen.Current.ShowOtherBattleReports;
        var battlesToShowNow = _battleReportRouter.Route(
            commandBattles, humanSeatIds, Session.State.ActiveNationId, foughtOnBoard);
        foreach (var battle in battlesToShowNow)
        {
            _pendingBattleOverlays.Enqueue(battle);
        }

        // T138: one game-end window per human seat that fell on this call, in order. A fall in the
        // construction-time prelude arrives here on the first command too, the same way the battles do.
        foreach (var fall in tacticalCompletion?.SeatFalls ?? Session.LastSeatFalls)
        {
            _pendingGameEndOverlays.Enqueue(fall);
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

        // T132: F12 (no modifier) hides and shows the right-hand column, like the button beside it.
        if (@event is InputEventKey { Pressed: true, Echo: false } panelKey
            && SidePanelToggle.IsToggleKey(
                panelKey.Keycode.ToString(), panelKey.CtrlPressed, panelKey.ShiftPressed, panelKey.AltPressed))
        {
            ToggleSidePanel();
            GetViewport().SetInputAsHandled();
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
            if (_mapView.TravelTargetPickArmed)
            {
                _mapView.CancelTravelTargetPick();
                UpdateCommandStrip();
                ShowScreenMessage("Travel target selection cancelled.");
                GetViewport().SetInputAsHandled();
                return;
            }
            SkipTravelAndClearSelection();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree()
    {
        _closing = true;
        StrategicTravel.Changed -= RefreshTravelDisplay;
        StrategicTravel.BoundaryCompleted -= OnStrategicBoundaryCompleted;
        StrategicTravel.Close();
        SidePanelToggle.Changed -= ApplySidePanelState;

        // T149: the screen is the only Owns slice that subscribes; every other Owns line lives in
        // godot/Audio. Unsubscribing here keeps a screen swap from leaving a stale handler on
        // Session.EventsPublished.
        Session.EventsPublished -= OnSessionEventsPublished;
    }

    private void ToggleSidePanel()
    {
        _sidePanelToggle.Toggle();
        ApplySidePanelState();
    }

    private void ApplySidePanelState()
    {
        _sideColumn.Visible = _sidePanelToggle.IsShown;
        _sidePanelButton.Text = _sidePanelToggle.Caption;
        _sidePanelButton.TooltipText = _sidePanelToggle.Tooltip;
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
        if (_closing || StrategicTravel.IsActive || _commandRoutingDepth > 0) return;
        // Saving still writes authoritative state while replay is intermediate. Its ordinary command
        // callback must not open a peace/result/next-battle window ahead of terminal consumption.
        if (ActiveOverlay is TacticalBattleScreen { IsPlayingBack: true } or BattleEndedScreen) return;

        // T127: an open tactical battle is fought on its screen before anything else is shown; the windows
        // queued behind it wait for the battle's end (its OK calls this again).
        if (Session.OpenTacticalBattle is { } openBattle)
        {
            if (ActiveOverlay is not TacticalBattleScreen)
            {
                ShowTacticalBattleOverlay(openBattle);
            }

            return;
        }

        // T138: a fall window comes after this call's battle windows and before a hotseat hand-off
        // (docs/tasks/T138.md's scope item 4), so the player whose seat fell sees the battle that caused
        // it and then the fall, before the device is passed. With no fall queued, T116's own order stands
        // unchanged: the hand-off first, then the battles routed to the seat it hands to.
        if (_pendingGameEndOverlays.Count > 0)
        {
            if (_pendingBattleOverlays.Count > 0)
            {
                ShowBattleResultOverlay(_pendingBattleOverlays.Dequeue());
                return;
            }

            ShowGameEndOverlay(_pendingGameEndOverlays.Dequeue());
            return;
        }

        if (CheckForHotseatHandoff())
        {
            return;
        }

        if (_pendingBattleOverlays.Count > 0)
        {
            ShowBattleResultOverlay(_pendingBattleOverlays.Dequeue());
            return;
        }

        // T139: the active seat's own offer of peace comes after its battle windows. An offer is raised
        // by the battle that precedes it, so by now the battle window has been shown and closed; one for
        // a seat that is not active waits in the engine until that seat's turn.
        if (ActiveOverlay is not PeaceOfferScreen
            && Session.PendingPeaceOfferFor(Session.State.ActiveNationId) is { } offer)
        {
            ShowPeaceOfferOverlay(offer);
            return;
        }

        if (ActiveOverlay is null && _pendingOfferNotices.Count > 0)
        {
            var notice = new OfferNoticeBox { Text = _pendingOfferNotices.Dequeue() };
            notice.Closed += () =>
            {
                CloseOverlay(notice);
                ShowNextOverlay();
            };
            ShowOverlay(notice);
        }
    }

    /// <summary>Fix #917: the OK-only box the original shows at the human's turn start for an AI
    /// nation's offer of trade or alliance. Closes on OK and on Esc.</summary>
    internal partial class OfferNoticeBox : Control
    {
        public event Action? Closed;

        public required string Text { get; init; }

        public override void _Ready()
        {
            var backdrop = new ColorRect { Color = AppTheme.Scrim, MouseFilter = MouseFilterEnum.Stop };
            backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(backdrop);

            var center = new CenterContainer();
            center.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(center);

            var panel = UiKit.MakePanel(UiKit.PanelColor);
            panel.CustomMinimumSize = new Vector2(420, 0);
            center.AddChild(panel);

            var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            column.AddThemeConstantOverride("separation", AppTheme.DialogGap);
            panel.AddChild(column);

            var label = UiKit.MakeLabel(Text, AppTheme.Body, UiKit.TextColor);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            column.AddChild(label);

            var ok = UiKit.MakeButton("OK", Close);
            ok.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
            column.AddChild(ok);
            ok.GrabFocus();
        }

        public void Close() => Closed?.Invoke();

        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
            {
                // Close removes this node from the tree, so the viewport is taken first.
                var viewport = GetViewport();
                Close();
                viewport.SetInputAsHandled();
            }
        }
    }

    /// <summary>
    /// T139: opens the Offer of peace window for the active seat's pending offer. It has no Close; the
    /// answer is submitted through <see cref="OnCommandIssued"/>, so <see cref="CommandIssued"/> counts it
    /// once and the reply line shows as any command's does.
    /// </summary>
    private void ShowPeaceOfferOverlay(PendingPeaceOffer offer)
    {
        var screen = new PeaceOfferScreen { Model = PeaceOfferViewModel.FromOffer(offer) };
        screen.Answered += command =>
        {
            CloseOverlay(screen);
            OnCommandIssued(AppDiagnostics.Submit(Session, command, "peace").Lines);
        };
        ShowOverlay(screen);
    }

    /// <summary>
    /// T138: opens the game-end window for one fallen seat, from the engine's own <see cref="SeatFall"/>
    /// and the nation's live start figures. The window's own Main menu exits to the main menu exactly as
    /// File → Close does (no confirmation — nothing is left to lose); its View map / Continue closes the
    /// window and lets <see cref="ShowNextOverlay"/> carry on with the hotseat hand-off and any battles.
    /// </summary>
    private void ShowGameEndOverlay(SeatFall fall)
    {
        if (Session.State.NationById(fall.NationId) is not { } nation)
        {
            return;
        }

        var model = GameEndViewModel.FromFall(
            fall, nation, Session.Ruleset, Session.IsGameOver);
        var screen = new GameEndScreen { Model = model };
        screen.MainMenuRequested += () => OwningAppRoot?.ShowMainMenu();
        screen.Closed += () =>
        {
            CloseOverlay(screen);
            ShowNextOverlay();
        };
        ShowOverlay(screen);
    }

    /// <summary>
    /// T127: opens the tactical battle screen for the battle the session holds open (a human's attack, an AI seat's
    /// attack on a human, or a loaded mid-battle save). Its orders go to the session; when an order ends the
    /// battle the <em>Battle ended</em> window follows, and its OK carries on with the call's other windows and the
    /// peace offer.
    /// </summary>
    private IReadOnlyList<TacticalBattleFrame> _nextTacticalOpening = Array.Empty<TacticalBattleFrame>();
    private SessionOutput? _tacticalCompletionRouting;

    public void PresentOutputForCheck(SessionOutput output) => CompleteTacticalCommand(output);

    private void CompleteTacticalCommand(SessionOutput output)
    {
        _tacticalCompletionRouting = output;
        OnCommandIssued(output.Lines);
    }

    private void ShowTacticalBattleOverlay(TacticalBattleView view)
    {
        _soundPlayer.ClearTactical();
        _boardBattleArmies.Add((view.AttackerArmyId, view.DefenderArmyId));
        var screen = new TacticalBattleScreen
        {
            Session = Session, InitialView = view, RepositoryRoot = RepositoryRoot,
            InitialTimeline = _nextTacticalOpening.Concat(Session.InitialTacticalTimeline).Concat(Session.LastTacticalTimeline)
                .Where(frame => frame.After.View.AttackerArmyId == view.AttackerArmyId
                    && frame.After.View.DefenderArmyId == view.DefenderArmyId).ToArray(),
        };
        _nextTacticalOpening = Array.Empty<TacticalBattleFrame>();
        screen.OrderIssued += output =>
        {
            CompleteTacticalCommand(output);
            if (output.TacticalTimeline.Count > 0)
                screen.ShowMessage("Battlefield updated · " + screen.Model.ControllerText);
        };
        var audio = new Audio.TacticalPlaybackAudio(Session.Ruleset, () => SettingsScreen.SoundEnabled, _soundPlayer.PlayTactical);
        screen.ExchangeEntered += audio.ExchangeEntered;
        screen.MeleeTargetSet += (unit, enemy) =>
        {
            if (SettingsScreen.SoundEnabled)
                _soundPlayer.PlayTactical(new Audio.TacticalAudioCue($"melee-target {unit}->{enemy}", 0, IC2.Engine.Assets.AssetKeys.SfxBattleMelee));
        };
        screen.BattleEnded += output => ShowBattleEndedOverlay(screen, output);
        screen.SaveRequested += () => CommandTable.TryInvoke("file.save");
        screen.SaveAsRequested += () => CommandTable.TryInvoke("file.save_as");
        ShowOverlay(screen);
    }

    private void ShowBattleEndedOverlay(TacticalBattleScreen battleScreen, SessionOutput output) =>
        AppDiagnostics.Operation(DiagnosticEvent.Result, "result", () => { ShowBattleEndedOverlayCore(battleScreen, output); return true; }, turn: Session.State.Calendar.TurnIndex);

    private void ShowBattleEndedOverlayCore(TacticalBattleScreen battleScreen, SessionOutput output)
    {
        // An AI lap may already have opened another battle in the same submission. Keep its opening
        // across the current result's dismissal and any read-only/save calls that replace LastTacticalTimeline.
        _nextTacticalOpening = output.TacticalTimeline
            .Where(frame => frame.BattleId != battleScreen.DisplayedFrame?.BattleId).ToArray();
        CloseOverlay(battleScreen);
        var report = output.EndedBattleReport!;
        var ended = new BattleEndedScreen
        {
            Model = BattleEndedViewModel.FromReport(report, Session.State, Session.Ruleset),
        };
        ended.Closed += () =>
        {
            CloseOverlay(ended);

            // The engine wrote the battle back in the call that ended it; OK carries on with that call's own
            // windows (the battle results routed to this seat, a fall, a hand-off) and then the peace offer.
            CompleteTacticalCommand(output);
        };
        ShowOverlay(ended);
    }

    private void ShowBattleResultOverlay(Engine.Battle.BattleResult battle) =>
        AppDiagnostics.Operation(DiagnosticEvent.Result, "result", () => { ShowBattleResultOverlayCore(battle); return true; }, turn: Session.State.Calendar.TurnIndex);

    private void ShowBattleResultOverlayCore(Engine.Battle.BattleResult battle)
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
    /// different one.
    /// </summary>
    /// <remarks>
    /// <strong>T116 reordering.</strong> This now runs <em>before</em> a queued battle overlay rather
    /// than after every one of them (<see cref="ShowNextOverlay"/>), so the "pass the device" screen is
    /// what an incoming human seat sees first and a battle the AI phase fought against it is shown once
    /// it continues — never to the seat that just ended its turn. The handoff's own Continue calls
    /// <see cref="ShowNextOverlay"/> again to drain the battles the router released for this seat; the
    /// tracking fields are updated here on every call, so that second pass finds no new handoff. Returns
    /// whether a handoff was shown, so the caller can defer the battles until Continue.
    /// </remarks>
    /// <returns><see langword="true"/> when a handoff screen was opened; otherwise <see langword="false"/>.</returns>
    private bool CheckForHotseatHandoff()
    {
        var info = HotseatHandoffDetector.Detect(
            Session.State, Session.Scenario, _lastKnownActiveNationId, _lastKnownActiveWasHuman);

        var activeNation = Session.State.NationById(Session.State.ActiveNationId);
        _lastKnownActiveNationId = Session.State.ActiveNationId;
        _lastKnownActiveWasHuman = activeNation?.Control == SeatControl.Human;

        if (info is null)
        {
            return false;
        }

        var calendar = Session.State.Calendar;
        var screen = new HotseatHandoffScreen
        {
            Info = info,
            CalendarLine = $"Week {calendar.Week}, {calendar.YearBc} BC (turn {calendar.TurnIndex})",
        };
        screen.Continued += () =>
        {
            CloseOverlay(screen);
            ShowNextOverlay();
        };
        ShowOverlay(screen);
        return true;
    }

    /// <summary>
    /// T25: opens <see cref="Screens.DiplomacyScreen"/> — the Strategy menu's International relations entry and its toolbar button
    /// call this. Public, rather than only reachable through them, so a headless check
    /// can call it directly too (<c>godot/Screens/Checks/**</c>) — the same convention
    /// <see cref="SubmitForCheck"/> already establishes for driving a command.
    /// </summary>
    public void OpenDiplomacyScreen()
    {
        if (!AllowStrategicInput()) return;
        var screen = new DiplomacyScreen { Session = Session };
        screen.CommandIssued += OnCommandIssued;
        screen.Closed += () => CloseOverlay(screen);
        ShowOverlay(screen);
    }

    private void ShowOverlay(Control overlay)
    {
        AppDiagnostics.Detail(DiagnosticEvent.Overlay, overlay.GetType().Name);
        if (ActiveOverlay is { } previous)
        {
            RemoveChild(previous);
            previous.QueueFree();
        }

        ActiveOverlay = overlay;
        overlay.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(overlay);

        // The overlay's own full-rect geometry is correct the moment it is parented, but the containers
        // inside it have not run their deferred layout pass yet: the panel would sit at (0,0) until the
        // next frame. One pass of the sort notification gives every container (and every autowrapping
        // label) its width; a label reports its wrapped height only once it has been given a width, so
        // each one's cached minimum size is then invalidated and a second pass sorts the panel to its real
        // size and centres it. This is the deferred pass Godot runs on the next frame, run now, so a panel
        // is centred on the frame its window opens (T147, bug #781 point 2).
        static IEnumerable<Label> AllLabels(Node node)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is Label label)
                {
                    yield return label;
                }

                foreach (var nested in AllLabels(child))
                {
                    yield return nested;
                }
            }
        }

        overlay.PropagateNotification((int)Container.NotificationSortChildren);
        foreach (var label in AllLabels(overlay))
        {
            label.UpdateMinimumSize();
        }

        overlay.PropagateNotification((int)Container.NotificationSortChildren);
    }

    private void CloseOverlay(Control overlay)
    {
        if (!ReferenceEquals(ActiveOverlay, overlay))
        {
            return;
        }

        AppDiagnostics.Detail(DiagnosticEvent.Overlay, "Close");
        ActiveOverlay = null;
        RemoveChild(overlay);
        overlay.QueueFree();
    }

    private void RefreshTopBar()
    {
        var calendar = DisplayState.Calendar;
        _calendarLabel.Text = $"Week {calendar.Week}, {calendar.YearBc} BC (turn {calendar.TurnIndex})";
        var nation = DisplayState.NationById(DisplayState.ActiveNationId);
        _activeNationLabel.Text = $"Playing: {nation?.Name ?? DisplayState.ActiveNationId}";
    }
}
