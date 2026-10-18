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
        var captureDirectory = System.Environment.GetEnvironmentVariable("IC2_STRATEGIC_MARKERS_CAPTURE_DIR");
        if (!string.IsNullOrWhiteSpace(captureDirectory))
        {
            if (System.Environment.GetEnvironmentVariable("IC2_STRATEGIC_MARKERS_ADJACENT") == "1")
                BuildAdjacentCapturePlan(captureDirectory);
            else
                BuildCapturePlan(captureDirectory);
            return;
        }
        foreach (var pack in new[] { "authored", "placeholder" })
        {
            _steps.Enqueue(() =>
            {
                SelectPack(pack);
                if (_map is not null) { RemoveChild(_map); _map.QueueFree(); }
                _session = BuildSession();
                _map = new GameMapView { Position = new Vector2(30, 30), Size = new Vector2(960, 600) };
                AddChild(_map);
                _map.Attach(_session, GameDataContext.RepositoryRoot);
                _map.ArmySelected += id => _selected = id;
                _map.FleetSelected += id => _selected = id;
                _map.CitySelected += id => _selected = id;
                _map.UnitListRequested += (_, id) => _listed = id;
                Check(_map.LoadedPackIdForCheck == pack, $"real input check loads {pack}");
            });
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
        }
        _steps.Enqueue(() =>
        {
            _map.ClearSelection();
            _map.CentreOnTile(100, 60);
            _map._GuiInput(new InputEventMouseMotion { Position = _map.TileCenterForCheck(100, 60) });
        });
        _steps.Enqueue(() =>
        {
            Check(_map.ForegroundMarkerForCheck == "army", "unselected hover starts on army");
            _map.SetDrawnCellPixelsForCheck(32);
            _map.CentreOnTile(110, 60);
        });
        _steps.Enqueue(() => Check(_map.ForegroundMarkerForCheck == "fleet",
            "stationary pointer recomputes nearest target after zoom and pan, without stale hover"));
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
        if (++_frames < 3) return;
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
                Check(_selected == id && _map.SelectedStrategicMarkerForCheck == id && _map.ForegroundMarkerForCheck == id,
                    $"left click selects and draws {id} above overlaps at requested {requestedPixels} px/tile");
                _listed = null;
                Click(edge, MouseButton.Right);
            });
            _steps.Enqueue(() =>
            {
                var context = _map.LastClickContextForCheck!;
                Check(_listed == id && _map.ForegroundMarkerForCheck == id, $"right footprint edge lists visible {id}");
                Check(context.X == x && context.Y == y && context.Distance == 0,
                    $"{id} tile-constrained marker uses entity tile and actual selection distance");
                _map.ClearSelection();
                _selected = null;
                Click(edge, MouseButton.Left);
            });
            _steps.Enqueue(() =>
            {
                Check(_selected == id && _map.SelectedStrategicMarkerForCheck == id && _map.ForegroundMarkerForCheck == id,
                    $"left footprint edge selects and outlines visible {id}");
                CheckContainment(id, x, y);
                _map.MissingStrategicTexturesForCheck = true;
            });
            _steps.Enqueue(() => { CheckContainment(id, x, y); _map.MissingStrategicTexturesForCheck = false; });
            foreach (var button in new[] { MouseButton.Left, MouseButton.Right })
            _steps.Enqueue(() =>
            {
                _map.ClearSelection();
                var center = _map.TileCenterForCheck(x, y);
                var tile = _map.TileCenterForCheck(x + 1, y).X - center.X;
                Click(center + new Vector2(tile * .49f, tile * .49f), button);
                Check(_map.LastClickContextForCheck is { } context && context.X == x && context.Y == y && context.Target?.Id == id,
                    $"{button} tile corner/padding keeps {id}");
                _map.ClearSelection(); // Neighbour probe must not issue a move from the preceding selection.
                Click(center + new Vector2(tile * .51f, 0), button);
                Check(_map.LastClickContextForCheck is { } next && next.X == x + 1 && next.Target?.Id != id,
                    $"{button} neighbouring tile never intercepted by {id}");
            });
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
                "order context distance uses actual fleet tile, not tile-constrained marker tile");
            Check(_map.ClipContents, "marker tile-constrained marker is clipped by real map control");
        });
    }

    private void CheckContainment(string id, int x, int y)
    {
        var center = _map.TileCenterForCheck(x, y);
        var tile = _map.TileCenterForCheck(x + 1, y).X - center.X;
        var cell = new Rect2(center - Vector2.One * tile / 2, Vector2.One * tile);
        foreach (var bounds in new[] { _map.StrategicDrawBoundsForCheck[id], _map.StrategicFeedbackBoundsForCheck[id] })
            Check(bounds.Position.X >= cell.Position.X - .001f && bounds.Position.Y >= cell.Position.Y - .001f
                && bounds.End.X <= cell.End.X + .001f && bounds.End.Y <= cell.End.Y + .001f,
                $"actual drawn art/feedback remains in {id} tile (fallback={_map.MissingStrategicTexturesForCheck})");
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

    // Windowed evidence mode uses the real map and textures. Before shots replay ONLY former
    // board dimensions in the actual drawer, not a fabricated board or a full baseline-input claim.
    private void BuildCapturePlan(string directory)
    {
        Directory.CreateDirectory(directory);
        var caption = new Label { Position = new Vector2(30, 10), Size = new Vector2(Size.X - 60, 40) };
        AddChild(caption);
        foreach (var pack in new[] { "authored", "placeholder" })
        foreach (var before in new[] { true, false })
        foreach (var zoom in new[] { "fit", "minimum", "maximum" })
        {
            var prefix = $"{pack}-{(before ? "before-size-replay" : "after")}-{zoom}";
            var positions = new List<(string Id, int X, int Y)>();
            _steps.Enqueue(() =>
            {
                if (_map is not null) { RemoveChild(_map); _map.QueueFree(); }
                SelectPack(pack);
                _map = new GameMapView { Position = new Vector2(30, 60), Size = Size - new Vector2(60, 90) };
                AddChild(_map);
                _map.Attach(BuildSession(), GameDataContext.RepositoryRoot);
                if (zoom != "fit") _map.SetDrawnCellPixelsForCheck(zoom == "maximum" ? 72 : 3);
                var tile = _map.TileCenterForCheck(101, 60).X - _map.TileCenterForCheck(100, 60).X;
                var spacing = Math.Max(2, (int)Math.Ceiling(80 / tile));
                _session = BuildVisualSession(spacing);
                _map.Attach(_session, GameDataContext.RepositoryRoot);
                if (zoom != "fit") _map.SetDrawnCellPixelsForCheck(zoom == "maximum" ? 72 : 3);
                _map.CentreOnTile(100, 60);
                _map.PreT187MarkerSizeForCheck = before;
                _map.ClearSelection();
                positions.Clear();
                for (var tier = 0; tier < 3; tier++)
                {
                    positions.Add(($"army-tier{tier + 1}", 100 + (tier - 1) * spacing, 60 - spacing));
                    positions.Add(($"fleet-tier{tier + 1}", 100 + (tier - 1) * spacing, 60 + spacing));
                }
                caption.Text = $"{prefix}: army tiers 1/2/3 above, fleet tiers 1/2/3 below. "
                    + $"Actual cell {tile:0.00}px; {(before ? "former sizes replayed" : "one-tile designed boards")}.";
                Check(_map.LoadedPackIdForCheck == pack, $"window evidence uses real {pack} pack");
            });
            _steps.Enqueue(() => Capture(directory, prefix + "-unselected.png"));
            foreach (var index in Enumerable.Range(0, 6))
            {
                var targetIndex = index;
                _steps.Enqueue(() =>
                {
                    var target = positions[targetIndex];
                    _map.ClearSelection();
                    var position = _map.TileCenterForCheck(target.X, target.Y);
                    // The after tour demonstrates a real footprint-edge click. Before shots use
                    // centres because the replay is visual sizing evidence, not baseline input.
                    if (!before)
                    {
                        var tile = _map.TileCenterForCheck(target.X + 1, target.Y).X - position.X;
                        var kind = target.Id.StartsWith("army", StringComparison.Ordinal)
                            ? StrategicMarkerKind.Army : StrategicMarkerKind.Fleet;
                        position.Y += (target.Id.StartsWith("army", StringComparison.Ordinal) ? -1 : 1)
                            * (tile / 2 - .01f);
                    }
                    Click(position, MouseButton.Left);
                    Check(_map.LastClickContextForCheck?.Target?.Id == target.Id && _map.SelectedStrategicMarkerForCheck == target.Id, $"capture edge resolves {target.Id} on its actual tile");
                    caption.Text = $"{prefix}: {target.Id} selected via {(before ? "centre" : "in-tile edge")} click.";
                });
                _steps.Enqueue(() => Capture(directory, prefix + $"-{positions[targetIndex].Id}-selected.png"));
            }
            _steps.Enqueue(() =>
            {
                _map.ClearSelection();
                _map.CentreOnTile(100 + 3 * (int)Math.Ceiling(80 / (_map.TileCenterForCheck(101, 60).X - _map.TileCenterForCheck(100, 60).X)), 60);
                var dense = _session.State.Armies.Single(a => a.Id == "dense");
                Click(_map.TileCenterForCheck(dense.X, dense.Y), MouseButton.Left);
                caption.Text = $"{prefix}: coincident city/fleet/army; army wins and draws in front.";
            });
            _steps.Enqueue(() => Capture(directory, prefix + "-dense-selected.png"));
        }
    }

    // Focused real-window supplement: neighboring production boards at overview zoom.
    private void BuildAdjacentCapturePlan(string directory)
    {
        Directory.CreateDirectory(directory);
        var caption = new Label { Position = new Vector2(30, 10), Size = new Vector2(Size.X - 60, 40) };
        AddChild(caption);
        foreach (var pack in new[] { "authored", "placeholder" })
        foreach (var pixels in new[] { 0f, 3f })
        {
            var prefix = $"{pack}-adjacent-{(pixels == 0 ? "fit" : "minimum")}";
            _steps.Enqueue(() =>
            {
                if (_map is not null) { RemoveChild(_map); _map.QueueFree(); }
                SelectPack(pack);
                _session = BuildSession();
                _map = new GameMapView { Position = new Vector2(30, 60), Size = Size - new Vector2(60, 90) };
                AddChild(_map);
                _map.Attach(_session, GameDataContext.RepositoryRoot);
                if (pixels > 0) _map.SetDrawnCellPixelsForCheck(pixels);
                _map.CentreOnTile(100, 63);
                _map.ClearSelection();
                caption.Text = $"{prefix}: adjacent armies (99,63)/(100,63), unselected.";
            });
            _steps.Enqueue(() => Capture(directory, prefix + "-unselected.png"));
            foreach (var x in new[] { 99, 100 })
            {
                var targetX = x;
                _steps.Enqueue(() =>
                {
                    var point = _map.TileCenterForCheck(targetX, 63);
                    _map._GuiInput(new InputEventMouseMotion { Position = point });
                    Click(point, MouseButton.Left);
                    var expected = targetX == 99 ? "dense-left" : "dense-right";
                    Check(_map.LastClickContextForCheck?.Target?.Id == expected
                        && _map.SelectedStrategicMarkerForCheck == expected,
                        $"adjacent window click selects {expected} on its real tile");
                    caption.Text = $"{prefix}: {expected} selected at actual tile centre; hover winner in front.";
                });
                _steps.Enqueue(() => Capture(directory, prefix + $"-{targetX}-selected.png"));
            }
        }
    }

    private void SelectPack(string pack)
    {
        var settings = new SettingsScreen();
        AddChild(settings);
        var picker = settings.FindChildren("*", "OptionButton", true, false).OfType<OptionButton>().First();
        var index = Enumerable.Range(0, picker.ItemCount).Single(i => picker.GetItemText(i) == pack);
        picker.Select(index);
        picker.EmitSignal(OptionButton.SignalName.ItemSelected, index);
        RemoveChild(settings);
        settings.QueueFree();
    }

    private void Capture(string directory, string fileName)
    {
        var result = GetViewport().GetTexture().GetImage().SavePng(Path.Combine(directory, fileName));
        Check(result == Error.Ok, $"saved real window {fileName}");
    }

    private static GameSession BuildVisualSession(int spacing)
    {
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");
        var initial = new GameSession(resolved.World, resolved.Ruleset, resolved.Scenario, seedOverride: 1, humanSeatNationId: "rome");
        var army = initial.State.Armies.First(a => a.Nation == "rome");
        var fleet = initial.State.Fleets.First() with { Nation = "rome", ConstructionTicksRemaining = null };
        var armies = Enumerable.Range(0, 3).Select(tier => army with
        {
            Id = $"army-tier{tier + 1}", X = 100 + (tier - 1) * spacing, Y = 60 - spacing,
            AboardFleetId = null,
            Units = ValueList.From(new[] { army.Units.First() with { Troops = new[] { 10000, 25000, 50000 }[tier] } }),
        }).ToList();
        var fleets = Enumerable.Range(0, 3).Select(tier => fleet with
        {
            Id = $"fleet-tier{tier + 1}", X = 100 + (tier - 1) * spacing, Y = 60 + spacing,
            Ships = new[] { 10, 25, 50 }[tier], CarriedArmyId = null,
        }).ToList();
        armies.Add(army with { Id = "dense", X = 100 + 3 * spacing, Y = 60, AboardFleetId = null });
        fleets.Add(fleet with { Id = "dense-fleet", X = 100 + 3 * spacing, Y = 60, CarriedArmyId = null });
        var state = initial.State with
        {
            Armies = ValueList.From(armies), Fleets = ValueList.From(fleets),
            Cities = ValueList.From(new[] { initial.State.Cities.First() with { X = 100 + 3 * spacing, Y = 60 } }),
        };
        return new GameSession(resolved.World, resolved.Ruleset, resolved.Scenario,
            new SaveGame(state.SchemaVersion, "strategic-marker-visual", "strategic-marker-visual",
                state.ScenarioId, state.WorldId, state.RulesetId, state));
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
