using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>T235: actual report visibility/scroll and human-turn/handoff routing.</summary>
public partial class HumanTurnNewsCheck : Node
{
    private bool _ok = true;

    public override async void _Ready()
    {
        try
        {
            await SingleHuman();
            await Hotseat();
        }
        catch (Exception ex) { GD.PrintErr($"HumanTurnNewsCheck: {ex}"); _ok = false; }
        GD.Print($"HumanTurnNewsCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private static GameSession Build(bool hotseat)
    {
        var data = GameDataContext.Repository.Resolve("toy-3city");
        var initial = new GameSession(data.World, data.Ruleset, data.Scenario, seedOverride: 1, humanSeatNationId: "north");
        var log = NewsLog.Empty;
        for (var i = 0; i < 40; i++) log = log.Append(new NewsEntry($"Report {i}"), data.Ruleset.NewsLog);
        var state = initial.State with {
            NewsLog = log,
            Armies = ValueList.From(initial.State.Armies.Where(a => a.Nation == "north")),
            Fleets = ValueList.From(initial.State.Fleets.Where(f => f.Nation == "north")),
            Relations = initial.State.Relations.WithRelation("north", "south", data.Ruleset.Diplomacy.StateCodes.Alliance),
            Nations = ValueList.From(initial.State.Nations.Select(n => n with {
                Control = hotseat || n.Id == "north" ? SeatControl.Human : SeatControl.Ai, Unity = 900, Treasury = 1000 })) };
        var save = new SaveGame(state.SchemaVersion, "news-check", "news-check", state.ScenarioId,
            state.WorldId, state.RulesetId, state);
        return new GameSession(data.World, data.Ruleset, data.Scenario, save);
    }

    private MainGameScreen AddMain(GameSession session)
    {
        var main = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot,
            Size = GetViewport().GetVisibleRect().Size };
        AddChild(main);
        return main;
    }

    private async Task Settle()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private static RichTextLabel Text(MainGameScreen main) => main.NewsLog.FindChildren("*", "RichTextLabel", true, false)
        .OfType<RichTextLabel>().Single();

    private void Latest(MainGameScreen main)
    {
        var text = Text(main);
        var last = main.Session.State.NewsLog.Slots.Last(e => !string.IsNullOrWhiteSpace(e.Text)).Text;
        var bar = text.GetVScrollBar();
        Check(main.NewsLog.Visible && text.Text.Contains(last, StringComparison.Ordinal), "human view shows the latest reports");
        Check(bar.Value >= bar.MaxValue - bar.Page - 1, "report view is scrolled to the latest end");
    }

    private async Task SingleHuman()
    {
        var session = Build(false);
        var before = session.State;
        var events = 0;
        session.EventsPublished += _ => events++;
        var main = AddMain(session);
        await Settle();
        Latest(main);
        Check(session.State == before && events == 0, "initial presentation changes no state/RNG and publishes no command events");
        main.NewsLog.FindChildren("*", "Button", true, false).OfType<Button>().Single(b => b.Text == "Dismiss")
            .EmitSignal(Button.SignalName.Pressed);
        main.SubmitForCheck("status");
        Check(!main.NewsLog.Visible, "ordinary commands respect dismissal within the current turn");
        main.NewsLog.Toggle();
        await Settle();
        Latest(main);
        main.NewsLog.Toggle();
        var turn = session.State.Calendar.TurnIndex;
        main.SubmitForCheck("end");
        await Settle();
        Check(session.State.ActiveNationId == "north" && session.State.Calendar.TurnIndex > turn,
            "the same human seat begins a new round after AI play");
        Latest(main);
        RemoveChild(main); main.QueueFree();
        await Settle();
    }

    private async Task Hotseat()
    {
        var session = Build(true);
        var main = AddMain(session);
        await Settle();
        main.SubmitForCheck("end");
        await Settle();
        Check(main.ActiveOverlay is HotseatHandoffScreen, "human seat change first shows the existing handoff curtain");
        Check(!main.NewsLog.Visible, "incoming reports are not exposed before handoff acknowledgement");
        if (main.ActiveOverlay is HotseatHandoffScreen screen) screen.Continue();
        await Settle();
        Latest(main);
        RemoveChild(main); main.QueueFree();
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
