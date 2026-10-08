using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T118's (#637) visual-review screenshot tour: a windowed run that drives the real
/// <see cref="AppRoot"/> through the New Game flow, then shows the <see cref="EndTurnBox"/> in the two
/// states <c>EndTurnBoxCheck.cs</c> exercises — the single supply line and the five-line cap — and saves
/// one screenshot of each. <strong>Headless screenshots do not work</strong> (issue #156: the dummy
/// renderer has no texture to read back), so this must be run windowed:
/// <c>godot.cmd --path godot res://Checks/EndTurnBoxScreenshotTour.tscn --quit-after 60</c>, with
/// <c>IC2_SCREENSHOT_DIR</c> set to a directory outside the repository. The produced PNGs are posted on
/// the task's PR for the human visual review the hazard gate asks for.
/// </summary>
/// <remarks>
/// The two warning states come from the same fixture <c>EndTurnBoxCheck.BuildSession</c> builds —
/// mirrored here rather than made public, because this tour is a sibling of the check, not a client of
/// it. A fresh game's own session cannot produce those exact states, so the tour drives the real New
/// Game flow first to prove the wiring, then swaps in a fixture-built <see cref="MainGameScreen"/> for
/// each capture (the real flow's screen is hidden behind it).
/// </remarks>
public partial class EndTurnBoxScreenshotTour : Control
{
    private const int SettleFrames = 6;

    private AppRoot _appRoot = null!;
    private MainGameScreen? _mainGame;
    private string _outputDirectory = string.Empty;
    private int _frame;
    private int _step;

