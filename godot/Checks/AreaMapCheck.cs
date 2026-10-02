using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T102's headless reproduction of the overview mini-map: the real <see cref="MainGameScreen"/> is
/// built exactly as the app builds it, a real mouse click is pushed at Rome's tile on the mini-map, and
/// the order map is asserted to have re-centred on Rome. The same check then proves the mini-map's view
/// rectangle follows a zoom and a pan, that a mini-map click submits no command and changes no
/// selection, and that the mini-map sits outside the order map's own rect. Run headless via:
/// <code>
/// Godot_..._console.exe --headless --path godot res://Checks/AreaMapCheck.tscn --quit-after 600
/// </code>
/// </summary>
/// <remarks>
/// The click goes through <see cref="Viewport.PushInput(InputEvent, bool)"/> with
/// <c>in_local_coords: true</c> and the control's own <see cref="Node2D.GlobalPosition"/> — without
/// <c>true</c> a headless click lands nowhere (the same defect T100's reviewer found in
/// <c>MenuBarCheck.ClickControl</c>). The click's effect on the order map's centre is the positive
/// control that it landed. Nothing here reads a pixel: the Godot checks run headless, and the windowed
/// visual sign-off is the main session's.
/// </remarks>
public partial class AreaMapCheck : Control
{
    private const int InitialSettleFrames = 6;
    private const int BetweenStepsFrames = 6;
    private const string RomeName = "Rome";

    private MainGameScreen _mainGame = null!;
    private AreaMapView _miniMap = null!;
    private CityState _rome = null!;

    private bool _ok = true;
    private int _frame;
    private int _step;

    private int _commandsSeen;
    private int _citySelections;
    private int _armySelections;
    private int _fleetSelections;
    private int _selectionsCleared;

