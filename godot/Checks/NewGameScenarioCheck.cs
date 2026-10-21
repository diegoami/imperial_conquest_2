using System.Text.Json.Nodes;
using Godot;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Engine.Serialization;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T200 Done-when 2 and 3: a scenario marked <c>"listed": true</c> in a test-only data root appears in New Game;
/// starting it gives Nations rows and toolbar swatches for exactly that world's nations, each of which opens its
/// status panel; a save, load and resume rebuilds the same rows. Runs the real <see cref="RulesetChooserScreen"/>
/// and <see cref="MainGameScreen"/>.
/// </summary>
public partial class NewGameScenarioCheck : Control
{
    private const string ScenarioId = "example-tiny-duel";
    private const int SettleFrames = 6;

    private readonly string _dataRoot = Path.Combine(Path.GetTempPath(), "ic2-t200-" + Guid.NewGuid().ToString("N"));
    private readonly List<(int WaitFrames, Action Run)> _plan = new();
    private bool _ok = true;
    private int _frame;
    private int _planIndex;

    private GameDataRepository _repository = null!;
    private RulesetChooserScreen _chooser = null!;
    private RulesetCard? _continuedCard;
    private MainGameScreen? _mainGame;
    private string _savePath = string.Empty;

    public override void _Ready()
    {
        _repository = BuildTestDataRoot();
        GameDataContext.UseRepositoryForCheck(_repository);

        _plan.Add((0, ShowChooser));
        _plan.Add((SettleFrames, CheckChooserAndStart));
        _plan.Add((SettleFrames, () => CheckNationRows("new game")));
        _plan.Add((1, SaveAndResume));
        _plan.Add((SettleFrames, () => CheckNationRows("resumed game")));
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

    /// <summary>The shipped tiny-duel world, toy ruleset and scenario, with the scenario marked listed.</summary>
    private GameDataRepository BuildTestDataRoot()
    {
        var shipped = Path.Combine(GameDataContext.RepositoryRoot, "data");
        foreach (var dir in new[] { "worlds", "rulesets", "scenarios" })
        {
            Directory.CreateDirectory(Path.Combine(_dataRoot, dir));
        }

        File.Copy(Path.Combine(shipped, "worlds", "example-tiny-duel.json"), Path.Combine(_dataRoot, "worlds", "example-tiny-duel.json"));
        File.Copy(Path.Combine(shipped, "rulesets", "toy-ruleset.json"), Path.Combine(_dataRoot, "rulesets", "toy-ruleset.json"));
        var scenario = JsonNode.Parse(File.ReadAllText(Path.Combine(shipped, "scenarios", ScenarioId + ".json")))!.AsObject();
        scenario["listed"] = true;
        File.WriteAllText(Path.Combine(_dataRoot, "scenarios", ScenarioId + ".json"), scenario.ToJsonString());
        return GameDataRepository.Load(_dataRoot);
    }

    private void ShowChooser()
    {
        _chooser = new RulesetChooserScreen();
        _chooser.SetAnchorsPreset(LayoutPreset.FullRect);
        _chooser.Continued += card => _continuedCard = card;
        AddChild(_chooser);
    }

    private void CheckChooserAndStart()
    {
        Check(_chooser.CardButtons.Count == 2, $"both preset cards still render (found {_chooser.CardButtons.Count})");
        Check(_chooser.ScenarioCardButtons.ContainsKey(ScenarioId), $"the listed scenario '{ScenarioId}' has a card");
        Check(_chooser.ScenarioCardButtons.Count == 1, $"only the listed scenario gets a card (found {_chooser.ScenarioCardButtons.Count})");

        var card = RulesetPresets.CardsFor(_repository).Single(c => c.ScenarioId == ScenarioId);
        _chooser.SelectCard(card);
        Check(_chooser.ScenarioCardButtons[ScenarioId].ButtonPressed, "selecting the listed card highlights it");
        _chooser.ConfirmSelection();
        Check(_continuedCard?.ScenarioId == ScenarioId, "Continue hands on the listed scenario's card");
        _chooser.QueueFree();

        var resolved = _repository.Resolve(ScenarioId);
        var selection = new NewGameSelection { SelectedCard = _continuedCard!, HumanNationId = resolved.Scenario.Seats[0].Nation };
        ShowGame(GameSessionFactory.CreateSession(_repository, selection));
    }

    private void ShowGame(GameSession session)
    {
        _mainGame?.QueueFree();
        _mainGame = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        _mainGame.CheckSaveSuffix = "t200-newgamescenariocheck";
        AddChild(_mainGame);
    }

    private void CheckNationRows(string label)
    {
        var world = _mainGame!.Session.World;
        var expected = world.Nations.Select(n => n.Name).Append("All nations").ToArray();

        var popup = _mainGame.MenuBar.MenuForCheck("Nations");
        var items = popup is null
            ? Array.Empty<string>()
            : Enumerable.Range(0, popup.ItemCount).Select(i => popup.GetItemText(i)).Where(t => t.Length > 0).ToArray();
        Check(items.SequenceEqual(expected), $"{label}: the Nations menu lists exactly the world's nations ([{string.Join(", ", items)}])");

        foreach (var nation in world.Nations)
        {
            Check(_mainGame.Toolbar.ButtonFor($"nations.{nation.Id}") is not null, $"{label}: a toolbar swatch for {nation.Id}");
            Check(_mainGame.CommandTable.TryInvoke($"nations.{nation.Id}"), $"{label}: the {nation.Id} row runs");
            Check(_mainGame.ViewedNationId == nation.Id, $"{label}: the {nation.Id} row views that nation");
        }

        Check(_mainGame.Toolbar.ButtonFor("nations.rome") is null, $"{label}: no classical swatch on this world");
        Check(_mainGame.CommandTable.TryInvoke("nations.all"), $"{label}: All nations runs");
    }

    private void SaveAndResume()
    {
        var session = _mainGame!.Session;
        _savePath = Path.Combine(_dataRoot, "t200.json");
        session.Submit($"save {_savePath}");
        Check(File.Exists(_savePath), "the non-classical game saves");
        var save = SaveManager.LoadFile(_savePath, session.World, session.Ruleset);
        ShowGame(new GameSession(session.World, session.Ruleset, session.Scenario, save));
    }

    private void Finish()
    {
        GameDataContext.UseRepositoryForCheck(null);
        try
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"NewGameScenarioCheck: could not clean up '{_dataRoot}': {ex.Message}");
        }

        var exitCode = _ok ? 0 : 1;
        GD.Print($"NewGameScenarioCheck: exiting with code {exitCode}.");
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
