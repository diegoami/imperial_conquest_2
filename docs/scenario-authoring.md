# Scenario Authoring

This document is a complete reference for every field in the three file kinds that compose a scenario: `World`, `Ruleset`, and `Scenario`. Use it to author custom scenarios for Imperial Conquest 2.

A scenario is the moddable, shareable unit of gameplay. It consists of:
- **One World**: the map, terrain, nations, cities, and starting forces
- **One Ruleset**: all gameplay constants, costs, and formula-variant selectors
- **One Scenario**: victory conditions and seat (player control) assignments

Files are JSON with a conventional structure. Every numeric value should carry a `_provenance` field explaining where it came from — a report in the research repository, a design decision, or an open question. This ensures the doc stays truthful and reviewable.

---

## The Provenance Convention

Every field that is not metadata (like `id` or `name`) should have a sibling `_provenance` entry that explains its origin. The `_provenance` object is a map of field names to source strings.

Each source string must start with one of these tags:
- **`confirmed:`** The value comes from a report in `docs/reports/` or is computed from confirmed data. Include the report name.
- **`derived:`** The value is computed from other confirmed data (e.g., a total or average).
- **`designed:`** The value is a design choice (a toy fixture, a new invention, or an improvement). Must also state what was searched and came up empty.
- **`open:`** The value is a placeholder pending research. Include the research question.

Example:
```json
{
  "treasury": 500,
  "_provenance": {
    "treasury": "designed: a toy fixture value. No report records a starting treasury for an invented 2-nation map."
  }
}
```

---

## World Files

A world file is stored under `data/worlds/` as a JSON file (e.g., `data/worlds/toy-3city.json`). It defines the map, nations, cities, and starting military forces.

### Top-level fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `schemaVersion` | integer | Yes | The version of the World schema. Must be `1`. |
| `id` | string | Yes | Unique identifier for this world. Used in scenario files to reference it. |
| `name` | string | Yes | Display name (e.g., "Classical Mediterranean", "Toy 3-city skirmish"). |
| `width` | integer | Yes | Map width in tiles (e.g., 320 for the classical world). |
| `height` | integer | Yes | Map height in tiles (e.g., 140 for the classical world). |
| `terrain` | object | Yes | Terrain grid encoding. See "Terrain grid" below. |
| `tileTypes` | array | Yes | List of terrain type definitions. See "Tile types" below. |
| `nations` | array | Yes | List of nation definitions. See "Nations" below. |
| `cities` | array | Yes | List of city definitions. See "Cities" below. |
| `startingArmies` | array | Yes | List of starting army definitions. See "Starting armies" below. |
| `startingFleets` | array | Yes | List of starting fleet definitions. See "Starting fleets" below. |
| `turnOrder` | array of strings | Yes | Order in which nations take turns, by id (e.g., `["rome", "gaul", "egypt", ...]`). |
| `startingRelations` | object | No | Diplomatic relations matrix at game start (see T75). If omitted, all nations start at peace. |
| `startingNews` | array | No | News log entries at game start (see T75). If omitted, the log is empty. |
| `startingNeighbours` | object | No | Adjacency list of neighbors for each nation (see T85). If omitted, neighbors are computed from geography. |
| `_provenance` | object | No | Provenance map for fields that need explanation. |

### Terrain grid

The terrain grid is always stored using run-length encoding in a `terrain` object:

```json
{
  "terrain": {
    "encoding": "runLength",
    "runs": [
      { "code": 0, "count": 10 },
      { "code": 2, "count": 5 },
      ...
    ],
    "data": null
  }
}
```

Each run is:
- `code`: the terrain type code (integer, must match a `code` value in `tileTypes`)
- `count`: how many consecutive tiles have this code

The total count across all runs must equal `width × height`.

The `data` field is always `null` for JSON files (it may be used for binary encodings in the future).

### Tile types

