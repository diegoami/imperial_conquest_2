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
/// Stages existing CLI verbs through <see cref="DialogCommandDraft"/>; OK atomically applies them
/// (<c>declare-war</c>, <c>make-peace</c>, <c>propose-alliance</c>, <c>propose-trade</c>,
/// <c>accept-offer</c>) — <c>docs/tasks/T25.md</c>'s own Owns note: "never mutate GameState from the UI."
/// The grid's own rows and which actions each one currently allows come from
/// <see cref="DiplomacyGridViewModel.BuildRows"/>, never re-derived here.
/// </remarks>
public partial class DiplomacyScreen : Control
{
    public required GameSession Session { get; init; }

    /// <summary>Raised once after OK applies the final choices, so <see cref="MainGameScreen"/> can refresh the
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
    private DialogCommandDraft _draft = null!;
    private IC2.Engine.Model.GameState _openingState = null!;
    private readonly Dictionary<string, string> _choices = new(StringComparer.Ordinal);
    public Button OkButton { get; private set; } = null!;
    public Button CancelButton { get; private set; } = null!;
    private IC2.Engine.Model.GameState WorkingState => _draft.WorkingState;

    private VBoxContainer _content = null!;

    public override void _Ready()
    {
        _draft = Session.BeginDialogEdit(DialogEditKind.Diplomacy);
        _openingState = Session.State;
        Rebuild();
    }

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
        var nation = WorkingState.NationById(WorkingState.ActiveNationId);
        content.AddChild(UiKit.MakeLabel($"Your nation: {nation?.Name ?? WorkingState.ActiveNationId}", AppTheme.Body, UiKit.TextColor));
        content.AddChild(UiKit.MakeLabel("Changes stay pending until OK. Cancel discards them.", AppTheme.Body, UiKit.MutedTextColor));
        var rows = DiplomacyGridViewModel.BuildRows(WorkingState, Session.Ruleset, WorkingState.ActiveNationId);
        string Names(DiplomaticStatus status) => string.Join(", ", rows.Where(r => r.Status == status).Select(r => r.NationName)) is { Length: > 0 } names ? names : "none";
        var partners = TradePartnerCap.CurrentPartners(WorkingState, Session.Ruleset, WorkingState.ActiveNationId, WorkingState.ActiveNationId);
        var partnerNames = partners.Count == 0 ? "none" : string.Join(", ", partners.Select(id => WorkingState.NationById(id)?.Name ?? id));
        RelationshipSummary = UiKit.MakeLabel($"At war with: {Names(DiplomaticStatus.War)}\nTrading ({partners.Count}/{Session.Ruleset.Diplomacy.MaxTradePartners}): {partnerNames}", AppTheme.Body, UiKit.TextColor);
        RelationshipSummary.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(RelationshipSummary);

        BuildPendingOfferRow();
        FeedbackLabel = UiKit.MakeLabel(_feedback, AppTheme.Body, UiKit.TextColor);
        FeedbackLabel.Name = "DiplomacyFeedback";
        FeedbackLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        FeedbackLabel.Visible = _feedback.Length > 0;
        content.AddChild(FeedbackLabel);

        Grid = new GridContainer { Columns = 7 };
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
        var footer = new HBoxContainer();
        OkButton = UiKit.MakeButton("OK", Commit);
        CancelButton = UiKit.MakeButton("Cancel", Close);
        footer.AddChild(OkButton);
        footer.AddChild(CancelButton);
        content.AddChild(footer);

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

    /// <summary>Discards choices and dismisses this screen, exposed under its own name for
    /// <c>godot/Screens/Checks/**</c>, the same convention <see cref="BattleResultScreen.Close"/> uses.</summary>
    public void Close()
    {
        _draft.Cancel();
        Closed?.Invoke();
    }

    private void AddHeaderRow()
    {
        Grid.AddChild(UiKit.MakeLabel("Nation", AppTheme.Body, UiKit.MutedTextColor));
        Grid.AddChild(UiKit.MakeLabel("Status / preview", AppTheme.Body, UiKit.MutedTextColor));
        Grid.AddChild(UiKit.MakeLabel("War", AppTheme.Body, UiKit.MutedTextColor));
        Grid.AddChild(UiKit.MakeLabel("Peace / end pact", AppTheme.Body, UiKit.MutedTextColor));
        Grid.AddChild(UiKit.MakeLabel("Offer alliance", AppTheme.Body, UiKit.MutedTextColor));
        Grid.AddChild(UiKit.MakeLabel("Offer trade", AppTheme.Body, UiKit.MutedTextColor));
        Grid.AddChild(UiKit.MakeLabel("Pending choice", AppTheme.Body, UiKit.MutedTextColor));
    }

