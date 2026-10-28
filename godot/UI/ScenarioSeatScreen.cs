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

        header.AddChild(UiKit.MakeLabel(
            $"New Game — {Selection.SelectedCard.DisplayName}", AppTheme.Header, UiKit.AccentColor));
        column.AddChild(UiKit.MakeLabel($"Scenario: {resolved.Scenario.Name}", AppTheme.Body, UiKit.MutedTextColor));
        column.AddChild(UiKit.MakeLabel($"World: {resolved.World.Id} · {_seatOptions.Count} nations", AppTheme.Body, UiKit.MutedTextColor));

        column.AddChild(new HSeparator());
        // #719: the original's "Human and computer leaders" form: a Human tick and an editable leader's name per
        // nation; computer nations' names are greyed. Two or more humans play hotseat.
        column.AddChild(UiKit.MakeLabel("Players:", AppTheme.Header, UiKit.TextColor));
        _playersLine = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.MutedTextColor);
        column.AddChild(_playersLine);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(0, 420),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
        };
        column.AddChild(scroll);
        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", AppTheme.DialogGap * 2);
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
            name.CustomMinimumSize = new Vector2(200, 0);
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
                CustomMinimumSize = new Vector2(260, 0),
            };
            grid.AddChild(leader);
            _leaderEdits[nationId] = leader;
        }

        column.AddChild(new HSeparator());
        column.AddChild(UiKit.MakeLabel(
            "AI personality (applies to every computer nation):", AppTheme.Header, UiKit.TextColor));

        _aggressionSlider = AddPersonalitySlider(column, "Aggression");
        _expansionSlider = AddPersonalitySlider(column, "Expansion drive");
        _loyaltySlider = AddPersonalitySlider(column, "Loyalty to alliances");

        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });
        _startButton = UiKit.MakeButton("Start Game", OnStartPressed, AppTheme.Button);
        _startButton.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        column.AddChild(_startButton);
        RefreshPlayers();
    }

    /// <summary>#719: the nations ticked Human, in seat order.</summary>
    public IReadOnlyList<string> HumanNationIds =>
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
            _leaderEdits[nationId].Editable = box.ButtonPressed;
        }

        var count = HumanNationIds.Count;
        _startButton.Disabled = count == 0;
        _playersLine.Text = count switch
        {
            0 => "Tick at least one nation a person plays.",
            1 => "One player; the computer plays every other nation.",
            _ => $"{count} players take turns at this computer (hotseat); the computer plays the rest.",
        };
    }

    private HSlider AddPersonalitySlider(VBoxContainer column, string label)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        column.AddChild(row);

        var caption = UiKit.MakeLabel(label, AppTheme.Body, UiKit.MutedTextColor);
        caption.CustomMinimumSize = new Vector2(180, 0);
        row.AddChild(caption);

        var slider = new HSlider
        {
            MinValue = 0.0,
            MaxValue = 1.0,
            Step = 0.05,
            Value = 0.5,
            CustomMinimumSize = new Vector2(220, 0),
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
        if (humans.Count == 0)
        {
            return;
        }

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
