using Godot;
using IC2.Engine.Serialization;

namespace IC2.Slice.UI;

/// <summary>
/// New Game's compact campaign list and selected-entry description. The owner replaced the
/// original oversized card layout on 2026-11-05; scenario/ruleset content and default stay the same.
/// </summary>
/// <remarks>
/// Legacy Card-named accessors still expose the actual row controls to the existing flow checks.
/// Classical Faithful and Improved retain equal prominence and the same default selection.
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
    private ScrollContainer _listScroll = null!;
    private VBoxContainer _description = null!;
    private CampaignGuideCatalog _guides = null!;
    private ScrollContainer _descriptionScroll = null!;
    public ScrollContainer DescriptionScroll => _descriptionScroll;
    public ScrollContainer ScenarioListScroll => _listScroll;
    public Button ContinueButton => _continueButton;

    private Button _continueButton = null!;

    public override void _Ready()
    {
        _guides = CampaignGuideCatalog.Load(GameDataContext.Repository);
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

        var title = UiKit.MakeLabel("New Game — Choose a Scenario", AppTheme.Header, UiKit.AccentColor);
        header.AddChild(title);

        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        column.AddChild(body);
        _listScroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            FollowFocus = true,
        };
        body.AddChild(_listScroll);
        var rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rows.AddThemeConstantOverride("separation", AppTheme.TableRowGap);
        _listScroll.AddChild(rows);

        var descriptionPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        body.AddChild(descriptionPanel);
        var descriptionScroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, FollowFocus = true };
        _descriptionScroll = descriptionScroll;
        var descriptionMargin = new MarginContainer();
        foreach (var edge in new[] { "left", "right", "top", "bottom" })
            descriptionMargin.AddThemeConstantOverride("margin_" + edge, AppTheme.PanelMarginSide);
        descriptionPanel.AddChild(descriptionMargin);
        descriptionMargin.AddChild(descriptionScroll);
        _description = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _description.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        descriptionScroll.AddChild(_description);

        foreach (var card in RulesetPresets.CardsFor(GameDataContext.Repository))
        {
            rows.AddChild(BuildCard(card));
        }

        _continueButton = UiKit.MakeButton("Continue", OnContinuePressed, AppTheme.Button);
        _continueButton.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
        column.AddChild(_continueButton);

        HighlightSelectedCard();
    }

    /// <summary>
    /// A compact single-line row. Summaries appear only in the selected entry's description pane.
    /// </summary>
    private Control BuildCard(RulesetCard card)
    {
        var button = new Button
        {
            ToggleMode = true,
            CustomMinimumSize = new Vector2(0, 44),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsStretchRatio = 1f,
            ClipText = false,
        };
        button.AddThemeConstantOverride("h_separation", AppTheme.DialogGap);
        button.Pressed += () => SelectCard(card);
        button.FocusEntered += () => { SelectCard(card); _listScroll.EnsureControlVisible(button); };
        button.GuiInput += input =>
        {
            if (input is not InputEventKey { Pressed: true } key) return;
            var index = _cards.FindIndex(row => row.Button == button);
            var next = key.Keycode switch { Key.Up => index - 1, Key.Down => index + 1,
                Key.Home => 0, Key.End => _cards.Count - 1, _ => -1 };
            if (next < 0 || next >= _cards.Count) return;
            _cards[next].Button.GrabFocus();
            button.AcceptEvent();
        };
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
        margin.AddThemeConstantOverride("margin_top", 4);
        margin.AddThemeConstantOverride("margin_bottom", 4);
        button.AddChild(margin);

        var content = new VBoxContainer();
        content.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        margin.AddChild(content);

        var name = UiKit.MakeLabel(card.DisplayName, AppTheme.Body, UiKit.TextColor);
        name.VerticalAlignment = VerticalAlignment.Center;
        name.SizeFlagsVertical = SizeFlags.ExpandFill;
        name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        name.ClipText = true;
        name.MouseFilter = MouseFilterEnum.Ignore;
        button.TooltipText = card.DisplayName;
        button.AccessibilityName = card.DisplayName;
        content.AddChild(name);
        if (card.Preset != RulesetPreset.Listed)
        {
            CardTitleLabels[card.Preset] = name;
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
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                ContentMarginLeft = 16,
                ContentMarginRight = 16,
                ContentMarginTop = 4,
                ContentMarginBottom = 4,
            };
            button.AddThemeStyleboxOverride("normal", style);
            button.AddThemeStyleboxOverride("hover", style);
            button.AddThemeStyleboxOverride("pressed", style);
        }
        foreach (var child in _description.GetChildren()) { _description.RemoveChild(child); child.QueueFree(); }
        var title = UiKit.MakeLabel(SelectedCard.DisplayName, AppTheme.Header, UiKit.AccentColor);
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _description.AddChild(title);
        if (_guides.ForScenario(SelectedCard.ScenarioId) is { } guide) AddGuide(guide);
        for (var i = 0; i < SelectedCard.PlayerSummary.Count; i++)
        {
            var label = UiKit.MakeLabel("• " + SelectedCard.PlayerSummary[i], AppTheme.Body, UiKit.TextColor);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            label.TooltipText = i < SelectedCard.Summary.Count ? SelectedCard.Summary[i] : string.Empty;
            _description.AddChild(label);
        }
        _descriptionScroll.ScrollVertical = 0;
    }

    private void AddGuide(CampaignGuide guide)
    {
        void Paragraph(string text, int size = 0, Color? color = null)
        {
            var label = UiKit.MakeLabel(text, size, color);
            label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _description.AddChild(label);
        }
        Paragraph((guide.Kind == "historical" ? "Historical background — " : "Geography — ") + guide.Period, AppTheme.Header, UiKit.AccentColor);
        Paragraph(guide.Background);
        Paragraph(guide.Abstractions, AppTheme.Note, UiKit.MutedTextColor);
        Paragraph("Choosing a power", AppTheme.Header, UiKit.AccentColor);
        Paragraph(guide.Validation, AppTheme.Note, UiKit.MutedTextColor);
        var resolved = GameDataContext.Repository.Resolve(SelectedCard.ScenarioId);
        foreach (var power in guide.Powers.OrderByDescending(p => p.Suggested))
        {
            var name = resolved.World.NationById(power.NationId)!.Name;
            Paragraph($"{(power.Suggested ? "Suggested: " : string.Empty)}{name} · {power.Focus}", AppTheme.Body, UiKit.AccentColor);
            var cities = resolved.World.Cities.Count(c => c.Owner == power.NationId);
            var armies = resolved.World.StartingArmies.Count(a => a.Nation == power.NationId);
            var fleets = resolved.World.StartingFleets.Count(f => f.Nation == power.NationId);
            Paragraph($"Opening: {cities} {(cities == 1 ? "city" : "cities")} · {armies} {(armies == 1 ? "army" : "armies")} · {fleets} {(fleets == 1 ? "fleet" : "fleets")}", AppTheme.Note, UiKit.MutedTextColor);
            Paragraph(power.Advice);
        }
        if (guide.Sources.Length > 0)
        {
            Paragraph("Historical sources", AppTheme.Header, UiKit.AccentColor);
            foreach (var source in guide.Sources)
            {
                var link = UiKit.MakeButton(source.Title, () => OS.ShellOpen(source.Url), AppTheme.Note);
                link.ClipText = true;
                link.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
                UiKit.SetHint(link, source.Title + "\n" + source.Url, true);
                _description.AddChild(link);
            }
        }
        Paragraph("Campaign rules", AppTheme.Header, UiKit.AccentColor);
    }

    private void OnContinuePressed() => ConfirmSelection();

    /// <summary>Confirms <see cref="SelectedPreset"/> and raises <see cref="Continued"/> — public so the
    /// DoD 4 headless check and any test can advance the flow without simulating a mouse click.</summary>
    public void ConfirmSelection() => Continued?.Invoke(SelectedCard);
}
