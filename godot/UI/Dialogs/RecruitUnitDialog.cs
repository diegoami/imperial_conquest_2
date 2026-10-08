using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// The Strategy menu's <strong>Recruit unit</strong> dialog — the original's <c>TArmyRecruits</c>
/// <strong>[confirmed: <c>docs/investigations/original-ui-command-audit.md</c> §1.3; decompiled
/// <c>TArmyRecruits_PrintNumbers</c> <c>0x00454C10</c>]</strong>. Titled "Army recruits", it offers
/// the active nation's cities, a unit type and a troop count, the city's training regiments, and
/// the three action buttons: Recruit unit (<c>recruit-standing</c>), Mobilize (<c>mobilize</c>), and
/// Disband (<c>disband-slot</c>, after a Yes / No / Cancel prompt).
/// </summary>
/// <remarks>
/// <para>
/// <strong>All rules live in the Godot-free <see cref="RecruitUnitDialogModel"/>.</strong> This
/// control owns only widgets and the three submit callbacks. The model reads the troop range from
/// <see cref="RecruitTroopBounds"/> (fix #519), the costs from
/// <see cref="IC2.Engine.Recruitment.StandingRecruitmentCost"/>, the training list from
/// <see cref="RecruitmentPanelViewModel.TrainingAtCity"/>, and the mobilization target from
/// <see cref="IC2.Engine.Armies.MobilizationReceivingArmy.Find"/> — so the dialog and the engine
/// cannot disagree between them.
/// </para>
/// <para>
/// <strong>Disband confirms first.</strong> The original's <c>TArmyRecruits_DisbandUnits</c> opens
/// a Yes / No / Cancel prompt with the text <em>"Are you sure you want to disband N unit."</em>
/// (or <em>"… N units."</em> when N &gt; 1) and acts only on Yes
/// <strong>[derived: code, <c>TArmyRecruits_DisbandUnits</c> :56133-56148; no refund: Wine candidate,
/// <c>2026-10-05-disbanding-a-queued-recruitment.md</c>]</strong>. The dialog uses the engine's own
/// <see cref="ConfirmPrompt"/>; the prompt's Yes submits the disband line, the prompt's No and
/// Cancel submit nothing.
/// </para>
/// <para>
/// <strong>The treasury check is a preset difference.</strong> <c>classical-faithful</c> lets the
/// order put the treasury into debt, as the original does; <c>improved</c> refuses it
/// <strong>[bug #549, T115]</strong>. The dialog adds no affordability check of its own — the
/// engine's refusal is the engine's, shown in the dialog's reply line, and the dialog stays open
/// for a second attempt.
/// </para>
/// </remarks>
public partial class RecruitUnitDialog : Control
{
    /// <summary>The session the dialog reads and submits to.</summary>
    public required GameSession Session { get; init; }

    /// <summary>Submits one composed line through the screen's path and returns the session's lines.</summary>
    public required Func<string, IReadOnlyList<string>> Submit { get; init; }

    /// <summary>Raised by OK, Cancel, and a confirmed Disband's overlay close; <see cref="MainGameScreen"/>
    /// closes the overlay.</summary>
    public event Action? Closed;

    private RecruitUnitDialogModel _model = null!;
    private OptionButton _cityDropdown = null!;
    private readonly List<string> _cityIds = new();
    private readonly List<Button> _typeButtons = new();
    private string _selectedUnitTypeId = string.Empty;
    private SpinBox _troops = null!;
    private Label _costLabel = null!;
    private ItemList _trainingList = null!;
    private Button _recruitButton = null!;
    private Button _mobilizeButton = null!;
    private Button _disbandButton = null!;
    private Button _okButton = null!;
    private Label _replyLabel = null!;
    private Label _mobTargetLabel = null!;

    /// <summary>
    /// The slot-table index of the training list's selected row, or <see langword="null"/> when none
    /// is selected. Kept as a field because <see cref="RefreshTrainingList"/> rebuilds the
    /// <c>ItemList</c> (whose <c>Clear</c> wipes the widget's own selection) on every refresh; the
    /// field survives and the rebuilt list re-selects the row it still names.
    /// </summary>
    private int? _selectedSlotIndex;

    /// <summary>The rows <see cref="_trainingList"/> currently shows, in row order — the map from a
    /// row position to <see cref="_selectedSlotIndex"/>.</summary>
    private IReadOnlyList<TrainingRegimentView> _trainingRows = Array.Empty<TrainingRegimentView>();

    /// <summary>The live model, exposed so a headless check can read its figures.</summary>
    public RecruitUnitDialogModel ModelForCheck => _model;

