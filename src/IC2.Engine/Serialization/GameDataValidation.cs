using IC2.Engine.Model;

namespace IC2.Engine.Serialization;

/// <summary>
/// The semantic checks a document has to pass once it has parsed and matched the schema: ids are
/// unique, references inside the document resolve, and the encodings that carry meaning (the terrain
/// grid's length, the relation matrix's symmetry) actually hold.
/// </summary>
/// <remarks>
/// These are reported as <see cref="MalformedGameDataException"/>: the file is syntactically fine and
/// structurally complete, but it does not describe a usable world/ruleset/scenario/save. Catching them
/// here means no later task has to defend against, say, a non-square relation matrix.
/// </remarks>
public static class GameDataValidation
{
    /// <summary>Runs the checks appropriate to whichever document kind was loaded.</summary>
    public static void Validate(string documentPath, IVersionedDocument document)
    {
        switch (document)
        {
            case World world:
                ValidateWorld(documentPath, world);
                break;
            case Ruleset ruleset:
                ValidateRuleset(documentPath, ruleset);
                break;
            case Scenario scenario:
                ValidateScenario(documentPath, scenario);
                break;
            case SaveGame save:
                ValidateSave(documentPath, save);
                break;
            case GameState state:
                ValidateState(documentPath, state);
                break;
            default:
                break;
        }
    }

