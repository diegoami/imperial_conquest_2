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
/// <strong>T140 (bug #718)</strong> rewrote the panel's facts lines against the research read
/// <c>2026-10-05-information-window-fields-and-bands.md</c>. The lines themselves come from
/// <see cref="InformationPanelModel"/> and the band words from <see cref="InformationWords"/>.
/// The own / foreign split for every fact is decided against the <em>active seat</em>'s nation
/// (<see cref="GameState.ActiveNationId"/>), never against the nation selected for viewing from
/// the Nations menu: selecting Carthage while Rome is the active seat shows Carthage's panel and
/// Carthage's cities as foreign.
/// <list type="bullet">
/// <item><description><strong>Two known engine gaps, reported rather than worked around</strong> (<c>docs/tasks/T24.md</c>):
/// "If a screen needs an engine or view-model capability that GameSession doesn't expose, do NOT change
/// src/. STOP and report what's missing.":</description></item>
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
/// Split fleet and Fortify city. T109's city Recruit and army Mobilize live in the Strategy menu's
/// dialogs (in flight); T140 stubs both buttons as already removed so the merge order does not block.</description></item>
/// </list>
/// </remarks>
public partial class ContextPanel : Control
{
    /// <summary>The red colour the original draws a conquered-nation row in (its own decompile
    /// assigns one; the clone picks this close red, never a literal it forgets to declare).</summary>
    public static readonly Color ConqueredRowColor = new(0.95f, 0.35f, 0.30f);

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

    /// <summary>
    /// T113, the city's right-click listing: shows the live mercenary offers on the right-clicked
    /// city's tile, in slot order, in the unit-list view — same panel surface as
    /// <see cref="ShowUnitList"/>, the heading "Mercenaries at &lt;city&gt;", one row per live offer
    /// (type, troops, quality, quarterly cost), or "There are no mercenaries at &lt;city&gt;" with
    /// none. <see cref="MainGameScreen"/> wires a city's right click to this in preference to
    /// <see cref="ShowUnitList"/> (the 2026-10-05 amendment; the right click still selects nothing
    /// and disarms no order, exactly as T99's path did).
    /// </summary>
    public void ShowMercenariesAtCity(string cityId)
    {
        _selection = Selection.ForUnitList(MapEntityKind.City, cityId);
        _showMercenariesForCity = true;
        Rebuild();
    }

    private bool _showMercenariesForCity;

    /// <summary>Re-reads the current selection's own state and rebuilds this panel's controls — call after
    /// any command that might have changed what is selected (a city captured, an army disbanded, ...).
    /// <see cref="ShowMercenariesAtCity"/>'s flag is preserved across a refresh, so a hire that lands
    /// and leaves the panel showing a city's mercenaries still does; a hire that lists an empty
    /// re-reads the model and shows the empty message.</summary>
    public void Refresh()
    {
        if (_showMercenariesForCity
            && _selection.Kind == SelectionKind.UnitList
            && _selection.Entity == MapEntityKind.City
            && _selection.Id is { } cityId)
        {
            // After a refresh the mercenary listing should still reflect the live offers — the
            // model's Refresh-style behaviour is to rebuild against the new state.
            Rebuild();
        }
        else
        {
            Rebuild();
        }
    }

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
                _showMercenariesForCity = false;
                BuildCityPanel(city);
                break;
            case SelectionKind.Army when Session.State.ArmyById(_selection.Id!) is { } army:
                _showMercenariesForCity = false;
                BuildArmyPanel(army);
                break;
            case SelectionKind.Fleet when Session.State.FleetById(_selection.Id!) is { } fleet:
                _showMercenariesForCity = false;
                BuildFleetPanel(fleet);
                break;
            case SelectionKind.UnitList:
                BuildUnitListPanel(_selection.Entity, _selection.Id!);
                // T113 (2026-10-05 amendment): a right click on an army or a fleet still lists its own
                // units — the mercenary listing is the city's case alone. Reset the flag for any
                // non-city path so a stale Mercenaries view from a previous city click cannot leak into
                // a following unit-list rebuild. BuildUnitListPanel reads the flag only inside the city
                // case (the new branch at the top of its switch), so leaving it on for an army/fleet
                // panel has no effect; resetting it is the cleaner explanation.
                if (_selection.Entity != MapEntityKind.City)
                {
                    _showMercenariesForCity = false;
                }

