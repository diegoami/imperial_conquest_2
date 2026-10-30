using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T242 (#1010): the classic look ("Classic tiles") draws the original's selection frame, image 12 with
/// SRCERASE (research 2026-10-30-unit-map-selection-drawing1010.md): the selected cell inverted, a black
/// rhombus outline, nothing animated. The improved look ("Painted") keeps T196's feedback.
/// <para>
/// Real <see cref="MainGameScreen"/> and real clicks for owned command selections and foreign
/// inspection frames. Foreign inspection must not enable orders or mutate campaign state.
/// </para>
/// Headless (CI): the frame's placement, its mask parameters, its look switch, its stability over
/// time and its travel offset are asserted on the real view. The headless driver renders nothing, so
/// the pixel samples run in a real window:
/// <c>godot --path godot res://Checks/SelectionFrameCheck.tscn --quit-after 900</c>; captures go to
/// <c>IC2_SELECTION_FRAME_CAPTURE_DIR</c>, else the git-ignored <c>rendered/checks/selection-frame</c>.
/// Success: "SelectionFrameCheck: exiting with code 0." and no FAIL line.
/// </summary>
public partial class SelectionFrameCheck : Node
{
    private bool _ok = true;
    private MainGameScreen? _main;
    private readonly bool _window = DisplayServer.GetName() != "headless";
    private string? _directory;

    public override async void _Ready()
    {
        var style = SettingsScreen.MapStyleId;
        var sound = SettingsScreen.SoundEnabled;
        try
        {
            SettingsScreen.SetSoundEnabledForCheck(false);
            if (_window)
            {
                _directory = System.Environment.GetEnvironmentVariable("IC2_SELECTION_FRAME_CAPTURE_DIR");
                if (string.IsNullOrWhiteSpace(_directory))
                    _directory = Path.Combine(GameDataContext.RepositoryRoot, "rendered", "checks", "selection-frame");
                Directory.CreateDirectory(_directory);
            }

            SettingsScreen.SetMapStyleForCheck(MapStyles.Tiles);
            Require(GameMapView.ClassicSelectionFrame, "Classic tiles is the classic look");
            foreach (var pixels in new[] { 32f, 56f })
            {
                await CheckClassic(pixels, "army", MapEntityKind.Army, "a", 50, 44, own: true);
                await CheckClassic(pixels, "fleet", MapEntityKind.Fleet, "f", 60, 47, own: true);
                await CheckClassic(pixels, "own-town", MapEntityKind.City, "arx", 45, 40, own: true);
                await CheckClassic(pixels, "foreign-town", MapEntityKind.City, "meridia", 55, 40, own: false);
                await CheckClassic(pixels, "foreign-army", MapEntityKind.Army, "enemy-army", 65, 44, own: false);
                await CheckClassic(pixels, "foreign-fleet", MapEntityKind.Fleet, "enemy-fleet", 70, 47, own: false);
            }

            await CheckInspectionLifecycle();
            await CheckTravel();
            SettingsScreen.SetMapStyleForCheck(MapStyles.Painted);
            Require(!GameMapView.ClassicSelectionFrame, "Painted is the improved look");
            await CheckImproved();
        }
        catch (Exception ex) { Require(false, ex.ToString()); }
        finally
        {
            DisposeMain();
            SettingsScreen.SetMapStyleForCheck(style);
            SettingsScreen.SetSoundEnabledForCheck(sound);
        }

        await Frames();
        GD.Print($"SelectionFrameCheck: {(_window ? "real window, pixels sampled" : "headless, no pixels")}; {Godot.Engine.GetProcessFrames()} frames.");
        GD.Print($"SelectionFrameCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private async Task CheckClassic(float pixels, string label, MapEntityKind kind, string id, int x, int y, bool own)
    {
        var name = $"classic-{label}-{pixels:0}px";
        var main = await Open(Scenario());
        var map = main.MapView;
        map.SetDrawnCellPixelsForCheck(pixels);
        map.CentreOnTile(x, y);
        // Keep the command strip laid out in both renders: a neighbour army is selected first, out of
        // the sampled cell, so only the target's selection differs between the two frames.
        // Keep the same action-strip layout in the selected/unselected renders. Foreign inspection
        // uses an inspected foreign neighbour, with no command strip in either render.
        if (own) main.SelectArmyForCheck("n");
        else Click(map, 35, 40); // portus: foreign inspection through the production click path.
        await Frames();
        var cell = CellRect(map, x, y);
        Require(Mathf.IsEqualApprox(cell.Size.X, pixels), $"{name}: the drawn cell is {pixels} px");
        Require(map.SelectionFrameRectForCheck is { } neighbour && !neighbour.Intersects(cell),
            $"{name}: the neighbour's frame stays out of the sampled cell");
        var mapRect = map.GetGlobalRect();
        using var unselected = await Shot(name + "-unselected");

        var beforeState = main.Session.State;
        var commands = 0;
        map.CommandIssued += _ => commands++;
        Click(map, x, y);

        await Frames();
        var selectedId = kind switch
        {
            MapEntityKind.Army => main.MapView.SelectedStrategicMarkerForCheck,
            MapEntityKind.Fleet => main.MapView.SelectedStrategicMarkerForCheck,
            _ => null,
        };
        Require(own ? kind == MapEntityKind.City || selectedId == id : selectedId is null,
            $"{name}: command selection is independent of the inspection frame");
        Require(commands == 0 && main.Session.State == beforeState, $"{name}: inspection/selection submits no order and changes no state");
        if (!own) Require(main.CommandStrip.Selection == UnitStripSelection.None,
            $"{name}: foreign inspection grants no command strip");
        Require(map.SelectionFrameRectForCheck is { } frame && Near(frame, cell),
            $"{name}: the frame covers exactly the selected cell ({map.SelectionFrameRectForCheck} vs {cell})");
        var material = map.SelectionFrameMaterialForCheck;
        Require(material?.Shader?.Code is { } code && code.Contains("hint_screen_texture", StringComparison.Ordinal)
            && !code.Contains("TIME", StringComparison.Ordinal)
            && (float)material.GetShaderParameter("mask_size") == OriginalSelectionFrame.MaskSize
            && (float)material.GetShaderParameter("line_inner") == OriginalSelectionFrame.LineInner
            && (float)material.GetShaderParameter("line_outer") == OriginalSelectionFrame.LineOuter,
            $"{name}: the frame inverts the screen under the image-12 mask, with no time input");
        Require(map.GetGlobalRect() == mapRect, $"{name}: selecting moves nothing on screen");
        using var selected = await Shot(name + "-selected");

        // Nothing changes over time: no timer, no blink. Sampled for an army and a town at the
        // original's 32 px, which keeps a real-window run inside 900 frames.
        using var again = pixels == OriginalSelectionFrame.MaskSize && label is "army" or "own-town"
            ? await Later(map, cell, name) : null;

        if (unselected is null || selected is null)
        {
            return;
        }

        var transform = ScreenTransform(map);
        if (again is not null)
            Require(SameInCell(selected, again, transform, cell), $"{name}: the cell's pixels are identical 1 s apart");
        var inside = Sample(transform, cell, white: true, 16f, 16f);
        var field = Sample(transform, cell, white: true, 16f, 7f);
        var outline = Sample(transform, cell, white: false, 8f, 8f);
        var corner = Sample(transform, cell, white: true, 1.5f, 1.5f);
        var outside = transform * (cell.Position + new Vector2(-cell.Size.X * .25f, cell.Size.Y * .5f));
        foreach (var (what, point) in new[] { ("inside the rhombus", inside), ("inside the rhombus, off the figure", field), ("the tile corner", corner) })
        {
            if (point is not { } p) { Require(false, $"{name}: no clean pixel {what}"); continue; }
            var before = Pixel(unselected, p);
            var after = Pixel(selected, p);
            Require(Inverse(before, after), $"{name}: {what} at {p} is the inverse of the unselected render ({Hex(before)} -> {Hex(after)})");
        }

        if (outline is { } o)
            Require(Pixel(selected, o) == new Color(0, 0, 0, 1) || Max(Pixel(selected, o)) <= 1f / 255f,
                $"{name}: the outline at {o} is black ({Hex(Pixel(selected, o))})");
        else Require(false, $"{name}: no clean outline pixel");
        var (clean, wrong) = Srcerase(unselected, selected, transform, cell);
        Require(clean > 0 && wrong == 0, $"{name}: every one of {clean} clean cell pixels is the SRCERASE of the unselected render ({wrong} differ)");
        Require(Same(Pixel(unselected, outside), Pixel(selected, outside)),
            $"{name}: outside the cell at {outside} is unchanged ({Hex(Pixel(unselected, outside))})");
    }

    private async Task CheckInspectionLifecycle()
    {
        var main = await Open(Scenario());
        var map = main.MapView;
        map.SetDrawnCellPixelsForCheck(32);
        map.CentreOnTile(55, 40);
        var commands = 0;
        map.CommandIssued += _ => commands++;
        var before = main.Session.State;
        Click(map, 55, 40);
        await Frames();
        Require(map.SelectionFrameRectForCheck is { } city && Near(city, CellRect(map, 55, 40)),
            "inspection lifecycle: real foreign-town click draws its frame");
        Click(map, 56, 40);
        await Frames();
        Require(commands == 0 && main.Session.State == before && main.CommandStrip.Selection == UnitStripSelection.None,
            "inspection lifecycle: a later terrain click cannot move or order the foreign subject");
        map.ClearSelection();
        await Frames();
        Require(map.SelectionFrameRectForCheck is null, "inspection lifecycle: Cancel clears the inspected frame");
        Click(map, 65, 44);
        Click(map, 50, 44);
        await Frames();
        Require(map.SelectedStrategicMarkerForCheck == "a" && main.CommandStrip.Selection == UnitStripSelection.OwnArmy
            && map.SelectionFrameRectForCheck is { } own && Near(own, CellRect(map, 50, 44)),
            "inspection lifecycle: owned selection replaces the foreign frame and retains its orders");
        map.ClearSelection();
        Click(map, 70, 47);
        await Frames();
        SettingsScreen.SetMapStyleForCheck(MapStyles.Painted);
        map.Refresh();
        await Frames();
        Require(map.SelectionFrameRectForCheck is null && map.SelectedStrategicMarkerForCheck is null
            && main.CommandStrip.Selection == UnitStripSelection.None,
            "inspection lifecycle: Painted retains its existing no-foreign-feedback policy");
        SettingsScreen.SetMapStyleForCheck(MapStyles.Tiles);
        map.Refresh();
        await Frames();
        Require(map.SelectionFrameRectForCheck is { } fleet && Near(fleet, CellRect(map, 70, 47)),
            "inspection lifecycle: switching back restores the current Classic inspection frame");
        Require(commands == 0 && main.Session.State == before, "inspection lifecycle: no campaign/RNG mutation");
    }

    private async Task<Image?> Later(GameMapView map, Rect2 cell, string name)
    {
        await ToSignal(GetTree().CreateTimer(1.0), SceneTreeTimer.SignalName.Timeout);
        await Frames();
        Require(map.SelectionFrameRectForCheck is { } later && Near(later, cell),
            $"{name}: the frame is still there, unchanged, 1 s later");
        return await Shot(name + "-selected-later");
    }

    private async Task CheckTravel()
    {
        var main = await Open(Scenario());
        var map = main.MapView;
        map.SetDrawnCellPixelsForCheck(32);
        map.CentreOnTile(50, 44);
        Click(map, 50, 44);
        await Frames();
        Require(map.SelectedStrategicMarkerForCheck == "a" && map.SelectionFrameRectForCheck is { } start
            && Near(start, CellRect(map, 50, 44)), "travel: the selected army is framed before it moves");
        main.SubmitForCheck("move a 53 44");
        main.StrategicTravel.Paused = true;
        Require(main.StrategicTravel.IsActive, "travel: the move plays back");
        main.StrategicTravel.Paused = false; main.StrategicTravel.Advance(.06); main.StrategicTravel.Paused = true;
        map.Refresh();
        await Frames();
        var position = main.StrategicTravel.Position(IC2.Engine.Movement.StrategicMarkerKind.Army, "a");
        Require(position is { X: > 50 and < 51 }, $"travel: the marker is between cells ({position})");
        if (position is { } p)
        {
            var tile = CellRect(map, 50, 44).Size.X;
            var expected = new Rect2(CellRect(map, 50, 44).Position + new Vector2((float)(p.X - 50), (float)(p.Y - 44)) * tile,
                Vector2.One * tile);
            Require(map.SelectedStrategicMarkerForCheck == "a" && map.SelectionFrameRectForCheck is { } moving && Near(moving, expected),
                $"travel: the frame follows the travelling marker ({map.SelectionFrameRectForCheck} vs {expected})");
            Require(map.StrategicDrawBoundsForCheck.TryGetValue("a", out var art) && expected.Encloses(art)
                && map.StrategicFeedbackBoundsForCheck.TryGetValue("a", out var feedback) && Near(feedback, expected),
                "travel: the marker and its frame stay inside the one-tile footprint");
            using var image = await Shot("classic-travel-mid");
            if (image is not null)
            {
                var transform = ScreenTransform(map);
                if (Sample(transform, expected, white: false, 8f, 8f) is { } o)
                    Require(Max(Pixel(image, o)) <= 1f / 255f, $"travel: the moving frame's outline at {o} is black");
                else Require(false, "travel: no clean outline pixel");
            }
        }

        main.StrategicTravel.Skip(); main.StrategicTravel.Skip();
        await Frames();
        var army = main.Session.State.ArmyById("a")!;
        Require(map.SelectedStrategicMarkerForCheck != "a"
            || (map.SelectionFrameRectForCheck is { } end && Near(end, CellRect(map, army.X, army.Y))),
            "travel: after arrival the frame is on the army's cell, or gone with the selection");
    }

    private async Task CheckImproved()
    {
        foreach (var (label, kind, id, x, y) in new[]
        {
            ("army", MapEntityKind.Army, "a", 50, 44),
            ("own-town", MapEntityKind.City, "arx", 45, 40),
        })
        {
            var name = $"improved-{label}";
            var main = await Open(Scenario());
            var map = main.MapView;
            map.SetDrawnCellPixelsForCheck(32);
            map.CentreOnTile(x, y);
            main.SelectArmyForCheck("n");
            await Frames();
            Require(map.SelectionFrameRectForCheck is null, $"{name}: no original frame on the neighbour");
            using var unselected = await Shot(name + "-unselected");
            Click(map, x, y);
            await Frames();
            Require(map.SelectionFrameRectForCheck is null, $"{name}: the improved look draws no original frame");
            if (kind == MapEntityKind.Army)
            {
                var art = map.StrategicDrawBoundsForCheck[id];
                var bounds = new StrategicMarkerBounds(art.Position.X, art.Position.Y, art.Size.X);
                var outline = StrategicMarkerGeometry.Outline(bounds, true);
                var stroke = StrategicMarkerGeometry.StrokeWidth(bounds.Size, true);
                var expected = new Rect2(outline.X, outline.Y, outline.Size, outline.Size).Grow(stroke / 2);
                Require(map.SelectedStrategicMarkerForCheck == id && Near(map.StrategicFeedbackBoundsForCheck[id], expected),
                    $"{name}: T196's selected outline is drawn as before");
            }

            using var selected = await Shot(name + "-selected");
            if (unselected is null || selected is null) continue;
            var transform = ScreenTransform(map);
            var cell = CellRect(map, x, y);
            var centre = (transform * cell.GetCenter()).Floor();
            Require(!Inverse(Pixel(unselected, centre), Pixel(selected, centre)),
                $"{name}: the cell is not inverted ({Hex(Pixel(unselected, centre))} -> {Hex(Pixel(selected, centre))})");
            var changed = 0;
            var screen = new Rect2(transform * cell.Position, transform.BasisXform(cell.Size));
            for (var py = (int)screen.Position.Y; py < (int)screen.End.Y; py++)
                for (var px = (int)screen.Position.X; px < (int)screen.End.X; px++)
                    if (!Same(Pixel(unselected, new Vector2(px, py)), Pixel(selected, new Vector2(px, py)))) changed++;
            Require(changed > 0, $"{name}: T196's feedback still changes the selected cell ({changed} px)");
        }
    }

    // ---- pixels ----

    private async Task<Image?> Shot(string name)
    {
        if (!_window) return null;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        if (_directory is not null) image.SavePng(Path.Combine(_directory, name + ".png"));
        return image;
    }

    private Transform2D ScreenTransform(GameMapView map) =>
        GetViewport().GetFinalTransform() * map.GetGlobalTransformWithCanvas();

    /// <summary>
    /// The screen pixel nearest to mask point (<paramref name="mx"/>, <paramref name="my"/>) of the wanted
    /// colour, white (inverted) or black (the outline), whose four screen neighbours have that colour
    /// too: a window's stretch scale and pixel snapping may move the drawn mask by a pixel.
    /// </summary>
    private static Vector2? Sample(Transform2D transform, Rect2 cell, bool white, float mx, float my)
    {
        var inverse = transform.AffineInverse();
        var screen = new Rect2(transform * cell.Position, transform.BasisXform(cell.Size));
        Vector2? best = null;
        var bestDistance = float.MaxValue;
        for (var py = (int)screen.Position.Y; py <= (int)screen.End.Y; py++)
            for (var px = (int)screen.Position.X; px <= (int)screen.End.X; px++)
            {
                bool? Outline(float sx, float sy)
                {
                    var local = inverse * new Vector2(sx + .5f, sy + .5f);
                    var u = (local.X - cell.Position.X) / cell.Size.X;
                    var v = (local.Y - cell.Position.Y) / cell.Size.Y;
                    return u is < 0 or >= 1 || v is < 0 or >= 1 ? null : OriginalSelectionFrame.IsOutlineAt(u, v);
                }

                var wanted = !white;
                if (Outline(px, py) != wanted || Outline(px - 1, py) != wanted || Outline(px + 1, py) != wanted
                    || Outline(px, py - 1) != wanted || Outline(px, py + 1) != wanted) continue;
                var centre = inverse * new Vector2(px + .5f, py + .5f);
                var mu = (centre.X - cell.Position.X) / cell.Size.X * OriginalSelectionFrame.MaskSize;
                var mv = (centre.Y - cell.Position.Y) / cell.Size.Y * OriginalSelectionFrame.MaskSize;
                var distance = new Vector2(mu - mx, mv - my).LengthSquared();
                if (distance < bestDistance) { bestDistance = distance; best = new Vector2(px, py); }
            }

        return best;
    }

    /// <summary>Every cell pixel whose mask colour is unambiguous (its four neighbours agree): black on the
    /// outline, the unselected render inverted elsewhere. Returns how many were compared and how many differ.</summary>
    private static (int Clean, int Wrong) Srcerase(Image unselected, Image selected, Transform2D transform, Rect2 cell)
    {
        var inverse = transform.AffineInverse();
        bool? Outline(int sx, int sy)
        {
            var local = inverse * new Vector2(sx + .5f, sy + .5f);
            var u = (local.X - cell.Position.X) / cell.Size.X;
            var v = (local.Y - cell.Position.Y) / cell.Size.Y;
            return u is < 0 or >= 1 || v is < 0 or >= 1 ? null : OriginalSelectionFrame.IsOutlineAt(u, v);
        }

        var screen = new Rect2(transform * cell.Position, transform.BasisXform(cell.Size));
        var (clean, wrong) = (0, 0);
        for (var py = (int)screen.Position.Y; py <= (int)screen.End.Y; py++)
            for (var px = (int)screen.Position.X; px <= (int)screen.End.X; px++)
            {
                var outline = Outline(px, py);
                if (outline is null || Outline(px - 1, py) != outline || Outline(px + 1, py) != outline
                    || Outline(px, py - 1) != outline || Outline(px, py + 1) != outline) continue;
                clean++;
                var before = Pixel(unselected, new Vector2(px, py));
                var after = Pixel(selected, new Vector2(px, py));
                if (outline.Value ? Max(after) > Tolerance : !Inverse(before, after)) wrong++;
            }

        return (clean, wrong);
    }

    private static bool SameInCell(Image a, Image b, Transform2D transform, Rect2 cell)
    {
        var screen = new Rect2(transform * cell.Position, transform.BasisXform(cell.Size)).Grow(2);
        for (var py = (int)screen.Position.Y; py < (int)screen.End.Y; py++)
            for (var px = (int)screen.Position.X; px < (int)screen.End.X; px++)
                if (!Same(Pixel(a, new Vector2(px, py)), Pixel(b, new Vector2(px, py)))) return false;
        return true;
    }

    private static Color Pixel(Image image, Vector2 point) => image.GetPixel((int)point.X, (int)point.Y);
    private static float Max(Color c) => Mathf.Max(c.R, Mathf.Max(c.G, c.B));
    private const float Tolerance = 1.5f / 255f;
    private static bool Same(Color a, Color b) =>
        Mathf.Abs(a.R - b.R) <= Tolerance && Mathf.Abs(a.G - b.G) <= Tolerance && Mathf.Abs(a.B - b.B) <= Tolerance;
    private static bool Inverse(Color before, Color after) =>
        Same(new Color(1 - before.R, 1 - before.G, 1 - before.B), after);
    private static string Hex(Color c) => "#" + c.ToHtml(false);

    // ---- scene ----

    private static Rect2 CellRect(GameMapView map, int x, int y)
    {
        var centre = map.TileCenterForCheck(x, y);
        var tile = map.TileCenterForCheck(x + 1, y).X - centre.X;
        return new Rect2(centre - (Vector2.One * tile / 2f), Vector2.One * tile);
    }

    private static bool Near(Rect2 a, Rect2 b) =>
        a.Position.DistanceTo(b.Position) < .01f && a.Size.DistanceTo(b.Size) < .01f;

    private static void Click(GameMapView map, int x, int y)
    {
        var position = map.TileCenterForCheck(x, y);
        map._GuiInput(new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left, Pressed = true });
        map._GuiInput(new InputEventMouseButton { Position = position, ButtonIndex = MouseButton.Left, Pressed = false });
    }

    private async Task<MainGameScreen> Open(GameSession session)
    {
        DisposeMain();
        var main = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
        main.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(main);
        _main = main;
        await Frames();
        return main;
    }

    private void DisposeMain()
    {
        if (_main is null) return;
        RemoveChild(_main);
        _main.QueueFree();
        _main = null;
    }

    /// <summary>Land above row 46 and calm sea below; north's army, neighbour army, fleet and town (arx), south's towns (meridia, portus).</summary>
    private static GameSession Scenario()
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        var plain = toy.World.TileTypes.First(t => t.Id == "plain").Code;
        var sea = toy.World.TileTypes.First(t => t.Id == "sea_calm").Code;
        const int width = 128, height = 96;
        var world = toy.World with
        {
            Width = width, Height = height,
            Terrain = new(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(plain, width * 46), new TerrainRun(sea, width * (height - 46)))),
        };
        var state = GameStateFactory.CreateInitial(world, toy.Ruleset, toy.Scenario);
        var army = state.Armies.First(a => a.Nation == "north") with { X = 50, Y = 44, Moves = 6, AboardFleetId = null, CoveredTileCode = plain };
        var places = new Dictionary<string, (int X, int Y)> { ["arx"] = (45, 40), ["meridia"] = (55, 40), ["portus"] = (35, 40) };
        state = state with
        {
            Armies = ValueList.Of(army with { Id = "a" }, army with { Id = "n", X = 40 },
                army with { Id = "enemy-army", Nation = "south", X = 65 }),
            Fleets = ValueList.Of(new FleetState("f", "north", 60, 47, 6, 30, 100, 100, 100, null, null, null, sea),
                new FleetState("enemy-fleet", "south", 70, 47, 6, 30, 100, 100, 100, null, null, null, sea)),
            Nations = ValueList.From(state.Nations.Select(n => n with { Control = SeatControl.Human })),
            Cities = ValueList.From(state.Cities.Select(c => places.TryGetValue(c.Id, out var p)
                ? c with { X = p.X, Y = p.Y, Owner = c.Id == "portus" ? "south" : c.Owner } : c)),
        };
        return new(world, toy.Ruleset, toy.Scenario,
            new SaveGame(state.SchemaVersion, "selection-frame", "selection-frame", toy.Scenario.Id, world.Id, toy.Ruleset.Id, state));
    }

    private async Task Frames()
    {
        for (var i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private void Require(bool condition, string description)
    {
        if (!condition) _ok = false;
        GD.Print($"SelectionFrameCheck: {(condition ? "PASS" : "FAIL")} {description}");
    }
}
