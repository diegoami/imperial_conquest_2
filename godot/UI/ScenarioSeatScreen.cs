using Godot;
using IC2.Engine.Model;
using IC2.Engine.Serialization;

namespace IC2.Slice.UI;

/// <summary>
/// New Game's second step — <c>docs/game-design.md</c> §"User interface" item 1: "Only after the
/// ruleset is picked does the flow continue into Scenario selection, human/AI seat assignment, and AI
/// personality sliders." Reached only from <see cref="RulesetChooserScreen"/>'s own Continue button
/// (<c>docs/tasks/T24.md</c> Done-when 4's "not reachable before" requirement).
/// </summary>
/// <remarks>
/// <strong>Scenario selection, as shipped today.</strong> Each ruleset card names exactly one scenario
/// (<see cref="RulesetPresets"/>'s own remarks: a <see cref="Scenario"/> declares its own fixed
/// <c>rulesetId</c>, and <c>classical-mediterranean</c>/<c>example-classical-improved</c> are the only
/// two shipped scenarios pairing that world with each ruleset) — so this screen shows the resolved
/// scenario's name read-only rather than a dropdown with one inert entry. A future scenario that ships
/// more than one map under the same ruleset is a data addition, not a UI one: this screen already reads
/// the scenario id from <see cref="NewGameSelection"/>'s own card, not a hardcoded one.
/// </remarks>
public partial class ScenarioSeatScreen : Control
{
    public required NewGameSelection Selection { get; init; }

    /// <summary>Raised once "Start Game" is pressed, after <see cref="Selection"/> has been filled in.</summary>
    public event Action? Started;

    /// <summary>Raised by the "Back" button — returns to the ruleset chooser.</summary>
    public event Action? BackRequested;

    private IReadOnlyList<SeatOption> _seatOptions = Array.Empty<SeatOption>();
    private readonly Dictionary<string, CheckBox> _humanBoxes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LineEdit> _leaderEdits = new(StringComparer.Ordinal);
    private IReadOnlyDictionary<string, string> _drawnNames = null!;
    private Button _startButton = null!;
    private Label _playersLine = null!;
    private HSlider _aggressionSlider = null!;
    private HSlider _expansionSlider = null!;
    private HSlider _loyaltySlider = null!;
    private GridContainer _body = null!;
    private VBoxContainer _roster = null!;
    private PanelContainer _setup = null!;
    public CheckBox ObserverChoice { get; private set; } = null!;

