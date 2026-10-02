using Godot;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T100's headless check (docs/tasks/T100.md, Done-when 2–7): it builds the <em>real</em>
/// <see cref="MainGameScreen"/> and drives the real menu bar and toolbar controls, then asserts:
/// <list type="bullet">
/// <item>Done-when 2 — the seven menus in the inventory's order, with the Army, Fleet, City and Show
/// mercenaries submenus;</item>
/// <item>Done-when 3 — the End turn menu item and the End turn toolbar button each advance the
/// calendar by one turn and each increment the table's one handler counter;</item>
/// <item>Done-when 5 — International relations opens <see cref="DiplomacyScreen"/>, News toggles the
/// news log, Help topics opens <see cref="HelpPage"/>, About opens <see cref="AboutDialog"/>, and
/// Shift+X clears a selection exactly once with no overlay while an open HelpPage or File → New
/// confirmation blocks it;</item>
/// <item>Done-when 6 — every enabled toolbar button has a non-empty tooltip with hints on, none with
/// Show hints off, and the menu item's check mark follows;</item>
/// <item>Done-when 7 — every disabled menu entry and toolbar button issues no command, the toolbar
/// sweep driven from the table and pressed through real viewport-local GUI input, with an enabled
/// End turn click as the positive control that proves the clicks land (rework N-a);</item>
/// </list>
/// Run headless via:
/// <code>
/// godot --headless --path godot res://Checks/MenuBarCheck.tscn --quit-after 900
/// </code>
/// </summary>
/// <remarks>
/// Same conventions as <c>godot/Checks/MapClickCheck.cs</c>: the real control tree with real
/// <see cref="Button.Pressed"/> / <see cref="PopupMenu.IdPressed"/> signals and real
/// <see cref="InputEventKey"/> events, never a hand-built mirror of the UI; the ordered plan waits
/// settle frames between interactions so a Godot layout pass follows every change. The session is the
/// shipped classical pair with Rome as the human seat, the same fixture <c>MapClickCheck</c> uses.
/// The Save file this check writes carries its own fixed suffix
/// (<see cref="MainGameScreen.CheckSaveSuffix"/>) and is deleted on exit.
/// </remarks>
public partial class MenuBarCheck : Control
{
    private const int InitialSettleFrames = 6;
    private const int BetweenStepsFrames = 3;

    private MainGameScreen _mainGame = null!;

    private bool _ok = true;
    private int _frame;
    private int _planIndex;

    private readonly List<(int WaitFrames, Action Run)> _plan = new();

    private int _commandsSeen;
    private int _selectionsCleared;
    private int _armiesSelected;

    /// <summary>The unwired table rows, with a toolbar button, that the real-input sweep presses
    /// (built in <see cref="CheckDisabledEntriesIssueNothing"/>, pressed in the next step).</summary>
    private IReadOnlyList<GameCommandRow> _sweptToolbarRows = Array.Empty<GameCommandRow>();

    private int _beforeIssued;
    private int _beforeCommands;
    private int _beforeTurn;
    private int _beforeCleared;
    private int _beforeArmies;

    private string? _savedPathToCleanUp;

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;

        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");
        var session = new GameSession(
            resolved.World,
            resolved.Ruleset,
            resolved.Scenario,
            seedOverride: 1,
            humanSeatNationId: "rome");