                break;
            default:
                _showMercenariesForCity = false;
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
    private void Fact(string text) => AddFactLabel(text, UiKit.TextColor);

    private void Fact(string text, Color color) => AddFactLabel(text, color);

    private void AddFactLabel(string text, Color color)
    {
        var label = UiKit.MakeLabel(text, 14, color);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _content.AddChild(label);
    }

    private void Note(string text)
    {
        var label = UiKit.MakeLabel(text, 12, UiKit.MutedTextColor);
        label.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _content.AddChild(label);
    }

    /// <summary>An empty <see cref="Label"/> between two fact rows — the original's blank-line spacer.</summary>
    private void Blank() => _content.AddChild(UiKit.MakeLabel(string.Empty, 8, UiKit.TextColor));

    /// <summary>
    /// T140 (bug #718): the viewed nation's status panel, populated from
    /// <see cref="NationStatusModel.Build"/>. The own nation's panel carries the full list (Leader,
    /// Capital, Cities, CityNames, Population, Unity, Tax rate, Mobilized, Treasury, the relations
    /// matrix, the training section); the foreign panel adds Population/Unity/Tax rate and every
    /// relation, but withholds Mobilized and Treasury. A relation row whose
    /// <see cref="NationStatusLine.IsRed"/> is true (a conquered nation) is drawn in this class's
    /// <see cref="ConqueredRowColor"/>.
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
            var color = line.IsRed ? ConqueredRowColor : UiKit.TextColor;
            if (string.Equals(line.Key, NationStatusModel.TrainingHeaderKey, StringComparison.Ordinal))
            {
                _content.AddChild(UiKit.MakeLabel(line.Text, 15, color));
            }
            else
            {
                Fact(line.Text, color);
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
        var viewer = Session.State.ActiveNationId;
        var lines = InformationPanelModel.City(Session.State, Session.World, Session.Ruleset, city, viewer);
        var cityHeading = $"City — {city.Name}";
        Heading(cityHeading);
        foreach (var line in lines)
        {
            Fact(line.Text);
        }

        // The T110 v0.5.0 decision: every fact above sits on its own label. Below it, the four
        // "extras" the clone keeps (the heads-up "Under siege" line, the per-city tax rate, the
        // garrison and "In training here") — bug #608's tax-rate line is the third, the garrison is
        // here for the player until T113 hands the city's right click to its mercenaries.
        Fact(city.UnderSiege ? "Under siege." : "Not under siege.");

        var owner = Session.State.NationById(city.Owner);
        Fact($"Tax rate: {(owner?.TaxRatePercent.ToString() ?? "?")}%  (display-only — no command sets it; see this class's remarks)");

        Fact($"Garrison: {(city.Garrison.Count == 0 ? "none" : string.Join(", ", city.Garrison.Select(u => $"{u.Troops}x {u.UnitTypeId}")))}");

        // Fix #513: the regiments this city is training (the clone keeps it below the original's
        // facts so the carry-over from T110 still applies).
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

        // T109's Recruit section is stubbed as already removed: the merge order reconciles
        // when T109 lands, and this branch renders nothing in its place.
        if (!string.Equals(city.Owner, Session.State.ActiveNationId, StringComparison.Ordinal))
        {
            Note("Only the active seat's own cities can be ordered.");
            return;
        }

        // T112: the panel is information only — its last order buttons are gone (Order
        // Fortification, the Recruit section T109 removes). See this class's remarks.
        Note("Orders for this city are in the Unit map menu.");
    }

