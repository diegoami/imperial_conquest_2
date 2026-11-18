using Godot;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T294f (#1847) Done-when: the New Game flow's random-map path — "Random map…" opens the generated-map
/// screen; style and seed generate the three candidate previews; picking one hands over to the seat
/// step listing the generated world's own nations; starting it produces a playable session whose world
/// is generated and whose save embeds the world and scenario, so it reloads through the shipped
/// repository alone (T294e). Runs the real <see cref="NewGameFlow"/>, <see cref="RandomMapScreen"/> and
/// <see cref="ScenarioSeatScreen"/>.
/// </summary>
public partial class RandomMapFlowCheck : Control
{
    private const int SettleFrames = 6;
    private const int MaxPollFrames = 900;

    private readonly List<(int WaitFrames, Action Run)> _plan = new();
    private NewGameFlow _flow = null!;
    private MainGameScreen? _mainGame;
    private string _savePath = string.Empty;
    private int _previewPolls;
    private bool _ok = true;
    private int _frame;
    private int _planIndex;

    public override void _Ready()
    {
        _plan.Add((0, ShowFlow));
        _plan.Add((SettleFrames, OpenRandomMap));
        _plan.Add((SettleFrames, GeneratePreviews));
        _plan.Add((1, WaitForPreviews));
        _plan.Add((SettleFrames, PickCandidate));
        _plan.Add((SettleFrames, StartGame));
        _plan.Add((SettleFrames, CheckGameAndSaveRoundTrip));
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

    private void ShowFlow()
    {
        _flow = new NewGameFlow();
        _flow.SetAnchorsPreset(LayoutPreset.FullRect);
        _flow.GameStarted += ShowGame;
        AddChild(_flow);
    }

    private void OpenRandomMap()
    {
        Check(_flow.Chooser is not null, "the flow opens on the ruleset chooser");
        Check(_flow.Chooser!.RandomMapButton is not null, "the chooser has a Random map… button");
        _flow.Chooser.RandomMapButton.EmitSignal(BaseButton.SignalName.Pressed);
        Check(_flow.RandomMapScreen is not null, "Random map… opens the generated-map screen");
        Check(_flow.SeatScreen is null, "the seat step is not reachable from the chooser's own child list");
    }

    private void GeneratePreviews()
    {
        var screen = _flow.RandomMapScreen!;
        Check(screen.StyleChoice.ItemCount == 5, $"the style dropdown offers the five shipped styles ({screen.StyleChoice.ItemCount})");
        screen.StyleChoice.Select(2); // central-asia
        screen.SeedEdit.Text = "not-a-number";
        screen.SeedEdit.EmitSignal(LineEdit.SignalName.TextChanged, screen.SeedEdit.Text);
        Check(screen.GenerateButton.Disabled, "an unparsable seed keeps Generate disabled");
        screen.SeedEdit.Text = "42";
        screen.SeedEdit.EmitSignal(LineEdit.SignalName.TextChanged, screen.SeedEdit.Text);
        Check(!screen.GenerateButton.Disabled, "a valid seed enables Generate");
        screen.GenerateButton.EmitSignal(BaseButton.SignalName.Pressed);
        Check(screen.GenerateButton.Disabled, "generation disables Generate until the previews are back");
    }

    private void WaitForPreviews()
    {
        var screen = _flow.RandomMapScreen!;
        var ready = screen.PreviewRects.Count > 0 && screen.PreviewRects.All(rect => rect.Texture is not null);
        if (!ready && _previewPolls < MaxPollFrames)
        {
            _previewPolls++;
            _planIndex--; // stay on this plan entry; generation runs off the UI thread
            return;
        }

        Check(ready, $"the three candidate previews rendered within {MaxPollFrames} frames");
        Check(screen.CandidateButtons.All(button => !button.Disabled), "each candidate is pickable once previews are back");
    }

    private void PickCandidate()
    {
        var screen = _flow.RandomMapScreen!;
        Check(_flow.SeatScreen is null, "the seat step still does not exist before a candidate is chosen");
        screen.CandidateButtons[1].EmitSignal(BaseButton.SignalName.Pressed);
        screen.ContinueButton.EmitSignal(BaseButton.SignalName.Pressed);
        Check(_flow.SeatScreen is not null, "a chosen candidate hands over to the seat step");
        var selection = _flow.SeatScreen!.Selection;
        Check(selection.GeneratedResolved is not null, "the seat step's selection carries the generated triple");
        var generated = selection.GeneratedResolved!;
        Check(generated.World.Id.StartsWith("random-central-asia-", StringComparison.Ordinal),
            $"the chosen candidate's world id ({generated.World.Id})");
        Check(generated.Scenario.Seats.Count == generated.World.Nations.Count,
            "the generated scenario seats every world nation");
        Check(generated.Ruleset.Id == _flow.SeatScreen.Selection.SelectedCard.RulesetId,
            "the generated game runs under the flow's first-step ruleset choice");
    }

    private void StartGame()
    {
        var seatScreen = _flow.SeatScreen!;
        var firstNation = seatScreen.Selection.GeneratedResolved!.World.Nations[0].Id;
        Check(seatScreen.HumanNationIds.Contains(firstNation), $"the generated world's first seat ({firstNation}) is ticked human");
        Check(seatScreen.CanStart, "Start Game is available");
        seatScreen.ConfirmSeatAndStart();
    }

    private void CheckGameAndSaveRoundTrip()
    {
        var session = _mainGame?.Session
                      ?? throw new InvalidOperationException("the flow did not start the main game screen");
        Check(session.World.Id.StartsWith("random-central-asia-", StringComparison.Ordinal), $"the playable session runs the generated world ({session.World.Id})");
        Check(session.EmbeddedWorld is not null && session.EmbeddedScenario is not null,
            "the session keeps its generated records for embedding");

        _savePath = Path.Combine(System.IO.Path.GetTempPath(), "ic2-t294f-" + Guid.NewGuid().ToString("N") + ".json");
        session.Submit($"save {_savePath}");
        Check(File.Exists(_savePath), "the generated game saves");

        var resolved = SaveManager.LoadFile(_savePath, GameDataContext.Repository);
        Check(resolved.World.Id == session.World.Id && resolved.Scenario.Id == session.Scenario.Id,
            "the save reloads its world and scenario from itself, through the shipped repository");
        var outcome = LoadGameModel.Open(_savePath, GameDataContext.Repository);
        Check(outcome.Session is not null, $"the load screen reopens the generated save ({outcome.Message ?? "opened"})");
        Check(outcome.Session?.EmbeddedWorld is not null, "the reopened session keeps riding its embedded records");
    }

    /// <summary>The started game lands here (the flow's own GameStarted event).</summary>
    public void ShowGame(GameSession session)
    {
        _mainGame?.QueueFree();
        _mainGame = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        _mainGame.CheckSaveSuffix = "t294f-randommapflowcheck";
        AddChild(_mainGame);
    }

    private void Finish()
    {
        GameDataContext.UseRepositoryForCheck(null);
        try
        {
            if (File.Exists(_savePath)) File.Delete(_savePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            GD.PrintErr($"RandomMapFlowCheck: could not clean up '{_savePath}': {ex.Message}");
        }

        var exitCode = _ok ? 0 : 1;
        GD.Print($"RandomMapFlowCheck: exiting with code {exitCode}.");
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
