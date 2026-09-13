using IC2.Engine.Model;
using IC2.Engine.Serialization;

namespace IC2.Engine.Tests.Model;

/// <summary>
/// The shipped toy data, loaded once, plus a deliberately non-trivial mid-game state built on top of
/// it — one that exercises every field the task entry requires the model to carry from day one.
/// </summary>
public static class ToyFixtures
{
    private static readonly Lazy<GameDataRepository> LazyRepository =
        new(() => GameDataRepository.Load(TestPaths.DataRoot));

    /// <summary>The repository loaded from the shipped <c>data/</c> directory.</summary>
    public static GameDataRepository Repository => LazyRepository.Value;

    /// <summary>The toy scenario with its world and ruleset resolved.</summary>
    public static ResolvedScenario Toy => Repository.Resolve("toy-3city");

    /// <summary>
    /// A mid-game state: an army embarked on a fleet, a second fleet under construction, an occupied
    /// mercenary pool, a negative diplomatic cooldown, a besieged city, and a partly-filled news log.
    /// </summary>
    public static GameState NonTrivialState()
    {
        var toy = Toy;
        var initial = GameStateFactory.CreateInitial(toy.World, toy.Ruleset, toy.Scenario);
        var ruleset = toy.Ruleset;

        var armies = initial.Armies.Select(army => army.Id == "north-army-1"
            ? army with
            {
                AboardFleetId = "north-fleet-1",
                CoveredTileCode = null,
                X = 0,
                Y = 3,
                Moves = 0,
                Money = ruleset.Economy.PurseCapPerUnit,
                SupplyTons = 120,
            }
            : army with { Morale = army.Morale + 1 });

        var fleets = initial.Fleets.Select(fleet => fleet.Id == "north-fleet-1"
            ? fleet with { CarriedArmyId = "north-army-1", Moves = 0 }
            : fleet with { ConditionPercent = fleet.ConditionPercent - 20 }).ToList();

        fleets.Add(new FleetState(
            Id: "north-fleet-2",
            Nation: "north",
            X: 0,
            Y: 0,
            Moves: 0,
            Ships: ruleset.Naval.OrderMinShips,
            ConditionPercent: 0,
            Money: 0,
            SupplyTons: 0,
            ConstructionTicksRemaining: ruleset.Naval.ConstructionTicks / 2,
            BuildCityId: "portus",
            CarriedArmyId: null,
            CoveredTileCode: null));

        var cities = initial.Cities.Select(city => city.Id == "meridia"
            ? city with { UnderSiege = true, Loyalty = ruleset.Loyalty.DefectionFloor }
            : city);

        var mercenaryPool = ValueList.Of(
            new MercenaryPoolSlot(SlotIndex: 0, NameLabel: 3, UnitTypeId: "light_infantry", Troops: 6438, Quality: 8),
            new MercenaryPoolSlot(SlotIndex: 7, NameLabel: 30, UnitTypeId: "light_cavalry", Troops: 2100, Quality: 6));

        var relations = initial.Relations
            .WithRelation("north", "south", ruleset.Diplomacy.StateCodes.War)
            .WithRelation("north", "south", ruleset.Diplomacy.CooldownAfterEndedWar);

        var news = NewsLog.Empty
            .Append(new NewsEntry("Meridia (south) falls to north."), ruleset.NewsLog)
            .Append(new NewsEntry("north destroys army of south."), ruleset.NewsLog)
            .Append(new NewsEntry("A fleet belonging to south is lost at sea."), ruleset.NewsLog);

        return initial with
        {
            Calendar = new CalendarState(Week: 7, SeasonIndex: 2, YearBc: 269, TurnIndex: 19),
            ActiveSeatIndex = 1,
            Armies = ValueList.From(armies),
            Fleets = ValueList.From(fleets),
            Cities = ValueList.From(cities),
            MercenaryPool = mercenaryPool,
            Relations = relations,
            NewsLog = news,
        };
    }

    /// <summary>A save wrapping <see cref="NonTrivialState"/>.</summary>
    public static SaveGame NonTrivialSave()
    {
        var state = NonTrivialState();
        return new SaveGame(
            SchemaVersion: GameDataSchema.CurrentVersion,
            Id: "toy-3city-turn-19",
            Label: "Toy skirmish, turn 19",
            ScenarioId: state.ScenarioId,
            WorldId: state.WorldId,
            RulesetId: state.RulesetId,
            State: state);
    }
}
