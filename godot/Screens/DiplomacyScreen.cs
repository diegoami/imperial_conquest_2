using IC2.Slice.Diagnostics;
using Godot;
using IC2.Engine.Diplomacy;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Screens;

/// <summary>
/// <c>docs/game-design.md</c> §"User interface" item 4: "the original's peace/trade/ally/war grid per
/// nation (the International Relations screen) — already a clear, working UI pattern, reused as-is."
/// </summary>
/// <remarks>
/// Issues orders only through <see cref="GameSession.Submit"/>, with the existing CLI verbs
/// (<c>declare-war</c>, <c>make-peace</c>, <c>propose-alliance</c>, <c>propose-trade</c>,
/// <c>accept-offer</c>) — <c>docs/tasks/T25.md</c>'s own Owns note: "never mutate GameState from the UI."
/// The grid's own rows and which actions each one currently allows come from
/// <see cref="DiplomacyGridViewModel.BuildRows"/>, never re-derived here.
/// </remarks>
public partial class DiplomacyScreen : Control
{
    public required GameSession Session { get; init; }

    /// <summary>Raised after this screen issues a command, so <see cref="MainGameScreen"/> can refresh the
    /// map/news the same way it does for <see cref="ContextPanel"/> and <see cref="GameMapView"/>.</summary>
    public event Action<IReadOnlyList<string>>? CommandIssued;

    /// <summary>Raised when the player dismisses this screen.</summary>
    public event Action? Closed;

    /// <summary>Exposed for <c>godot/Screens/Checks/**</c> to assert on the rendered grid without re-deriving it.</summary>
    public GridContainer Grid { get; private set; } = null!;
    public Label RelationshipSummary { get; private set; } = null!;
    public Label FeedbackLabel { get; private set; } = null!;
    public Label? PendingOfferLabel { get; private set; }
    public Button? AcceptOfferButton { get; private set; }
    private string _feedback = string.Empty;

    private VBoxContainer _content = null!;

    public override void _Ready() => Rebuild();

    private void Rebuild()
    {
        foreach (var child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }

        ModalOverlay.Build(out var root, out var content, AppTheme.Scrim, minimumWidth: 560f);
        AddChild(root);
        _content = content;

        content.AddChild(UiKit.MakeLabel("International Relations", AppTheme.Header, UiKit.AccentColor));
        var nation = Session.State.NationById(Session.State.ActiveNationId);
        content.AddChild(UiKit.MakeLabel($"Your nation: {nation?.Name ?? Session.State.ActiveNationId}", AppTheme.Body, UiKit.TextColor));
        var rows = DiplomacyGridViewModel.BuildRows(Session.State, Session.Ruleset, Session.State.ActiveNationId);
        string Names(DiplomaticStatus status) => string.Join(", ", rows.Where(r => r.Status == status).Select(r => r.NationName)) is { Length: > 0 } names ? names : "none";
        var partners = TradePartnerCap.CurrentPartners(Session.State, Session.Ruleset, Session.State.ActiveNationId, Session.State.ActiveNationId);
        var partnerNames = partners.Count == 0 ? "none" : string.Join(", ", partners.Select(id => Session.State.NationById(id)?.Name ?? id));
        RelationshipSummary = UiKit.MakeLabel($"At war with: {Names(DiplomaticStatus.War)}\nTrading ({partners.Count}/{Session.Ruleset.Diplomacy.MaxTradePartners}): {partnerNames}", AppTheme.Body, UiKit.TextColor);
        RelationshipSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(RelationshipSummary);

        BuildPendingOfferRow();
        FeedbackLabel = UiKit.MakeLabel(_feedback, AppTheme.Body, UiKit.TextColor);
        FeedbackLabel.Name = "DiplomacyFeedback";
        FeedbackLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        FeedbackLabel.Visible = _feedback.Length > 0;
        content.AddChild(FeedbackLabel);

        Grid = new GridContainer { Columns = 6 };
        Grid.AddThemeConstantOverride("h_separation", AppTheme.DialogGap);
        Grid.AddThemeConstantOverride("v_separation", AppTheme.TableRowGap);

        // The grid has one row per nation (16 in the shipped scenario), taller than the window, so it sits
        // in a scroll container whose height is capped to what the viewport leaves after the title, the
        // pending-offer row and the Close button: the Close button always stays on screen (bug #968).
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        scroll.AddChild(Grid);
        content.AddChild(scroll);

        AddHeaderRow();
        foreach (var row in rows)
        {
            AddNationRow(row);
        }

        content.AddChild(new HSeparator());
        content.AddChild(UiKit.MakeButton("Close", Close));

        var cap = Math.Max(MinimumGridHeight, GetViewportRect().Size.Y - ReservedHeight);
        scroll.CustomMinimumSize = new Vector2(0, Math.Min(cap, Grid.GetCombinedMinimumSize().Y));
    }

    /// <summary>Height the window keeps for everything but the grid: title, pending offer, separator, the
    /// Close button, the panel's margins and the gaps between them.</summary>
    private const float ReservedHeight = 450f;

