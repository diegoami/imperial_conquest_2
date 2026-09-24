#:project ../src/IC2.Data/IC2.Data.csproj
#:project ../src/IC2.Engine/IC2.Engine.csproj
#:property JsonSerializerIsReflectionEnabledByDefault=true

// T29: one-shot, re-runnable export of the shipped classical-mediterranean world, the
// classical-faithful ruleset, and the scenario that starts the original game on that world.
//
// This is the ONLY step in the whole build that reads the user's original game files
// (docs/task-catalogue.md, "T29 Export the shipped classical-mediterranean world and ruleset").
// Every later build, test and play session uses the three committed JSON files this script
// writes, never the original DAT again.
//
// Usage:
//   dotnet run scripts/export-classical-world.cs [path-to-assets.local.ini]
// With no argument, it looks for assets.local.ini at the repository root (this file's parent
// directory). The DAT itself is never committed; only the JSON this script writes is.
//
// Re-running this script against the same DAT must produce byte-identical output
// (docs/task-catalogue.md T29 DoD 5, the same pattern as T11's asset generator). Every value
// below is either read straight from the DAT through IC2.Data's parsers (T30), transcribed from
// the T04 fixtures corpus (tests/fixtures/corpus.json), or an explicitly [designed] placeholder
// with its own _provenance note — nothing is invented, and nothing is hand-edited into the
// committed JSON afterwards. If a mismatch is ever found in the committed files, the fix is a
// change to this script followed by a re-run, never a direct edit to the JSON
// (docs/task-catalogue.md T29 Hazards).

using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using IC2.Data;
using IC2.Engine.Model;
using IC2.Engine.Serialization;

var scriptDir = AppContext.BaseDirectory; // not reliable for file-based apps; use source path instead
var repoRoot = Path.GetFullPath(Path.Combine(FindThisFileDirectory(), ".."));

var iniPath = args.Length > 0 ? args[0] : Path.Combine(repoRoot, "assets.local.ini");
Console.WriteLine($"Repository root: {repoRoot}");
Console.WriteLine($"assets.local.ini: {iniPath}");

var settings = AssetSettings.Load(iniPath);
var datBytes = File.ReadAllBytes(settings.DatPath);
Console.WriteLine($"Read DAT: {settings.DatPath} ({datBytes.Length} bytes)");

// ============================================================================================
// 1. Parse the DAT through IC2.Data's own parsers (T30). No byte-level parsing happens here:
//    every field below comes from a public IC2.Data type, so a DAT-layout correction only ever
//    needs a change there, never here.
// ============================================================================================
var prefix = WorldPrefix.Parse(datBytes);
var nationTable = SaveNationTable.Parse(datBytes);
var armyTable = SaveArmyTable.Parse(datBytes);
var fleetTable = SaveFleetTable.Parse(datBytes);

// ---- DoD 1: cross-check against IC2.Data's own parse, never re-derived independently -------
// WorldPrefix.Parse and SaveNationTable.Parse are exactly "T30's DAT nation-table parse" the
// task entry names; NationCatalog is the cross-check, not the source of record. The Detect/Parse
// chain already refuses to load a differently-ordered or differently-named table (SaveNationTable
// .ParseDat throws if a name disagrees with NationCatalog), so this loop is a second, explicit,
// non-tautological check over the exported values themselves, not a repeat of the parser's own
// internal guard.
if (prefix.Cities.Count != WorldPrefix.CityCount)
    throw new InvalidOperationException($"Expected {WorldPrefix.CityCount} cities; DAT parse gave {prefix.Cities.Count}.");
if (WorldPrefix.MapWidth != 320 || WorldPrefix.MapHeight != 140)
    throw new InvalidOperationException("The confirmed 320x140 map size has changed in IC2.Data; T29 must be re-checked against the investigation before re-running.");
if (nationTable.Nations.Count != 16)
    throw new InvalidOperationException($"Expected 16 nations; DAT parse gave {nationTable.Nations.Count}.");
for (ushort i = 0; i < nationTable.Nations.Count; i++)
{
    var expected = NationCatalog.Name(i);
    var actual = nationTable.Nations[i].Name;
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"Nation {i}: DAT parse gives '{actual}' but NationCatalog gives '{expected}'. " +
            "Per T29's hazard, this is an escalation, not a fallback to NationCatalog's list.");
    }
}
Console.WriteLine($"DoD 1 cross-check OK: {prefix.Cities.Count} cities, {nationTable.Nations.Count} nations, {WorldPrefix.MapWidth}x{WorldPrefix.MapHeight} map, names match NationCatalog in order.");

// ============================================================================================
// 2. Ids. Deterministic, derived only from DAT content (never a random or ordering-dependent
//    choice), so a re-run assigns exactly the same ids.
// ============================================================================================
string Slug(string name)
{
    var chars = name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
    var s = new string(chars);
    while (s.Contains("--")) s = s.Replace("--", "-");
    return s.Trim('-');
}

var nationIds = nationTable.Nations.Select(n => Slug(n.Name)).ToArray();
if (nationIds.Distinct(StringComparer.Ordinal).Count() != nationIds.Length)
    throw new InvalidOperationException("Two nations produced the same slug id.");

// City ids: slug the name, disambiguating a repeated slug with a stable numeric suffix in city
// table order (the DAT's own order, so a re-run always assigns the same suffix).
var cityIds = new string[prefix.Cities.Count];
var slugCounts = new Dictionary<string, int>(StringComparer.Ordinal);
for (var i = 0; i < prefix.Cities.Count; i++)
{
    var baseSlug = Slug(prefix.Cities[i].Name);
    var count = slugCounts.GetValueOrDefault(baseSlug, 0) + 1;
    slugCounts[baseSlug] = count;
    cityIds[i] = count == 1 ? baseSlug : $"{baseSlug}-{count}";
}
if (cityIds.Distinct(StringComparer.Ordinal).Count() != cityIds.Length)
    throw new InvalidOperationException("Two cities produced the same id after disambiguation.");

string NationId(ushort code) => nationIds[code];
string CityId(int index) => cityIds[index];

// ============================================================================================
// 3. Terrain grid: row-major cell codes, recovered from the DAT's map array.
//
//    The raw map array doubles as a render buffer: at a city or a launched unit's own tile it
//    stores that entity's marker code (confirmed empirically below), not the terrain underneath.
//    An army or fleet record carries its own "covered cell" word precisely so the original can
//    restore the terrain when the unit leaves (ArmyRecord.CoveredCell's own doc comment;
//    docs/investigations/dat-file-layout.md). This script uses that field to recover true terrain
//    at every starting army's and fleet's tile.
//
//    A city has no such restore field: nothing in the 34-byte city record
//    (docs/investigations/dat-file-layout.md) stores a pre-city terrain code, and a city's tile
//    is permanently overwritten by its own marker in the shipped DAT. [designed]: searched
//    CityRecord's own fields and dat-file-layout.md's field table for a stored terrain-under-city
//    value and found none, so every city tile defaults to "plain" (code 2) — the commonest, and
//    cheapest, terrain in the confirmed move-cost table. This is a known, bounded gap (334 of
//    44,800 cells, 0.75%) that the export cannot close from the DAT alone.
// ============================================================================================
const int PlainTerrainCode = 2;

var armyPositions = armyTable.Armies.ToDictionary(a => ((int)a.X, (int)a.Y), a => (int)a.CoveredCell);
var fleetPositions = new Dictionary<(int, int), int>();
foreach (var f in fleetTable.Fleets)
{
    var covered = f.RawByteAt(24) | (f.RawByteAt(25) << 8);
    fleetPositions[((int)f.X, (int)f.Y)] = covered;
}
var cityPositions = new HashSet<(int, int)>(prefix.Cities.Select(c => ((int)c.X, (int)c.Y)));

// Sanity: DoD 7's army/fleet tiles must not double as a city tile (would make "restore from
// CoveredCell" and "default city terrain" disagree about the same cell). Confirmed empirically
// disjoint for the shipped DAT; guarded here so a different DAT fails loudly instead of silently
// picking one value over the other.
foreach (var pos in armyPositions.Keys)
    if (cityPositions.Contains(pos))
        throw new InvalidOperationException($"Army at {pos} sits on a city tile; terrain recovery is ambiguous.");
foreach (var pos in fleetPositions.Keys)
    if (cityPositions.Contains(pos))
        throw new InvalidOperationException($"Fleet at {pos} sits on a city tile; terrain recovery is ambiguous.");

