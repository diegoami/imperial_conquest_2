using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class RecruitQualityCheck : Node
{
    private bool _ok = true;
    public override async void _Ready()
    {
        try
        {
            GetTree().Root.Size = new Vector2I(1500, 1000);
            GetTree().Root.ContentScaleSize = new Vector2I(1500, 1000);
            var data = GameDataContext.Repository.Resolve("toy-3city");
            var world = data.World with { Nations = ValueList.From(data.World.Nations.Select(n => n.Id == "north"
                ? n with { RecruitmentSlots = ValueList.Of(new RecruitmentSlot("arx", "light_infantry", 1000, 16),
                    new RecruitmentSlot("arx", "light_infantry", 1000, 24),
                    new RecruitmentSlot("arx", "light_infantry", 1000, 0),
                    new RecruitmentSlot("arx", "heavy_cavalry", 900, 12)) } : n)) };
            var session = new GameSession(world, data.Ruleset, data.Scenario);
            var dialog = new RecruitUnitDialog { Session = session, Submit = line => session.Submit(line).Lines,
                Size = new Vector2(1500, 1000) };
            AddChild(dialog);
            dialog.SelectCityForCheck("arx");
            await Frames();
            var beforeSelection = session.State;
            var list = dialog.TrainingListForCheck;
            var mobilize = dialog.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == "Mobilize");
            var recruit = dialog.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == "Recruit unit");
            Check(recruit.GetGlobalRect().End.Y <= list.GetGlobalRect().Position.Y
                && mobilize.GetGlobalRect().Position.Y >= list.GetGlobalRect().End.Y,
                "Recruit sits with creation controls above the queue; Mobilize sits below it");
            Click(list, 0);
            Check(list.GetSelectedItems().SequenceEqual(new[] { 0 }) && !mobilize.Disabled, "plain click selects one ready regiment");
            Click(list, 2);
            Check(list.GetSelectedItems().SequenceEqual(new[] { 2 }) && mobilize.Disabled,
                "plain click replaces selection with the unready regiment");
            Click(list, 0);
            Check(list.GetSelectedItems().SequenceEqual(new[] { 0 }) && !mobilize.Disabled,
                "selecting a ready regiment again removes the stale unready selection and re-enables Mobilize");
            Click(list, 2, ctrl: true);
            Check(list.GetSelectedItems().SequenceEqual(new[] { 0, 2 }) && mobilize.Disabled,
                "Ctrl-click adds an unready regiment without losing the ready one");
            Click(list, 2, ctrl: true);
            Check(list.GetSelectedItems().SequenceEqual(new[] { 0 }) && !mobilize.Disabled,
                "Ctrl-click removes the unready regiment and immediately re-enables Mobilize");
            Click(list, 0);
            Click(list, 1, shift: true);
            Check(list.GetSelectedItems().SequenceEqual(new[] { 0, 1 }) && !mobilize.Disabled,
                "Shift-click selects the ready range using the native selection anchor");
            Check(session.State == beforeSelection, "selection changes no campaign resources or random state");
            Check(dialog.TrainingRowTextsForCheck.Contains("light_infantry - 1000 troops - very poor"),
                "minimum-ready regiment visibly reports very poor quality");
            Check(dialog.TrainingRowTextsForCheck.Contains("light_infantry - 1000 troops - average"),
                "further training displays its higher actual mobilization quality");
            Check(dialog.TrainingRowTextsForCheck.Contains("light_infantry - 1000 troops - ready in 8 weeks"),
                "unfinished training shows its actual ETA after the type and troop count");
            Check(dialog.TrainingRowTextsForCheck.Contains("heavy_cavalry - 900 troops - ready in 2 weeks"),
                "the compact cavalry example uses actual readiness data");
            var row = dialog.TrainingRowsForCheck.ToList().FindIndex(r => r.StateCode == 16);
            dialog.SelectTrainingRowForCheck(row);
            Check(dialog.TrainingLocationForCheck == "Training at " + session.State.CityById("arx")!.Name,
                "selected regiment's city is visible separately from its compact caption");
            dialog.MobilizeForCheck();
            Check(session.State.Armies.Any(a => a.Nation == "north" && a.Units.Any(u => u.Quality == 4)),
                "displayed very poor quality matches the actual mobilized unit");
            RemoveChild(dialog);
            dialog.QueueFree();
        }
        catch (Exception ex) { GD.PrintErr($"RecruitQualityCheck: {ex}"); _ok = false; }
        GD.Print($"RecruitQualityCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }
    private void Check(bool value, string message)
    {
        GD.Print((value ? "PASS: " : "FAIL: ") + message);
        _ok &= value;
    }

    private async Task Frames()
    { for (var i = 0; i < 6; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    private void Click(ItemList list, int row, bool ctrl = false, bool shift = false)
    {
        var position = list.GlobalPosition + list.GetItemRect(row).GetCenter();
        var viewport = GetViewport();
        viewport.PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position }, true);
        foreach (var pressed in new[] { true, false })
            viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = pressed,
                CtrlPressed = ctrl, ShiftPressed = shift, Position = position, GlobalPosition = position,
                ButtonMask = pressed ? MouseButtonMask.Left : 0 }, true);
    }
}
