using Godot;
using IC2.Engine.Ai;
using IC2.Engine.Core;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class ObserverPlaybackCheck : Control
{
    private MainGameScreen? _main;
    private bool _ok = true;

    private GameSession Build(bool distant = false)
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        var initial = new GameSession(toy.World, toy.Ruleset, toy.Scenario, seedOverride: 42, humanSeatNationId: "north");
        var world = toy.World with { Width = 20, Height = 15,
            Terrain = new(TerrainEncoding.RunLength, ValueList.Of(new TerrainRun(2, 20 * 15))) };
        var north = initial.State.Armies.First(a => a.Nation == "north") with { X = 3, Y = 3, Moves = 10,
            SupplyTons = 1000, Units = ValueList.Of(new UnitSlot(0, "heavy_infantry", 20000, 6, "Guard")) };
        var south = initial.State.Armies.First(a => a.Nation == "south") with { X = distant ? 16 : 4, Y = distant ? 10 : 3, Moves = 10,
            SupplyTons = 1000, Units = ValueList.Of(new UnitSlot(0, "light_infantry", 1000, 6, "Foot")) };
        var scenario = toy.Scenario with { Seats = ValueList.From(toy.Scenario.Seats.Select(s => s with { Control = SeatControl.Ai })) };
        var rules = toy.Ruleset with { Ai = toy.Ruleset.Ai with { CityScoreThreshold = int.MaxValue } };
        var state = initial.State with { Armies = ValueList.Of(north, south), Fleets = ValueList<FleetState>.Empty,
            Nations = ValueList.From(initial.State.Nations.Select(n => n with { Control = SeatControl.Ai })),
            Relations = initial.State.Relations.WithRelation("north", "south", rules.Diplomacy.StateCodes.War) };
        return new(world, rules, scenario, new SaveGame(state.SchemaVersion, "observer-playback", "Observer playback",
            state.ScenarioId, state.WorldId, state.RulesetId, state)) { ObserverTacticalBattles = true };
    }

    private MainGameScreen AddMain(GameSession session)
    {
        var main = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
        main.SetAnchorsPreset(LayoutPreset.FullRect); AddChild(main); _main = main;
        return main;
    }

    public override async void _Ready()
    {
        var preferences = SettingsScreen.Current;
        try
        {
            SettingsScreen.Commit(new UserSettings());
            await CheckObserverInspection();
            var session = Build(); var reference = Build(); reference.CaptureObserverBattle = (_, _) => false;
            var main = AddMain(session);
            await Frames();
            Check(main.ObserverBattleChoice.Selected == 1 && main.ObserverSpeedChoice.GetItemId(main.ObserverSpeedChoice.Selected) == 50,
                "observer starts paused with explicit experimental tactical policy and readable 0.5x speed");
            var unchanged = session.State;
            main.ObserverWatchButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(main.ActiveOverlay is ObserverWatchDialog, "Watch civilizations opens a distinct multi-selection dialog");
            if (main.ActiveOverlay is ObserverWatchDialog chooser)
            {
                Check(chooser.Choices.Count == 2 && chooser.Choices.Values.All(c => c.ButtonPressed), "new worlds watch all civilizations by default");
                chooser.SetAll(false); chooser.Choices["north"].ButtonPressed = true; chooser.Confirm();
            }
            Check(main.ObserverWatches("north") && !main.ObserverWatches("south") && session.State == unchanged,
                "real watch selection commits presentation preferences without a simulation order");
            var passes = new List<AiTurnDecided>(); session.EventsPublished += batch => passes.AddRange(batch.OfType<AiTurnDecided>());
            main.ObserverRoundButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Until(() => main.ActiveOverlay is TacticalBattleScreen);
            Check(main.ActiveOverlay is TacticalBattleScreen && session.OpenTacticalBattle is null,
                "a watched encounter opens actual recorded tactical playback without a live human battle");
            var board = (TacticalBattleScreen)main.ActiveOverlay!;
            Check(main.ObserverTacticalPresentations == 1 && passes.Count(p => p.NationId == "north") == 1,
                "both nations participating creates one presentation and one AI pass");
            main._Input(new InputEventKey { Keycode = Key.Space, Pressed = true });
            var frozen = session.State; var frame = board.DisplayedFrame?.FrameId;
            await Frames();
            Check(main.ObserverPresentationPaused && board.DisplayedFrame?.FrameId == frame && session.State == frozen,
                "real Space pauses battle presentation and future simulation");
            // The initial copy-in frame may precede the first painted placement.
            // Resume the actual display clock until a unit is visible, then inspect it paused.
            board.ObserverPauseButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Until(() => board.Model.Cells.Any(c => c.Slot >= 0 && c.IconKey is not null));
            board.ObserverPauseButton.EmitSignal(BaseButton.SignalName.Pressed);
            var visible = board.Model.Cells.FirstOrDefault(c => c.Slot >= 0 && c.IconKey is not null);
            Check(visible is not null, "a real displayed unit is available to inspect");
            if (visible is not null)
            {
                var name = board.DisplayedSnapshot.View.Slots[visible.Slot].Name;
                board.ClickBoardCell(visible.X, board.BoardYForEngineY(visible.Y));
                await Frames();
                Check(board.PanelLabel.Visible && board.PanelLabel.Text.Contains(name, StringComparison.Ordinal),
                    "readonly board click exposes the displayed unit information");
            }
            board.PressToolbarButton("end_turn"); board.PressToolbarButton("computer_general"); board.UndoLastAction();
            Check(session.State == frozen && passes.Count(p => p.NationId == "north") == 1,
                "battle orders and Undo cannot mutate an observer replay");
            var speed = Enumerable.Range(0, board.ObserverSpeedChoice.ItemCount).First(i => board.ObserverSpeedChoice.GetItemId(i) == 200);
            board.ObserverSpeedChoice.Select(speed); board.ObserverSpeedChoice.EmitSignal(OptionButton.SignalName.ItemSelected, (long)speed);
            await Frames();
            Check(main.ObserverSpeedChoice.GetItemId(main.ObserverSpeedChoice.Selected) == 200 && session.State == frozen,
                "battle speed control persists and synchronizes with the map without changing state/RNG");
            board.ObserverPauseButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(main.ObserverPlayback.StopAtTurn == 1, "resuming a paused Next round retains its original boundary");
            board.ObserverSkipButton.EmitSignal(BaseButton.SignalName.Pressed);
            await Until(() => !main.ObserverPlayback.IsRunning && !main.StrategicTravel.IsActive && main.ActiveOverlay is null);
            reference.Submit("end");
            Check(session.State == reference.State && session.State.Calendar.TurnIndex == 1,
                "UI pause/speed/skip produces the same full round and RNG as uncaptured headless tactical observation");
            var end = session.State; await Frames();
            Check(session.State == end, "round stepping does not overshoot after finishing visible activity");
            Check(!main.ObserverTakeControlButton.Disabled && !main.ObserverWatchButton.Disabled,
                "takeover and watch changes become available at the safe paused boundary");
            RemoveChild(main); main.QueueFree(); _main = null; await Frames();

            SettingsScreen.Commit(new UserSettings().WithWatchedNations(session.World.Id, Array.Empty<string>()));
            var unseen = Build(); var fast = AddMain(unseen);
            await Frames(); fast.ObserverNextRound();
            await Until(() => !fast.ObserverPlayback.IsRunning && !fast.StrategicTravel.IsActive && fast.ActiveOverlay is null);
            Check(unseen.State == reference.State && fast.ObserverTacticalPresentations == 0,
                "unwatched battles fast-forward using identical tactical resolution and no battle window");
            RemoveChild(fast); fast.QueueFree(); _main = null; await Frames();

            SettingsScreen.Commit(new UserSettings().WithWatchedNations(session.World.Id, new[] { "north" }));
            var moving = AddMain(Build(distant: true));
            await Frames(); moving.ObserverNextRound();
            await Until(() => moving.StrategicTravel.CurrentRoute is not null);
            moving.ObserverPause();
            Check(moving.StrategicTravel.IsActive && moving.StrategicTravel.CurrentRoute?.OwnerId == "north",
                "watched strategic movement has an actual paced route");
            var live = moving.Session.State; var route = moving.StrategicTravel.CurrentRoute!;
            var displayed = moving.DisplayState.ArmyById(route.EntityId)!;
            var position = moving.MapView.TileCenterForCheck(displayed.X, displayed.Y);
            moving.MapView._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, Position = position });
            moving.MapView._GuiInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, Position = position });
            Check(moving.MapView.SelectedStrategicMarkerForCheck == route.EntityId && moving.Session.State == live,
                "paused map inspection picks the displayed marker without an army order");
            moving.SkipObserverActivity();
            Check(moving.Session.State == live && !moving.StrategicTravel.IsActive, "Skip activity drains presentation while paused without advancing simulation");
        }
        catch (Exception ex) { Check(false, ex.ToString()); }
        finally
        {
            if (_main is not null) { RemoveChild(_main); _main.QueueFree(); _main = null; }
            SettingsScreen.Commit(preferences);
        }
        await Frames();
        GD.Print($"ObserverPlaybackCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private async Task CheckObserverInspection()
    {
        var original = Build(distant: true);
        var other = original.State.Nations.First(n => n.Id != original.State.ActiveNationId).Id;
        var army = original.State.Armies.First(a => a.Nation == other) with { Money = 12345 };
        var state = original.State with {
            Armies = ValueList.From(original.State.Armies.Select(a => a.Id == army.Id ? army : a)),
            Fleets = ValueList.Of(new FleetState("inspection-fleet", other, 0, 0, 3, 30, 87, 100, 6789, null, null, army.Id, 0)) };
        var session = new GameSession(original.World, original.Ruleset, original.Scenario,
            new SaveGame(state.SchemaVersion, "observer-inspection", "Observer inspection",
                state.ScenarioId, state.WorldId, state.RulesetId, state));
        var main = AddMain(session);
        await Frames();
        var before = session.State;
        main.ObserverWatchButton.EmitSignal(BaseButton.SignalName.Pressed);
        var chooser = (ObserverWatchDialog)main.ActiveOverlay!;
        chooser.SetAll(false); chooser.Choices[other].ButtonPressed = true; chooser.Confirm();
        Check(main.ViewedNationId == other && main.ContextPanel.StatusNationIdForCheck == other,
            "watching one civilization selects its inspection perspective, not the active AI seat");
        Check(Labels(main.ContextPanel).Contains($"Treasury: {InformationWords.Grouped(state.NationById(other)!.Treasury)} talents")
            && Labels(main.ContextPanel).Contains($"Mobilized: {state.NationById(other)!.MobilizedPercent}%"),
            "observer nation information includes treasury and mobilization");
        main.OpenDiplomacyScreen();
        var relations = (DiplomacyScreen)main.ActiveOverlay!;
        var choice = relations.ObserverPerspectiveChoice;
        Check(choice is not null && choice.GetItemText(choice.Selected) == state.NationById(other)!.Name,
            "International relations opens for the watched civilization, not the active AI seat");
        Check(Labels(relations.Grid).Contains(state.NationById(state.ActiveNationId)!.Name)
            && !Labels(relations.Grid).Contains(state.NationById(other)!.Name),
            "relation rows are relative to the watched nation, excluding its own row");
        Check(relations.Grid.GetChildren().All(n => n is not Button) && relations.AcceptOfferButton is null
            && Labels(relations).Contains("Observing · read-only"), "observer diplomacy has no treaty actions or pending-offer acceptance");
        if (choice is not null)
        {
            var index = choice.Selected == 0 ? 1 : 0;
            var expected = choice.GetItemText(index);
            choice.EmitSignal(OptionButton.SignalName.ItemSelected, index);
            Check(relations.ObserverPerspectiveChoice!.GetItemText(relations.ObserverPerspectiveChoice.Selected) == expected,
                "observer can inspect another civilization's relations without taking control");
        }
        relations.Close();
        main.ContextPanel.ShowArmy(army.Id);
        Check(Labels(main.ContextPanel).Contains("Money: 12,345 talents")
            && Labels(main.ContextPanel).All(t => !t.Contains("withheld", StringComparison.OrdinalIgnoreCase)),
            "observer sees the non-active army's full information");
        main.ContextPanel.ShowFleet("inspection-fleet");
        Check(Labels(main.ContextPanel).Contains("Money: 12,345 talents")
            && Labels(main.ContextPanel).Contains("Repair: 87%"), "observer sees fleet and embarked-army facts");
        var city = state.Cities.First(c => c.Owner == other);
        main.ContextPanel.ShowCity(city.Id);
        Check(Labels(main.ContextPanel).Any(t => t.StartsWith("Supply: ", StringComparison.Ordinal) && t.EndsWith(" tons", StringComparison.Ordinal)),
            "observer sees non-active city supplies");
        Check(session.State == before && session.IsObserverMode,
            "inspection preserves campaign state, random seed and AI control");
        RemoveChild(main); main.QueueFree(); _main = null; await Frames();
        // The remembered single watch choice also applies when opening another campaign.
        var loaded = AddMain(session); await Frames();
        Check(loaded.ViewedNationId == other, "remembered watch selection initializes inspection perspective");
        RemoveChild(loaded); loaded.QueueFree(); _main = null; await Frames();
        session.Submit($"take-control {state.ActiveNationId}");
        Check(!session.IsObserverMode, "control case returns to ordinary human play");
        var human = AddMain(session); await Frames();
        human.SetViewedNation(other);
        Check(Labels(human.ContextPanel).Contains("Treasury:") && Labels(human.ContextPanel).Contains("Mobilized:"),
            "viewing another nation during human play still withholds its private figures");
        human.ContextPanel.ShowArmy(army.Id);
        Check(Labels(human.ContextPanel).Contains("Money: withheld"), "ordinary human play retains foreign-army secrecy");
        human.ContextPanel.ShowFleet("inspection-fleet");
        Check(Labels(human.ContextPanel).Contains("Money: withheld"), "ordinary human play retains carried-army secrecy");
        RemoveChild(human); human.QueueFree(); _main = null; await Frames();
        SettingsScreen.Commit(new UserSettings());
    }

    private static IEnumerable<string> Labels(Node root)
    {
        if (root is Label label) yield return label.Text;
        foreach (var child in root.GetChildren()) foreach (var text in Labels(child)) yield return text;
    }

    private async Task Until(Func<bool> condition)
    {
        for (var i = 0; i < 600 && !condition(); i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(condition(), "expected observer playback boundary becomes available");
    }
    private async Task Frames() { for (var i = 0; i < 5; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void Check(bool condition, string text)
    { if (condition) GD.Print("PASS: " + text); else { _ok = false; GD.PrintErr("FAIL: " + text); } }
}
