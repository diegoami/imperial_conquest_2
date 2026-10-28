using Godot;
using IC2.Engine.Armies;
using IC2.Engine.Model;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// The Unit map's <strong>Transfer unit</strong> dialog — the original's <c>TArmyToArmy</c> window,
/// titled <em>"Army to army transfer"</em>, whose one <c>OK</c> carries every unit, every ton and every
/// talent of both directions [derived: form and code, <c>TUnitMap_ArmyToArmyTransfer</c> /
/// <c>ArmyTransferDialog_InitializeForm</c>; <c>docs/tasks/T111.md</c>]. The selected army is the left
/// list, the game's own partner is the right; under each is Transfer and Disband, and the two spinners
/// move supply and money.
/// </summary>
/// <remarks>
/// <para>
/// All rules live in the Godot-free <see cref="ArmyTransferModel"/>; this control owns only widgets, the
/// display of the staged units and the submit callback. A staged unit moves to the other list, and
/// Transfer on it again puts it back. Disband stages T107's <c>disband-unit</c> after confirmation. <c>OK</c>
/// submits exactly one <c>army-transfer</c> (<c>docs/tasks/T117.md</c>); Cancel submits nothing.
/// </para>
/// <para>
/// <strong>Feedback.</strong> Every command goes through the screen's own submit path, so
/// <see cref="MainGameScreen.CommandIssued"/> counts it once and the session's own reply shows in
/// <see cref="_replyLabel"/>.
/// </para>
/// </remarks>
public partial class ArmyTransferDialog : Control
{
    /// <summary>The session the dialog reads and submits to.</summary>
    public required GameSession Session { get; init; }

    /// <summary>A, the selected army — the command's first id.</summary>
    public required string ArmyId { get; init; }

    /// <summary>B, the game-picked partner — the command's second id.</summary>
    public required string PartnerId { get; init; }

    /// <summary>Applies one confirmed plan through the screen’s ordinary outcome path.</summary>
    public Func<DialogCommandDraft, string?, DialogEditResult> CommitEdits { get; init; } = (draft, final) => draft.Commit(final);

    /// <summary>Raised by Close, OK and Cancel; <see cref="MainGameScreen"/> closes the overlay.</summary>
    public event Action? Closed;

    private ArmyTransferModel _model = null!;
    private DialogCommandDraft _draft = null!;
    private ItemList _selectedList = null!;
    private ItemList _partnerList = null!;
    private Label _figuresLabel = null!;
    private Label _selectedTitle = null!;
    private Label _partnerTitle = null!;
    private SpinBox _supplySpin = null!;
    private SpinBox _moneySpin = null!;
    private Label _supplyLabel = null!;
    private Label _moneyLabel = null!;
    private Label _replyLabel = null!;
    private Button _okButton = null!;
    private bool _updatingSpinners;

    public bool ConfirmationEnabledForCheck => !_okButton.Disabled;
    public string PreviewNoticeForCheck => _figuresLabel.Text;

    // The display rows: each unit slot with its source army and source index, and which side it is
    // currently shown on (true = the partner's list). A staged unit flips sides; its source index never
    // changes, which is exactly what the composed command's units=/back-units= need.

    private sealed record TransferRow(string ArmyId, int SourceIndex, UnitSlot Unit, bool OnPartnerSide);

    /// <summary>The live model, exposed so a headless check can read the staged nets and limits.</summary>
    public ArmyTransferModel ModelForCheck => _model;

    /// <summary>The session's own reply line for the last command submitted from this dialog.</summary>
    public string ReplyForCheck => _replyLabel.Text;

    public override void _Ready()
    {
        var selected = Session.State.ArmyById(ArmyId);
        var partner = Session.State.ArmyById(PartnerId);
        if (selected is null || partner is null)
        {
            Closed?.Invoke();
            return;
        }

        _draft = Session.BeginDialogEdit(DialogEditKind.Army);
        _model = ArmyTransferModel.ForArmies(selected, partner, Session.Ruleset, _draft.WorkingState, Session.World);

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
        panel.CustomMinimumSize = new Vector2(760, 0);
        center.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 4);
        panel.AddChild(column);

