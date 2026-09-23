using System.Collections.Generic;
using System.IO;
using System.Linq;
using IC2.Data;
using IC2.Engine.Model;
using IC2.Engine.Persistence;

namespace IC2.Engine.Import;

/// <summary>
/// Maps <c>IC2.Data</c>'s parsed original-save tables onto the new domain model — <c>docs/tasks/T21.md</c>,
/// implementing <c>docs/game-design.md</c> §"Original-save compatibility".
/// </summary>
/// <remarks>
/// <para>
/// <strong>Import policy.</strong> An imported original <c>.sav</c> always maps onto the shipped
/// <see cref="RequiredWorldId"/> World and <see cref="RequiredRulesetId"/> Ruleset — the only numbers the
/// imported state was ever balanced against. Importing onto a different World or Ruleset is rejected with
/// <see cref="SaveContextMismatchException"/>, the same typed rejection
/// <c>IC2.Engine.Persistence.SaveManager</c> uses for a native save whose recorded World/Ruleset disagrees
/// with what the caller is running under — this is the same policy, applied one step earlier, before a
/// save even exists to record an id.
/// </para>
/// <para>
/// <strong>City and nation ids are positional, not name-matched.</strong> A city's or nation's original
/// table index is a structural property of the SAV/DAT format itself: <c>WorldPrefix</c> reads the
/// identical 334-city "shared prefix" from every DAT and every SAV, in the same fixed order, and
/// <c>SaveNationTable.ParseSav</c> already rejects a save whose nation order does not match
/// <c>NationCatalog</c>'s. <c>scripts/export-classical-world.cs</c> relies on the exact same guarantee
/// to build <c>data/worlds/classical-mediterranean.json</c>'s <c>cities</c>/<c>nations</c> arrays in table
/// order, so city index <c>i</c> / nation code <c>c</c> here is <c>world.Cities[i]</c> /
/// <c>world.Nations[c]</c> there, by construction — never a name lookup, which city names cannot support
/// (some repeat; that is exactly why the exporter's own city ids need a disambiguating suffix).
/// </para>
/// <para>
/// <strong>What is not, and cannot be, recovered.</strong> The original SAV format has no persisted
/// diplomatic-relation matrix, elapsed-turn-cycle counter or RNG seed that <c>IC2.Data</c> parses — none
/// of the seven tables this importer reads carries one. Every one of these becomes a <c>[designed]</c>
/// default at the moment of import, exactly mirroring the same defaults <c>GameStateFactory</c> already
/// uses for a brand-new game: relations start at uniform peace, the turn/nation "AtStart" scorecard
/// fields reset to the import moment (there is no way to recover the true original game-start baseline
/// from a save already in progress), and <see cref="Model.CalendarState.TurnIndex"/> starts at 0. The
/// caller supplies the <see cref="Scenario"/> (for its id and <see cref="Scenario.RandomSeed"/>) rather
/// than this importer inventing either.
/// </para>
/// </remarks>
public static class OriginalSaveImporter
{
    /// <summary>The only World an original save is ever balanced against.</summary>
    public const string RequiredWorldId = "classical-mediterranean";

    /// <summary>The only Ruleset an original save is ever balanced against.</summary>
    public const string RequiredRulesetId = "classical-faithful";

    private const int NationCount = 16;

