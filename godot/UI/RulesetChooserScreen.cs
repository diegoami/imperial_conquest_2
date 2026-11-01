using Godot;

namespace IC2.Slice.UI;

/// <summary>
/// New Game's first and most prominent screen — <c>docs/tasks/T24.md</c> Scope and
/// <c>docs/game-design.md</c> §"User interface" item 1: a two-card <c>Classical Faithful</c> vs
/// <c>Improved</c> picker, shown before scenario or seat selection, each card carrying a short
/// plain-language summary of what it changes (<see cref="RulesetPresets"/>), <c>Classical Faithful</c>
/// pre-highlighted as the default.
/// </summary>
/// <remarks>
/// <c>docs/tasks/T24.md</c> Done-when 4 checks this screen directly (not only its Godot-free model): both
/// cards must render as equally-weighted, labelled options, and the pre-selected default must be
/// <c>Classical Faithful</c>. <see cref="CardButtons"/> and <see cref="SelectedPreset"/> are exposed
/// (rather than kept private) exactly so <c>godot/Checks/RulesetFlowCheck.cs</c> can assert on the real
/// scene tree this class builds, not a hand-maintained mirror of it.
/// </remarks>
public partial class RulesetChooserScreen : Control
{
    /// <summary>Raised once the player confirms a card and presses Continue.</summary>
    public event Action<RulesetCard>? Continued;

    /// <summary>Raised by the "Back" button — returns to the main menu.</summary>
    public event Action? BackRequested;

    /// <summary>Both cards' own root buttons, keyed by preset — see this class's own remarks.</summary>
    public Dictionary<RulesetPreset, Button> CardButtons { get; } = new();

    /// <summary>Each card's own title label, keyed by preset — the actual rendered text a check or test
    /// reads, rather than assuming <see cref="RulesetPresets"/>' own <c>DisplayName</c> made it to the
    /// scene unchanged.</summary>
    public Dictionary<RulesetPreset, Label> CardTitleLabels { get; } = new();

    public RulesetPreset SelectedPreset => SelectedCard.Preset;

    /// <summary>T200: the highlighted card, a preset or a listed scenario.</summary>
    public RulesetCard SelectedCard { get; private set; } = RulesetPresets.CardFor(RulesetPresets.Default);

    /// <summary>T200: the cards of the listed scenarios, by scenario id (the presets are in <see cref="CardButtons"/>).</summary>
    public Dictionary<string, Button> ScenarioCardButtons { get; } = new(StringComparer.Ordinal);

    private readonly List<(RulesetCard Card, Button Button)> _cards = new();

    private Button _continueButton = null!;

    public override void _Ready()
    {
        UiKit.ApplyBackground(this, UiKit.Background);

        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        AddChild(root);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", AppTheme.ScreenMarginSide);
        margin.AddThemeConstantOverride("margin_right", AppTheme.ScreenMarginSide);
        margin.AddThemeConstantOverride("margin_top", AppTheme.ScreenMarginVertical);
        margin.AddThemeConstantOverride("margin_bottom", AppTheme.ScreenMarginVertical);
        margin.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        margin.AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        column.AddChild(header);

        var backButton = UiKit.MakeButton("< Back", () => BackRequested?.Invoke(), AppTheme.Button);
        backButton.CustomMinimumSize = new Vector2(90, 34);
        header.AddChild(backButton);

        var title = UiKit.MakeLabel("New Game — Choose a Ruleset", AppTheme.Header, UiKit.AccentColor);
        header.AddChild(title);

        var cardScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(cardScroll);
        var cards = new HFlowContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        cards.AddThemeConstantOverride("h_separation", AppTheme.DialogGap);
        cards.AddThemeConstantOverride("v_separation", AppTheme.DialogGap);
        cardScroll.AddChild(cards);

        foreach (var card in RulesetPresets.CardsFor(GameDataContext.Repository))
        {
            cards.AddChild(BuildCard(card));
        }

        _continueButton = UiKit.MakeButton("Continue", OnContinuePressed, AppTheme.Button);
        _continueButton.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        column.AddChild(_continueButton);

        HighlightSelectedCard();
    }

