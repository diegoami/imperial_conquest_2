using Godot;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// The main game screen — <c>docs/game-design.md</c> §"User interface" item 2, "the dominant,
/// near-always-visible view": a top bar (calendar, active seat, End Turn), the map
/// (<see cref="GameMapView"/>), the persistent contextual side panel (<see cref="ContextPanel"/>), the
/// bottom filter toolbar, and the non-modal news log (<see cref="NewsLogPanel"/>).
/// </summary>
public partial class MainGameScreen : Control
{
    public required GameSession Session { get; init; }

    public required string RepositoryRoot { get; init; }

    private Label _calendarLabel = null!;
    private Label _activeNationLabel = null!;
    private GameMapView _mapView = null!;
    private ContextPanel _contextPanel = null!;
    private NewsLogPanel _newsLog = null!;
    private Label _lastCommandLabel = null!;

    public override void _Ready()
    {
        UiKit.ApplyBackground(this, UiKit.Background);

        var root = new VBoxContainer();
        root.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(root);

        root.AddChild(BuildTopBar());

        var body = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.SetSplitOffset(-320);
        root.AddChild(body);

        _mapView = new GameMapView { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddChild(_mapView);

        _contextPanel = new ContextPanel
        {
            Session = Session,
            MapView = _mapView,
            CustomMinimumSize = new Vector2(300, 0),
        };
        body.AddChild(_contextPanel);

        root.AddChild(BuildBottomToolbar());

        _lastCommandLabel = UiKit.MakeLabel(string.Empty, 12, UiKit.MutedTextColor);
        root.AddChild(_lastCommandLabel);

        _newsLog = new NewsLogPanel { Session = Session, CustomMinimumSize = new Vector2(0, 200) };
        root.AddChild(_newsLog);

        _mapView.CitySelected += id => _contextPanel.ShowCity(id);
        _mapView.ArmySelected += id => _contextPanel.ShowArmy(id);
        _mapView.FleetSelected += id => _contextPanel.ShowFleet(id);
        _mapView.SelectionCleared += () => _contextPanel.ShowNationOverview();
        _mapView.CommandIssued += OnCommandIssued;
        _contextPanel.CommandIssued += OnCommandIssued;

        _mapView.Attach(Session, RepositoryRoot);
        RefreshTopBar();
    }

    private Control BuildTopBar()
    {
        var bar = UiKit.MakePanel(UiKit.PanelColorRaised, cornerRadius: 0);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 24);
        bar.AddChild(row);

        _calendarLabel = UiKit.MakeLabel(string.Empty, 16, UiKit.TextColor);
        row.AddChild(_calendarLabel);

        _activeNationLabel = UiKit.MakeLabel(string.Empty, 16, UiKit.AccentColor);
        row.AddChild(_activeNationLabel);

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(spacer);

        row.AddChild(UiKit.MakeButton("End Turn", OnEndTurnPressed, 16));

        return bar;
    }

    private Control BuildBottomToolbar()
    {
        var bar = UiKit.MakePanel(UiKit.PanelColorRaised, cornerRadius: 0);
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 10);
        bar.AddChild(row);

        row.AddChild(MakeFilterToggle("Cities", true, value => _mapView.ShowCities = value));
        row.AddChild(MakeFilterToggle("Armies", true, value => _mapView.ShowArmies = value));
        row.AddChild(MakeFilterToggle("Fleets", true, value => _mapView.ShowFleets = value));

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        row.AddChild(spacer);

        row.AddChild(UiKit.MakeButton("News", () => _newsLog.Toggle(), 14));

        return row;
    }

    private Control MakeFilterToggle(string label, bool initial, Action<bool> onToggled)
    {
        var button = new CheckButton { Text = label, ButtonPressed = initial };
        button.Toggled += onToggled.Invoke;
        return button;
    }

    private void OnEndTurnPressed() => OnCommandIssued(Session.Submit("end").Lines);

    private void OnCommandIssued(IReadOnlyList<string> lines)
    {
        _lastCommandLabel.Text = lines.Count == 0 ? string.Empty : lines[^1];
        _mapView.Refresh();
        _contextPanel.Refresh();
        RefreshTopBar();
        if (_newsLog.Visible)
        {
            _newsLog.Refresh();
        }
    }

    private void RefreshTopBar()
    {
        var calendar = Session.State.Calendar;
        _calendarLabel.Text = $"Week {calendar.Week}, {calendar.YearBc} BC (turn {calendar.TurnIndex})";
        var nation = Session.State.NationById(Session.State.ActiveNationId);
        _activeNationLabel.Text = $"Playing: {nation?.Name ?? Session.State.ActiveNationId}";
    }
}
