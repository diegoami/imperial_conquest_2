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
            AddCases(cellPixels);
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

    private void AddCases(float requestedPixels)
    {
        foreach (var (id, kind, x, y) in new[]
        {
            ("army", StrategicMarkerKind.Army, 100, 60),
            ("fleet", StrategicMarkerKind.Fleet, 110, 60),
            ("dense-left", StrategicMarkerKind.Army, 99, 63),
            ("dense-right", StrategicMarkerKind.Army, 100, 63),
            ("cargo-fleet", StrategicMarkerKind.Fleet, 105, 63),
        })
        {
            Vector2 edge = default;
            _steps.Enqueue(() =>
            {
                _map.CentreOnTile(x, y);
                _map.ClearSelection();
                _selected = null;
                var tile = _map.TileCenterForCheck(x + 1, y).X - _map.TileCenterForCheck(x, y).X;
                var center = _map.TileCenterForCheck(x, y);
                edge = center + new Vector2(0, (y >= 63 ? 1 : -1) * (StrategicMarkerGeometry.Size(kind, tile) / 2 - .01f));
                Check(new Rect2(Vector2.Zero, _map.Size).HasPoint(edge), "edge input lies inside actual control");
                _map._GuiInput(new InputEventMouseMotion { Position = center });
            });
            _steps.Enqueue(() =>
            {
                Check(_selected is null && _map.ForegroundMarkerForCheck == id,
                    $"unselected hover exposes the nearest {id} before clicking");
                Click(_map.TileCenterForCheck(x, y), MouseButton.Left);
            });
            _steps.Enqueue(() =>
            {
                Check(requestedPixels != 72 || Mathf.IsEqualApprox(_map.ZoomFactor, GameMapView.MaxZoom), "maximum zoom uses production clamp");
                Check(_selected == id && _map.ForegroundMarkerForCheck == id,
                    $"left click selects and draws {id} above overlaps at requested {requestedPixels} px/tile");
                _listed = null;
                Click(edge, MouseButton.Right);
            });
            _steps.Enqueue(() =>
            {
                var context = _map.LastClickContextForCheck!;
                Check(_listed == id && _map.ForegroundMarkerForCheck == id, $"right footprint edge lists visible {id}");
                Check(context.X == x && context.Y == y && context.Distance == 0,
                    $"{id} overhang uses entity tile and actual selection distance");
                _map.ClearSelection();
                _selected = null;
                Click(edge, MouseButton.Left);
            });
            _steps.Enqueue(() => Check(_selected == id && _map.ForegroundMarkerForCheck == id,
                $"left footprint edge selects and outlines visible {id}"));
        }
        _steps.Enqueue(() =>
        {
            _map.CentreOnTile(115, 65);
            _map.ClearSelection();
            Click(_map.TileCenterForCheck(115, 65), MouseButton.Right);
            Check(_map.LastClickContextForCheck is { X: 115, Y: 65, Target: null }, "empty map keeps clicked tile");
            _map.CentreOnTile(120, 60);
            Click(_map.TileCenterForCheck(120, 60), MouseButton.Right);
            Check(_map.LastClickContextForCheck?.Target is null, "fleet under construction has no visible target");
            _map.CentreOnTile(100, 60);
            Click(_map.TileCenterForCheck(100, 60), MouseButton.Right);
            Check(_listed == "army", "coincident army precedes fleet and city");
            Click(_map.TileCenterForCheck(100, 60), MouseButton.Left);
        });
        _steps.Enqueue(() =>
        {
            _map.CentreOnTile(110, 60);
            var tile = _map.TileCenterForCheck(111, 60).X - _map.TileCenterForCheck(110, 60).X;
            var edge = _map.TileCenterForCheck(110, 60) - new Vector2(0,
                StrategicMarkerGeometry.Size(StrategicMarkerKind.Fleet, tile) / 2 - .01f);
            Click(edge, MouseButton.Right);
            Check(_map.LastClickContextForCheck is { X: 110, Y: 60, Distance: 10, Target.Id: "fleet" },
                "order context distance uses actual fleet tile, not overhang tile");
            Check(_map.ClipContents, "marker overhang is clipped by real map control");
        });
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
