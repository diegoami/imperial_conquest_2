using Godot;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class RallyMapCheck : Control
{
    private MainGameScreen _main = null!;
    private bool _ok = true;

    public override async void _Ready()
    {
        var preferences = SettingsScreen.Current;
        try
        {
            SettingsScreen.Commit(new UserSettings());
            var data = GameDataContext.Repository.Resolve("toy-3city");
            var initial = new GameSession(data.World, data.Ruleset, data.Scenario, humanSeatNationId: "north");
            var cells = Enumerable.Repeat(0, 40 * 40).ToArray();
            foreach (var city in initial.State.Cities) cells[city.Y * 40 + city.X] = 2;
            var world = data.World with { Width = 40, Height = 40,
                Terrain = new TerrainGrid(TerrainEncoding.RunLength, ValueList.From(cells.Select(c => new TerrainRun(c, 1)))) };
            var building = initial.State.Fleets.First() with { Id = "rally-fleet", Nation = "north", X = 0, Y = 0,
                Moves = 12, ConstructionTicksRemaining = 1, BuildCityId = "arx", CarriedArmyId = null };
            var state = initial.State with { Armies = ValueList<ArmyState>.Empty, Fleets = ValueList.Of(building),
                Nations = ValueList.From(initial.State.Nations.Select(n => n with { Control = SeatControl.Human })) };
            var session = new GameSession(world, data.Ruleset, data.Scenario,
                new SaveGame(state.SchemaVersion, "rally-check", "rally-check", state.ScenarioId, state.WorldId, state.RulesetId, state));
            _main = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
            _main.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(_main);
            _main.StrategicTravel.CellSeconds = 0;
            await Frames();
            var home = state.CityById("arx")!;
            _main.RallyPointSetButtonForCheck.EmitSignal(BaseButton.SignalName.Pressed);
            Click(home.X, home.Y);
            _main.FleetRallyPointSetButtonForCheck.EmitSignal(BaseButton.SignalName.Pressed);
            Click(home.X, home.Y);
            await Frames();
            Check(session.RallyPointFor("north")?.CityId == home.Id && session.RallyPointFor("north", true)?.CityId == home.Id,
                "real army and fleet controls set independent rally destinations at one port");
            CheckMarkers("co-located markers are distinct");
            _main.MapView._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.WheelUp,
                Pressed = true, Position = _main.MapView.TileCenterForCheck(home.X, home.Y) });
            await Frames(); CheckMarkers("both marker kinds remain distinct after map zoom");
            _main.FleetRallyPointClearButtonForCheck.EmitSignal(BaseButton.SignalName.Pressed);
            await Frames();
            Check(session.RallyPointFor("north", true) is null && session.RallyPointFor("north") is not null
                && _main.MapView.RallyMarkersForCheck.Count == 1, "clear fleet removes only its setting and symbol");
            _main.PerformStrategicUndo();
            await Frames(); CheckMarkers("Undo restores the fleet setting and drawn symbol");

            SetFleetTarget(30, 30);
            var point = session.RallyPointFor("north", true)!;
            Check((point.X, point.Y) == (30, 30), "fleet rally accepts a sea tile");
            var foreign = state.Cities.First(c => c.Owner == "south");
            SetFleetTarget(foreign.X, foreign.Y);
            Check(session.RallyPointFor("north", true) == point && _main.LastMessageForCheck.Contains("refused", StringComparison.OrdinalIgnoreCase),
                "a foreign city is visibly refused without replacing the fleet rally");

            var moves = new List<string>();
            _main.CommandIssued += lines => { if (lines[0].StartsWith("> move-fleet ", StringComparison.Ordinal)) moves.Add(lines[0]); };
            EndTurn();
            Check(session.State.ActiveNationId == "south" && session.FleetRallyOrders.Count == 0,
                "the other hotseat nation receives no rally order before launch");
            await Frames();
            Check(_main.MapView.RallyMarkersForCheck.Count == 0, "north markers are not shown in south's turn");
            EndTurn();
            var launch = session.State.FleetById(building.Id)!;
            Check(!launch.IsUnderConstruction && launch.Moves == 12 && moves.Count == 0
                && session.FleetRallyOrderFor(building.Id) is not null,
                "the real launch binds its order without moving, even with retained launch moves");
            var path = Path.Combine(Path.GetTempPath(), $"ic2-rally-map-{Guid.NewGuid():N}.json");
            try
            {
                _main.SaveAsForCheck(path);
                var save = SaveManager.LoadFile(path, world, data.Ruleset);
                var resumed = new GameSession(world, data.Ruleset, data.Scenario, save);
                Check(resumed.NationRallyPoints.SequenceEqual(session.NationRallyPoints)
                    && resumed.FleetRallyOrders.SequenceEqual(session.FleetRallyOrders),
                    "real Save As retains both symbols' settings and the new fleet journey");
            }
            finally { if (File.Exists(path)) File.Delete(path); }
            EndTurn();
            var after = session.State.FleetById(building.Id)!;
            Check(moves.Count == 1 && (after.X != launch.X || after.Y != launch.Y),
                "next confirmed human End turn moves the new fleet exactly once");
            EndTurn(); // Return from the other human seat.
            var beforeManual = session.State;
            var previousOrder = session.FleetRallyOrderFor(building.Id);
            Check(previousOrder is not null, "a long unfinished fleet journey remains saved");
            _main.SubmitForCheck($"move-fleet {building.Id} {after.X} {after.Y}");
            Check(session.FleetRallyOrderFor(building.Id) is null, "manual fleet movement overrides its rally journey");
            Check(_main.PerformStrategicUndo() && session.State == beforeManual
                && session.FleetRallyOrderFor(building.Id) == previousOrder, "Undo restores manual movement and the cancelled fleet order together");
        }
        catch (Exception ex) { Check(false, ex.ToString()); }
        finally
        {
            if (_main is not null) { RemoveChild(_main); _main.QueueFree(); }
            SettingsScreen.Commit(preferences);
        }
        await Frames();
        GD.Print($"RallyMapCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private void SetFleetTarget(int x, int y)
    {
        _main.FleetRallyPointSetButtonForCheck.EmitSignal(BaseButton.SignalName.Pressed);
        if (_main.ActiveOverlay is ConfirmPrompt prompt) prompt.AnswerForCheck(true);
        Click(x, y);
    }
    private void EndTurn()
    {
        _main.MenuBar.PressItemForCheck("game.end_turn");
        if (_main.ActiveOverlay is EndTurnBox box) box.EndTurnButton.EmitSignal(BaseButton.SignalName.Pressed);
        while (true)
        {
            if (_main.ActiveOverlay is HotseatHandoffScreen handoff) handoff.Continue();
            else if (_main.ActiveOverlay is MainGameScreen.OfferNoticeBox notice) notice.Close();
            else break;
        }
    }
    private void CheckMarkers(string message)
    {
        var markers = _main.MapView.RallyMarkersForCheck;
        Check(markers.Count == 2 && markers.Any(m => m.IsFleet) && markers.Any(m => !m.IsFleet)
            && markers[0].Position.DistanceTo(markers[1].Position) >= 10, message);
    }
    private void Click(int x, int y)
    {
        var position = _main.MapView.TileCenterForCheck(x, y);
        _main.MapView._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = position });
        _main.MapView._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = position });
    }
    private async Task Frames() { for (var i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void Check(bool condition, string message)
    {
        if (condition) GD.Print("PASS: " + message);
        else { _ok = false; GD.PrintErr("FAIL: " + message); }
    }
}
