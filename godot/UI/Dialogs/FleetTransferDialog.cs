using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// The Fleet menu's <strong>Transfer ships</strong> dialog — the original's <c>TFleetToFleet</c> window
/// titled <em>"Fleet to fleet transfer"</em>, the naval twin of <c>TArmyToArmy</c>: ships, supply and
/// money, either direction, between the selected fleet and the game's own adjacent partner
/// <c>[confirmed: decompiled-unit-map-orders-and-record-fields.md; Wine candidate:
/// 2026-10-02-fleet-orders-live.md]</c>.
/// </summary>
/// <remarks>
/// All rules live in the Godot-free <see cref="FleetTransferModel"/>; this control owns only widgets and
/// the submit callback. <c>OK</c> composes one <c>fleet-transfer</c> per direction staged (two when the
/// player staged both ways) and <c>Cancel</c> submits nothing. With no own fleet at distance one, the
/// screen opens no dialog at all and shows the no-partner message.
/// </remarks>
public partial class FleetTransferDialog : Control
{
    /// <summary>The session the dialog reads and submits to.</summary>
    public required GameSession Session { get; init; }

    /// <summary>A, the selected fleet.</summary>
    public required string FleetId { get; init; }

    /// <summary>B, the game-picked partner.</summary>
    public required string PartnerId { get; init; }

    /// <summary>Submits one composed line through the screen's path and returns the session's lines.</summary>
    public required Func<string, IReadOnlyList<string>> Submit { get; init; }

    /// <summary>Raised by OK and Cancel; <see cref="MainGameScreen"/> closes the overlay.</summary>
    public event Action? Closed;

    private FleetTransferModel _model = null!;
    private Label _figuresLabel = null!;
    private Label _replyLabel = null!;
    private Label _shipsLabel = null!;
    private Label _supplyLabel = null!;
    private Label _moneyLabel = null!;

    /// <summary>The live model, exposed so a headless check can read the staging.</summary>
    public FleetTransferModel ModelForCheck => _model;

    /// <summary>The session's own reply line for the last command submitted from this dialog.</summary>
    public string ReplyForCheck => _replyLabel.Text;

    public override void _Ready()
    {
        var selected = Session.State.FleetById(FleetId);
        var partner = Session.State.FleetById(PartnerId);
        if (selected is null || partner is null)
        {
            Closed?.Invoke();
            return;
        }

        _model = FleetTransferModel.ForFleets(selected, partner, Session.Ruleset);
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
        panel.CustomMinimumSize = new Vector2(600, 0);
        center.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        panel.AddChild(column);

        column.AddChild(UiKit.MakeLabel("Fleet to fleet transfer", 18, UiKit.AccentColor));

        _figuresLabel = UiKit.MakeLabel(string.Empty, 14, UiKit.TextColor);
        _figuresLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_figuresLabel);

        var legend = UiKit.MakeLabel(
            "Positive moves the selected fleet's ships, supply and money to the partner; negative moves them back.",
            12, UiKit.MutedTextColor);
        legend.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(legend);

        column.AddChild(new HSeparator());
        column.AddChild(BuildStepper("Ships", 1, 10, delta =>
        {
            _model.AdjustShips(delta);
            Refresh();
        }));
        _shipsLabel = UiKit.MakeLabel(string.Empty, 13, UiKit.MutedTextColor);
        column.AddChild(_shipsLabel);

        column.AddChild(BuildStepper("Supply", FleetCityDialogModels.SupplyStepTons, FleetCityDialogModels.SupplyLargeStepTons, delta =>
        {
            _model.AdjustSupply(delta);
            Refresh();
        }));
        _supplyLabel = UiKit.MakeLabel(string.Empty, 13, UiKit.MutedTextColor);
        column.AddChild(_supplyLabel);

        column.AddChild(BuildStepper("Money", FleetCityDialogModels.MoneyStepTalents, FleetCityDialogModels.MoneyLargeStepTalents, delta =>
        {
            _model.AdjustMoney(delta);
            Refresh();
        }));
        _moneyLabel = UiKit.MakeLabel(string.Empty, 13, UiKit.MutedTextColor);
        column.AddChild(_moneyLabel);

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

    private Control BuildStepper(string caption, int step, int largeStep, Action<int> adjust)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(UiKit.MakeLabel(caption, 14, UiKit.TextColor));
        row.AddChild(UiKit.MakeButton($"−{largeStep}", () => adjust(-largeStep)));
        row.AddChild(UiKit.MakeButton($"−{step}", () => adjust(-step)));
        row.AddChild(UiKit.MakeButton($"+{step}", () => adjust(step)));
        row.AddChild(UiKit.MakeButton($"+{largeStep}", () => adjust(largeStep)));
        return row;
    }

    /// <summary>The dialog's OK, exactly as its button does.</summary>
    public void OkForCheck() => Ok();

    /// <summary>The dialog's Cancel, exactly as its button does.</summary>
    public void CancelForCheck() => Cancel();

    /// <summary>One press on the ship arrows.</summary>
    public void AdjustShipsForCheck(int delta)
    {
        _model.AdjustShips(delta);
        Refresh();
    }

    /// <summary>One press on the supply arrows.</summary>
    public void AdjustSupplyForCheck(int delta)
    {
        _model.AdjustSupply(delta);
        Refresh();
    }

    /// <summary>One press on the money arrows.</summary>
    public void AdjustMoneyForCheck(int delta)
    {
        _model.AdjustMoney(delta);
        Refresh();
    }

    private void Ok()
    {
        var lines = _model.ComposeOk();
        if (lines.Count == 0)
        {
            return;
        }

        foreach (var line in lines)
        {
            var output = Submit(line);
            _replyLabel.Text = output.Skip(1).FirstOrDefault(text => text.Length > 0) ?? string.Empty;
        }

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

        var selected = Session.State.FleetById(FleetId);
        var partner = Session.State.FleetById(PartnerId);
        _figuresLabel.Text = selected is null || partner is null
            ? string.Empty
            : $"{FleetId}: {selected.Ships} ships · {selected.SupplyTons} t · {selected.Money} talents"
              + $"   |   {PartnerId}: {partner.Ships} ships · {partner.SupplyTons} t · {partner.Money} talents";

        _shipsLabel.Text = Net("Ships", _model.ShipsNet, _model.MaxShipsToPartner, _model.MaxShipsBack);
        _supplyLabel.Text = Net("Supply", _model.SupplyNet, _model.MaxSupplyToPartner, _model.MaxSupplyBack);
        _moneyLabel.Text = Net("Money", _model.MoneyNet, _model.MaxMoneyToPartner, _model.MaxMoneyBack);
    }

    private static string Net(string resource, int net, int maxToPartner, int maxBack) => net switch
    {
        > 0 => $"{resource}: +{net} (A → B)  ·  max +{maxToPartner}",
        < 0 => $"{resource}: {net} (B → A)  ·  max -{maxBack}",
        _ => $"{resource}: 0  ·  max +{maxToPartner} / -{maxBack}",
    };
}
