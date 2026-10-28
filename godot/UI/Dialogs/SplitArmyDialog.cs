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
/// The dialog shares <see cref="ArmyTransferDialog"/>'s two-list shape: A is the selected army and B the
/// new army, which opens empty. <b>Transfer</b> under a list moves the selected unit to the other list,
/// the supply and money spinners move the amounts toward B, and <b>OK</b> composes <em>one</em>
/// <c>split-army</c> with every staged unit, the supply and the money (T141's form); the engine places
/// the new army. <b>Cancel</b> submits nothing. <b>Disband</b> under either list submits
/// <c>disband-unit</c> on a private working copy after confirmation. Only OK commits; Cancel discards it (T233).
/// </para>
/// <para>
/// Rules live in the Godot-free <see cref="SplitArmyModel"/>; this control owns widgets and the submit
/// callback. An army aboard a fleet reaches the dialog exactly as one on land does (T142); the screen
/// opens no dialog and says why when no free land cell exists for the new army.
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

    /// <summary>Open with a strength-balanced whole-unit proposal instead of empty staging.</summary>
    public bool SuggestBalanced { get; init; }

    /// <summary>Applies one confirmed plan through the screen’s ordinary outcome path.</summary>
    public Func<DialogCommandDraft, string?, DialogEditResult> CommitEdits { get; init; } = (draft, final) => draft.Commit(final);

    /// <summary>Raised by Close, OK and Cancel; <see cref="MainGameScreen"/> closes the overlay.</summary>
    public event Action? Closed;

    private SplitArmyModel _model = null!;
    private DialogCommandDraft _draft = null!;
    private ItemList _armyList = null!;
    private ItemList _newArmyList = null!;
    private Label _refusalLabel = null!;
    private Label _replyLabel = null!;
    private Label _supplyLabel = null!;
    private Label _moneyLabel = null!;
    private Label _strengthLabel = null!;
    private Label _armyTitle = null!;
    private Label _newArmyTitle = null!;
    private SpinBox _supplySpin = null!;
    private SpinBox _moneySpin = null!;
    private Button _okButton = null!;
    private bool _updatingSpinners;

    // The army-list index each shown row stands for, per list (the armies' own indexes, which the
    // composed commands need).
    private readonly List<int> _armyRows = new();
    private readonly List<int> _newArmyRows = new();

    /// <summary>The live model, exposed so a headless check can read the refusal and the staging.</summary>
    public SplitArmyModel ModelForCheck => _model;

    /// <summary>The session's own reply line for the last command submitted from this dialog.</summary>
    public string ReplyForCheck => _replyLabel.Text;

    /// <summary>The strength preview after the current whole-unit staging.</summary>
    public string StrengthTextForCheck => _strengthLabel.Text;

    public override void _Ready()
    {
        var army = Session.State.ArmyById(ArmyId);
        if (army is null)
        {
            Closed?.Invoke();
            return;
        }

        _draft = Session.BeginDialogEdit(DialogEditKind.Army);
        _model = SplitArmyModel.ForArmy(_draft.WorkingState, army, Session.Ruleset, NewArmyId);
        if (SuggestBalanced) _model.SuggestBalancedSplit();
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
        panel.CustomMinimumSize = new Vector2(680, 0);
        center.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 4);
        panel.AddChild(column);

        column.AddChild(UiKit.MakeLabel("Split army", AppTheme.Header, UiKit.AccentColor));
        _refusalLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.TextColor);
        _refusalLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_refusalLabel);

        var lists = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        lists.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        column.AddChild(lists);
        _armyList = BuildSide(lists, $"Army {ArmyId}", stagedSide: false);
        _newArmyList = BuildSide(lists, $"New army {NewArmyId}", stagedSide: true);

        _strengthLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.TextColor);
        column.AddChild(_strengthLabel);

        column.AddChild(new HSeparator());
        column.AddChild(BuildSupplyRow());
        column.AddChild(BuildMoneyRow());

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
        _okButton = UiKit.MakeButton("OK", Ok);
        buttons.AddChild(_okButton);
    }

    private ItemList BuildSide(Container parent, string title, bool stagedSide)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 4);
        parent.AddChild(box);
        var heading = UiKit.MakeLabel(title, AppTheme.Body, UiKit.TextColor);
        box.AddChild(heading);
        if (stagedSide) _newArmyTitle = heading;
        else _armyTitle = heading;

        var list = new ItemList { CustomMinimumSize = new Vector2(320, 140), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        list.AddThemeFontSizeOverride("font_size", AppTheme.Note);
        list.AddThemeConstantOverride("v_separation", 0);
        box.AddChild(list);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        box.AddChild(actions);
        actions.AddChild(UiKit.MakeButton("Transfer", () => TransferSelected(stagedSide)));
        actions.AddChild(UiKit.MakeButton("Disband", () => DisbandSelected(stagedSide)));
        return list;
    }

    private Control BuildSupplyRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        row.AddChild(UiKit.MakeLabel("Supply", AppTheme.Body, UiKit.TextColor));
        row.AddChild(UiKit.MakeButton("-100 t", () => PressSupply(-ArmyDialogModels.SupplyLargeStepTons)));
        _supplySpin = new SpinBox { Step = ArmyDialogModels.SupplyStepTons, CustomMinimumSize = new Vector2(120, 0) };
        _supplySpin.ValueChanged += _ => SpinSupply();
        row.AddChild(_supplySpin);
        row.AddChild(UiKit.MakeButton("+100 t", () => PressSupply(ArmyDialogModels.SupplyLargeStepTons)));
        _supplyLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.MutedTextColor);
        row.AddChild(_supplyLabel);
        return row;
    }

    private Control BuildMoneyRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        row.AddChild(UiKit.MakeLabel("Money", AppTheme.Body, UiKit.TextColor));
        row.AddChild(UiKit.MakeButton("-100", () => PressMoney(-ArmyDialogModels.MoneyLargeStepTalents)));
        _moneySpin = new SpinBox { Step = ArmyDialogModels.MoneyStepTalents, CustomMinimumSize = new Vector2(120, 0) };
        _moneySpin.ValueChanged += _ => SpinMoney();
        row.AddChild(_moneySpin);
        row.AddChild(UiKit.MakeButton("+100", () => PressMoney(ArmyDialogModels.MoneyLargeStepTalents)));
        _moneyLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.MutedTextColor);
        row.AddChild(_moneyLabel);
        return row;
    }

    private void SpinSupply()
    {
        if (_updatingSpinners)
        {
            return;
        }

        _model.AdjustSupply((int)_supplySpin.Value - _model.Supply);
        Refresh();
    }

    private void SpinMoney()
    {
        if (_updatingSpinners)
        {
            return;
        }

        _model.AdjustMoney((int)_moneySpin.Value - _model.Money);
        Refresh();
    }

    private void PressSupply(int delta)
    {
        _model.AdjustSupply(delta);
        Refresh();
    }

    private void PressMoney(int delta)
    {
        _model.AdjustMoney(delta);
        Refresh();
    }

    /// <summary>The army-list index of the selected row of one list, or -1.</summary>
    private int SelectedArmyIndex(bool stagedSide)
    {
        var list = stagedSide ? _newArmyList : _armyList;
        var rows = stagedSide ? _newArmyRows : _armyRows;
        var picked = list.GetSelectedItems();
        return picked.Length > 0 && picked[0] < rows.Count ? rows[picked[0]] : -1;
    }

    private void TransferSelected(bool stagedSide)
    {
        var index = SelectedArmyIndex(stagedSide);
        if (index < 0)
        {
            return;
        }

        if (stagedSide)
        {
            _model.UnstageUnit(index);
        }
        else
        {
            _model.StageUnit(index);
        }

        Refresh();
    }

    private void DisbandSelected(bool stagedSide)
    {
        var index = SelectedArmyIndex(stagedSide);
        if (index >= 0)
        {
            var prompt = new ConfirmPrompt { Question = ChangeUnitsModel.DisbandPromptText(1) };
            prompt.Confirmed += () => { prompt.QueueFree(); Disband(index); };
            prompt.Refused += prompt.QueueFree;
            AddChild(prompt); prompt.SetAnchorsPreset(LayoutPreset.FullRect);
        }
    }

    /// <summary>
    /// Stages <c>disband-unit</c> for the selected origin unit on the private working copy.
    /// When the private command accepts it, the model re-reads the working army and drops the unit
    /// from the staging; a refusal leaves everything as it was.
    /// </summary>
    private void Disband(int armyIndex)
    {
        var before = _draft.WorkingState.ArmyById(ArmyId)?.Units.Count ?? 0;
        var result = _draft.Stage(_model.DisbandLine(armyIndex));
        var message = CommandOutcomeWording.Apply(CommandOutcomeText.OutcomeBlock(result.Output.Lines));
        _replyLabel.Text = result.Accepted ? $"Pending: {message}" : message;
        var army = _draft.WorkingState.ArmyById(ArmyId);
        if (army is null)
        {
            Closed?.Invoke();
            return;
        }

        if (army.Units.Count < before)
        {
            _model.ApplyDisband(armyIndex, army);
        }

        Refresh();
    }

    /// <summary>The selected army's list widget, so a check can select a row the way a click does.</summary>
    public ItemList ArmyListForCheck => _armyList;

    /// <summary>The new army's list widget, so a check can select a row the way a click does.</summary>
    public ItemList NewArmyListForCheck => _newArmyList;

    /// <summary>Stages a unit exactly as pressing Transfer on it does — a check's seam.</summary>
    public void StageUnitForCheck(int index)
    {
        _model.StageUnit(index);
        Refresh();
    }

    /// <summary>Unstages a unit exactly as Transfer under the new army's list does — a check's seam.</summary>
    public void UnstageUnitForCheck(int index)
    {
        _model.UnstageUnit(index);
        Refresh();
    }

    /// <summary>
    /// Disbands the selected army's unit at <paramref name="armyIndex"/>, exactly as the Disband button
    /// under either list does.
    /// </summary>
    public void DisbandForCheck(int armyIndex) => Disband(armyIndex);

    /// <summary>One press of a supply arrow, exactly as the buttons do.</summary>
    public void PressSupplyForCheck(int delta) => PressSupply(delta);

    /// <summary>One press of a money arrow, exactly as the buttons do.</summary>
    public void PressMoneyForCheck(int delta) => PressMoney(delta);

    /// <summary>OK, exactly as its button does: one composed <c>split-army</c>, then close.</summary>
    public void OkForCheck() => Ok();

    /// <summary>Cancel, exactly as its button does: nothing is submitted.</summary>
    public void CancelForCheck() => Cancel();

    private void Ok()
    {
        var line = _model.ComposeOk();
        var editsOnly = _draft.HasChanges && _model.StagedUnits.Count == 0 && _model.Money == 0 && _model.Supply == 0;
        if (line is null && !editsOnly)
        {
            return;
        }

        var result = CommitEdits(_draft, line);
        _replyLabel.Text = CommandOutcomeWording.Apply(CommandOutcomeText.OutcomeBlock(result.Output.Lines));
        if (result.Accepted) Closed?.Invoke();
    }

    private void Cancel()
    {
        _model.Cancel();
        _draft.Cancel();
        Closed?.Invoke();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Cancel();
        }
    }

    private void Refresh()
    {
        if (_model is null)
        {
            return;
        }

        var army = _draft.WorkingState.ArmyById(ArmyId);
        if (army is not null)
        {
            var preview = _model.PreviewBalances(_draft.WorkingState, Session.World);
            var childTroops = _model.StagedUnits.Sum(i => army.Units[i].Troops);
            _armyTitle.Text = $"{ArmyId} · {army.TotalTroops - childTroops:N0} troops\n"
                + $"Supply {army.SupplyTons:N0} → {preview.ParentSupply:N0} t\n"
                + $"Money {army.Money:N0} → {preview.ParentMoney:N0}";
            _newArmyTitle.Text = $"{NewArmyId} · {childTroops:N0} troops\n"
                + $"Supply 0 → {preview.NewSupply:N0} t\n"
                + $"Money 0 → {preview.NewMoney:N0}";
        }
        var editsOnly = _draft.HasChanges && _model.StagedUnits.Count == 0 && _model.Money == 0 && _model.Supply == 0;
        _refusalLabel.Text = editsOnly ? "OK applies pending edits without creating a new army." : _model.RefusalMessage ?? string.Empty;
        _okButton.Disabled = !editsOnly && (!_model.CanSplit || _model.StagedUnits.Count == 0);

        _armyList.Clear();
        _armyRows.Clear();
        _newArmyList.Clear();
        _newArmyRows.Clear();
        if (army is not null)
        {
            for (var i = 0; i < army.Units.Count; i++)
            {
                var staged = _model.StagedUnits.Contains(i);
                (staged ? _newArmyList : _armyList).AddItem(FormatUnit(army.Units[i]));
                (staged ? _newArmyRows : _armyRows).Add(i);
            }
        }

        _updatingSpinners = true;
        _supplySpin.MinValue = 0;
        _supplySpin.MaxValue = _model.MaxSupply;
        _supplySpin.Value = _model.Supply;
        _moneySpin.MinValue = 0;
        _moneySpin.MaxValue = _model.MaxMoney;
        _moneySpin.Value = _model.Money;
        _updatingSpinners = false;

        _supplyLabel.Text = $"{_model.Supply:N0} t → right";
        _moneyLabel.Text = $"{_model.Money:N0} talents → right";
        _strengthLabel.Text = $"Projected strength: army {_model.ParentPower:N0} · new army {_model.NewArmyPower:N0}";
    }

    private string FormatUnit(UnitSlot unit) =>
        ArmyDialogModels.UnitLine(Session.Ruleset, unit.Troops, unit.UnitTypeId, unit.Quality, unit.Name);
}