    public override void _Ready()
    {
        _outputDirectory = System.Environment.GetEnvironmentVariable("IC2_SCREENSHOT_DIR") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_outputDirectory))
        {
            GD.PrintErr("EndTurnBoxScreenshotTour: IC2_SCREENSHOT_DIR is not set. Nothing will be captured.");
        }
        else
        {
            Directory.CreateDirectory(_outputDirectory);
        }

        // Same anchor/size caveat ScreenshotTour.cs documents: a scene reached by an explicit --path
        // argument (not the project's own run/main_scene) never gets its Size resolved from its anchors,
        // so this is set directly instead.
        Size = GetViewport().GetVisibleRect().Size;

        _appRoot = new AppRoot();
        _appRoot.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_appRoot);
    }

    public override void _Process(double delta)
    {
        _frame++;

        switch (_step)
        {
            case 0 when _frame >= SettleFrames:
                // Reach the main game screen the same way ScreenshotTour.cs's own tour does (default
                // ruleset card, seat 0).
                _appRoot.ShowNewGameFlow();
                _frame = 0;
                _step = 1;
                break;

            case 1 when _frame >= SettleFrames:
                ((NewGameFlow)_appRoot.CurrentScreen!).Chooser!.ConfirmSelection();
                _frame = 0;
                _step = 2;
                break;

            case 2 when _frame >= SettleFrames:
                ((NewGameFlow)_appRoot.CurrentScreen!).SeatScreen!.ConfirmSeatAndStart();
                // The real New Game flow has now reached MainGameScreen (point 3). Its own fresh
                // session has no warning, so the two captured states come from EndTurnBoxCheck's own
                // fixture; hide the real flow so only the fixture screen is in the viewport.
                _appRoot.Visible = false;
                ShowFixture(warning: true, sixWarnings: false);
                _frame = 0;
                _step = 3;
                break;

            case 3 when _frame >= SettleFrames:
                _mainGame!.MenuBar.PressItemForCheck("game.end_turn");
                _frame = 0;
                _step = 4;
                break;

            case 4 when _frame >= SettleFrames:
                Capture("01-one-line.png");
                if (_mainGame!.ActiveOverlay is EndTurnBox box)
                {
                    box.MakeMoreMovesButton.EmitSignal(Button.SignalName.Pressed);
                }

                _frame = 0;
                _step = 5;
                break;

            case 5 when _frame >= SettleFrames:
                ShowFixture(warning: true, sixWarnings: true);
                _frame = 0;
                _step = 6;
                break;

            case 6 when _frame >= SettleFrames:
                _mainGame!.MenuBar.PressItemForCheck("game.end_turn");
                _frame = 0;
                _step = 7;
                break;

            case 7 when _frame >= SettleFrames:
                Capture("02-five-lines.png");
                GD.Print("EndTurnBoxScreenshotTour: done.");
                GetTree().Quit(0);
                break;
        }
    }

    /// <summary>Swaps the fixture-built main game screen for the requested warning state.</summary>
    private void ShowFixture(bool warning, bool sixWarnings)
    {
        if (_mainGame is not null)
        {
            RemoveChild(_mainGame);
            _mainGame.QueueFree();
        }

        _mainGame = new MainGameScreen
        {
            Session = BuildSession(warning, sixWarnings),
            RepositoryRoot = GameDataContext.RepositoryRoot,
        };
        _mainGame.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_mainGame);
    }

    private void Capture(string fileName)
    {
        if (string.IsNullOrWhiteSpace(_outputDirectory))
        {
            return;
        }

        var path = Path.Combine(_outputDirectory, fileName);
        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(path);
        GD.Print(error == Error.Ok
            ? $"EndTurnBoxScreenshotTour: saved {path}"
            : $"EndTurnBoxScreenshotTour: could not save {path}: {error}");
    }

    // The fixture below mirrors EndTurnBoxCheck.BuildSession (and its WarningArmy helper) line for line:
    // the tour is a sibling of the check, not a client, so the check's private builder stays private.
    private static GameSession BuildSession(bool warning, bool sixWarnings = false)
    {
        var resolved = GameDataContext.Repository.Resolve("toy-3city");
        var ruleset = resolved.Ruleset with
        {
            Flags = resolved.Ruleset.Flags with { EndTurnWarningScope = EndTurnWarningScope.EveryUnit },
        };
        var initial = new GameSession(
            resolved.World, ruleset, resolved.Scenario, seedOverride: 1, humanSeatNationId: "north");

        var state = initial.State;
        if (sixWarnings)
        {
            var land = WarningArmy("north-land-warning", embarked: false, money: -1);
            var aboard = WarningArmy("north-sea-warning", embarked: true, money: -1);
            var fleet = state.Fleets.Single(fleet => fleet.Id == "north-fleet-1") with
            {
                X = 0,
                Y = 3,
                Ships = 10,
                SupplyTons = 1,
                ConditionPercent = 100,
                CarriedArmyId = aboard.Id,
            };
            state = state with
            {
                Armies = ValueList.Of(land, aboard),
                Fleets = ValueList.Of(fleet),
            };
        }
        else
        {
            // One 50,000-troop army: supply 99 is under the 20% line (500 × 99 = 49,500 < 50,000) and
            // supply 1,000 is over it, while the full purse keeps the mercenary-pay check silent (a
            // regular-only roster owes nothing). The fleet sits on Arx's tile, docked, supplied and
            // fresh, so it trips nothing.
            var land = WarningArmy("north-land-warning", embarked: false, money: 100_000)
                with { SupplyTons = warning ? 99 : 1_000 };
            var fleet = state.Fleets.Single(fleet => fleet.Id == "north-fleet-1") with
            {
                X = 2,
                Y = 1,
                SupplyTons = 1_000,
                ConditionPercent = 100,
            };
            state = state with
            {
                Armies = ValueList.Of(land),
                Fleets = ValueList.Of(fleet),
            };
        }

        var save = new SaveGame(
            state.SchemaVersion,
            "end-turn-box-check",
            "end-turn-box-check",
            state.ScenarioId,
            state.WorldId,
            state.RulesetId,
            state);
        return new GameSession(resolved.World, ruleset, resolved.Scenario, save);
    }

    private static ArmyState WarningArmy(string id, bool embarked, int money) => new(
        id,
        "north",
        X: embarked ? 0 : 2,
        Y: embarked ? 3 : 1,
        Moves: 0,
        Morale: 70,
        Money: money,
        SupplyTons: 99,
        CoveredTileCode: embarked ? null : 2,
        AboardFleetId: embarked ? "north-fleet-1" : null,
        Units: ValueList.Of(new UnitSlot(0, "light_infantry", 50_000, 6, "Regulars")));
}
