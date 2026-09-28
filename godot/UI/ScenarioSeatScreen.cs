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

    private OptionButton _seatPicker = null!;
    private IReadOnlyList<SeatOption> _seatOptions = Array.Empty<SeatOption>();
    private HSlider _aggressionSlider = null!;
    private HSlider _expansionSlider = null!;
    private HSlider _loyaltySlider = null!;

    public override void _Ready()
    {
        UiKit.ApplyBackground(this, UiKit.Background);

        var resolved = GameDataContext.Repository.Resolve(Selection.SelectedCard.ScenarioId);
        _seatOptions = GameSessionFactory.SeatOptionsFor(resolved);

        var margin = new MarginContainer();
        margin.SetAnchorsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 32);
        margin.AddThemeConstantOverride("margin_right", 32);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 14);
        margin.AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);
        column.AddChild(header);

        var backButton = UiKit.MakeButton("< Back", () => BackRequested?.Invoke(), 14);
        backButton.CustomMinimumSize = new Vector2(90, 34);
        header.AddChild(backButton);

        header.AddChild(UiKit.MakeLabel(
            $"New Game — {Selection.SelectedCard.DisplayName}", 26, UiKit.AccentColor));
        column.AddChild(UiKit.MakeLabel($"Scenario: {resolved.Scenario.Name}", 16, UiKit.MutedTextColor));
        column.AddChild(UiKit.MakeLabel($"World: {resolved.World.Id} · {_seatOptions.Count} nations", 14, UiKit.MutedTextColor));

        column.AddChild(new HSeparator());
        column.AddChild(UiKit.MakeLabel("Play as:", 18, UiKit.TextColor));

        _seatPicker = new OptionButton
        {
            CustomMinimumSize = new Vector2(260, 36),
            SizeFlagsHorizontal = SizeFlags.ShrinkBegin,
        };
        foreach (var option in _seatOptions)
        {
            _seatPicker.AddItem(option.DisplayName);
        }

        if (_seatOptions.Count > 0)
        {
            _seatPicker.Selected = 0;
        }

        column.AddChild(_seatPicker);

        column.AddChild(new HSeparator());
        column.AddChild(UiKit.MakeLabel(
            "AI personality (applies to every AI-controlled nation):", 18, UiKit.TextColor));

        _aggressionSlider = AddPersonalitySlider(column, "Aggression");
        _expansionSlider = AddPersonalitySlider(column, "Expansion drive");
        _loyaltySlider = AddPersonalitySlider(column, "Loyalty to alliances");

        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });
        var startButton = UiKit.MakeButton("Start Game", OnStartPressed, 20);
        startButton.SizeFlagsHorizontal = SizeFlags.ShrinkBegin;
        column.AddChild(startButton);
    }

    private HSlider AddPersonalitySlider(VBoxContainer column, string label)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 12);
        column.AddChild(row);

        var caption = UiKit.MakeLabel(label, 14, UiKit.MutedTextColor);
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
        if (_seatOptions.Count == 0)
        {
            return;
        }

        var chosenIndex = Mathf.Clamp(_seatPicker.Selected, 0, _seatOptions.Count - 1);
        Selection.HumanNationId = _seatOptions[chosenIndex].NationId;
        Selection.AiPersonalityOverride = new AiPersonality(
            _aggressionSlider.Value, _expansionSlider.Value, _loyaltySlider.Value);

        Started?.Invoke();
    }
}
