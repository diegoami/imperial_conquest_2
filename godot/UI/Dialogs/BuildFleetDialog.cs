using Godot;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// The Strategy menu's <strong>Build fleet</strong> dialog — the original's <c>TBuildFleet</c>
/// <strong>[confirmed: <c>docs/investigations/original-ui-command-audit.md</c> §1.3; decompiled
/// <c>TPremierForm_BuildNewFleet</c>]</strong>. Titled "Build fleet", it lists the nation's fleets
/// already under construction, the order form when a free coastal city exists, the three
/// pre-open refusals, and submits <c>order-fleet</c> on OK.
/// </summary>
/// <remarks>
/// <para>
/// <strong>All rules live in the Godot-free <see cref="BuildFleetDialogModel"/>.</strong> This
/// control owns only widgets and the submit callback. The model reads the ship range from
/// <see cref="IC2.Engine.Model.NavalRules.OrderMinShips"/> /
/// <see cref="IC2.Engine.Model.NavalRules.OrderMaxShips"/>, the cost from
/// <see cref="IC2.Engine.Model.NavalRules.BuildCostPerShip"/>, the fleet port from
/// <see cref="IC2.Engine.Naval.CoastalCity.IsCoastal"/>, and the pre-open refusals in the
/// order the original's <c>TPremierForm_BuildNewFleet</c> lists them.
/// </para>
/// <para>
/// <strong>Original vs. clone: the dialog closes after OK.</strong> The original's
/// <c>TBuildFleet</c> OK says "The fleet will be built at &lt;C&gt;." and stays open so a second
/// OK orders a second fleet
/// <strong>[Wine candidate: <c>2026-10-02-fleet-orders-live.md</c>]</strong>. The clone closes
/// the dialog on OK — one order per OK
/// <strong>[designed, the user's decision of 2026-10-02]</strong>. A second fleet, when the
/// player wants one, is a second menu pick.
/// </para>
/// <para>
/// <strong>The fleet port is the engine's own pick.</strong> The dialog offers the free coastal
/// cities <see cref="IC2.Engine.Naval.Commands.OrderFleetCommandHandler"/> itself accepts
/// (the engine's own coastal test) and submits to the first one. The Hazards flag on
/// <em>whether <c>TBuildFleet</c> lets the player pick the city</em> is still
/// <strong>[open]</strong>; the engine and the dialog agree on one port for every nation in the
/// shipped world, and a code comment marks the question for review
/// (<c>docs/tasks/T109.md</c> Hazards).
/// </para>
/// </remarks>
public partial class BuildFleetDialog : Control
{
    /// <summary>The session the dialog reads and submits to.</summary>
    public required GameSession Session { get; init; }

    /// <summary>Submits one composed line through the screen's path and returns the session's lines.</summary>
    public required Func<string, IReadOnlyList<string>> Submit { get; init; }

    /// <summary>Raised by OK and Cancel; <see cref="MainGameScreen"/> closes the overlay.</summary>
    public event Action? Closed;

    private BuildFleetDialogModel _model = null!;
    private SpinBox _ships = null!;
    private Label _costLabel = null!;
    private Label _quarterlyCostLabel = null!;
    private Label _capacityLabel = null!;
    private VBoxContainer _contentColumn = null!;
    private Label _refusalLabel = null!;
    private Label _underConstructionHeader = null!;
    private VBoxContainer _underConstructionColumn = null!;
    private Button _okButton = null!;
    private Label _replyLabel = null!;
    private Label _fleetPortLabel = null!;

    /// <summary>The live model, exposed for the headless check.</summary>
    public BuildFleetDialogModel ModelForCheck => _model;

    /// <summary>The ship-count box's value, exposed for the headless check.</summary>
    public int ShipsForCheck => (int)_ships.Value;

    /// <summary>The session's own reply line for the last command submitted from this dialog.</summary>
    public string ReplyForCheck => _replyLabel.Text;