        _mainGame = new MainGameScreen
        {
            Session = session,
            RepositoryRoot = GameDataContext.RepositoryRoot,
        };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);

        _mainGame.CommandIssued += _ => _commandsSeen++;
        _mainGame.MapView.SelectionCleared += () => _selectionsCleared++;
        _mainGame.MapView.ArmySelected += _ => _armiesSelected++;
        _mainGame.CheckSaveSuffix = "t100-menubarcheck";

        BuildPlan();
    }

    public override void _Process(double delta)
    {
        _frame++;

        try
        {
            if (_planIndex >= _plan.Count)
            {
                return;
            }

            var (waitFrames, run) = _plan[_planIndex];
            if (_frame < waitFrames)
            {
                return;
            }

            run();
            _planIndex++;
            _frame = 0;

            if (_planIndex >= _plan.Count)
            {
                Finish();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MenuBarCheck: unhandled exception: {ex}");
            Finish();
        }
    }

    public override void _ExitTree() => CleanUpSavedFile();

    private void BuildPlan()
    {
        _plan.AddRange(new (int WaitFrames, Action Run)[]
        {
            // Done-when 2 and Done-when 6 (hints on), plus rework B5's fit and icon-only assertions.
            (InitialSettleFrames, CheckMenuStructure),
            (BetweenStepsFrames, CheckHintsOn),
            (BetweenStepsFrames, CheckToolbarFitsAndIsIconOnly),

            // Done-when 7. The toolbar half presses the real buttons through GUI input before the
            // counts are asserted (rework B3); the enabled End turn click is the positive control that
            // proves those real-input clicks land (rework N-a).
            (BetweenStepsFrames, CheckDisabledEntriesIssueNothing),
            (BetweenStepsFrames, PressDisabledToolbarButtonsThroughRealInput),
            (BetweenStepsFrames, AssertDisabledToolbarPressesIssuedNothing),
            (BetweenStepsFrames, ClickEnabledEndTurnToolbarThroughRealInput),
            (BetweenStepsFrames, AssertEnabledEndTurnToolbarAdvanced),

            // Done-when 5: the four overlays and the news toggle, none of which mutates the turn.
            (BetweenStepsFrames, CheckNewsToggles),
            (BetweenStepsFrames, CheckInternationalRelations),
            (BetweenStepsFrames, CheckHelpTopics),
            (BetweenStepsFrames, CheckAbout),

            // Done-when 5, the key: select an own army, prove the overlay guard blocks Shift+X with
            // HelpPage and with File -> New's ConfirmPrompt open, then clear exactly once with no
            // overlay (rework B4).
            (BetweenStepsFrames, SelectOwnArmy),
            (BetweenStepsFrames, AssertOwnArmySelected),
            (BetweenStepsFrames, OpenHelpPageForShortcutGuard),
            (BetweenStepsFrames, AssertShiftXBlockedByHelpPage),
            (BetweenStepsFrames, OpenNewConfirmForShortcutGuard),
            (BetweenStepsFrames, AssertShiftXBlockedByConfirmPrompt),
            (BetweenStepsFrames, PressShiftXRealInput),
            (BetweenStepsFrames, AssertShiftXClearedExactlyOnce),

            // Done-when 3: the menu item, then the toolbar button, through the one handler.
            (BetweenStepsFrames, PressEndTurnMenu),
            (BetweenStepsFrames, AssertEndTurnMenuAdvanced),
            (BetweenStepsFrames, PressEndTurnToolbar),
            (BetweenStepsFrames, AssertEndTurnToolbarAdvanced),

            // Done-when 4.
            (BetweenStepsFrames, PressSaveMenu),
            (BetweenStepsFrames, AssertSaveWritten),

            // Done-when 6 (hints off, and the check mark).
            (BetweenStepsFrames, ToggleHintsOff),
            (BetweenStepsFrames, AssertHintsOff),
        });
    }

    // ---- Done-when 2 ----

    private void CheckMenuStructure()
    {
        var expected = new[] { "File", "Game", "Strategy", "Nations", "Area map", "Unit map", "Help" };
        Check(
            _mainGame.MenuBar.MenuTitles.SequenceEqual(expected),
            $"the menu bar's seven menus come in the inventory's order (got {string.Join(", ", _mainGame.MenuBar.MenuTitles)})");

        foreach (var menu in expected)
        {
            Check(_mainGame.MenuBar.MenuForCheck(menu) is not null, $"the '{menu}' menu exists");
        }

        CheckSubmenu("Unit map", "Army");
        CheckSubmenu("Unit map", "Fleet");
        CheckSubmenu("Unit map", "City");
        CheckSubmenu("Area map", "Show mercenaries");

        CheckCancelSelectionShortcut();
    }

    /// <summary>
    /// Rework B4: the Cancel selection row's <see cref="GameCommandRow.Shortcut"/> is <em>shown</em> in
    /// the item's tooltip but registers no live Godot accelerator — an accelerator acts in Godot's
    /// shortcut-input pass, before <c>MainGameScreen._UnhandledInput</c>'s overlay guard, and so would
    /// fire through a modal. The item still lives in the Unit map menu.
    /// </summary>
    private void CheckCancelSelectionShortcut()
    {
        var unitMap = _mainGame.MenuBar.MenuForCheck("Unit map");
        var index = unitMap is null ? -1 : FindItemIndex(unitMap, "Cancel selection");
        Check(index >= 0, "the Unit map menu has a Cancel selection entry");
        if (index < 0)
        {
            return;
        }

        var accelerator = (long)unitMap!.GetItemAccelerator(index);
        Check(
            accelerator == 0,
            $"Cancel selection registers no live accelerator (got 0x{accelerator:X})");

        var tooltip = unitMap.GetItemTooltip(index);
        Check(
            tooltip.Contains("Shift+X", StringComparison.Ordinal),
            $"Cancel selection shows Shift+X in its tooltip (got '{tooltip}')");
    }

    private void CheckSubmenu(string menu, string submenu)
    {
        var popup = _mainGame.MenuBar.SubmenuForCheck(menu, submenu);
        Check(popup is not null, $"the {menu} → {submenu} submenu exists");
        Check(
            popup is not null && popup.ItemCount > 0,
            $"the {menu} → {submenu} submenu has entries ({(popup?.ItemCount ?? 0)})");
    }

    // ---- Done-when 6, hints on ----

    private void CheckHintsOn()
    {
        var enabled = EnabledToolbarButtons();
        Check(enabled.Count > 0, "the toolbar has at least one enabled button");
        Check(
            enabled.All(button => !string.IsNullOrEmpty(button.TooltipText)),
            "every enabled toolbar button has a non-empty tooltip with hints on");

        Check(_mainGame.MenuBar.HintsEnabled, "the menu bar starts with hints on");

        var helpPopup = _mainGame.MenuBar.MenuForCheck("Help");
        var showHints = helpPopup is null ? -1 : FindItemIndex(helpPopup, "Show hints");
        Check(
            showHints >= 0 && helpPopup!.IsItemChecked(showHints),
            "the Show hints menu item is checked while hints are on");
    }

    // ---- Rework B5: the toolbar fits the design viewport and is icon-only ----

    /// <summary>
    /// Rework B5: every command button draws its icon and keeps the caption in the tooltip rather than
    /// on the button, so the toolbar's minimum width stays inside the 1500&#160;px design viewport and
    /// cannot push the context panel (and the last nation swatches) off-screen. A missing texture is the
    /// one allowed fallback, and then the caption is the button's text — never both.
    /// </summary>
    private void CheckToolbarFitsAndIsIconOnly()
    {
        var viewportWidth = GetViewport().GetVisibleRect().Size.X;
        var toolbarRight = _mainGame.Toolbar.GetGlobalRect().End.X;
        Check(
            toolbarRight <= 1500.5f,
            $"the toolbar's right edge {toolbarRight} fits the 1500 px design viewport "
            + $"(viewport is {viewportWidth} px)");

        var iconRows = GameCommandTable.Rows
            .Where(row => row.IconKey is not null && _mainGame.Toolbar.ButtonFor(row.Id) is not null)
            .ToList();
        Check(iconRows.Count == 9, $"the toolbar has 9 icon command buttons ({iconRows.Count})");

        var badStates = new List<string>();
        foreach (var row in iconRows)
        {
            var button = _mainGame.Toolbar.ButtonFor(row.Id)!;
            if (button.Icon is null || !string.IsNullOrEmpty(button.Text))
            {
                badStates.Add($"{row.Id}(icon={(button.Icon is null ? "null" : "set")}, text='{button.Text}')");
            }
            else if (string.IsNullOrEmpty(button.TooltipText))
            {
                badStates.Add($"{row.Id}(empty tooltip)");
            }
        }

        Check(
            badStates.Count == 0,
            "every icon command button has its icon set, empty text and the caption as tooltip "
            + $"(bad: {(badStates.Count == 0 ? "<none>" : string.Join(", ", badStates))})");
    }

    // ---- Done-when 7 ----

    private void CheckDisabledEntriesIssueNothing()
    {
        _beforeIssued = _mainGame.CommandTable.IssuedCount;
        _beforeCommands = _commandsSeen;

        // The menu half drives from the table: every unwired row, not a sample. Every press goes
        // through the same disabled guard a real click does (PressItemForCheck refuses to emit when
        // the item is disabled).
        var disabledMenuRows = GameCommandTable.Rows.Where(row => !row.Wired).ToList();
        Check(
            disabledMenuRows.Count > 0,
            $"the table has disabled menu entries to sweep ({disabledMenuRows.Count})");

        var menuPresses = disabledMenuRows
            .Where(row => _mainGame.MenuBar.PressItemForCheck(row.Id))
            .Select(row => row.Id)
            .ToList();
        Check(
            menuPresses.Count == 0,
            $"every one of the {disabledMenuRows.Count} disabled menu entries refuses the press "
            + $"(pressed: {(menuPresses.Count == 0 ? "<none>" : string.Join(", ", menuPresses))})");

        // Rework B3: the toolbar half drives from the table too — every unwired row that has a toolbar
        // button, plus the 17 nation swatches (which the table also holds) — never from each button's
        // own Disabled flag, because a wrongly enabled button would then drop out of its own sweep.
        var nationRows = GameCommandTable.Rows
            .Where(row => string.Equals(row.Menu, "Nations", StringComparison.Ordinal))
            .ToList();
        Check(nationRows.Count == 17, $"the table has 17 nation rows ({nationRows.Count})");

        _sweptToolbarRows = GameCommandTable.Rows
            .Where(row => !row.Wired && _mainGame.Toolbar.ButtonFor(row.Id) is not null)
            .ToList();
        Check(
            _sweptToolbarRows.Count == 21,
            $"every unwired toolbar command is swept (got {_sweptToolbarRows.Count})");

        var unexpectedlyEnabled = _sweptToolbarRows
            .Where(row => _mainGame.Toolbar.ButtonFor(row.Id)!.Disabled == false)
            .Select(row => row.Id)
            .ToList();
        Check(
            unexpectedlyEnabled.Count == 0,
            $"every one of the {_sweptToolbarRows.Count} swept toolbar buttons is Disabled "
            + $"(enabled: {(unexpectedlyEnabled.Count == 0 ? "<none>" : string.Join(", ", unexpectedlyEnabled))})");
    }

    /// <summary>
    /// Rework B3: presses every swept toolbar button through Godot's real GUI input path (a mouse
    /// press/release at the button's centre, <see cref="Viewport.PushInput"/> in viewport-local
    /// coordinates, rework N-a), not through a helper that skips the input system. The next step asserts
    /// the handler and session counts did not move; the enabled End turn positive control in the same
    /// helper path proves these clicks actually land.
    /// </summary>
    private void PressDisabledToolbarButtonsThroughRealInput()
    {
        _beforeIssued = _mainGame.CommandTable.IssuedCount;
        _beforeCommands = _commandsSeen;
        foreach (var row in _sweptToolbarRows)
        {
            ClickControl(_mainGame.Toolbar.ButtonFor(row.Id)!);
        }
    }

    private void AssertDisabledToolbarPressesIssuedNothing()
    {
        Check(
            _mainGame.CommandTable.IssuedCount == _beforeIssued,
            $"no disabled toolbar button reached the table's handler "
            + $"({_beforeIssued} -> {_mainGame.CommandTable.IssuedCount})");
        Check(
            _commandsSeen == _beforeCommands,
            $"no disabled toolbar button issued a session command ({_beforeCommands} -> {_commandsSeen})");
    }

    /// <summary>
    /// Rework N-a: the positive control for <see cref="ClickControl"/>. A real viewport-local click on
    /// the enabled End turn toolbar button must advance the turn and the table's handler counter by one,
    /// so the sweep's "nothing happened" assertions are not vacuous (before rework N-a the default window
    /// coordinates meant even an enabled button's click landed nowhere).
    /// </summary>
    private void ClickEnabledEndTurnToolbarThroughRealInput()
    {
        _beforeTurn = _mainGame.Session.State.Calendar.TurnIndex;
        _beforeIssued = _mainGame.CommandTable.IssuedCount;
        ClickControl(_mainGame.Toolbar.ButtonFor("game.end_turn")!);
    }

    private void AssertEnabledEndTurnToolbarAdvanced()
    {
        Check(
            _mainGame.Session.State.Calendar.TurnIndex == _beforeTurn + 1,
            "a real-input click on the enabled End turn toolbar button advances the calendar by one turn "
            + $"({_beforeTurn} -> {_mainGame.Session.State.Calendar.TurnIndex})");
        Check(
            _mainGame.CommandTable.IssuedCount == _beforeIssued + 1,
            "a real-input click on the enabled End turn toolbar button increments the table's handler "
            + $"counter ({_beforeIssued} -> {_mainGame.CommandTable.IssuedCount})");
    }

    // ---- Done-when 5 ----

    private void CheckNewsToggles()
    {
        var wasVisible = _mainGame.NewsLog.Visible;
        _mainGame.MenuBar.PressItemForCheck("strategy.news");
        Check(
            _mainGame.NewsLog.Visible != wasVisible,
            $"the News menu item toggles the news log (was {wasVisible}, now {_mainGame.NewsLog.Visible})");

        _mainGame.Toolbar.PressForCheck("strategy.news");
        Check(
            _mainGame.NewsLog.Visible == wasVisible,
            $"the News toolbar button toggles it back (now {_mainGame.NewsLog.Visible})");
    }

    private void CheckInternationalRelations()
    {
        _mainGame.MenuBar.PressItemForCheck("strategy.relations");
        Check(
            _mainGame.ActiveOverlay is DiplomacyScreen,
            $"International relations opens DiplomacyScreen (got {_mainGame.ActiveOverlay?.GetType().Name ?? "null"})");
        CloseOverlay<DiplomacyScreen>();
    }

    private void CheckHelpTopics()
    {
        _mainGame.MenuBar.PressItemForCheck("help.topics");
        Check(
            _mainGame.ActiveOverlay is HelpPage,
            $"Help topics opens the help page (got {_mainGame.ActiveOverlay?.GetType().Name ?? "null"})");
        CloseOverlay<HelpPage>();
    }

    private void CheckAbout()
    {
        _mainGame.MenuBar.PressItemForCheck("help.about");
        Check(
            _mainGame.ActiveOverlay is AboutDialog,
            $"About opens the about box (got {_mainGame.ActiveOverlay?.GetType().Name ?? "null"})");
        CloseOverlay<AboutDialog>();
    }

    private void CloseOverlay<T>()
        where T : Control
    {
        if (_mainGame.ActiveOverlay is not T overlay)
        {
            return;
        }

        if (overlay is DiplomacyScreen diplomacy)
        {
            diplomacy.Close();
        }
        else if (overlay is HelpPage help)
        {
            help.Close();
        }
        else if (overlay is AboutDialog about)
        {
            about.Close();
        }

        Check(_mainGame.ActiveOverlay is null, $"{typeof(T).Name} closes cleanly");
    }

    private void SelectOwnArmy()
    {
        _beforeArmies = _armiesSelected;
        _beforeCleared = _selectionsCleared;
        LeftClick(100, 37);
    }

    private void AssertOwnArmySelected() =>
        Check(_armiesSelected > _beforeArmies, "a left click on Rome's own army selects it");

    /// <summary>
    /// Rework B4: Shift+X with a modal overlay up must clear nothing and reach no handler. Every one of
    /// the four presses below goes through <see cref="Viewport.PushInput"/> with a real
    /// <see cref="InputEventKey"/>, never a direct <c>_UnhandledInput</c> call, so a live accelerator or
    /// Godot's shortcut-input pass would be seen if one still existed.
    /// </summary>
    private void OpenHelpPageForShortcutGuard()
    {
        _mainGame.MenuBar.PressItemForCheck("help.topics");
        Check(
            _mainGame.ActiveOverlay is HelpPage,
            $"the help page is open before the guarded Shift+X press "
            + $"(got {_mainGame.ActiveOverlay?.GetType().Name ?? "null"})");
    }

    private void AssertShiftXBlockedByHelpPage()
    {
        _beforeCleared = _selectionsCleared;
        _beforeIssued = _mainGame.CommandTable.IssuedCount;
        PushShiftX();
        Check(
            _mainGame.CommandTable.IssuedCount == _beforeIssued,
            "Shift+X with the help page open does not reach the table's handler "
            + $"({_beforeIssued} -> {_mainGame.CommandTable.IssuedCount})");
        Check(
            _selectionsCleared == _beforeCleared,
            "Shift+X with the help page open does not clear the selection "
            + $"({_beforeCleared} -> {_selectionsCleared})");
        CloseOverlay<HelpPage>();
    }

    private void OpenNewConfirmForShortcutGuard()
    {
        _mainGame.MenuBar.PressItemForCheck("file.new");
        Check(
            _mainGame.ActiveOverlay is ConfirmPrompt,
            $"File -> New's confirmation is open before the guarded Shift+X press "
            + $"(got {_mainGame.ActiveOverlay?.GetType().Name ?? "null"})");
    }

    private void AssertShiftXBlockedByConfirmPrompt()
    {
        _beforeCleared = _selectionsCleared;
        _beforeIssued = _mainGame.CommandTable.IssuedCount;
        PushShiftX();
        Check(
            _mainGame.CommandTable.IssuedCount == _beforeIssued,
            "Shift+X with the New confirmation open does not reach the table's handler "
            + $"({_beforeIssued} -> {_mainGame.CommandTable.IssuedCount})");
        Check(
            _selectionsCleared == _beforeCleared,
            "Shift+X with the New confirmation open does not clear the selection "
            + $"({_beforeCleared} -> {_selectionsCleared})");
        CloseConfirmPromptForCheck();
    }

    private void PressShiftXRealInput()
    {
        _beforeCleared = _selectionsCleared;
        _beforeIssued = _mainGame.CommandTable.IssuedCount;
        PushShiftX();
    }

    private void AssertShiftXClearedExactlyOnce()
    {
        Check(
            _selectionsCleared == _beforeCleared + 1,
            "Shift+X with no overlay clears the selection exactly once "
            + $"({_beforeCleared} -> {_selectionsCleared})");
        Check(
            _mainGame.CommandTable.IssuedCount == _beforeIssued + 1,
            "Shift+X with no overlay runs the table's Cancel selection handler exactly once "
            + $"({_beforeIssued} -> {_mainGame.CommandTable.IssuedCount})");
    }

    private void PushShiftX() => GetViewport().PushInput(new InputEventKey
    {
        Keycode = Key.X,
        Pressed = true,
        ShiftPressed = true,
    });

    private void CloseConfirmPromptForCheck()
    {
        // Bring the help page up: MainGameScreen.ShowOverlay removes the confirmation through its own
        // path, not the prompt's Escape handler — that handler frees the prompt mid-call and then
        // dereferences its now-null viewport (ConfirmPrompt._UnhandledInput:97, an outside-PR defect).
        // The help page then closes through its public Close().
        _mainGame.MenuBar.PressItemForCheck("help.topics");
        Check(_mainGame.ActiveOverlay is HelpPage, "the confirmation is replaced by the help page");
        CloseOverlay<HelpPage>();
    }

    // ---- Done-when 3 ----

    private void PressEndTurnMenu()
    {
        _beforeTurn = _mainGame.Session.State.Calendar.TurnIndex;
        _beforeIssued = _mainGame.CommandTable.IssuedCount;
        _mainGame.MenuBar.PressItemForCheck("game.end_turn");
    }

    private void AssertEndTurnMenuAdvanced()
    {
        Check(
            _mainGame.Session.State.Calendar.TurnIndex == _beforeTurn + 1,
            $"the End turn menu item advances the calendar by one turn "
            + $"({_beforeTurn} -> {_mainGame.Session.State.Calendar.TurnIndex})");
        Check(
            _mainGame.CommandTable.IssuedCount == _beforeIssued + 1,
            $"the End turn menu item increments the table's handler counter "
            + $"({_beforeIssued} -> {_mainGame.CommandTable.IssuedCount})");
    }

    private void PressEndTurnToolbar()
    {
        _beforeTurn = _mainGame.Session.State.Calendar.TurnIndex;
        _beforeIssued = _mainGame.CommandTable.IssuedCount;
        _mainGame.Toolbar.PressForCheck("game.end_turn");
    }

    private void AssertEndTurnToolbarAdvanced()
    {
        Check(
            _mainGame.Session.State.Calendar.TurnIndex == _beforeTurn + 1,
            $"the End turn toolbar button advances the calendar by one turn "
            + $"({_beforeTurn} -> {_mainGame.Session.State.Calendar.TurnIndex})");
        Check(
            _mainGame.CommandTable.IssuedCount == _beforeIssued + 1,
            $"the End turn toolbar button increments the table's handler counter "
            + $"({_beforeIssued} -> {_mainGame.CommandTable.IssuedCount})");
    }

    // ---- Done-when 4 ----

    private void PressSaveMenu()
    {
        _mainGame.MenuBar.PressItemForCheck("file.save");
        _savedPathToCleanUp = _mainGame.LastSavedPath;
    }

    private void AssertSaveWritten()
    {
        var path = _savedPathToCleanUp;
        Check(path is not null && File.Exists(path), $"the Save menu item wrote a file (path: {path ?? "<none>"})");
        if (path is null)
        {
            return;
        }

        var savesDirectory = ProjectSettings.GlobalizePath("user://saves");
        Check(
            path.StartsWith(savesDirectory, StringComparison.OrdinalIgnoreCase),
            $"the save is under user://saves ('{savesDirectory}')");
    }

    // ---- Done-when 6, hints off ----

    private void ToggleHintsOff() => _mainGame.MenuBar.PressItemForCheck("help.show_hints");

    private void AssertHintsOff()
    {
        Check(!_mainGame.MenuBar.HintsEnabled, "Show hints turned hints off");

        var enabled = EnabledToolbarButtons();
        Check(
            enabled.All(button => string.IsNullOrEmpty(button.TooltipText)),
            "no enabled toolbar button has a tooltip with hints off");

        var helpPopup = _mainGame.MenuBar.MenuForCheck("Help");
        var showHints = helpPopup is null ? -1 : FindItemIndex(helpPopup, "Show hints");
        Check(
            showHints >= 0 && !helpPopup!.IsItemChecked(showHints),
            "the Show hints menu item is unchecked while hints are off");
    }

    // ---- helpers ----

    private List<Button> EnabledToolbarButtons() =>
        _mainGame.Toolbar.Buttons.Where(button => !button.Disabled).ToList();

    private static int FindItemIndex(PopupMenu popup, string text)
    {
        for (var i = 0; i < popup.ItemCount; i++)
        {
            if (string.Equals(popup.GetItemText(i), text, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private void LeftClick(int x, int y)
    {
        var position = _mainGame.MapView.TileCenterForCheck(x, y);
        _mainGame.MapView._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = position,
        });
        _mainGame.MapView._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = position,
        });
    }

    /// <summary>
    /// Clicks <paramref name="control"/> the way a user does — a real mouse press and release at its
    /// centre through <see cref="Viewport.PushInput"/> — so a control that wrongly became enabled would
    /// issue its command (rework B3). The events are pushed with <c>in_local_coords: true</c> (rework
    /// N-a): the default window coordinates do not map to the control under the headless 0&#215;0 window,
    /// so the click would otherwise land nowhere. <see cref="ClickEnabledEndTurnToolbarThroughRealInput"/>
    /// is the positive control that proves this helper's clicks actually register.
    /// </summary>
    private void ClickControl(Control control)
    {
        var center = control.GlobalPosition + (control.Size / 2f);
        var viewport = GetViewport();
        viewport.PushInput(new InputEventMouseMotion { Position = center, GlobalPosition = center }, true);
        viewport.PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = center,
            GlobalPosition = center,
            ButtonMask = MouseButtonMask.Left,
        }, true);
        viewport.PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = center,
            GlobalPosition = center,
        }, true);
    }

    private void Finish()
    {
        CleanUpSavedFile();
        var exitCode = _ok ? 0 : 1;
        GD.Print($"MenuBarCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }

    private void CleanUpSavedFile()
    {
        if (_savedPathToCleanUp is null)
        {
            return;
        }

        try
        {
            if (File.Exists(_savedPathToCleanUp))
            {
                File.Delete(_savedPathToCleanUp);
            }

            var directory = Path.GetDirectoryName(_savedPathToCleanUp);
            if (directory is not null && Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length == 0)
            {
                Directory.Delete(directory);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"MenuBarCheck: could not clean up '{_savedPathToCleanUp}': {ex.Message}");
        }
    }

    private bool Check(bool condition, string what)
    {
        if (condition)
        {
            GD.Print($"PASS: {what}");
            return true;
        }

        GD.PrintErr($"FAIL: {what}");
        _ok = false;
        return false;
    }
}
