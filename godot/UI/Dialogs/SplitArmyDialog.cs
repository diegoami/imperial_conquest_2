using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// The Unit map's <strong>Split army</strong> dialog — the original's <c>TUnitMap_SplitArmy</c> opening
/// its army-to-army form titled <em>"Split army"</em>, with the two unit lists; the units moved to the
/// second list form the new army [derived: form and code, <c>TArmyToArmy_InitializeForm</c>,
/// <c>docs/tasks/T111.md</c>]. It needs two or more units, the original's refusal being
/// <see cref="ArmyDialogModels.SplitOneUnitRefusal"/>.
/// </summary>
/// <remarks>
/// <para>
/// The dialog shares <see cref="ArmyTransferDialog"/>'s two-list shape. It composes one
/// <c>split-army</c> and the engine places the new army (<c>docs/tasks/T111.md</c>: "the dialog doesn't
/// place the army"). The engine's <c>split-army</c> CLI takes a single unit index, so the dialog moves
/// the one unit staged to the second list.
/// </para>
/// <para>
/// Rules live in the Godot-free <see cref="SplitArmyModel"/>; this control owns widgets and the submit
/// callback. Cancel submits nothing.
/// </para>
/// </remarks>
public partial class SplitArmyDialog : Control
{
    /// <summary>The session the dialog reads and submits to.</summary>
    public required GameSession Session { get; init; }

    /// <summary>The army being split.</summary>
    public required string ArmyId { get; init; }

    /// <summary>The new army's id, chosen by the screen.</summary>
    public required string NewArmyId { get; init; }

    /// <summary>Submits one composed line through the screen's path and returns the session's lines.</summary>
    public required Func<string, IReadOnlyList<string>> Submit { get; init; }

    /// <summary>Raised by Close, OK and Cancel; <see cref="MainGameScreen"/> closes the overlay.</summary>
    public event Action? Closed;

    private SplitArmyModel _model = null!;
    private ItemList _armyList = null!;
    private ItemList _newArmyList = null!;
    private Label _refusalLabel = null!;
    private Label _replyLabel = null!;
    private Button _okButton = null!;
    private readonly List<UnitSlot> _staged = new();

    /// <summary>The live model, exposed so a headless check can read the refusal and the staged unit.</summary>
    public SplitArmyModel ModelForCheck => _model;

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

        _model = SplitArmyModel.ForArmy(Session.State, army, Session.Ruleset, NewArmyId);
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
        panel.CustomMinimumSize = new Vector2(640, 0);
        center.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        panel.AddChild(column);

        column.AddChild(UiKit.MakeLabel("Split army", 18, UiKit.AccentColor));
        _refusalLabel = UiKit.MakeLabel(string.Empty, 14, UiKit.TextColor);
        _refusalLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_refusalLabel);

        var lists = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        lists.AddThemeConstantOverride("separation", 12);
        column.AddChild(lists);

        var left = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddChild(UiKit.MakeLabel($"Army {ArmyId}", 15, UiKit.TextColor));
        _armyList = new ItemList { CustomMinimumSize = new Vector2(280, 140), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        left.AddChild(_armyList);
        left.AddChild(UiKit.MakeButton("Split off", StageSelected));
        lists.AddChild(left);

        var right = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddChild(UiKit.MakeLabel($"New army {NewArmyId}", 15, UiKit.TextColor));
        _newArmyList = new ItemList { CustomMinimumSize = new Vector2(280, 140), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        right.AddChild(_newArmyList);
        lists.AddChild(right);

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
    }

    private void StageSelected()
    {
        var selected = (int)_armyList.GetSelectedItems().FirstOrDefault();
        var units = Session.State.ArmyById(ArmyId)?.Units;
        if (units is null || selected < 0 || selected >= units.Count || _staged.Count > 0)
        {
            return;
        }

        _model.StageUnit(selected);
        _staged.Add(units[selected]);
        Refresh();
    }

    /// <summary>Stages one army unit, exactly as pressing Split off on it does — a check's seam.</summary>
    public void StageUnitForCheck(int index)
    {
        _model.StageUnit(index);
        var units = Session.State.ArmyById(ArmyId)?.Units;
        if (units is not null && index >= 0 && index < units.Count)
        {
            _staged.Clear();
            _staged.Add(units[index]);
        }

        Refresh();
    }

    /// <summary>OK, exactly as its button does: one composed <c>split-army</c>, then close.</summary>
    public void OkForCheck() => Ok();

    /// <summary>Cancel, exactly as its button does: nothing is submitted.</summary>
    public void CancelForCheck() => Cancel();

    private void Ok()
    {
        var line = _model.ComposeOk();
        if (line is null)
        {
            return;
        }

        var lines = Submit(line);
        _replyLabel.Text = lines.Skip(1).FirstOrDefault(text => text.Length > 0) ?? string.Empty;
        Refresh();
        Closed?.Invoke();
    }

    private void Cancel()
    {
        _model.Cancel();
        Closed?.Invoke();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Cancel();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Refresh()
    {
        if (_model is null)
        {
            return;
        }

        var army = Session.State.ArmyById(ArmyId);
        _refusalLabel.Text = _model.RefusalMessage ?? string.Empty;
        _okButton.Disabled = !_model.CanSplit;

        _armyList.Clear();
        if (army is not null)
        {
            for (var i = 0; i < army.Units.Count; i++)
            {
                var unit = army.Units[i];
                if (_staged.Contains(unit))
                {
                    continue;
                }

                _armyList.AddItem(FormatUnit(unit));
            }
        }

        _newArmyList.Clear();
        foreach (var unit in _staged)
        {
            _newArmyList.AddItem(FormatUnit(unit));
        }
    }

    private static string FormatUnit(UnitSlot unit) =>
        $"{unit.Troops}x {unit.UnitTypeId} ({ArmyDialogModels.QualityCaption(unit.Quality)})"
        + (string.IsNullOrEmpty(unit.Name) ? string.Empty : $" — {unit.Name}");
}
