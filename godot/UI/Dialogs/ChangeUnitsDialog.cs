using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// The Unit map's <strong>Change units</strong> dialog — the original's <c>TChangeArmyUnits</c>, whose
/// buttons are Rename unit, Split unit, Join units, Disband, OK and Cancel [derived: form, the feature
/// inventory rows UA06, D05 and D06; <c>docs/tasks/T111.md</c>]. It renames, splits, joins and disbands
/// single units through T107's <c>rename-unit</c>, <c>split-unit</c> and <c>disband-unit</c> and the
/// existing <c>join-units</c>.
/// </summary>
/// <remarks>
/// <para>
/// The engine exposes each verb as its own command, so each button composes and submits the one command
/// it names and the list refreshes with the session's answer in <see cref="_replyLabel"/>; OK and Cancel
/// close the dialog. The units are listed with their regiment quality, the same caption the army panel
/// and the unit list print.
/// </para>
/// <para>
/// Rules and composed lines live in the Godot-free <see cref="ChangeUnitsModel"/>.
/// </para>
/// </remarks>
public partial class ChangeUnitsDialog : Control
{
    /// <summary>The session the dialog reads and submits to.</summary>
    public required GameSession Session { get; init; }

    /// <summary>The army whose units the dialog lists.</summary>
    public required string ArmyId { get; init; }

    /// <summary>Submits one composed line through the screen's path and returns the session's lines.</summary>
    public required Func<string, IReadOnlyList<string>> Submit { get; init; }

    /// <summary>Raised by Close, OK and Cancel; <see cref="MainGameScreen"/> closes the overlay.</summary>
    public event Action? Closed;

    private ChangeUnitsModel _model = null!;
    private ItemList _unitList = null!;
    private Label _replyLabel = null!;
    private Control _renamePanel = null!;
    private Label _renameCurrentLabel = null!;
    private LineEdit _renameEdit = null!;
    private Control _splitPanel = null!;
    private SpinBox _splitAmount = null!;
    private Label _splitSourceLabel = null!;
    private Label _splitBalances = null!;
    private Button _splitButton = null!;
    private int? _splitUnitIndex;
    private bool _updatingSplit;

    /// <summary>The live model, exposed so a headless check can read the army's units.</summary>
    public ChangeUnitsModel ModelForCheck => _model;

    /// <summary>The session's own reply line for the last command submitted from this dialog.</summary>
    public string ReplyForCheck => _replyLabel.Text;

    public override void _Ready()
    {
        var army = Session.State.ArmyById(ArmyId);
        if (army is null)
        {
            Closed?.Invoke();
            return;
        }

        _model = ChangeUnitsModel.ForArmy(army);
        BuildUi();
        Refresh();
    }

    private void BuildUi()
    {
        var backdrop = new ColorRect { Color = AppTheme.Scrim, MouseFilter = MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.MakePanel(UiKit.PanelColor);
        panel.CustomMinimumSize = new Vector2(560, 0);
        center.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        panel.AddChild(column);

        column.AddChild(UiKit.MakeLabel("Change units", AppTheme.Header, UiKit.AccentColor));
        _unitList = new ItemList
        {
            CustomMinimumSize = new Vector2(0, 180),
            SelectMode = ItemList.SelectModeEnum.Multi,
        };
        _unitList.ItemSelected += _ => RefreshSplitSelection();
        _unitList.MultiSelected += (_, _) => RefreshSplitSelection();
        column.AddChild(_unitList);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        column.AddChild(actions);
        actions.AddChild(UiKit.MakeButton("Rename unit", ShowRenamePanel));
        actions.AddChild(UiKit.MakeButton("Split unit", ShowSplitPanel));
        actions.AddChild(UiKit.MakeButton("Join units", JoinSelected));
        actions.AddChild(UiKit.MakeButton("Disband", DisbandSelected));

        _renamePanel = BuildRenamePanel();
        column.AddChild(_renamePanel);
        _splitPanel = BuildSplitPanel();
        column.AddChild(_splitPanel);

        column.AddChild(new HSeparator());
        _replyLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.TextColor);
        _replyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_replyLabel);

