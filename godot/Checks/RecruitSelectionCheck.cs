using Godot;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>Real type buttons, Max and recruit callbacks. Optional --save/--city reads a private file without writing it.</summary>
public partial class RecruitSelectionCheck : Node
{
    private bool _ok = true;
    public override void _Ready()
    {
        try
        {
            var args = OS.GetCmdlineUserArgs();
            var path = args.FirstOrDefault(a => a.StartsWith("--save="))?[7..];
            var city = args.FirstOrDefault(a => a.StartsWith("--city="))?[7..] ?? "rome";
            var original = path is null ? null : File.ReadAllBytes(path);
            GameSession session;
            if (path is null)
            {
                var data = GameDataContext.Repository.Resolve("classical-mediterranean");
                session = new GameSession(data.World, data.Ruleset, data.Scenario, humanSeatNationId: "rome");
                var state = session.State with { Nations = ValueList.From(session.State.Nations.Select(n => n.Id == "rome"
                    ? n with { Treasury = -2149, RecruitmentSlots = ValueList<RecruitmentSlot>.Empty } : n)) };
                typeof(GameSession).GetProperty(nameof(GameSession.State))!.GetSetMethod(true)!.Invoke(session, new object[] { state });
            }
            else
            {
                var summary = SaveManager.PeekSummaryFile(path);
                var repository = GameDataContext.Repository;
                var world = repository.WorldById(summary.WorldId)!;
                var rules = repository.RulesetById(summary.RulesetId)!;
                session = new GameSession(world, rules, repository.ScenarioById(summary.ScenarioId)!, SaveManager.LoadFile(path, world, rules));
            }
            string lastCommand = "";
            var dialog = new RecruitUnitDialog { Session = session, Submit = line => { lastCommand = line; return session.Submit(line).Lines; } };
            AddChild(dialog);
            dialog.SelectCityForCheck(city);
            Check(dialog.SelectedCityIdForCheck == city && dialog.RecruitEnabledForCheck, "requested city is selected and eligible for recruitment");
            var buttons = dialog.FindChildren("*", "Button", true, false).OfType<Button>().ToArray();
            var types = dialog.ModelForCheck.UnitTypes;
            var typeButtons = types.Select(t => buttons.Single(b => b.TooltipText == t.Name)).ToArray();
            foreach (var type in types.Reverse())
            {
                var button = typeButtons.Single(b => b.TooltipText == type.Name);
                button.ButtonPressed = true;
                button.EmitSignal(Button.SignalName.Pressed);
                Check(typeButtons.Count(b => b.ButtonPressed) == 1 && button.ButtonPressed
                      && dialog.SelectedUnitTypeForCheck == type.Id, "exactly one real type button matches the selected recruitment type");
                buttons.Single(b => b.Text == "Max").EmitSignal(Button.SignalName.Pressed);
                var maximum = dialog.ModelForCheck.TroopBoundsFor(type.Id).Maximum;
                Check(dialog.TroopsForCheck == maximum, "Faithful Max selects the full battalion while the treasury is negative");
                var before = session.State.NationById(session.State.ActiveNationId)!.RecruitmentSlots.Count;
                dialog.RecruitForCheck();
                var slots = session.State.NationById(session.State.ActiveNationId)!.RecruitmentSlots;
                Check(slots.Count == before + 1 && slots[^1].UnitTypeId == type.Id && slots[^1].Troops == maximum
                      && slots[^1].TargetCityId == city && lastCommand.Contains(type.Id),
                    "recruited slot and submitted order match the visible selected type, town and Max quantity");
            }
            RemoveChild(dialog);
            dialog.QueueFree();
            if (path is not null) Check(original!.SequenceEqual(File.ReadAllBytes(path)), "private source save remains byte-for-byte unchanged");
        }
        catch (Exception ex) { GD.PrintErr($"RecruitSelectionCheck: {ex}"); _ok = false; }
        GD.Print($"RecruitSelectionCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
