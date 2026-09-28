using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;

namespace IC2.Slice.UI;

/// <summary>
/// The main game screen's persistent contextual side panel — <c>docs/game-design.md</c> §"User
/// interface" item 2: "swaps content by selection — a city ..., an army/fleet ..., or a nation
/// overview." Never a popup: <see cref="Rebuild"/> replaces this panel's own children in place.
/// </summary>
/// <remarks>
/// <strong>Two known engine gaps, reported rather than worked around</strong> (<c>docs/tasks/T24.md</c>:
/// "If a screen needs an engine or view-model capability that GameSession doesn't expose, do NOT change
/// src/. STOP and report what's missing."):
/// <list type="bullet">
/// <item><description><strong>Tax rate is display-only.</strong> <see cref="NationState.TaxRatePercent"/>
/// is read all over the economy systems but nothing in <c>src/IC2.Engine/Economy/Commands</c> or
/// <c>src/IC2.Engine/Cities/Orders</c> ever writes it — there is no command that sets a nation's tax
/// rate. This panel shows the value; it has no control to change it.</description></item>
/// <item><description><strong>No dedicated army-to-army transfer.</strong> Fleets have
/// <c>FleetToFleetTransferCommand</c>; armies have no equivalent, so this panel's own army "transfer"
/// action is the same supply purchase (<c>economy.buy-supply</c>) the city panel's own troop/money
/// slider already uses, not a fabricated new command.</description></item>
/// </list>
/// </remarks>
public partial class ContextPanel : Control
{
    private static readonly (string Id, string Label)[] UnitTypes =
    {
        ("light_infantry", "Light Infantry"),
        ("heavy_infantry", "Heavy Infantry"),
        ("archers", "Archers"),
        ("light_cavalry", "Light Cavalry"),
        ("heavy_cavalry", "Heavy Cavalry"),
    };

    public required GameSession Session { get; init; }

    public required GameMapView MapView { get; init; }

    /// <summary>Raised after this panel issues a command, so <see cref="MainGameScreen"/> can append the
    /// output to the news log and refresh the top bar/map.</summary>
    public event Action<IReadOnlyList<string>>? CommandIssued;

    private VBoxContainer _content = null!;
    private Selection _selection = Selection.None();

    public override void _Ready()
    {
        var panel = UiKit.MakePanel(UiKit.PanelColor);
        panel.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(panel);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.AddChild(scroll);

        _content = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _content.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(_content);

        ShowNationOverview();
    }

    public void ShowNationOverview()
    {
        _selection = Selection.None();
        Rebuild();
    }

    public void ShowCity(string cityId)
    {
        _selection = Selection.ForCity(cityId);
        Rebuild();
    }

    public void ShowArmy(string armyId)
    {
        _selection = Selection.ForArmy(armyId);
        Rebuild();
    }

    public void ShowFleet(string fleetId)
    {
        _selection = Selection.ForFleet(fleetId);
        Rebuild();
    }

    /// <summary>Re-reads the current selection's own state and rebuilds this panel's controls — call after
    /// any command that might have changed what is selected (a city captured, an army disbanded, ...).</summary>
    public void Refresh() => Rebuild();

    private void Rebuild()
    {
        foreach (var child in _content.GetChildren())
        {
            _content.RemoveChild(child);
            child.QueueFree();
        }

        switch (_selection.Kind)
        {
            case SelectionKind.City when Session.State.CityById(_selection.Id!) is { } city:
                BuildCityPanel(city);
                break;
            case SelectionKind.Army when Session.State.ArmyById(_selection.Id!) is { } army:
                BuildArmyPanel(army);
                break;
            case SelectionKind.Fleet when Session.State.FleetById(_selection.Id!) is { } fleet:
                BuildFleetPanel(fleet);
                break;
            default:
                BuildNationOverview();
                break;
        }
    }

    private void Heading(string text) => _content.AddChild(UiKit.MakeLabel(text, 20, UiKit.AccentColor));

    private void Fact(string text) => _content.AddChild(UiKit.MakeLabel(text, 14, UiKit.TextColor));

    private void Note(string text)
    {
        var label = UiKit.MakeLabel(text, 12, UiKit.MutedTextColor);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _content.AddChild(label);
    }

