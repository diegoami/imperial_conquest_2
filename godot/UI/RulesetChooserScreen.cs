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
    public event Action<RulesetPreset>? Continued;

    /// <summary>Raised by the "Back" button — returns to the main menu.</summary>
    public event Action? BackRequested;

    /// <summary>Both cards' own root buttons, keyed by preset — see this class's own remarks.</summary>
    public Dictionary<RulesetPreset, Button> CardButtons { get; } = new();

    /// <summary>Each card's own title label, keyed by preset — the actual rendered text a check or test
    /// reads, rather than assuming <see cref="RulesetPresets"/>' own <c>DisplayName</c> made it to the
    /// scene unchanged.</summary>
    public Dictionary<RulesetPreset, Label> CardTitleLabels { get; } = new();

    public RulesetPreset SelectedPreset { get; private set; } = RulesetPresets.Default;

    private Button _continueButton = null!;

    public override void _Ready()
    {
        UiKit.ApplyBackground(this, UiKit.Background);

        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        root.AddThemeConstantOverride("separation", 16);
        AddChild(root);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 32);
        margin.AddThemeConstantOverride("margin_right", 32);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        margin.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 16);
        margin.AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);
        column.AddChild(header);

        var backButton = UiKit.MakeButton("< Back", () => BackRequested?.Invoke(), 14);
        backButton.CustomMinimumSize = new Vector2(90, 34);
        header.AddChild(backButton);

        var title = UiKit.MakeLabel("New Game — Choose a Ruleset", 26, UiKit.AccentColor);
        header.AddChild(title);

        var cards = new HBoxContainer();
        cards.AddThemeConstantOverride("separation", 20);
        cards.SizeFlagsVertical = SizeFlags.ExpandFill;
        column.AddChild(cards);

        foreach (var card in RulesetPresets.Cards)
        {
            cards.AddChild(BuildCard(card));
        }

        _continueButton = UiKit.MakeButton("Continue", OnContinuePressed, 18);
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
        button.AddThemeConstantOverride("h_separation", 0);
        button.Pressed += () => SelectPreset(card.Preset);
        CardButtons[card.Preset] = button;

        // A MarginContainer, not a bare FullRect anchor, so the card's own text keeps a real inset from
        // the button's edges: a Control added straight to a Button and FullRect-anchored ignores that
        // Button's own StyleBoxFlat content margins (those only apply to the Button's own built-in text),
        // which left T24's first pass with bullet text flush against the card's left/top border.
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        button.AddChild(margin);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", 8);
        margin.AddChild(content);

        var name = UiKit.MakeLabel(card.DisplayName, 22, UiKit.TextColor);
        name.HorizontalAlignment = HorizontalAlignment.Center;
        content.AddChild(name);
        CardTitleLabels[card.Preset] = name;

        foreach (var line in card.Summary)
        {
            var label = UiKit.MakeLabel("• " + line, 13, UiKit.MutedTextColor);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
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
    public void SelectPreset(RulesetPreset preset)
    {
        SelectedPreset = preset;
        HighlightSelectedCard();
    }

    private void HighlightSelectedCard()
    {
        foreach (var (preset, button) in CardButtons)
        {
            var selected = preset == SelectedPreset;
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
    public void ConfirmSelection() => Continued?.Invoke(SelectedPreset);
}