        var buttons = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        buttons.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        column.AddChild(buttons);
        buttons.AddChild(UiKit.MakeButton("Cancel", Cancel));
        buttons.AddChild(UiKit.MakeButton("OK", Cancel));
    }

    private Control BuildRenamePanel()
    {
        var panel = new VBoxContainer();
        panel.AddChild(UiKit.MakeLabel("Rename unit", AppTheme.Body, UiKit.TextColor));
        _renameCurrentLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.MutedTextColor);
        panel.AddChild(_renameCurrentLabel);
        panel.AddChild(UiKit.MakeLabel("New name", AppTheme.Body, UiKit.MutedTextColor));
        _renameEdit = new LineEdit { CustomMinimumSize = new Vector2(320, 0) };
        panel.AddChild(_renameEdit);
        var row = new HBoxContainer();
        panel.AddChild(row);
        row.AddChild(UiKit.MakeButton("Rename", () =>
        {
            if (FirstSelected() is { } index)
            {
                SubmitIfAny(_model.RenameLine(index, _renameEdit.Text.Trim()));
            }

            panel.Visible = false;
        }));
        row.AddChild(UiKit.MakeButton("Cancel", () => panel.Visible = false));
        panel.Visible = false;
        return panel;
    }

    private Control BuildSplitPanel()
    {
        var panel = new VBoxContainer();
        panel.AddChild(UiKit.MakeLabel("Split unit", AppTheme.Body, UiKit.TextColor));
        _splitSourceLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.TextColor);
        panel.AddChild(_splitSourceLabel);
        _splitBalances = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.TextColor);
        _splitBalances.Name = "SplitBalances";
        panel.AddChild(_splitBalances);

        var spinners = new HBoxContainer();
        spinners.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        panel.AddChild(spinners);
        spinners.AddChild(UiKit.MakeLabel("New unit", AppTheme.Body, UiKit.MutedTextColor));
        _splitAmount = new SpinBox { Step = 1, CustomMinimumSize = new Vector2(110, 0) };
        _splitAmount.ValueChanged += _ => UpdateSplitBalances();
        spinners.AddChild(_splitAmount);
        foreach (var delta in new[] { -1000, -100, 100, 1000 })
        {
            var step = delta;
            spinners.AddChild(UiKit.MakeButton(delta > 0 ? $"+{delta}" : $"{delta}", () =>
                _splitAmount.Value = Math.Clamp(_splitAmount.Value + step, _splitAmount.MinValue, _splitAmount.MaxValue)));
        }

        var row = new HBoxContainer();
        panel.AddChild(row);
        _splitButton = UiKit.MakeButton("Split", SubmitSplit);
        row.AddChild(_splitButton);
        row.AddChild(UiKit.MakeButton("Cancel", () =>
        {
            panel.Visible = false;
            _splitUnitIndex = null;
        }));
        panel.Visible = false;
        return panel;
    }

    private void SubmitSplit()
    {
        if (_splitUnitIndex is not { } index || FirstSelected() != index
            || _unitList.GetSelectedItems().Length != 1) return;
        var bounds = _model.SplitBounds(index, Session.Ruleset);
        var amount = (int)_splitAmount.Value;
        if (!bounds.CanSplit || amount < bounds.Minimum || amount > bounds.Maximum)
        {
            _replyLabel.Text = bounds.Refusal ?? "Choose a valid size for both units.";
            return;
        }
        SubmitIfAny(_model.SplitUnitLine(index, amount));
        _splitPanel.Visible = false;
    }

    private void ShowRenamePanel()
    {
        if (RefuseMoreThanOne(ChangeUnitsModel.RenameOneAtATime))
        {
            return;
        }

        if (FirstSelected() is not { } index)
        {
            return;
        }

        var current = Session.State.ArmyById(ArmyId)?.Units[index].Name ?? string.Empty;
        _renameCurrentLabel.Text = $"Current name: {current}";
        _renameEdit.Text = current;
        _renamePanel.Visible = true;
        _splitPanel.Visible = false;
    }

    private void ShowSplitPanel()
    {
        if (RefuseMoreThanOne(ChangeUnitsModel.SplitOneAtATime))
        {
            return;
        }
        var index = FirstSelected() ?? -1;
        var bounds = _model.SplitBounds(index, Session.Ruleset);
        if (!bounds.CanSplit)
        {
            _replyLabel.Text = bounds.Refusal!;
            _splitPanel.Visible = false;
            _splitUnitIndex = null;
            return;
        }
        _splitUnitIndex = index;
        _updatingSplit = true;
        _splitAmount.MinValue = bounds.Minimum;
        _splitAmount.MaxValue = bounds.Maximum;
        _splitAmount.Value = bounds.DefaultTroops;
        _updatingSplit = false;
        var unit = _model.Units[index];
        _splitSourceLabel.Text = $"{unit.Name} · {unit.Troops:N0} troops";
        _splitPanel.Visible = true;
        _renamePanel.Visible = false;
        UpdateSplitBalances();
    }

    private void RefreshSplitSelection()
    {
        if (_splitPanel is { Visible: true }) ShowSplitPanel();
    }

    private void UpdateSplitBalances()
    {
        if (_updatingSplit || _splitUnitIndex is not { } index) return;
        var source = _model.Units[index];
        var amount = (int)_splitAmount.Value;
        _splitBalances.Text = $"Kept: {source.Troops - amount:N0} · New: {amount:N0} troops";
        var bounds = _model.SplitBounds(index, Session.Ruleset);
        _splitButton.Disabled = !bounds.CanSplit || amount < bounds.Minimum || amount > bounds.Maximum;
    }

    private void JoinSelected()
    {
        var selected = _unitList.GetSelectedItems();
        if (selected.Length >= 2)
        {
            SubmitIfAny(_model.JoinUnitsLine(selected));
        }
    }

    private void DisbandSelected()
    {
        var selected = _unitList.GetSelectedItems();
        if (selected.Length == 0)
        {
            return;
        }

        // The original asks first and acts on every selected unit [derived: code, row P08]. The text and
        // the one-order-per-unit composition are the model's; this dialog only wires the prompt.
        var prompt = new ConfirmPrompt { Question = ChangeUnitsModel.DisbandPromptText(selected.Length) };
        prompt.Confirmed += () =>
        {
            DismissPrompt(prompt);
            SubmitDisbandOrders(selected, confirmed: true);
        };
        prompt.Refused += () =>
        {
            DismissPrompt(prompt);
            SubmitDisbandOrders(selected, confirmed: false);
        };
        AddChild(prompt);
        prompt.SetAnchorsPreset(LayoutPreset.FullRect);
    }

    /// <summary>Submits the orders the model composes for the answered prompt; No composes none.</summary>
    private void SubmitDisbandOrders(IReadOnlyList<int> selectedIndexes, bool confirmed)
    {
        foreach (var line in _model.DisbandOrders(selectedIndexes, confirmed))
        {
            SubmitIfAny(line);
        }
    }

    private static void DismissPrompt(ConfirmPrompt prompt) => prompt.QueueFree();

    /// <summary>The original acts on one unit only for Rename and Split; with more selected it says so and does nothing.</summary>
    private bool RefuseMoreThanOne(string refusal)
    {
        if (_unitList.GetSelectedItems().Length <= 1)
        {
            return false;
        }

        _replyLabel.Text = refusal;
        _renamePanel.Visible = false;
        _splitPanel.Visible = false;
        return true;
    }

    private int? FirstSelected()
    {
        var selected = _unitList.GetSelectedItems();
        return selected.Length > 0 ? selected[0] : null;
    }

    // ---- check seams: the same composition and submit the buttons use ----

    /// <summary>The unit list widget, so a check can clear or set the selection the way a click does.</summary>
    public ItemList UnitListForCheck => _unitList;

    /// <summary>Selects a unit row by index, the way a click does.</summary>
    public void SelectUnitForCheck(int index)
    {
        if (index >= 0 && index < _model.Units.Count)
        {
            _unitList.Select(index);
        }
    }

    /// <summary>Renames one unit, exactly as the Rename panel's Rename button does.</summary>
    public void RenameUnitForCheck(int index, string name) => SubmitIfAny(_model.RenameLine(index, name.Trim()));

    /// <summary>Splits one unit, exactly as the Split panel's Split button does.</summary>
    public void SplitUnitForCheck(int index, int troops) => SubmitIfAny(_model.SplitUnitLine(index, troops));

    /// <summary>Joins the given units, exactly as the Join units button does.</summary>
    public void JoinUnitsForCheck(params int[] indexes) =>
        SubmitIfAny(_model.JoinUnitsLine(indexes));

    /// <summary>OK, exactly as its button does: close, submitting nothing further.</summary>
    public void OkForCheck() => Cancel();

    /// <summary>Cancel, exactly as its button does: close, submitting nothing.</summary>
    public void CancelForCheck() => Cancel();

    private void Cancel() => Closed?.Invoke();

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Cancel();
        }
    }

    private void SubmitIfAny(string? line)
    {
        if (line is null)
        {
            return;
        }

        var lines = Submit(line);
        _replyLabel.Text = lines.Skip(1).FirstOrDefault(text => text.Length > 0) ?? string.Empty;
        _model = ChangeUnitsModel.ForArmy(Session.State.ArmyById(ArmyId)!);
        Refresh();
    }

    private void Refresh()
    {
        if (_model is null)
        {
            return;
        }

        _splitPanel.Visible = false;
        _splitUnitIndex = null;
        _unitList.Clear();
        foreach (var unit in _model.Units)
        {
            _unitList.AddItem(ArmyDialogModels.UnitLine(Session.Ruleset, unit.Troops, unit.UnitTypeId, unit.Quality, unit.Name));
        }
    }
}
