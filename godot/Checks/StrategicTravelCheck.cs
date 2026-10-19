using Godot;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Movement;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;
using TravelKind = IC2.Engine.Movement.StrategicMarkerKind;

namespace IC2.Slice.Checks;

/// <summary>Actual controls, drawing, dispatch and overlay routing on public synthetic scenarios.
/// T196 windowed modes (after import; never headless): set IC2_SELECTION_MODE=baseline
/// (record visibility) or pixels/final (assert visibility), IC2_TRAVEL_CAPTURE_DIR to an ignored
/// evidence directory, IC2_TRAVEL_HEAD to exact git HEAD; then run
/// godot --path godot res://Checks/StrategicTravelCheck.tscn --quit-after 900.
/// Success: exactly "StrategicTravelCheck: exiting with code 0." and no FAIL/ERROR/FATAL.
/// Production pixels failing the bounded .12 channel-difference mask assertion fail pixels/final.
/// Baseline mode retains all semantic/footprint assertions but logs visibility failures for reproduction.</summary>
public partial class StrategicTravelCheck : Node
{
    private bool _ok = true;
    private MainGameScreen? _main;
    private readonly List<(string File, string Caption)> _captures = new();
    private readonly HashSet<string> _pacingPacks = new();
    private readonly string? _directory = System.Environment.GetEnvironmentVariable("IC2_TRAVEL_CAPTURE_DIR");
    private string _pack = "";
    // Named T196 real-window mode: baseline records failures without changing production;
    // pixels/final assert the same rendered comparison. Headless execution is rejected.
    private readonly string? _selectionMode = System.Environment.GetEnvironmentVariable("IC2_SELECTION_MODE");

    public override async void _Ready()
    {
        var pack = SettingsScreen.SelectedPackId;
        var sound = SettingsScreen.SoundEnabled;
        var shot = SettingsScreen.BattleShotDelaySeconds;
        var attack = SettingsScreen.BattleAttackDelaySeconds;
        try
        {
            if ((!string.IsNullOrWhiteSpace(_directory) || _selectionMode is not null) && DisplayServer.GetName() == "headless")
                throw new InvalidOperationException("Window evidence requires the real windowed display driver.");
            SettingsScreen.SetSoundEnabledForCheck(false);
            foreach (var selected in new[] { "authored", "placeholder" })
            {
                _pack = selected;
                typeof(SettingsScreen).GetProperty(nameof(SettingsScreen.SelectedPackId))!.SetValue(null, selected);
                if (_selectionMode is not null)
                {
                    await CheckSelectionFeedback();
                    continue;
                }
                foreach (var fleet in new[] { false, true })
                foreach (var selection in new[] { false, true })
                    await CheckTravel(fleet, selection);
                await CheckProjectionAndLifecycle();
                await CheckBoundaryReconciliation();
                await CheckSelectionAtActionBoundary();
                await CheckBattleBarrier();
                await CheckAiLap(false);
                await CheckAiLap(true);
                await CheckSiegeArrival();
                await CheckFallHandoffNoticeOrder();
                await CheckPeaceOrder();
                await CheckOffscreenAndClosure();
                if (!string.IsNullOrWhiteSpace(_directory)) await CapturePacing();
            }
        }
        catch (Exception ex) { Require(false, ex.ToString()); }
        finally
        {
            DisposeMain();
            typeof(SettingsScreen).GetProperty(nameof(SettingsScreen.SelectedPackId))!.SetValue(null, pack);
            SettingsScreen.SetSoundEnabledForCheck(sound);
            SettingsScreen.SetBattlePauses(shot, attack);
            WriteGallery();
            GD.Print("StrategicTravelCheck: settings restored.");
        }
        await Frames();
        // As in merged TacticalAudioCheck, finalize retired wrappers while Godot is still alive.
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
        GD.Print($"StrategicTravelCheck: exiting with code {(_ok ? 0 : 1)}.");
        GetTree().Quit(_ok ? 0 : 1);
    }

