using Godot;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// The City menu's <strong>Fortify city</strong> dialog — the original's <c>TFortifyCity</c>: 0 to
/// <c>(100 − current)</c> points at <c>population(k) × points</c>, refused under siege or while an order
/// is already pending <c>[confirmed: decompiled-unit-map-orders-and-record-fields.md]</c>.
/// </summary>
/// <remarks>
/// All rules live in the Godot-free <see cref="FortifyCityModel"/>; this control owns only widgets and
/// the submit callback. <c>OK</c> submits one <c>order-city … fortify</c>. The dialog adds no
/// affordability check of its own: bug #549 (T115) makes the treasury rule a preset difference, and
/// whatever the engine returns is what the player sees.
/// </remarks>
public partial class FortifyDialog : Control
{
    /// <summary>The session the dialog reads and submits to.</summary>
    public required GameSession Session { get; init; }

    /// <summary>The city being fortified.</summary>
    public required string CityId { get; init; }

    /// <summary>Submits one composed line through the screen's path and returns the session's lines.</summary>
    public required Func<string, IReadOnlyList<string>> Submit { get; init; }

    /// <summary>Raised by OK and Cancel; <see cref="MainGameScreen"/> closes the overlay.</summary>
    public event Action? Closed;

    private FortifyCityModel _model = null!;
    private SpinBox _points = null!;
    private Label _refusalLabel = null!;
    private Label _stateLabel = null!;
    private Label _costLabel = null!;
    private Label _replyLabel = null!;
    private Button _okButton = null!;

    /// <summary>The live model, exposed so a headless check can read the range and the cost.</summary>
    public FortifyCityModel ModelForCheck => _model;

    /// <summary>The session's own reply line for the last command submitted from this dialog.</summary>
    public string ReplyForCheck => _replyLabel.Text;

    public override void _Ready()
    {
        var city = Session.State.CityById(CityId);
        if (city is null)
        {
            Closed?.Invoke();
            return;
        }

        _model = FortifyCityModel.ForCity(Session.State, city, Session.Ruleset);
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
        panel.CustomMinimumSize = new Vector2(440, 0);
        center.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        panel.AddChild(column);

        column.AddChild(UiKit.MakeLabel($"Fortify city — {CityId}", 18, UiKit.AccentColor));
        _refusalLabel = UiKit.MakeLabel(string.Empty, 14, UiKit.TextColor);
        _refusalLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_refusalLabel);

        _stateLabel = UiKit.MakeLabel(string.Empty, 14, UiKit.TextColor);
        _stateLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        column.AddChild(_stateLabel);

        column.AddChild(UiKit.MakeLabel("Points", 13, UiKit.MutedTextColor));
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        column.AddChild(row);
        row.AddChild(UiKit.MakeButton("−10", () => Adjust(-10)));
        row.AddChild(UiKit.MakeButton("−1", () => Adjust(-1)));
        _points = new SpinBox
        {
            MinValue = 0,
            MaxValue = 0,
            Step = 1,
            CustomMinimumSize = new Vector2(110, 0),
        };
        _points.ValueChanged += _ => Refresh();
        row.AddChild(_points);
        row.AddChild(UiKit.MakeButton("+1", () => Adjust(1)));
        row.AddChild(UiKit.MakeButton("+10", () => Adjust(10)));

        _costLabel = UiKit.MakeLabel(string.Empty, 14, UiKit.TextColor);
        column.AddChild(_costLabel);

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

    private void Adjust(int delta) =>
        _points.Value = Math.Clamp((int)_points.Value + delta, _model.MinPoints, _model.MaxPoints);

    /// <summary>Sets the points box, exactly as the arrows do.</summary>
    public void SetPointsForCheck(int points)
    {
        _points.Value = Math.Clamp(points, _model.MinPoints, _model.MaxPoints);
        Refresh();
    }

    /// <summary>The dialog's OK, exactly as its button does.</summary>
    public void OkForCheck() => Ok();

    /// <summary>The dialog's Cancel, exactly as its button does.</summary>
    public void CancelForCheck() => Cancel();

    private void Ok()
    {
        var line = _model.ComposeOk((int)_points.Value);
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

        _refusalLabel.Text = _model.RefusalMessage ?? string.Empty;
        _points.MinValue = _model.MinPoints;
        _points.MaxValue = _model.MaxPoints;
        var points = (int)_points.Value;
        _stateLabel.Text =
            $"Fortification {_model.CurrentPercent}% → {_model.CurrentPercent + Math.Min(points, _model.MaxPoints)}%"
            + $"  ·  max {_model.MaxPercent}%";
        _costLabel.Text = $"Cost: {_model.CostFor(points)} talents  ·  points {points} / {_model.MaxPoints}";
        _okButton.Disabled = !_model.CanFortify || points <= 0;
    }
}
