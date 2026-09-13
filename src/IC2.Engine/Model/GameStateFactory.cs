namespace IC2.Engine.Model;

/// <summary>
/// Builds the initial <see cref="GameState"/> a <see cref="Scenario"/> describes, by materialising the
/// world's starting data against the scenario's seat assignments.
/// </summary>
/// <remarks>
/// This is pure data materialisation, not a rule: no value is computed, defaulted or rolled here. The
/// only thing it reads from the <see cref="Ruleset"/> is the numeric code that means "at peace", so
/// the starting relation matrix is built from ruleset data rather than a literal.
/// </remarks>
public static class GameStateFactory
{
    /// <summary>Creates the starting state for a scenario.</summary>
    /// <exception cref="ArgumentException">
    /// The scenario and world disagree: the scenario seats a nation the world does not define, or the
    /// world defines a nation the scenario gives no seat, or a starting unit sits off the map.
    /// Duplicate ids are not re-checked here — <see cref="IC2.Engine.Serialization.GameDataValidation"/>
    /// rejects those when the world is loaded.
    /// </exception>
    public static GameState CreateInitial(World world, Ruleset ruleset, Scenario scenario)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(scenario);

        foreach (var seat in scenario.Seats)
        {
            if (world.NationById(seat.Nation) is null)
            {
                throw new ArgumentException(
                    $"Scenario '{scenario.Id}' seats nation '{seat.Nation}', which world '{world.Id}' does not define.",
                    nameof(scenario));
            }
        }

        var nations = new NationState[world.Nations.Count];
        for (var i = 0; i < world.Nations.Count; i++)
        {
            var definition = world.Nations[i];
            var seat = scenario.SeatFor(definition.Id)
                       ?? throw new ArgumentException(
                           $"Scenario '{scenario.Id}' assigns no seat to nation '{definition.Id}'. "
                           + "Every nation in the world needs an explicit seat; the engine never defaults one.",
                           nameof(scenario));

            var cityCount = 0;
            foreach (var city in world.Cities)
            {
                if (string.Equals(city.Owner, definition.Id, StringComparison.Ordinal))
                {
                    cityCount++;
                }
            }

            nations[i] = new NationState(
                Id: definition.Id,
                Name: definition.Name,
                ColorHex: definition.ColorHex,
                LeaderName: definition.LeaderName,
                CapitalCityId: definition.CapitalCityId,
                Control: seat.Control,
                Personality: seat.Personality,
                Treasury: definition.Treasury,
                Unity: definition.Unity,
                Wealth: definition.Wealth,
                TaxRatePercent: definition.TaxRatePercent,
                Population: definition.Population,
                PopulationAtStart: definition.Population,
                TreasuryAtStart: definition.Treasury,
                CityCountAtStart: cityCount,
                Eliminated: cityCount == 0);
        }

        var cities = new CityState[world.Cities.Count];
        for (var i = 0; i < world.Cities.Count; i++)
        {
            var definition = world.Cities[i];
            cities[i] = new CityState(
                Id: definition.Id,
                Name: definition.Name,
                X: definition.X,
                Y: definition.Y,
                Owner: definition.Owner,
                Allegiance: definition.Allegiance,
                Loyalty: definition.Loyalty,
                SupplyTons: definition.SupplyTons,
                FortificationCode: definition.FortificationCode,
                PopulationThousands: definition.PopulationThousands,
                MaxPopulationThousands: definition.MaxPopulationThousands,
                Tribute: definition.Tribute,
                UnderSiege: false,
                Garrison: definition.Garrison);
        }

        var terrain = world.Terrain.Decode(world.Width, world.Height);

        var armies = new ArmyState[world.StartingArmies.Count];
        for (var i = 0; i < world.StartingArmies.Count; i++)
        {
            var definition = world.StartingArmies[i];
            armies[i] = new ArmyState(
                Id: definition.Id,
                Nation: definition.Nation,
                X: definition.X,
                Y: definition.Y,
                Moves: definition.Moves,
                Morale: definition.Morale,
                Money: definition.Money,
                SupplyTons: definition.SupplyTons,
                CoveredTileCode: CellAt(terrain, world, definition.X, definition.Y),
                AboardFleetId: null,
                Units: definition.Units);
        }

        var fleets = new FleetState[world.StartingFleets.Count];
        for (var i = 0; i < world.StartingFleets.Count; i++)
        {
            var definition = world.StartingFleets[i];
            fleets[i] = new FleetState(
                Id: definition.Id,
                Nation: definition.Nation,
                X: definition.X,
                Y: definition.Y,
                Moves: definition.Moves,
                Ships: definition.Ships,
                ConditionPercent: definition.ConditionPercent,
                Money: definition.Money,
                SupplyTons: definition.SupplyTons,
                ConstructionTicksRemaining: null,
                BuildCityId: null,
                CarriedArmyId: null,
                CoveredTileCode: CellAt(terrain, world, definition.X, definition.Y));
        }

        // Every field here is read from the ruleset, including the starting week. Seeding it from
        // anything else is not a cosmetic choice: with the confirmed `week = (week + 2) mod 12` cycle,
        // only an odd start ever reaches the season boundary, so a wrong start week silently freezes
        // the season and the year for the whole game.
        var calendar = new CalendarState(
            Week: ruleset.Calendar.StartWeek,
            SeasonIndex: ruleset.Calendar.StartSeasonIndex,
            YearBc: ruleset.Calendar.StartYearBc,
            TurnIndex: 0);

        return new GameState(
            SchemaVersion: GameDataSchema.CurrentVersion,
            WorldId: world.Id,
            RulesetId: ruleset.Id,
            ScenarioId: scenario.Id,
            Calendar: calendar,
            TurnOrder: world.TurnOrder,
            ActiveSeatIndex: 0,
            RandomSeed: scenario.RandomSeed,
            Nations: ValueList<NationState>.Of(nations),
            Cities: ValueList<CityState>.Of(cities),
            Armies: ValueList<ArmyState>.Of(armies),
            Fleets: ValueList<FleetState>.Of(fleets),
            MercenaryPool: ValueList<MercenaryPoolSlot>.Empty,
            Relations: DiplomaticRelations.Uniform(
                ValueList.From(nations.Select(n => n.Id)),
                ruleset.Diplomacy.StateCodes.Peace),
            NewsLog: NewsLog.Empty);
    }

    private static int CellAt(int[] terrain, World world, int x, int y)
    {
        if ((uint)x >= (uint)world.Width || (uint)y >= (uint)world.Height)
        {
            throw new ArgumentException(
                $"World '{world.Id}' places a unit at ({x}, {y}), outside its {world.Width}×{world.Height} map.",
                nameof(world));
        }

        return terrain[(y * world.Width) + x];
    }
}
