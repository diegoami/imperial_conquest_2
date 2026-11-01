using Godot;
using IC2.Engine.Battle;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>Presentation-only controls; no owner profile saves or settings writes.</summary>
public partial class BattleFeedbackCheck : Node
{
    private bool _ok = true;

    public override async void _Ready()
    {
        try
        {
            var data = GameDataContext.Repository.Resolve("toy-3city");
            var world = data.World with
            {
                StartingArmies = ValueList.From(data.World.StartingArmies.Select(a => a.Nation == "north"
                    ? a with { X = 3, Y = 2 } : a with { X = 4, Y = 2 })),
            };
            var session = new GameSession(world, data.Ruleset, data.Scenario, seedOverride: 1, humanSeatNationId: "north");
            session.Submit("declare-war south");
            session.Submit("attack-army north-army-1 south-army-1");
            var basis = BattleResultViewModel.FromResult(session.LastBattles.Single());
            var before = session.State;
            foreach (var kind in new[] { BattleKind.Field, BattleKind.Siege, BattleKind.Naval })
            foreach (var peace in new[] { "none", "agreed", "offered" })
            {
                // Deliberately varied view-model fixtures test presentation, not new resolver evidence.
                var view = basis with { Kind = kind, PeaceTreatyFired = peace == "agreed",
                    PeaceTreatyOffered = peace == "offered",
                    DefenderId = kind == BattleKind.Siege ? world.Cities[0].Id : basis.DefenderId };
                var screen = new BattleResultScreen { Session = session, Result = view };
                AddChild(screen);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                Check(screen.ReportTitleLabel.Text == view.ReportTitle, $"actual {kind} report heading");
                Check(screen.PeaceHeadingLabel?.Text == view.PeaceHeading, $"actual {peace} peace section");
                if (screen.PeaceHeadingLabel is { } heading)
                    Check(heading.GetParent().GetParent().GetIndex() < screen.WinnerLabel.GetIndex(),
                        "peace section precedes ordinary result details");
                if (kind == BattleKind.Siege)
                    Check(screen.FindChildren("*", "Label", true, false).OfType<Label>()
                        .Any(label => label.Text == $"Siege of {world.Cities[0].Name}"), "siege city appears prominently");
                Check(!screen.FindChildren("*", "Label", true, false).OfType<Label>()
                    .Any(label => label.Text == "No peace treaty followed."), "no routine no-peace filler");
                RemoveChild(screen);
                screen.QueueFree();
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            Check(ReferenceEquals(before, session.State), "report presentation changes no authoritative state");
        }
        catch (Exception ex) { GD.PrintErr($"BattleFeedbackCheck: {ex}"); _ok = false; }
        GD.Print($"BattleFeedbackCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