    private static void ValidateWorld(string documentPath, World world)
    {
        if (world.Width <= 0 || world.Height <= 0)
        {
            throw new MalformedGameDataException(
                documentPath, $"a world's map must have a positive size; got {world.Width}×{world.Height}.");
        }

        RequireDistinct(documentPath, world.TileTypes, t => t.Id, "tileTypes");
        RequireDistinct(documentPath, world.Nations, n => n.Id, "nations");
        RequireDistinct(documentPath, world.Cities, c => c.Id, "cities");
        RequireDistinct(documentPath, world.StartingArmies, a => a.Id, "startingArmies");
        RequireDistinct(documentPath, world.StartingFleets, f => f.Id, "startingFleets");

        var codes = new HashSet<int>();
        foreach (var tileType in world.TileTypes)
        {
            if (!codes.Add(tileType.Code))
            {
                throw new MalformedGameDataException(
                    documentPath, $"two tile types share cell code {tileType.Code}.");
            }
        }

        int[] cells;
        try
        {
            cells = world.Terrain.Decode(world.Width, world.Height);
        }
        catch (InvalidOperationException ex)
        {
            throw new MalformedGameDataException(documentPath, ex.Message, ex);
        }

        foreach (var cell in cells)
        {
            if (world.TileTypeByCode(cell) is null)
            {
                throw new MalformedGameDataException(
                    documentPath, $"the terrain grid uses cell code {cell}, which no tile type defines.");
            }
        }

        foreach (var nation in world.Nations)
        {
            if (nation.CapitalCityId is { } capital && world.CityById(capital) is null)
            {
                throw new UnresolvedReferenceException(documentPath, "city", capital);
            }
        }

        foreach (var city in world.Cities)
        {
            RequireNation(documentPath, world, city.Owner);
            RequireNation(documentPath, world, city.Allegiance);
            RequireOnMap(documentPath, world, city.X, city.Y, $"city '{city.Id}'");
        }

        foreach (var army in world.StartingArmies)
        {
            RequireNation(documentPath, world, army.Nation);
            RequireOnMap(documentPath, world, army.X, army.Y, $"army '{army.Id}'");
        }

        foreach (var fleet in world.StartingFleets)
        {
            RequireNation(documentPath, world, fleet.Nation);
            RequireOnMap(documentPath, world, fleet.X, fleet.Y, $"fleet '{fleet.Id}'");
        }

        if (world.TurnOrder.Count != world.Nations.Count)
        {
            throw new MalformedGameDataException(
                documentPath,
                $"turnOrder lists {world.TurnOrder.Count} nations but the world defines {world.Nations.Count}.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var nationId in world.TurnOrder)
        {
            RequireNation(documentPath, world, nationId);
            if (!seen.Add(nationId))
            {
                throw new MalformedGameDataException(documentPath, $"turnOrder lists '{nationId}' twice.");
            }
        }
    }

    private static void ValidateRuleset(string documentPath, Ruleset ruleset)
    {
        RequireDistinct(documentPath, ruleset.UnitTypes, u => u.Id, "unitTypes");
        RequireDistinct(documentPath, ruleset.Terrain.MoveCosts, m => m.TileTypeId, "terrain.moveCosts");
        RequireDistinct(documentPath, ruleset.CityOrders.Orders, o => o.Id, "cityOrders.orders");

        var matrix = ruleset.Combat.DetailedResolver.TypeEffectiveness;
        var order = ruleset.Combat.DetailedResolver.TypeEffectivenessOrder;
        if (matrix.Count != order.Count)
        {
            throw new MalformedGameDataException(
                documentPath,
                $"combat.detailedResolver.typeEffectiveness has {matrix.Count} rows but its order lists {order.Count} types.");
        }

        foreach (var row in matrix)
        {
            if (row.Count != order.Count)
            {
                throw new MalformedGameDataException(
                    documentPath, "combat.detailedResolver.typeEffectiveness must be square.");
            }
        }

        foreach (var typeId in order)
        {
            if (ruleset.UnitTypeById(typeId) is null)
            {
                throw new UnresolvedReferenceException(documentPath, "unit type", typeId);
            }
        }
    }

    private static void ValidateScenario(string documentPath, Scenario scenario)
    {
        RequireDistinct(documentPath, scenario.Seats, s => s.Nation, "seats");

        if (scenario.Victory.Type == VictoryConditionType.Custom && string.IsNullOrWhiteSpace(scenario.Victory.Goal))
        {
            throw new MalformedGameDataException(
                documentPath, "a custom victory condition must state a \"goal\".");
        }

        foreach (var seat in scenario.Seats)
        {
            if (seat.Personality is { } personality)
            {
                RequireUnitRange(documentPath, personality.Aggression, $"seats['{seat.Nation}'].personality.aggression");
                RequireUnitRange(documentPath, personality.ExpansionDrive, $"seats['{seat.Nation}'].personality.expansionDrive");
                RequireUnitRange(documentPath, personality.LoyaltyToAlliances, $"seats['{seat.Nation}'].personality.loyaltyToAlliances");
            }
        }
    }

    private static void ValidateSave(string documentPath, SaveGame save)
    {
        ValidateState(documentPath, save.State);

        if (!string.Equals(save.WorldId, save.State.WorldId, StringComparison.Ordinal)
            || !string.Equals(save.RulesetId, save.State.RulesetId, StringComparison.Ordinal)
            || !string.Equals(save.ScenarioId, save.State.ScenarioId, StringComparison.Ordinal))
        {
            throw new MalformedGameDataException(
                documentPath, "the save's world/ruleset/scenario ids disagree with the state it carries.");
        }
    }

    private static void ValidateState(string documentPath, GameState state)
    {
        RequireDistinct(documentPath, state.Nations, n => n.Id, "state.nations");
        RequireDistinct(documentPath, state.Cities, c => c.Id, "state.cities");
        RequireDistinct(documentPath, state.Armies, a => a.Id, "state.armies");
        RequireDistinct(documentPath, state.Fleets, f => f.Id, "state.fleets");

        if (!state.Relations.IsWellFormed())
        {
            throw new MalformedGameDataException(
                documentPath, "the diplomatic relation matrix must be square, sized to its nation list, and symmetric.");
        }

        if (state.TurnOrder.Count == 0)
        {
            throw new MalformedGameDataException(documentPath, "a state must have at least one seat in its turn order.");
        }

        if (state.ActiveSeatIndex < 0 || state.ActiveSeatIndex >= state.TurnOrder.Count)
        {
            throw new MalformedGameDataException(
                documentPath,
                $"activeSeatIndex {state.ActiveSeatIndex} is outside the {state.TurnOrder.Count}-seat turn order.");
        }

        foreach (var nationId in state.TurnOrder)
        {
            if (state.NationById(nationId) is null)
            {
                throw new UnresolvedReferenceException(documentPath, "nation", nationId);
            }
        }

        foreach (var nationId in state.Relations.NationIds)
        {
            if (state.NationById(nationId) is null)
            {
                throw new UnresolvedReferenceException(documentPath, "nation", nationId);
            }
        }

        if (state.NewsLog.MostRecentSlot < -1 || state.NewsLog.MostRecentSlot >= state.NewsLog.Slots.Count)
        {
            throw new MalformedGameDataException(
                documentPath,
                $"newsLog.mostRecentSlot {state.NewsLog.MostRecentSlot} does not address one of its {state.NewsLog.Slots.Count} slots.");
        }

        foreach (var army in state.Armies)
        {
            if (army.AboardFleetId is { } fleetId && state.FleetById(fleetId) is null)
            {
                throw new UnresolvedReferenceException(documentPath, "fleet", fleetId);
            }

            if ((army.AboardFleetId is not null) != (army.CoveredTileCode is null))
            {
                throw new MalformedGameDataException(
                    documentPath,
                    $"army '{army.Id}' must cover a map tile exactly when it is not aboard a fleet.");
            }
        }

        foreach (var fleet in state.Fleets)
        {
            if (fleet.CarriedArmyId is { } armyId && state.ArmyById(armyId) is null)
            {
                throw new UnresolvedReferenceException(documentPath, "army", armyId);
            }

            if (fleet.BuildCityId is { } cityId && state.CityById(cityId) is null)
            {
                throw new UnresolvedReferenceException(documentPath, "city", cityId);
            }

            if (fleet.IsUnderConstruction && fleet.BuildCityId is null)
            {
                throw new MalformedGameDataException(
                    documentPath, $"fleet '{fleet.Id}' is under construction but names no build city.");
            }
        }
    }

    private static void RequireUnitRange(string documentPath, double value, string field)
    {
        if (value is < 0 or > 1 || double.IsNaN(value))
        {
            throw new MalformedGameDataException(
                documentPath, $"'{field}' must be between 0 and 1; got {value}.");
        }
    }

    private static void RequireNation(string documentPath, World world, string nationId)
    {
        if (world.NationById(nationId) is null)
        {
            throw new UnresolvedReferenceException(documentPath, "nation", nationId);
        }
    }

    private static void RequireOnMap(string documentPath, World world, int x, int y, string what)
    {
        if ((uint)x >= (uint)world.Width || (uint)y >= (uint)world.Height)
        {
            throw new MalformedGameDataException(
                documentPath, $"{what} sits at ({x}, {y}), outside the {world.Width}×{world.Height} map.");
        }
    }

    private static void RequireDistinct<T>(string documentPath, ValueList<T> items, Func<T, string> idSelector, string what)
    {
        if (items.FirstDuplicateId(idSelector) is { } duplicate)
        {
            throw new MalformedGameDataException(documentPath, $"{what} contains two entries with id '{duplicate}'.");
        }
    }
}