var cellCodes = new ushort[WorldPrefix.MapWidth * WorldPrefix.MapHeight]; // row-major: index = y*width + x
var recoveredCount = 0;
var cityDefaultedCount = 0;
for (var y = 0; y < WorldPrefix.MapHeight; y++)
{
    for (var x = 0; x < WorldPrefix.MapWidth; x++)
    {
        var raw = prefix.CellAt(x, y);
        ushort resolved;
        if (armyPositions.TryGetValue((x, y), out var armyCovered))
        {
            resolved = (ushort)armyCovered;
            recoveredCount++;
        }
        else if (fleetPositions.TryGetValue((x, y), out var fleetCovered))
        {
            resolved = (ushort)fleetCovered;
            recoveredCount++;
        }
        else if (cityPositions.Contains((x, y)))
        {
            resolved = PlainTerrainCode;
            cityDefaultedCount++;
        }
        else
        {
            resolved = raw;
        }

        if (resolved > 11)
            throw new InvalidOperationException($"Resolved terrain code {resolved} at ({x},{y}) is outside the confirmed 0..11 terrain table.");

        cellCodes[(y * WorldPrefix.MapWidth) + x] = resolved;
    }
}
Console.WriteLine($"Terrain: {recoveredCount} cells recovered from a unit's own CoveredCell, {cityDefaultedCount} city cells defaulted to plain [designed].");

var terrainBytes = new byte[cellCodes.Length * 2];
for (var i = 0; i < cellCodes.Length; i++)
    BinaryPrimitives.WriteUInt16LittleEndian(terrainBytes.AsSpan(i * 2, 2), cellCodes[i]);

// Kept as an ordinary inline-Data grid for the rest of this script: GameDataValidation.Validate
// below and GameStateFactory.CreateInitial both call TerrainGrid.Decode directly on this in-memory
// value, before anything is written to disk, so there is no sidecar file yet to resolve one from.
// Step 11 below is what actually splits this into the shipped world.json + sidecar pair (T62).
var terrainGrid = new TerrainGrid(TerrainEncoding.Base64, Runs: null, Data: Convert.ToBase64String(terrainBytes));

// ============================================================================================
// 4. Tile types: the confirmed 12-entry terrain table (DAT offset 0x1F622), the same one
//    data/worlds/toy-3city.json already ships — this table is a property of the game, not of any
//    one world, so it is transcribed here rather than re-derived.
// ============================================================================================
var tileTypes = ValueList.Of(
    TileType("sea_coastal", 0, "Sea", passableByArmies: false, passableByFleets: true,
        "confirmed: terrain-move-cost-table-in-dat.md -- cell code 0, named 'Sea' in the DAT terrain table at 0x1F622; corpus id terrain.moveCost.code0."),
    TileType("sea_deep", 1, "Sea", passableByArmies: false, passableByFleets: true,
        "confirmed: terrain-move-cost-table-in-dat.md -- cell code 1, a second 'Sea' entry distinguished only by its higher move cost; corpus id terrain.moveCost.code1."),
    TileType("plain", 2, "Plain", passableByArmies: true, passableByFleets: false,
        "confirmed: terrain-move-cost-table-in-dat.md; corpus id terrain.moveCost.code2."),
    TileType("desert", 3, "Desert", passableByArmies: true, passableByFleets: false,
        "confirmed: terrain-move-cost-table-in-dat.md; corpus id terrain.moveCost.code3."),
    TileType("forest", 4, "Forest", passableByArmies: true, passableByFleets: false,
        "confirmed: terrain-move-cost-table-in-dat.md; corpus id terrain.moveCost.code4."),
    TileType("mountains", 5, "Mountains", passableByArmies: true, passableByFleets: false,
        "confirmed: terrain-move-cost-table-in-dat.md; corpus id terrain.moveCost.code5."),
    TileType("river_1", 6, "River", passableByArmies: true, passableByFleets: false,
        "confirmed: terrain-move-cost-table-in-dat.md -- six distinct river codes, 6..11, all named 'River'; corpus id terrain.moveCost.code6."),
    TileType("river_2", 7, "River", passableByArmies: true, passableByFleets: false,
        "confirmed: terrain-move-cost-table-in-dat.md; corpus id terrain.moveCost.code7."),
    TileType("river_3", 8, "River", passableByArmies: true, passableByFleets: false,
        "confirmed: terrain-move-cost-table-in-dat.md; corpus id terrain.moveCost.code8."),
    TileType("river_4", 9, "River", passableByArmies: true, passableByFleets: false,
        "confirmed: terrain-move-cost-table-in-dat.md; corpus id terrain.moveCost.code9."),
    TileType("river_5", 10, "River", passableByArmies: true, passableByFleets: false,
        "confirmed: terrain-move-cost-table-in-dat.md; corpus id terrain.moveCost.code10."),
    TileType("river_6", 11, "River", passableByArmies: true, passableByFleets: false,
        "confirmed: terrain-move-cost-table-in-dat.md; corpus id terrain.moveCost.code11."));

static TileType TileType(string id, int code, string name, bool passableByArmies, bool passableByFleets, string note) =>
    new(id, code, name, passableByArmies, passableByFleets,
        ProvenanceMap.Of(("code", note)));

// ============================================================================================
// 5. Nations. Every numeric field is the DAT nation record, taken straight from
//    SaveNationTable.Parse (T30) -- DoD 9's tax base is exported as stored, never recomputed.
//    Leader names and the human-player flag are New Game state, not world data (DoD 2): every
//    nation gets an explicit, non-DAT placeholder leader name, and no field here claims DAT
//    provenance for it.
// ============================================================================================
// A 16-way palette, chosen to be pairwise distinct (godot/MapViewer.cs's own palette has two
// byte-identical pairs and a near-duplicate -- issue #154 -- so this export does not reuse it).
// [designed]: no report records an original in-game nation colour scheme to transcribe; searched
// docs/reports/ and found none, so this is a placeholder pending T49's asset specification.
var palette = new[]
{
    "#c62828", "#1565c0", "#2e7d32", "#f9a825", "#6a1b9a", "#00838f", "#ef6c00", "#4e342e",
    "#ad1457", "#283593", "#00695c", "#9e9d24", "#5d4037", "#37474f", "#8e24aa", "#d84315",
};
if (palette.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 16)
    throw new InvalidOperationException("Nation colour palette has a duplicate.");

var nationDefs = new NationDefinition[16];
for (var i = 0; i < 16; i++)
{
    var n = nationTable.Nations[i];
    var population = 0;
    for (var ci = 0; ci < prefix.Cities.Count; ci++)
        if (prefix.Cities[ci].OwnerCode == n.Code)
            population += prefix.Cities[ci].PopulationThousands;

    string? capitalId = n.CapitalCityIndex == SaveNationTable.NoCapitalSentinel
        ? null
        : CityId(n.CapitalCityIndex);

    nationDefs[i] = new NationDefinition(
        Id: nationIds[i],
        Name: n.Name,
        ColorHex: palette[i],
        LeaderName: "(unassigned -- drawn at New Game)",
        CapitalCityId: capitalId,
        Treasury: n.Treasury,
        Unity: n.UnityValue,
        Wealth: n.Wealth,
        TaxBase: n.TaxBase,
        TaxRatePercent: n.TaxRatePercent,
        MobilizedPercent: n.MobilizedPercent,
        Population: population,
        Provenance: ProvenanceMap.Of(
            ("name", "confirmed: T30's DAT nation-table parse (IC2.Data.SaveNationTable.Parse), DAT 0x1B100; cross-checked against NationCatalog -- docs/investigations/dat-file-layout.md."),
            ("colorHex", "designed: no report records an original nation colour scheme; searched docs/reports/ and found none. Placeholder pending T49's asset specification."),
            ("leaderName", "designed: not in the DAT. TPremierForm_NewGame's FUN_00448aa4 draws a leader at New Game from a 12-candidate-per-nation pool at DAT 0x2089A -- docs/investigations/dat-file-layout.md. This placeholder carries no DAT provenance; DoD 2 asserts no leader string in this export claims one."),
            ("capitalCityId", "confirmed: DAT nation record capital-city-index field (+0x415), T30's parse."),
            ("treasury", "confirmed: DAT nation record +0x40d, T30's parse."),
            ("unity", "confirmed: DAT nation record +0x411, T30's parse."),
            ("wealth", "confirmed: DAT nation record +0x409, T30's parse -- nation-tax-base-and-city-economy-fields.md."),
            ("taxBase", "confirmed: DAT nation record +0x41b, exported as stored, not recomputed -- nation-tax-base-and-city-economy-fields.md, docs/task-catalogue.md T29 DoD 9."),
            ("taxRatePercent", "confirmed: DAT nation record +0x419, T30's parse."),
            ("mobilizedPercent", "confirmed: DAT nation record +0x413, T30's parse."),
            ("population", "derived: not itself a DAT nation-record field; summed from this nation's own cities' populationThousands (WorldPrefix.Parse), consistent with the score formula's use of a nation-level population figure (corpus id diplomacy.scoreFormula).")));
}

