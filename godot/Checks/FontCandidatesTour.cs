using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>T169 final theme tour: opens the real panels/dialogs used by T168's as-is evaluation.
/// Rejected font candidates have been removed; the evaluation's historical boards remain its evidence.
/// Run windowed with IC2_SCREENSHOT_DIR; ScreenshotTour covers menu/chooser/seat/game, this tour covers dialogs.</summary>
public partial class FontCandidatesTour : Control
{
    private const int SettleFrames = 8;
    private string _outputDirectory = string.Empty;
    private MainGameScreen? _game;
    private readonly Queue<(string Name, Action Open)> _asIsSteps = new();
    private string? _pendingCapture;
    private int _frame;
    public override void _Ready()
    {
        _outputDirectory=System.Environment.GetEnvironmentVariable("IC2_SCREENSHOT_DIR") ?? string.Empty;
        if(string.IsNullOrWhiteSpace(_outputDirectory))throw new InvalidOperationException("IC2_SCREENSHOT_DIR is required for the final theme tour.");
        Directory.CreateDirectory(_outputDirectory);
        Size=GetViewport().GetVisibleRect().Size;
        _game=new MainGameScreen { Session=BuildAsIsSession(),RepositoryRoot=GameDataContext.RepositoryRoot };
        _game.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_game);
        StartAsIsSteps();
    }
    public override void _Process(double delta) => ProcessAsIs();
    private void StartAsIsSteps()
    {
        var g = _game!;
        var state = g.Session.State;
        var nation = state.ActiveNationId;
        var army = state.ArmyById("tour-a")!;
        var fleet = state.Fleets.FirstOrDefault(f => f.Id == "tour-fleet");
        void Menu(string id) => g.MenuBar.PressItemForCheck(id);
        void Army(string id) { g.SelectArmyForCheck(army.Id); Menu(id); }
        void Fleet(string id) { if (fleet is not null) { g.SelectFleetForCheck(fleet.Id); Menu(id); } }
        void Add(string name, Action a) => _asIsSteps.Enqueue((name, a));
        Add("10-panel-army", () => g.SelectArmyForCheck(army.Id));
        if (fleet is not null) { Add("11-panel-fleet", () => g.SelectFleetForCheck(fleet.Id)); }
        Add("12-panel-city", () => g.ContextPanel.ShowCity("rome"));
        Add("20-dialog-taxation", () => Menu("strategy.taxation"));
        Add("21-dialog-balance-sheet", () => Menu("strategy.balance_sheet"));
        Add("22-dialog-recruit-unit", () => Menu("strategy.recruit_unit"));
        Add("23-dialog-build-fleet", () => Menu("strategy.build_fleet"));
        Add("24-dialog-find-city", () => Menu("area_map.find_city"));
        Add("30-dialog-army-supply", () => Army("unit_map.army_supply"));
        Add("31-dialog-army-recruit-mercenaries", () => Army("unit_map.army_recruit_mercenaries"));
        Add("32-dialog-army-transfer", () => Army("unit_map.army_transfer_unit"));
        Add("33-dialog-army-split", () => Army("unit_map.army_split"));
        Add("34-dialog-army-change-units", () => Army("unit_map.army_change_units"));
        if (fleet is not null) Add("40-dialog-fleet-supply", () => Fleet("unit_map.fleet_supply"));
        if (fleet is not null) Add("41-dialog-fleet-repair", () => Fleet("unit_map.fleet_repair"));
        if (fleet is not null) Add("42-dialog-fleet-transfer", () => Fleet("unit_map.fleet_transfer_ships"));
        if (fleet is not null) Add("43-dialog-fleet-split", () => Fleet("unit_map.fleet_split"));
        Add("50-dialog-fortify-city", () => { g.SelectCityForCheck("rome"); Menu("unit_map.city_fortify"); });
        Add("60-menu-nations-relations", () => Menu("strategy.relations"));
    }

    /// <summary>
    /// The shipped classical world with Rome as the human seat plus two adjacent Roman armies and a Roman
    /// fleet (the same placements <c>ArmyOrdersCheck</c> uses), so Transfer, Join, Split and the fleet
    /// dialogs have something to open on.
    /// </summary>
    private static GameSession BuildAsIsSession()
    {
        var resolved = GameDataContext.Repository.Resolve("classical-mediterranean");
        static StartingArmy Roman(string id, int x, int y, params (string Type, int Troops, int Quality)[] units) => new(
            id, "rome", x, y, Morale: 70, Money: 100, SupplyTons: 100, Moves: 8,
            Units: ValueList.From(units.Select((unit, index) =>
                new UnitSlot(0, unit.Type, unit.Troops, unit.Quality, $"{id} unit {index + 1}"))));
        var armies = resolved.World.StartingArmies.ToList();
        armies.Add(Roman("tour-a", 100, 100, ("heavy_infantry", 5_000, 6), ("archers", 3_000, 8), ("light_cavalry", 2_000, 7)));
        armies.Add(Roman("tour-b", 100, 99, ("light_cavalry", 4_000, 7), ("light_infantry", 1_000, 5)));
        var fleets = resolved.World.StartingFleets.ToList();
        fleets.Add(new StartingFleet("tour-fleet", "rome", 100, 120, Ships: 10, ConditionPercent: 80, Money: 0, SupplyTons: 300, Moves: 8));
        fleets.Add(new StartingFleet("tour-fleet-b", "rome", 100, 121, Ships: 6, ConditionPercent: 100, Money: 50, SupplyTons: 150, Moves: 8));
        var world = resolved.World with
        {
            StartingArmies = ValueList.From(armies),
            StartingFleets = ValueList.From(fleets),
        };
        var initial = new GameSession(world, resolved.Ruleset, resolved.Scenario, seedOverride: 1, humanSeatNationId: "rome");

        // A Roman city one tile from tour-a (a supply provider, and the place mercenaries are offered) and
        // two mercenary offers there, so Supply army and Recruit mercenaries open on the sample game.
        var city = new CityState(
            "tour-city", "Tour city", X: 101, Y: 100, Owner: "rome", Allegiance: "rome",
            Loyalty: 90, SupplyTons: 500, FortificationCode: 0,
            PopulationThousands: 100, MaxPopulationThousands: 100, Tribute: 0, Garrison: ValueList<UnitSlot>.Empty);
        var offers = new List<MercenaryPoolSlot>
        {
            new(900, X: 101, Y: 100, NameLabel: 0, UnitTypeId: "heavy_cavalry", Troops: 960, Quality: 9),
            new(901, X: 101, Y: 100, NameLabel: 0, UnitTypeId: "light_infantry", Troops: 3_868, Quality: 8),
        };
        var state = initial.State with
        {
            Cities = ValueList.From(initial.State.Cities.Concat(new[] { city })),
            MercenaryPool = ValueList.From(initial.State.MercenaryPool.Concat(offers)),
        };
        var save = new SaveGame(state.SchemaVersion, "font-tour", "font-tour", state.ScenarioId, state.WorldId, state.RulesetId, state);
        return new GameSession(world, resolved.Ruleset, resolved.Scenario, save);
    }

    private void ProcessAsIs()
    {
        _frame++;
        if (_frame < SettleFrames)
        {
            return;
        }

        if (_pendingCapture is not null)
        {
            Capture("asis-" + _pendingCapture + ".png");
            _pendingCapture = null;
            _frame = 0;
            return;
        }

        if (_asIsSteps.Count == 0)
        {
            GD.Print("FontCandidatesTour (asis): done.");
            GetTree().Quit(0);
            return;
        }

        var (name, open) = _asIsSteps.Dequeue();
        var before = _game.ActiveOverlay;
        open();
        if (name.Contains("dialog") && (_game.ActiveOverlay is null || ReferenceEquals(_game.ActiveOverlay, before)))
        {
            throw new InvalidOperationException($"FontCandidatesTour (asis): {name} did not open ({_game.LastMessageForCheck}).");
        }

        _pendingCapture = name;
        _frame = 0;
    }

    private void Capture(string fileName)
    {
        if (string.IsNullOrWhiteSpace(_outputDirectory))
        {
            return;
        }

        var path = Path.Combine(_outputDirectory, fileName);
        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print(error == Error.Ok
            ? $"FontCandidatesTour: saved {path}"
            : $"FontCandidatesTour: could not save {path}: {error}");
    }

}
