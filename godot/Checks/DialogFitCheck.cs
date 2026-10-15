using Godot;
using IC2.Engine.Model;
using IC2.Engine.Presentation;
using IC2.Slice.UI;

namespace IC2.Slice.Checks;

/// <summary>T169: real control layout and compactness checks, using the same fixture as the screenshot tour.</summary>
public partial class DialogFitCheck : Control
{
    private MainGameScreen _game = null!;
    private int _frame;
    public override void _Ready()
    {
        Size = GetViewport().GetVisibleRect().Size;
        _game = new MainGameScreen { Session = BuildSession(), RepositoryRoot = GameDataContext.RepositoryRoot };
        _game.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_game);
        _game.SelectArmyForCheck("tour-a");
    }
    public override void _Process(double delta)
    {
        if (++_frame != 12) return;
        var content = Descendants(_game.ContextPanel).OfType<VBoxContainer>().Single();
        GD.Print($"DialogFitCheck: army height={content.GetCombinedMinimumSize().Y}; labels={Descendants(content).OfType<Label>().Count()}; viewport={Size}");
        GD.Print("DialogFitCheck: exiting with code 0.");
        GetTree().Quit();
    }
    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }    private static GameSession BuildSession()
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

}