    private void AddNationRow(DiplomacyRelationRow row)
    {
        // Choices replace the opening relation, rather than acting on the previous
        // preview's cooldown/war. Final combined legality is still checked by commands.
        var original = DiplomacyGridViewModel.BuildRows(_openingState, Session.Ruleset, _openingState.ActiveNationId)
            .First(r => r.NationId == row.NationId);
        Grid.AddChild(UiKit.MakeLabel(row.NationName, AppTheme.Body, UiKit.TextColor));
        var light = UiKit.PanelColor.Luminance > 0.5f;
        var color = row.Status switch
        {
            DiplomaticStatus.War => new Color(light ? "#7A243A" : "#FF9D9D"),
            DiplomaticStatus.Trade => new Color(light ? "#1F4662" : "#7DDBD5"),
            DiplomaticStatus.Alliance => new Color(light ? "#28502A" : "#A8DC9B"),
            _ => UiKit.TextColor,
        };
        Grid.AddChild(UiKit.MakeLabel(row.StatusCaption + (_choices.ContainsKey(row.NationId) ? " (pending)" : ""), AppTheme.Body, color));
        Grid.AddChild(MakeActionButton("Declare War", original.CanDeclareWar, () => ConfirmDeclareWar(row.NationId)));
        var peaceCaption = original.Status == DiplomaticStatus.Trade ? "End trade" : original.Status == DiplomaticStatus.Alliance ? "End alliance" : "Make Peace";
        Grid.AddChild(MakeActionButton(peaceCaption, original.CanMakePeace, () => Choose(row.NationId, $"make-peace {row.NationId}")));
        Grid.AddChild(MakeActionButton("Offer alliance", original.CanProposeAlliance, () => Choose(row.NationId, $"propose-alliance {row.NationId}")));
        Grid.AddChild(MakeActionButton("Offer trade", original.CanProposeTrade, () => Choose(row.NationId, $"propose-trade {row.NationId}")));
        var reset = MakeActionButton("Reset", _choices.ContainsKey(row.NationId), () => Choose(row.NationId, null));
        reset.Name = $"Reset_{row.NationId}";
        Grid.AddChild(reset);
    }

    private void BuildPendingOfferRow()
    {
        var offer = WorkingState.PendingOffer;
        PendingOfferLabel = null;
        AcceptOfferButton = null;
        if (offer is null)
        {
            return;
        }

        var proposer = WorkingState.NationById(offer.ProposingNationId)?.Name ?? offer.ProposingNationId;
        var trade = offer.ProposedRelationCode == Session.Ruleset.Diplomacy.StateCodes.Trade;
        var kind = trade ? "trade" : "alliance";
        var active = WorkingState.ActiveNationId;
        var valid = WorkingState.NationById(offer.ProposingNationId) is { Eliminated: false }
            && WorkingState.Relations.IndexOf(offer.ProposingNationId) >= 0;
        var alreadyActive = valid && WorkingState.Relations.Get(active, offer.ProposingNationId) == offer.ProposedRelationCode;
        var pending = alreadyActive && _openingState.Relations.Get(active, offer.ProposingNationId) != offer.ProposedRelationCode;
        PendingOfferLabel = UiKit.MakeLabel(!valid ? $"The offer from {proposer} is no longer available."
            : pending ? $"{(trade ? "Trade" : "Alliance")} with {proposer} is pending. Press OK to apply."
            : alreadyActive ? $"{(trade ? "Trade" : "Alliance")} with {proposer} is already active."
            : $"{proposer} offers {kind} to {WorkingState.NationById(active)?.Name ?? active}.", AppTheme.Body, UiKit.TextColor);
        PendingOfferLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _content.AddChild(PendingOfferLabel);
        AcceptOfferButton = UiKit.MakeButton($"Accept {kind} offer", () => Choose(offer.ProposingNationId, "accept-offer"));
        AcceptOfferButton.Disabled = alreadyActive || !valid;
        AcceptOfferButton.TooltipText = !valid ? "The proposing nation is no longer available."
            : pending ? "This agreement is pending until OK."
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
            Question = DiplomacyGridViewModel.WarDeclarationQuestion(WorkingState, Session.Ruleset, WorkingState.ActiveNationId, targetId),
        };
        void Dismiss()
        {
            RemoveChild(prompt);
            prompt.QueueFree();
            PendingPrompt = null;
        }

        prompt.Confirmed += () => { Dismiss(); Choose(targetId, $"declare-war {targetId}"); };
        prompt.Refused += Dismiss;
        prompt.SetAnchorsPreset(LayoutPreset.FullRect);
        PendingPrompt = prompt;
        AddChild(prompt);
    }

    /// <summary>#1280 F4: the open Declare War confirmation, for the headless check.</summary>
    public ConfirmPrompt? PendingPrompt { get; private set; }

    private void Choose(string targetId, string? commandLine)
    {
        var choices = new Dictionary<string, string>(_choices, StringComparer.Ordinal);
        if (commandLine is null) choices.Remove(targetId);
        else choices[targetId] = commandLine;
        var plan = _openingState.Nations.Where(n => choices.ContainsKey(n.Id)).Select(n => choices[n.Id]);
        var result = _draft.ReplacePlan(plan);
        _feedback = result.Accepted ? "Pending changes updated. Press OK to apply, or Cancel to discard."
            : CommandOutcomeWording.Apply(CommandOutcomeText.OutcomeBlock(result.Output.Lines));
        if (result.Output.Lines.Any(line => line.Contains("rejected (diplomacy.trade-cap-reached)", StringComparison.Ordinal)))
            _feedback += "\nUse End trade to end an existing agreement first.";
        if (result.Accepted)
        {
            _choices.Clear();
            foreach (var pair in choices) _choices.Add(pair.Key, pair.Value);
        }
        Rebuild();
    }

    private void Commit()
    {
        var command = string.Join("; ", _draft.Commands);
        var result = AppDiagnostics.Operation(DiagnosticEvent.Command, "diplomacy", () => _draft.Commit(),
            command: command, turn: Session.State.Calendar.TurnIndex,
            success: applied => applied.Accepted, nation: Session.State.ActiveNationId);
        if (!result.Accepted)
        {
            _feedback = CommandOutcomeWording.Apply(CommandOutcomeText.OutcomeBlock(result.Output.Lines));
            Rebuild();
            return;
        }
        if (_draft.HasChanges) CommandIssued?.Invoke(result.Output.Lines);
        Closed?.Invoke();
    }
}
