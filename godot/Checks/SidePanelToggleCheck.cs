using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T132: the right-hand info column hides and shows again. Builds the real <see cref="MainGameScreen"/>
/// on a new classical-mediterranean game at the 1500x850 viewport, clicks the toggle button and presses
/// F12 through <see cref="Viewport.PushInput(InputEvent, bool)"/>, and asserts the map's width, zoom and
/// pan, the selection, the command seams and the run-long memory. Run headless via:
/// <code>
/// godot --headless --path godot res://Checks/SidePanelToggleCheck.tscn --quit-after 600
/// </code>
/// </summary>
public partial class SidePanelToggleCheck : Control
{
    private const int SettleFrames = 6;

    private MainGameScreen _mainGame = null!;
    private MainGameScreen? _second;
    private readonly List<Action> _steps = new();
    private int _stepIndex;
    private int _frame;
    private bool _ok = true;

    private int _commandsSeen;
    private object _stateAtStart = null!;
    private int _issuedAtStart;

    private float _mapWidth0;
    private float _panelWidth0;
    private float _zoom0;
    private Vector2 _pan0;
    private int _armySelections;
    private int _selectionsCleared;
    private string? _armyId;
    private string? _viewed0;
    private string _panelText = string.Empty;
    private MainGameScreen _clickScreen = null!;
    private bool _shownBeforeOverlay;

    public override void _Ready()
    {
        new SidePanelToggle().Set(true);
        Size = GetViewport().GetVisibleRect().Size;

        _mainGame = Build();
        _clickScreen = _mainGame;
        _stateAtStart = _mainGame.Session.State;
        _mainGame.CommandIssued += _ => _commandsSeen++;
        _mainGame.MapView.CommandIssued += _ => _commandsSeen++;
        _mainGame.ContextPanel.CommandIssued += _ => _commandsSeen++;
        _mainGame.MapView.ArmySelected += _ => _armySelections++;
        _mainGame.MapView.SelectionCleared += () => _selectionsCleared++;

        _steps.Add(CheckStart);
        _steps.Add(ClickButton);
        _steps.Add(CheckHidden);
        _steps.Add(PressF12);
        _steps.Add(CheckShownAgain);
        _steps.Add(SelectArmy);
        _steps.Add(CheckArmySelected);
        _steps.Add(ClickButton);
        _steps.Add(CheckSelectionKeptWhileHidden);
        _steps.Add(PressF12);
        _steps.Add(CheckSelectionKeptWhenShown);
        _steps.Add(ClickButton);
        _steps.Add(SelectOtherArmyWhileHidden);
        _steps.Add(CheckSelectedWhileHidden);
        _steps.Add(PressF12);
        _steps.Add(CheckSelectedWhileHiddenShowsOnShow);
        _steps.Add(OpenOverlay);
        _steps.Add(PressF12);
        _steps.Add(CheckOverlayBlockedF12);
        _steps.Add(CloseOverlay);
        _steps.Add(ClickButton);
        _steps.Add(BuildSecond);
        _steps.Add(CheckSecondHidden);
        _steps.Add(ClickSecondButton);
        _steps.Add(CheckBothShown);
        _steps.Add(PressF12);
        _steps.Add(CheckBothHidden);
        _steps.Add(FinalNoCommands);
        _steps.Add(Finish);
    }

    private MainGameScreen Build()
    {
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");
        var session = new GameSession(
            resolved.World, resolved.Ruleset, resolved.Scenario, seedOverride: 1, humanSeatNationId: "rome");
        var screen = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
        screen.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(screen);
        return screen;
    }

