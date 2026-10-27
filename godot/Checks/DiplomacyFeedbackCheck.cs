using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.Screens;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>Public synthetic campaigns; checks the production dialog rather than mirroring acceptance rules.</summary>
public partial class DiplomacyFeedbackCheck : Node
{
    private bool _ok = true;

    public override async void _Ready()
    {
        try
        {
            await CheckTrade(capped: true);
            await CheckTrade(capped: false);
            CheckWarRefusal();
            await CheckMainGameTradeRefusal();
        }
        catch (Exception ex) { GD.PrintErr($"DiplomacyFeedbackCheck: {ex}"); _ok = false; }
        GD.Print($"DiplomacyFeedbackCheck: exiting with code {(_ok ? 0 : 1)}.");
        CheckExit.Quit(this, _ok ? 0 : 1);
    }

    private static GameSession Campaign(bool capped)
    {
        var data = GameDataContext.Repository.Resolve("classical-mediterranean");
        var session = new GameSession(data.World, data.Ruleset, data.Scenario, humanSeatNationId: "rome");
        var state = session.State;
        var active = state.ActiveNationId;
        var codes = session.Ruleset.Diplomacy.StateCodes;
        var relations = state.Relations;
        foreach (var nation in state.Nations.Where(n => n.Id != active))
            relations = relations.WithRelation(active, nation.Id, codes.Peace);
        if (capped)
            foreach (var nation in state.Nations.Where(n => n.Id != active && n.Id != "media").Take(session.Ruleset.Diplomacy.MaxTradePartners))
                relations = relations.WithRelation(active, nation.Id, codes.Trade);
        SetState(session, state with { Relations = relations, PendingOffer = new PendingDiplomaticOffer("media", codes.Trade) });
        return session;
    }

    private async Task CheckTrade(bool capped)
    {
        var session = Campaign(capped);
        var before = session.State;
        var screen = new DiplomacyScreen { Session = session, Size = GetViewport().GetVisibleRect().Size };
        var commands = 0;
        screen.CommandIssued += _ => commands++;
        AddChild(screen);
        Check(screen.PendingOfferLabel!.Text.Contains("Media") && screen.PendingOfferLabel.Text.Contains("trade"),
            "pending offer identifies proposer and agreement type");
        Check(screen.RelationshipSummary.Text.Contains(capped ? "Trading (3/3)" : "Trading (0/3)"),
            "summary explicitly shows current trade count and limit");
        screen.AcceptOfferButton!.EmitSignal(Button.SignalName.Pressed);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(commands == 1 && screen.FeedbackLabel.Visible && screen.FeedbackLabel.GetLineCount() > 0,
            "one click dispatches once and leaves visible feedback inside the dialog");
        Check(screen.FeedbackLabel.Size.Y >= AppTheme.Body
              && GetViewport().GetVisibleRect().Encloses(screen.FeedbackLabel.GetGlobalRect()),
            "feedback has readable height and lies inside the viewport after layout");
        Check(!screen.FeedbackLabel.Text.Contains("diplomacy."), "feedback uses readable text rather than command identifiers");
        Check(screen.IsAncestorOf(screen.FeedbackLabel), "feedback belongs to the open modal");
        if (capped)
        {
            Check(screen.FeedbackLabel.Text.Contains("You can only trade with 3 nations") && screen.FeedbackLabel.Text.Contains("End trade"),
                "Media refusal explains the full cap and how to free a partner slot");
            Check(session.State.Relations.Get(before.ActiveNationId, "media") == session.Ruleset.Diplomacy.StateCodes.Peace
                  && session.State.RandomSeed == before.RandomSeed, "refusal preserves relations and RNG");
            Check(screen.Grid.GetChildren().OfType<Label>().Count(l => l.Text == "TRADING") == 3
                  && screen.Grid.GetChildren().OfType<Button>().Count(b => b.Text == "End trade" && !b.Disabled) == 3,
                "current partners and their End trade actions are clearly distinct");
        }
        else
        {
            Check(screen.FeedbackLabel.Text.Contains("accepted")
                  && session.State.Relations.Get(before.ActiveNationId, "media") == session.Ruleset.Diplomacy.StateCodes.Trade,
                "successful acceptance both changes the real relation and confirms it");
            Check(screen.RelationshipSummary.Text.Contains("Trading (1/3): Media") && screen.AcceptOfferButton!.Disabled
                  && screen.PendingOfferLabel!.Text.Contains("already active"), "accepted offer becomes an active agreement, not another invitation to accept");
            Check(session.State.PendingOffer == before.PendingOffer, "UI leaves original pending-offer persistence semantics intact");
        }
        RemoveChild(screen);
        screen.QueueFree();
    }

