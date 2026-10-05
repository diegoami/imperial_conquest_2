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
    private SpinBox _splitThousands = null!;
    private SpinBox _splitHundreds = null!;

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
        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.6f), MouseFilter = MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.MakePanel(UiKit.PanelColor);
        panel.CustomMinimumSize = new Vector2(560, 0);
        center.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        panel.AddChild(column);

        column.AddChild(UiKit.MakeLabel("Change units", 18, UiKit.AccentColor));
        _unitList = new ItemList
        {
            CustomMinimumSize = new Vector2(0, 180),
            SelectMode = ItemList.SelectModeEnum.Multi,
        };
        column.AddChild(_unitList);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
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
        buttons.AddChild(UiKit.MakeButton("OK", Cancel));
    }

    private Control BuildRenamePanel()
    {
        var panel = new VBoxContainer();
        panel.AddChild(UiKit.MakeLabel("Rename unit", 15, UiKit.TextColor));
        _renameCurrentLabel = UiKit.MakeLabel(string.Empty, 13, UiKit.MutedTextColor);
        panel.AddChild(_renameCurrentLabel);
        panel.AddChild(UiKit.MakeLabel("New name", 13, UiKit.MutedTextColor));
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
        panel.AddChild(UiKit.MakeLabel("Split unit", 15, UiKit.TextColor));

        var spinners = new HBoxContainer();
        spinners.AddThemeConstantOverride("separation", 8);
        panel.AddChild(spinners);
        spinners.AddChild(UiKit.MakeLabel("1000s", 13, UiKit.MutedTextColor));
        _splitThousands = new SpinBox { MinValue = 0, MaxValue = 1_000, Step = 1_000 };
        spinners.AddChild(_splitThousands);
        spinners.AddChild(UiKit.MakeLabel("100s", 13, UiKit.MutedTextColor));
        _splitHundreds = new SpinBox { MinValue = 0, MaxValue = 900, Step = 100, Value = 100 };
        spinners.AddChild(_splitHundreds);

        var row = new HBoxContainer();
        panel.AddChild(row);
        row.AddChild(UiKit.MakeButton("Split", () =>
        {
            if (FirstSelected() is { } index)
            {
                SubmitIfAny(_model.SplitUnitLine(index, SplitTroops()));
            }

            panel.Visible = false;
        }));
        row.AddChild(UiKit.MakeButton("Cancel", () => panel.Visible = false));
        panel.Visible = false;
        return panel;
    }

    /// <summary>The two split spinners' sum, in troops — the original's 1000s and 100s boxes.</summary>
    private int SplitTroops() => (int)_splitThousands.Value + (int)_splitHundreds.Value;

    private void ShowRenamePanel()
    {
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
        _splitPanel.Visible = true;
        _renamePanel.Visible = false;
    }

    private void JoinSelected()
    {
        var selected = _unitList.GetSelectedItems();
        if (selected.Length >= 2)
        {
            SubmitIfAny(_model.JoinUnitsLine(selected[0], selected[1]));
        }
    }

    private void DisbandSelected()
    {
        if (FirstSelected() is { } index)
        {
            SubmitIfAny(_model.DisbandUnitLine(index));
        }
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

    /// <summary>Joins two units, exactly as the Join units button does.</summary>
    public void JoinUnitsForCheck(int firstIndex, int secondIndex) =>
        SubmitIfAny(_model.JoinUnitsLine(firstIndex, secondIndex));

    /// <summary>Disbands one unit, exactly as the Disband button does.</summary>
    public void DisbandUnitForCheck(int index) => SubmitIfAny(_model.DisbandUnitLine(index));

    /// <summary>OK, exactly as its button does: close, submitting nothing further.</summary>
    public void OkForCheck() => Cancel();

    /// <summary>Cancel, exactly as its button does: close, submitting nothing.</summary>
    public void CancelForCheck() => Cancel();

    private void Cancel() => Closed?.Invoke();

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Cancel();
            GetViewport().SetInputAsHandled();
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

        _unitList.Clear();
        foreach (var unit in _model.Units)
        {
            var name = string.IsNullOrEmpty(unit.Name) ? string.Empty : $" — {unit.Name}";
            _unitList.AddItem(
                $"{unit.Troops}x {unit.UnitTypeId} ({ArmyDialogModels.QualityCaption(unit.Quality)}){name}");
        }
    }
}
