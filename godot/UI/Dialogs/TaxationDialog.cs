using Godot;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// The Strategy menu's <strong>Taxation</strong> dialog — the original's <c>TChangeTax</c>
/// <strong>[confirmed: <c>docs/investigations/original-ui-command-audit.md</c> §1.3; decompiled
/// <c>TChangeTax_PrintNewNumbers</c> <c>0x0044C2E8</c>]</strong>. Titled "Change tax level", it shows the
/// nation's current and the slider's new rate alongside their tax-base income, OK commits
/// <c>set-tax</c>, Cancel submits nothing.
/// </summary>
/// <remarks>
/// <para>
/// All rules live in the Godot-free <see cref="TaxationDialogModel"/>; this control owns only
/// widgets and the page-key handler. The slider's minimum and maximum are the ruleset's
/// <see cref="IC2.Engine.Model.EconomyRules.TaxRateMinPercent"/> and
/// <see cref="IC2.Engine.Model.EconomyRules.TaxRateMaxPercent"/>, the same bounds the engine's own
/// <c>set-tax</c> handler range-checks against, so the slider can never move past a value the engine
/// will accept.
/// </para>
/// <para>
/// <strong>Page keys move by 5.</strong> The original's <c>TChangeTax</c> slider reports page size 5
/// <strong>[Wine candidate: <c>2026-10-02-unit-map-mouse-orders-and-tax-range.md</c>, (g)]</strong>;
/// the arrow keys move by 1 (Godot's own <see cref="Range.Step"/>). The handler clamps to the
/// ruleset's inclusive bounds on every change, so a page past the maximum stops at the maximum and a
/// page below the minimum stops at the minimum (Done-when 3).
/// </para>
/// <para>
/// <strong>The preview is the engine's own <c>TaxIncome</c>.</strong> The dialog renders
/// <c>taxBase × rate / 100</c> by calling <see cref="TaxationDialogModel.IncomeFor"/> for the new
/// rate and <see cref="TaxationDialogModel.CurrentIncome"/> for the current one; the ruleset's
/// <c>TaxRateDivisor</c> (100 in the shipped ruleset) and the nation's tax base are the only
/// inputs, and the engine's own
/// <see cref="IC2.Engine.Economy.TaxIncome.Compute"/> reads them the same way. A ruleset change
/// moves the dialog and the engine together.
/// </para>
/// </remarks>
public partial class TaxationDialog : Control
{
    /// <summary>The session the dialog reads and submits to.</summary>
    public required GameSession Session { get; init; }

    /// <summary>Submits one composed line through the screen's path and returns the session's lines.</summary>
    public required Func<string, IReadOnlyList<string>> Submit { get; init; }

    /// <summary>Raised by OK and Cancel; <see cref="MainGameScreen"/> closes the overlay.</summary>
    public event Action? Closed;

    private TaxationDialogModel _model = null!;
    private Godot.Range _slider = null!;
    private Label _currentRateLabel = null!;
    private Label _currentIncomeLabel = null!;
    private Label _newRateLabel = null!;
    private Label _newIncomeLabel = null!;
    private Label _replyLabel = null!;
    private Button _okButton = null!;

    /// <summary>The live model, exposed so a headless check can read the income, the bounds and the line.</summary>
    public TaxationDialogModel ModelForCheck => _model;

    /// <summary>The slider's current value, exactly as the rendered widgets see it — for the headless check.</summary>
    public int SliderValueForCheck => (int)_slider.Value;

    /// <summary>The slider's per-arrow step, as the real <see cref="Godot.Range"/> carries it — for the
    /// headless check, which asserts the shipped slider really steps by 1.</summary>
    public double SliderStepForCheck => _slider.Step;

    /// <summary>The session's own reply line for the last command submitted from this dialog.</summary>
    public string ReplyForCheck => _replyLabel.Text;

    public override void _Ready()
    {
        _model = TaxationDialogModel.ForActiveNation(Session.State, Session.Ruleset);

        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.6f), MouseFilter = MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.MakePanel(UiKit.PanelColor);
        panel.CustomMinimumSize = new Vector2(420, 0);
        center.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        panel.AddChild(column);

        column.AddChild(UiKit.MakeLabel(StrategyDialogModels.TaxationTitle, 18, UiKit.AccentColor));