Each tile type in `tileTypes` is an object with:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Unique identifier for this terrain type (e.g., "plain", "forest", "sea_coastal"). |
| `code` | integer | Yes | Numeric code used in the terrain grid (0–255). No two tile types may share a code. |
| `name` | string | Yes | Display name (e.g., "Plain", "Forest", "Sea"). |
| `passableByArmies` | boolean | Yes | Whether armies can move through this terrain. |
| `passableByFleets` | boolean | Yes | Whether fleets can move through this terrain. |
| `_provenance` | object | No | Provenance map. |

### Nations

Each nation in `nations` is an object with:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Unique identifier (e.g., "rome", "gaul", "egypt"). |
| `name` | string | Yes | Display name (e.g., "Roman Republic"). |
| `colorHex` | string | Yes | Hex color code for the nation (e.g., "#c62828"). |
| `leaderName` | string | Yes | Name of the nation's leader. |
| `capitalCityId` | string | Yes | The `id` of the city that is this nation's capital (must resolve to a city in `cities`). |
| `treasury` | integer | Yes | Starting treasury (gold coins). |
| `unity` | integer | Yes | Starting unity (morale / cohesion). |
| `wealth` | integer | Yes | Starting wealth (used in economy calculations). |
| `taxBase` | integer | Yes | Starting tax base (calculated from city populations but can be stored as a fixed value). |
| `taxRatePercent` | integer | Yes | Tax rate as a percentage (0–100). |
| `mobilizedPercent` | integer | Yes | Percentage of population mobilized into military units (0–100). |
| `population` | integer | Yes | Total population (in thousands or as configured by the ruleset). |
| `_provenance` | object | No | Provenance map. |

### Cities

Each city in `cities` is an object with:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Unique identifier. |
| `name` | string | Yes | Display name. |
| `x` | integer | Yes | X coordinate on the map. |
| `y` | integer | Yes | Y coordinate on the map. |
| `owner` | string | Yes | Nation `id` that currently owns the city. |
| `allegiance` | string | Yes | Nation `id` that the city is loyal to (may differ from owner after conquest/defection). |
| `loyalty` | integer | Yes | Loyalty rating (0–100, where 65 is the defection floor). |
| `supplyTons` | integer | Yes | Supply stored in the city (tons). |
| `fortificationCode` | integer | Yes | Fortification state (0–100 for completed, >100 for order in progress). Encoding: values >100 encode a fortify order, where `(code - 100) / 100` is progress and the extra encodes the order strength. |
| `populationThousands` | integer | Yes | Current population (in thousands). |
| `maxPopulationThousands` | integer | Yes | Maximum population the city can support. |
| `tribute` | integer | Yes | Tribute or tax owed from the city. |
| `garrison` | array of units | Yes | Units garrisoned in the city (may be empty). See "Units" below. |
| `_provenance` | object | No | Provenance map. |

### Starting armies

Each army in `startingArmies` is an object with:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Unique identifier. |
| `nation` | string | Yes | Nation `id` that controls the army. |
| `x` | integer | Yes | X coordinate on the map. |
| `y` | integer | Yes | Y coordinate on the map. |
| `morale` | integer | Yes | Army morale (0–100). |
| `money` | integer | Yes | Money in the army's purse (0–1000). |
| `supplyTons` | integer | Yes | Supply the army is carrying (tons). |
| `moves` | integer | Yes | Movement points remaining (this is typically set during gameplay, but starting value may be specified). |
| `units` | array of units | Yes | Units in the army (at least one). See "Units" below. |
| `_provenance` | object | No | Provenance map. |

### Starting fleets

Each fleet in `startingFleets` is an object with:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Unique identifier. |
| `nation` | string | Yes | Nation `id` that controls the fleet. |
| `x` | integer | Yes | X coordinate on the map. |
| `y` | integer | Yes | Y coordinate on the map. |
| `ships` | integer | Yes | Number of ships (minimum 10). |
| `conditionPercent` | integer | Yes | Fleet condition (0–100). |
| `money` | integer | Yes | Money the fleet is carrying (0–1000). |
| `supplyTons` | integer | Yes | Supply the fleet is carrying (tons). |
| `moves` | integer | Yes | Movement points remaining. |
| `_provenance` | object | No | Provenance map. |

