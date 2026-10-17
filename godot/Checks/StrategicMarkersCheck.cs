using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>Real strategic drawing and both mouse buttons, using production footprint and context.
/// Run after import: godot --headless --path godot res://Checks/StrategicMarkersCheck.tscn --quit-after 900.</summary>
public partial class StrategicMarkersCheck : Control
{
    private GameMapView _map = null!;
    private GameSession _session = null!;
    private readonly Queue<Action> _steps = new();
    private int _frames;
    private bool _ok = true;
    private string? _selected;
    private string? _listed;

    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;
        _session = BuildSession();
        _map = new GameMapView { Position = new Vector2(30, 30), Size = new Vector2(960, 600) };
        AddChild(_map);
        _map.Attach(_session, GameDataContext.RepositoryRoot);
        _map.ArmySelected += id => _selected = id;
        _map.FleetSelected += id => _selected = id;
        _map.CitySelected += id => _selected = id;
        _map.UnitListRequested += (_, id) => _listed = id;
        foreach (var pixels in new[] { 0f, 3f, 72f })
        {
            var cellPixels = pixels;
            _steps.Enqueue(() =>
            {
                if (cellPixels > 0) _map.SetDrawnCellPixelsForCheck(cellPixels);
                _map.CentreOnTile(100, 60);
            });
            _steps.Enqueue(() => Exercise(cellPixels));
        }
        _steps.Enqueue(() =>
        {
            _map.Size = new Vector2(720, 480);
            _map.CentreOnTile(0, 0);
        });
        _steps.Enqueue(() =>
        {
            Check(_map.ClipContents, "resized/panned production drawing is clipped");
            Click(_map.TileCenterForCheck(0, 0), MouseButton.Right);
            Check(_map.LastClickContextForCheck is { X: 0, Y: 0, Target.Id: "edge" }, "world-edge marker targets real world tile");
        });
    }

    public override void _Process(double delta)
    {
        if (++_frames < 6) return;
        _frames = 0;
        try
        {
            if (_steps.TryDequeue(out var step)) step();
            if (_steps.Count > 0) return;
            GD.Print($"StrategicMarkersCheck: exiting with code {(_ok ? 0 : 1)}.");
            GetTree().Quit(_ok ? 0 : 1);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"StrategicMarkersCheck: FAIL {ex}");
            GD.Print("StrategicMarkersCheck: exiting with code 1.");
            GetTree().Quit(1);
        }
    }

    private void Exercise(float requestedPixels)
    {
        var tile = _map.TileCenterForCheck(101, 60).X - _map.TileCenterForCheck(100, 60).X;
        Check(requestedPixels != 72 || Mathf.IsEqualApprox(_map.ZoomFactor, GameMapView.MaxZoom), "maximum zoom uses production clamp");
        foreach (var (id, kind, x, y) in new[]
        {
            ("army", StrategicMarkerKind.Army, 100, 60),
            ("fleet", StrategicMarkerKind.Fleet, 110, 60),
            ("dense-left", StrategicMarkerKind.Army, 99, 63),
            ("dense-right", StrategicMarkerKind.Army, 100, 63),
            ("cargo-fleet", StrategicMarkerKind.Fleet, 105, 63),
        })
        {
            _map.ClearSelection();
            _selected = null;
            var center = _map.TileCenterForCheck(x, y);
            Click(center, MouseButton.Left);
            Check(_selected == id, $"left click selects {id} at {tile} px/tile");
            var size = StrategicMarkerGeometry.Size(kind, tile);
            // Fleet/army fixtures have empty space on their north side; this tests the real square
            // boundary, including overview overhang, with no copied size formula.
            var edge = center - new Vector2(0, size / 2 - .01f);
            _listed = null;
            Click(edge, MouseButton.Right);
            var context = _map.LastClickContextForCheck!;
            Check(_listed == id, $"right footprint-edge click lists {id}");
            Check(context.X == x && context.Y == y && context.Distance == 0,
                $"{id} overhang uses entity tile and actual selection distance");
            _map.ClearSelection();
            Click(edge, MouseButton.Left);
            Check(_selected == id, $"left footprint-edge click selects {id}");
        }
        _map.ClearSelection();
        Click(_map.TileCenterForCheck(115, 65), MouseButton.Right);
        Check(_map.LastClickContextForCheck is { X: 115, Y: 65, Target: null }, "empty map keeps clicked tile");
        Click(_map.TileCenterForCheck(120, 60), MouseButton.Right);
        Check(_map.LastClickContextForCheck?.Target is null, "fleet under construction has no visible target");
        Click(_map.TileCenterForCheck(100, 60), MouseButton.Right);
        Check(_listed == "army", "coincident army precedes fleet and city");
        Check(_map.ClipContents, "all marker overhang is clipped by real map control");
    }

    private void Click(Vector2 position, MouseButton button)
    {
        _map._GuiInput(new InputEventMouseButton { Position = position, ButtonIndex = button, Pressed = true });
        _map._GuiInput(new InputEventMouseButton { Position = position, ButtonIndex = button, Pressed = false });
    }

    private void Check(bool condition, string message)
    {
        GD.Print($"StrategicMarkersCheck: {(condition ? "PASS" : "FAIL")} {message}");
        _ok &= condition;
    }

    private static GameSession BuildSession()
    {
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");
        var initial = new GameSession(resolved.World, resolved.Ruleset, resolved.Scenario, seedOverride: 1, humanSeatNationId: "rome");
        var army = initial.State.Armies.First(a => a.Nation == "rome");
        var fleet = initial.State.Fleets.First() with { Nation = "rome", ConstructionTicksRemaining = null };
        var armies = new[]
        {
            army with { Id = "army", X = 100, Y = 60, AboardFleetId = null },
            army with { Id = "dense-left", X = 99, Y = 63, AboardFleetId = null },
            army with { Id = "dense-right", X = 100, Y = 63, AboardFleetId = null },
            army with { Id = "cargo", X = 105, Y = 63, AboardFleetId = "cargo-fleet", CoveredTileCode = null },
            army with { Id = "edge", X = 0, Y = 0, AboardFleetId = null },
        };
        var fleets = new[]
        {
            fleet with { Id = "same-tile-fleet", X = 100, Y = 60 },
            fleet with { Id = "fleet", X = 110, Y = 60 },
            fleet with { Id = "cargo-fleet", X = 105, Y = 63, CarriedArmyId = "cargo" },
            fleet with { Id = "construction", X = 120, Y = 60, ConstructionTicksRemaining = 1 },
        };
        var state = initial.State with
        {
            Armies = ValueList.From(armies), Fleets = ValueList.From(fleets),
            Cities = ValueList.From(new[] { initial.State.Cities.First() with { X = 100, Y = 60 } }),
        };
        var save = new SaveGame(state.SchemaVersion, "strategic-markers-check", "strategic-markers-check",
            state.ScenarioId, state.WorldId, state.RulesetId, state);
        return new GameSession(resolved.World, resolved.Ruleset, resolved.Scenario, save);
    }
}
