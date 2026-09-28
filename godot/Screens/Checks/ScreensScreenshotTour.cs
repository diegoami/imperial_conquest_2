using Godot;
using IC2.Engine.Battle;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>
/// T25's own binding visual sign-off (Q-B): a windowed run that shows each of the three new screens and
/// saves one screenshot each — the battle result in both the destroyed and the scattered case, the
/// diplomacy grid, and the hotseat handoff both blind and not blind. <strong>Headless screenshots do not
/// work</strong> (issue #156, the dummy renderer has no texture to read back — see
/// <c>godot/Checks/ScreenshotTour.cs</c>'s own remarks, which this class follows exactly) — this must be
/// run windowed: <c>Godot_..._console.exe --path godot res://Screens/Checks/ScreensScreenshotTour.tscn
/// --quit-after 40</c>, with <c>IC2_SCREENSHOT_DIR</c> set to an existing directory outside the
/// repository.
/// </summary>
public partial class ScreensScreenshotTour : Control
{
    private const int SettleFrames = 6;

    private string _outputDirectory = string.Empty;
    private int _frame;
    private int _step;
    private Control? _current;

    public override void _Ready()
    {
        _outputDirectory = System.Environment.GetEnvironmentVariable("IC2_SCREENSHOT_DIR") ?? string.Empty;
        if (string.IsNullOrWhiteSpace(_outputDirectory))
        {
            GD.PrintErr("ScreensScreenshotTour: IC2_SCREENSHOT_DIR is not set. Nothing will be captured.");
        }
        else
        {
            Directory.CreateDirectory(_outputDirectory);
        }

        // Same fix as godot/Checks/ScreenshotTour.cs's own remarks: a scene reached by an explicit --path
        // argument (not the project's own configured main scene) never gets its root Control sized from
        // the viewport automatically, so this is set directly.
        Size = GetViewport().GetVisibleRect().Size;
    }

    public override void _Process(double delta)
    {
        _frame++;

        switch (_step)
        {
            case 0 when _frame >= SettleFrames:
                ShowNext(BattleScreen(LoserFate.Destroyed));
                _frame = 0;
                _step = 1;
                break;

            case 1 when _frame >= SettleFrames:
                Capture("01-battle-result-destroyed.png");
                ShowNext(BattleScreen(LoserFate.Scattered));
                _frame = 0;
                _step = 2;
                break;

            case 2 when _frame >= SettleFrames:
                Capture("02-battle-result-scattered.png");
                ShowNext(new DiplomacyScreen { Session = ToySession() });
                _frame = 0;
                _step = 3;
                break;

            case 3 when _frame >= SettleFrames:
                Capture("03-diplomacy-grid.png");
                ShowNext(new HotseatHandoffScreen
                {
                    Info = new HotseatHandoffInfo("south", "Southern Realm", Blind: false),
                    CalendarLine = "Week 1, 218 BC (turn 1)",
                });
                _frame = 0;
                _step = 4;
                break;

            case 4 when _frame >= SettleFrames:
                Capture("04-hotseat-handoff-not-blind.png");
                ShowNext(new HotseatHandoffScreen { Info = new HotseatHandoffInfo("south", "Southern Realm", Blind: true) });
                _frame = 0;
                _step = 5;
                break;

            case 5 when _frame >= SettleFrames:
                Capture("05-hotseat-handoff-blind.png");
                GD.Print("ScreensScreenshotTour: done.");
                GetTree().Quit(0);
                break;
        }
    }

    private void ShowNext(Control screen)
    {
        if (_current is not null)
        {
            RemoveChild(_current);
            _current.QueueFree();
        }

        _current = screen;
        screen.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(screen);
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
            ? $"ScreensScreenshotTour: saved {path}"
            : $"ScreensScreenshotTour: could not save {path}: {error}");
    }

    private static GameSession ToySession()
    {
        var toy = GameDataContext.Repository.Resolve("toy-3city");
        return new GameSession(toy.World, toy.Ruleset, toy.Scenario);
    }

    private static BattleResultScreen BattleScreen(LoserFate fate) => new()
    {
        Session = ToySession(),
        Result = BattleResultViewModel.FromResult(MakeBattleResult(fate)),
    };

    private static BattleResult MakeBattleResult(LoserFate fate) => new(
        Kind: BattleKind.Field,
        AttackerId: "north-army-1",
        DefenderId: "south-army-1",
        AttackerNationId: "north",
        DefenderNationId: "south",
        AttackerPower: 120,
        DefenderPower: 80,
        Winner: BattleSide.Attacker,
        AppliedDefeatOutcome: fate == LoserFate.Destroyed ? DefeatOutcome.Destroyed : DefeatOutcome.Scatter,
        LoserFate: fate,
        WinnerCasualties: 12,
        LoserCasualties: 80,
        UnitCasualties: ValueList<UnitCasualty>.Empty,
        Promotions: ValueList<UnitPromotion>.Of(new UnitPromotion(0, "Light Infantry", QualityBefore: 1, QualityAfter: 2, PromotedByRoll: true)),
        AbsorbedMoney: 250,
        AbsorbedSupplyTons: 10,
        WinnerUnityDelta: 5,
        LoserUnityDelta: -10,
        WinnerShipsLost: 0,
        WinnerConditionLost: 0,
        WinnerUnitsLost: 0,
        PeaceTreatyFired: fate == LoserFate.Destroyed,
        PeaceTreatyOffered: false,
        Scatter: fate == LoserFate.Scattered
            ? new ScatterOutcome(FromX: 4, FromY: 2, ToX: 5, ToY: 3, RequestedDistance: 2, ActualDistance: 2)
            : null);
}
