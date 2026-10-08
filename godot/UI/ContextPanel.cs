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
/// <item><description><strong>The panels are information only.</strong> T112 removed the last order
/// buttons — the city panel's Order Fortification (and, before it, Recruit) and the fleet panel's Repair
/// and Scuttle — so this panel renders no command button at all (the user's decision that it shows
/// information only). The Unit map's menu and its command strip own every order: Supply fleet/army
/// (<c>economy.buy-supply</c>/<c>naval.buy-fleet-supply</c>, T134/T112), Transfer unit/ships
/// (<c>armies.army-transfer</c>/<c>naval.fleet-to-fleet-transfer</c>, T106/T117/T114), Repair fleet,
/// Split fleet and Fortify city. T109 moves the city's Recruit and the army's Mobilize into the
/// Strategy menu's dialogs.</description></item>
/// </list>
/// </remarks>
public partial class ContextPanel : Control
{
    public required GameSession Session { get; init; }

    public required GameMapView MapView { get; init; }

    /// <summary>Raised after this panel issues a command, so <see cref="MainGameScreen"/> can append the
    /// output to the news log and refresh the top bar/map. <strong>T112:</strong> the panel is information
    /// only, so it never raises this itself; the event is kept because
    /// <c>godot/Checks/MapClickCheck.cs</c> (out of this task's Owns) subscribes to it.</summary>
#pragma warning disable CS0067 // The event is subscribed by MapClickCheck; the panel issues no command since T112.
    public event Action<IReadOnlyList<string>>? CommandIssued;
#pragma warning restore CS0067

    private VBoxContainer _content = null!;
    private Selection _selection = Selection.None();
    private string? _viewedNationId;
    private bool _viewedNationSet;

    /// <summary>The status lines the last status-panel rebuild rendered — the panel's own output, not a
    /// re-run of <see cref="NationStatusModel.Build"/>. Empty for All nations and for the "No nation."
    /// branch; a check reads it to prove what the panel actually shows.</summary>
    private IReadOnlyList<NationStatusLine> _viewedNationLines = Array.Empty<NationStatusLine>();

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

    /// <summary>
    /// T110: shows the <em>viewed</em> nation's status panel — <see langword="null"/> is the original's
    /// All nations selection, which shows no status panel. <see cref="MainGameScreen"/> calls this from
    /// the Nations menu and the nation swatches.
    /// </summary>
    public void SetViewedNation(string? nationId)
    {
        _viewedNationId = nationId;
        _viewedNationSet = true;
        _selection = Selection.None();
        Rebuild();
    }

    /// <summary>The nation whose status panel is currently shown, or <see langword="null"/> when a unit
    /// is selected or the view is All nations — what <c>NationsAreaMapCheck</c> asserts.</summary>
    public string? StatusNationIdForCheck =>
        _selection.Kind == SelectionKind.None ? EffectiveViewedNationId() : null;

    /// <summary>Whether the panel is showing the All nations view (no status panel).</summary>
    public bool ShowsAllNationsForCheck =>
        _selection.Kind == SelectionKind.None && EffectiveViewedNationId() is null;

    /// <summary>The status lines the panel actually rendered for the viewed nation, or
    /// <see langword="null"/> when a selection (a city, army, fleet or unit list) is showing instead.
    /// This is the panel's own output, so a check can assert the public-facts rule at the panel rather
    /// than re-running <see cref="NationStatusModel.Build"/>.</summary>
    public IReadOnlyList<NationStatusLine>? ViewedNationLinesForCheck =>
        _selection.Kind == SelectionKind.None ? _viewedNationLines : null;

    /// <summary>Whether the viewed nation's rendered status panel carries a line with this key.</summary>
    public bool HasViewedNationLineForCheck(string key) =>
        ViewedNationLinesForCheck?.Any(line => string.Equals(line.Key, key, StringComparison.Ordinal)) == true;

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