    private void BuildArmyPanel(ArmyState army)
    {
        var isOwn = string.Equals(army.Nation, Session.State.ActiveNationId, StringComparison.Ordinal);
        var lines = isOwn
            ? InformationPanelModel.OwnArmy(Session.State, Session.World, Session.Ruleset, army)
            : InformationPanelModel.ForeignArmy(Session.State, Session.World, Session.Ruleset, army);

        Heading($"Army — {army.Id}");
        foreach (var line in lines)
        {
            if (line.Text.Length == 0)
            {
                Blank();
            }
            else
            {
                Fact(line.Text);
            }
        }

        if (!isOwn)
        {
            // T99's withholding: the four facts above are already "withheld" captions; this is the
            // panel's final whisper, which the existing clone still ships for the right path.
            Note("Only the active seat's own armies can be ordered.");
            return;
        }

        // T109's Mobilize button is stubbed as removed; T111 moved Disband to the Army menu; T112
        // made the panel information-only. See this class's remarks.
        Note("Orders for this army are in the Unit map menu.");
    }

    private void BuildFleetPanel(FleetState fleet)
    {
        var isOwn = string.Equals(fleet.Nation, Session.State.ActiveNationId, StringComparison.Ordinal);
        var lines = isOwn
            ? InformationPanelModel.OwnFleet(Session.State, Session.World, Session.Ruleset, fleet)
            : InformationPanelModel.ForeignFleet(Session.State, Session.World, Session.Ruleset, fleet);

        Heading($"Fleet — {fleet.Id}");
        foreach (var line in lines)
        {
            if (line.Text.Length == 0)
            {
                Blank();
            }
            else
            {
                Fact(line.Text);
            }
        }

        if (!isOwn)
        {
            Note("Only the active seat's own fleets can be ordered.");
            return;
        }

        // T112 removed Repair and Scuttle (the last two fleet-order buttons); T140 leaves the
        // panel information-only and points to the Unit map menu. See this class's remarks.
        Note("Orders for this fleet are in the Unit map menu.");
    }

    /// <summary>
    /// T99's unit-list view — what the original's right button shows
    /// (<c>ShowCityUnits</c>/<c>ShowArmyUnits</c>/<c>ShowFleetUnits</c>, audit §2.1): a city's garrison,
    /// an army's units, or a fleet's ships and any army aboard. It is the panel's one view that is
    /// <em>not</em> a selection: the map's selection, and any order armed by it, are untouched.
    /// </summary>
    /// <remarks>
    /// T113 (the 2026-10-05 amendment): the <see cref="MapEntityKind.City"/> case lists the city's
    /// mercenary offers instead of its garrison when <see cref="_showMercenariesForCity"/> is set —
    /// the user's right click on a city lists the mercenaries on offer there, matching the original's
    /// <c>TInformation_ShowCityUnits</c> literal "Mercenaries at &lt;city&gt;" / "There are no
    /// mercenaries at &lt;city&gt;" [Wine candidate: feature inventory row UM07, <c>FI_b1_25_city_right_click.png</c>;
    /// derived: code, the literals]. The T99 garrison listing is unchanged for the left-click path.
    /// </remarks>
    private void BuildUnitListPanel(MapEntityKind entity, string id)
    {
        switch (entity)
        {
            case MapEntityKind.City when _showMercenariesForCity && Session.State.CityById(id) is { } mercCity:
                BuildCityMercenaryListing(mercCity);
                return;

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

    /// <summary>
    /// The T113 (2026-10-05 amendment) city right-click listing: the live mercenary offers on the
    /// city's tile, in slot order, with the columns the Recruit mercenaries dialog shows. With none,
    /// the heading "There are no mercenaries at &lt;city&gt;". The model is
    /// <see cref="MercenaryDialogModel.ForCity"/>; the view and the dialog read it through one path so
    /// they cannot disagree between them.
    /// </summary>
    private void BuildCityMercenaryListing(CityState city)
    {
        var model = MercenaryDialogModel.ForCity(Session.State, city.Id, Session.Ruleset);
        if (model.Offers.Count == 0)
        {
            Heading($"There are no mercenaries at {city.Name}");
            return;
        }

        Heading($"Mercenaries at {city.Name}");
        foreach (var offer in model.Offers)
        {
            Fact(
                $"{offer.TypeName}  —  {offer.Troops.ToString(System.Globalization.CultureInfo.InvariantCulture)} troops"
                + $"  —  {MercenaryDialogModel.QualityCaption(offer.Quality)}"
                + $"  —  {offer.QuarterlyCostTalents.ToString(System.Globalization.CultureInfo.InvariantCulture)}/quarter");
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
