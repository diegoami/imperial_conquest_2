using Godot;
using IC2.Engine.Model;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// #719: New Game's seat screen ticks several human nations and edits their leaders' names, as the original's
/// "Human and computer leaders" form does. Starting it makes a hotseat game: both seats human, the typed names in
/// place, and ending the first player's turn hands over to the second. Runs the real
/// <see cref="ScenarioSeatScreen"/> and <see cref="MainGameScreen"/> on the toy scenario.
/// </summary>
public partial class HotseatNewGameCheck : Control
{
    private const int SettleFrames = 6;

    private readonly List<(int WaitFrames, Action Run)> _plan = new();
    private readonly NewGameSelection _selection = new();
    private ScenarioSeatScreen _seats = null!;
    private MainGameScreen? _mainGame;
    private bool _started;
    private string _firstSeat = string.Empty;
    private bool _ok = true;
    private int _frame;
    private int _planIndex;

    public override void _Ready()
    {
        _plan.Add((0, ShowSeats));
        _plan.Add((SettleFrames, PickPlayers));
        _plan.Add((SettleFrames, CheckGameAndEndTurn));
        _plan.Add((SettleFrames * 2, CheckHandoff));
    }

    public override void _Process(double delta)
    {
        _frame++;
        try
        {
            if (_planIndex >= _plan.Count)
            {
                return;
            }

            var (waitFrames, run) = _plan[_planIndex];
            if (_frame < waitFrames)
            {
                return;
            }

            run();
            _planIndex++;
            _frame = 0;
            if (_planIndex >= _plan.Count)
            {
                Finish();
            }
        }
        catch (Exception ex)
        {
            Check(false, $"unhandled exception: {ex}");
            _planIndex = _plan.Count;
            Finish();
        }
    }

    private void ShowSeats()
    {
        _selection.SelectedCard = RulesetPresets.CardForScenario(GameDataContext.Repository.Resolve("toy-3city"));
        _seats = new ScenarioSeatScreen { Selection = _selection };
        _seats.SetAnchorsPreset(LayoutPreset.FullRect);
        _seats.Started += () => _started = true;
        AddChild(_seats);
    }

    private void PickPlayers()
    {
        Check(_seats.HumanNationIds.SequenceEqual(new[] { "north" }), $"the scenario's own human seat is ticked at first ({string.Join(",", _seats.HumanNationIds)})");
        Check(!_seats.IsLeaderEditableForCheck("south"), "a computer nation's leader's name is greyed");

        _seats.SetHumanForCheck("north", false);
        Check(!_seats.CanStart, "with no human ticked, Start Game is disabled");

        _seats.SetHumanForCheck("north", true);
        _seats.SetHumanForCheck("south", true);
        Check(_seats.IsLeaderEditableForCheck("south"), "ticking a nation human makes its leader's name editable");
        _seats.SetLeaderNameForCheck("north", "Marcus Furius");
        _seats.SetLeaderNameForCheck("south", "  Hanno  ");
        _seats.ConfirmSeatAndStart();
        Check(_started, "Start Game starts");
        Check(_selection.HumanNationIds.SequenceEqual(new[] { "north", "south" }), "both ticked nations are the players");
        _seats.QueueFree();

        _mainGame = new MainGameScreen
        {
            Session = GameSessionFactory.CreateSession(GameDataContext.Repository, _selection),
            RepositoryRoot = GameDataContext.RepositoryRoot,
        };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);
    }

    private void CheckGameAndEndTurn()
    {
        var state = _mainGame!.Session.State;
        Check(state.Nations.All(n => n.Control == SeatControl.Human), "both seats are human (a hotseat game)");
        Check(state.NationById("north")!.LeaderName == "Marcus Furius", $"north's typed leader is in place ({state.NationById("north")!.LeaderName})");
        Check(state.NationById("south")!.LeaderName == "Hanno", $"south's typed leader is trimmed and in place ({state.NationById("south")!.LeaderName})");
        _firstSeat = state.ActiveNationId;
        _mainGame.SubmitForCheck("end");
    }

    private void CheckHandoff()
    {
        var active = _mainGame!.Session.State.ActiveNationId;
        Check(active != _firstSeat, $"the turn passed to the other player ({_firstSeat} -> {active})");
        Check(_mainGame.ActiveOverlay is HotseatHandoffScreen, $"the hotseat hand-off screen is up ({_mainGame.ActiveOverlay?.GetType().Name ?? "none"})");
    }

    private void Finish()
    {
        var exitCode = _ok ? 0 : 1;
        GD.Print($"HotseatNewGameCheck: exiting with code {exitCode}.");
        CheckExit.Quit(this, exitCode);
    }

    private void Check(bool condition, string what)
    {
        if (condition)
        {
            GD.Print($"PASS: {what}");
            return;
        }

        _ok = false;
        GD.PrintErr($"FAIL: {what}");
    }
}
