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
/// Shift+X clears a selection;</item>
/// <item>Done-when 6 — every enabled toolbar button has a non-empty tooltip with hints on, none with
/// Show hints off, and the menu item's check mark follows;</item>
/// <item>Done-when 7 — a disabled menu entry and a disabled toolbar button issue no command.</item>
/// </list>
/// Run headless via:
/// <code>
/// godot --headless --path godot res://Checks/MenuBarCheck.tscn --quit-after 600
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
            // Done-when 2 and Done-when 6 (hints on).
            (InitialSettleFrames, CheckMenuStructure),
            (BetweenStepsFrames, CheckHintsOn),

            // Done-when 7.
            (BetweenStepsFrames, CheckDisabledEntriesIssueNothing),

            // Done-when 5: the four overlays and the news toggle, none of which mutates the turn.
            (BetweenStepsFrames, CheckNewsToggles),
            (BetweenStepsFrames, CheckInternationalRelations),
            (BetweenStepsFrames, CheckHelpTopics),
            (BetweenStepsFrames, CheckAbout),

            // Done-when 5, the key: select an own army, then Shift+X.
            (BetweenStepsFrames, SelectOwnArmy),
            (BetweenStepsFrames, AssertOwnArmySelected),
            (BetweenStepsFrames, PressShiftX),
            (BetweenStepsFrames, AssertShiftXCleared),

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

    // ---- Done-when 7 ----

    private void CheckDisabledEntriesIssueNothing()
    {
        _beforeIssued = _mainGame.CommandTable.IssuedCount;
        _beforeCommands = _commandsSeen;

        var menuPressed = _mainGame.MenuBar.PressItemForCheck("strategy.taxation");
        var toolbarPressed = _mainGame.Toolbar.PressForCheck("strategy.taxation");
        var nationPressed = _mainGame.Toolbar.PressForCheck("nations.rome");

        Check(!menuPressed, "the disabled Taxation menu entry reports it was not pressed");
        Check(!toolbarPressed, "the disabled Taxation toolbar button reports it was not pressed");
        Check(!nationPressed, "the disabled nation toolbar button reports it was not pressed");
        Check(
            _mainGame.CommandTable.IssuedCount == _beforeIssued,
            $"no disabled entry reached the table's handler ({_beforeIssued} -> {_mainGame.CommandTable.IssuedCount})");
        Check(
            _commandsSeen == _beforeCommands,
            $"no disabled entry issued a session command ({_beforeCommands} -> {_commandsSeen})");
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

    private void PressShiftX()
    {
        _beforeCleared = _selectionsCleared;
        _mainGame._UnhandledInput(new InputEventKey
        {
            Keycode = Key.X,
            Pressed = true,
            ShiftPressed = true,
        });
    }

    private void AssertShiftXCleared() =>
        Check(
            _selectionsCleared > _beforeCleared,
            "Shift+X clears the selection (GameMapView.SelectionCleared fired)");

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