    /// <summary>The id of the city the dropdown shows — exposed for the headless check.</summary>
    public string SelectedCityIdForCheck => _cityDropdown.Selected < 0
        ? string.Empty
        : _cityDropdown.GetItemMetadata(_cityDropdown.Selected).AsString();

    /// <summary>The id of the unit type the buttons show — exposed for the headless check.</summary>
    public string SelectedUnitTypeForCheck => _selectedUnitTypeId;

    /// <summary>The troop box's value, exposed for the headless check.</summary>
    public int TroopsForCheck => (int)_troops.Value;

    /// <summary>The troop box's arrow step, as the real <see cref="SpinBox"/> carries it — for the
    /// headless check, which asserts the shipped box really steps by the bounds' own step.</summary>
    public double TroopStepForCheck => _troops.Step;

    /// <summary>The session's own reply line for the last command submitted from this dialog.</summary>
    public string ReplyForCheck => _replyLabel.Text;

    /// <summary>The Mobilize target line, exposed for the headless check.</summary>
    public string MobilizeTargetForCheck => _mobTargetLabel.Text;

    public override void _Ready()
    {
        _model = RecruitUnitDialogModel.ForActiveNation(Session.State, Session.Ruleset, Session.World);
        if (_model.UnitTypes.Count == 0)
        {
            Closed?.Invoke();
            return;
        }

        _selectedUnitTypeId = _model.UnitTypes[0].Id;

        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.6f), MouseFilter = MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.MakePanel(UiKit.PanelColor);
        panel.CustomMinimumSize = new Vector2(520, 0);
        center.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        panel.AddChild(column);

        column.AddChild(UiKit.MakeLabel(StrategyDialogModels.RecruitUnitTitle, 18, UiKit.AccentColor));

        // City dropdown: the original lists the active nation's own cities (the "All cities" entry
        // is a recruitment convenience the clone does not yet model — see the task's own
        // description). The dropdown is built once from the model's list.
        _cityDropdown = new OptionButton();
        foreach (var city in _model.OwnedCities)
        {
            _cityDropdown.AddItem(city.Name);
            _cityDropdown.SetItemMetadata(_cityDropdown.ItemCount - 1, city.Id);
            _cityIds.Add(city.Id);
        }

        _cityDropdown.ItemSelected += _ =>
        {
            _selectedSlotIndex = null;
            Refresh();
        };
        column.AddChild(_cityDropdown);

        // Type buttons: one per ruleset unit type, in the ruleset's own order.
        var typeRow = new HBoxContainer();
        typeRow.AddThemeConstantOverride("separation", 6);
        column.AddChild(typeRow);
        foreach (var type in _model.UnitTypes)
        {
            var button = new Button
            {
                Text = type.Abbreviation.Length > 0 ? type.Abbreviation : type.Id,
                ToggleMode = true,
            };
            button.Pressed += () => SelectUnitType(type.Id);
            typeRow.AddChild(button);
            _typeButtons.Add(button);
        }

        // The first type is selected by default. The troop box carries the type's bounds.
        var initialBounds = _model.TroopBoundsFor(_selectedUnitTypeId);
        _troops = new SpinBox
        {
            MinValue = initialBounds.Minimum,
            MaxValue = initialBounds.Maximum,
            Step = initialBounds.Step,
            Value = initialBounds.DefaultValue,
            CustomMinimumSize = new Vector2(140, 0),
        };
        _troops.ValueChanged += _ => Refresh();
        column.AddChild(_troops);

        _costLabel = UiKit.MakeLabel(string.Empty, 14, UiKit.TextColor);
        column.AddChild(_costLabel);

        column.AddChild(new HSeparator());
        column.AddChild(UiKit.MakeLabel("In training here", 14, UiKit.MutedTextColor));

        _trainingList = new ItemList { CustomMinimumSize = new Vector2(0, 140) };
        _trainingList.ItemSelected += index =>
        {
            var row = index >= 0 && index < _trainingRows.Count ? _trainingRows[(int)index] : null;
            _selectedSlotIndex = row?.SlotIndex;
            Refresh();
        };
        column.AddChild(_trainingList);

        // The action buttons: each reads the same training list selection.
        var actionRow = new HBoxContainer();
        actionRow.AddThemeConstantOverride("separation", 6);
        column.AddChild(actionRow);
        _recruitButton = UiKit.MakeButton("Recruit unit", SubmitRecruit);
        actionRow.AddChild(_recruitButton);
        _mobilizeButton = UiKit.MakeButton("Mobilize", SubmitMobilize);
        actionRow.AddChild(_mobilizeButton);
        _disbandButton = UiKit.MakeButton("Disband", SubmitDisband);
        actionRow.AddChild(_disbandButton);

