using Godot;
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
/// Transfer on it again puts it back. Disband submits T107's <c>disband-unit</c> at once. <c>OK</c>
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

    /// <summary>Submits one composed line through the screen's path and returns the session's lines.</summary>
    public required Func<string, IReadOnlyList<string>> Submit { get; init; }

    /// <summary>Raised by Close, OK and Cancel; <see cref="MainGameScreen"/> closes the overlay.</summary>
    public event Action? Closed;

    private ArmyTransferModel _model = null!;
    private ItemList _selectedList = null!;
    private ItemList _partnerList = null!;
    private Label _figuresLabel = null!;
    private SpinBox _supplySpin = null!;
    private SpinBox _moneySpin = null!;
    private Label _supplyLabel = null!;
    private Label _moneyLabel = null!;
    private Label _replyLabel = null!;
    private bool _updatingSpinners;

    // The display rows: each unit slot with its source army and source index, and which side it is
    // currently shown on (true = the partner's list). A staged unit flips sides; its source index never
    // changes, which is exactly what the composed command's units=/back-units= need.
    private readonly List<TransferRow> _rows = new();

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

        _model = ArmyTransferModel.ForArmies(selected, partner, Session.Ruleset);
        for (var i = 0; i < selected.Units.Count; i++)
        {
            _rows.Add(new TransferRow(selected.Id, i, selected.Units[i], OnPartnerSide: false));
        }

        for (var i = 0; i < partner.Units.Count; i++)
        {
            _rows.Add(new TransferRow(partner.Id, i, partner.Units[i], OnPartnerSide: true));
        }

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
        panel.CustomMinimumSize = new Vector2(760, 0);
        center.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        panel.AddChild(column);

        column.AddChild(UiKit.MakeLabel("Army to army transfer", 18, UiKit.AccentColor));
        _figuresLabel = UiKit.MakeLabel(string.Empty, 14, UiKit.TextColor);
        _figuresLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_figuresLabel);

        var lists = new HBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        lists.AddThemeConstantOverride("separation", 12);
        column.AddChild(lists);
        _selectedList = BuildSide(lists, $"Army {ArmyId}", isSelectedSide: true);
        _partnerList = BuildSide(lists, $"Army {PartnerId}", isSelectedSide: false);

        column.AddChild(new HSeparator());
        column.AddChild(BuildSupplyRow());
        column.AddChild(BuildMoneyRow());

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
        buttons.AddChild(UiKit.MakeButton("OK", Ok));
    }

    private ItemList BuildSide(Container parent, string title, bool isSelectedSide)
    {
        var box = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 4);
        parent.AddChild(box);
        box.AddChild(UiKit.MakeLabel(title, 15, UiKit.TextColor));

        var list = new ItemList { CustomMinimumSize = new Vector2(320, 140), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        box.AddChild(list);

        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        box.AddChild(actions);
        actions.AddChild(UiKit.MakeButton("Transfer", () => TransferSelected(isSelectedSide)));
        actions.AddChild(UiKit.MakeButton("Disband", () => DisbandSelected(isSelectedSide)));
        return list;
    }

    private Control BuildSupplyRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(UiKit.MakeLabel("Supply", 14, UiKit.TextColor));
        row.AddChild(UiKit.MakeButton("-100 t", () => PressSupply(-ArmyDialogModels.SupplyLargeStepTons)));
        _supplySpin = new SpinBox { Step = ArmyDialogModels.SupplyStepTons, CustomMinimumSize = new Vector2(120, 0) };
        _supplySpin.ValueChanged += _ => SpinSupply();
        row.AddChild(_supplySpin);
        row.AddChild(UiKit.MakeButton("+100 t", () => PressSupply(ArmyDialogModels.SupplyLargeStepTons)));
        _supplyLabel = UiKit.MakeLabel(string.Empty, 13, UiKit.MutedTextColor);
        row.AddChild(_supplyLabel);
        return row;
    }

    private Control BuildMoneyRow()
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(UiKit.MakeLabel("Money", 14, UiKit.TextColor));
        row.AddChild(UiKit.MakeButton("-100", () => PressMoney(-ArmyDialogModels.MoneyLargeStepTalents)));
        _moneySpin = new SpinBox { Step = ArmyDialogModels.MoneyStepTalents, CustomMinimumSize = new Vector2(120, 0) };
        _moneySpin.ValueChanged += _ => SpinMoney();
        row.AddChild(_moneySpin);
        row.AddChild(UiKit.MakeButton("+100", () => PressMoney(ArmyDialogModels.MoneyLargeStepTalents)));
        _moneyLabel = UiKit.MakeLabel(string.Empty, 13, UiKit.MutedTextColor);
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
        var row = SelectedRow(isSelectedSide);
        if (row is null)
        {
            return;
        }

        Toggle(row);
        Refresh();
    }

    private void DisbandSelected(bool isSelectedSide)
    {
        if (SelectedRow(isSelectedSide) is { } row)
        {
            DisbandSelected(row);
        }
    }

    private void DisbandSelected(TransferRow row)
    {
        var lines = Submit($"disband-unit {row.ArmyId} {row.SourceIndex}");
        _replyLabel.Text = lines.Skip(1).FirstOrDefault(text => text.Length > 0) ?? string.Empty;

        // The unit is gone from the live state, so rebuild the model and the display lists from it
        // (any staging is dropped, exactly as a fresh dialog would open).
        if (!RebuildFromState())
        {
            Closed?.Invoke();
            return;
        }

        Refresh();
    }

    /// <summary>Re-reads both armies from the live state; false when either is gone.</summary>
    private bool RebuildFromState()
    {
        var selected = Session.State.ArmyById(ArmyId);
        var partner = Session.State.ArmyById(PartnerId);
        if (selected is null || partner is null)
        {
            return false;
        }

        _model = ArmyTransferModel.ForArmies(selected, partner, Session.Ruleset);
        _rows.Clear();
        for (var i = 0; i < selected.Units.Count; i++)
        {
            _rows.Add(new TransferRow(selected.Id, i, selected.Units[i], OnPartnerSide: false));
        }

        for (var i = 0; i < partner.Units.Count; i++)
        {
            _rows.Add(new TransferRow(partner.Id, i, partner.Units[i], OnPartnerSide: true));
        }

        return true;
    }

    /// <summary>
    /// Flips one row between the two lists, keeping the model's staged set in step: a row of the
    /// selected army on the partner side is staged as <c>units=</c>, a row of the partner army on the
    /// selected side is staged as <c>back-units=</c>.
    /// </summary>
    private void Toggle(TransferRow row)
    {
        var nowPartnerSide = !row.OnPartnerSide;
        var index = _rows.FindIndex(candidate => ReferenceEquals(candidate, row));
        if (index < 0)
        {
            return;
        }

        _rows[index] = row with { OnPartnerSide = nowPartnerSide };

        if (string.Equals(row.ArmyId, _model.SelectedArmyId, StringComparison.Ordinal))
        {
            if (nowPartnerSide)
            {
                _model.StageUnitToPartner(row.SourceIndex);
            }
            else
            {
                _model.UnstageUnitToPartner(row.SourceIndex);
            }
        }
        else
        {
            if (nowPartnerSide)
            {
                _model.UnstageUnitBack(row.SourceIndex);
            }
            else
            {
                _model.StageUnitBack(row.SourceIndex);
            }
        }
    }

    private TransferRow? SelectedRow(bool isSelectedSide)
    {
        var list = isSelectedSide ? _selectedList : _partnerList;
        var selected = (int)list.GetSelectedItems().FirstOrDefault();
        var rows = Rows(isSelectedSide);
        return selected >= 0 && selected < rows.Count ? rows[selected] : null;
    }

    private List<TransferRow> Rows(bool isSelectedSide) =>
        _rows.Where(row => row.OnPartnerSide != isSelectedSide).ToList();

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
        var row = _rows.FirstOrDefault(
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
        var row = _rows.FirstOrDefault(
            candidate => string.Equals(candidate.ArmyId, armyId, StringComparison.Ordinal)
                && candidate.SourceIndex == sourceIndex);
        if (row is null)
        {
            return false;
        }

        DisbandSelected(row);
        return true;
    }

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
        SubmitIfAny(_model.ComposeOk());
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

    private void SubmitIfAny(string? line)
    {
        if (line is null)
        {
            return;
        }

        var lines = Submit(line);
        _replyLabel.Text = lines.Skip(1).FirstOrDefault(text => text.Length > 0) ?? string.Empty;
        Refresh();
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
            : $"{ArmyId}: {selected.TotalTroops} troops · {selected.SupplyTons} t · {selected.Money} talents"
              + $"   |   {PartnerId}: {partner.TotalTroops} troops · {partner.SupplyTons} t · {partner.Money} talents";

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

        _supplyLabel.Text = NetLabel("Supply", _model.SupplyNet, _model.MaxSupplyToPartner, _model.MaxSupplyBack);
        _moneyLabel.Text = NetLabel("Money", _model.MoneyNet, _model.MaxMoneyToPartner, _model.MaxMoneyBack);
    }

    private static void Fill(ItemList list, IEnumerable<TransferRow> rows)
    {
        list.Clear();
        foreach (var row in rows)
        {
            var name = string.IsNullOrEmpty(row.Unit.Name) ? string.Empty : $" — {row.Unit.Name}";
            list.AddItem(
                $"{row.Unit.Troops}x {row.Unit.UnitTypeId} ({ArmyDialogModels.QualityCaption(row.Unit.Quality)}){name}");
        }
    }

    private static string NetLabel(string resource, int net, int maxToPartner, int maxBack)
    {
        if (net > 0)
        {
            return $"{resource}: +{net} (A → B)  ·  max +{maxToPartner}";
        }

        if (net < 0)
        {
            return $"{resource}: {net} (B → A)  ·  max -{maxBack}";
        }

        return $"{resource}: 0  ·  max +{maxToPartner} / -{maxBack}";
    }
}