### Units

A unit is embedded in an army, fleet, or city garrison. Each unit is an object with:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `mercenaryLabel` | integer | Yes | Mercenary indicator: 0 for a regular unit, 1–255 for a named mercenary type. |
| `unitTypeId` | string | Yes | Unit type identifier (must resolve to a `UnitTypeRules` in the ruleset). |
| `troops` | integer | Yes | Number of troops in the unit. |
| `quality` | integer | Yes | Unit quality / experience rating. |
| `name` | string | Yes | Display name (e.g., "1st Guards Battalion"). |

---

## Ruleset Files

A ruleset file is stored under `data/rulesets/` as a JSON file. It defines all gameplay constants, unit stats, costs, and formula variants.

### Top-level fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `schemaVersion` | integer | Yes | Must be `1`. |
| `id` | string | Yes | Unique identifier (e.g., "classical-faithful", "toy-ruleset"). |
| `name` | string | Yes | Display name. |
| `description` | string | Yes | Description of the ruleset (e.g., "the original game's rules, bugs included"). |
| `calendar` | object | Yes | Calendar configuration. See "Calendar rules" below. |
| `unitTypes` | array | Yes | Unit type stat table. See "Unit types" below. |
| `terrain` | object | Yes | Terrain movement costs and defaults. See "Terrain rules" below. |
| `economy` | object | Yes | Economy parameters (tax rates, income formulas, etc.). |
| `recruitment` | object | Yes | Recruitment costs and formulas. |
| `armyManagement` | object | Yes | Army maintenance and supply costs. |
| `naval` | object | Yes | Naval rules (ship costs, fleet maintenance, etc.). |
| `combat` | object | Yes | Combat mechanics (battle formulas, casualty rates, etc.). |
| `siege` | object | Yes | Siege mechanics and attrition. |
| `loyalty` | object | Yes | Loyalty, defection, and rebellion rules. |
| `capture` | object | Yes | Capture mechanics (pillage, ransom, defection on capture). |
| `diplomacy` | object | Yes | Diplomatic relations, alliances, and treaties. |
| `cityOrders` | object | Yes | City work orders (fortification, unit building, etc.). |
| `newsLog` | object | Yes | News log messages and season names. |
| `mapMarkers` | object | Yes | Map marker configuration. |
| `victory` | object | Yes | Victory condition thresholds and parameters. |
| `ai` | object | Yes | AI weights and heuristics. |
| `flags` | object | Yes | Feature flags selecting formula variants. See "Flags" below. |
| `_provenance` | object | No | Provenance map. |

### Calendar rules

The `calendar` object defines the game calendar:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `weekStep` | integer | Yes | Number of weeks advanced per turn. |
| `weekModulus` | integer | Yes | Total weeks per cycle (e.g., 12 for a 4-season year). |
| `seasonAdvanceFromWeek` | integer | Yes | Week number that triggers season advancement. |
| `seasonsPerYear` | integer | Yes | Number of seasons in a year (e.g., 4). |
| `startWeek` | integer | Yes | Starting week (must be able to reach `seasonAdvanceFromWeek`). |
| `startSeasonIndex` | integer | Yes | Starting season (0-indexed). |
| `startYearBc` | integer | Yes | Starting year (can be negative for BC, or positive for AD). |
| `cityUnitStateCodeStep` | integer | Yes | Step value for city unit state codes. |
| `cityUnitStateCodeCap` | integer | Yes | Maximum city unit state code. |
| `_provenance` | object | No | Provenance map. |

### Unit types

