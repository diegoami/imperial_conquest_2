using Godot;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class ArmyTravelClickCheck : Control
{
    private bool _ok = true;
    private MainGameScreen _main = null!;
    public override async void _Ready()
    {
        var preferences = SettingsScreen.Current;
        try
        {
            SettingsScreen.Commit(new UserSettings { RememberArmyDestinations = true });
            var data = GameDataContext.Repository.Resolve("toy-3city");
            var world = data.World with { Width = 40, Height = 40,
                Terrain = new TerrainGrid(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(2, 1600))) };
            var source = new GameSession(data.World, data.Ruleset, data.Scenario, humanSeatNationId: "north");
            var army = source.State.ArmyById("north-army-1")! with { X = 4, Y = 10, Moves = 2 };
            var state = source.State with { Armies = ValueList.Of(army), Fleets = ValueList<FleetState>.Empty };
            var session = new GameSession(world, data.Ruleset, data.Scenario,
                new SaveGame(state.SchemaVersion, "click-travel", "click-travel", state.ScenarioId,
                    state.WorldId, state.RulesetId, state));
            _main = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
            _main.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(_main);
            _main.StrategicTravel.CellSeconds = 0;
            await Frames();
            _main.SelectArmyForCheck(army.Id);
            var before = session.State;
            Click(20, 10);
            var moved = session.State;
            Check(moved.ArmyById(army.Id) is { X: 6, Y: 10, Moves: 0 },
                "ordinary distant click immediately uses the existing walker and available moves");
            Check(session.TravelTargetFor(army.Id) is { X: 20, Y: 10 },
                "partial movement remembers the remainder");
            Check(session.StrategicUndoCount == 1, "movement and remembered remainder form one Undo action");
            Check(_main.PerformStrategicUndo() && session.State == before && session.TravelTargetFor(army.Id) is null,
                "Undo restores exact state/RNG and removes the click-created remainder");
            Click(20, 10);
            Check(session.State == moved, "replaying the click after Undo cannot reroll movement");
            _main.SelectArmyForCheck(army.Id);
            var prior = session.TravelTargetFor(army.Id);
            Check(_main.MapView.SelectedTravelTarget == prior, "reselecting shows the saved destination circle");
            Click(25, 10);
            Check(session.State == moved && session.TravelTargetFor(army.Id) is { X: 25 },
                "an exhausted army replaces its order without a move or RNG draw");
            _main.PerformStrategicUndo();
            Check(session.TravelTargetFor(army.Id) == prior, "Undo restores the previous destination");
            _main.SelectArmyForCheck(army.Id);
            Click(6, 10);
            Check(session.TravelTargetFor(army.Id) is null, "clicking the selected army clears its circle and order");
            var city = session.State.Cities.First();
            Click(city.X, city.Y);
            Check(session.TravelTargetFor(army.Id)?.CityId == city.Id && session.State == moved,
                "the retained selection can target a distant city without a siege or movement");
            var inspected = 0;
            _main.MapView.UnitListRequested += (_, _) => inspected++;
            var pos = _main.MapView.TileCenterForCheck(6, 10);
            _main.MapView._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left,
                Pressed = true, DoubleClick = true, Position = pos });
            _main.MapView._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left,
                Pressed = false, Position = pos });
            Check(inspected == 1 && session.State == moved, "double-click inspection issues no game order");
            Check(GameCommandTable.RowById("unit_map.army_set_target") is null
                && GameCommandTable.RowById("unit_map.army_cancel_target") is null,
                "separate travel menu actions are removed");
            var savedOrder = session.TravelTargetFor(army.Id);
            SettingsScreen.Commit(new UserSettings());
            Check(session.TravelTargetFor(army.Id) == savedOrder,
                "turning off new journey creation preserves existing campaign orders");
            session.ClearArmyTravelTarget(army.Id);
            _main.SelectArmyForCheck(army.Id);
            Click(20, 10);
            Check(session.TravelTargetFor(army.Id) is null && session.State == moved,
                "disabled default retains ordinary exhausted-army behavior without creating a target");

            // A legal city approach must finish beside the city, with no implicit siege.
            RemoveChild(_main); _main.QueueFree();
            SettingsScreen.Commit(new UserSettings { RememberArmyDestinations = true, ConfirmMoves = true });
            var home = state.Cities.First(c => c.Owner == "north");
            var approachArmy = army with { X = home.X + 3, Y = home.Y, Moves = 8 };
            var approachState = state with { Armies = ValueList.Of(approachArmy) };
            var approach = new GameSession(world, data.Ruleset, data.Scenario,
                new SaveGame(state.SchemaVersion, "city-travel", "city-travel", state.ScenarioId,
                    state.WorldId, state.RulesetId, approachState));
            _main = new MainGameScreen { Session = approach, RepositoryRoot = GameDataContext.RepositoryRoot };
            _main.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(_main);
            _main.StrategicTravel.CellSeconds = 0;
            await Frames();
            var commands = new List<string>();
            _main.MapView.CommandIssued += lines => commands.Add(lines[0]);
            _main.SelectArmyForCheck(army.Id);
            var cityBefore = approach.State;
            Click(home.X, home.Y);
            Check(approach.State == cityBefore && approach.TravelTargetFor(army.Id) is null,
                "move confirmation first marks the city without changing orders or state");
            Click(home.X, home.Y);
            var arrived = approach.State.ArmyById(army.Id)!;
            Check(Math.Max(Math.Abs(arrived.X - home.X), Math.Abs(arrived.Y - home.Y)) == 1
                && approach.TravelTargetFor(army.Id) is null && commands.Count == 1
                && commands[0].StartsWith("> move ", StringComparison.Ordinal),
                "confirmed city travel arrives adjacent using only one ordinary move");
        }
        catch (Exception ex) { Check(false, ex.ToString()); }
        finally
        {
            if (_main is not null) { RemoveChild(_main); _main.QueueFree(); }
            SettingsScreen.Commit(preferences);
        }
        await Frames();
        GD.Print($"ArmyTravelClickCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private async Task Frames() { for (var i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void Click(int x, int y)
    {
        var pos = _main.MapView.TileCenterForCheck(x, y);
        _main.MapView._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = pos });
        _main.MapView._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = pos });
    }
    private void Check(bool condition, string message)
    {
        if (condition) GD.Print("PASS: " + message);
        else { _ok = false; GD.PrintErr("FAIL: " + message); }
    }
}