// ============================================================================================
// 6. Cities.
// ============================================================================================
var cityDefs = new CityDefinition[prefix.Cities.Count];
for (var i = 0; i < prefix.Cities.Count; i++)
{
    var c = prefix.Cities[i];
    cityDefs[i] = new CityDefinition(
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
        Garrison: ValueList<UnitSlot>.Empty,
        Provenance: ProvenanceMap.Of(
            ("garrison", "confirmed absence: the DAT's 34-byte city record (docs/investigations/dat-file-layout.md) has no unit-slot data; every city record field is accounted for by name/x/y/owner/allegiance/loyalty/supplies/fortification/population/referencePopulation/tribute.")));
}

// ============================================================================================
// 7. Starting armies and fleets -- DoD 7: taken from T30's parse, unit slots included, never
//    re-derived.
// ============================================================================================
var startingArmies = new StartingArmy[armyTable.Armies.Count];
for (var i = 0; i < armyTable.Armies.Count; i++)
{
    var a = armyTable.Armies[i];
    var units = a.Units.Select(u => new UnitSlot(
        MercenaryLabel: u.MercenaryLabel,
        UnitTypeId: UnitTypeIdFor(u.TypeCode),
        Troops: u.Troops,
        Quality: u.QualityCode,
        Name: u.Name)).ToArray();

    startingArmies[i] = new StartingArmy(
        Id: $"army-{a.Index}",
        Nation: NationId(a.OwnerCode),
        X: a.X,
        Y: a.Y,
        Morale: a.Morale,
        Money: a.Money,
        SupplyTons: a.Supplies,
        Moves: a.Moves,
        Units: ValueList.Of(units),
        Provenance: ProvenanceMap.Of(
            ("_group", "confirmed: T30's DAT army-table parse (IC2.Data.SaveArmyTable.Parse), DAT 0x18A5C, record " + a.Index + ".")));
}

var startingFleets = new StartingFleet[fleetTable.Fleets.Count];
for (var i = 0; i < fleetTable.Fleets.Count; i++)
{
    var f = fleetTable.Fleets[i];
    startingFleets[i] = new StartingFleet(
        Id: $"fleet-{f.Index}",
        Nation: NationId(f.OwnerCode),
        X: f.X,
        Y: f.Y,
        Ships: f.ShipCount,
        ConditionPercent: f.ConditionPercent ?? throw new InvalidOperationException($"Fleet {f.Index} is not launched in the shipped DAT."),
        Money: f.Money,
        SupplyTons: f.Supplies,
        Moves: f.Moves,
        Provenance: ProvenanceMap.Of(
            ("_group", "confirmed: T30's DAT fleet-table parse (IC2.Data.SaveFleetTable.Parse), DAT 0x1B0CC, record " + f.Index + ".")));
}

static string UnitTypeIdFor(ushort typeCode) => typeCode switch
{
    0 => "light_infantry",
    1 => "heavy_infantry",
    2 => "archers",
    3 => "light_cavalry",
    4 => "heavy_cavalry",
    _ => throw new InvalidOperationException($"Unknown unit type code {typeCode}."),
};

// ============================================================================================
// 8. Assemble the World. TurnOrder is the DAT's own nation-table order: the original shuffles it
//    at New Game (FUN_00448aa4), which this export does not reproduce -- [designed] default.
// ============================================================================================
var world = new World(
    SchemaVersion: GameDataSchema.CurrentVersion,
    Id: "classical-mediterranean",
    Name: "Classical Mediterranean",
    Width: WorldPrefix.MapWidth,
    Height: WorldPrefix.MapHeight,
    Terrain: terrainGrid,
    TileTypes: tileTypes,
    Nations: ValueList.Of(nationDefs),
    Cities: ValueList.Of(cityDefs),
    StartingArmies: ValueList.Of(startingArmies),
    StartingFleets: ValueList.Of(startingFleets),
    TurnOrder: ValueList.Of(nationIds),
    Provenance: ProvenanceMap.Of(
        ("width", "confirmed: WorldPrefix.MapWidth, T30's DAT parse -- 320x140, docs/investigations/dat-file-layout.md."),
        ("height", "confirmed: WorldPrefix.MapHeight, T30's DAT parse."),
        ("terrain", "confirmed for 44,466 of 44,800 cells: WorldPrefix.Parse's row-major decode of DAT bytes 0x0..0x15DFF, converted from the parser's x-major indexing to the schema's row-major one. The 15 starting-army and 2 starting-fleet tiles are restored from each unit's own CoveredCell field (docs/investigations/dat-file-layout.md) rather than the raw map array, which stores that unit's own rendering marker at its tile, not the terrain under it. The 334 city tiles are [designed] defaults (see tileTypes.plain's use below) -- no DAT field records the terrain a city overwrote."),
        ("tileTypes", "confirmed: the same 12-entry terrain table data/worlds/toy-3city.json ships, DAT offset 0x1F622 -- terrain-move-cost-table-in-dat.md."),
        ("nations", "confirmed: T30's DAT nation-table parse, DAT 0x1B100, cross-checked field-for-field against NationCatalog's ordered name list (16/16 match) -- docs/investigations/dat-file-layout.md, docs/task-catalogue.md T29 DoD 1."),
        ("cities", "confirmed: WorldPrefix.Parse's city table, DAT 0x15E00, 334 records -- docs/investigations/dat-file-layout.md."),
        ("startingArmies", "confirmed: T30's DAT army-table parse, DAT 0x18A5C, 15 fixed records, no count word -- docs/investigations/dat-file-layout.md, docs/task-catalogue.md T29 DoD 7."),
        ("startingFleets", "confirmed: T30's DAT fleet-table parse, DAT 0x1B0CC, 2 fixed records, no count word -- docs/investigations/dat-file-layout.md, docs/task-catalogue.md T29 DoD 7."),
        ("turnOrder", "designed: the DAT's own nation-table order (0..15), used as a default. TPremierForm_NewGame's FUN_00448aa4 shuffles the 16-entry turn order at New Game -- docs/investigations/dat-file-layout.md -- which this export does not reproduce; not DAT-derived play state.")));

Console.WriteLine($"World assembled: {world.Nations.Count} nations, {world.Cities.Count} cities, {world.StartingArmies.Count} armies, {world.StartingFleets.Count} fleets.");

GameDataValidation.Validate("classical-mediterranean.json (in-memory)", world);
Console.WriteLine("World passes GameDataValidation.");

// ============================================================================================
// 9. Ruleset. Built from the toy ruleset's own already-reviewed confirmed constants (the same
//    game, so the same numbers), re-annotated so that every field this script can positively
//    match against a T04 fixtures-corpus id (tests/fixtures/corpus.json) cites that id, verified
//    against the corpus's own value where the corpus entry is itself numeric/boolean.
//    docs/task-catalogue.md T29 DoD 3: no value invented here that is not already in the corpus.
// ============================================================================================
var toyRulesetPath = Path.Combine(repoRoot, "data", "rulesets", "toy-ruleset.json");
var toyRulesetJson = File.ReadAllText(toyRulesetPath);
var rulesetNode = JsonNode.Parse(toyRulesetJson, new JsonNodeOptions { PropertyNameCaseInsensitive = false })!.AsObject();

var corpusPath = Path.Combine(repoRoot, "tests", "fixtures", "corpus.json");
var corpusNode = JsonNode.Parse(File.ReadAllText(corpusPath))!.AsObject();
var corpusById = new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
foreach (var entry in corpusNode["entries"]!.AsArray())
{
    var id = entry!["id"]!.GetValue<string>();
    corpusById[id] = entry["value"];
}