    /// <summary>
    /// T99, the user's two-button decision of 2026-10-01: shows the clicked entity's unit list — a
    /// city's garrison, an army's units, or a fleet's ships and any army aboard — without making it
    /// the map's selection. <see cref="MainGameScreen"/> wires
    /// <see cref="GameMapView.UnitListRequested"/> to this.
    /// </summary>
    public void ShowUnitList(MapEntityKind entity, string id)
    {
        _selection = Selection.ForUnitList(entity, id);
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
            case SelectionKind.UnitList:
                BuildUnitListPanel(_selection.Entity, _selection.Id!);
                break;
            default:
                BuildViewedNationPanel();
                break;
        }
    }

    private void Heading(string text) => _content.AddChild(UiKit.MakeLabel(text, 20, UiKit.AccentColor));

    /// <summary>
    /// Fix #491: an unwrapped <see cref="Label"/>'s minimum size is its full single-line text width, so
    /// a long stats/Troops line (<c>"5000x heavy_infantry, ..."</c>) forced this panel's
    /// <see cref="VBoxContainer"/> — and with it <c>MainGameScreen</c>'s 340&#160;px
    /// <c>CustomMinimumSize</c> panel — wider than the viewport, pushing the panel (and its order
    /// buttons) off-screen. <see cref="Note"/> already wraps for exactly this reason; <c>Fact</c> now
    /// does the same, which lets the panel settle back to its 340&#160;px floor.
    /// </summary>
    private void Fact(string text)
    {
        var label = UiKit.MakeLabel(text, 14, UiKit.TextColor);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _content.AddChild(label);
    }

    private void Note(string text)
    {
        var label = UiKit.MakeLabel(text, 12, UiKit.MutedTextColor);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _content.AddChild(label);
    }

    /// <summary>
    /// T110: the viewed nation's status panel, or the All nations view when the viewed nation is null.
    /// The own nation's lines come from <see cref="NationStatusModel"/> with the full confirmed list; a
    /// foreign nation's are its public facts only (the user's decision of 2026-10-01).
    /// </summary>
    private void BuildViewedNationPanel()
    {
        var viewed = EffectiveViewedNationId();
        _viewedNationLines = Array.Empty<NationStatusLine>();
        if (viewed is null)
        {
            Heading("All nations");
            Note("Every nation's highlights are shown. Choose a nation for its status panel.");
            Note("Select a city, army or fleet on the map for its own details.");
            return;
        }

        var nation = Session.State.NationById(viewed);
        if (nation is null)
        {
            Heading("Nation Overview");
            Note("No nation.");
            return;
        }

        Heading("Nation Overview");
        Fact(nation.Name);
        _viewedNationLines = NationStatusModel.Build(
            Session.State, Session.Ruleset, viewed, viewerNationId: Session.State.ActiveNationId);
        foreach (var line in _viewedNationLines)
        {
            if (string.Equals(line.Key, NationStatusModel.TrainingHeaderKey, StringComparison.Ordinal))
            {
                _content.AddChild(UiKit.MakeLabel(line.Text, 15, UiKit.TextColor));
            }
            else
            {
                Fact(line.Text);
            }
        }

        Note("Select a city, army or fleet on the map for its own details.");
    }

    /// <summary>The viewed nation once <see cref="SetViewedNation"/> has run; before that, the active
    /// seat's nation, so the panel's first paint is unchanged.</summary>
    private string? EffectiveViewedNationId() =>
        _viewedNationSet ? _viewedNationId : Session.State.ActiveNationId;

    private void BuildCityPanel(CityState city)
    {
        Heading($"City — {city.Name}");
        Fact($"Owner: {DisplayNation(city.Owner)}  ·  Population: {city.PopulationThousands}k / {city.MaxPopulationThousands}k");
        Fact($"Loyalty: {city.Loyalty}  ·  Supply: {city.SupplyTons}t  ·  Fortification: {city.FortificationCode}");
        Fact(city.UnderSiege ? "Under siege." : "Not under siege.");

        var owner = Session.State.NationById(city.Owner);
        Fact($"Tax rate: {(owner?.TaxRatePercent.ToString() ?? "?")}%  (display-only — no command sets it; see this class's remarks)");

        Fact($"Garrison: {(city.Garrison.Count == 0 ? "none" : string.Join(", ", city.Garrison.Select(u => $"{u.Troops}x {u.UnitTypeId}")))}");

        // Fix #513, Defect 1: the regiments this city is training, among everything else the panel says
        // about the city. Shown for any owner's city (a captured city's queue has been cleared by the
        // engine, but the panel never invents that); only the commands below are gated on the active seat.
        var inTraining = RecruitmentPanelViewModel.TrainingAtCity(Session.State, Session.Ruleset, city.Owner, city.Id);
        _content.AddChild(UiKit.MakeLabel("In training here", 15, UiKit.TextColor));
        if (inTraining.Count == 0)
        {
            Note("No regiment is training here.");
        }
        else
        {
            foreach (var regiment in inTraining)
            {
                Fact($"{regiment.UnitTypeId} — {regiment.Troops} troops — {regiment.ReadinessText}");
            }
        }

        if (!string.Equals(city.Owner, Session.State.ActiveNationId, StringComparison.Ordinal))
        {
            Note("Only the active seat's own cities can be ordered.");
            return;
        }

        // T112: the panel is information only — its last order buttons (Order Fortification, and before
        // it Recruit) are gone, and the city's orders live in the Unit map → City menu (and, from T109,
        // the Strategy menu's dialogs). See this class's remarks.
        Note("Orders for this city are in the Unit map menu.");
    }

    private void BuildArmyPanel(ArmyState army)
    {
        Heading($"Army — {army.Id}");
        Fact($"Nation: {DisplayNation(army.Nation)}  ·  Position: ({army.X}, {army.Y})");

        if (!string.Equals(army.Nation, Session.State.ActiveNationId, StringComparison.Ordinal))
        {
            // T99, the original's own foreign-army fog [confirmed: ptolemy-run-ui-inventory-and-leader-draw.md
            // §5]: a foreign army's moves, morale, money and supply are withheld — the panel names each
            // withheld field but shows no number for it. Fleets and cities are unchanged. The exact
            // glyph the original used is not transcribed in the audit, so naming the withholding is
            // this panel's own [designed] rendering of the confirmed rule.
            Fact("Moves: withheld  ·  Morale: withheld  ·  Money: withheld  ·  Supply: withheld");
            Fact($"Troops: {FormatUnits(army.Units)}");
            Note("Only the active seat's own armies can be ordered.");
            return;
        }

        Fact($"Moves: {army.Moves}  ·  Morale: {army.Morale}  ·  Money: {army.Money}  ·  Supply: {army.SupplyTons}t");
        Fact($"Troops: {FormatUnits(army.Units)}");

        _content.AddChild(new HSeparator());

        // T111: the army panel's Disband button is gone — the Army menu's Disband army entry (with
        // T99's confirmation prompt) is now the one path to disband-army.
        // T112: the panel is information only; the Mobilize button moved with T109's Recruit unit dialog,
        // so no army order button remains here. See this class's remarks.
        Note("Orders for this army are in the Unit map menu.");
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

        // T112: the fleet panel's Repair and Scuttle buttons are gone — the panel is information only,
        // and the fleet's orders live in the Unit map → Fleet menu. See this class's remarks.
        Note("Orders for this fleet are in the Unit map menu.");
    }

    /// <summary>
    /// T99's unit-list view — what the original's right button shows
    /// (<c>ShowCityUnits</c>/<c>ShowArmyUnits</c>/<c>ShowFleetUnits</c>, audit §2.1): a city's garrison,
    /// an army's units, or a fleet's ships and any army aboard. It is the panel's one view that is
    /// <em>not</em> a selection: the map's selection, and any order armed by it, are untouched.
    /// </summary>
    private void BuildUnitListPanel(MapEntityKind entity, string id)
    {
        switch (entity)
        {
            case MapEntityKind.City when Session.State.CityById(id) is { } city:
                Heading($"Units — {city.Name}");
                Fact($"Owner: {DisplayNation(city.Owner)}  ·  Population: {city.PopulationThousands}k");
                Note("The garrison:");
                ListUnitSlots(city.Garrison);
                return;

            case MapEntityKind.Army when Session.State.ArmyById(id) is { } army:
                Heading($"Units — {army.Id}");
                Fact($"Nation: {DisplayNation(army.Nation)}  ·  Position: ({army.X}, {army.Y})");
                Note("The army's units:");
                ListUnitSlots(army.Units);
                return;

            case MapEntityKind.Fleet when Session.State.FleetById(id) is { } fleet:
                Heading($"Units — {fleet.Id}");
                Fact($"Nation: {DisplayNation(fleet.Nation)}  ·  Position: ({fleet.X}, {fleet.Y})");
                Fact($"Ships: {fleet.Ships}  ·  Condition: {fleet.ConditionPercent}%");
                if (fleet.CarriedArmyId is { } aboard && Session.State.ArmyById(aboard) is { } carried)
                {
                    Note($"The army aboard ({carried.Id}):");
                    ListUnitSlots(carried.Units);
                }
                else
                {
                    Note("No army aboard.");
                }

                return;

            default:
                Heading("Units");
                Note("Nothing is here.");
                return;
        }
    }

    /// <summary>One line per unit slot — the list shape the original's unit list shows.</summary>
    private void ListUnitSlots(IEnumerable<UnitSlot> slots)
    {
        var any = false;
        foreach (var slot in slots)
        {
            any = true;
            Fact($"{slot.Troops}x {slot.UnitTypeId} ({ArmyDialogModels.QualityCaption(slot.Quality)})"
                + (string.IsNullOrEmpty(slot.Name) ? string.Empty : $" — {slot.Name}"));
        }

        if (!any)
        {
            Note("None.");
        }
    }

    /// <summary>
    /// One army's units as the panel's Troops line shows them — troops, type, and T111's regiment quality
    /// caption next to both, folded in by the user's decision of 2026-10-02.
    /// </summary>
    private static string FormatUnits(IReadOnlyList<UnitSlot> units) =>
        units.Count == 0
            ? "none"
            : string.Join(", ", units.Select(u =>
                $"{u.Troops}x {u.UnitTypeId} ({ArmyDialogModels.QualityCaption(u.Quality)})"));

    private string DisplayNation(string nationId) => Session.State.NationById(nationId)?.Name ?? nationId;

    private string CityName(string cityId) => Session.State.CityById(cityId)?.Name ?? cityId;

    private enum SelectionKind
    {
        None,
        City,
        Army,
        Fleet,

        /// <summary>T99: the right button's unit-list view — a city's garrison, an army's units, or a fleet's ships and any army aboard.</summary>
        UnitList,
    }

    private readonly record struct Selection(SelectionKind Kind, MapEntityKind Entity, string? Id)
    {
        public static Selection None() => new(SelectionKind.None, MapEntityKind.City, null);

        public static Selection ForCity(string id) => new(SelectionKind.City, MapEntityKind.City, id);

        public static Selection ForArmy(string id) => new(SelectionKind.Army, MapEntityKind.Army, id);

        public static Selection ForFleet(string id) => new(SelectionKind.Fleet, MapEntityKind.Fleet, id);

        public static Selection ForUnitList(MapEntityKind entity, string id) =>
            new(SelectionKind.UnitList, entity, id);
    }
}
