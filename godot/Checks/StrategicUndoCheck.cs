using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class StrategicUndoCheck : Control
{
    private MainGameScreen _main = null!;
    private GameState _before = null!;
    private string _subject = string.Empty;
    private int _frame, _stage;
    private bool _ok = true;

    public override void _Ready()
    {
        var data = GameDataContext.Repository.Resolve("toy-3city");
        _main = new MainGameScreen { Session = new GameSession(data.World, data.Ruleset, data.Scenario,
            humanSeatNationId: "north"), RepositoryRoot = GameDataContext.RepositoryRoot };
        _main.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_main);
        _main.StrategicTravel.CellSeconds = 10;
    }

    public override void _Process(double delta)
    {
        if (++_frame < 6) return;
        _frame = 0;
        try
        {
            switch (_stage++)
            {
                case 0:
                    Check(_main.StrategicUndoButton.Disabled, "Undo starts disabled without history");
                    _main.SelectArmyForCheck("north-army-1");
                    _subject = _main.ContextPanel.SubjectForCheck;
                    _before = _main.Session.State;
                    _main.SubmitForCheck("move north-army-1 3 3");
                    Check(_main.Session.State != _before, "a real move changes authoritative state");
                    break;
                case 1:
                    Check(_main.StrategicTravel.IsActive, "movement is still presenting before Undo");
                    Check(!_main.StrategicUndoButton.Disabled, "Undo is available during movement presentation");
                    _main.StrategicUndoButton.EmitSignal(BaseButton.SignalName.Pressed);
                    Check(_main.Session.State == _before, "button restores campaign and RNG exactly");
                    Check(!_main.StrategicTravel.IsActive, "Undo cancels stale movement frames");
                    Check(_main.ContextPanel.SubjectForCheck == _subject, "Undo restores the prior inspected selection");
                    _main.StrategicTravel.Advance(100);
                    Check(_main.LastCommandText.Contains("undone", StringComparison.Ordinal), "old completion cannot overwrite Undo feedback");
                    break;
                case 2:
                    Check(_main.StrategicUndoButton.Disabled, "Undo disables again at the turn boundary");
                    _main.SubmitForCheck("set-tax 10");
                    break;
                case 3:
                    GetViewport().PushInput(new InputEventKey { Keycode = Key.Z, PhysicalKeycode = Key.Z,
                        CtrlPressed = true, Pressed = true });
                    break;
                case 4:
                    Check(_main.Session.State == _before, "real Ctrl+Z input restores the last strategic action");
                    _main.SubmitForCheck("set-tax 12");
                    _main.SubmitForCheck("end");
                    break;
                case 5:
                    Check(!_main.Session.CanUndoStrategic, "End turn prevents undo of opponent actions");
                    Check(_main.Session.StrategicUndoCount == 0, "End turn releases the history");
                    GD.Print($"StrategicUndoCheck: exiting with code {(_ok ? 0 : 1)}.");
                    SetProcess(false);
                    CheckExit.Quit(this, _ok ? 0 : 1);
                    break;
            }
        }
        catch (Exception ex)
        {
            Check(false, ex.ToString());
            GD.Print("StrategicUndoCheck: exiting with code 1.");
            SetProcess(false); CheckExit.Quit(this, 1);
        }
    }

    private void Check(bool condition, string message)
    {
        if (condition) GD.Print("PASS: " + message);
        else { _ok = false; GD.PrintErr("FAIL: " + message); }
    }
}
