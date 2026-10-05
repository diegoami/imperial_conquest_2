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

    private readonly Queue<Engine.Battle.BattleResult> _pendingBattleOverlays = new();

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

        // One pack loader for the main toolbar and T110's Area-map strip, so both draw the same keys and
        // a missing texture falls back the same way in each.
        var assetLoader = AssetPackTextureLoader.TryLoadPack(
            RepositoryRoot,
            SettingsScreen.SelectedPackId,
            onFailure: key => GD.PushWarning(
                $"T100 toolbar: asset pack could not resolve or load '{key}'; falling back to the caption."));

        Toolbar = new CommandToolbar
        {
            Table = CommandTable,
            World = Session.World,
            AssetLoader = assetLoader,
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
            Table = CommandTable,
            AssetLoader = assetLoader,
            SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
        };
        sideColumn.AddChild(_areaMapView.BuildStrip());
        sideColumn.AddChild(_areaMapView);

        _contextPanel = new ContextPanel
        {
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

        _mapView.CitySelected += id =>
        {
            _selectedArmyId = null;
            _selectedFleetId = null;
            _contextPanel.ShowCity(id);
        };
        _mapView.ArmySelected += id =>
        {
            _selectedArmyId = id;
            _selectedFleetId = null;
            _contextPanel.ShowArmy(id);
        };
        _mapView.FleetSelected += id =>
        {
            _selectedArmyId = null;
            _selectedFleetId = id;
            _contextPanel.ShowFleet(id);
        };
        _mapView.SelectionCleared += () =>
        {
            _selectedArmyId = null;
            _selectedFleetId = null;
            _contextPanel.ShowNationOverview();
        };
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
        WireNationSwatches();

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

        // T134: Army -> Supply army opens the SupplyDialog for the selected army (or the army a selected
        // fleet carries), or shows one of the two messages and opens nothing.
        CommandTable.Bind("unit_map.army_supply", OpenSupplyArmyDialog);

        // Help: the new page, the hints toggle, and the about box.
        CommandTable.Bind("help.topics", ShowHelpPage);
        CommandTable.Bind("help.show_hints", ToggleHints);
        CommandTable.Bind("help.about", ShowAbout);

        // T110: the Nations menu and its toolbar swatches set the viewed nation. None of these submits a
        // command (Done-when 7) — only the panel and the highlight scope change.
        foreach (var row in GameCommandTable.Rows.Where(
            r => string.Equals(r.Menu, "Nations", StringComparison.Ordinal)))
        {
            var nationId = string.Equals(row.Id, "nations.all", StringComparison.Ordinal)
                ? null
                : row.Id["nations.".Length..];
            CommandTable.Bind(row.Id, () => SetViewedNation(nationId));
        }

        // T110: the Area map's five Show entries toggle their own highlight layer and its check marks;
        // Find a city opens the TFindCity dialog. The Show mercenaries rows stay unwired (T113), so no
        // handler is bound for them. They highlight and hide nothing.
        CommandTable.Bind("area_map.show_cities", () =>
        {
            _areaMapView.ToggleHighlight(AreaMapHighlightKind.Cities);
            SyncShowChecks();
        });
        CommandTable.Bind("area_map.show_capital", () =>
        {
            _areaMapView.ToggleHighlight(AreaMapHighlightKind.Capital);
            SyncShowChecks();
        });
        CommandTable.Bind("area_map.show_armies", () =>
        {
            _areaMapView.ToggleHighlight(AreaMapHighlightKind.Armies);
            SyncShowChecks();
        });
        CommandTable.Bind("area_map.show_fleets", () =>
        {
            _areaMapView.ToggleHighlight(AreaMapHighlightKind.Fleets);
            SyncShowChecks();
        });
        CommandTable.Bind("area_map.show_all", () =>
        {
            _areaMapView.ToggleShowAll();
            SyncShowChecks();
        });
        CommandTable.Bind("area_map.find_city", () => OpenFindCityDialog());
    }

    /// <summary>
    /// T110: makes <paramref name="nationId"/> the viewed nation (or <see langword="null"/> for All
    /// nations): the context panel shows its status panel, the Area-map highlights re-scope to it, its
    /// Nations menu item is checked and its toolbar swatch is pressed (a radio group). Choosing All
    /// nations shows no status panel.
    /// </summary>
    public void SetViewedNation(string? nationId)
    {
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
        foreach (var row in GameCommandTable.Rows.Where(
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
        foreach (var row in GameCommandTable.Rows.Where(
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
            var row = GameCommandTable.Rows.FirstOrDefault(r =>
                string.Equals(r.Menu, "Nations", StringComparison.Ordinal)
                && string.Equals(r.Caption, popup.GetItemText(i), StringComparison.Ordinal));
            if (row is not null)
            {
                // B3: GameMenuBar adds every item plain (AddItem), so a check mark is never drawn until
                // the item is made checkable. The 17 Nations entries (16 nations + All nations) are a
                // radio group: exactly one is checked, the viewed nation. SetItemAsRadioCheckable resets
                // the checked flag, so it must run before SetItemChecked.
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
            var row = GameCommandTable.Rows.FirstOrDefault(r =>
                string.Equals(r.Menu, "Area map", StringComparison.Ordinal)
                && r.Submenu is null
                && string.Equals(r.Caption, popup.GetItemText(i), StringComparison.Ordinal));
            if (row is null)
            {
                continue;
            }

            // B3: only the five Show entries are check items; Find a city stays a plain item so no
            // check box is drawn beside it. Again the checkable flag resets the mark, so set it first.
            if (IsShowEntry(row.Id))
            {
                popup.SetItemAsCheckable(i, true);
                popup.SetItemChecked(i, IsShowLayerOn(row.Id));
            }
        }
    }

    /// <summary>The five Area map Show entries — the ones that are check items, unlike Find a city.</summary>
    private static bool IsShowEntry(string commandId) =>
        commandId is "area_map.show_cities"
            or "area_map.show_capital"
            or "area_map.show_armies"
            or "area_map.show_fleets"
            or "area_map.show_all";

    private bool IsShowLayerOn(string commandId)
    {
        var active = _areaMapView.ActiveHighlightsForCheck;
        return commandId switch
        {
            "area_map.show_cities" => active.Contains(AreaMapHighlightKind.Cities),
            "area_map.show_capital" => active.Contains(AreaMapHighlightKind.Capital),
            "area_map.show_armies" => active.Contains(AreaMapHighlightKind.Armies),
            "area_map.show_fleets" => active.Contains(AreaMapHighlightKind.Fleets),
            "area_map.show_all" => Enum.GetValues<AreaMapHighlightKind>().All(active.Contains),
            _ => false,
        };
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
        _selectedArmyId = armyId;
        _selectedFleetId = null;
        _contextPanel.ShowArmy(armyId);
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
            Submit = SubmitFromSupplyDialog,
        };
        dialog.Closed += () => CloseOverlay(dialog);
        ShowOverlay(dialog);
    }

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
    /// Submits one line the Supply dialog composed through <see cref="GameSession.Submit"/> and
    /// <see cref="OnCommandIssued"/> — the same path every other control's command takes, so
    /// <see cref="CommandIssued"/> counts it exactly once.
    /// </summary>
    private IReadOnlyList<string> SubmitFromSupplyDialog(string line)
    {
        var lines = Session.Submit(line).Lines;
        OnCommandIssued(lines);
        return lines;
    }

    /// <summary>Shows a screen-level message without issuing a command — see <see cref="LastMessageForCheck"/>.</summary>
    private void ShowScreenMessage(string message)
    {
        _lastCommandLabel.Text = message;
        _lastCommandLabel.TooltipText = message;
        UpdateLastCommandLinesSkipped();
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

        // T110 [designed]: at every turn start the viewed nation is the active seat's (Scope), even when
        // the active seat itself is unchanged — in a one-human game "end" plays every AI seat and comes
        // back to the same seat, so a seat comparison alone missed the turn boundary and a viewed
        // Carthage carried into Rome's next turn. The calendar's turn index is the boundary; the active
        // seat id changing mid-turn (a hotseat pass) is the other. _lastKnownActiveNationId is still the
        // prior turn's until CheckForHotseatHandoff updates it later in this method.
        var activeId = Session.State.ActiveNationId;
        var turnIndex = Session.State.Calendar.TurnIndex;
        var turnStarted = turnIndex != _lastKnownTurnIndex;
        _lastKnownTurnIndex = turnIndex;
        if (turnStarted || !string.Equals(activeId, _lastKnownActiveNationId, StringComparison.Ordinal))
        {
            // Only re-view when it actually differs: an unconditional SetViewedNation on every "end"
            // would clear a Find-a-city highlight chosen this turn for a view that did not change.
            if (!string.Equals(ViewedNationId, activeId, StringComparison.Ordinal))
            {
                SetViewedNation(activeId);
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
        var battlesToShowNow = _battleReportRouter.Route(
            Session.LastBattles, humanSeatIds, Session.State.ActiveNationId);
        foreach (var battle in battlesToShowNow)
        {
            _pendingBattleOverlays.Enqueue(battle);
        }

        // T138: one game-end window per human seat that fell on this call, in order. A fall in the
        // construction-time prelude arrives here on the first command too, the same way the battles do.
        foreach (var fall in Session.LastSeatFalls)
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
            _mapView.ClearSelection();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree()
    {
        SidePanelToggle.Changed -= ApplySidePanelState;
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
        }
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
            fall, nation, Session.Ruleset, Session.State.Calendar.YearBc, Session.IsGameOver);
        var screen = new GameEndScreen { Model = model };
        screen.MainMenuRequested += () => OwningAppRoot?.ShowMainMenu();
        screen.Closed += () =>
        {
            CloseOverlay(screen);
            ShowNextOverlay();
        };
        ShowOverlay(screen);
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