    public override void _Ready()
    {
        UiKit.ApplyBackground(this, UiKit.Background);

        var resolved = GameDataContext.Repository.Resolve(Selection.SelectedCard.ScenarioId);
        _seatOptions = GameSessionFactory.SeatOptionsFor(resolved);
        _drawnNames = GameSessionFactory.LeaderNamePreview(resolved);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", AppTheme.ScreenMarginSide);
        margin.AddThemeConstantOverride("margin_right", AppTheme.ScreenMarginSide);
        margin.AddThemeConstantOverride("margin_top", AppTheme.ScreenMarginVertical);
        margin.AddThemeConstantOverride("margin_bottom", AppTheme.ScreenMarginVertical);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        margin.AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        column.AddChild(header);

        var backButton = UiKit.MakeButton("< Back", () => BackRequested?.Invoke(), AppTheme.Button);
        backButton.CustomMinimumSize = new Vector2(90, 34);
        header.AddChild(backButton);

        var title = UiKit.MakeLabel($"New Game — {Selection.SelectedCard.DisplayName}", AppTheme.Header, UiKit.AccentColor);
        title.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        title.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        header.AddChild(title);
        column.AddChild(UiKit.MakeLabel($"Scenario: {resolved.Scenario.Name}", AppTheme.Body, UiKit.MutedTextColor));
        column.AddChild(UiKit.MakeLabel($"World: {resolved.World.Id} · {_seatOptions.Count} nations", AppTheme.Body, UiKit.MutedTextColor));

        column.AddChild(new HSeparator());
        // #719: the original's "Human and computer leaders" form: a Human tick and an editable leader's name per
        // nation; computer nations' names are greyed. Two or more humans play hotseat.
        _body = new GridContainer { Columns = 2, SizeFlagsVertical = SizeFlags.ExpandFill };
        _body.AddThemeConstantOverride("h_separation", AppTheme.PanelGap);
        _body.AddThemeConstantOverride("v_separation", AppTheme.DialogGap);
        column.AddChild(_body);
        _roster = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        _body.AddChild(_roster);
        _roster.AddChild(UiKit.MakeLabel("Players", AppTheme.Header, UiKit.TextColor));
        _playersLine = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.MutedTextColor);
        _playersLine.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _roster.AddChild(_playersLine);
        _setup = UiKit.MakePanel(UiKit.PanelColor);
        _setup.Name = "SetupPanel";
        _setup.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _setup.SizeFlagsVertical = SizeFlags.ExpandFill;
        _setup.SizeFlagsStretchRatio = 0.65f;
        _body.AddChild(_setup);
        var actions = new VBoxContainer();
        actions.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        _setup.AddChild(actions);
        _startButton = UiKit.MakeButton("Start Game", OnStartPressed, AppTheme.Header);
        _startButton.Name = "StartGame";
        _startButton.CustomMinimumSize = new Vector2(0, 48);
        _startButton.AddThemeColorOverride("font_color", UiKit.AccentColor);
        actions.AddChild(_startButton);
        var settingsScroll = new ScrollContainer { Name = "SettingsScroll", SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        actions.AddChild(settingsScroll);
        var settings = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        settings.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        settingsScroll.AddChild(settings);
        ObserverChoice = new CheckBox { Name = "ObserverMode", Text = "Observer mode (experimental)",
            ButtonPressed = Selection.ObserverMode,
            TooltipText = "Watch all nations played by the computer. Automatic battles; starts paused." };
        ObserverChoice.Toggled += _ => RefreshPlayers();
        settings.AddChild(ObserverChoice);

        var scroll = new ScrollContainer
        {
            Name = "PlayerScroll",
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        _roster.AddChild(scroll);
        var grid = new GridContainer { Columns = 3, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        grid.AddThemeConstantOverride("h_separation", AppTheme.DialogGap);
        grid.AddThemeConstantOverride("v_separation", 4);
        scroll.AddChild(grid);
        foreach (var heading in new[] { "Nation", "Human", "Leader's name" })
        {
            grid.AddChild(UiKit.MakeLabel(heading, AppTheme.Body, UiKit.AccentColor));
        }

        var humansAtStart = resolved.Scenario.Seats.Where(seat => seat.Control == SeatControl.Human).Select(seat => seat.Nation).ToHashSet();
        if (humansAtStart.Count == 0 && _seatOptions.Count > 0)
        {
            humansAtStart.Add(_seatOptions[0].NationId);
        }

        foreach (var option in _seatOptions)
        {
            var nationId = option.NationId;
            var name = UiKit.MakeLabel(option.DisplayName, AppTheme.Body, UiKit.TextColor);
            name.CustomMinimumSize = new Vector2(90, 0);
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            name.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            grid.AddChild(name);

            var human = new CheckBox { Name = $"Human_{nationId}", ButtonPressed = humansAtStart.Contains(nationId) };
            human.Toggled += enabled =>
            {
                var edit = _leaderEdits[nationId];
                edit.Editable = enabled;
                if (enabled) { edit.GrabFocus(); edit.SelectAll(); }
                else edit.Text = _drawnNames[nationId];
                RefreshPlayers();
            };
            grid.AddChild(human);
            _humanBoxes[nationId] = human;

            var leader = new LineEdit
            {
                Name = $"Leader_{nationId}",
                Text = _drawnNames[nationId],
                PlaceholderText = _drawnNames[nationId],
                MaxLength = NewGameSelection.MaxLeaderNameLength,
                CustomMinimumSize = new Vector2(120, 0),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            grid.AddChild(leader);
            _leaderEdits[nationId] = leader;
        }

        settings.AddChild(new HSeparator());
        var personalityTitle = UiKit.MakeLabel("AI personality", AppTheme.Header, UiKit.TextColor);
        settings.AddChild(personalityTitle);
        var explanation = UiKit.MakeLabel("Applies to every computer nation.", AppTheme.Body, UiKit.MutedTextColor);
        explanation.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        settings.AddChild(explanation);
        _aggressionSlider = AddPersonalitySlider(settings, "Aggression");
        _expansionSlider = AddPersonalitySlider(settings, "Expansion drive");
        _loyaltySlider = AddPersonalitySlider(settings, "Loyalty to alliances");
        Resized += UpdateLayout;
        UpdateLayout();
        RefreshPlayers();
    }

    private void UpdateLayout()
    {
        // Stack the action area first when two readable columns no longer fit.
        var narrow = Size.X < 950;
        _body.Columns = narrow ? 1 : 2;
        _setup.CustomMinimumSize = new Vector2(narrow ? 0 : 330, 0);
        _body.MoveChild(narrow ? _setup : _roster, 0);
    }

    /// <summary>#719: the nations ticked Human, in seat order.</summary>
    public IReadOnlyList<string> HumanNationIds =>
        ObserverChoice.ButtonPressed ? Array.Empty<string>() :
            _seatOptions.Select(o => o.NationId).Where(id => _humanBoxes.TryGetValue(id, out var box) && box.ButtonPressed).ToList();

    /// <summary>#719: whether Start Game can be pressed (at least one human).</summary>
    public bool CanStart => !_startButton.Disabled;

    /// <summary>#719, for checks: tick or clear a nation's Human box, as a click would.</summary>
    public void SetHumanForCheck(string nationId, bool human) => _humanBoxes[nationId].ButtonPressed = human;

    /// <summary>#719, for checks: type a leader's name, as the keyboard would.</summary>
    public void SetLeaderNameForCheck(string nationId, string name) => _leaderEdits[nationId].Text = name;

    /// <summary>#719, for checks: whether a nation's leader's name can be edited (only a human's can).</summary>
    public bool IsLeaderEditableForCheck(string nationId) => _leaderEdits[nationId].Editable;
    public LineEdit LeaderEditForCheck(string nationId) => _leaderEdits[nationId];

    private void RefreshPlayers()
    {
        foreach (var (nationId, box) in _humanBoxes)
        {
            box.Disabled = ObserverChoice.ButtonPressed;
            _leaderEdits[nationId].Editable = box.ButtonPressed && !ObserverChoice.ButtonPressed;
        }

        var count = HumanNationIds.Count;
        _startButton.Disabled = count == 0 && !ObserverChoice.ButtonPressed;
        _playersLine.Text = ObserverChoice.ButtonPressed ? "Observer: every nation is computer controlled; starts paused." : count switch
        {
            0 => "Tick at least one nation a person plays.",
            1 => "One player; the computer plays every other nation.",
            _ => $"{count} players take turns at this computer (hotseat); the computer plays the rest.",
        };
    }

    private HSlider AddPersonalitySlider(VBoxContainer column, string label)
    {
        var row = new VBoxContainer();
        row.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        column.AddChild(row);

        var caption = UiKit.MakeLabel(label, AppTheme.Body, UiKit.MutedTextColor);
        row.AddChild(caption);

        var slider = new HSlider
        {
            MinValue = 0.0,
            MaxValue = 1.0,
            Step = 0.05,
            Value = 0.5,
            Name = label.Replace(" ", ""),
            CustomMinimumSize = new Vector2(0, 24),
        };
        row.AddChild(slider);
        return slider;
    }

    /// <summary>Confirms the current seat picker/slider values and raises <see cref="Started"/> — public
    /// so the DoD 4 headless check, any test, and <c>godot/Checks/ScreenshotTour.cs</c> can advance the
    /// flow without simulating a mouse click, mirroring <see cref="RulesetChooserScreen.ConfirmSelection"/>.</summary>
    public void ConfirmSeatAndStart() => OnStartPressed();

    private void OnStartPressed()
    {
        var humans = HumanNationIds;
        if (humans.Count == 0 && !ObserverChoice.ButtonPressed)
        {
            return;
        }

        Selection.ObserverMode = ObserverChoice.ButtonPressed;
        Selection.HumanNationIds.Clear();
        Selection.HumanNationIds.AddRange(humans);
        Selection.LeaderNames.Clear();
        foreach (var nationId in humans)
        {
            var typed = _leaderEdits[nationId].Text.Trim();
            if (typed.Length > 0)
            {
                Selection.LeaderNames[nationId] = typed;
            }
        }

        Selection.AiPersonalityOverride = new AiPersonality(
            _aggressionSlider.Value, _expansionSlider.Value, _loyaltySlider.Value);

        Started?.Invoke();
    }
}