var mismatches = new List<string>();
var annotated = 0;
foreach (var (dotPath, corpusId) in RulesetCorpusMap.Entries)
{
    if (!corpusById.TryGetValue(corpusId, out var corpusValue))
        throw new InvalidOperationException($"Corpus id '{corpusId}' (mapped from '{dotPath}') is not in tests/fixtures/corpus.json.");

    // typeEffectiveness[row][col] and seasonValues[i] are raw scalar array elements (not an
    // object's named property), so NavigateToLeaf's "path ends in an array index" guard -- which
    // exists to force exactly this kind of path to a special case, the same way it already does
    // for terrain.moveCosts[i] and diplomacy.stateCodes -- would reject them. Index the array
    // directly instead; parentObj/leafKey are left unset and unused for these two prefixes, since
    // their annotation (below) targets the containing object, not the array element.
    JsonObject? parentObj;
    string? leafKey;
    JsonNode? leafValue;
    if (dotPath.StartsWith("combat.detailedResolver.typeEffectiveness.", StringComparison.Ordinal))
    {
        var idx = dotPath.Split('.');
        var row = int.Parse(idx[3]);
        var col = int.Parse(idx[4]);
        leafValue = rulesetNode["combat"]!["detailedResolver"]!["typeEffectiveness"]!.AsArray()[row]!.AsArray()[col];
        (parentObj, leafKey) = (null, null);
    }
    else if (dotPath.StartsWith("economy.supplyConsumption.seasonValues.", StringComparison.Ordinal))
    {
        var index = int.Parse(dotPath.Split('.')[3]);
        leafValue = rulesetNode["economy"]!["supplyConsumption"]!["seasonValues"]!.AsArray()[index];
        (parentObj, leafKey) = (null, null);
    }
    else
    {
        (parentObj, leafKey, leafValue) = NavigateToLeaf(rulesetNode, dotPath);
    }

    if (corpusValue is JsonValue cv && cv.TryGetValue<long>(out var corpusLong) &&
        leafValue is JsonValue lv && lv.TryGetValue<long>(out var leafLong))
    {
        if (corpusLong != leafLong)
            mismatches.Add($"{dotPath} = {leafLong} but corpus '{corpusId}' = {corpusLong}");
    }
    else if (corpusValue is JsonValue cvb && cvb.TryGetValue<bool>(out var corpusBool) &&
             leafValue is JsonValue lvb && lvb.TryGetValue<bool>(out var leafBool))
    {
        if (corpusBool != leafBool)
            mismatches.Add($"{dotPath} = {leafBool} but corpus '{corpusId}' = {corpusBool}");
    }
    // A non-scalar corpus entry (a formula string) documents the field without a numeric
    // cross-check; still cited below, just not diffed.

    // TerrainMoveCost (terrain.moveCosts[i]) carries no "_provenance" field of its own -- the
    // schema puts terrain provenance on the containing "terrain" object instead, keyed by a
    // bracketed path (mirroring how a hand-authored file would cite it; see toy-3city.json's own
    // convention of a bracketed key for a provenance note about one array element).
    if (dotPath.StartsWith("terrain.moveCosts.", StringComparison.Ordinal) && dotPath.EndsWith(".moveCost", StringComparison.Ordinal))
    {
        var index = dotPath.Split('.')[2];
        var terrainObj = rulesetNode["terrain"]!.AsObject();
        AnnotateProvenance(terrainObj, $"moveCosts[{index}].moveCost", $" T04 fixtures corpus id: '{corpusId}'.");
    }
    else if (dotPath.StartsWith("diplomacy.stateCodes.", StringComparison.Ordinal))
    {
        // RelationStateCodes (diplomacy.stateCodes) carries no "_provenance" field of its own --
        // same reasoning as terrain.moveCosts above.
        var suffix = dotPath["diplomacy.stateCodes.".Length..];
        var diplomacyObj = rulesetNode["diplomacy"]!.AsObject();
        AnnotateProvenance(diplomacyObj, $"stateCodes.{suffix}", $" T04 fixtures corpus id: '{corpusId}'.");
    }
    else if (dotPath.StartsWith("combat.detailedResolver.typeEffectiveness.", StringComparison.Ordinal))
    {
        // Same reasoning as terrain.moveCosts[i] above: the array element is a raw number with no
        // "_provenance" field of its own, so the citation lives on detailedResolver, bracketed by
        // [row][col] (#236 N1).
        var idx = dotPath.Split('.');
        var detailedResolverObj = rulesetNode["combat"]!["detailedResolver"]!.AsObject();
        AnnotateProvenance(detailedResolverObj, $"typeEffectiveness[{idx[3]}][{idx[4]}]", $" T04 fixtures corpus id: '{corpusId}'.");
    }
    else if (dotPath.StartsWith("economy.supplyConsumption.seasonValues.", StringComparison.Ordinal))
    {
        // Same reasoning again: seasonValues[i] is a raw number, cited on the containing object (#236 N1).
        var index = dotPath.Split('.')[3];
        var supplyConsumptionObj = rulesetNode["economy"]!["supplyConsumption"]!.AsObject();
        AnnotateProvenance(supplyConsumptionObj, $"seasonValues[{index}]", $" T04 fixtures corpus id: '{corpusId}'.");
    }
    else
    {
        AnnotateProvenance(parentObj!, leafKey!, $" T04 fixtures corpus id: '{corpusId}'.");
    }

    annotated++;
}

if (mismatches.Count > 0)
{
    throw new InvalidOperationException(
        "The following fields disagree with their mapped T04 fixtures-corpus entry -- fix RulesetCorpusMap or the ruleset, never the committed JSON by hand:\n  " +
        string.Join("\n  ", mismatches));
}
Console.WriteLine($"Ruleset: {annotated} field(s) cross-checked and annotated against the T04 fixtures corpus (of {RulesetCorpusMap.Entries.Length} mapped; not every ruleset constant has a distinct corpus id -- see the script's own report).");

rulesetNode["id"] = "classical-faithful";
rulesetNode["name"] = "Classical, faithful";
rulesetNode["description"] =
    "The shipped, historically-faithful preset: task T29's export of the classical-mediterranean " +
    "ruleset. Every confirmed constant is the same figure toy-ruleset.json already carries (the " +
    "same game), transcribed from the reports in the research repository's docs/reports/ and " +
    "cross-checked here against the T04 fixtures corpus (tests/fixtures/corpus.json) wherever the " +
    "corpus gives that constant its own id. Its flags reproduce the original faithfully; the " +
    "'improved' preset (task T36) is this file with docs/game-design.md's 'improved' column applied.";

// ---- #299 guard: no "_provenance" string may ship written from the toy ruleset's own point of
// view. The rule this script follows is that toy-ruleset.json's own provenance wording is written
// preset-neutrally -- never naming "toy" or "classical-faithful" by file -- so that copying it
// verbatim into this preset (or improved.json, T36) never says something false about the file it
// lands in. This check is the safety net for that rule: it catches a regression at the source, the
// same way the DoD 2 leaderName check below does, rather than trusting every future edit to
// toy-ruleset.json to remember it.
var toyMentions = FindToyProvenanceMentions(rulesetNode);
if (toyMentions.Count > 0)
    throw new InvalidOperationException(
        "The following _provenance strings mention \"toy\", which would ship a note written from " +
        "toy-ruleset.json's own point of view inside classical-faithful.json -- fix the wording in " +
        "toy-ruleset.json (preset-neutral, no file names) and re-run the export, never hand-edit the " +
        "committed JSON:\n  " + string.Join("\n  ", toyMentions));

var rulesetJsonText = rulesetNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
var ruleset = GameDataLoader.Load<Ruleset>("classical-faithful.json (in-memory)", rulesetJsonText);
Console.WriteLine("Ruleset round-trips through GameDataLoader/GameDataValidation with no schema errors.");

// ---- DoD 10 safety net: assert the ruleset's own season-name list already matches the calendar
// before this export ships it, so a future editor cannot silently commit a mismatched pair. This
// does not replace the loader-side check DoD 10 asks for (out of this task's Owns list -- see the
// PR body); it only guards this script's own output.
if (ruleset.NewsLog.SeasonNames.Count != ruleset.Calendar.SeasonsPerYear)
    throw new InvalidOperationException(
        $"newsLog.seasonNames has {ruleset.NewsLog.SeasonNames.Count} entries but calendar.seasonsPerYear is {ruleset.Calendar.SeasonsPerYear}.");