        column.AddChild(UiKit.MakeLabel("Army transfer", AppTheme.Header, UiKit.AccentColor));
        _figuresLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.TextColor);
        _figuresLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_figuresLabel);

        var lists = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        lists.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        column.AddChild(lists);
        _selectedList = BuildSide(lists, $"Army {ArmyId}", isSelectedSide: true);
        _partnerList = BuildSide(lists, $"Army {PartnerId}", isSelectedSide: false);
        // Each side's heading carries its troop total (set in Refresh).

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

    private ItemList BuildSide(Container parent, string title, bool isSelectedSide)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 4);
        parent.AddChild(box);
        var heading = UiKit.MakeLabel(title, AppTheme.Body, UiKit.TextColor);
        box.AddChild(heading);
        if (isSelectedSide)
        {
            _selectedTitle = heading;
        }
        else
        {
            _partnerTitle = heading;
        }

        var list = new ItemList { CustomMinimumSize = new Vector2(320, 140), SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SelectMode = ItemList.SelectModeEnum.Multi };
        list.AddThemeFontSizeOverride("font_size", AppTheme.Note);
        list.AddThemeConstantOverride("v_separation", 0);
        box.AddChild(list);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        box.AddChild(actions);
        actions.AddChild(UiKit.MakeButton("Transfer", () => TransferSelected(isSelectedSide)));
        actions.AddChild(UiKit.MakeButton("Disband", () => DisbandSelected(isSelectedSide)));
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

        _model.AdjustSupply((int)_supplySpin.Value - _model.SupplyNet);
        Refresh();
    }

    private void SpinMoney()
    {
        if (_updatingSpinners)
        {
            return;
        }

        _model.AdjustMoney((int)_moneySpin.Value - _model.MoneyNet);
        Refresh();
    }

    private void TransferSelected(bool isSelectedSide)
    {
        var rows = Rows(isSelectedSide);
        var list = isSelectedSide ? _selectedList : _partnerList;
        var picked = list.GetSelectedItems().Where(i => i >= 0 && i < rows.Count).OrderByDescending(i => i)
            .Select(i => rows[i]).ToArray();
        if (picked.Length == 0) return;
        StageRows(picked);
        Refresh();
    }

    private void DisbandSelected(bool isSelectedSide)
    {
        var list = isSelectedSide ? _selectedList : _partnerList;
        var rows = Rows(isSelectedSide);
        var selected = list.GetSelectedItems().Where(i => i >= 0 && i < rows.Count).Select(i => rows[i])
            .OrderBy(r => r.ArmyId, StringComparer.Ordinal).ThenByDescending(r => r.SourceIndex).ToArray();
        if (selected.Length == 0) return;
        var prompt = new ConfirmPrompt { Question = ChangeUnitsModel.DisbandPromptText(selected.Length) };
        prompt.Confirmed += () =>
        {
            prompt.QueueFree();
            StageDisbands(selected);
        };
        prompt.Refused += prompt.QueueFree;
        AddChild(prompt); prompt.SetAnchorsPreset(LayoutPreset.FullRect);
    }

    private void StageDisbands(TransferRow[] selected)
    {
        var result = _draft.StageMany(selected.Select(r => $"disband-unit {r.ArmyId} {r.SourceIndex}"));
        var message = CommandOutcomeWording.Apply(CommandOutcomeText.OutcomeBlock(result.Output.Lines));
        _replyLabel.Text = result.Accepted ? $"Pending: {selected.Length} unit(s) disbanded." : message;
        if (result.Accepted) _model = _model.AfterDisbands(_draft.WorkingState, selected.Select(r => (r.ArmyId, r.SourceIndex)));
        Refresh();
    }

    /// <summary>
    /// Requests one whole-unit move using its original identity and current working side.
    /// </summary>
    private void Toggle(TransferRow row)
    {
        StageRows(new[] { row });
    }

    private void StageRows(IEnumerable<TransferRow> rows)
    {
        _model.StageTransfers(rows.Select(row => new ArmyTransferUnitStep(row.ArmyId == ArmyId,
            row.SourceIndex, !row.OnPartnerSide)), atomic: true);
        _replyLabel.Text = string.Join('\n', _model.LastUnitMessages);
    }

    private List<TransferRow> Rows(bool isSelectedSide) =>
        (isSelectedSide ? _model.SelectedWorkingUnits : _model.PartnerWorkingUnits)
            .Select(row => new TransferRow(row.SelectedOrigin ? ArmyId : PartnerId, row.Index, row.Unit,
                OnPartnerSide: !isSelectedSide)).ToList();

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

    /// <summary>
    /// Transfers the unit at <paramref name="sourceIndex"/> of <paramref name="armyId"/> to the other
    /// list, exactly as pressing Transfer on it does — a check addresses a unit by its source, not by a
    /// display position that the staging itself can shift.
    /// </summary>
    public bool TransferBySourceForCheck(string armyId, int sourceIndex)
    {
        var row = Rows(true).Concat(Rows(false)).FirstOrDefault(
            candidate => string.Equals(candidate.ArmyId, armyId, StringComparison.Ordinal)
                && candidate.SourceIndex == sourceIndex);
        if (row is null)
        {
            return false;
        }

        Toggle(row);
        Refresh();
        return true;
    }

    /// <summary>Disbands the unit at <paramref name="sourceIndex"/>, exactly as its Disband button does.</summary>
    public bool DisbandBySourceForCheck(string armyId, int sourceIndex)
    {
        var row = Rows(true).Concat(Rows(false)).FirstOrDefault(
            candidate => string.Equals(candidate.ArmyId, armyId, StringComparison.Ordinal)
                && candidate.SourceIndex == sourceIndex);
        if (row is null)
        {
            return false;
        }

        StageDisbands(new[] { row });
        return true;
    }

    /// <summary>The selected army's list widget, so a check can select a row the way a click does.</summary>
    public ItemList SelectedListForCheck => _selectedList;

    /// <summary>The partner's list widget, so a check can select a row the way a click does.</summary>
    public ItemList PartnerListForCheck => _partnerList;

    public void TransferSelectedForCheck(bool selectedSide) => TransferSelected(selectedSide);
    public void DisbandSelectedForCheck(bool selectedSide) => DisbandSelected(selectedSide);

    /// <summary>One press of a supply arrow, exactly as the buttons do.</summary>
    public void PressSupplyForCheck(int delta) => PressSupply(delta);

    /// <summary>One press of a money arrow, exactly as the buttons do.</summary>
    public void PressMoneyForCheck(int delta) => PressMoney(delta);

    /// <summary>OK, exactly as its button does: one composed <c>army-transfer</c>, then close.</summary>
    public void OkForCheck() => Ok();

    /// <summary>Cancel, exactly as its button does: nothing is submitted.</summary>
    public void CancelForCheck() => Cancel();

    private void Ok()
    {
        if (_model.PreviewRejection is { } rejection)
        {
            _replyLabel.Text = rejection;
            Refresh();
            return;
        }
        var result = CommitEdits(_draft, _model.ComposeOk());
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

        var selected = Session.State.ArmyById(ArmyId);
        var partner = Session.State.ArmyById(PartnerId);
        _figuresLabel.Text = selected is null || partner is null
            ? string.Empty
            : "Pending changes · OK applies · Cancel discards";
        var rejection = _model.PreviewRejection;
        _okButton.Disabled = rejection is not null;
        if (rejection is not null) _figuresLabel.Text = rejection;
        _selectedTitle.Text = $"{ArmyId} · {_model.SelectedTroopTotal:N0} troops\n"
            + $"Supply {_model.SelectedSupplyNow:N0} → {_model.SelectedSupplyAfter:N0} t\n"
            + $"Money {selected?.Money ?? 0:N0} → {_model.SelectedMoneyAfter:N0}";
        _partnerTitle.Text = $"{PartnerId} · {_model.PartnerTroopTotal:N0} troops\n"
            + $"Supply {_model.PartnerSupplyNow:N0} → {_model.PartnerSupplyAfter:N0} t\n"
            + $"Money {partner?.Money ?? 0:N0} → {_model.PartnerMoneyAfter:N0}";

        Fill(_selectedList, Rows(isSelectedSide: true));
        Fill(_partnerList, Rows(isSelectedSide: false));

        _updatingSpinners = true;
        _supplySpin.MinValue = -_model.MaxSupplyBack;
        _supplySpin.MaxValue = _model.MaxSupplyToPartner;
        _supplySpin.Value = _model.SupplyNet;
        _moneySpin.MinValue = -_model.MaxMoneyBack;
        _moneySpin.MaxValue = _model.MaxMoneyToPartner;
        _moneySpin.Value = _model.MoneyNet;
        _updatingSpinners = false;

        _supplyLabel.Text = _model.PendingSupplyText;
        _moneyLabel.Text = _model.PendingMoneyText;
    }

    private void Fill(ItemList list, IEnumerable<TransferRow> rows)
    {
        list.Clear();
        foreach (var row in rows)
        {
            list.AddItem(ArmyDialogModels.UnitLine(Session.Ruleset, row.Unit.Troops, row.Unit.UnitTypeId, row.Unit.Quality, row.Unit.Name));
        }
    }

}