    private void CheckWarRefusal()
    {
        var session = Campaign(capped: false);
        SetState(session, session.State with { Relations = session.State.Relations.WithRelation(session.State.ActiveNationId, "media", session.Ruleset.Diplomacy.StateCodes.War) });
        var screen = new DiplomacyScreen { Session = session, Size = GetViewport().GetVisibleRect().Size };
        AddChild(screen);
        Check(screen.RelationshipSummary.Text.Contains("At war with: Media")
              && screen.Grid.GetChildren().OfType<Label>().Any(l => l.Text == "AT WAR"), "war partners appear in the summary and explicit current-status column");
        screen.AcceptOfferButton!.EmitSignal(Button.SignalName.Pressed);
        Check(screen.FeedbackLabel.Visible && screen.FeedbackLabel.Text.Contains("cannot trade with Media"),
            "stale offer at war explains its refusal instead of silently changing the relation");
        RemoveChild(screen);
        screen.QueueFree();
    }

    private static void SetState(GameSession session, GameState state) =>
        typeof(GameSession).GetProperty(nameof(GameSession.State))!.GetSetMethod(true)!.Invoke(session, new object[] { state });

    private async Task CheckMainGameTradeRefusal()
    {
        var campaign = Campaign(capped: true);
        var before = campaign.State;
        // Resume the controlled snapshot without constructor-time AI battle
        // reports waiting to replace the modal during the integration check.
        var save = new SaveGame(before.SchemaVersion, "trade-feedback", "trade-feedback",
            before.ScenarioId, before.WorldId, before.RulesetId, before);
        var session = new GameSession(campaign.World, campaign.Ruleset, campaign.Scenario, save);
        var main = new MainGameScreen { Session = session, RepositoryRoot = GameDataContext.RepositoryRoot,
            Size = GetViewport().GetVisibleRect().Size };
        AddChild(main);
        var commands = 0;
        main.CommandIssued += _ => commands++;
        main.OpenDiplomacyScreen();
        var screen = (DiplomacyScreen)main.ActiveOverlay!;
        screen.AcceptOfferButton!.EmitSignal(Button.SignalName.Pressed);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        Check(commands == 1 && ReferenceEquals(main.ActiveOverlay, screen), "trade refusal keeps the actual diplomacy modal and submits once");
        Check(screen.FeedbackLabel.IsVisibleInTree() && screen.FeedbackLabel.Text.Contains("You can only trade with 3 nations")
              && GetViewport().GetVisibleRect().Encloses(screen.FeedbackLabel.GetGlobalRect()),
            "actual main-game refusal is readable inside the active trade dialog");
        Check(main.OrderNoticeForCheck.Length == 0, "trade refusal does not create a covered duplicate notice behind its dialog");
        Check(session.State == before, "displaying the refusal leaves campaign state and RNG unchanged");
        RemoveChild(main);
        main.QueueFree();
    }

    private void Check(bool condition, string message)
    {
        GD.Print((condition ? "PASS: " : "FAIL: ") + message);
        _ok &= condition;
    }
}
