using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class RecruitQualityCheck : Node
{
    private bool _ok = true;
    public override void _Ready()
    {
        try
        {
            var data = GameDataContext.Repository.Resolve("toy-3city");
            var world = data.World with { Nations = ValueList.From(data.World.Nations.Select(n => n.Id == "north"
                ? n with { RecruitmentSlots = ValueList.Of(new RecruitmentSlot("arx", "light_infantry", 1000, 16),
                    new RecruitmentSlot("arx", "light_infantry", 1000, 24),
                    new RecruitmentSlot("arx", "light_infantry", 1000, 0),
                    new RecruitmentSlot("arx", "heavy_cavalry", 900, 12)) } : n)) };
            var session = new GameSession(world, data.Ruleset, data.Scenario);
            var dialog = new RecruitUnitDialog { Session = session, Submit = line => session.Submit(line).Lines };
            AddChild(dialog);
            dialog.SelectCityForCheck("arx");
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
}