    private void BuildNationOverview()
    {
        var nation = Session.State.NationById(Session.State.ActiveNationId);
        Heading("Nation Overview");
        if (nation is null)
        {
            Note("No active nation.");
            return;
        }

        Fact($"{nation.Name}");
        Fact($"Treasury: {nation.Treasury}");
        Fact($"Unity: {nation.Unity}");
        Fact($"Wealth: {nation.Wealth}");
        Fact($"Tax base: {nation.TaxBase}  ·  Tax rate: {nation.TaxRatePercent}%");
        Fact($"Population: {nation.Population}");
        Fact($"Cities: {Session.State.Cities.Count(c => c.Owner == nation.Id)}");
        Note("Select a city, army or fleet on the map for its own actions.");
    }

    private void BuildCityPanel(CityState city)
    {
        Heading($"City — {city.Name}");
        Fact($"Owner: {DisplayNation(city.Owner)}  ·  Population: {city.PopulationThousands}k / {city.MaxPopulationThousands}k");
        Fact($"Loyalty: {city.Loyalty}  ·  Supply: {city.SupplyTons}t  ·  Fortification: {city.FortificationCode}");
        Fact(city.UnderSiege ? "Under siege." : "Not under siege.");

        var owner = Session.State.NationById(city.Owner);
        Fact($"Tax rate: {(owner?.TaxRatePercent.ToString() ?? "?")}%  (display-only — no command sets it; see this class's remarks)");

        Fact($"Garrison: {(city.Garrison.Count == 0 ? "none" : string.Join(", ", city.Garrison.Select(u => $"{u.Troops}x {u.UnitTypeId}")))}");

        if (!string.Equals(city.Owner, Session.State.ActiveNationId, StringComparison.Ordinal))
        {
            Note("Only the active seat's own cities can be ordered.");
            return;
        }

        _content.AddChild(new HSeparator());
        _content.AddChild(UiKit.MakeLabel("Recruit standing unit", 15, UiKit.TextColor));

        var typePicker = new OptionButton();
        foreach (var (_, label) in UnitTypes)
        {
            typePicker.AddItem(label);
        }

        _content.AddChild(typePicker);

        var troopSpin = new SpinBox { MinValue = 10, MaxValue = 2000, Step = 10, Value = 200 };
        _content.AddChild(troopSpin);

        _content.AddChild(UiKit.MakeButton("Recruit", () =>
        {
            var unitTypeId = UnitTypes[typePicker.Selected].Id;
            Issue($"recruit-standing {city.Id} {unitTypeId} {(int)troopSpin.Value}");
        }));

        _content.AddChild(new HSeparator());
        _content.AddChild(UiKit.MakeLabel("Fortify", 15, UiKit.TextColor));
        var fortifyPoints = new SpinBox { MinValue = 1, MaxValue = 100, Step = 1, Value = 10 };
        _content.AddChild(fortifyPoints);
        _content.AddChild(UiKit.MakeButton("Order Fortification", () =>
            Issue($"order-city {city.Id} fortify {(int)fortifyPoints.Value}")));

        _content.AddChild(new HSeparator());
        _content.AddChild(UiKit.MakeLabel("Supply transfer (troop/money slider)", 15, UiKit.TextColor));

        var ownArmies = Session.State.Armies.Where(a => a.Nation == city.Owner).ToList();
        if (ownArmies.Count == 0)
        {
            Note("No own army to transfer supply to.");
            return;
        }

        var armyPicker = new OptionButton();
        foreach (var army in ownArmies)
        {
            armyPicker.AddItem(army.Id);
        }

        _content.AddChild(armyPicker);

        var tonsSlider = new HSlider { MinValue = 0, MaxValue = 50, Step = 1, Value = 5, CustomMinimumSize = new Vector2(180, 0) };
        var tonsLabel = UiKit.MakeLabel("5 tons", 12, UiKit.MutedTextColor);
        tonsSlider.ValueChanged += value => tonsLabel.Text = $"{(int)value} tons";
        _content.AddChild(tonsSlider);
        _content.AddChild(tonsLabel);

        _content.AddChild(UiKit.MakeButton("Transfer Supply", () =>
        {
            var armyId = ownArmies[armyPicker.Selected].Id;
            Issue($"buy {armyId} {city.Id} {(int)tonsSlider.Value}");
        }));
    }

