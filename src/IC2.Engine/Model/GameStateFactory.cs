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

            // Follow-up #106 item 1: this duplicates VictoryEvaluator's own private city-count scan, but
            // over World.Cities (ValueList<CityDefinition>) rather than GameState.Cities
            // (ValueList<CityState>) -- there is no scenario state yet at this point, only the world
            // definition. GameState.CountCitiesOwnedBy (added alongside the quarterly treasury credit's
            // own need for a live city count) covers the GameState-Cities half of that duplication;
            // unifying this loop with it too would need a shared element-agnostic abstraction over both
            // record types, which reaches into src/IC2.Engine/Victory/** (VictoryEvaluator's own file),
            // outside this task's Owns list. Left as its own loop rather than half-fixed.
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
                TaxBase: definition.TaxBase,
                TaxRatePercent: definition.TaxRatePercent,
                MobilizedPercent: definition.MobilizedPercent,
                Population: definition.Population,
                PopulationAtStart: definition.Population,
                TreasuryAtStart: definition.Treasury,
                CityCountAtStart: cityCount,
                RecruitmentSlots: ValueList<RecruitmentSlot>.Empty,
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
            Relations: StartingRelationsFor(world, ruleset, nations),
            NewsLog: StartingNewsFor(world, ruleset),
            PendingOffer: null);
    }

    /// <summary>
    /// The starting relation matrix: the world's own <see cref="World.StartingRelations"/> when it
    /// carries one (T75 — the DAT's matrix, kept verbatim, no cooldowns rolled forward), otherwise
    /// uniform peace exactly as before this field existed.
    /// </summary>
    /// <remarks>
    /// T75 Done-when 2's ruleset-dependent half: <see cref="World.ValidateStartingRelationsShape"/> (run
    /// at load, no ruleset available) already guarantees the matrix is well-formed and keyed to exactly
    /// this world's nations; the one check that needs a <see cref="Ruleset"/> — every value is one of the
    /// ruleset's own relation codes or a cooldown within its range — can only run here, once a ruleset
    /// actually exists.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// A matrix cell is neither one of <paramref name="ruleset"/>'s relation codes nor a cooldown within
    /// its configured range.
    /// </exception>
    private static DiplomaticRelations StartingRelationsFor(World world, Ruleset ruleset, NationState[] nations)
    {
        if (world.StartingRelations is { } startingRelations)
        {
            ValidateStartingRelationValues(world, startingRelations, ruleset);
            return startingRelations;
        }

        return DiplomaticRelations.Uniform(
            ValueList.From(nations.Select(n => n.Id)),
            ruleset.Diplomacy.StateCodes.Peace);
    }

    /// <summary>
    /// Checks every cell against the ruleset's own relation codes explicitly, rather than assuming the
    /// codes are a contiguous <c>0..max</c> range: a ruleset whose codes are not contiguous would
    /// otherwise let a gap value through (T75 rework N2).
    /// </summary>
    /// <param name="world">
    /// Named only so a bad value's exception can name it (review round 1, B2, Owns amendment #399):
    /// the bad data is <paramref name="world"/>'s own <c>startingRelations</c>, never
    /// <paramref name="ruleset"/>, which only supplies the codes/cooldowns to check against.
    /// </param>
    private static void ValidateStartingRelationValues(World world, DiplomaticRelations relations, Ruleset ruleset)
    {
        var codes = ruleset.Diplomacy.StateCodes;
        var validCodes = new HashSet<int> { codes.Peace, codes.Trade, codes.Alliance, codes.War };
        var minCooldown = Math.Min(
            Math.Min(ruleset.Diplomacy.CooldownAfterBrokenTrade, ruleset.Diplomacy.CooldownAfterBrokenAlliance),
            Math.Min(
                ruleset.Diplomacy.CooldownAfterEndedWar,
                Math.Min(ruleset.Diplomacy.CooldownAfterPeaceTerms, ruleset.Diplomacy.CooldownAfterAllyPeace)));

        foreach (var row in relations.Matrix)
        {
            foreach (var value in row)
            {
                var isKnownCode = validCodes.Contains(value);
                var isCooldown = value < 0 && value >= minCooldown;
                if (!isKnownCode && !isCooldown)
                {
                    throw new ArgumentException(
                        $"startingRelations has a value {value}, which is neither one of the ruleset's "
                        + $"relation codes ({string.Join(", ", validCodes.OrderBy(v => v))}) nor a "
                        + $"cooldown in [{minCooldown}, -1].",
                        nameof(world));
                }
            }
        }
    }

    /// <summary>
    /// The starting news log: the world's own <see cref="World.StartingNews"/> when it carries one (T75
    /// — the DAT's 27-line seed, kept verbatim), otherwise an empty log exactly as before this field
    /// existed.
    /// </summary>
    /// <remarks>
    /// T75 Done-when 2's ruleset-dependent half: <see cref="World.ValidateStartingNewsShape"/> (run at
    /// load, no ruleset available) already guarantees <c>mostRecentSlot</c> addresses the last slot; the
    /// two checks that need a <see cref="Ruleset"/> — a slot's text within the message-length budget,
    /// counted in bytes, and holding only printable bytes — can only run here.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// A slot's text holds a byte outside the printable 0x20-0x7E range the news writer and the DAT
    /// parser both accept, or exceeds <paramref name="ruleset"/>'s message length once counted in bytes.
    /// </exception>
    /// <exception cref="IC2.Engine.Serialization.MalformedGameDataException">
    /// Follow-up <see href="https://github.com/diegoami/imperial_conquest_2/issues/372">#372</see> N6
    /// (the user's decision of 2026-09-25): <paramref name="world"/>'s starting news seed carries more
    /// slots than <paramref name="ruleset"/>'s own ring buffer holds. <see cref="World.ValidateStartingNewsShape"/>
    /// cannot catch this at load time — it has no <see cref="Ruleset"/> to know the ring's capacity from
    /// — so a seed this long would otherwise pass state validation and be silently trimmed down to size
    /// only on the very first write (<see cref="NewsLog.Append"/>'s own eviction), rather than rejected
    /// up front like every other malformed starting-data shape.
    /// </exception>
    private static NewsLog StartingNewsFor(World world, Ruleset ruleset)
    {
        if (world.StartingNews is not { } startingNews)
        {
            return NewsLog.Empty;
        }

        if (startingNews.Slots.Count > ruleset.NewsLog.RingBufferSlots)
        {
            throw new IC2.Engine.Serialization.MalformedGameDataException(
                world.Id,
                $"startingNews has {startingNews.Slots.Count} slots, over the ruleset's "
                + $"{ruleset.NewsLog.RingBufferSlots}-slot ring buffer.");
        }

        // The printable-byte check runs first, over every character, before any length is compared:
        // "length in bytes" only equals string.Length once every character is confirmed single-byte
        // ASCII (T75 rework B4) -- the same invariant SaveNewsLog.ReadSlotText and
        // NewsLogWriter.TruncateToByteLength both enforce on the DAT parse and the news writer's own
        // output, so a slot that violates it is rejected before its "length" is trusted as a byte count.
        var maxTextBytes = ruleset.NewsLog.MessageByteLength - 1;
        foreach (var slot in startingNews.Slots)
        {
            foreach (var ch in slot.Text)
            {
                if (ch is < (char)0x20 or > (char)0x7E)
                {
                    // #372 N7: ch is a UTF-16 code unit (System.Char), not a byte -- "character", not
                    // "byte", is what this is actually counting.
                    throw new ArgumentException(
                        $"startingNews has a slot text with a character outside the printable "
                        + $"0x20-0x7E range: 0x{(int)ch:X2}.",
                        nameof(world));
                }
            }

            if (slot.Text.Length > maxTextBytes)
            {
                // #372 N7: slot.Text.Length counts UTF-16 code units too; the printable check just
                // above is what makes that number equal a byte count here, but the value itself is a
                // character count, so it is named as one.
                throw new ArgumentException(
                    $"startingNews has a slot text of {slot.Text.Length} characters, over the ruleset's "
                    + $"{maxTextBytes}-byte limit.",
                    nameof(world));
            }
        }

        return startingNews;
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