    /// <summary>
    /// One card: a <see cref="Button"/> so the whole card is clickable, wrapping its own title and
    /// summary lines. <strong>Equally weighted</strong> (Done-when 4): both cards get the same
    /// <see cref="Control.SizeFlagsHorizontal"/> stretch (<see cref="SizeFlags.ExpandFill"/>, ratio 1)
    /// and the same <see cref="Control.CustomMinimumSize"/>, so neither is laid out larger or more
    /// prominent than the other — the only visual difference between them is which one currently carries
    /// <see cref="UiKit.SelectedBorderColor"/> (<see cref="HighlightSelectedCard"/>).
    /// </summary>
    private Control BuildCard(RulesetCard card)
    {
        var button = new Button
        {
            ToggleMode = true,
            CustomMinimumSize = new Vector2(420, 360),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1f,
            ClipText = false,
        };
        button.AddThemeConstantOverride("h_separation", AppTheme.DialogGap);
        button.Pressed += () => SelectCard(card);
        _cards.Add((card, button));
        if (card.Preset == RulesetPreset.Listed)
        {
            ScenarioCardButtons[card.ScenarioId] = button;
        }
        else
        {
            CardButtons[card.Preset] = button;
        }

        // A MarginContainer, not a bare FullRect anchor, so the card's own text keeps a real inset from
        // the button's edges: a Control added straight to a Button and FullRect-anchored ignores that
        // Button's own StyleBoxFlat content margins (those only apply to the Button's own built-in text),
        // which left T24's first pass with bullet text flush against the card's left/top border.
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", AppTheme.PanelMarginSide);
        margin.AddThemeConstantOverride("margin_right", AppTheme.PanelMarginSide);
        margin.AddThemeConstantOverride("margin_top", AppTheme.PanelMarginSide);
        margin.AddThemeConstantOverride("margin_bottom", AppTheme.PanelMarginSide);
        button.AddChild(margin);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        margin.AddChild(content);

        var name = UiKit.MakeLabel(card.DisplayName, AppTheme.Header, UiKit.TextColor);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        content.AddChild(name);
        if (card.Preset != RulesetPreset.Listed)
        {
            CardTitleLabels[card.Preset] = name;
        }

        // #1288: the player's words on the card; the exact ruleset paths are each line's tooltip.
        for (var i = 0; i < card.PlayerSummary.Count; i++)
        {
            var label = UiKit.MakeLabel("• " + card.PlayerSummary[i], AppTheme.Body, UiKit.MutedTextColor);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.TooltipText = i < card.Summary.Count ? card.Summary[i] : string.Empty;
            label.MouseFilter = Control.MouseFilterEnum.Pass;
            content.AddChild(label);
        }

        return button;
    }

    /// <summary>
    /// Selects <paramref name="preset"/> — called from a card's own click and (for the DoD 4 check
    /// script and any test) directly, so "picking either value" can be exercised without simulating a
    /// mouse event. Never instantiates the seat screen itself: that only happens once
    /// <see cref="OnContinuePressed"/> fires, keeping "before any scenario/seat control is reachable"
    /// true for as long as this screen is the only one in the tree.
    /// </summary>
    public void SelectPreset(RulesetPreset preset) => SelectCard(RulesetPresets.CardFor(preset));

    /// <summary>T200: highlights <paramref name="card"/>; a check calls it for a listed scenario's card.</summary>
    public void SelectCard(RulesetCard card)
    {
        SelectedCard = card;
        HighlightSelectedCard();
    }

    private void HighlightSelectedCard()
    {
        foreach (var (card, button) in _cards)
        {
            var selected = string.Equals(card.ScenarioId, SelectedCard.ScenarioId, StringComparison.Ordinal);
            button.ButtonPressed = selected;
            var style = new StyleBoxFlat
            {
                BgColor = selected ? UiKit.PanelColorRaised : UiKit.PanelColor,
                BorderColor = selected ? UiKit.SelectedBorderColor : UiKit.PanelColor,
                BorderWidthLeft = 3,
                BorderWidthRight = 3,
                BorderWidthTop = 3,
                BorderWidthBottom = 3,
                CornerRadiusTopLeft = 10,
                CornerRadiusTopRight = 10,
                CornerRadiusBottomLeft = 10,
                CornerRadiusBottomRight = 10,
                ContentMarginLeft = 16,
                ContentMarginRight = 16,
                ContentMarginTop = 14,
                ContentMarginBottom = 14,
            };
            button.AddThemeStyleboxOverride("normal", style);
            button.AddThemeStyleboxOverride("hover", style);
            button.AddThemeStyleboxOverride("pressed", style);
        }
    }

    private void OnContinuePressed() => ConfirmSelection();

    /// <summary>Confirms <see cref="SelectedPreset"/> and raises <see cref="Continued"/> — public so the
    /// DoD 4 headless check and any test can advance the flow without simulating a mouse click.</summary>
    public void ConfirmSelection() => Continued?.Invoke(SelectedCard);
}