    private int _commandsBeforeMiniMapClick;
    private int _selectionEventsBeforeMiniMapClick;
    private (int X, int Y) _centreBeforeMiniMapClick;
    private AreaMapPixelRect _viewRectBeforeZoom;

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
        _mainGame.MapView.CitySelected += _ => _citySelections++;
        _mainGame.MapView.ArmySelected += _ => _armySelections++;
        _mainGame.MapView.FleetSelected += _ => _fleetSelections++;
        _mainGame.MapView.SelectionCleared += () => _selectionsCleared++;
    }

    public override void _Process(double delta)
    {
        _frame++;

        try
        {
            switch (_step)
            {
                case 0 when _frame >= InitialSettleFrames:
                    CheckLayout();
                    _frame = 0;
                    _step = 1;
                    break;

                case 1 when _frame >= BetweenStepsFrames:
                    SelectRomeOnOrderMap();
                    _frame = 0;
                    _step = 2;
                    break;

                case 2 when _frame >= BetweenStepsFrames:
                    ClickMiniMapAtRome();
                    _frame = 0;
                    _step = 3;
                    break;

                case 3 when _frame >= BetweenStepsFrames:
                    CheckMiniMapClickReCentred();
                    _frame = 0;
                    _step = 4;
                    break;

                case 4 when _frame >= BetweenStepsFrames:
                    ZoomAndPanOrderMap();
                    _frame = 0;
                    _step = 5;
                    break;

                case 5 when _frame >= BetweenStepsFrames:
                    CheckViewRectangleFollows();
                    Finish();
                    break;
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"AreaMapCheck: unhandled exception: {ex}");
            Finish();
        }
    }

    private void CheckLayout()
    {
        _miniMap = FindChild<AreaMapView>(_mainGame)
            ?? throw new InvalidOperationException("MainGameScreen built no AreaMapView.");
        _rome = _mainGame.Session.State.Cities.Single(c => c.Name == RomeName);

        Check(_miniMap.HasTerrainForCheck, "the mini-map baked its terrain from the game state");
        Check(
            _miniMap.GeometryForCheck.PixelWidth >= _mainGame.Session.World.Width
                && _miniMap.GeometryForCheck.PixelHeight >= _mainGame.Session.World.Height,
            "the mini-map gives the world one pixel or more per tile "
            + $"({_miniMap.GeometryForCheck.PixelWidth}x{_miniMap.GeometryForCheck.PixelHeight} "
            + $"for {_mainGame.Session.World.Width}x{_mainGame.Session.World.Height} tiles at "
            + $"{_miniMap.GeometryForCheck.Scale}px/tile)");

        // The mini-map must not widen the right-hand column past the context panel's 340 px floor.
        Check(
            Mathf.Abs(_mainGame.ContextPanel.Size.X - 340f) < 1f,
            $"the context panel keeps its 340px floor beside the mini-map (got {_mainGame.ContextPanel.Size.X}px)");

        var mapRect = _mainGame.MapView.GetGlobalRect();
        var miniRect = _miniMap.GetGlobalRect();
        Check(
            !miniRect.Intersects(mapRect),
            $"the mini-map's rect {miniRect} does not overlap the order map's rect {mapRect}");
    }

    /// <summary>
    /// Clicks Rome on the order map through the real <c>_GuiInput</c> path the existing
    /// <c>MapClickCheck</c> uses, so there is a selection for the mini-map click to leave unchanged.
    /// </summary>
    private void SelectRomeOnOrderMap()
    {
        var position = _mainGame.MapView.TileCenterForCheck(_rome.X, _rome.Y);
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

    private void ClickMiniMapAtRome()
    {
        _commandsBeforeMiniMapClick = _commandsSeen;
        _selectionEventsBeforeMiniMapClick = SelectionEventCount();
        var centre = _mainGame.MapView.VisibleTileRect;
        _centreBeforeMiniMapClick = (
            Mathf.FloorToInt(centre.Position.X + (centre.Size.X / 2f)),
            Mathf.FloorToInt(centre.Position.Y + (centre.Size.Y / 2f)));

        // The click goes to the tile's centre, in the mini-map's own pixels, converted to viewport
        // coordinates. in_local_coords: true is what makes the click land under the headless window.
        var tileCentre = _miniMap.GeometryForCheck.TileCentre(_rome.X, _rome.Y);
        var global = _miniMap.GlobalPosition + new Vector2(tileCentre.X, tileCentre.Y);
        var viewport = GetViewport();
        viewport.PushInput(new InputEventMouseMotion { Position = global, GlobalPosition = global }, true);
        viewport.PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = global,
            GlobalPosition = global,
            ButtonMask = MouseButtonMask.Left,
        }, true);
        viewport.PushInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = global,
            GlobalPosition = global,
        }, true);
    }

    private void CheckMiniMapClickReCentred()
    {
        var visible = _mainGame.MapView.VisibleTileRect;
        var centreX = Mathf.FloorToInt(visible.Position.X + (visible.Size.X / 2f));
        var centreY = Mathf.FloorToInt(visible.Position.Y + (visible.Size.Y / 2f));
        var distance = Mathf.Max(Mathf.Abs(centreX - _rome.X), Mathf.Abs(centreY - _rome.Y));

        // The positive control: this distance can only be small because the pushed click landed.
        Check(
            distance <= 1,
            $"a real-input click on the mini-map at Rome re-centres the order map on Rome "
            + $"(centre tile ({centreX},{centreY}), Rome ({_rome.X},{_rome.Y}), Chebyshev {distance})");
        Check(
            (centreX, centreY) != _centreBeforeMiniMapClick,
            $"the mini-map click actually moved the order map "
            + $"(was ({_centreBeforeMiniMapClick.X},{_centreBeforeMiniMapClick.Y}))");

        Check(
            _commandsSeen == _commandsBeforeMiniMapClick,
            $"a mini-map click submits no command ({_commandsBeforeMiniMapClick} -> {_commandsSeen})");
        Check(
            SelectionEventCount() == _selectionEventsBeforeMiniMapClick,
            $"a mini-map click changes no selection "
            + $"({_selectionEventsBeforeMiniMapClick} -> {SelectionEventCount()} selection events)");
    }

    private void ZoomAndPanOrderMap()
    {
        _viewRectBeforeZoom = _miniMap.ViewRectForCheck;

        _mainGame.MapView.ZoomToMaxForCheck();

        // A real drag: the press arms the map's own drag state, the motion pans it. The 40 px move is
        // past the view's 4 px click threshold, so the release is a pan, not a click.
        var map = _mainGame.MapView;
        var start = map.Size / 2f;
        map._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
            Position = start,
        });
        map._GuiInput(new InputEventMouseMotion
        {
            Position = start + new Vector2(40f, 30f),
            Relative = new Vector2(40f, 30f),
        });
        map._GuiInput(new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = false,
            Position = start + new Vector2(40f, 30f),
        });
    }

    private void CheckViewRectangleFollows()
    {
        var geometry = _miniMap.GeometryForCheck;
        var visible = _mainGame.MapView.VisibleTileRect;
        var expected = geometry.MapTileRect(
            visible.Position.X,
            visible.Position.Y,
            visible.Position.X + visible.Size.X,
            visible.Position.Y + visible.Size.Y);

        Check(
            _miniMap.ViewRectForCheck == expected,
            "after the order map is zoomed and panned, the mini-map's view rectangle equals "
            + $"GameMapView.VisibleTileRect mapped through AreaMapGeometry (mini {_miniMap.ViewRectForCheck} "
            + $"vs mapped {expected})");
        Check(
            _miniMap.ViewRectForCheck != _viewRectBeforeZoom,
            $"the view rectangle moved with the zoom and pan (was {_viewRectBeforeZoom}, "
            + $"now {_miniMap.ViewRectForCheck})");
    }

    private int SelectionEventCount() =>
        _citySelections + _armySelections + _fleetSelections + _selectionsCleared;

    private static T? FindChild<T>(Node root)
        where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T match)
            {
                return match;
            }

            var nested = FindChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void Check(bool condition, string description)
    {
        _ok &= condition;
        GD.Print(condition ? $"PASS: {description}" : $"FAIL: {description}");
    }

    private void Finish()
    {
        var exitCode = _ok ? 0 : 1;
        GD.Print($"AreaMapCheck: exiting with code {exitCode}.");
        GetTree().Quit(exitCode);
    }
}