// ---- DoD 2 safety net: no leader string in the export may claim DAT provenance -----------------
foreach (var nation in world.Nations)
{
    var source = nation.Provenance?.SourceFor("leaderName") ?? "";
    if (source.Contains("confirmed", StringComparison.OrdinalIgnoreCase) && source.Contains("DAT", StringComparison.Ordinal))
        throw new InvalidOperationException($"Nation '{nation.Id}' leaderName provenance appears to claim DAT provenance: \"{source}\"");
}

// ============================================================================================
// 10. Scenario: seats every nation (AI by default -- New Game assigns human seats and leaders),
//     references the exported world and ruleset. Turn order and leader names are not fixed here,
//     for the same reason DoD 8 gives.
// ============================================================================================
var seats = nationIds.Select(id => new Seat(Nation: id, Control: SeatControl.Ai, Personality: null)).ToArray();
var scenario = new Scenario(
    SchemaVersion: GameDataSchema.CurrentVersion,
    Id: "classical-mediterranean",
    Name: "Classical Mediterranean, 270 BC",
    WorldId: world.Id,
    RulesetId: ruleset.Id,
    Seats: ValueList.Of(seats),
    Victory: new VictoryCondition(VictoryConditionType.TotalConquest, Goal: null),
    TurnLimit: null,
    BlindHotseat: false,
    RandomSeed: 270,
    Provenance: ProvenanceMap.Of(
        ("seats", "designed: every nation defaults to AI control. The original assigns the human seat(s) and shuffles the turn order at New Game (TPremierForm_NewGame's FUN_00448aa4 -- docs/investigations/dat-file-layout.md); this scenario, loaded as-is, is an all-AI game."),
        ("victory", "confirmed: the original's only win condition is holding every city on the map -- corpus id victory.allCitiesThreshold (334)."),
        ("turnLimit", "designed: the original has no turn limit, only the hard end year (ruleset victory.hardEndYearBc) -- corpus id victory.yearLimitBC. Left unset (no additional cap) for the faithful preset."),
        ("randomSeed", "designed: a fixed seed so a load of this scenario is reproducible, per docs/game-design.md principle 4. The value (270, the campaign's start year BC) carries no meaning beyond that.")));

GameDataValidation.Validate("classical-mediterranean.json (scenario, in-memory)", scenario);
Console.WriteLine("Scenario passes GameDataValidation.");

// Sanity: the scenario must actually build a GameState against this world/ruleset.
_ = GameStateFactory.CreateInitial(world, ruleset, scenario);
Console.WriteLine("Scenario + world + ruleset build an initial GameState with no errors.");

// ============================================================================================
// 11. Write the four files, canonical form (GameJson.Serialize), UTF-8, no BOM, LF line endings
//     -- deterministic byte-for-byte given the same DAT (DoD 5).
//
//     T62: the world's terrain blob -- 44,800 cells, over 119,000 base64 characters, most of the
//     390 KB world.json -- ships as a sidecar file next to the world JSON instead of inline
//     "data", so reading the world to understand its shape no longer costs reading past a blob
//     that conveys nothing (docs/tasks/T62.md Scope). The bytes themselves are untouched: the
//     sidecar gets exactly the same base64 string `terrainGrid.Data` above already held; only the
//     world.json we write now points at it via "dataFile" instead of embedding it. GameDataLoader
//     resolves the sidecar back into TerrainGrid.Data at load time, so every reader downstream of
//     the loader -- including this same script's own validation just above -- sees the identical
//     value either way.
// ============================================================================================
const string terrainSidecarFileName = "classical-mediterranean.terrain.b64";
var terrainBase64 = world.Terrain.Data
                     ?? throw new InvalidOperationException("world.Terrain.Data was unexpectedly null before the T62 sidecar split.");
var worldForExport = world with { Terrain = world.Terrain with { Data = null, DataFile = terrainSidecarFileName } };

WriteCanonical(Path.Combine(repoRoot, "data", "worlds", "classical-mediterranean.json"), GameJson.Serialize(worldForExport));
WriteCanonical(Path.Combine(repoRoot, "data", "worlds", terrainSidecarFileName), terrainBase64);
WriteCanonical(Path.Combine(repoRoot, "data", "rulesets", "classical-faithful.json"), GameJson.Serialize(ruleset));
WriteCanonical(Path.Combine(repoRoot, "data", "scenarios", "classical-mediterranean.json"), GameJson.Serialize(scenario));

Console.WriteLine();
Console.WriteLine($"Wrote data/worlds/classical-mediterranean.json, data/worlds/{terrainSidecarFileName}, data/rulesets/classical-faithful.json, data/scenarios/classical-mediterranean.json.");

