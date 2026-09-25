using System.Collections.Generic;
using System.IO;
using System.Linq;
using IC2.Data;
using IC2.Engine.Model;
using IC2.Engine.Persistence;
using IC2.Engine.Serialization;

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
/// <strong>What is imported, since T73.</strong> The diplomatic-relation matrix and the saved turn
/// order/index are both persisted in the SAV and both parsed by <c>IC2.Data</c> (<c>NationRecord.Relations</c>,
/// <c>SaveTurnState.TurnOrder</c>/<c>TurnOrderIndex</c>) — a round-0 review of this task (PR #319, B2)
/// caught an earlier version of this remark claiming otherwise for both, when neither claim held up
/// against <c>decompiled-diplomacy-peace-terms-and-instant-battles.md</c> §"The relation matrix" and
/// <c>decompiled-sav-file-layout.md</c>'s 2026-09-14 correction. <see cref="Model.DiplomaticRelations"/>
/// and <see cref="Model.GameState.TurnOrder"/>/<see cref="Model.GameState.ActiveSeatIndex"/> below are
/// the save's own values, cell for cell and seat for seat (Done-when 11, 12) — never
/// <see cref="Model.DiplomaticRelations.Uniform"/> and never re-derived by searching <see cref="World.TurnOrder"/>
/// for the active nation.
/// </para>
/// <para>
/// <strong>What is not, and cannot be, recovered.</strong> The original SAV format has no
/// elapsed-turn-cycle counter or RNG seed that <c>IC2.Data</c> parses. Both become a <c>[designed]</c>
/// default at the moment of import, exactly mirroring the same defaults <c>GameStateFactory</c> already
/// uses for a brand-new game: the turn/nation "AtStart" scorecard fields reset to the import moment
/// (there is no way to recover the true original game-start baseline from a save already in progress),
/// and <see cref="Model.CalendarState.TurnIndex"/> starts at 0. The caller supplies the
/// <see cref="Scenario"/> (for its id and <see cref="Scenario.RandomSeed"/>) rather than this importer
/// inventing either.
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

        // "The import path IS IC2.Data, essentially unchanged" (game-design.md): every one of the eight
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
        var newsLog = SaveNewsLog.Parse(data);

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
        // uses. CityCountAtStart/Eliminated are derived the same way, deliberately overriding
        // NationRecord.CityCount/IsEliminated — see OriginalSaveFieldMapping's own remarks (review B1)
        // for why the SAV's own count can be stale. The "AtStart" scorecard fields reset to the import
        // moment — see this class's own remarks.
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
                // scenario's own seat personality, the same source GameStateFactory.CreateInitial uses
                // (review N6) — equivalent to null today only because the shipped scenario assigns none.
                Personality: scenario.SeatFor(nationId)?.Personality,
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
        // liveArmyIndices (review N1): a fleet naming an army index that is neither tombstoned nor a real
        // surviving army (corrupt or out-of-range data no corpus save has) now fails loudly instead of
        // being kept as a plausible-looking but unverified link.
        var tombstonedArmyIndices = armyTable.SkippedRecords.Select(s => s.Index).ToHashSet();
        var liveArmyIndices = armyTable.Armies.Select(a => a.Index).ToHashSet();
        // #340 N1 (Owns amendment, PR #396): a tombstoned fleet (SkippedRecords) can still claim a
        // surviving army at its own +22 -- the fleet was absorbed into another mid-turn (bug #276) and
        // compacted out, but the army it was carrying lives on. Passed to ResolveArmyAboardFleet below
        // so that army is unlinked instead of failing the import.
        var armiesClaimedByTombstonedFleets = fleetTable.SkippedRecords
            .Where(s => s.CarriedArmyIndex.HasValue)
            .Select(s => (int)s.CarriedArmyIndex!.Value)
            .ToHashSet();
        var links = EmbarkationLinker.Resolve(
            fleetTable.Fleets.Select(f => new EmbarkationLinker.FleetClaim(f.Index, f.CarriedArmyIndex)),
            liveArmyIndices,
            tombstonedArmyIndices,
            documentPath);
        var fleetCarriesArmyIndex = links.FleetCarriesArmyIndex;
        var armyCarriedByFleetIndex = links.ArmyCarriedByFleetIndex;

        // #340 N1 (Owns amendment #399): the world's own terrain, decoded once, so an army unlinked
        // from a tombstoned fleet (below) can be given the real cell it now covers, instead of the
        // stale AboardFleetSentinel its own record never had a reason to overwrite.
        var terrain = world.Terrain.Decode(world.Width, world.Height);

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
                a.Index, a.IsAboardFleet, armyCarriedByFleetIndex, FleetId, documentPath,
                armiesClaimedByTombstonedFleets);

            int? coveredTileCode;
            if (aboardFleetId is not null)
            {
                coveredTileCode = null;
            }
            else if (a.IsAboardFleet)
            {
                // #340 N1: the only fleet that ever claimed this army is tombstoned, so
                // ResolveArmyAboardFleet unlinked it above -- but the record's own CoveredCell is
                // still AboardFleetSentinel (65535): nothing in the original ever had a reason to
                // rewrite it once the carrying fleet was compacted out. The army is back on the map
                // at (a.X, a.Y); its covered tile is the terrain actually there, not the sentinel.
                coveredTileCode = CoveredTerrainCellAt(terrain, world, a.X, a.Y, documentPath);
            }
            else
            {
                coveredTileCode = a.CoveredCell;
            }

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

        // ---- Turn order (Done-when 12, review B2): the save's own 16-seat order and index, never
        // world.TurnOrder and never re-derived by searching it for the active nation. CurrentNationCode
        // is read here too, only as a defensive re-check of the identity SaveTurnState.Parse itself
        // already enforces (TurnOrder[TurnOrderIndex] == CurrentNationCode) -- cheap, and it means this
        // field is genuinely consumed rather than silently dropped (OriginalSaveFieldMapping).
        var importedTurnOrder = new string[NationCount];
        for (var i = 0; i < NationCount; i++)
        {
            importedTurnOrder[i] = NationId(turnState.TurnOrder[i]);
        }

        var activeSeatIndex = (int)turnState.TurnOrderIndex;
        var currentNationId = NationId(turnState.CurrentNationCode);
        if (!string.Equals(importedTurnOrder[activeSeatIndex], currentNationId, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"'{documentPath}': turn order at index {activeSeatIndex} names " +
                $"'{importedTurnOrder[activeSeatIndex]}', not the trailer's current nation '{currentNationId}'.");
        }

        // ---- Relations (Done-when 11, review B2): the save's own symmetric matrix, cell for cell --
        // wars, trades, alliances and negative cooldowns carried exactly as SaveNationTable.Parse already
        // validated them (symmetric, zero diagonal, MinRelationValue..MaxRelationValue). Never
        // DiplomaticRelations.Uniform, which was this importer's own earlier -- and wrong -- default.
        var relationRows = new ValueList<int>[NationCount];
        for (var code = 0; code < NationCount; code++)
        {
            var row = nationTable.Nations[code].Relations;
            var cells = new int[NationCount];
            for (var other = 0; other < NationCount; other++)
            {
                cells[other] = row[other];
            }

            relationRows[code] = ValueList<int>.Of(cells);
        }

        var relations = new DiplomaticRelations(ValueList.From(nationIds), ValueList<ValueList<int>>.Of(relationRows));

        // ---- News log (T73; Done-when 2's note): the save's own slots, oldest first, exactly as
        // SaveNewsLog.Parse already orders them -- never NewsLog.Empty, this importer's own earlier
        // default (review N6).
        var newsLogState = new NewsLog(
            MostRecentSlot: newsLog.NewestIndex,
            Slots: ValueList.From(newsLog.Slots.Select(s => new NewsEntry(s))));

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
            TurnOrder: ValueList.From(importedTurnOrder),
            ActiveSeatIndex: activeSeatIndex,
            RandomSeed: scenario.RandomSeed,
            Nations: ValueList<NationState>.Of(nations),
            Cities: ValueList<CityState>.Of(cities),
            Armies: ValueList<ArmyState>.Of(armies),
            Fleets: ValueList<FleetState>.Of(fleets),
            MercenaryPool: ValueList.From(mercenaryPool),
            Relations: relations,
            NewsLog: newsLogState,
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

        // Fail fast on any dangling reference or malformed invariant this importer's own mapping missed
        // -- the same checks SaveManager.LoadFile would run on the way back in, run here too (review N1)
        // rather than left only to a caller that happens to validate before using the result.
        GameDataValidation.Validate(documentPath, state);
        GameDataValidation.Validate(documentPath, save);

        var report = new OriginalSaveImportReport(
            // Derived from the declared mapping (review B1), not a hard-coded empty list: the only
            // entries are OriginalSaveFieldMapping's DeclaredUnmapped ones, the user's narrow waiver for
            // MercenaryRecord.X/Y (docs/tasks/T21.md "Mercenary position").
            UnmappedFields: ValueList.From(OriginalSaveFieldMapping.UnmappedFieldNames),
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

    /// <summary>#340 N1: the terrain cell at a position the raw save data itself no longer names
    /// correctly (an army unlinked from a tombstoned fleet). Mirrors
    /// <see cref="GameStateFactory"/>'s own <c>CellAt</c>, which every other position-to-terrain lookup
    /// in this project already uses for a world's starting units.</summary>
    private static int CoveredTerrainCellAt(int[] terrain, World world, int x, int y, string documentPath)
    {
        if ((uint)x >= (uint)world.Width || (uint)y >= (uint)world.Height)
        {
            throw new InvalidDataException(
                $"'{documentPath}': an army unlinked from a tombstoned fleet sits at ({x}, {y}), outside "
                + $"world '{world.Id}'s {world.Width}×{world.Height} map.");
        }

        return terrain[(y * world.Width) + x];
    }

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
}
