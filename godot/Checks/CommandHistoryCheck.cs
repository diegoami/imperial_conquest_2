using Godot;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

public partial class CommandHistoryCheck : Control
{
    private bool _ok = true;
    public override async void _Ready()
    {
        try
        {
            Size = new Vector2(1500, 850);
            var selection = new NewGameSelection
            {
                SelectedCard = RulesetPresets.CardForScenario(GameDataContext.Repository.Resolve("toy-3city"))
            };
            selection.HumanNationIds.AddRange(new[] { "north", "south" });
            var session = GameSessionFactory.CreateSession(GameDataContext.Repository, selection);
            var game = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot };
            game.SetAnchorsPreset(LayoutPreset.FullRect);
            AddChild(game);
            await Settle();
            var before = session.State;
            for (var i = 0; i < 105; i++) game.SubmitForCheck("invalid-history-" + i);
            await Settle();
            Check(ReferenceEquals(before, session.State), "refused history examples preserve campaign/RNG state");
            Check(game.CommandLog.History.Entries.Count == 100
                && game.CommandLog.History.Entries[0].Contains("invalid-history-5", StringComparison.Ordinal)
                && game.CommandLog.History.Entries[^1].Contains("invalid-history-104", StringComparison.Ordinal), "only the latest 100 real result blocks remain in order");
            var scroll = game.CommandLog.GetVScrollBar();
            Check(scroll.MaxValue > scroll.Page && scroll.Value + scroll.Page >= scroll.MaxValue - 1, "history scrolls and newest result is at bottom");
            scroll.Value = 0;
            await Settle();
            Check(scroll.Value == 0, "older retained results can be read by scrolling back");
            game.Panels.SetOpen("Log", false);
            game.SubmitForCheck("hidden-result");
            Check(!game.Panels.IsOpen("Log") && game.CommandLog.LatestResult.Contains("hidden-result", StringComparison.Ordinal), "new output updates hidden log without reopening it");
            game.Panels.SetOpen("Log", true);
            await Settle();
            Check(game.CommandLog.Text.EndsWith(game.CommandLog.LatestResult, StringComparison.Ordinal), "reopened log has current complete content");
            game.SubmitForCheck("end");
            await Settle();
            Check(session.State.ActiveNationId == "south" && game.CommandLog.History.Entries.Count == 0
                && game.CommandLog.Text.Length == 0, "real hotseat handoff clears previous player's history before display");
            RemoveChild(game); game.QueueFree();
        }
        catch (Exception ex) { Check(false, ex.ToString()); }
        GD.Print($"CommandHistoryCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }
    private async Task Settle() { for (var i = 0; i < 8; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }
    private void Check(bool condition, string message) { GD.Print((condition ? "PASS: " : "FAIL: ") + message); _ok &= condition; }
}