    // [designed] Acceptance measurement, not an original-game colour specification:
    // compare the SAME viewport/owner/entity/pointer with only selection changed. Sample
    // pixel centres inside its actual art rectangle (bounded by one projected tile).
    // A channel difference >= .12 in at least max(1, floor(width/2)) pixels rejects a
    // missing/indistinguishable/occluded selection without prescribing its hue. Human
    // assessment remains required; this metric alone does not establish clarity.
    private async Task CheckSelectionFeedback()
    {
        foreach (var fleet in new[] { false, true })
        foreach (var light in new[] { false, true })
        foreach (var overlap in new[] { false, true })
        foreach (var zoom in new[] { "fit", "minimum", "maximum" })
        {
            var prefix = $"selection-{_pack}-{(fleet ? "fleet" : "army")}-{(light ? "light" : "dark")}-{(overlap ? "coincident" : "neighbour")}-{zoom}";
            var filter = System.Environment.GetEnvironmentVariable("IC2_SELECTION_FILTER");
            if (filter is not null && !prefix.Contains(filter, StringComparison.Ordinal)) continue;
            var main = await Open(FeedbackSession(fleet, light, overlap));
            var map = main.MapView;
            if (zoom != "fit") map.SetDrawnCellPixelsForCheck(zoom == "maximum" ? 72 : 3);
            map.CentreOnTile(50, 45);
            var id = fleet ? "f" : "a";
            // Actual pointer chooses army on coincidences, even when the fleet is selected.
            map._GuiInput(new InputEventMouseMotion { Position = map.TileCenterForCheck(50, 45) });
            // Keep the real strip laid out in both frames: this target is unselected,
            // while its adjacent army is selected outside the sampling mask. Otherwise
            // strip visibility changes raster phase and can fake a difference at 3px.
            main.SelectArmyForCheck("neighbour");
            await Frames();
            map.CentreOnTile(50, 45);
            map._GuiInput(new InputEventMouseMotion { Position = map.TileCenterForCheck(50, 45) });
            using var unselected = await FeedbackImage(prefix + "-unselected");
            var unselectedRect = FeedbackPixelRect(map, id, unselected);
            var before = main.Session.State;
            map.ClearSelection();
            if (fleet && overlap) main.SelectFleetForCheck(id); // ordinary picking intentionally army-first
            else Click(map, new(50, 45), MouseButton.Left);
            await Frames(); // command-strip layout changes the map's screen-space origin
            map.CentreOnTile(50, 45);
            map._GuiInput(new InputEventMouseMotion { Position = map.TileCenterForCheck(50, 45) });
            using var selected = await FeedbackImage(prefix + "-selected");
            Require(map.SelectedStrategicMarkerForCheck == id
                && main.ContextPanel.SubjectForCheck == (fleet ? "Fleet:f" : "Army:a")
                && main.CommandStrip.Selection == (fleet ? UnitStripSelection.OwnFleet : UnitStripSelection.OwnArmy),
                prefix + " actual selection, panel subject and command strip agree");
            var rect = map.StrategicDrawBoundsForCheck[id];
            var pixelRect = FeedbackPixelRect(map, id, selected);
            Require(unselectedRect.Position.DistanceTo(pixelRect.Position) < .01f
                && unselectedRect.Size.DistanceTo(pixelRect.Size) < .01f,
                prefix + " pixel comparison keeps identical viewport raster phase");
            var origin = pixelRect.Position;
            var changed = 0;
            var samples = 0;
            for (var y = (int)Math.Ceiling(origin.Y - .5f); y + .5f < origin.Y + pixelRect.Size.Y; y++)
            for (var x = (int)Math.Ceiling(origin.X - .5f); x + .5f < origin.X + pixelRect.Size.X; x++)
            {
                if (x < 0 || y < 0 || x >= selected.GetWidth() || y >= selected.GetHeight()) continue;
                samples++;
                var ux = (int)Math.Floor(unselectedRect.Position.X + (x + .5f - origin.X) * unselectedRect.Size.X / pixelRect.Size.X);
                var uy = (int)Math.Floor(unselectedRect.Position.Y + (y + .5f - origin.Y) * unselectedRect.Size.Y / pixelRect.Size.Y);
                var a = unselected.GetPixel(ux, uy); var b = selected.GetPixel(x, y);
                if (Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B))) >= .12f) changed++;
            }
            var required = Math.Max(1, (int)Math.Floor(pixelRect.Size.X / 2));
            var visible = changed >= required;
            GD.Print($"StrategicTravelCheck: BASELINE {prefix} visible={visible} changed={changed}/{samples} required={required} tile={TilePixels(main)} owner={(light ? "#F0E6BC/#182535" : "#182535/#F0E6BC")}");
            if (_selectionMode != "baseline") Require(visible, prefix + " rendered selected feedback remains distinguishable inside entity rectangle");
            if (!string.IsNullOrWhiteSpace(_directory)) File.AppendAllText(Path.Combine(_directory, "selection-pixels.csv"),
                $"{prefix},{samples},{changed},{required},{visible}\n");
            Require(rect.Size.X <= TilePixels(main) + .01f && rect.Size.Y <= TilePixels(main) + .01f,
                prefix + " actual marker remains one projected tile");
            // Right-click a different visible neighbour through the real event/panel path.
            Click(map, new(51, 45), MouseButton.Right);
            await Frames();
            Require(map.SelectedStrategicMarkerForCheck == id && main.ContextPanel.SubjectForCheck == "UnitList:neighbour"
                && main.CommandStrip.Selection == (fleet ? UnitStripSelection.OwnFleet : UnitStripSelection.OwnArmy)
                && main.Session.State == before, prefix + " right-click list preserves selected identity, armed strip and authoritative state");
            if (zoom == "maximum")
            {
                using var listed = await FeedbackImage(prefix + "-right-list");
            }
            if (overlap)
            {
                Click(map, new(50, 45), MouseButton.Right);
                Require(map.LastClickContextForCheck?.Target is { Kind: MapEntityKind.Army, Id: "a" },
                    prefix + " same-tile pointer winner remains army before fleet and city");
            }
            map.ClearSelection();
            Require(map.SelectedStrategicMarkerForCheck is null && main.CommandStrip.Selection == UnitStripSelection.None,
                prefix + " deselection clears map and command strip");
        }
    }

    private Rect2 FeedbackPixelRect(GameMapView map, string id, Image image)
    {
        var rect = map.StrategicDrawBoundsForCheck[id];
        var transform = GetViewport().GetFinalTransform() * map.GetGlobalTransformWithCanvas();
        var origin = transform * rect.Position;
        var end = transform * rect.End;
        GD.Print($"StrategicTravelCheck: PIXEL-MASK {id} image={image.GetWidth()}x{image.GetHeight()} viewport={GetViewport().GetVisibleRect().Size} rect={origin}/{end - origin}");
        return new Rect2(origin, end - origin);
    }

    private async Task<Image> FeedbackImage(string name)
    {
        await Frames();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        if (!string.IsNullOrWhiteSpace(_directory))
        {
            Directory.CreateDirectory(_directory);
            Require(image.SavePng(Path.Combine(_directory, name + ".png")) == Error.Ok, "real window capture " + name);
            _captures.Add((name + ".png", "T196 " + _selectionMode + ": " + name + "; same entity, terrain, owner and pointer."));
        }
        return image;
    }

    private static GameSession FeedbackSession(bool fleet, bool light, bool overlap)
    {
        var initial = MovementSession(fleet, 10);
        var terrain = initial.World.TileTypes.First(t => t.Id == (fleet ? "sea_calm" : "plain")).Code;
        var world = initial.World with { Width = 512, Height = 256,
            Terrain = new(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(terrain, 512 * 256))), Nations = ValueList.From(initial.World.Nations.Select(n => n with
            { ColorHex = light ? "#F0E6BC" : "#182535", GlyphColorHex = light ? "#182535" : "#F0E6BC" })) };
        var state = initial.State;
        var template = GameStateFactory.CreateInitial(world, initial.Ruleset, initial.Scenario).Armies[0];
        var army = template with { Id = "a", X = 50, Y = 45, Nation = "north", Moves = 10, AboardFleetId = null };
        var neighbour = army with { Id = "neighbour", X = 51 };
        var armies = fleet && !overlap ? ValueList.Of(neighbour) : ValueList.Of(army, neighbour);
        var fleets = state.Fleets;
        if (!fleet && overlap) fleets = ValueList.Of(new FleetState("f", "north", 50, 45, 10, 30, 100, 100, 100, null, null, null, null));
        state = state with { Armies = armies, Fleets = fleets,
            Cities = ValueList.From(state.Cities.Select((c, i) => c with { X = overlap && i == 0 ? 50 : 40 + i * 10, Y = overlap && i == 0 ? 45 : 40 })) };
        return new(world, initial.Ruleset, initial.Scenario,
            new SaveGame(state.SchemaVersion, "selection-feedback", "selection-feedback", state.ScenarioId, state.WorldId, state.RulesetId, state));
    }

    private async Task CheckTravel(bool fleet, bool selected)
    {
        var session = MovementSession(fleet, 2);
        var main = await Open(session);
        Require(main.MapView.LoadedPackIdForCheck == _pack, "real main map loads requested " + _pack + " pack");
        var id = fleet ? "f" : "a";
        var origin = new GridPoint(50, 45);
        main.MapView.CentreOnTile(origin.X, origin.Y);
        if (selected) Click(main.MapView, origin, MouseButton.Left);
        main.AreaMapView.ToggleHighlight(fleet ? AreaMapHighlightKind.Fleets : AreaMapHighlightKind.Armies);
        var initial = session.State;
        var count = 0;
        main.CommandIssued += _ => count++;
        main.SubmitForCheck(fleet ? "move-fleet f 57 48" : "move a 57 48");
        main.StrategicTravel.Paused = true;
        Require(main.StrategicTravel.IsActive && !main.StrategicTravel.CommandsAllowed, "real order starts common input lock");
        Require(main.MapView.DisplayState == main.ContextPanel.DisplayState && main.MapView.DisplayState == main.AreaMapView.DisplayState,
            "all controls share exactly the same boundary projection");
        Require(Position(main.DisplayState, fleet, id) == origin && Position(session.State, fleet, id) == new GridPoint(52, 46),
            "real truncated accepted route has no final destination ghost");
        var prefix = $"{_pack}-{(fleet ? "fleet" : "army")}-{(selected ? "selected" : "unselected")}";
        await Capture(prefix + "-start", "Partial stop: source marker and information remain at origin; authoritative destination is later.");
        var final = session.State;
        foreach (var command in new[] { "move a 0 0", "end", "disband a", "status" }) main.SubmitForCheck(command);
        foreach (var command in new[] { "game.end_turn", "unit_map.army_join", "unit_map.fleet_join", "strategy.taxation", "strategy.recruit_unit", "nations.carthage" })
            main.CommandTable.TryInvoke(command);
        var oldContext = main.MapView.LastClickContextForCheck;
        Click(main.MapView, new(51, 45), MouseButton.Left);
        Click(main.MapView, new(51, 45), MouseButton.Right);
        main.Toolbar.ButtonFor("game.end_turn")?.EmitSignal(Button.SignalName.Pressed);
        main.MenuBar.PressItemForCheck("game.end_turn");
        if (selected) main.CommandStrip.PressForCheck(fleet ? "unit_map.fleet_join" : "unit_map.army_join");
        typeof(MainGameScreen).GetMethod("SubmitFromDialog", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(main, new object[] { fleet ? "move-fleet f 54 45" : "move a 54 45" });
        Require(session.State == final && count == 0 && main.MapView.LastClickContextForCheck == oldContext,
            "buttons/menu/toolbar/raw/End and both map buttons reject before dispatch, with no queued command");
        Require(main.LastMessageForCheck.Contains("locked", StringComparison.Ordinal), "lock provides readable feedback");
        main.ContextPanel.ShowUnitList(fleet ? MapEntityKind.Fleet : MapEntityKind.Army, id);
        Advance(main, .06);
        await Frames();
        var kind = fleet ? TravelKind.Fleet : TravelKind.Army;
        var interpolated = main.StrategicTravel.Position(kind, id);
        Require(interpolated is { X: > 50 and < 51 }, "production centre interpolates between adjacent accepted cells");
        Require(main.MapView.StrategicDrawBoundsForCheck.TryGetValue(id, out var bounds)
            && Mathf.Abs(bounds.GetCenter().X - main.MapView.TileCenterForCheck(50, 45).X
                - (float)(interpolated!.Value.X - 50) * TilePixels(main)) < .01f,
            "actual marker drawing translates existing geometry by fractional centre");
        Require(Position(main.ContextPanel.DisplayState, fleet, id) == origin
            && main.AreaMapView.LastDrawnHighlightTilesForCheck.Contains((50, 45))
            && !main.AreaMapView.LastDrawnHighlightTilesForCheck.Contains((52, 46)),
            "forced panel/minimap draw keeps last entered cell, no destination highlight");
        Require(Labels(main.ContextPanel).Any(t => t.Contains("Position: (50, 45)", StringComparison.Ordinal)),
            "actual laid-out unit list reports origin at fractional midpoint");
        foreach (var zoom in new[] { "fit", "minimum", "maximum" })
        {
            if (zoom != "fit") main.MapView.SetDrawnCellPixelsForCheck(zoom == "maximum" ? 72 : 3);
            main.MapView.CentreOnTile(50, 45);
            main.MapView.Refresh(); main.ContextPanel.Refresh(); main.AreaMapView.Refresh();
            await Frames();
            Require(main.MapView.StrategicDrawBoundsForCheck[id].Size.X <= TilePixels(main) + .01f,
                "moving board width/height remains one projected tile at " + zoom);
            await Capture(prefix + "-mid-" + zoom, "Fractional centre, locked orders, actual panel/minimap; " + zoom + " practical zoom.");
        }
        main.MapView.CentreOnTile(50 + (int)Math.Floor(main.MapView.Size.X / TilePixels(main) / 2), 45);
        await Frames();
        Require(main.MapView.ClipContents, "fractional moving art clips at actual pan edge");
        await Capture(prefix + "-mid-pan-edge", "Moving marker straddles actual viewport edge; pan/zoom stay available under input lock.");
        main.MapView.CentreOnTile(50, 45);
        Advance(main, .06);
        await Frames();
        Require(Position(main.DisplayState, fleet, id) == new GridPoint(51, 45), "all controls advance at exact first paid cell");
        Require(Labels(main.ContextPanel).Any(t => t.Contains("Position: (51, 45)", StringComparison.Ordinal)),
            "actual list refresh advances discrete cell");
        if (selected) Require(main.MapView.SelectedStrategicMarkerForCheck == id, "selected identity survives exhausted authoritative destination until arrival");
        main.StrategicTravel.Skip(); main.StrategicTravel.Skip();
        await Frames();
        Require(!main.StrategicTravel.IsActive && main.DisplayState == session.State && count == 1,
            "Skip drains exactly once and then reconciles all controls to live state");
        Require(!selected || main.MapView.SelectedStrategicMarkerForCheck is null, "exhausted selection clears visibly on completion");
        Require(main.MapView.DisplayState == final && main.ContextPanel.DisplayState == final && main.AreaMapView.DisplayState == final,
            "map/panel/minimap switch to final authoritative state together");
        await Capture(prefix + "-arrival", "Partial-stop arrival: all views agree; exhausted selection and lock clear.");
        Require(session.State == final && initial.RandomSeed != final.RandomSeed, "playback adds no further authoritative changes");
    }

    private async Task CheckProjectionAndLifecycle()
    {
        var session = MovementSession(false, 10);
        var main = await Open(session);
        main.MapView.CentreOnTile(50, 45);
        main.SubmitForCheck("move a 53 45"); main.StrategicTravel.Paused = true;
        var source = main.StrategicTravel.CurrentStep!;
        main.StrategicTravel.Skip();
        var southIndex = source.Before.TurnOrder.ToList().IndexOf("south");
        var before = source.Before with
        {
            ActiveSeatIndex = southIndex,
            Armies = ValueList.Of(source.Before.Armies[0] with { Nation = "south", Money = 12345 }),
        };
        var after = source.After with { ActiveSeatIndex = southIndex, Armies = ValueList<ArmyState>.Empty };
        var foreign = source with { Before = before, After = after,
            Routes = ValueList.From(source.Routes.Select(r => r with { OwnerId = "south" })) };
        var synthetic = new SessionOutput(Array.Empty<string>(), false)
        { StrategicTimeline = new(10000, ValueList.Of(foreign)) };
        main.ContextPanel.ShowArmy("a");
        main.PresentOutputForCheck(synthetic); main.StrategicTravel.Paused = true;
        main.ContextPanel.Refresh(); main.AreaMapView.Refresh(); main.MapView.Refresh();
        await Frames();
        Require(main.ContextPanel.DisplayState.ActiveNationId == "north" && main.StrategicTravel.CurrentStep!.Before.ActiveNationId == "south",
            "foreign AI simulation identity never becomes information viewer");
        Require(Labels(main.ContextPanel).Any(t => t.Contains("withheld", StringComparison.OrdinalIgnoreCase))
            && Labels(main.ContextPanel).All(t => !t.Contains("12345", StringComparison.Ordinal)),
            "actual foreign army panel withholds private funds during foreign simulation");
        Require(main.MapView.StrategicDrawBoundsForCheck.ContainsKey("a"), "removed final entity remains visible in prior-owner snapshot until arrival");
        await Capture(_pack + "-foreign-ai-prior-owner", "Foreign AI simulation seat with north viewer: prior owner retained, private fields withheld.");
        main.StrategicTravel.Skip();
        Require(main.DisplayState == session.State, "synthetic lifecycle batch releases snapshot on drain");

        // Real carried fleet route; hidden army shadows carrier, never gets a second board.
        main = await Open(MovementSession(true, 10, cargo: true));
        main.MapView.CentreOnTile(50, 45);
        main.SubmitForCheck("move-fleet f 53 45"); main.StrategicTravel.Paused = true;
        Advance(main, .12); await Frames();
        Require(main.DisplayState.ArmyById("cargo") is { X: 51, AboardFleetId: "f" }
            && !main.MapView.StrategicDrawBoundsForCheck.ContainsKey("cargo"), "carried army shadows fleet without duplicate marker");
        await Capture(_pack + "-carried-fleet", "Real carried fleet: cargo remains hidden while following accepted cell.");
        main.StrategicTravel.Fast = true;
        Advance(main, 100); Require(!main.StrategicTravel.IsActive, "Fast/large delta completes real fleet exactly once");
    }

    private async Task CheckBoundaryReconciliation()
    {
        foreach (var mode in new[] { "removal", "capture", "merge", "scatter", "embark", "disembark" })
        {
            var fleet = mode == "disembark";
            var producer = MovementSession(fleet, 10, cargo: fleet);
            var output = producer.Submit(fleet ? "move-fleet f 53 45" : "move a 53 45");
            var source = output.StrategicTimeline.Steps.Single(step => step.Routes.Count > 0);
            var before = source.Before;
            var oldCity = before.Cities[0] with { Owner = "south", X = 45, Y = 40 };
            before = before with { Cities = ValueList.From(before.Cities.Select((c, i) => i == 0 ? oldCity : c)),
                MercenaryPool = ValueList.Of(new MercenaryPoolSlot(0, oldCity.X, oldCity.Y, 0, "light_infantry", 1234, 6)) };
            var afterMove = source.After with { Cities = before.Cities, MercenaryPool = before.MercenaryPool };
            var unit = fleet ? afterMove.ArmyById("cargo")! : afterMove.ArmyById("a")!;
            var final = afterMove with
            {
                Cities = ValueList.From(before.Cities.Select((c, i) => i == 0 ? c with { Owner = "north" } : c)),
                MercenaryPool = ValueList<MercenaryPoolSlot>.Empty,
                Armies = mode switch
                {
                    "removal" => ValueList<ArmyState>.Empty,
                    "capture" => ValueList.Of(unit with { Nation = "south" }),
                    "merge" => ValueList.Of(unit with { Id = "merged" }),
                    "scatter" => ValueList.Of(unit with { X = 60, Y = 48 }),
                    "embark" => ValueList.Of(unit with { AboardFleetId = "carrier", CoveredTileCode = null }),
                    _ => ValueList.Of(unit with { AboardFleetId = null, CoveredTileCode = 2, X = 54 }),
                },
            };
            if (mode == "embark") final = final with { Fleets = ValueList.Of(new FleetState("carrier", "north", 53, 45, 3, 30, 100, 100, 100, null, null, "a", 0)) };
            if (mode == "disembark") final = final with { Fleets = ValueList.Of(final.Fleets[0] with { CarriedArmyId = null }) };
            source = source with { Before = before, After = afterMove };
            var reconciliation = source with { StepId = source.StepId + 1, ActionId = source.ActionId + 1,
                Kind = StrategicTravelStepKind.Reconciliation, Identity = mode, Before = afterMove, After = final,
                Routes = ValueList<StrategicTravelObservation>.Empty };
            var save = new SaveGame(final.SchemaVersion, "boundary", "boundary", final.ScenarioId, final.WorldId, final.RulesetId, final);
            var main = await Open(new GameSession(producer.World, producer.Ruleset, producer.Scenario, save));
            main.MapView.CentreOnTile(50, 45);
            main.AreaMapView.SetHighlight(AreaMapHighlightKind.Cities, true);
            main.AreaMapView.SetHighlight(AreaMapHighlightKind.MercenariesLightInfantry, true);
            main.MapView.SelectStrategicForCheck(fleet ? MapEntityKind.Fleet : MapEntityKind.Army, fleet ? "f" : "a");
            main.ContextPanel.ShowCity(oldCity.Id);
            main.PresentOutputForCheck(new SessionOutput(Array.Empty<string>(), false)
                { StrategicTimeline = new(1, ValueList.Of(source, reconciliation)) });
            main.StrategicTravel.Paused = true;
            main.ContextPanel.Refresh(); main.AreaMapView.Refresh(); main.MapView.Refresh();
            await Frames();
            Require(Labels(main.ContextPanel).Contains("Controlled by: " + before.NationById("south")!.Name),
                mode + ": actual laid-out city controller uses prior boundary");
            Require(main.DisplayState.CityById(oldCity.Id)!.Owner == "south" && main.Session.State.CityById(oldCity.Id)!.Owner == "north",
                mode + ": prior city owner retained while authoritative state is captured");
            Require(!main.AreaMapView.LastDrawnHighlightTilesForCheck.Contains((oldCity.X, oldCity.Y))
                || main.AreaMapView.LastDrawnMercenaryIconsForCheck.ContainsKey((oldCity.X, oldCity.Y)),
                mode + ": city/mercenary highlights use same prior boundary");
            Require(main.AreaMapView.LastDrawnMercenaryIconsForCheck.ContainsKey((oldCity.X, oldCity.Y)),
                mode + ": actual mercenary drawing does not leak final empty pool");
            main.AreaMapView.SetHighlight(AreaMapHighlightKind.MercenariesLightInfantry, false);
            main.AreaMapView.SetFindCityHighlight(oldCity.Id);
            main.AreaMapView.Refresh(); await Frames();
            Require(main.AreaMapView.LastDrawnHighlightTilesForCheck.Contains((oldCity.X, oldCity.Y)), mode + ": actual Find-city highlight uses display state");
            Require(main.MapView.StrategicDrawBoundsForCheck.ContainsKey(fleet ? "f" : "a"), mode + ": moving marker survives later lifecycle change");
            main.AreaMapView.SetHighlight(AreaMapHighlightKind.MercenariesLightInfantry, true);
            main.StrategicTravel.Skip(); await Frames();
            Require(main.MapView.DisplayState == main.Session.State && main.ContextPanel.DisplayState == main.Session.State
                && main.AreaMapView.DisplayState == main.Session.State, mode + ": atomic boundary reconciles all controls together");
            Require(main.AreaMapView.LastDrawnMercenaryIconsForCheck.Count == 0, mode + ": mercenary pool reconciles at arrival");
            Require(mode switch
            {
                "removal" or "merge" or "embark" => !main.MapView.StrategicDrawBoundsForCheck.ContainsKey("a"),
                "capture" => main.DisplayState.ArmyById("a")!.Nation == "south",
                "scatter" => main.DisplayState.ArmyById("a") is { X: 60, Y: 48 },
                _ => main.MapView.StrategicDrawBoundsForCheck.ContainsKey("cargo"),
            }, mode + ": actual final marker/lifecycle matches authoritative boundary");
            if (mode is "removal" or "merge" or "embark")
                Require(main.MapView.SelectedStrategicMarkerForCheck is null, mode + ": missing/hidden selection clears on arrival");
            if (mode == "capture")
            {
                main.SelectArmyForCheck("a");
                Require(!main.CommandStrip.Visible, "captured foreign unit has no own command strip");
            }
        }
    }

    private async Task CheckSelectionAtActionBoundary()
    {
        var seed = MovementSession(false, 1);
        var state = seed.State with { Armies = ValueList.From(seed.State.Armies.Append(
            seed.State.Armies[0] with { Id = "b", X = 60, Moves = 2 })) };
        var producer = new GameSession(seed.World, seed.Ruleset, seed.Scenario,
            new SaveGame(state.SchemaVersion, "selection-chain", "selection-chain", state.ScenarioId, state.WorldId, state.RulesetId, state));
        var first = producer.Submit("move a 51 45");
        var second = producer.Submit("move b 61 45");
        var main = await Open(producer);
        main.MapView.SetDrawnCellPixelsForCheck(32); main.MapView.CentreOnTile(55, 45);
        main.MapView.SelectStrategicForCheck(MapEntityKind.Army, "a");
        main.PresentOutputForCheck(second with { StrategicTimeline = new(second.StrategicTimeline.BatchId,
            ValueList.From(first.StrategicTimeline.Steps.Concat(second.StrategicTimeline.Steps))) });
        main.StrategicTravel.Paused = true;
        Require(main.MapView.SelectedStrategicMarkerForCheck == "a", "chained batch keeps selected identity before its own arrival");
        Advance(main, StrategicTravelPlayback.CandidateCellSeconds);
        Require(main.StrategicTravel.IsActive && main.StrategicTravel.CurrentRoute?.EntityId == "b"
            && main.MapView.SelectedStrategicMarkerForCheck is null,
            "exhausted selection clears at its action boundary while later unrelated route remains locked");
        main.StrategicTravel.Skip();
    }

    private async Task CheckBattleBarrier()
    {
        var session = MovementSession(false, 20);
        var main = await Open(session);
        main.MapView.CentreOnTile(50, 45);
        main.SubmitForCheck("move a 53 45"); main.StrategicTravel.Paused = true;
        Require(main.ActiveOverlay is null, "overlay router stays behind active travel");
        var count = 0;
        main.CommandIssued += _ => count++;
        main.CommandTable.TryInvoke("help.about");
        Require(main.StrategicTravel.IsActive, "read-only help reentrancy does not consume retained completion");
        // Close the actual read-only overlay through its normal Closed event.
        if (main.ActiveOverlay is AboutDialog about) about.Close();
        main.StrategicTravel.Skip(); main.StrategicTravel.Skip();
        Require(count == 1, "retained command completion runs once across read-only callback");

        // Actual tactical attack without a route keeps merged T180/T183 behavior.
        DisposeMain();
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        var world = toy.World with { StartingArmies = ValueList.Of(
            new StartingArmy("north-army-1", "north", 3, 2, 68, 100, 500, 5,
                ValueList.Of(new UnitSlot(0, "light_infantry", 9000, 6, "Foot"))),
            new StartingArmy("south-army-1", "south", 4, 2, 59, 40, 60, 5,
                ValueList.Of(new UnitSlot(0, "heavy_infantry", 600, 6, "Guard")))) };
        main = await Open(new GameSession(world, toy.Ruleset, toy.Scenario) { HumanBattles = HumanBattleMode.Tactical });
        SettingsScreen.SetBattlePauses(.1, .1);
        main.SubmitForCheck("attack-army north-army-1 south-army-1");
        Require(main.ActiveOverlay is TacticalBattleScreen, "unchanged actual tactical attack opens through existing gate");
        if (main.ActiveOverlay is TacticalBattleScreen battle)
        {
            battle.SkipPlayback(); battle.PressToolbarButton("computer_general");
            Require(main.ActiveOverlay is TacticalBattleScreen, "T180 terminal frame owns overlay until replay arrival");
            battle.SkipPlayback();
            Require(main.ActiveOverlay is BattleEndedScreen, "T180 result follows terminal replay once");
        }
        await Capture(_pack + "-tactical-result", "Merged tactical terminal replay remains ahead of its result.");
    }

    private async Task CheckAiLap(bool tactical)
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        StartingArmy Army(string id, string nation, int x, int y, int troops) => new(id, nation, x, y,
            Morale: nation == "north" ? 68 : 59, Money: 100, SupplyTons: 500, Moves: 5,
            Units: ValueList.Of(new UnitSlot(0, nation == "north" ? "light_infantry" : "heavy_infantry", troops, 6, "Foot")));
        var world = toy.World with { StartingArmies = ValueList.Of(
            Army("north-army-1", "north", 2, 3, 400), Army("north-army-2", "north", 6, 4, 400),
            Army("south-army-1", "south", 4, 3, 6000), Army("south-army-2", "south", 6, 5, 6000)) };
        var session = new GameSession(world, toy.Ruleset, toy.Scenario)
        { HumanBattles = tactical ? HumanBattleMode.Tactical : HumanBattleMode.Instant };
        session.Submit("declare-war south");
        var main = await Open(session);
        main.MapView.CentreOnTile(3, 3);
        main.ContextPanel.ShowArmy("south-army-1");
        var completions = 0;
        main.CommandIssued += _ => completions++;
        main.SubmitForCheck("end"); main.StrategicTravel.Paused = true;
        typeof(MainGameScreen).GetMethod("ShowNextOverlay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(main, null);
        Require(main.StrategicTravel.IsActive && main.ActiveOverlay is null,
            "actual AI approach arrives before " + (tactical ? "suspended tactical creation" : "ordinary completed AI result"));
        if (!main.StrategicTravel.IsActive) return;
        Require(main.StrategicTravel.CurrentRoute?.OwnerId == "south" && main.ContextPanel.DisplayState.ActiveNationId == "north",
            "actual computer travel retains human viewer independently from simulation seat");
        await Capture(_pack + (tactical ? "-ai-tactical-approach" : "-ai-instant-approach"),
            "Actual AI approach: associated battle overlay is not created before accepted-cell arrival.");
        var arrivedRoute = main.StrategicTravel.CurrentRoute!;
        var acceptedArrival = arrivedRoute.Destination;
        main.StrategicTravel.Skip();
        if (tactical) Require(main.MapView.DisplayState.ArmyById(arrivedRoute.EntityId) is { } arrived
            && arrived.X == acceptedArrival.X && arrived.Y == acceptedArrival.Y,
            $"suspended AI arrived marker never snaps back to rolled-back replay state ({arrivedRoute.EntityId} expected {acceptedArrival}; held {main.MapView.DisplayState.ArmyById(arrivedRoute.EntityId)?.X},{main.MapView.DisplayState.ArmyById(arrivedRoute.EntityId)?.Y})");
        Require(completions == 1, "AI command completion runs exactly once after travel");
        Require(main.ContextPanel.DisplayState.ActiveNationId == "north", "drained suspended AI lap still uses current human information viewer");
        Require(tactical ? main.ActiveOverlay is TacticalBattleScreen : main.ActiveOverlay is BattleResultScreen,
            "arrival opens exact associated AI battle overlay");
        await Capture(_pack + (tactical ? "-ai-tactical-arrival" : "-ai-instant-arrival"),
            "Arrival barrier released: associated AI battlefield/result now opens.");
        if (!tactical)
        {
            var count = 0;
            while (main.ActiveOverlay is BattleResultScreen result && count++ < 10) result.Close();
            Require(!main.StrategicTravel.IsActive && count > 0, "ordinary completed AI lap drains results without replayed strategic prefix");
            return;
        }
        var ids = new HashSet<string>();
        for (var lap = 0; lap < 4 && main.ActiveOverlay is TacticalBattleScreen battle; lap++)
        {
            battle.SkipPlayback();
            if (battle.DisplayedFrame is { } frame) Require(ids.Add(frame.BattleId), "each suspended AI battlefield opens once with distinct identity");
            battle.PressToolbarButton("computer_general"); battle.SkipPlayback();
            Require(main.ActiveOverlay is BattleEndedScreen, "each tactical terminal replay precedes its result");
            session.Submit("help"); // Replaces mutable Last* while result owns the retained output.
            if (main.ActiveOverlay is BattleEndedScreen ended) ended.Close();
            if (main.StrategicTravel.IsActive)
            {
                Require(main.ActiveOverlay is null && main.DisplayState.ActiveNationId == "north", "resumed AI suffix travels with original human viewer before next battlefield creation");
                main.StrategicTravel.Skip();
            }
        }
        Require(ids.Count >= 2, "actual suspended/resumed multi-battle AI lap retains second opening and suffix");
        main.StrategicTravel.Skip();
        Require(!main.StrategicTravel.IsActive, "multi-battle lap ends without stale strategic batch");
    }

    private async Task CheckSiegeArrival()
    {
        var seed = MovementSession(false, 20);
        var state = seed.State with
        {
            Armies = ValueList.Of(seed.State.Armies[0] with
            { Units = ValueList.Of(new UnitSlot(0, "archers", 400_000, 6, "Siege")) }),
            Cities = ValueList.From(seed.State.Cities.Select((city, i) => i == 1
                ? city with { Id = "target", X = 52, Y = 45, Owner = "south", Allegiance = "south",
                    Loyalty = 0, Garrison = ValueList<UnitSlot>.Empty, FortificationCode = 0 } : city)),
        };
        var producer = new GameSession(seed.World, seed.Ruleset, seed.Scenario,
            new SaveGame(state.SchemaVersion, "siege", "siege", state.ScenarioId, state.WorldId, state.RulesetId, state));
        var march = producer.Submit("move a 51 45");
        var siege = producer.Submit("besiege-city a target");
        Require(siege.Battles.Count == 1 && producer.State.CityById("target")!.Owner == "north",
            "actual siege producer supplies captured city and associated result");
        var main = await Open(producer);
        main.MapView.CentreOnTile(51, 45); main.ContextPanel.ShowCity("target");
        var exact = siege with { StrategicTimeline = new(siege.StrategicTimeline.BatchId,
            ValueList.From(march.StrategicTimeline.Steps.Concat(siege.StrategicTimeline.Steps))) };
        var saved = false;
        var savePath = Path.Combine(GameDataContext.RepositoryRoot, "rendered", "t195-reentrant-save.json");
        main.CommandIssued += _ =>
        {
            if (saved) return;
            saved = true;
            main.SaveAsForCheck(savePath);
        };
        main.PresentOutputForCheck(exact); main.StrategicTravel.Paused = true;
        Require(main.StrategicTravel.IsActive && main.ActiveOverlay is null
            && main.DisplayState.CityById("target")!.Owner == "south",
            "real paid march precedes siege/capture reconciliation and associated result");
        await Capture(_pack + "-siege-approach", "Real paid march before real siege: prior city owner and no result window before arrival.");
        Advance(main, .06); main.ContextPanel.Refresh(); main.AreaMapView.Refresh();
        await Capture(_pack + "-siege-midpoint", "Real siege boundary stays behind the moving marker's arrival.");
        main.StrategicTravel.Skip(); main.StrategicTravel.Skip();
        Require(saved && File.Exists(savePath) && main.Session.LastBattles.Count == 0,
            "real Save reentrancy replaces LastBattles during retained arrival completion");
        Require(main.ActiveOverlay is BattleResultScreen && main.DisplayState.CityById("target")!.Owner == "north",
            "arrival reconciles capture then opens exactly retained siege result across Save callback");
        if (main.ActiveOverlay is BattleResultScreen result) result.Close();
        Require(main.ActiveOverlay is not BattleResultScreen, "reentrant completion cannot duplicate siege result");
        await Capture(_pack + "-siege-arrival", "Real siege/capture arrived; retained result was presented once despite Save replacing Last*.");
        File.Delete(savePath);
    }

    private async Task CheckFallHandoffNoticeOrder()
    {
        // Reuse only the merged check's toy fixture data. A paid fleet prefix and the unchanged
        // actual conquest/fall/handoff are composed from their accepted producer batches.
        var fixture = (GameSession)typeof(GameEndCheck).GetMethod("HotseatConquestSession",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, new object[] { true })!;
        var state = fixture.State with { Fleets = ValueList.Of(
            new FleetState("witness", "south", 0, 4, 3, 1, 100, 100, 100, null, null, null, 0)) };
        var session = new GameSession(fixture.World, fixture.Ruleset, fixture.Scenario,
            new SaveGame(state.SchemaVersion, "fall-travel", "fall-travel", state.ScenarioId, state.WorldId, state.RulesetId, state));
        var main = await Open(session);
        main.MapView.CentreOnTile(0, 4);
        var saved = false;
        var path = Path.Combine(GameDataContext.RepositoryRoot, "rendered", "t195-fall-reentrant-save.json");
        main.CommandIssued += _ =>
        {
            if (saved) return;
            saved = true; main.SaveAsForCheck(path);
        };
        var prefix = session.Submit("move-fleet witness 0 5");
        var completed = session.Submit("end");
        main.PresentOutputForCheck(completed with { StrategicTimeline = new(completed.StrategicTimeline.BatchId,
            ValueList.From(prefix.StrategicTimeline.Steps.Concat(completed.StrategicTimeline.Steps))) });
        main.StrategicTravel.Paused = true;
        Require(prefix.StrategicTimeline.Steps.Any(step => step.Routes.Count > 0)
            && main.StrategicTravel.IsActive && completed.SeatFalls.Any(f => f.Reason == SeatFallReason.Conquered),
            "real paid fleet prefix precedes actual AI conquest, fall and next human handoff");
        // Synthetic already-published notice input tests the existing observer/router, not AI diplomacy.
        var notice = new IC2.Engine.Diplomacy.PendingDiplomaticOfferAnnounced("north", "Northern League", "east", "Eastern League",
            session.Ruleset.Diplomacy.StateCodes.Trade, "Northern League wants to trade with Eastern League.");
        typeof(MainGameScreen).GetMethod("OnSessionEventsPublished", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(main, new object[] { new DomainEvent[] { notice } });
        var authoritative = session.State;
        typeof(MainGameScreen).GetMethod("ShowNextOverlay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(main, null);
        Require(main.ActiveOverlay is null, "fall/handoff/notice router cannot advance before strategic arrival");
        await Capture(_pack + "-fall-handoff-before-arrival", "Real AI conquest/fall and next human handoff wait behind paid fleet movement; queued notice also waits.");
        main.StrategicTravel.Skip();
        Require(saved && session.LastSeatFalls.Count == 0, "reentrant Save replaced mutable falls without losing retained output");
        var battles = 0;
        while (main.ActiveOverlay is BattleResultScreen result && battles++ < 10) result.Close();
        Require(battles > 0 && main.ActiveOverlay is GameEndScreen, "arrival battle results precede the exact fallen-seat window");
        if (main.ActiveOverlay is GameEndScreen fall) fall.Continue();
        Require(main.ActiveOverlay is HotseatHandoffScreen { Info.NextNationId: "east" },
            "fallen-seat window precedes exactly the next human handoff across Save reentrancy");
        if (main.ActiveOverlay is HotseatHandoffScreen handoff) handoff.Continue();
        Require(main.ActiveOverlay is MainGameScreen.OfferNoticeBox box && box.Text == notice.DialogText,
            "queued notice follows battle, fall and human handoff in the existing order");
        await Capture(_pack + "-notice-after-handoff", "Arrival, battle, fallen seat and handoff completed; the queued notice now appears.");
        main.ActiveOverlay?._UnhandledInput(new InputEventKey { Pressed = true, Keycode = Key.Escape });
        main.StrategicTravel.Skip();
        Require(main.ActiveOverlay is null && session.State == authoritative, "notice closes once and presentation changes no authoritative state");
        File.Delete(path);
    }

    private async Task CheckPeaceOrder()
    {
        // Fixture data only: the actual merged peace-check's loaded rules make its real offer deterministic.
        var session = (GameSession)typeof(PeaceOfferCheck).GetMethod("HumanWinsSession",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, new object[] { false })!;
        var outwards = session.Submit("move army-0 66 4");
        var back = session.Submit("move army-0 67 4");
        var attack = session.Submit("attack-army army-0 army-2");
        Require(outwards.StrategicTimeline.Steps.Any(s => s.Routes.Count > 0)
            && back.StrategicTimeline.Steps.Any(s => s.Routes.Count > 0)
            && attack.Battles.Count > 0 && session.PendingPeaceOfferFor("rome") is not null,
            "real paid routes and battle produce deterministic pending peace offer");
        var main = await Open(session);
        main.MapView.CentreOnTile(67, 4);
        main.PresentOutputForCheck(attack with { StrategicTimeline = new(attack.StrategicTimeline.BatchId,
            ValueList.From(outwards.StrategicTimeline.Steps.Concat(back.StrategicTimeline.Steps).Concat(attack.StrategicTimeline.Steps))) });
        main.StrategicTravel.Paused = true;
        typeof(MainGameScreen).GetMethod("ShowNextOverlay", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(main, null);
        Require(main.StrategicTravel.IsActive && main.ActiveOverlay is null, "battle and peace offer both wait for preceding real routes");
        main.StrategicTravel.Skip();
        Require(main.ActiveOverlay is BattleResultScreen, "arrival opens battle result before pending peace");
        if (main.ActiveOverlay is BattleResultScreen result) result.Close();
        Require(main.ActiveOverlay is PeaceOfferScreen, "pending peace offer follows its associated battle result exactly once");
        await Capture(_pack + "-peace-after-arrival-and-result", "Real pending peace offer appears only after the paid movement and associated battle result.");
    }

    private async Task CapturePacing()
    {
        var directory = Path.Combine(_directory!, _pack + "-pacing-frames");
        Directory.CreateDirectory(directory);
        var frames = new List<(string File, ulong Tick, string Mode, double? X, Image Pixels)>();
        async Task Frame(MainGameScreen main, string mode)
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var file = $"frame-{frames.Count:D4}.png";
            var tick = Time.GetTicksUsec();
            var x = main.StrategicTravel.Position(TravelKind.Army, "a")?.X;
            // PNG compression stalls the render thread; retain pixels and save only after playback.
            frames.Add((file, tick, mode, x, GetViewport().GetTexture().GetImage()));
        }
        foreach (var mode in new[] { "normal-0.12-seconds-per-cell", "fast-4x", "skip" })
        {
            var main = await Open(MovementSession(false, 20));
            main.MapView.ZoomToMaxForCheck(); main.MapView.CentreOnTile(53, 46);
            Click(main.MapView, new(50, 45), MouseButton.Left);
            main.AreaMapView.SetHighlight(AreaMapHighlightKind.Armies, true);
            var layer = new CanvasLayer { Layer = 100 };
            layer.AddChild(new Label { Text = $"{_pack}: {mode}. Real-time production movement; pan fixed; sound off.",
                Position = new Vector2(12, 74), Size = new Vector2(1000, 32) });
            main.AddChild(layer);
            await Frame(main, mode);
            await ToSignal(GetTree().CreateTimer(.45), SceneTreeTimer.SignalName.Timeout);
            await Frame(main, mode);
            main.SubmitForCheck("move a 57 48");
            if (mode == "fast-4x")
            {
                var fast = Buttons(main).Single(button => button.Text == "Fast");
                fast.SetPressedNoSignal(true);
                fast.EmitSignal(Button.SignalName.Toggled, true);
            }
            var started = Time.GetTicksMsec();
            while (main.StrategicTravel.IsActive && Time.GetTicksMsec() - started < 5000)
            {
                if (mode == "skip" && Time.GetTicksMsec() - started >= 80)
                    Buttons(main).Single(button => button.Text == "Skip").EmitSignal(Button.SignalName.Pressed);
                await Frame(main, mode);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Require(!main.StrategicTravel.IsActive, "real-time windowed " + mode + " arrives within watchdog");
            await Frame(main, mode);
            await ToSignal(GetTree().CreateTimer(.45), SceneTreeTimer.SignalName.Timeout);
            await Frame(main, mode);
        }
        foreach (var frame in frames)
        {
            using var pixels = frame.Pixels;
            if (pixels.SavePng(Path.Combine(directory, frame.File)) != Error.Ok)
                throw new IOException("Could not save real pacing frame.");
        }
        var invariant = System.Globalization.CultureInfo.InvariantCulture;
        var concat = new System.Text.StringBuilder("ffconcat version 1.0\n");
        for (var i = 0; i < frames.Count; i++)
        {
            concat.AppendLine($"file '{_pack}-pacing-frames/{frames[i].File}'");
            concat.AppendLine("option framerate 1000");
            var duration = i + 1 < frames.Count ? Math.Max(.001, (frames[i + 1].Tick - frames[i].Tick) / 1_000_000d) : .1;
            concat.AppendLine("duration " + duration.ToString("0.000000", invariant));
        }
        File.WriteAllText(Path.Combine(_directory!, _pack + "-pacing.ffconcat"), concat.ToString());
        File.WriteAllLines(Path.Combine(_directory!, _pack + "-pacing-frames.csv"),
            new[] { "file,actual_tick_microseconds,mode,fractional_x" }.Concat(frames.Select(frame =>
                $"{frame.File},{frame.Tick},{frame.Mode},{frame.X?.ToString("0.000000", invariant)}")));
        _pacingPacks.Add(_pack);
    }

    private static IEnumerable<Button> Buttons(Node node) => node.GetChildren().SelectMany(child =>
        (child is Button button ? new[] { button } : Array.Empty<Button>()).Concat(Buttons(child)));

    private async Task CheckOffscreenAndClosure()
    {
        var main = await Open(MovementSession(false, 20));
        main.MapView.ZoomToMaxForCheck(); main.MapView.CentreOnTile(120, 90);
        var before = main.MapView.TileCenterForCheck(50, 45);
        main.SubmitForCheck("move a 53 45");
        Require(!main.StrategicTravel.IsActive, "wholly offscreen submission drains without waiting for a later frame");
        main.StrategicTravel.Paused = true;
        Advance(main, 0);
        await Capture(_pack + "-offscreen-no-jump", "Wholly offscreen accepted route consumed immediately; user camera stayed fixed.");
        Require(!main.StrategicTravel.IsActive && main.MapView.TileCenterForCheck(50, 45) == before,
            "wholly offscreen route consumes immediately without forced camera jump");
        main.MapView.CentreOnTile(53, 45); main.StrategicTravel.CellSeconds = 0;
        main.SubmitForCheck("move a 55 45");
        Require(!main.StrategicTravel.IsActive, "zero delay drains production arrival synchronously");
        await Capture(_pack + "-zero-delay", "Zero presentation delay: authoritative arrival with no stale lock or selection.");
        main.StrategicTravel.CellSeconds = .12;
        main.SubmitForCheck("move a 56 45"); main.StrategicTravel.Paused = true;
        main._UnhandledInput(new InputEventKey { Pressed = true, Keycode = Key.Escape });
        Require(!main.StrategicTravel.IsActive, "Esc drains travel then clears selection without command");
        main.SubmitForCheck("move a 57 45"); main.StrategicTravel.Paused = true;
        var state = main.Session.State;
        DisposeMain();
        main.StrategicTravel.Skip(); main.StrategicTravel.Advance(1000);
        Require(!main.StrategicTravel.IsActive && main.Session.State == state, "closure releases pending display and never dispatches stale completion");
    }

    private async Task<MainGameScreen> Open(GameSession session)
    {
        DisposeMain();
        var main = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
        main.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        AddChild(main); _main = main;
        await Frames();
        return main;
    }

    private void DisposeMain()
    {
        if (_main is null) return;
        RemoveChild(_main); _main.QueueFree(); _main = null;
    }

    private static GameSession MovementSession(bool fleet, int moves, bool cargo = false)
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        var code = toy.World.TileTypes.First(t => t.Id == (fleet ? "sea_calm" : "plain")).Code;
        var world = toy.World with { Width = 128, Height = 96,
            Terrain = new(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(code, 128 * 96))) };
        var state = GameStateFactory.CreateInitial(world, toy.Ruleset, toy.Scenario);
        var army = state.Armies[0] with { Id = "a", X = 50, Y = 45, Moves = moves, CoveredTileCode = code };
        state = state with
        {
            Armies = fleet ? cargo ? ValueList.Of(army with { Id = "cargo", CoveredTileCode = null, AboardFleetId = "f" }) : ValueList<ArmyState>.Empty : ValueList.Of(army),
            Fleets = fleet ? ValueList.Of(new FleetState("f", "north", 50, 45, moves, 30, 100, 100, 100, null, null, cargo ? "cargo" : null, code)) : ValueList<FleetState>.Empty,
            Nations = ValueList.From(state.Nations.Select(n => n with { Control = SeatControl.Human })),
            Cities = ValueList.From(state.Cities.Select((c, i) => c with { X = 45 + i * 10, Y = 40 })),
        };
        return new(world, toy.Ruleset, toy.Scenario,
            new SaveGame(state.SchemaVersion, "strategic-travel", "strategic-travel", toy.Scenario.Id, world.Id, toy.Ruleset.Id, state));
    }

    private static GridPoint Position(GameState state, bool fleet, string id) => fleet
        ? new(state.FleetById(id)!.X, state.FleetById(id)!.Y) : new(state.ArmyById(id)!.X, state.ArmyById(id)!.Y);
    private static float TilePixels(MainGameScreen main) => main.MapView.TileCenterForCheck(51, 45).X - main.MapView.TileCenterForCheck(50, 45).X;
    private static void Click(GameMapView map, GridPoint tile, MouseButton button)
    {
        var position = map.TileCenterForCheck(tile.X, tile.Y);
        map._GuiInput(new InputEventMouseButton { Position = position, ButtonIndex = button, Pressed = true });
        map._GuiInput(new InputEventMouseButton { Position = position, ButtonIndex = button, Pressed = false });
    }
    private static void Advance(MainGameScreen main, double seconds)
    {
        main.StrategicTravel.Paused = false; main.StrategicTravel.Advance(seconds); main.StrategicTravel.Paused = true;
    }
    private async Task Frames()
    {
        for (var i = 0; i < 3; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private static IEnumerable<string> Labels(Node node) => node.GetChildren().SelectMany(n =>
        (n is Label label ? new[] { label.Text } : Array.Empty<string>()).Concat(Labels(n)));
    private void Require(bool condition, string description)
    {
        if (!condition) _ok = false;
        GD.Print($"StrategicTravelCheck: {(condition ? "PASS" : "FAIL")} {description}");
    }
    private async Task Capture(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(_directory)) return;
        Directory.CreateDirectory(_directory);
        await Frames();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var path = Path.Combine(_directory, name + ".png");
        using var image = GetViewport().GetTexture().GetImage();
        Require(image.SavePng(path) == Error.Ok, "real window capture " + name);
        _captures.Add((name + ".png", description));
    }
    private void WriteGallery()
    {
        if (string.IsNullOrWhiteSpace(_directory)) return;
        var head = System.Environment.GetEnvironmentVariable("IC2_TRAVEL_HEAD") ?? "UNPINNED";
        var receipt = $"Exact source HEAD {head}. Real window captures, authored and placeholder. Public synthetic scenarios; candidate 0.12 seconds/cell; sound disabled, prior settings restored. Human visual acceptance pending.";
        File.WriteAllText(Path.Combine(_directory, "receipts.txt"), receipt + "\n" + string.Join("\n", _captures.Select(c => c.File + ": " + c.Caption)));
        File.WriteAllText(Path.Combine(_directory, "index.html"), "<!doctype html><meta charset='utf-8'><title>T195 strategic travel</title><style>body{font:16px system-ui;background:#151b22;color:#eee;max-width:1400px;margin:auto}img{max-width:100%}figure{margin:30px 0}figcaption{padding:8px}</style><h1>T195 strategic travel</h1><p>" + System.Net.WebUtility.HtmlEncode(receipt) + "</p>" +
            string.Join("", _pacingPacks.Select(pack => $"<h2>{pack}: normal / Fast / Skip</h2><video controls preload='metadata' style='max-width:100%' src='{pack}-pacing.mp4'></video>")) +
            string.Join("", _captures.Select(c => $"<figure><img loading='lazy' src='{c.File}'><figcaption>{System.Net.WebUtility.HtmlEncode(c.Caption)} ({c.File})</figcaption></figure>")));
    }
}