    public override void _Ready()
    {
        _model = BuildFleetDialogModel.ForActiveNation(Session.State, Session.Ruleset, Session.World);

        var backdrop = new ColorRect { Color = new Color(0f, 0f, 0f, 0.6f), MouseFilter = MouseFilterEnum.Stop };
        backdrop.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(backdrop);

        var center = new CenterContainer();
        center.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(center);

        var panel = UiKit.MakePanel(UiKit.PanelColor);
        panel.CustomMinimumSize = new Vector2(460, 0);
        center.AddChild(panel);

        var column = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        column.AddThemeConstantOverride("separation", 8);
        panel.AddChild(column);

        column.AddChild(UiKit.MakeLabel(StrategyDialogModels.BuildFleetTitle, 18, UiKit.AccentColor));

        _contentColumn = new VBoxContainer();
        _contentColumn.AddThemeConstantOverride("separation", 6);
        column.AddChild(_contentColumn);

        _refusalLabel = UiKit.MakeLabel(string.Empty, 14, UiKit.TextColor);
        _refusalLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _contentColumn.AddChild(_refusalLabel);

        _fleetPortLabel = UiKit.MakeLabel(string.Empty, 13, UiKit.MutedTextColor);
        _fleetPortLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _contentColumn.AddChild(_fleetPortLabel);

        _underConstructionHeader = UiKit.MakeLabel("Fleets under construction", 14, UiKit.MutedTextColor);
        _contentColumn.AddChild(_underConstructionHeader);

        _underConstructionColumn = new VBoxContainer();
        _underConstructionColumn.AddThemeConstantOverride("separation", 2);
        _contentColumn.AddChild(_underConstructionColumn);

        column.AddChild(new HSeparator());

        column.AddChild(UiKit.MakeLabel("Ships", 13, UiKit.MutedTextColor));
        _ships = new SpinBox
        {
            MinValue = _model.MinShips,
            MaxValue = _model.MaxShips,
            Step = 1,
            Value = _model.MinShips,
            CustomMinimumSize = new Vector2(140, 0),
        };
        _ships.ValueChanged += _ => Refresh();
        column.AddChild(_ships);

        _costLabel = UiKit.MakeLabel(string.Empty, 14, UiKit.TextColor);
        column.AddChild(_costLabel);
        _quarterlyCostLabel = UiKit.MakeLabel(string.Empty, 14, UiKit.TextColor);
        column.AddChild(_quarterlyCostLabel);
        _capacityLabel = UiKit.MakeLabel(string.Empty, 14, UiKit.TextColor);
        column.AddChild(_capacityLabel);

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

        var ships = (int)_ships.Value;
        var cost = _model.InitialCostFor(ships);
        var quarterly = ships * Session.Ruleset.Economy.ShipUpkeepPerQuarter;
        var capacity = ships * Session.Ruleset.Naval.TransportTroopsPerShip;

        _costLabel.Text = $"Cost: {cost.ToString(System.Globalization.CultureInfo.InvariantCulture)} talents";
        _quarterlyCostLabel.Text =
            $"Quarterly upkeep: {quarterly.ToString(System.Globalization.CultureInfo.InvariantCulture)} talents";
        _capacityLabel.Text =
            $"Troop capacity: {capacity.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

        _refusalLabel.Text = _model.PreOpenRefusalMessage ?? string.Empty;

        if (_model.FreeCoastalCities.Count > 0)
        {
            _fleetPortLabel.Text = $"Fleet port: {_model.FreeCoastalCities[0].Name}";
        }
        else
        {
            _fleetPortLabel.Text = string.Empty;
        }

        // Under construction: clear and refill.
        foreach (var child in _underConstructionColumn.GetChildren())
        {
            _underConstructionColumn.RemoveChild(child);
            child.QueueFree();
        }

        var pending = _model.UnderConstructionFleets;
        if (pending.Count == 0)
        {
            _underConstructionHeader.Visible = false;
        }
        else
        {
            _underConstructionHeader.Visible = true;
            foreach (var fleet in pending)
            {
                _underConstructionColumn.AddChild(UiKit.MakeLabel(
                    $"A fleet of {fleet.Ships} ships will be ready in {fleet.WeeksRemaining} weeks at {fleet.CityName}.",
                    13,
                    UiKit.TextColor));
            }
        }

        _okButton.Disabled = _model.PreOpenRefusalMessage is not null;
    }

    /// <summary>Sets the ship-count box, exactly as a click on its arrows does.</summary>
    public void SetShipsForCheck(int ships)
    {
        _ships.Value = Math.Clamp(ships, _model.MinShips, _model.MaxShips);
        Refresh();
    }

    /// <summary>Submits the dialog's <c>order-fleet</c> line, exactly as the OK button does.</summary>
    public void OkForCheck() => Ok();

    /// <summary>Closes the dialog without submitting, exactly as Cancel does.</summary>
    public void CancelForCheck() => Cancel();

    private void Ok()
    {
        if (_model.PreOpenRefusalMessage is not null)
        {
            return;
        }

        if (_model.FreeCoastalCities.Count == 0)
        {
            return;
        }

        var ships = (int)_ships.Value;
        var city = _model.FreeCoastalCities[0];
        var newFleetId = $"t109-fleet-{System.Guid.NewGuid():N}";
        var line = _model.OrderFleetLine(ships, city.Id, newFleetId);
        var lines = Submit(line);
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
}