        var currentBox = new VBoxContainer();
        currentBox.AddThemeConstantOverride("separation", 2);
        column.AddChild(currentBox);
        currentBox.AddChild(UiKit.MakeLabel("Current", 13, UiKit.MutedTextColor));
        _currentRateLabel = UiKit.MakeLabel(string.Empty, 14, UiKit.TextColor);
        currentBox.AddChild(_currentRateLabel);
        _currentIncomeLabel = UiKit.MakeLabel(string.Empty, 14, UiKit.TextColor);
        currentBox.AddChild(_currentIncomeLabel);

        column.AddChild(new HSeparator());

        var newBox = new VBoxContainer();
        newBox.AddThemeConstantOverride("separation", 2);
        column.AddChild(newBox);
        newBox.AddChild(UiKit.MakeLabel("New", 13, UiKit.MutedTextColor));
        _newRateLabel = UiKit.MakeLabel(string.Empty, 14, UiKit.TextColor);
        newBox.AddChild(_newRateLabel);
        _newIncomeLabel = UiKit.MakeLabel(string.Empty, 14, UiKit.TextColor);
        newBox.AddChild(_newIncomeLabel);

        // The slider: 1 per arrow (Godot's Step) and 5 per Page (handled by the key handler below,
        // not by the Range's Page — that snaps value to its multiples, which would break the brief's
        // "39 + 5 page = 40" rule). Min/max are the ruleset's inclusive bounds.
        _slider = new HSlider
        {
            MinValue = _model.MinimumRate,
            MaxValue = _model.MaximumRate,
            Step = StrategyDialogModels.TaxArrowStep,
            Value = _model.CurrentRate,
            CustomMinimumSize = new Vector2(0, 24),
        };
        _slider.ValueChanged += _ => Refresh();
        column.AddChild(_slider);

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

        Refresh();
    }

    private void Refresh()
    {
        if (_model is null)
        {
            return;
        }

        var currentRate = _model.CurrentRate;
        var newRate = (int)_slider.Value;

        _currentRateLabel.Text = $"Tax {currentRate}%";
        _currentIncomeLabel.Text = $"Income {_model.CurrentIncome.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        _newRateLabel.Text = $"Tax {newRate}%";
        _newIncomeLabel.Text = $"Income {_model.IncomeFor(newRate).ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        _okButton.Disabled = newRate == currentRate;
    }

    /// <summary>Sets the slider's value, exactly as a page-key press would. Exposed for the headless
    /// check, which exercises the same clamp the dialog's own handler applies.</summary>
    public void SetRateForCheck(int rate)
    {
        _slider.Value = _model.Clamp(rate);
    }

    /// <summary>Submits the dialog's <c>set-tax</c> line, exactly as the OK button does.</summary>
    public void OkForCheck() => Ok();

    /// <summary>Closes the dialog without submitting, exactly as Cancel does.</summary>
    public void CancelForCheck() => Cancel();

    private void Ok()
    {
        var rate = (int)_slider.Value;
        if (rate == _model.CurrentRate)
        {
            // No change: OK does nothing visible (Done-when 3's "OK sets Rome's rate" still
            // expects a no-op when the slider is where it started).
            Closed?.Invoke();
            return;
        }

        var lines = Submit(_model.SetTaxLine(rate));
        _replyLabel.Text = lines.Skip(1).FirstOrDefault(text => text.Length > 0) ?? string.Empty;
        Closed?.Invoke();
    }

    private void Cancel() => Closed?.Invoke();

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            Cancel();
            GetViewport().SetInputAsHandled();
        }
    }

    // Page Up / Page Down are taken in _Input, before the GUI: a focused control (the SpinBox's text
    // field, the slider) would otherwise consume them and the handler would never run (Sol's
    // re-check of PR 883, R1).
    public override void _Input(InputEvent @event)
    {
        // Page Up / Page Down: the original's page size is 5 (Wine candidate). We don't set the
        // Range's Page property because Godot would snap the value to its multiples — 39 + 5 = 44
        // would clamp to 40, but 38 + 5 = 43 would clamp to 40 too, contradicting the brief's "39
        // +5 page gives 40" rule. The Range's Step is the slider's snapping unit, the page key is
        // just a five-step nudge.
        if (@event is InputEventKey { Pressed: true, Keycode: Key.Pageup })
        {
            _slider.Value = _model.Clamp((int)_slider.Value + StrategyDialogModels.TaxPageStep);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (@event is InputEventKey { Pressed: true, Keycode: Key.Pagedown })
        {
            _slider.Value = _model.Clamp((int)_slider.Value - StrategyDialogModels.TaxPageStep);
            GetViewport().SetInputAsHandled();
        }
    }
}