    private void BuildArmyPanel(ArmyState army)
    {
        Heading($"Army — {army.Id}");
        Fact($"Nation: {DisplayNation(army.Nation)}  ·  Position: ({army.X}, {army.Y})");
        Fact($"Moves: {army.Moves}  ·  Morale: {army.Morale}  ·  Money: {army.Money}  ·  Supply: {army.SupplyTons}t");
        Fact($"Troops: {(army.Units.Count == 0 ? "none" : string.Join(", ", army.Units.Select(u => $"{u.Troops}x {u.UnitTypeId}")))}");

        if (!string.Equals(army.Nation, Session.State.ActiveNationId, StringComparison.Ordinal))
        {
            Note("Only the active seat's own armies can be ordered.");
            return;
        }

        _content.AddChild(new HSeparator());
        _content.AddChild(UiKit.MakeButton("Move (click a tile on the map)", () => MapView.BeginMoveOrder(army.Id)));
        _content.AddChild(UiKit.MakeButton("Attack (click a target on the map)", () => MapView.BeginAttackOrder(army.Id)));
        _content.AddChild(UiKit.MakeButton("Mobilize first ready slot", () => MobilizeFirstReadySlot(army.Id)));
        _content.AddChild(UiKit.MakeButton("Disband", () => Issue($"disband-army {army.Id}")));
    }

    private void BuildFleetPanel(FleetState fleet)
    {
        Heading($"Fleet — {fleet.Id}");
        Fact($"Nation: {DisplayNation(fleet.Nation)}  ·  Position: ({fleet.X}, {fleet.Y})");
        Fact($"Ships: {fleet.Ships}  ·  Condition: {fleet.ConditionPercent}%  ·  Money: {fleet.Money}  ·  Supply: {fleet.SupplyTons}t");

        if (!string.Equals(fleet.Nation, Session.State.ActiveNationId, StringComparison.Ordinal))
        {
            Note("Only the active seat's own fleets can be ordered.");
            return;
        }

        _content.AddChild(new HSeparator());
        var repairPoints = new SpinBox { MinValue = 1, MaxValue = 100, Step = 1, Value = 10 };
        _content.AddChild(UiKit.MakeLabel("Repair points", 13, UiKit.MutedTextColor));
        _content.AddChild(repairPoints);
        _content.AddChild(UiKit.MakeButton("Repair", () => Issue($"repair-fleet {fleet.Id} {(int)repairPoints.Value}")));
        _content.AddChild(UiKit.MakeButton("Scuttle", () => Issue($"scuttle-fleet {fleet.Id}")));
    }

    /// <summary>
    /// Mobilizes recruitment slot 0 — <strong>[designed]</strong>: <c>mobilize</c> takes a bare slot
    /// index (<c>MobilizeRecruitSlotCommand</c>), not a "ready" flag this panel could filter on, so
    /// "first" is simply index 0; whether that slot is actually ready is exactly what
    /// <c>MobilizeRecruitSlotCommandHandler</c>'s own rejection reports back through
    /// <see cref="Issue"/> when it is not.
    /// </summary>
    private void MobilizeFirstReadySlot(string newArmyName)
    {
        var nation = Session.State.NationById(Session.State.ActiveNationId);
        if (nation is null || nation.RecruitmentSlots.Count == 0)
        {
            CommandIssued?.Invoke(new[] { "No recruitment slot to mobilize." });
            return;
        }

        Issue($"mobilize 0 {newArmyName}-recruit");
    }

    private string DisplayNation(string nationId) => Session.State.NationById(nationId)?.Name ?? nationId;

    private void Issue(string commandLine)
    {
        var output = Session.Submit(commandLine);
        CommandIssued?.Invoke(output.Lines);
        MapView.Refresh();
        Refresh();
    }

    private enum SelectionKind
    {
        None,
        City,
        Army,
        Fleet,
    }

    private readonly record struct Selection(SelectionKind Kind, string? Id)
    {
        public static Selection None() => new(SelectionKind.None, null);

        public static Selection ForCity(string id) => new(SelectionKind.City, id);

        public static Selection ForArmy(string id) => new(SelectionKind.Army, id);

        public static Selection ForFleet(string id) => new(SelectionKind.Fleet, id);
    }
}
