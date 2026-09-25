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

        // T75 (rework, Owns amendment): the load-time half of Done-when 2. Every matrix nation id is
        // checked here, the same RequireNation call every other cross-reference in this method already
        // uses -- so an unknown id surfaces as UnresolvedReferenceException, exactly as it would for a
        // city's owner or an army's nation, and World.ValidateStartingRelationsShape (called next) never
        // has to raise that case itself: by the time it runs, every id it sees is already a real nation.
        // The ruleset-dependent half (a value outside the relation codes/cooldown range, a news text over
        // the message length or holding a non-printable byte) is GameStateFactory's own check, made only
        // once a ruleset is actually available -- this method has none.
        if (world.StartingRelations is { } startingRelations)
        {
            foreach (var nationId in startingRelations.NationIds)
            {
                RequireNation(documentPath, world, nationId);
            }

            try
            {
                world.ValidateStartingRelationsShape();
            }
            catch (InvalidOperationException ex)
            {
                throw new MalformedGameDataException(documentPath, ex.Message, ex);
            }
        }

        if (world.StartingNews is not null)
        {
            try
            {
                world.ValidateStartingNewsShape();
            }
            catch (InvalidOperationException ex)
            {
                throw new MalformedGameDataException(documentPath, ex.Message, ex);
            }
        }
    }

    private static void ValidateRuleset(string documentPath, Ruleset ruleset)
    {
        ValidateCalendar(documentPath, ruleset.Calendar);
        ValidateNewsLogSeasonNames(documentPath, ruleset);
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

        // N5 (T63 review round 1): every one of these divides a live value at runtime -- a besieger's
        // casualties (deletionDivisor*), the population floor and erosion terms (siege.*), and a heavy
        // storm's whole-unit loss (naval.stormUnitLossDivisor). A 0 here is not a bad game balance
        // number, it is a DivideByZeroException the first time the call site runs, so this is a load
        // time check rather than leaving it to crash mid-turn.
        RequirePositiveDivisor(documentPath, ruleset.Combat.DeletionDivisorNational, "combat.deletionDivisorNational");
        RequirePositiveDivisor(documentPath, ruleset.Combat.DeletionDivisorMercenary, "combat.deletionDivisorMercenary");
        RequirePositiveDivisor(documentPath, ruleset.Siege.ErosionFloorDenominator, "siege.erosionFloorDenominator");
        RequirePositiveDivisor(documentPath, ruleset.Siege.ErosionCeilingDenominator, "siege.erosionCeilingDenominator");
        RequirePositiveDivisor(documentPath, ruleset.Siege.PopulationFloorDivisor, "siege.populationFloorDivisor");
        RequirePositiveDivisor(documentPath, ruleset.Naval.StormUnitLossDivisor, "naval.stormUnitLossDivisor");
    }

    /// <summary>
    /// N5 (T63 review round 1): a shared guard for the ruleset's own division-safety fields -- see the
    /// call sites in <see cref="ValidateRuleset"/> for which ones and why.
    /// </summary>
    private static void RequirePositiveDivisor(string documentPath, int value, string field)
    {
        if (value <= 0)
        {
            throw new MalformedGameDataException(
                documentPath, $"'{field}' must be greater than 0 (it is a runtime divisor); got {value}.");
        }
    }

    /// <summary>
    /// DoD 10 (folded follow-up <see href="https://github.com/diegoami/imperial_conquest_2/issues/99">#99</see>):
    /// a ruleset whose <c>newsLog.seasonNames</c> list does not have exactly one name per
    /// <c>calendar.seasonsPerYear</c> season used to crash the first time the round-tick header
    /// indexed it, rather than failing when the file was loaded. <see cref="Ruleset.ValidateSeasonNames"/>
    /// (<c>src/IC2.Engine/Model/Ruleset.cs</c>) is the check itself; this wraps its
    /// <see cref="InvalidOperationException"/> as a <see cref="MalformedGameDataException"/> naming
    /// the document, exactly like every other check in this class.
    /// </summary>
    private static void ValidateNewsLogSeasonNames(string documentPath, Ruleset ruleset)
    {
        try
        {
            ruleset.ValidateSeasonNames();
        }
        catch (InvalidOperationException ex)
        {
            throw new MalformedGameDataException(documentPath, ex.Message, ex);
        }
    }

    /// <summary>
    /// Checks that the calendar can actually turn over. The season advances only when the weekly step
    /// lands exactly on <see cref="CalendarRules.SeasonAdvanceFromWeek"/>, so a start week of the wrong
    /// parity produces a game whose season — and therefore whose year — never moves, with no visible
    /// error anywhere. That is a data mistake worth catching at load rather than months later.
    /// </summary>
    private static void ValidateCalendar(string documentPath, CalendarRules calendar)
    {
        if (calendar.WeekModulus <= 0 || calendar.WeekStep <= 0 || calendar.SeasonsPerYear <= 0)
        {
            throw new MalformedGameDataException(
                documentPath, "calendar.weekModulus, calendar.weekStep and calendar.seasonsPerYear must all be positive.");
        }

        if (calendar.StartWeek < 0 || calendar.StartWeek >= calendar.WeekModulus)
        {
            throw new MalformedGameDataException(
                documentPath,
                $"calendar.startWeek {calendar.StartWeek} is outside the 0..{calendar.WeekModulus - 1} week cycle.");
        }

        if (calendar.SeasonAdvanceFromWeek < 0 || calendar.SeasonAdvanceFromWeek >= calendar.WeekModulus)
        {
            throw new MalformedGameDataException(
                documentPath,
                $"calendar.seasonAdvanceFromWeek {calendar.SeasonAdvanceFromWeek} is outside the "
                + $"0..{calendar.WeekModulus - 1} week cycle.");
        }

        if (calendar.StartSeasonIndex < 0 || calendar.StartSeasonIndex >= calendar.SeasonsPerYear)
        {
            throw new MalformedGameDataException(
                documentPath,
                $"calendar.startSeasonIndex {calendar.StartSeasonIndex} is outside the "
                + $"0..{calendar.SeasonsPerYear - 1} season cycle.");
        }

        var week = calendar.StartWeek;
        for (var step = 0; step < calendar.WeekModulus; step++)
        {
            if (week == calendar.SeasonAdvanceFromWeek)
            {
                return;
            }

            week = (week + calendar.WeekStep) % calendar.WeekModulus;
        }

        throw new MalformedGameDataException(
            documentPath,
            $"calendar.startWeek {calendar.StartWeek} advancing by {calendar.WeekStep} never reaches "
            + $"calendar.seasonAdvanceFromWeek {calendar.SeasonAdvanceFromWeek} in a {calendar.WeekModulus}-week "
            + "cycle, so the season and the year could never advance.");
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
        // The loader checks the schema version of the root object only, so the nested state's own
        // version is checked here, beside the ids it is already cross-checked against.
        if (save.State.SchemaVersion != save.SchemaVersion)
        {
            throw new SchemaVersionMismatchException(documentPath, save.State.SchemaVersion, save.SchemaVersion);
        }

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

        // The ring buffer's index and its payload have to agree, or NewsLog.Append would be deciding
        // fullness from one and writing the other.
        if (!state.NewsLog.IsConsistent())
        {
            throw new MalformedGameDataException(
                documentPath,
                $"newsLog.mostRecentSlot {state.NewsLog.MostRecentSlot} does not address the last of its "
                + $"{state.NewsLog.Slots.Count} slots.");
        }

        // Every later system will reach for state.NationById(city.Owner) and dereference it. The world
        // loader already resolves these; a state loaded from a save has to be held to the same rule.
        foreach (var city in state.Cities)
        {
            RequireStateNation(documentPath, state, city.Owner);
            RequireStateNation(documentPath, state, city.Allegiance);
        }

        // T35: a recruitment slot's target city, and the pending offer's proposing nation, are the two
        // new cross-references this task's model additions carry.
        foreach (var nation in state.Nations)
        {
            foreach (var slot in nation.RecruitmentSlots)
            {
                if (state.CityById(slot.TargetCityId) is null)
                {
                    throw new UnresolvedReferenceException(documentPath, "city", slot.TargetCityId);
                }
            }
        }

        if (state.PendingOffer is { } pendingOffer)
        {
            RequireStateNation(documentPath, state, pendingOffer.ProposingNationId);
        }

        foreach (var army in state.Armies)
        {
            RequireStateNation(documentPath, state, army.Nation);

            if (army.AboardFleetId is { } fleetId)
            {
                var carrier = state.FleetById(fleetId)
                              ?? throw new UnresolvedReferenceException(documentPath, "fleet", fleetId);

                // The link has to be mutual, or two armies could both claim the one fleet that
                // FleetState documents as carrying a single army.
                if (!string.Equals(carrier.CarriedArmyId, army.Id, StringComparison.Ordinal))
                {
                    throw new MalformedGameDataException(
                        documentPath,
                        $"army '{army.Id}' is aboard fleet '{fleetId}', but that fleet carries "
                        + $"'{carrier.CarriedArmyId ?? "no army"}'.");
                }
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
            RequireStateNation(documentPath, state, fleet.Nation);

            if (fleet.CarriedArmyId is { } armyId)
            {
                var carried = state.ArmyById(armyId)
                              ?? throw new UnresolvedReferenceException(documentPath, "army", armyId);

                if (!string.Equals(carried.AboardFleetId, fleet.Id, StringComparison.Ordinal))
                {
                    throw new MalformedGameDataException(
                        documentPath,
                        $"fleet '{fleet.Id}' carries army '{armyId}', but that army is aboard "
                        + $"'{carried.AboardFleetId ?? "no fleet"}'.");
                }
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

    private static void RequireStateNation(string documentPath, GameState state, string nationId)
    {
        if (state.NationById(nationId) is null)
        {
            throw new UnresolvedReferenceException(documentPath, "nation", nationId);
        }
    }

    /// <remarks>
    /// The 0-1 bound is a schema bound, not a tunable rule: <see cref="AiPersonality"/>'s parameters are
    /// <em>defined</em> as fractions in <c>docs/game-design.md</c> §AI, so a value outside that range is
    /// a malformed document rather than an unusual ruleset. It is therefore deliberately not a
    /// <see cref="Ruleset"/> field.
    /// </remarks>
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