static void WriteCanonical(string path, string json)
{
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    var normalized = json.Replace("\r\n", "\n");
    if (!normalized.EndsWith('\n')) normalized += "\n";
    File.WriteAllText(path, normalized, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

static (JsonObject parent, string key, JsonNode? value) NavigateToLeaf(JsonObject root, string dotPath)
{
    var parts = dotPath.Split('.');
    JsonNode current = root;
    for (var i = 0; i < parts.Length - 1; i++)
    {
        current = int.TryParse(parts[i], out var index)
            ? current.AsArray()[index]!
            : current[parts[i]]!;
    }

    var lastKey = parts[^1];
    if (int.TryParse(lastKey, out _))
        throw new InvalidOperationException($"Path '{dotPath}' ends in an array index; provenance lives on the containing object, map the object's own scalar sibling instead.");
    var parentObj = current.AsObject();
    return (parentObj, lastKey, parentObj[lastKey]);
}

static void AnnotateProvenance(JsonObject parent, string key, string suffix)
{
    if (parent["_provenance"] is not JsonObject provenance)
    {
        provenance = new JsonObject();
        parent["_provenance"] = provenance;
    }

    var existing = provenance[key]?.GetValue<string>();
    provenance[key] = string.IsNullOrEmpty(existing) ? suffix.TrimStart() : existing + suffix;
}

static string FindThisFileDirectory([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
    Path.GetDirectoryName(path)!;

/// <summary>
/// Recursively finds every string value inside an object literally named <c>_provenance</c>
/// (anywhere in the tree) that mentions "toy" case-insensitively -- the #299 guard. Returns each
/// hit as <c>"dotted.path.key: \"text\""</c> for a readable error.
/// </summary>
static List<string> FindToyProvenanceMentions(JsonNode? node, string path = "")
{
    var hits = new List<string>();
    switch (node)
    {
        case JsonObject obj:
            foreach (var (key, value) in obj)
            {
                var childPath = path.Length == 0 ? key : $"{path}.{key}";
                if (key == "_provenance" && value is JsonObject provenance)
                {
                    foreach (var (provenanceKey, provenanceValue) in provenance)
                    {
                        if (provenanceValue is JsonValue jv && jv.TryGetValue<string>(out var text) &&
                            text.Contains("toy", StringComparison.OrdinalIgnoreCase))
                        {
                            hits.Add($"{childPath}.{provenanceKey}: \"{text}\"");
                        }
                    }
                }
                else
                {
                    hits.AddRange(FindToyProvenanceMentions(value, childPath));
                }
            }
            break;
        case JsonArray arr:
            for (var i = 0; i < arr.Count; i++)
            {
                hits.AddRange(FindToyProvenanceMentions(arr[i], $"{path}[{i}]"));
            }
            break;
    }

    return hits;
}

/// <summary>
/// The dot-path (into <c>data/rulesets/toy-ruleset.json</c>'s own JSON shape, which
/// <c>classical-faithful.json</c> shares) to T04 fixtures-corpus id (<c>tests/fixtures/corpus.json</c>)
/// mapping this script uses to annotate and cross-check every field it can positively identify.
/// </summary>
/// <remarks>
/// <para>
/// Built by matching each ruleset leaf's field-name tokens against every corpus entry whose own
/// value agrees numerically, keeping only unambiguous matches, and then hand-correcting every case
/// where two different fields happen to share a value (several are called out by name in
/// <c>Ruleset.cs</c>'s own doc comments, e.g. <c>EconomyRules.TaxBaseContributionMultiplier</c>
/// against <c>TreasuryCreditTaxBaseQuarterShareDivisor</c>, both 4 but different fields) or where a
/// value collision made a positional match ambiguous (<c>terrain.moveCosts</c>, unit-type
/// <c>moves</c>/<c>shots</c>/<c>range</c>/<c>quarterlyPrice</c> — corrected here using the fixed,
/// known order of <c>toy-ruleset.json</c>'s own <c>terrain.moveCosts</c> and <c>unitTypes</c>
/// arrays, not by value).
/// </para>
/// <para>
/// A mapping to a corpus id whose own <c>value</c> is numeric or boolean is cross-checked by the
/// script (a mismatch aborts the export); a mapping to a corpus id whose <c>value</c> is a formula
/// string (e.g. <c>"fleet.moves.baseFormula"</c>) is cited but not diffed — the corpus does not
/// give that specific constant its own scalar id, only the formula it appears in.
/// </para>
/// <para>
/// <strong>This is not every ruleset field.</strong> ~109 of the ~360 numeric/boolean leaves in
/// <c>classical-faithful.json</c> have no confident match here (down from ~140 before T68 added
/// the type-effectiveness matrix, the two caps and the four season values -- #236 N1) — mostly
/// designed placeholders (weather effect magnitudes, the scatter/detailed-resolver reserve
/// constants) or values the corpus documents only as part of a longer prose passage with no
/// isolable id. Those keep <c>toy-ruleset.json</c>'s original report-citation provenance, which is
/// still a valid citation per <c>Provenance.cs</c>'s format — just not corpus-id-traced. See T68's
/// PR body for the exact count and every remaining unmapped <c>confirmed</c> entry, each with the
/// reason no id fits.
/// </para>
/// </remarks>
internal static class RulesetCorpusMap
{
    public static readonly (string Path, string CorpusId)[] Entries =
    {
        ("armyManagement.maxArmies", "caps.maxArmies"),
        ("armyManagement.maxTroopsPerArmy", "caps.maxTroopsPerArmy"),
        ("armyManagement.maxUnitsPerArmy", "caps.maxUnitsPerArmy"),
        ("armyManagement.newArmyMorale", "supplyMorale.newArmyMorale"),
        ("calendar.cityUnitStateCodeCap", "calendar.stateCodeCap"),
        ("calendar.cityUnitStateCodeStep", "calendar.stateCodeIncrementPerWeek"),
        ("calendar.startSeasonIndex", "calendar.seasonWrapValue"),
        ("calendar.startWeek", "calendar.weekWrapValue"),
        ("calendar.startYearBc", "calendar.yearStartBC"),
        ("calendar.weekStep", "calendar.weekIncrementPerTurn"),
        ("capture.captureTreasuryCreditMultiplier", "capture.treasuryCreditMultiplier"),
        ("capture.captureUnityGain", "capture.unityGain"),
        ("capture.captureUnityLoss", "capture.unityLoss"),
        ("capture.cascadeAllegiantDefenseDivisor", "defection.rebelliousSympathyDivisor"),
        ("capture.cascadeDistanceMax", "defection.cascadeConditions"),
        ("capture.cascadeLoyaltyThreshold", "defection.cascadeConditions"),
        ("capture.cascadeUnityThreshold", "defection.cascadeConditions"),
        ("capture.defectionTreasuryCreditMultiplier", "defection.cascadeConditions"),
        ("capture.defectionUnityGain", "defection.unityGain"),
        ("capture.defectionUnityLoss", "defection.unityLoss"),
        ("cityOrders.orders.0.inProgressEncodingRadix", "capture.siegeDefenderStrengthFormula"),
        ("combat.absorbedSupplyTroopDivisor", "battle.instantResolver.winnerSuppliesCap"),
        ("combat.autoPeaceChanceDenominator", "battle.instantResolver.reparationTriggerChance"),
        ("combat.autoPeaceChanceNumerator", "battle.instantResolver.reparationTriggerChance"),
        ("combat.autoPeaceLoserCityThreshold", "battle.instantResolver.reparationTriggerCityThreshold"),
        ("combat.autoPeaceLoserUnityThreshold", "battle.instantResolver.reparationTriggerUnityThreshold"),
        ("combat.detailedResolver.meleeLossCapPercent", "caps.meleeLossPercentOfOwnTroops"),
        ("combat.detailedResolver.meleeLossHardCap", "caps.meleeLossAbsoluteCap"),
        // The 25-value type-effectiveness matrix (#236 N1), in the recorded orientation: row =
        // attacker's unitTypes index, column = defender's, matching the corpus's own
        // matrix.<attacker>.vs.<defender> ids value-for-value (verified against toy-ruleset.json's
        // committed 5x5 array before this map was written -- see the PR body).
        ("combat.detailedResolver.typeEffectiveness.0.0", "matrix.lightInfantry.vs.lightInfantry"),
        ("combat.detailedResolver.typeEffectiveness.0.1", "matrix.lightInfantry.vs.heavyInfantry"),
        ("combat.detailedResolver.typeEffectiveness.0.2", "matrix.lightInfantry.vs.archers"),
        ("combat.detailedResolver.typeEffectiveness.0.3", "matrix.lightInfantry.vs.lightCavalry"),
        ("combat.detailedResolver.typeEffectiveness.0.4", "matrix.lightInfantry.vs.heavyCavalry"),
        ("combat.detailedResolver.typeEffectiveness.1.0", "matrix.heavyInfantry.vs.lightInfantry"),
        ("combat.detailedResolver.typeEffectiveness.1.1", "matrix.heavyInfantry.vs.heavyInfantry"),
        ("combat.detailedResolver.typeEffectiveness.1.2", "matrix.heavyInfantry.vs.archers"),
        ("combat.detailedResolver.typeEffectiveness.1.3", "matrix.heavyInfantry.vs.lightCavalry"),
        ("combat.detailedResolver.typeEffectiveness.1.4", "matrix.heavyInfantry.vs.heavyCavalry"),
        ("combat.detailedResolver.typeEffectiveness.2.0", "matrix.archers.vs.lightInfantry"),
        ("combat.detailedResolver.typeEffectiveness.2.1", "matrix.archers.vs.heavyInfantry"),
        ("combat.detailedResolver.typeEffectiveness.2.2", "matrix.archers.vs.archers"),
        ("combat.detailedResolver.typeEffectiveness.2.3", "matrix.archers.vs.lightCavalry"),
        ("combat.detailedResolver.typeEffectiveness.2.4", "matrix.archers.vs.heavyCavalry"),
        ("combat.detailedResolver.typeEffectiveness.3.0", "matrix.lightCavalry.vs.lightInfantry"),
        ("combat.detailedResolver.typeEffectiveness.3.1", "matrix.lightCavalry.vs.heavyInfantry"),
        ("combat.detailedResolver.typeEffectiveness.3.2", "matrix.lightCavalry.vs.archers"),
        ("combat.detailedResolver.typeEffectiveness.3.3", "matrix.lightCavalry.vs.lightCavalry"),
        ("combat.detailedResolver.typeEffectiveness.3.4", "matrix.lightCavalry.vs.heavyCavalry"),
        ("combat.detailedResolver.typeEffectiveness.4.0", "matrix.heavyCavalry.vs.lightInfantry"),
        ("combat.detailedResolver.typeEffectiveness.4.1", "matrix.heavyCavalry.vs.heavyInfantry"),
        ("combat.detailedResolver.typeEffectiveness.4.2", "matrix.heavyCavalry.vs.archers"),
        ("combat.detailedResolver.typeEffectiveness.4.3", "matrix.heavyCavalry.vs.lightCavalry"),
        ("combat.detailedResolver.typeEffectiveness.4.4", "matrix.heavyCavalry.vs.heavyCavalry"),
        ("combat.naval.conditionDivisor", "battle.naval.strengthFormula"),
        ("combat.naval.randomBandCount", "battle.naval.randomBonus"),
        ("combat.naval.randomBandPercent", "battle.naval.randomBonus"),
        ("combat.naval.unitySwingShipDivisor", "battle.naval.unityChangeFormula"),
        ("combat.naval.winnerDamageDivisor", "battle.naval.winnerDamageFormula"),
        ("combat.powerDivisor", "battle.instantResolver.powerFormula"),
        ("combat.powerTroopDivisor", "battle.instantResolver.powerFormula"),
        ("combat.promotionChanceDenominator", "battle.instantResolver.promotionChance"),
        ("combat.qualityFloor", "battle.instantResolver.qualityFloor"),
        ("combat.unitySwing", "battle.instantResolver.unityWinnerDelta"),
        ("combat.winnerCasualtyNumerator", "battle.instantResolver.casualtyFormula"),
        ("diplomacy.cooldownAfterAllyPeace", "diplomacy.allyPostWarCooldown"),
        ("diplomacy.cooldownAfterBrokenAlliance", "diplomacy.cooldown.brokenAlliance"),
        ("diplomacy.cooldownAfterBrokenTrade", "diplomacy.cooldown.brokenTrade"),
        ("diplomacy.cooldownAfterEndedWar", "diplomacy.cooldown.endedWar"),
        ("diplomacy.faithfulThawColumnLimit", "diplomacy.thawBugColumnsProcessed"),
        ("diplomacy.maxTradePartners", "diplomacy.maxTradePartners"),
        ("diplomacy.reparationsPerCity", "reparation.formula"),
        ("diplomacy.reparationsWealthDivisor", "reparation.formula"),
        ("diplomacy.stateCodes.alliance", "diplomacy.relationState.alliance"),
        ("diplomacy.stateCodes.peace", "diplomacy.relationState.peace"),
        ("diplomacy.stateCodes.trade", "diplomacy.relationState.trade"),
        ("diplomacy.stateCodes.war", "diplomacy.relationState.war"),
        ("diplomacy.thawBonus", "diplomacy.thaw.bonusChance"),
        ("diplomacy.thawBonusChanceDenominator", "diplomacy.thaw.bonusChance"),
        ("diplomacy.thawPerQuarter", "diplomacy.thaw.stepPerQuarter"),
        ("economy.armySupplyTonsPerTroops", "supply.capacityFormula.army"),
        ("economy.autoResupplyPurseTopUpAmount", "autoResupply.ownCity.purseTopUpAmount"),
        ("economy.autoResupplyPurseTopUpThreshold", "autoResupply.ownCity.purseTopUpThreshold"),
        ("economy.citySupplyBaselineSeasonValue", "citySupply.baselineSeasonValue"),
        ("economy.citySupplyCapTonsPerPopulationThousand", "citySupply.capTonsPerPopulationThousand"),
        ("economy.citySupplyMobilizationDivisor", "citySupply.mobilizationDivisor"),
        ("economy.citySupplyProductionDivisor", "citySupply.productionDivisor"),
        ("economy.debtTreasuryFloor", "economy.debtTreasuryFloor"),
        ("economy.debtUnityThreshold", "economy.debtUnityThreshold"),
        ("economy.debtWealthDivisor", "economy.debtWealthDivisor"),
        ("economy.depositionRandomDivisor", "economy.depositionRandomDivisor"),
        ("economy.depositionTreasuryCredit", "economy.depositionTreasuryCredit"),
        ("economy.depositionUnityCeiling", "economy.depositionUnityCeiling"),
        ("economy.depositionUnityGainAmount", "economy.depositionUnityGainAmount"),
        ("economy.famineLoyaltyLossAmount", "citySupply.winterFamineLoyaltyLossAmount"),
        ("economy.famineLoyaltyLossProbabilityDenominator", "citySupply.winterFamineLoyaltyLossChance"),
        ("economy.fleetSupplyTonsPerShip", "supply.capacityFormula.fleet"),
        ("economy.lowTaxLoyaltyCityThreshold", "economy.lowTaxLoyaltyCityThreshold"),
        ("economy.lowTaxLoyaltyThresholdPercent", "economy.lowTaxLoyaltyThresholdPercent"),
        ("economy.loyaltyFallProbabilityDenominator", "economy.loyaltyFallProbabilityDenominator"),
        ("economy.loyaltyFallTaxDivisor", "economy.loyaltyFallTaxDivisor"),
        ("economy.loyaltyRiseRollBound", "economy.loyaltyRiseRollBound"),
        ("economy.mercenaryDesertionSupplyDivisor", "mercenary.upkeepFormula"),
        ("economy.mobilizationDecayPerQuarter", "economy.mobilizationDecayPerQuarter"),
        ("economy.populationGrowthConstantAddend", "economy.populationGrowthConstantAddend"),
        ("economy.populationGrowthGapDivisor", "economy.populationGrowthGapDivisor"),
        ("economy.populationGrowthMobilizationDivisor", "economy.populationGrowthMobilizationDivisor"),
        ("economy.populationGrowthTaxDivisor", "economy.populationGrowthTaxDivisor"),
        ("economy.purseCapPerUnit", "caps.maxPurseTalents"),
        ("economy.rebellionLoyaltyThreshold", "economy.rebellionLoyaltyThreshold"),
        ("economy.shipUpkeepPerQuarter", "economy.shipUpkeepPerQuarter"),
        ("economy.supplyConsumption.consumptionBaseValue", "supplyMorale.consumptionFormula"),
        ("economy.supplyConsumption.consumptionDivisor", "supplyMorale.consumptionDivisor"),
        ("economy.supplyConsumption.fleetEmbarkedDivisor", "supplyMorale.fleetEmbarkedConsumptionFormula"),
        // The four season values (#236 N1), indexed 0=Spring..3=Winter (calendar.startSeasonIndex's
        // own convention, per toy-ruleset.json's own economy.supplyConsumption._provenance note).
        ("economy.supplyConsumption.seasonValues.0", "supplyMorale.seasonTable.spring"),
        ("economy.supplyConsumption.seasonValues.1", "supplyMorale.seasonTable.summer"),
        ("economy.supplyConsumption.seasonValues.2", "supplyMorale.seasonTable.autumn"),
        ("economy.supplyConsumption.seasonValues.3", "supplyMorale.seasonTable.winter"),
        ("economy.supplyDialogArmyCapacityBonus", "supply.dialogCapacityBonus.army"),
        ("economy.supplyMorale.baseMovesMax", "supplyMorale.baseMovesFormula"),
        ("economy.supplyMorale.deadBandUpperPercent", "supplyMorale.deadBandUpperPercent"),
        ("economy.supplyMorale.decayAmount", "supplyMorale.decayAmount"),
        ("economy.supplyMorale.decayThresholdPercent", "supplyMorale.decayThresholdPercent"),
        ("economy.supplyMorale.moraleCeiling", "supplyMorale.ceiling"),
        ("economy.supplyMorale.moraleFloor", "supplyMorale.floor"),
        ("economy.supplyMorale.movesReductionCap", "supplyMorale.baseMovesFormula"),
        ("economy.supplyMorale.movesTroopDivisor", "supplyMorale.baseMovesFormula"),
        ("economy.supplyMorale.regenAmount", "supplyMorale.regenAmount"),
        ("economy.supplyTonsPerTalent", "supply.purchaseFormula"),
        ("economy.taxBaseContributionMultiplier", "capture.taxBaseMultiplier"),
        ("economy.threatenedCityAdjacencyRadius", "economy.threatenedCityAdjacencyRadius"),
        ("economy.tradeIncomeTaxBaseDivisor", "economy.tradeIncomeTaxBaseDivisor"),
        ("economy.treasuryCreditPerCityUpkeep", "economy.treasuryCreditPerCityUpkeep"),
        ("economy.treasuryCreditTaxBaseQuarterShareDivisor", "economy.treasuryCreditTaxBaseQuarterShareDivisor"),
        ("economy.treasuryCreditWealthDivisor", "economy.treasuryCreditWealthDivisor"),
        ("economy.unityBaseGainPerQuarter", "economy.unityBaseGainPerQuarter"),
        ("economy.unityCap", "caps.maxUnity"),
        ("economy.unityFloor", "economy.unityFloor"),
        ("economy.unityMobilizationDivisor", "economy.unityMobilizationDivisor"),
        ("economy.unityTaxRateDivisor", "economy.unityTaxRateDivisor"),
        ("economy.wealthPerPopulationThousand", "economy.wealthPerPopulationThousand"),
        ("economy.weather.earlyLateWeekThreshold", "weather.oddsBySeasonWeek.springEarly"),
        ("economy.weather.locationCount", "weather.trackedLocationCount"),
        ("loyalty.allegiantRecaptureTarget", "loyalty.target.recapture"),
        ("loyalty.defectionFloor", "loyalty.floor.defection"),
        ("loyalty.forcedCaptureFloor", "loyalty.floor.forcedCapture"),
        ("mapMarkers.armyTroopTierThresholds", "movement.blockingMarkerRange.army"),
        ("mapMarkers.fleetShipTierThresholds", "movement.blockingMarkerRange.fleet"),
        ("naval.buildCostPerShip", "fleet.costFormula"),
        ("naval.constructionTicks", "fleet.constructionTicks"),
        ("naval.damageSlowdownConditionThreshold", "fleet.condition.movePenaltyThreshold"),
        ("naval.deathConditionThreshold", "fleet.condition.deathThreshold"),
        ("naval.joinMaxShips", "fleet.joinMaxShips"),
        ("naval.launchConditionPercent", "fleet.launchState"),
        ("naval.movesBaseValue", "fleet.moves.baseFormula"),
        ("naval.movesCarriedArmyAddend", "fleet.moves.carriedArmyPenaltyFormula"),
        ("naval.movesCarriedArmyTroopDivisor", "fleet.moves.carriedArmyPenaltyFormula"),
        ("naval.movesShipDivisor", "fleet.moves.baseFormula"),
        ("naval.movesShipOffset", "fleet.moves.baseFormula"),
        ("naval.orderMaxShips", "fleet.maxShipsPerOrder"),
        ("naval.orderMinShips", "fleet.minShipsPerOrder"),
        ("naval.repairCostDivisor", "fleet.repairCostFormula"),
        ("naval.splitMinShips", "fleet.splitMinShips"),
        ("naval.stormAwayFromCoastDamageAddend", "fleet.storm.awayFromCoastMultiplier"),
        ("naval.stormAwayFromCoastDamageMultiplier", "fleet.storm.awayFromCoastMultiplier"),
        ("naval.stormDamageRandomDivisor", "fleet.storm.damagePercentDivisor"),
        ("naval.stormNearCoastDamageDivisor", "fleet.storm.nearCoastDamageDivisor"),
        ("naval.stormShipLossDamageThreshold", "fleet.storm.shipLossDamageThreshold"),
        ("naval.stormTripleDamageCap", "fleet.storm.coveredCellTripleCap"),
        ("naval.stormWinterDamageCap", "fleet.storm.winterDoublingCap"),
        ("naval.stormWinterSpikeChanceDenominator", "fleet.storm.winterAndAwayRareCatastrophe"),
        ("naval.stormWinterSpikeDamage", "fleet.storm.winterAndAwayRareCatastrophe"),
        ("naval.transportTroopsPerShip", "fleet.embarkCapacityPerShip"),
        ("naval.zeroSupplyMovesPenalty", "fleet.condition.zeroSupplyMovesLoss"),
        ("newsLog.messageByteLength", "newsMessage.weekHeader"),
        ("newsLog.ringBufferSlots", "caps.maxNewsSlots"),
        ("recruitment.mercenaryHireTroopDivisor", "mercenary.hireCostFormula"),
        ("recruitment.mercenaryPoolSlots", "caps.maxMercenarySlots"),
        ("recruitment.mercenaryUpkeepQualityDivisor", "mercenary.upkeepFormula"),
        ("recruitment.troopsPerCostUnit", "recruitment.costFormula.initial"),
        ("siege.archerStrengthMultiplier", "capture.attackerSiegeStrengthArcherMultiplier"),
        ("siege.defenderFortificationWeight", "capture.siegeDefenderStrengthFormula"),
        ("siege.defenderLoyaltyWeight", "capture.siegeDefenderStrengthFormula"),
        ("siege.defenderNonAllegiantDenominator", "capture.nonAllegiantDefenderPenalty"),
        ("siege.defenderNonAllegiantNumerator", "capture.nonAllegiantDefenderPenalty"),
        ("siege.defenderPopulationWeight", "capture.siegeDefenderStrengthFormula"),
        ("siege.highLoyaltyBonusDenominator", "capture.fortBonusMultiplier"),
        ("siege.highLoyaltyBonusNumerator", "capture.fortBonusMultiplier"),
        ("siege.highLoyaltyThreshold", "capture.fortBonusThreshold"),
        ("siege.powerDivisor", "capture.siegeDefenderStrengthFormula"),
        ("terrain.moveCosts.0.moveCost", "terrain.moveCost.code0"),
        ("terrain.moveCosts.1.moveCost", "terrain.moveCost.code1"),
        ("terrain.moveCosts.10.moveCost", "terrain.moveCost.code10"),
        ("terrain.moveCosts.11.moveCost", "terrain.moveCost.code11"),
        ("terrain.moveCosts.2.moveCost", "terrain.moveCost.code2"),
        ("terrain.moveCosts.3.moveCost", "terrain.moveCost.code3"),
        ("terrain.moveCosts.4.moveCost", "terrain.moveCost.code4"),
        ("terrain.moveCosts.5.moveCost", "terrain.moveCost.code5"),
        ("terrain.moveCosts.6.moveCost", "terrain.moveCost.code6"),
        ("terrain.moveCosts.7.moveCost", "terrain.moveCost.code7"),
        ("terrain.moveCosts.8.moveCost", "terrain.moveCost.code8"),
        ("terrain.moveCosts.9.moveCost", "terrain.moveCost.code9"),
        ("unitTypes.0.combatPowerWeight", "unitType.lightInfantry.powerWeight"),
        ("unitTypes.0.moves", "unitType.lightInfantry.moves"),
        ("unitTypes.0.quarterlyPrice", "unitType.lightInfantry.recruitCostQuarterly"),
        ("unitTypes.0.range", "unitType.lightInfantry.range"),
        ("unitTypes.0.recruitCost", "unitType.lightInfantry.recruitCostInitial"),
        ("unitTypes.0.shots", "unitType.lightInfantry.shots"),
        ("unitTypes.0.standardBattalionSize", "unitType.lightInfantry.battalionSize"),
        ("unitTypes.1.combatPowerWeight", "unitType.heavyInfantry.powerWeight"),
        ("unitTypes.1.moves", "unitType.heavyInfantry.moves"),
        ("unitTypes.1.quarterlyPrice", "unitType.heavyInfantry.recruitCostQuarterly"),
        ("unitTypes.1.range", "unitType.heavyInfantry.range"),
        ("unitTypes.1.recruitCost", "unitType.heavyInfantry.recruitCostInitial"),
        ("unitTypes.1.shots", "unitType.heavyInfantry.shots"),
        ("unitTypes.1.standardBattalionSize", "unitType.heavyInfantry.battalionSize"),
        ("unitTypes.2.combatPowerWeight", "unitType.archers.powerWeight"),
        ("unitTypes.2.moves", "unitType.archers.moves"),
        ("unitTypes.2.quarterlyPrice", "unitType.archers.recruitCostQuarterly"),
        ("unitTypes.2.range", "unitType.archers.range"),
        ("unitTypes.2.recruitCost", "unitType.archers.recruitCostInitial"),
        ("unitTypes.2.shots", "unitType.archers.shots"),
        ("unitTypes.2.standardBattalionSize", "unitType.archers.battalionSize"),
        ("unitTypes.3.combatPowerWeight", "unitType.lightCavalry.powerWeight"),
        ("unitTypes.3.moves", "unitType.lightCavalry.moves"),
        ("unitTypes.3.quarterlyPrice", "unitType.lightCavalry.recruitCostQuarterly"),
        ("unitTypes.3.range", "unitType.lightCavalry.range"),
        ("unitTypes.3.recruitCost", "unitType.lightCavalry.recruitCostInitial"),
        ("unitTypes.3.shots", "unitType.lightCavalry.shots"),
        ("unitTypes.3.standardBattalionSize", "unitType.lightCavalry.battalionSize"),
        ("unitTypes.4.combatPowerWeight", "unitType.heavyCavalry.powerWeight"),
        ("unitTypes.4.moves", "unitType.heavyCavalry.moves"),
        ("unitTypes.4.quarterlyPrice", "unitType.heavyCavalry.recruitCostQuarterly"),
        ("unitTypes.4.range", "unitType.heavyCavalry.range"),
        ("unitTypes.4.recruitCost", "unitType.heavyCavalry.recruitCostInitial"),
        ("unitTypes.4.shots", "unitType.heavyCavalry.shots"),
        ("unitTypes.4.standardBattalionSize", "unitType.heavyCavalry.battalionSize"),
        ("victory.hardEndYearBc", "victory.yearLimitBC"),
        ("victory.totalConquestRequiresEveryCity", "victory.allCitiesThreshold"),
    };
}