        _mobTargetLabel = UiKit.MakeLabel(string.Empty, 12, UiKit.MutedTextColor);
        _mobTargetLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_mobTargetLabel);

        column.AddChild(new HSeparator());
        _replyLabel = UiKit.MakeLabel(string.Empty, 13, UiKit.TextColor);
        _replyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_replyLabel);

        var buttons = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        buttons.AddThemeConstantOverride("separation", 10);
        column.AddChild(buttons);
        buttons.AddChild(UiKit.MakeButton("Cancel", Cancel));
        _okButton = UiKit.MakeButton("OK", Ok);
        buttons.AddChild(_okButton);

        // Default the type-button pressed state to the first type.
        if (_typeButtons.Count > 0)
        {
            _typeButtons[0].ButtonPressed = true;
        }

        // Default the city selection to the first owned city.
        if (_cityDropdown.ItemCount > 0)
        {
            _cityDropdown.Select(0);
        }

        Refresh();
    }

    private void SelectUnitType(string unitTypeId)
    {
        _selectedUnitTypeId = unitTypeId;
        var bounds = _model.TroopBoundsFor(unitTypeId);
        _troops.MinValue = bounds.Minimum;
        _troops.MaxValue = bounds.Maximum;
        _troops.Step = bounds.Step;
        _troops.Value = bounds.DefaultValue;
        Refresh();
    }

    private void Refresh()
    {
        if (_model is null)
        {
            return;
        }

        var troops = (int)_troops.Value;
        _costLabel.Text =
            $"Cost: {_model.InitialCostFor(troops, _selectedUnitTypeId).ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            + $"  ·  quarterly: {_model.QuarterlyCostFor(troops, _selectedUnitTypeId).ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        RefreshTrainingList();
        var selectedRow = _trainingRows.FirstOrDefault(row => row.SlotIndex == _selectedSlotIndex);
        if (selectedRow is { } row)
        {
            var target = _model.MobilizationTargetFor(row.SlotIndex, RecruitUnitDialogModel.NextArmyId(Session.State));
            _mobTargetLabel.Text = target.ReceivingArmyId is null
                ? $"The regiment will appear in a new army beside the city."
                : $"The regiment will join army {target.ReceivingArmyId}.";
        }
        else
        {
            _mobTargetLabel.Text = string.Empty;
        }

        _mobilizeButton.Disabled = selectedRow is not { IsReady: true };
        _disbandButton.Disabled = selectedRow is null;
    }

    private void RefreshTrainingList()
    {
        _trainingList.Clear();
        _trainingRows = _model.TrainingAtCity(SelectedCityIdForCheck);
        var reselectRow = -1;
        for (var i = 0; i < _trainingRows.Count; i++)
        {
            _trainingList.AddItem(FormatTrainingRow(_trainingRows[i]));
            if (_trainingRows[i].SlotIndex == _selectedSlotIndex)
            {
                reselectRow = i;
            }
        }

        // ItemList.Clear wiped the widget's selection; restore it when the refreshed list still
        // contains the selected slot, so Mobilize and Disband keep acting on the player's pick.
        if (reselectRow >= 0)
        {
            _trainingList.Select(reselectRow);
        }
    }

    private static string FormatTrainingRow(TrainingRegimentView row) =>
        $"{row.UnitTypeId} — {row.Troops} troops — {row.ReadinessText}";

    /// <summary>
    /// The slot-table index the selected training row names, or <see langword="null"/>. This is the
    /// index the engine's <c>mobilize</c> and <c>disband-slot</c> verbs take, which is the row's
    /// <see cref="TrainingRegimentView.SlotIndex"/>, not its position in this city's list.
    /// </summary>
    private int? SelectedSlotIndex() => _selectedSlotIndex;

    private void SubmitRecruit()
    {
        var cityId = SelectedCityIdForCheck;
        if (string.IsNullOrEmpty(cityId))
        {
            return;
        }

        var line = _model.RecruitStandingLine(cityId, _selectedUnitTypeId, (int)_troops.Value);
        var lines = Submit(line);
        _replyLabel.Text = lines.Skip(1).FirstOrDefault(text => text.Length > 0) ?? string.Empty;
        Refresh();
    }

    private void SubmitMobilize()
    {
        if (SelectedSlotIndex() is not { } slotIndex)
        {
            return;
        }

        var line = _model.MobilizeLine(slotIndex, RecruitUnitDialogModel.NextArmyId(Session.State));
        var lines = Submit(line);
        _replyLabel.Text = lines.Skip(1).FirstOrDefault(text => text.Length > 0) ?? string.Empty;
        _selectedSlotIndex = null;
        Refresh();
    }

    private void SubmitDisband()
    {
        if (SelectedSlotIndex() is not { } slotIndex)
        {
            return;
        }

        var slotCount = _model.Slots.Count;
        if (slotIndex < 0 || slotIndex >= slotCount)
        {
            return;
        }

        // One row can be selected at a time, so the prompt always names one unit. The text is the
        // original's own [derived: code, TArmyRecruits_DisbandUnits :56133-56148].
        var prompt = new ConfirmPrompt { Question = RecruitUnitDialogModel.DisbandPromptText(1) };
        prompt.Confirmed += () =>
        {
            prompt.QueueFree();
            var line = _model.DisbandSlotLine(slotIndex);
            var lines = Submit(line);
            _replyLabel.Text = lines.Skip(1).FirstOrDefault(text => text.Length > 0) ?? string.Empty;
            _selectedSlotIndex = null;
            Refresh();
        };
        prompt.Refused += prompt.QueueFree;

        AddChild(prompt);
        prompt.SetAnchorsPreset(LayoutPreset.FullRect);
    }

    private void Ok() => Closed?.Invoke();

    private void Cancel() => Closed?.Invoke();

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Cancel();
            GetViewport().SetInputAsHandled();
            return;
        }

        // Page Up / Page Down move the troop box by the selected type's page step (1,000, fix #519),
        // the same nudge the Taxation slider's page keys apply to its own step. The SpinBox's Page
        // property is not used because Godot snaps a Range's value to multiples of its Page, which
        // would break the bounds' own arithmetic; the handler clamps to the type's bounds instead.
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Pageup or Key.Pagedown } key)
        {
            var bounds = _model.TroopBoundsFor(_selectedUnitTypeId);
            var delta = key.Keycode == Key.Pageup ? bounds.PageStep : -bounds.PageStep;
            _troops.Value = Math.Clamp((int)_troops.Value + delta, bounds.Minimum, bounds.Maximum);
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Selects a city by id, exactly as the dropdown does — exposed for the headless check.</summary>
    public void SelectCityForCheck(string cityId)
    {
        for (var i = 0; i < _cityIds.Count; i++)
        {
            if (string.Equals(_cityIds[i], cityId, StringComparison.Ordinal))
            {
                _cityDropdown.Select(i);
                _selectedSlotIndex = null;
                Refresh();
                return;
            }
        }
    }

    /// <summary>Selects a unit type by id, exactly as a type-button press does — exposed for the headless check.</summary>
    public void SelectUnitTypeForCheck(string unitTypeId)
    {
        for (var i = 0; i < _model.UnitTypes.Count; i++)
        {
            if (string.Equals(_model.UnitTypes[i].Id, unitTypeId, StringComparison.Ordinal))
            {
                _typeButtons[i].ButtonPressed = true;
                SelectUnitType(unitTypeId);
                return;
            }
        }
    }

    /// <summary>Sets the troop box's value, exactly as a press on the box would — exposed for the headless check.</summary>
    public void SetTroopsForCheck(int troops)
    {
        var bounds = _model.TroopBoundsFor(_selectedUnitTypeId);
        _troops.Value = Math.Clamp(troops, bounds.Minimum, bounds.Maximum);
        Refresh();
    }

    /// <summary>Selects a training-list row by its position, exactly as a click would — exposed for
    /// the headless check. The row's own slot-table index becomes the selection.</summary>
    public void SelectTrainingRowForCheck(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _trainingList.ItemCount || rowIndex >= _trainingRows.Count)
        {
            return;
        }

        _selectedSlotIndex = _trainingRows[rowIndex].SlotIndex;
        _trainingList.Select(rowIndex);
        Refresh();
    }

    /// <summary>The Recruit unit button, exactly as a click on it does — exposed for the headless check.</summary>
    public void RecruitForCheck() => SubmitRecruit();

    /// <summary>The Mobilize button, exactly as a click on it does — exposed for the headless check.</summary>
    public void MobilizeForCheck() => SubmitMobilize();

    /// <summary>The Disband button, exactly as a click on it does — exposed for the headless check.</summary>
    public void DisbandForCheck() => SubmitDisband();

    /// <summary>The dialog's Cancel button, exactly as a click on it does — exposed for the headless check.</summary>
    public void CancelForCheck() => Cancel();
}