    /// <summary>
    /// Imports an original <c>.sav</c> byte-for-byte through <c>IC2.Data</c>'s own parsers, and maps every
    /// field they expose onto a live <see cref="GameState"/>.
    /// </summary>
    /// <param name="data">The raw bytes of an original <c>.sav</c> file.</param>
    /// <param name="documentPath">The file name, used only in messages.</param>
    /// <param name="world">The World to import onto — must be <see cref="RequiredWorldId"/>.</param>
    /// <param name="ruleset">The Ruleset to import onto — must be <see cref="RequiredRulesetId"/>.</param>
    /// <param name="scenario">
    /// Supplies the id and random seed the resulting <see cref="SaveGame"/> records — the original save
    /// has no seed of its own to carry over, so the caller's own scenario (ordinarily the shipped
    /// <c>classical-mediterranean</c> one) supplies both.
    /// </param>
    /// <param name="saveId">The id the resulting <see cref="SaveGame"/> carries.</param>
    /// <param name="saveLabel">The label the resulting <see cref="SaveGame"/> carries.</param>
    /// <exception cref="SaveContextMismatchException">
    /// <paramref name="world"/> or <paramref name="ruleset"/> is not the one an original save always maps
    /// onto.
    /// </exception>
    /// <exception cref="InvalidDataException">
    /// The save's own bytes are malformed, or cross-reference something inconsistently (e.g. a fleet and
    /// an army disagreeing about which one carries the other) — thrown by <c>IC2.Data</c> itself, or by
    /// this method's own cross-table checks, in the same style.
    /// </exception>
    public static OriginalSaveImportResult Import(
        byte[] data,
        string documentPath,
        World world,
        Ruleset ruleset,
        Scenario scenario,
        string saveId,
        string saveLabel)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentException.ThrowIfNullOrWhiteSpace(documentPath);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(ruleset);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveId);
        ArgumentException.ThrowIfNullOrWhiteSpace(saveLabel);

        // "Attempting to load an original save into a game already using a different Ruleset or World is
        // rejected with a clear message, not silently reinterpreted" (game-design.md). expectedId is what
        // the caller is running under; foundId is what an original save is always saved "as" — the
        // reverse of SaveManager's own native-save check, whose message this reuses verbatim.
        if (!string.Equals(world.Id, RequiredWorldId, StringComparison.Ordinal))
        {
            throw new SaveContextMismatchException(documentPath, "world", expectedId: world.Id, foundId: RequiredWorldId);
        }

        if (!string.Equals(ruleset.Id, RequiredRulesetId, StringComparison.Ordinal))
        {
            throw new SaveContextMismatchException(documentPath, "ruleset", expectedId: ruleset.Id, foundId: RequiredRulesetId);
        }

        // Positional city/nation mapping (this class's own remarks) only holds for a world shaped exactly
        // like the one the exporter built. Both counts are structural properties of RequiredWorldId, not
        // gameplay data, so a mismatch here means the caller passed a corrupted or hand-edited copy under
        // the right id — worth a clear, named failure rather than an IndexOutOfRangeException deep inside
        // the mapping below.
        if (world.Nations.Count != NationCount)
        {
            throw new InvalidDataException(
                $"'{world.Id}' defines {world.Nations.Count} nations; an original save always has {NationCount}.");
        }

        if (world.Cities.Count != WorldPrefix.CityCount)
        {
            throw new InvalidDataException(
                $"'{world.Id}' defines {world.Cities.Count} cities; an original save always has {WorldPrefix.CityCount}.");
        }

        // "The import path IS IC2.Data, essentially unchanged" (game-design.md): every one of the seven
        // parsers runs unmodified, and its own exceptions (InvalidDataException,
        // AllArmyRecordsTombstonedException, UnrecognizedSaveFormatException, DatDataNotPresentException
        // for a DAT passed here by mistake) are left to propagate rather than wrapped.
        var worldPrefix = WorldPrefix.Parse(data);
        var nationTable = SaveNationTable.Parse(data);
        var armyTable = SaveArmyTable.Parse(data);
        var fleetTable = SaveFleetTable.Parse(data);
        var recruitmentTable = SaveRecruitmentTable.Parse(data);
        var mercenaryTable = SaveMercenaryTable.Parse(data);
        var turnState = SaveTurnState.Parse(data);
        var pendingOffer = SavePendingOffer.Parse(data);

        var nationIds = new string[NationCount];
        for (var code = 0; code < NationCount; code++)
        {
            nationIds[code] = world.Nations[code].Id;
        }

        string NationId(ushort code)
        {
            if (code >= NationCount)
            {
                throw new InvalidDataException($"'{documentPath}': out-of-range nation code {code}.");
            }

            return nationIds[code];
        }

        string CityId(int index)
        {
            if ((uint)index >= (uint)world.Cities.Count)
            {
                throw new InvalidDataException($"'{documentPath}': out-of-range city index {index}.");
            }

            return world.Cities[index].Id;
        }

        // ---- Cities: positional, straight from WorldPrefix — identical shape to the exporter's own
        // CityDefinition mapping, minus the world-only Id/garrison-absence provenance. UnderSiege is
        // never derived (#220, planned as T67: nothing writes it yet).
        var cities = new CityState[worldPrefix.Cities.Count];
        for (var i = 0; i < worldPrefix.Cities.Count; i++)
        {
            var c = worldPrefix.Cities[i];
            cities[i] = new CityState(
                Id: CityId(i),
                Name: c.Name,
                X: c.X,
                Y: c.Y,
                Owner: NationId(c.OwnerCode),
                Allegiance: NationId(c.AllegianceCode),
                Loyalty: c.LoyaltyValue,
                SupplyTons: c.Supplies,
                FortificationCode: c.FortificationPercent,
                PopulationThousands: c.PopulationThousands,
                MaxPopulationThousands: c.ReferencePopulationThousands,
                Tribute: c.TributeTalents,
                UnderSiege: false,
                Garrison: ValueList<UnitSlot>.Empty);
        }

        // ---- Recruitment slots, grouped by nation, each nation's own slots kept in their original
        // table order (SaveRecruitmentTable.Parse already walks slot 0..39 in order).
        var recruitmentByNation = new List<RecruitmentSlot>[NationCount];
        for (var i = 0; i < NationCount; i++)
        {
            recruitmentByNation[i] = new List<RecruitmentSlot>();
        }

        foreach (var entry in recruitmentTable.Entries)
        {
            recruitmentByNation[entry.NationCode].Add(new RecruitmentSlot(
                TargetCityId: CityId(entry.CityIndex),
                UnitTypeId: UnitTypeIdFor(entry.TypeCode, documentPath),
                Troops: entry.Troops,
                StateCode: entry.StateCode));
        }

        // ---- Nations: every numeric field read straight from the parsed record (DoD 5: tax base and
        // wealth especially — never recomputed from the imported cities). Population has no dedicated
        // save field (same as the exporter's own NationDefinition.Population); it is summed from the
        // cities just built, the identical [derived] formula scripts/export-classical-world.cs already
        // uses. The "AtStart" scorecard fields reset to the import moment — see this class's own remarks.
        var nations = new NationState[NationCount];
        for (var code = 0; code < NationCount; code++)
        {
            var n = nationTable.Nations[code];
            var nationId = nationIds[code];

            var population = 0;
            var cityCount = 0;
            foreach (var city in cities)
            {
                if (string.Equals(city.Owner, nationId, StringComparison.Ordinal))
                {
                    population += city.PopulationThousands;
                    cityCount++;
                }
            }

            string? capitalCityId = n.CapitalCityIndex == SaveNationTable.NoCapitalSentinel
                ? null
                : CityId(n.CapitalCityIndex);

            nations[code] = new NationState(
                Id: nationId,
                Name: n.Name,
                ColorHex: world.Nations[code].ColorHex,
                LeaderName: n.Leader ?? world.Nations[code].LeaderName,
                CapitalCityId: capitalCityId,
                Control: n.HumanPlayer ? SeatControl.Human : SeatControl.Ai,
                Personality: null,
                Treasury: n.Treasury,
                Unity: n.UnityValue,
                Wealth: n.Wealth,
                TaxBase: n.TaxBase,
                TaxRatePercent: n.TaxRatePercent,
                MobilizedPercent: n.MobilizedPercent,
                Population: population,
                PopulationAtStart: population,
                TreasuryAtStart: n.Treasury,
                CityCountAtStart: cityCount,
                RecruitmentSlots: ValueList.From(recruitmentByNation[code]),
                Eliminated: cityCount == 0);
        }

        // ---- Army/fleet embarkation cross-links, resolved before either table is mapped, so a dangling
        // reference (a fleet carrying a now-compacted-out tombstoned army, or the reverse) is caught here
        // rather than silently producing a GameState GameDataValidation would reject for an opaque reason.
        // See EmbarkationLinker's own remarks for why this is a separate, independently-testable type.
        var tombstonedArmyIndices = armyTable.SkippedRecords.Select(s => s.Index).ToHashSet();
        var links = EmbarkationLinker.Resolve(
            fleetTable.Fleets.Select(f => new EmbarkationLinker.FleetClaim(f.Index, f.CarriedArmyIndex)),
            tombstonedArmyIndices,
            documentPath);
        var fleetCarriesArmyIndex = links.FleetCarriesArmyIndex;
        var armyCarriedByFleetIndex = links.ArmyCarriedByFleetIndex;

        // ---- Armies: Moves is the one field DoD 6 forbids carrying through unclamped (a negative value
        // is a genuine underflow bug in the original, never a sentinel — see ArmyRecord.Moves's own
        // remarks); every other field is a direct copy, including Morale (DoD 8: no 51..70 clamp) and
        // Money (never routed through PurseAccounting.Credit's 1,000 cap — bug #315 is exactly that
        // mistake, made by a different code path, not this one).
        var armies = new ArmyState[armyTable.Armies.Count];
        var clampedMoveArmyIds = new List<string>();
        for (var i = 0; i < armyTable.Armies.Count; i++)
        {
            var a = armyTable.Armies[i];
            var armyId = ArmyId(a.Index);

            var moves = a.Moves;
            if (moves < 0)
            {
                clampedMoveArmyIds.Add(armyId);
                moves = 0;
            }

            var aboardFleetId = EmbarkationLinker.ResolveArmyAboardFleet(
                a.Index, a.IsAboardFleet, armyCarriedByFleetIndex, FleetId, documentPath);
            int? coveredTileCode = aboardFleetId is null ? a.CoveredCell : null;

            var units = new UnitSlot[a.Units.Count];
            for (var u = 0; u < a.Units.Count; u++)
            {
                units[u] = ToUnitSlot(a.Units[u], documentPath);
            }

            armies[i] = new ArmyState(
                Id: armyId,
                Nation: NationId(a.OwnerCode),
                X: a.X,
                Y: a.Y,
                Moves: moves,
                Morale: a.Morale,
                Money: a.Money,
                SupplyTons: a.Supplies,
                CoveredTileCode: coveredTileCode,
                AboardFleetId: aboardFleetId,
                Units: ValueList.Of(units));
        }

        // ---- Fleets: ConditionPercent is [designed] 0 while under construction — the field genuinely
        // has no meaning yet (word +20 doubles as the build-city index instead; FleetRecord.ConditionPercent
        // is itself null for exactly this reason). Searched FleetTickSystem.cs for a "still building"
        // placeholder value and found none: the field is only ever assigned at launch
        // (NavalRules.LaunchConditionPercent). CoveredTileCode mirrors that: null while under
        // construction, since the fleet has no map position yet either (FleetRecord.X/Y read (0,0)).
        var fleets = new FleetState[fleetTable.Fleets.Count];
        for (var i = 0; i < fleetTable.Fleets.Count; i++)
        {
            var f = fleetTable.Fleets[i];
            var fleetId = FleetId(f.Index);

            string? buildCityId = null;
            int? constructionTicksRemaining = null;
            int? coveredTileCode = null;
            if (!f.IsLaunched)
            {
                buildCityId = f.BuildCityIndex is { } buildCityIndex ? CityId(buildCityIndex) : null;
                constructionTicksRemaining = f.ConstructionCountdown;
            }
            else
            {
                var coveredRaw = (ushort)(f.RawByteAt(24) | (f.RawByteAt(25) << 8));
                coveredTileCode = coveredRaw;
            }

            fleets[i] = new FleetState(
                Id: fleetId,
                Nation: NationId(f.OwnerCode),
                X: f.X,
                Y: f.Y,
                Moves: f.Moves,
                Ships: f.ShipCount,
                ConditionPercent: f.ConditionPercent ?? 0,
                Money: f.Money,
                SupplyTons: f.Supplies,
                ConstructionTicksRemaining: constructionTicksRemaining,
                BuildCityId: buildCityId,
                CarriedArmyId: fleetCarriesArmyIndex.TryGetValue(f.Index, out var carriedArmyIndex)
                    ? ArmyId(carriedArmyIndex)
                    : null,
                CoveredTileCode: coveredTileCode);
        }

        // ---- Mercenary pool: only occupied slots (SaveMercenaryTable.Parse always returns all 50; an
        // empty one carries the consumed/never-offered sentinel or a zero troop count).
        var mercenaryPool = new List<MercenaryPoolSlot>();
        foreach (var m in mercenaryTable.Records)
        {
            if (m.IsEmpty)
            {
                continue;
            }

            mercenaryPool.Add(new MercenaryPoolSlot(
                SlotIndex: m.Index,
                NameLabel: m.Label,
                UnitTypeId: UnitTypeIdFor(m.TypeCode, documentPath),
                Troops: m.Troops,
                Quality: m.QualityCode));
        }

        // ---- Calendar: Week/SeasonIndex/YearBc read straight from the trailer. TurnIndex has no source
        // field at all (this class's own remarks) and starts at 0.
        var calendar = new CalendarState(
            Week: turnState.Week,
            SeasonIndex: turnState.SeasonCode,
            YearBc: turnState.YearBc,
            TurnIndex: 0);

        var activeNationId = NationId(turnState.CurrentNationCode);
        var activeSeatIndex = IndexInTurnOrder(world, activeNationId, documentPath);

        var pendingOfferState = pendingOffer.HasOffer
            ? new PendingDiplomaticOffer(
                ProposingNationId: NationId(pendingOffer.ProposingNationIndex),
                ProposedRelationCode: pendingOffer.ProposedRelationState)
            : null;

        var state = new GameState(
            SchemaVersion: GameDataSchema.CurrentVersion,
            WorldId: world.Id,
            RulesetId: ruleset.Id,
            ScenarioId: scenario.Id,
            Calendar: calendar,
            TurnOrder: world.TurnOrder,
            ActiveSeatIndex: activeSeatIndex,
            RandomSeed: scenario.RandomSeed,
            Nations: ValueList<NationState>.Of(nations),
            Cities: ValueList<CityState>.Of(cities),
            Armies: ValueList<ArmyState>.Of(armies),
            Fleets: ValueList<FleetState>.Of(fleets),
            MercenaryPool: ValueList.From(mercenaryPool),
            Relations: DiplomaticRelations.Uniform(ValueList.From(nationIds), ruleset.Diplomacy.StateCodes.Peace),
            NewsLog: NewsLog.Empty,
            PendingOffer: pendingOfferState);

        var save = new SaveGame(
            SchemaVersion: GameDataSchema.CurrentVersion,
            Id: saveId,
            Label: saveLabel,
            ScenarioId: scenario.Id,
            WorldId: world.Id,
            RulesetId: ruleset.Id,
            State: state,
            Provenance: ProvenanceMap.Of(
                ("state", $"confirmed: imported from original save '{documentPath}' through IC2.Data's own " +
                          "parsers (T30, T34, T44, T64), unchanged -- docs/game-design.md " +
                          "§\"Original-save compatibility\".")));

        var report = new OriginalSaveImportReport(
            UnmappedFields: ValueList<string>.Empty,
            SkippedArmies: ValueList.From(armyTable.SkippedRecords.Select(s => new SkippedRecordReport(s.Index, s.X, s.Y))),
            SkippedFleets: ValueList.From(fleetTable.SkippedRecords.Select(s => new SkippedRecordReport(s.Index, s.X, s.Y))),
            ArmiesWithClampedMoves: ValueList.From(clampedMoveArmyIds),
            NationsImported: nations.Length,
            CitiesImported: cities.Length,
            ArmiesImported: armies.Length,
            FleetsImported: fleets.Length,
            RecruitmentSlotsImported: recruitmentTable.Entries.Count,
            MercenarySlotsImported: mercenaryPool.Count);

        return new OriginalSaveImportResult(save, report);
    }

    /// <summary>Army id convention: matches <c>scripts/export-classical-world.cs</c>'s own
    /// <c>StartingArmy</c> ids exactly (<c>"army-{index}"</c>), so an army's id is stable and traceable to
    /// its original table slot whether it came from the shipped World or from an imported save.</summary>
    private static string ArmyId(int index) => $"army-{index}";

    /// <inheritdoc cref="ArmyId"/>
    private static string FleetId(int index) => $"fleet-{index}";

    private static UnitSlot ToUnitSlot(ArmyUnit unit, string documentPath) => new(
        MercenaryLabel: unit.MercenaryLabel,
        UnitTypeId: UnitTypeIdFor(unit.TypeCode, documentPath),
        Troops: unit.Troops,
        Quality: unit.QualityCode,
        Name: unit.Name);

    /// <summary>The same unit-type code table <c>scripts/export-classical-world.cs</c>'s own
    /// <c>UnitTypeIdFor</c> uses, and <c>IC2.Data.UnitCatalog.TypeName</c> corroborates by label —
    /// established, reviewed mapping, not a new one.</summary>
    private static string UnitTypeIdFor(ushort typeCode, string documentPath) => typeCode switch
    {
        0 => "light_infantry",
        1 => "heavy_infantry",
        2 => "archers",
        3 => "light_cavalry",
        4 => "heavy_cavalry",
        _ => throw new InvalidDataException($"'{documentPath}': unknown unit type code {typeCode}."),
    };

    private static int IndexInTurnOrder(World world, string nationId, string documentPath)
    {
        for (var i = 0; i < world.TurnOrder.Count; i++)
        {
            if (string.Equals(world.TurnOrder[i], nationId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new InvalidDataException(
            $"'{documentPath}': nation '{nationId}' is not present in world '{world.Id}''s turn order.");
    }
}