    private const float MinimumGridHeight = 120f;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            var viewport = GetViewport();
            Close();
            viewport.SetInputAsHandled();
        }
    }

    /// <summary>Dismisses this screen — what the "Close" button does, exposed under its own name for
    /// <c>godot/Screens/Checks/**</c>, the same convention <see cref="BattleResultScreen.Close"/> uses.</summary>
    public void Close() => Closed?.Invoke();

    private void AddHeaderRow()
    {
        Grid.AddChild(UiKit.MakeLabel("Nation", AppTheme.Body, UiKit.MutedTextColor));
        Grid.AddChild(UiKit.MakeLabel("Current status", AppTheme.Body, UiKit.MutedTextColor));
        Grid.AddChild(UiKit.MakeLabel("War", AppTheme.Body, UiKit.MutedTextColor));
        Grid.AddChild(UiKit.MakeLabel("Peace / end pact", AppTheme.Body, UiKit.MutedTextColor));
        Grid.AddChild(UiKit.MakeLabel("Offer alliance", AppTheme.Body, UiKit.MutedTextColor));
        Grid.AddChild(UiKit.MakeLabel("Offer trade", AppTheme.Body, UiKit.MutedTextColor));
    }

    private void AddNationRow(DiplomacyRelationRow row)
    {
        Grid.AddChild(UiKit.MakeLabel(row.NationName, AppTheme.Body, UiKit.TextColor));
        var light = UiKit.PanelColor.Luminance > 0.5f;
        var color = row.Status switch
        {
            DiplomaticStatus.War => new Color(light ? "#7A243A" : "#FF9D9D"),
            DiplomaticStatus.Trade => new Color(light ? "#1F4662" : "#7DDBD5"),
            DiplomaticStatus.Alliance => new Color(light ? "#28502A" : "#A8DC9B"),
            _ => UiKit.TextColor,
        };
        Grid.AddChild(UiKit.MakeLabel(row.StatusCaption, AppTheme.Body, color));
        Grid.AddChild(MakeActionButton("Declare War", row.CanDeclareWar, () => ConfirmDeclareWar(row.NationId)));
        var peaceCaption = row.Status == DiplomaticStatus.Trade ? "End trade" : row.Status == DiplomaticStatus.Alliance ? "End alliance" : "Make Peace";
        Grid.AddChild(MakeActionButton(peaceCaption, row.CanMakePeace, () => Issue($"make-peace {row.NationId}")));
        Grid.AddChild(MakeActionButton("Offer alliance", row.CanProposeAlliance, () => Issue($"propose-alliance {row.NationId}")));
        Grid.AddChild(MakeActionButton("Offer trade", row.CanProposeTrade, () => Issue($"propose-trade {row.NationId}")));
    }

    private void BuildPendingOfferRow()
    {
        var offer = Session.State.PendingOffer;
        PendingOfferLabel = null;
        AcceptOfferButton = null;
        if (offer is null)
        {
            return;
        }

        var proposer = Session.State.NationById(offer.ProposingNationId)?.Name ?? offer.ProposingNationId;
        var trade = offer.ProposedRelationCode == Session.Ruleset.Diplomacy.StateCodes.Trade;
        var kind = trade ? "trade" : "alliance";
        var active = Session.State.ActiveNationId;
        var valid = Session.State.NationById(offer.ProposingNationId) is { Eliminated: false }
            && Session.State.Relations.IndexOf(offer.ProposingNationId) >= 0;
        var alreadyActive = valid && Session.State.Relations.Get(active, offer.ProposingNationId) == offer.ProposedRelationCode;
        PendingOfferLabel = UiKit.MakeLabel(!valid ? $"The offer from {proposer} is no longer available."
            : alreadyActive ? $"{(trade ? "Trade" : "Alliance")} with {proposer} is already active."
            : $"{proposer} offers {kind} to {Session.State.NationById(active)?.Name ?? active}.", AppTheme.Body, UiKit.TextColor);
        PendingOfferLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _content.AddChild(PendingOfferLabel);
        AcceptOfferButton = UiKit.MakeButton($"Accept {kind} offer", () => Issue("accept-offer"));
        AcceptOfferButton.Disabled = alreadyActive || !valid;
        AcceptOfferButton.TooltipText = !valid ? "The proposing nation is no longer available."
            : alreadyActive ? "This agreement is already active." : $"Accept the {kind} offer from {proposer}.";
        _content.AddChild(AcceptOfferButton);
    }

    private Control MakeActionButton(string label, bool enabled, Action onPressed)
    {
        var button = UiKit.MakeButton(label, onPressed, AppTheme.Button);
        button.Disabled = !enabled;
        return button;
    }

    /// <summary>
    /// #1280 F4: Declare War asks first, naming every nation the war will be with (the target's allies
    /// join it); Yes issues <c>declare-war</c>, No or Esc changes nothing.
    /// </summary>
    private void ConfirmDeclareWar(string targetId)
    {
        var prompt = new ConfirmPrompt
        {
            Question = DiplomacyGridViewModel.WarDeclarationQuestion(Session.State, Session.Ruleset, Session.State.ActiveNationId, targetId),
        };
        void Dismiss()
        {
            RemoveChild(prompt);
            prompt.QueueFree();
        }

        prompt.Confirmed += () => { Dismiss(); Issue($"declare-war {targetId}"); };
        prompt.Refused += Dismiss;
        prompt.SetAnchorsPreset(LayoutPreset.FullRect);
        PendingPrompt = prompt;
        AddChild(prompt);
    }

    /// <summary>#1280 F4: the open Declare War confirmation, for the headless check.</summary>
    public ConfirmPrompt? PendingPrompt { get; private set; }

    private void Issue(string commandLine)
    {
        var output = AppDiagnostics.Submit(Session, commandLine, "diplomacy");
        _feedback = CommandOutcomeWording.Apply(CommandOutcomeText.OutcomeBlock(output.Lines));
        if (output.Lines.Any(line => line.Contains("rejected (diplomacy.trade-cap-reached)", StringComparison.Ordinal)))
            _feedback += "\nUse End trade to end an existing agreement first.";
        CommandIssued?.Invoke(output.Lines);

        // Rebuilds so the grid reflects the relation the command just changed immediately, the same
        // "act, then Refresh()" shape ContextPanel.Issue already uses.
        Rebuild();
    }
}
