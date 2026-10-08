using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// The Fleet menu's <strong>Split fleet</strong> dialog — the original's <c>TFleetToFleet</c> form
/// titled <em>"Split fleet"</em>: a two-column table of the first and the second fleet's ships, supply
/// and money, with 1s/10s arrows for ships and 10s/100s for supply and money, the down arrows moving to
/// the second fleet <c>[Wine candidate: 2026-10-02-fleet-orders-live.md; docs/tasks/T141.md]</c>. It
/// needs at least 20 ships and no army aboard.
/// </summary>
/// <remarks>
/// All rules live in the Godot-free <see cref="SplitFleetModel"/>; this control owns only widgets and the
/// submit callback. <c>OK</c> composes exactly one <c>split-fleet</c> (T141's form, with
/// <c>supply=</c> and <c>money=</c>) and the engine places the new fleet one tile away on water;
/// <c>Cancel</c> submits nothing.
/// </remarks>
public partial class SplitFleetDialog : Control
{
    /// <summary>The session the dialog reads and submits to.</summary>
    public required GameSession Session { get; init; }

    /// <summary>The fleet being split (A).</summary>
    public required string FleetId { get; init; }

    /// <summary>The new fleet's id (B), chosen by the screen.</summary>
    public required string NewFleetId { get; init; }

    /// <summary>Submits one composed line through the screen's path and returns the session's lines.</summary>
    public required Func<string, IReadOnlyList<string>> Submit { get; init; }

    /// <summary>Raised by OK and Cancel; <see cref="MainGameScreen"/> closes the overlay.</summary>
    public event Action? Closed;

    private SplitFleetModel _model = null!;
    private Label _refusalLabel = null!;
    private Label _figuresLabel = null!;
    private Label _replyLabel = null!;
    private Label _shipsLabel = null!;
    private Label _supplyLabel = null!;
    private Label _moneyLabel = null!;
    private Label _newFleetLabel = null!;
    private Button _okButton = null!;

    /// <summary>The live model, exposed so a headless check can read the staging.</summary>
    public SplitFleetModel ModelForCheck => _model;

    /// <summary>The session's own reply line for the last command submitted from this dialog.</summary>
    public string ReplyForCheck => _replyLabel.Text;

    public override void _Ready()
    {
        var fleet = Session.State.FleetById(FleetId);
        if (fleet is null)
        {
            Closed?.Invoke();
            return;
        }

        _model = SplitFleetModel.ForFleet(Session.State, fleet, Session.Ruleset, NewFleetId);
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

        column.AddChild(UiKit.MakeLabel("Split fleet", 18, UiKit.AccentColor));
        _refusalLabel = UiKit.MakeLabel(string.Empty, 14, UiKit.TextColor);
        _refusalLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_refusalLabel);

        _figuresLabel = UiKit.MakeLabel(string.Empty, 14, UiKit.TextColor);
        _figuresLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_figuresLabel);

        column.AddChild(new HSeparator());
        column.AddChild(BuildStepper("Ships", SplitFleetModelSteps.ShipStep, SplitFleetModelSteps.ShipLargeStep, (int step) => _model.AdjustShips(step)));
        _shipsLabel = UiKit.MakeLabel(string.Empty, 13, UiKit.MutedTextColor);
        column.AddChild(_shipsLabel);

        column.AddChild(BuildStepper("Supply", SplitFleetModelSteps.SupplyStep, SplitFleetModelSteps.SupplyLargeStep, (int step) => _model.AdjustSupply(step)));
        _supplyLabel = UiKit.MakeLabel(string.Empty, 13, UiKit.MutedTextColor);
        column.AddChild(_supplyLabel);

        column.AddChild(BuildStepper("Money", SplitFleetModelSteps.MoneyStep, SplitFleetModelSteps.MoneyLargeStep, (int step) => _model.AdjustMoney(step)));
        _moneyLabel = UiKit.MakeLabel(string.Empty, 13, UiKit.MutedTextColor);
        column.AddChild(_moneyLabel);

        _newFleetLabel = UiKit.MakeLabel(string.Empty, 13, UiKit.TextColor);
        _newFleetLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_newFleetLabel);

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

    private Control BuildStepper(string caption, int step, int largeStep, Action<int> adjust)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        row.AddChild(UiKit.MakeLabel(caption, 14, UiKit.TextColor));
        row.AddChild(UiKit.MakeButton($"−{largeStep}", () =>
        {
            adjust(-largeStep);
            Refresh();
        }));
        row.AddChild(UiKit.MakeButton($"−{step}", () =>
        {
            adjust(-step);
            Refresh();
        }));
        row.AddChild(UiKit.MakeButton($"+{step}", () =>
        {
            adjust(step);
            Refresh();
        }));
        row.AddChild(UiKit.MakeButton($"+{largeStep}", () =>
        {
            adjust(largeStep);
            Refresh();
        }));
        return row;
    }

    /// <summary>The dialog's OK, exactly as its button does.</summary>
    public void OkForCheck() => Ok();

    /// <summary>The dialog's Cancel, exactly as its button does.</summary>
    public void CancelForCheck() => Cancel();

    /// <summary>One press on each of the ship arrows.</summary>
    public void AdjustShipsForCheck(int delta)
    {
        _model.AdjustShips(delta);
        Refresh();
    }

    /// <summary>One press on each of the supply arrows.</summary>
    public void AdjustSupplyForCheck(int delta)
    {
        _model.AdjustSupply(delta);
        Refresh();
    }

    /// <summary>One press on each of the money arrows.</summary>
    public void AdjustMoneyForCheck(int delta)
    {
        _model.AdjustMoney(delta);
        Refresh();
    }

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

        var fleet = Session.State.FleetById(FleetId);
        _refusalLabel.Text = _model.RefusalMessage ?? string.Empty;
        _okButton.Disabled = !_model.CanSplit || _model.Ships < 1;

        _figuresLabel.Text = fleet is null
            ? string.Empty
            : $"{FleetId}: {fleet.Ships} ships · {fleet.SupplyTons} t · {fleet.Money} talents";
        _shipsLabel.Text = $"Ships: {_model.Ships} to the new fleet  ·  max {_model.MaxShips}";
        _supplyLabel.Text = $"Supply: {_model.Supply} t to the new fleet  ·  max {_model.MaxSupply}";
        _moneyLabel.Text = $"Money: {_model.Money} to the new fleet  ·  max {_model.MaxMoney}";
        _newFleetLabel.Text = $"The engine places {NewFleetId} on a free water tile one tile away.";
    }
}

/// <summary>The stepper steps the split dialog shows — the confirmed original arrows, as named
/// constants rather than scattered literals.</summary>
internal static class SplitFleetModelSteps
{
    public const int ShipStep = FleetCityDialogModels.ShipStep;
    public const int ShipLargeStep = FleetCityDialogModels.ShipLargeStep;
    public const int SupplyStep = FleetCityDialogModels.SupplyStepTons;
    public const int SupplyLargeStep = FleetCityDialogModels.SupplyLargeStepTons;
    public const int MoneyStep = FleetCityDialogModels.MoneyStepTalents;
    public const int MoneyLargeStep = FleetCityDialogModels.MoneyLargeStepTalents;
}
