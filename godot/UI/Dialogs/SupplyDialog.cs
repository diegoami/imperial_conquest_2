using Godot;
using IC2.Engine.Economy;
using IC2.Engine.Model;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// The Army menu's <strong>Supply army</strong> dialog — the original's <c>TAFSupply</c> window for an
/// army [confirmed: <c>docs/investigations/original-ui-command-audit.md</c> §1.6; decompiled
/// <c>TUnitMap_SupplyArmy</c> <c>0x00446F50</c> opens <c>TAFSupply</c> <c>0x0043EDC0</c>]. It lists the
/// providers within one tile, applies a free provider's press at once and stages a foreign city's, and
/// moves money between the army's purse and the treasury (or a co-located fleet).
/// </summary>
/// <remarks>
/// <para>
/// A plain modal overlay added by <see cref="MainGameScreen"/>, the same shape as
/// <see cref="FindCityDialog"/>. All rules live in the Godot-free <see cref="SupplyDialogModel"/>; this
/// control owns only widgets, the submit callback and <see cref="Closed"/>.
/// </para>
/// <para>
/// <strong>Feedback.</strong> Every command goes through the screen's own submit path, so
/// <see cref="MainGameScreen.CommandIssued"/> counts it once, and the session's own reply line is shown
/// in <see cref="_replyLabel"/>. A refusal therefore is never silent.
/// </para>
/// <para>
/// <strong>[designed] design calls, for the visual review:</strong> providers as one selected list; the
/// <c>via</c> picker with Treasury first; each money press applied at once; Close drops a staged paid
/// amount. The steps 10 and 100 tons/talents are the confirmed arrows' own values (no ruleset key holds
/// them).
/// </para>
/// </remarks>
public partial class SupplyDialog : Control
{
    /// <summary>The session the dialog submits to and reads its figures from.</summary>
    public required GameSession Session { get; init; }

    /// <summary>
    /// The army the dialog acts on (the Army menu's Supply army) — <see langword="null"/> when the dialog
    /// is opened for a fleet instead, in which case <see cref="FleetId"/> is set. Exactly one is set.
    /// </summary>
    public string? ArmyId { get; init; }

    /// <summary>The fleet the dialog acts on (the Fleet menu's Supply fleet, T112), or <see langword="null"/>.</summary>
    public string? FleetId { get; init; }

    private bool IsFleet => FleetId is not null;

    /// <summary>The buying army's or fleet's id.</summary>
    private string SubjectId => FleetId ?? ArmyId!;

    /// <summary>
    /// Submits one composed command line through the screen's own path and returns the session's output
    /// lines, so the dialog can show the reply and refresh its figures.
    /// </summary>
    public required Func<string, IReadOnlyList<string>> Submit { get; init; }

    /// <summary>Raised by Close (or Esc); <see cref="MainGameScreen"/> closes the overlay.</summary>
    public event Action? Closed;

    private SupplyDialogModel _model = null!;
    private ItemList _providerList = null!;
    private OptionButton _viaPicker = null!;
    private Label _figuresLabel = null!;
    private Label _providerKindLabel = null!;
    private Label _stagedLabel = null!;
    private Label _moneyLabel = null!;
    private Label _replyLabel = null!;
    private readonly List<Button> _paidOnlyButtons = new();

    /// <summary>The live model, exposed so a headless check can read the staged amount and the rows.</summary>
    public SupplyDialogModel ModelForCheck => _model;

    /// <summary>The session's own reply line for the last command submitted from this dialog.</summary>
    public string ReplyForCheck => _replyLabel.Text;

    public override void _Ready()
    {
        _model = IsFleet
            ? SupplyDialogModel.ForFleet(Session.State, Session.State.FleetById(FleetId!)!, Session.Ruleset)
            : SupplyDialogModel.ForArmy(Session.State, Session.State.ArmyById(ArmyId!)!, Session.Ruleset);

        var backdrop = new ColorRect { Color = AppTheme.Scrim, MouseFilter = MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.MakePanel(UiKit.PanelColor);
        panel.CustomMinimumSize = new Vector2(460, 0);
        center.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        panel.AddChild(column);

        column.AddChild(UiKit.MakeLabel(
            $"{(IsFleet ? "Supply fleet" : "Supply army")} — {SubjectId}", AppTheme.Header, UiKit.AccentColor));

        _figuresLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.TextColor);
        _figuresLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_figuresLabel);

        column.AddChild(UiKit.MakeLabel("Providers", AppTheme.Body, UiKit.TextColor));
        _providerList = new ItemList { CustomMinimumSize = new Vector2(0, 120) };
        _providerList.ItemSelected += index =>
        {
            if (index < 0 || index >= _model.Providers.Count)
            {
                return;
            }

            _model.SelectProvider((int)index);
            UpdateProviderDetails();
            RefreshMoneyControls();
        };
        column.AddChild(_providerList);