    public override void _Process(double delta)
    {
        _frame++;
        if (_frame < SettleFrames || _stepIndex >= _steps.Count)
        {
            return;
        }

        _frame = 0;
        try
        {
            _steps[_stepIndex++]();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"SidePanelToggleCheck: unhandled exception: {ex}");
            GetTree().Quit(1);
        }
    }

    private void CheckStart()
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        _issuedAtStart = _mainGame.CommandTable.IssuedCount;
        Check(_mainGame.SideColumn.Visible, "at the start the column is visible");
        Check(_mainGame.ContextPanel.Size.X >= 340f, $"the context panel is at least 340px wide (got {_mainGame.ContextPanel.Size.X})");
        Check(Inside(_mainGame.ContextPanel.GetGlobalRect(), viewport), "the context panel is inside the viewport");
        Check(_mainGame.SidePanelButton.Text == "»", $"the button reads » (got '{_mainGame.SidePanelButton.Text}')");
        _viewed0 = _mainGame.ContextPanel.StatusNationIdForCheck;
        Check(_viewed0 is not null, $"a nation is viewed at the start ({_viewed0})");
        _mapWidth0 = _mainGame.MapView.Size.X;
        _panelWidth0 = _mainGame.ContextPanel.Size.X;
        _zoom0 = _mainGame.MapView.ZoomFactor;
        _pan0 = _mainGame.MapView.VisibleTileRect.Position;
    }

    private void ClickButton()
    {
        var centre = _clickScreen.SidePanelButton.GetGlobalRect().GetCenter();
        var viewport = GetViewport();
        viewport.PushInput(new InputEventMouseMotion { Position = centre, GlobalPosition = centre }, true);
        viewport.PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = centre,
            GlobalPosition = centre,
            ButtonMask = MouseButtonMask.Left,
        }, true);
        viewport.PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = centre,
            GlobalPosition = centre,
        }, true);
    }

    private void PressF12()
    {
        GetViewport().PushInput(new InputEventKey { Keycode = Key.F12, PhysicalKeycode = Key.F12, Pressed = true }, true);
    }

    private void CheckHidden()
    {
        var viewport = GetViewport().GetVisibleRect().Size;
        Check(!_mainGame.SideColumn.Visible, "a real click on the button hides the column");
        Check(_mainGame.MapView.Size.X >= _mapWidth0 + 340f, $"the map grew by at least 340px ({_mapWidth0} -> {_mainGame.MapView.Size.X})");
        Check(_mainGame.SidePanelButton.Text == "«", $"the button reads « (got '{_mainGame.SidePanelButton.Text}')");
        Check(Inside(_mainGame.SidePanelButton.GetGlobalRect(), viewport), "the button stays inside the viewport");
        Check(
            Mathf.IsEqualApprox(_mainGame.MapView.ZoomFactor, _zoom0)
                && _mainGame.MapView.VisibleTileRect.Position.IsEqualApprox(_pan0),
            $"the map's zoom and pan are unchanged (zoom {_zoom0} -> {_mainGame.MapView.ZoomFactor}, "
            + $"pan {_pan0} -> {_mainGame.MapView.VisibleTileRect.Position})");
        CheckNoCommands("after the hide");
    }

    private void CheckShownAgain()
    {
        Check(_mainGame.SideColumn.Visible, "a real F12 key event shows the column again");
        Check(_mainGame.MapView.Size.X == _mapWidth0, $"the map's width equals the start's ({_mapWidth0} vs {_mainGame.MapView.Size.X})");
        Check(_mainGame.ContextPanel.Size.X == _panelWidth0, $"the panel's width equals the start's ({_panelWidth0} vs {_mainGame.ContextPanel.Size.X})");
        Check(_mainGame.SidePanelButton.Text == "»", "the button reads » again");
        CheckNoCommands("after the show");
    }

    private void SelectArmy()
    {
        var armies = _mainGame.Session.State.Armies;
        var army = armies.First(a =>
            a.Nation == "rome" && a.AboardFleetId is null
            && ReferenceEquals(armies.First(o => o.X == a.X && o.Y == a.Y && o.AboardFleetId is null), a));
        _armyId = army.Id;
        ClickTile(army.X, army.Y);
    }

    private void ClickTile(int x, int y)
    {
        var position = _mainGame.MapView.TileCenterForCheck(x, y);
        _mainGame.MapView._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = position });
        _mainGame.MapView._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = position });
    }

    private string PanelText() =>
        string.Join("|", AllLabels(_mainGame.ContextPanel).Select(l => l.Text));

    private static IEnumerable<Label> AllLabels(Node root)
    {
        foreach (var child in root.GetChildren())
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

    private void SelectOtherArmyWhileHidden()
    {
        Check(!_mainGame.SideColumn.Visible, "the column is hidden before selecting while hidden");
        _panelText = PanelText();
        var armies = _mainGame.Session.State.Armies;
        var other = armies.First(a =>
            a.Id != _armyId && a.Nation == "rome" && a.AboardFleetId is null
            && ReferenceEquals(armies.First(o => o.X == a.X && o.Y == a.Y && o.AboardFleetId is null), a));
        _armyId = other.Id;
        ClickTile(other.X, other.Y);
    }

    private void CheckSelectedWhileHidden()
    {
        Check(_armySelections == 2, $"selecting a unit while hidden selects it ({_armyId}; {_armySelections} selections)");
        Check(!_mainGame.SideColumn.Visible, "the column stays hidden after selecting a unit");
        Check(PanelText() != _panelText, "the (hidden) panel now shows the newly selected army");
        _panelText = PanelText();
        Check(_selectionsCleared == 0, "no selection was cleared");
        CheckNoCommands("after selecting while hidden");
    }

    private void CheckSelectedWhileHiddenShowsOnShow()
    {
        Check(_mainGame.SideColumn.Visible, "F12 shows the column after a selection made while hidden");
        Check(PanelText() == _panelText && ShowsArmyView(), "the shown panel is the army view of the army selected while hidden");
        CheckNoCommands("after showing a selection made while hidden");
    }

    private void CheckArmySelected()
    {
        Check(_armySelections >= 1, $"clicking the army selects it ({_armyId})");
        Check(ShowsArmyView(), "the panel shows the army view");
        _panelText = PanelText();
    }

    private void CheckSelectionKeptWhileHidden()
    {
        Check(!_mainGame.SideColumn.Visible, "the column is hidden with an army selected");
        Check(_armySelections == 1 && _selectionsCleared == 0, "hiding keeps the selection");
        Check(PanelText() == _panelText, "hiding keeps the selected army's identity (the panel text is identical)");
        CheckNoCommands("after hiding with an army selected");
    }

    private void CheckSelectionKeptWhenShown()
    {
        Check(_mainGame.SideColumn.Visible, "the column is shown again");
        Check(_armySelections == 1 && _selectionsCleared == 0, "showing keeps the selection");
        Check(PanelText() == _panelText, "showing keeps the selected army's identity (the panel text is identical)");
        Check(ShowsArmyView(), "the panel's army view survived hide and show");
        CheckNoCommands("after showing with an army selected");
    }

    /// <summary>
    /// Whether the panel is showing an army's own view. T111 moved the army orders — Disband among
    /// them — out of this panel and into their own dialogs, so the old Disband-button probe no longer
    /// exists. The army panel's one stable marker is its heading, <c>"Army — {army.Id}"</c>
    /// (<see cref="ContextPanel.BuildArmyPanel"/>), the same heading <c>MapClickCheck</c> already keys
    /// on. A selected unit is not a nation status panel, so the two nation accessors remain as a guard
    /// against the All-nations and nation-overview views.
    /// </summary>
    private bool ShowsArmyView() =>
        _mainGame.ContextPanel.StatusNationIdForCheck is null
        && !_mainGame.ContextPanel.ShowsAllNationsForCheck
        && AllLabels(_mainGame.ContextPanel).Any(
            label => label.Text.StartsWith("Army — ", StringComparison.Ordinal));

    private void OpenOverlay()
    {
        _shownBeforeOverlay = _mainGame.SideColumn.Visible;
        _mainGame.OpenDiplomacyScreen();
        Check(_mainGame.ActiveOverlay is DiplomacyScreen, "the diplomacy screen opens as an overlay without a table command");
        CheckNoCommands("after opening an overlay");
    }

    private void CheckOverlayBlockedF12()
    {
        Check(_mainGame.SideColumn.Visible == _shownBeforeOverlay, "with an overlay open, F12 changes nothing");
        CheckNoCommands("with F12 pressed under an overlay");
    }

    private void CloseOverlay()
    {
        (_mainGame.ActiveOverlay as DiplomacyScreen)?.Close();
        Check(_mainGame.ActiveOverlay is null, "the diplomacy screen closes");
        CheckNoCommands("after closing the overlay");
    }

    private void BuildSecond()
    {
        Check(!_mainGame.SideColumn.Visible, "the first screen is hidden before the New Game");
        _second = Build();
    }

    private void CheckSecondHidden()
    {
        Check(!_second!.SideColumn.Visible, "a second screen in the same run starts with the column hidden");
        Check(_second.SidePanelButton.Text == "«", "the second screen's button reads «");
    }

    private void ClickSecondButton()
    {
        _clickScreen = _second!;
        ClickButton();
    }

    private void CheckBothShown()
    {
        Check(_mainGame.SideColumn.Visible && _second!.SideColumn.Visible, "toggling after the second screen is built shows both columns");
        Check(_mainGame.SidePanelButton.Text == "»" && _second!.SidePanelButton.Text == "»", "both buttons read » and agree");
        Check(_mainGame.SidePanelButton.TooltipText == _second.SidePanelButton.TooltipText, "both tooltips agree");
    }

    private void CheckBothHidden()
    {
        Check(!_mainGame.SideColumn.Visible && !_second!.SideColumn.Visible, "F12 with two live screens hides both columns");
        Check(_mainGame.SidePanelButton.Text == "«" && _second!.SidePanelButton.Text == "«", "both buttons read « and agree");
        Check(_mainGame.SideColumn.Visible == new SidePanelToggle().IsShown, "both screens agree with SidePanelToggle.IsShown");
    }

    private void FinalNoCommands() => CheckNoCommands("at the end of the run");

    private void CheckNoCommands(string when)
    {
        Check(_commandsSeen == 0, $"no command was issued {when} (seam count {_commandsSeen})");
        Check(_mainGame.CommandTable.IssuedCount == _issuedAtStart, $"CommandTable.IssuedCount is unchanged {when}");
        Check(ReferenceEquals(_mainGame.Session.State, _stateAtStart), $"Session.State is the same reference {when}");
        if (_armyId is null)
        {
            var viewed = _mainGame.ContextPanel.StatusNationIdForCheck;
            Check(viewed == _viewed0, $"the viewed nation is unchanged {when} ({_viewed0} vs {viewed})");
        }
    }

    private void Finish()
    {
        new SidePanelToggle().Set(true);
        GD.Print($"SidePanelToggleCheck: exiting with code {(_ok ? 0 : 1)}.");
        GetTree().Quit(_ok ? 0 : 1);
    }

    private static bool Inside(Rect2 r, Vector2 v) =>
        r.Position.X >= -0.5f && r.Position.Y >= -0.5f && r.End.X <= v.X + 0.5f && r.End.Y <= v.Y + 0.5f;

    private void Check(bool condition, string description)
    {
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
        _ok &= condition;
    }
}
