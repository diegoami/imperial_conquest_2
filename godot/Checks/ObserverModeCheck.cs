using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class ObserverModeCheck : Control
{
    private readonly List<(int Frames, Action Run)> _plan = new();
    private int _frame, _step;
    private bool _ok = true;
    private ScenarioSeatScreen _seats = null!;
    private readonly NewGameSelection _selection = new();
    private MainGameScreen _main = null!;
    private GameSession _reference = null!;
    private GameState _paused = null!;
    private UserSettings? _preferences;

    public override void _Ready()
    {
        _preferences = SettingsScreen.Current;
        // Retain the legacy Instant observer controls as a separate compatibility
        // case. Watched tactical playback has its own real-input check.
        SettingsScreen.Commit(new UserSettings { ObserverTacticalBattles = false });
        _plan.Add((0, () => {
            _selection.SelectedCard = RulesetPresets.CardForScenario(GameDataContext.Repository.Resolve("toy-3city"));
            _seats = new ScenarioSeatScreen { Selection = _selection };
            AddChild(_seats);
        }));
        _plan.Add((6, () => {
            Check(!_seats.ObserverChoice.ButtonPressed, "ordinary play remains the default");
            _seats.SetHumanForCheck("north", false);
            Check(!_seats.CanStart, "empty human selection alone is still refused");
            _seats.ObserverChoice.ButtonPressed = true;
            Check(_seats.CanStart, "explicit Observer choice permits all-AI New Game");
            _seats.ConfirmSeatAndStart();
            _reference = GameSessionFactory.CreateSession(GameDataContext.Repository, _selection);
            _main = MakeMain(GameSessionFactory.CreateSession(GameDataContext.Repository, _selection));
            RemoveChild(_seats); _seats.QueueFree();
            _paused = _main.Session.State;
        }));
        _plan.Add((8, () => {
            Check(_main.Session.State == _paused, "new observer waits paused without advancing");
            Check(_main.ObserverControls.Visible, "observer controls are discoverable");
            _main.SetViewedNation("south");
            _main.ObserverNextRound();
        }));
        _plan.Add((8, () => {
            _reference.Submit("end");
            Check(_main.Session.State == _reference.State, "Next round exactly matches existing watch state and RNG");
            Check(!_main.ObserverPlayback.IsRunning, "Next round stops at the next date");
            Check(_main.ViewedNationId == "south", "observer inspection filter survives nation turns");
            Check(_main.ActiveOverlay is null, "observation does not queue automatic dialogs");
            _main.ObserverPlay();
            Check(_main.ObserverTakeControlButton.Disabled, "takeover is disabled during automatic play");
            _main.ObserverTakeControl(_main.Session.State.ActiveNationId);
            Check(_main.Session.IsObserverMode, "attempted takeover while running changes no control");
        }));
        _plan.Add((4, () => { _main.ObserverPause(); _main.StrategicTravel.Skip(); _paused = _main.Session.State; }));
        _plan.Add((8, PrepareBattleObserver));
        _plan.Add((8, () => {
            Check(_main.Session.LastBattles.Count > 0, "an actual AI battle resolved during observation");
            Check(_main.ActiveOverlay is null, "actual AI battle did not interrupt automatic observation");
            Check(!_main.ObserverReportsButton.Disabled, "latest real battle report remains inspectable");
            _main.ObserverReportsButton.EmitSignal(BaseButton.SignalName.Pressed);
            Check(_main.ActiveOverlay is not null, "explicit report inspection opens its dialog");
            Check(!_main.ObserverPlayback.IsRunning, "opening reports pauses observation");
            RemoveChild(_main); _main.QueueFree();
            var normal = GameSessionFactory.CreateSession(GameDataContext.Repository,
                new NewGameSelection { SelectedCard = _selection.SelectedCard, HumanNationId = "north" });
            _main = MakeMain(normal);
            _main.SubmitForCheck("observe");
            Check(_main.Session.IsObserverMode, "a loaded/normal strategic campaign can enter observation");
            Check(!_main.ObserverPlayback.IsRunning, "entry starts paused");
            var active = _main.Session.State.ActiveNationId;
            _main.ObserverTakeControl(active);
            Check(!_main.Session.IsObserverMode, "paused observer can take control of a nation");
            Check(_main.Session.State.ActiveNationId == active, "active-nation takeover does not skip its turn");
            Check(!_main.ObserverControls.Visible, "observer controls disappear after takeover");
        }));
    }

    private void PrepareBattleObserver()
    {
        Check(_main.Session.State == _paused, "Pause stops future nation turns");
        _main.CommandTable.TryInvoke("strategy.balance_sheet");
        Check(_main.ActiveOverlay is not null, "inspection remains accessible while observing");
        Check(!_main.ObserverPlayback.IsRunning, "inspection does not resume observation");
        RemoveChild(_main); _main.QueueFree();
        var fresh = GameSessionFactory.CreateSession(GameDataContext.Repository, _selection);
        var state = fresh.State;
        var north = state.Armies.First(a => a.Nation == "north") with { X = 3, Y = 2, Moves = 10,
            SupplyTons = 1000, Units = ValueList.Of(new UnitSlot(0, "light_infantry", 400, 6, "Foot")) };
        var south = state.Armies.First(a => a.Nation == "south") with { X = 4, Y = 2, Moves = 10,
            SupplyTons = 1000, Units = ValueList.Of(new UnitSlot(0, "heavy_infantry", 6000, 6, "Guard")) };
        state = state with { Armies = ValueList.Of(north, south),
            ActiveSeatIndex = state.TurnOrder.ToList().IndexOf("south"),
            Relations = state.Relations.WithRelation("north", "south", fresh.Ruleset.Diplomacy.StateCodes.War) };
        var world = fresh.World with { Terrain = new(TerrainEncoding.RunLength,
            ValueList.Of(new TerrainRun(2, fresh.World.Width * fresh.World.Height))) };
        var save = new SaveGame(GameDataSchema.CurrentVersion, "observer-battle", "Observer battle",
            fresh.Scenario.Id, world.Id, fresh.Ruleset.Id, state);
        _main = MakeMain(new GameSession(world, fresh.Ruleset, fresh.Scenario, save));
        _main.ObserverNextRound();
    }

    private MainGameScreen MakeMain(GameSession session)
    {
        var screen = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
        screen.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(screen);
        screen.StrategicTravel.CellSeconds = 0;
        return screen;
    }

    public override void _Process(double delta)
    {
        if (_step >= _plan.Count || ++_frame < _plan[_step].Frames) return;
        try { _plan[_step++].Run(); _frame = 0; }
        catch (Exception ex) { Check(false, ex.ToString()); _step = _plan.Count; }
        if (_step == _plan.Count)
        {
            GD.Print($"ObserverModeCheck: exiting with code {(_ok ? 0 : 1)}.");
            CheckExit.Quit(this, _ok ? 0 : 1);
        }
    }

    private void Check(bool condition, string message)
    {
        if (condition) GD.Print("PASS: " + message);
        else { _ok = false; GD.PrintErr("FAIL: " + message); }
    }

    public override void _ExitTree()
    {
        if (_preferences is not null) SettingsScreen.Commit(_preferences);
    }
}