The `unitTypes` array is a list of unit stat rows. Each unit type is an object with:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `id` | string | Yes | Unique unit type identifier (e.g., "light_infantry", "heavy_cavalry"). |
| `name` | string | Yes | Display name. |
| `abbreviation` | string | Yes | Short abbreviation (e.g., "LI", "HC"). |
| `moves` | integer | Yes | Movement points the unit has per turn. |
| `standardBattalionSize` | integer | Yes | Default number of troops in a standard battalion. |
| `shots` | integer | Yes | Number of shots (for ranged units; 0 for melee). |
| (Additional fields...) | | | Many other stat fields are defined; see a shipped ruleset for the full list. |
| `_provenance` | object | No | Provenance map. |

Refer to the schema in `src/IC2.Engine/Model/Ruleset.cs` for the complete list of unit type fields.

### Terrain rules

The `terrain` object defines movement costs:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `moveCosts` | array | Yes | List of terrain type movement costs. Each entry has `tileTypeId` and `moveCost`. |
| `defaultMoveCost` | integer | Yes | Move cost for terrain types not explicitly listed. |
| `_provenance` | object | No | Provenance map. |

### Flags

The `flags` object selects formula variants. Each flag is a boolean field named after the choice. Examples:
- `partialDefeatWhenLosingBattle`: true = losers take casualties; false = losers are eliminated.
- `loyaltyBelowThresholdCausesDefection`: true = low loyalty triggers defection events.

---

## Scenario Files

A scenario file is stored under `data/scenarios/` as a JSON file. It specifies victory conditions and player seat assignments.

### Top-level fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `schemaVersion` | integer | Yes | Must be `1`. |
| `id` | string | Yes | Unique scenario identifier (e.g., "toy-3city", "classical-start-270ad"). |
| `name` | string | Yes | Display name. |
| `worldId` | string | Yes | The `id` of the world this scenario uses. |
| `rulesetId` | string | Yes | The `id` of the ruleset this scenario uses. |
| `seats` | array | Yes | Array of seat assignments, one per nation in the world. See "Seats" below. |
| `victory` | object | Yes | Victory condition specification. See "Victory condition" below. |
| `turnLimit` | integer or null | No | Maximum number of turns before the game ends (null for unlimited). |
| `blindHotseat` | boolean | Yes | If true, hotseat mode does not show the map between seats (for hidden movement). |
| `randomSeed` | integer | Yes | RNG seed for reproducible games (set to a fixed value for testing). |
| `_provenance` | object | No | Provenance map. |

### Seats

