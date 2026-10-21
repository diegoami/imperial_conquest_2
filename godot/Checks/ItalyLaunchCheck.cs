using Godot;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T204 Done-when 2: the Italy 500 BC campaign launches from New Game with the shipped data. The real
/// <see cref="RulesetChooserScreen"/> offers its card; choosing it and the Rome seat starts a
/// <see cref="MainGameScreen"/> on the 209×233 Italy world, whose Nations menu lists its twelve nations.
/// <c>scripts/package.ps1 -Verify</c> runs this scene inside the exported build too.
/// </summary>
public partial class ItalyLaunchCheck : Control
{
    private const string ScenarioId = "italy-500bc";
    private const int SettleFrames = 8;

    private readonly List<(int WaitFrames, Action Run)> _plan = new();
    private RulesetChooserScreen _chooser = null!;
    private MainGameScreen? _mainGame;
    private bool _ok = true;
    private int _frame;
    private int _planIndex;

    public override void _Ready()
    {
        _plan.Add((0, ShowChooser));
        _plan.Add((SettleFrames, ChooseItalyAndStart));
        _plan.Add((SettleFrames, CheckGame));
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

    private void ShowChooser()
    {
        _chooser = new RulesetChooserScreen();
        _chooser.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_chooser);
    }

    private void ChooseItalyAndStart()
    {
        Check(_chooser.ScenarioCardButtons.ContainsKey(ScenarioId), "New Game offers the Italy, 500 BC card");
        var card = RulesetPresets.CardsFor(GameDataContext.Repository).Single(c => c.ScenarioId == ScenarioId);
        RulesetCard? continued = null;
        _chooser.Continued += c => continued = c;
        _chooser.SelectCard(card);
        _chooser.ConfirmSelection();
        Check(continued?.ScenarioId == ScenarioId, "Continue hands on the Italy card");
        _chooser.QueueFree();

        var selection = new NewGameSelection { SelectedCard = card, HumanNationId = "rome" };
        _mainGame = new MainGameScreen
        {
            Session = GameSessionFactory.CreateSession(GameDataContext.Repository, selection),
            RepositoryRoot = GameDataContext.RepositoryRoot,
        };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);
    }

    private void CheckGame()
    {
        var world = _mainGame!.Session.World;
        Check(world.Id == ScenarioId && world.Width == 209 && world.Height == 233, $"the game runs on the Italy world ({world.Id}, {world.Width}×{world.Height})");
        Check(_mainGame.Session.State.Calendar.YearBc == 500, $"the game starts in 500 BC ({_mainGame.Session.State.Calendar.YearBc} BC)");
        var popup = _mainGame.MenuBar.MenuForCheck("Nations");
        var rows = popup is null ? 0 : Enumerable.Range(0, popup.ItemCount).Count(i => popup.GetItemText(i).Length > 0);
        Check(rows == 13, $"the Nations menu lists the twelve nations and All nations ({rows} rows)");
        var terrain = _mainGame.MapView.TerrainDrawRectForCheck;
        Check(terrain.Encloses(new Rect2(Vector2.Zero, _mainGame.MapView.Size)), "the map fills its view");
    }

    private void Finish()
    {
        var exitCode = _ok ? 0 : 1;
        GD.Print($"ItalyLaunchCheck: exiting with code {exitCode}.");
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