        _providerKindLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.MutedTextColor);
        column.AddChild(_providerKindLabel);

        var supplyButtons = new HBoxContainer();
        supplyButtons.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        column.AddChild(supplyButtons);
        supplyButtons.AddChild(UiKit.MakeButton("+10 t", () => PressSupply(SupplyDialogModel.SupplyStepTons)));
        supplyButtons.AddChild(UiKit.MakeButton("+100 t", () => PressSupply(SupplyDialogModel.SupplyLargeStepTons)));
        _paidOnlyButtons.Add(AddPaidOnly(supplyButtons, "-10 t", () => PressSupply(-SupplyDialogModel.SupplyStepTons)));
        _paidOnlyButtons.Add(AddPaidOnly(supplyButtons, "-100 t", () => PressSupply(-SupplyDialogModel.SupplyLargeStepTons)));
        _paidOnlyButtons.Add(AddPaidOnly(supplyButtons, "Transfer", () => SubmitIfAny(_model.TransferStaged())));

        _stagedLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.TextColor);
        column.AddChild(_stagedLabel);

        column.AddChild(new HSeparator());
        column.AddChild(UiKit.MakeLabel("Money", AppTheme.Body, UiKit.TextColor));
        _moneyLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.TextColor);
        column.AddChild(_moneyLabel);

        _viaPicker = new OptionButton { CustomMinimumSize = new Vector2(220, 0) };
        _viaPicker.TooltipText = "Money source for +; destination for −. Selecting an army provider selects its purse. Treasury and fleet sources can also be chosen here.";
        column.AddChild(UiKit.MakeLabel("Money source (+) / destination (−)", AppTheme.Body, UiKit.TextColor));
        column.AddChild(_viaPicker);
        _viaPicker.ItemSelected += index =>
        {
            if (index < 0 || index >= _model.MoneyViaChoices.Count)
            {
                return;
            }

            _model.SelectMoneySource((int)index);
        };

        var moneyButtons = new HBoxContainer();
        moneyButtons.AddThemeConstantOverride("separation", AppTheme.DialogGap);
        column.AddChild(moneyButtons);
        moneyButtons.AddChild(UiKit.MakeButton("+100", () => PressMoney(100)));
        moneyButtons.AddChild(UiKit.MakeButton("+10", () => PressMoney(10)));
        moneyButtons.AddChild(UiKit.MakeButton("-10", () => PressMoney(-10)));
        moneyButtons.AddChild(UiKit.MakeButton("-100", () => PressMoney(-100)));

        column.AddChild(new HSeparator());
        _replyLabel = UiKit.MakeLabel(string.Empty, AppTheme.Body, UiKit.TextColor);
        _replyLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_replyLabel);

        var buttons = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Alignment = BoxContainer.AlignmentMode.End,
        };
        column.AddChild(buttons);
        buttons.AddChild(UiKit.MakeButton("Close", Close));

        UpdateFigures();
    }

    /// <summary>Selects a provider row by id — a check's way to drive the real list.</summary>
    public void SelectProviderForCheck(string providerId)
    {
        for (var i = 0; i < _model.Providers.Count; i++)
        {
            if (string.Equals(_model.Providers[i].Id, providerId, StringComparison.Ordinal))
            {
                _model.SelectProvider(i);
                _providerList.Select(i);
                UpdateFigures();
                return;
            }
        }
    }

    /// <summary>One press of a supply arrow, exactly as the button does it.</summary>
    public void PressSupplyForCheck(int stepTons) => PressSupply(stepTons);

    /// <summary>Stages an amount on the selected paid provider, exactly as the arrow buttons do.</summary>
    public void AdjustStagedForCheck(int deltaTons)
    {
        _model.AdjustStaged(deltaTons);
        UpdateFigures();
    }

    /// <summary>Transfer, exactly as the button does it.</summary>
    public void TransferForCheck() => SubmitIfAny(_model.TransferStaged());

    /// <summary>One press of a money arrow, exactly as the button does it.</summary>
    public void PressMoneyForCheck(int signedTalents, string? viaFleetId)
    {
        if (viaFleetId is not null)
        {
            _model.SelectVia(viaFleetId);
        }

        SubmitIfAny(_model.PressMoney(signedTalents, viaFleetId));
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            GetViewport().SetInputAsHandled();
            Close();
        }
    }

    /// <summary>Adds a button that is only meaningful on the paid (foreign-city) path.</summary>
    private static Button AddPaidOnly(Container parent, string text, Action onPressed)
    {
        var button = UiKit.MakeButton(text, onPressed);
        parent.AddChild(button);
        return button;
    }

    private void PressSupply(int stepTons)
    {
        var line = _model.PressSupply(stepTons);
        if (line is null)
        {
            // A paid provider staged the step: nothing is submitted, but the staged figure moved.
            UpdateFigures();
            return;
        }

        SubmitIfAny(line);
    }

    private void PressMoney(int signedTalents)
    {
        var line = _model.PressSelectedMoney(signedTalents);
        if (line is null)
            _replyLabel.Text = "No money moved: source empty or receiving purse full.";
        else SubmitIfAny(line);
    }

    private void SubmitIfAny(string? line)
    {
        if (line is null)
        {
            return;
        }

        var supplyBefore = _model.UnitSupplyTons;
        var moneyBefore = _model.UnitMoney;
        var source = line.StartsWith("army-transfer ", StringComparison.Ordinal)
            ? _model.SelectedViaArmyId : _model.SelectedProvider?.Id;
        var lines = Submit(line);

        // The session echoes the submitted line first ("> buy ..."); the reply is the first non-empty
        // line after the echo.
        _replyLabel.Text = lines.Skip(1).FirstOrDefault(text => text.Length > 0) ?? string.Empty;
        RefreshFigures();
        if (!lines.Any(text => text.Contains("rejected", StringComparison.OrdinalIgnoreCase))
            && !line.StartsWith("transfer-money ", StringComparison.Ordinal)
            && (_model.UnitSupplyTons != supplyBefore || _model.UnitMoney != moneyBefore))
        {
            var supplyDelta = _model.UnitSupplyTons - supplyBefore;
            var moneyDelta = _model.UnitMoney - moneyBefore;
            _replyLabel.Text = $"Supply {supplyDelta:+0;-0;0} t · Money {moneyDelta:+0;-0;0}";
            if (source is not null && Session.State.ArmyById(source) is { } other)
                _replyLabel.Text += $"\n{source}: {other.SupplyTons:N0} t · {other.Money:N0} money";
        }
    }

    /// <summary>Closes the dialog — the Close button, Esc, and a check's own close seam all call this.</summary>
    public void Close()
    {
        // Drop any staged amount: nothing pending when the dialog closes.
        _model.Close();
        Closed?.Invoke();
    }

    private void RefreshFigures()
    {
        if (IsFleet)
        {
            var fleet = Session.State.FleetById(FleetId!);
            if (fleet is null)
            {
                return;
            }

            _model.Refresh(Session.State, fleet);
            UpdateFigures();
            return;
        }

        var army = Session.State.ArmyById(ArmyId!);
        if (army is null)
        {
            return;
        }

        _model.Refresh(Session.State, army);
        UpdateFigures();
    }

    private void UpdateFigures()
    {
        _figuresLabel.Text =
            $"Supply {_model.UnitSupplyTons}t  ·  Room {_model.UnitRoomTons}t  ·  "
            + $"{_model.UnitMoney} money  ·  Treasury {_model.NationalTreasury}";

        _providerList.Clear();
        foreach (var provider in _model.Providers)
        {
            _providerList.AddItem(provider.Label);
        }

        if (_model.Providers.Count > 0)
        {
            _providerList.Select(_model.SelectedProviderIndex);
        }

        UpdateProviderDetails();

        RefreshMoneyControls();
    }

    private void RefreshMoneyControls()
    {
        _moneyLabel.Text =
            $"National balance {_model.NationalTreasury}  ·  {_model.UnitMoney} money";

        _viaPicker.Clear();
        foreach (var choice in _model.MoneyViaChoices)
        {
            _viaPicker.AddItem($"{choice.Label} — {choice.Balance} money");
        }

        var viaIndex = -1;
        for (var i = 0; i < _model.MoneyViaChoices.Count; i++)
        {
            if (string.Equals(_model.MoneyViaChoices[i].FleetId, _model.SelectedViaFleetId, StringComparison.Ordinal)
                && string.Equals(_model.MoneyViaChoices[i].ArmyId, _model.SelectedViaArmyId, StringComparison.Ordinal))
            {
                viaIndex = i;
            }
        }

        if (viaIndex < 0)
        {
            _viaPicker.AddItem($"Unavailable source: {_model.SelectedViaArmyId ?? _model.SelectedViaFleetId}");
            viaIndex = _viaPicker.ItemCount - 1;
        }
        _viaPicker.Select(viaIndex);
    }

    /// <summary>The provider-dependent labels and the paid-only buttons — no list rebuild, so it is safe to call from the list's own selection signal.</summary>
    private void UpdateProviderDetails()
    {
        var selected = _model.SelectedProvider;
        var paid = selected is { IsFree: false };
        foreach (var button in _paidOnlyButtons)
        {
            button.Disabled = !paid;
        }

        _providerKindLabel.Text = selected is null
            ? "No provider."
            : selected.IsFree
                ? "Free transfer"
                : $"Paid — {_model.StagedCostTalents} talents for {_model.StagedTons} tons.";

        _stagedLabel.Text = selected is { IsFree: false }
            ? $"Staged {_model.StagedTons} t  ·  Cost {_model.StagedCostTalents} talents"
            : string.Empty;
    }
}