Each seat in `seats` is an object assigning one nation to a player:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `nation` | string | Yes | Nation `id` (must be in the world's nation list). |
| `control` | enum | Yes | Control type: `"human"` or `"ai"`. |
| `personality` | object or null | No | AI personality parameters (required if `control == "ai"`). See "AI personality" below. |

### AI personality

If a nation is controlled by the AI, the `personality` object defines its behavior:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `aggression` | number | Yes | Aggression level (0–1; higher = more aggressive). |
| `expansionDrive` | number | Yes | Desire to expand territory (0–1). |
| `loyaltyToAlliances` | number | Yes | Tendency to keep alliances (0–1; higher = more loyal). |

### Victory condition

The `victory` object specifies the win condition:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | enum | Yes | Condition type: `"totalConquest"`, `"diplomaticVictory"`, `"custom"`, etc. |
| `goal` | string or null | No | Custom goal text (required if `type == "custom"`). |

Example:
```json
{
  "victory": {
    "type": "totalConquest",
    "goal": null
  }
}
```

---

## Examples

### Minimal world file

```json
{
  "schemaVersion": 1,
  "id": "minimal-world",
  "name": "Minimal Test World",
  "width": 10,
  "height": 10,
  "terrain": {
    "encoding": "runLength",
    "runs": [
      { "code": 2, "count": 100 }
    ],
    "data": null
  },
  "tileTypes": [
    {
      "id": "plain",
      "code": 2,
      "name": "Plain",
      "passableByArmies": true,
      "passableByFleets": false,
      "_provenance": {
        "code": "designed: example value"
      }
    }
  ],
  "nations": [
    {
      "id": "nation_a",
      "name": "Nation A",
      "colorHex": "#FF0000",
      "leaderName": "Leader A",
      "capitalCityId": "city_a",
      "treasury": 1000,
      "unity": 600,
      "wealth": 500,
      "taxBase": 100,
      "taxRatePercent": 20,
      "mobilizedPercent": 25,
      "population": 100,
      "_provenance": {
        "treasury": "designed: example value"
      }
    }
  ],
  "cities": [
    {
      "id": "city_a",
      "name": "Capital A",
      "x": 5,
      "y": 5,
      "owner": "nation_a",
      "allegiance": "nation_a",
      "loyalty": 80,
      "supplyTons": 500,
      "fortificationCode": 50,
      "populationThousands": 150,
      "maxPopulationThousands": 250,
      "tribute": 50,
      "garrison": []
    }
  ],
  "startingArmies": [],
  "startingFleets": [],
  "turnOrder": ["nation_a"],
  "_provenance": {
    "width": "designed: minimal test world"
  }
}
```

### Minimal scenario file

```json
{
  "schemaVersion": 1,
  "id": "minimal-scenario",
  "name": "Minimal Test Scenario",
  "worldId": "minimal-world",
  "rulesetId": "classical-faithful",
  "seats": [
    {
      "nation": "nation_a",
      "control": "human",
      "personality": null
    }
  ],
  "victory": {
    "type": "totalConquest",
    "goal": null
  },
  "turnLimit": 50,
  "blindHotseat": false,
  "randomSeed": 12345,
  "_provenance": {
    "turnLimit": "designed: example limit for testing"
  }
}
```

---

## Authoring Custom Rulesets

While the shipped rulesets (`classical-faithful` and `improved`) cover the game's core mechanics, a modder can create entirely custom rulesets by authoring a new `.json` file under `data/rulesets/`.

A custom ruleset file follows the exact structure of `Ruleset` in this document. **Every numeric value must carry provenance**:
- **`confirmed:`** if transcribed from a report in `docs/reports/`
- **`designed:`** if a new design choice (must state what was searched and found empty)
- **`derived:`** if computed from other confirmed data

### Example: Custom ruleset based on a shipped one

A practical approach is to copy a shipped ruleset (e.g., `classical-faithful.json`), change a few clearly-labelled values, and document each change with `_provenance`. For instance:

```json
{
  "schemaVersion": 1,
  "id": "my-custom-ruleset",
  "name": "My House Rules",
  "description": "Classical rules with house-rule modifications",
  "calendar": { /* ... copy from classical-faithful ... */ },
  "economy": {
    "taxableIncome": 150,
    "_provenance": {
      "taxableIncome": "designed: lowered from 200 to make economy tighter. Searched docs/reports/ and no original value was found to lock to, so this is a design choice."
    }
    /* ... rest of economy ... */
  },
  /* ... other rule sections ... */
}
```

Then author a scenario referencing it:

```json
{
  "id": "my-scenario",
  "worldId": "classical-mediterranean",
  "rulesetId": "my-custom-ruleset",
  /* ... */
}
```

Load with: `IC2.Cli --scenario my-scenario`

---

## Tips for custom scenarios

1. **Keep it small for testing**: Use a small world (e.g., 10×10) for fast unit tests.
2. **Always include provenance**: Even for toy values, document where they came from.
3. **Test your scenario**: Load it with `IC2.Cli --scenario <id>` and run a few turns to ensure all references resolve.
4. **Use realistic values when possible**: Draw from the research reports in `docs/reports/` or the shipped `classical-mediterranean` world.
5. **Reserve nation IDs carefully**: Use lowercase, hyphen-separated names (e.g., `northern-league`, `southern-league`).

---

## Schema versions

This document describes schema version `1`. If the schema changes in the future, old files will declare an older version number.
