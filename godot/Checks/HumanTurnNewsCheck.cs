using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>T260: persistent dismissal, newest-first reports, date and real scroll geometry.</summary>
public partial class HumanTurnNewsCheck : Node
{
    private bool _ok = true;

    public override async void _Ready()
    {
        try
        {
            await SingleHuman();
            await Hotseat();
            await DateAndLayout();
        }
        catch (Exception ex) { GD.PrintErr($"HumanTurnNewsCheck: {ex}"); _ok = false; }
        GD.Print($"HumanTurnNewsCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private static GameSession Build(bool hotseat, int season = 0, int week = 1)
    {
        var data = GameDataContext.Repository.Resolve("toy-3city");
        var initial = new GameSession(data.World, data.Ruleset, data.Scenario, seedOverride: 1, humanSeatNationId: "north");
        var log = NewsLog.Empty;
        for (var i = 0; i < 40; i++) log = log.Append(new NewsEntry($"Report {i}\n  Detail {i}"), data.Ruleset.NewsLog);
        var state = initial.State with {
            Calendar = initial.State.Calendar with { SeasonIndex = season, Week = week },
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
        Check(text.Text.StartsWith("News log:\n  " + main.Session.State.NewsLog.Slots.Last().Text, StringComparison.Ordinal),
            "newest entry is first without reversing its multiline detail");
        Check(bar.Value <= bar.MinValue + 1, "report view opens at the newest top");
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
        Check(!main.NewsLog.Visible, "dismissal survives the next human turn");
        main.NewsLog.Toggle();
        await Settle();
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
        main.NewsLog.Dismiss();
        main.SubmitForCheck("end");
        await Settle();
        if (main.ActiveOverlay is HotseatHandoffScreen next) next.Continue();
        await Settle();
        Check(!main.NewsLog.Visible, "dismissal survives acknowledged hotseat handoff");
        RemoveChild(main); main.QueueFree();
    }

    private async Task DateAndLayout()
    {
        var session = Build(false, season: 1, week: 11);
        var main = AddMain(session);
        await Settle();
        var date = main.FindChildren("CampaignDate", "Label", true, false).OfType<Label>().Single();
        Check(date.Text.Contains("Summer", StringComparison.Ordinal), "loaded summer appears in top-right date");
        var text = Text(main);
        var rect = text.GetGlobalRect();
        var panelRect = main.NewsLog.GetGlobalRect();
        Check(rect.Size.Y > 0 && rect.Position.Y >= panelRect.Position.Y && rect.End.Y <= panelRect.End.Y + 1,
            "news text viewport fits inside the actual panel including the bottom line");
        var bar = text.GetVScrollBar();
        bar.Value = bar.MaxValue;
        await Settle();
        Check(bar.Page > 0 && bar.Value >= bar.MaxValue - bar.Page - 1,
            "long wrapped news scrolls fully to its last entry");
        main.SubmitForCheck("end");
        await Settle();
        Check(date.Text.Contains("Autumn", StringComparison.Ordinal), "season updates when a turn crosses the boundary");
        RemoveChild(main); main.QueueFree();
        await Settle();
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
