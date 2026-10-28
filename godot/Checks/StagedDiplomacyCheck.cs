using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>Exercise the actual screen's final choices, confirmations and commit boundary.</summary>
public partial class StagedDiplomacyCheck : Node
{
    private bool _ok = true;
    public override async void _Ready()
    {
        try
        {
            await ChoicesAndCancel();
            CommitMixed();
            CommitWarsInDisplayedOrder();
            RefusalKeepsPlan();
            Stale();
        }
        catch (Exception ex) { GD.PrintErr($"StagedDiplomacyCheck: {ex}"); _ok = false; }
        GD.Print($"StagedDiplomacyCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private static GameSession Build(bool capped = false, bool noPacts = false)
    {
        var data = GameDataContext.Repository.Resolve("classical-mediterranean");
        var initial = new GameSession(data.World, data.Ruleset, data.Scenario, seedOverride: 42, humanSeatNationId: "rome");
        var state = initial.State;
        var codes = data.Ruleset.Diplomacy.StateCodes;
        var relations = state.Relations;
        foreach (var nation in state.Nations.Where(n => n.Id != "rome"))
            relations = relations.WithRelation("rome", nation.Id, codes.Peace);
        if (noPacts)
            foreach (var a in state.Nations)
                foreach (var b in state.Nations.Where(b => b.Id != a.Id))
                    relations = relations.WithRelation(a.Id, b.Id, codes.Peace);
        if (capped)
            foreach (var nation in state.Nations.Where(n => n.Id != "rome" && n.Id != "media").Take(3))
                relations = relations.WithRelation("rome", nation.Id, codes.Trade);
        state = state with { Relations = relations, PendingOffer = new PendingDiplomaticOffer("media", codes.Trade) };
        return new GameSession(data.World, data.Ruleset, data.Scenario,
            new SaveGame(state.SchemaVersion, "diplomacy-draft", "diplomacy-draft", state.ScenarioId, state.WorldId, state.RulesetId, state));
    }

    private DiplomacyScreen Open(GameSession session)
    {
        var screen = new DiplomacyScreen { Session = session, Size = GetViewport().GetVisibleRect().Size };
        AddChild(screen);
        return screen;
    }

    private static Button RowButton(DiplomacyScreen screen, string name, string caption)
    {
        var cells = screen.Grid.GetChildren();
        var start = Enumerable.Range(7, cells.Count - 7).First(i => i % 7 == 0 && cells[i] is Label l && l.Text == name);
        return cells.Skip(start).Take(7).OfType<Button>().First(b => b.Text == caption);
    }

    private static void Press(Button button) => button.EmitSignal(BaseButton.SignalName.Pressed);
    private void Remove(DiplomacyScreen screen) { RemoveChild(screen); screen.QueueFree(); }

    private async Task ChoicesAndCancel()
    {
        var session = Build();
        var before = session.State;
        var events = 0;
        session.EventsPublished += _ => events++;
        var screen = Open(session);
        var notifications = 0;
        screen.CommandIssued += _ => notifications++;
        Press(RowButton(screen, "Media", "Declare War"));
        Check(screen.PendingPrompt is not null && session.State == before, "war asks before staging and preserves campaign");
        screen.PendingPrompt!.AnswerForCheck(false);
        Check(screen.PendingPrompt is null && RowButton(screen, "Media", "Reset").Disabled,
            "No leaves no pending war");
        Press(RowButton(screen, "Media", "Declare War"));
        screen.PendingPrompt!.AnswerForCheck(true);
        Check(screen.Grid.GetChildren().OfType<Label>().Any(l => l.Text == "AT WAR (pending)") && session.State == before,
            "Yes previews war without publishing it");
        Press(screen.AcceptOfferButton!);
        Check(screen.RelationshipSummary.Text.Contains("At war with: none") && screen.RelationshipSummary.Text.Contains("Trading (1/3): Media"),
            "replacing pending war with accepted offer rebuilds from the opening relation");
        Press(RowButton(screen, "Media", "Reset"));
        Check(screen.RelationshipSummary.Text.Contains("Trading (0/3)") && !screen.AcceptOfferButton!.Disabled,
            "Reset removes a pending offer choice and its effects");
        Press(screen.AcceptOfferButton!);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(GetViewport().GetVisibleRect().Encloses(screen.OkButton.GetGlobalRect())
            && GetViewport().GetVisibleRect().Encloses(screen.CancelButton.GetGlobalRect()), "OK and Cancel remain visible with an offer and feedback");
        Press(screen.CancelButton);
        Check(ReferenceEquals(session.State, before) && events == 0 && notifications == 0,
            "Cancel discards all choices, including confirmed war and accepted offer, without RNG/news/events");
        Remove(screen);
    }

    private void CommitMixed()
    {
        var session = Build();
        var control = Build();
        var screen = Open(session);
        var observed = new List<GameState>();
        session.EventsPublished += _ => observed.Add(session.State);
        var notifications = 0;
        var closes = 0;
        screen.CommandIssued += _ => notifications++;
        screen.Closed += () => closes++;
        // Click in reverse display order. Commit must use displayed nation order.
        Press(screen.AcceptOfferButton!);
        Press(RowButton(screen, "Carthage", "Declare War"));
        screen.PendingPrompt!.AnswerForCheck(true);
        control.Submit("declare-war carthage");
        control.Submit("accept-offer");
        Press(screen.OkButton);
        Check(session.State == control.State, "mixed final choices match real command outcomes and RNG");
        Check(observed.Count == 1 && observed[0] == session.State && notifications == 1 && closes == 1,
            "OK publishes one completed campaign and closes once");
        Press(screen.OkButton);
        Check(observed.Count == 1 && notifications == 1 && closes == 1, "a repeated OK cannot apply or publish twice");
        Remove(screen);
    }

    private void CommitWarsInDisplayedOrder()
    {
        var session = Build(noPacts: true);
        var displayedOrder = Build(noPacts: true);
        var clickOrder = Build(noPacts: true);
        displayedOrder.Submit("declare-war carthage"); displayedOrder.Submit("declare-war media");
        clickOrder.Submit("declare-war media"); clickOrder.Submit("declare-war carthage");
        // Unlike the trade/war pair above, these commands do not commute: reports retain their order.
        Check(displayedOrder.State.NewsLog != clickOrder.State.NewsLog,
            "positive control: reversing the two wars changes the observable news history");
        var before = session.State;
        var screen = Open(session);
        Press(RowButton(screen, "Media", "Declare War")); screen.PendingPrompt!.AnswerForCheck(true);
        Press(RowButton(screen, "Carthage", "Declare War")); screen.PendingPrompt!.AnswerForCheck(true);
        Check(ReferenceEquals(before, session.State), "reversed clicks still leave the campaign untouched before OK");
        Press(screen.OkButton);
        Check(session.State == displayedOrder.State && session.State != clickOrder.State,
            "OK commits noncommuting choices in displayed nation order, including their report chronology");
        Remove(screen);
    }

    private void RefusalKeepsPlan()
    {
        var session = Build(capped: true);
        var before = session.State;
        var screen = Open(session);
        Press(RowButton(screen, "Greece", "Declare War"));
        screen.PendingPrompt!.AnswerForCheck(true);
        Press(screen.AcceptOfferButton!);
        Check(screen.FeedbackLabel.Text.Contains("You can only trade with 3 nations")
            && screen.RelationshipSummary.Text.Contains("At war with: Greece") && ReferenceEquals(session.State, before),
            "a refused combined choice leaves the previous private plan and campaign intact");
        Press(screen.CancelButton);
        Check(ReferenceEquals(session.State, before), "Cancel also discards a valid prefix after refusal");
        Remove(screen);
    }

    private void Stale()
    {
        var session = Build();
        var screen = Open(session);
        var closes = 0;
        screen.Closed += () => closes++;
        Press(screen.AcceptOfferButton!);
        session.Submit("declare-war carthage");
        var external = session.State;
        Press(screen.OkButton);
        Check(ReferenceEquals(session.State, external) && closes == 0 && screen.FeedbackLabel.Visible
            && screen.FeedbackLabel.Text.Contains("Reopen", StringComparison.OrdinalIgnoreCase),
            "stale OK leaves the dialog open with feedback and preserves external changes");
        Remove(screen);
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
