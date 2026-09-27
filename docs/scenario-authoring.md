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
| `startingRelations` | object or null | No | Diplomatic relations matrix at game start (see T75). See "Starting relations" below. If omitted, all nations start at peace. |
| `startingNews` | object or null | No | News log entries at game start (see T75) — an object (`mostRecentSlot` + `slots`), not a plain array. See "Starting news" below. If omitted, the log is empty. |
| `startingNeighbours` | array or null | No | Adjacency list of neighbours for each nation (see T85). See "Starting neighbours" below. If omitted, neighbours are computed from geography. |
| `_provenance` | object | No | Provenance map for fields that need explanation. |

### Terrain grid

The `terrain` object (`TerrainGrid`) carries the row-major cell grid in one of two mutually exclusive
encodings, selected by `encoding`:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `encoding` | enum | Yes | `"runLength"` or `"base64"`. Selects which of `runs`/`data`/`dataFile` carries the cells. |
| `runs` | array or null | For `runLength` | List of `{ "code": <int>, "count": <int> }` runs. Present only for `"runLength"`; must not be set for `"base64"`. |
| `data` | string or null | For inline `base64` | The base64 cells inline, one little-endian unsigned 16-bit cell code per cell. Mutually exclusive with `dataFile`. |
| `dataFile` | string or null | For sidecar `base64` | The base64 cells in a sidecar file, named relative to the directory the world document itself was loaded from (e.g. `classical-mediterranean.terrain.b64` next to `classical-mediterranean.json`) — used for a world large enough that the blob would dominate the JSON. Resolved into `data` by the loader before any other code sees the document. |

A small fixture (this document's own examples, and `example-tiny-duel.json`) uses `"runLength"`:

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

The total count across all runs must equal `width × height`. A large export (the shipped
`classical-mediterranean` world) instead uses `"base64"` with `dataFile`, keeping the grid out of the
JSON entirely; see rule 11 of `CLAUDE.md` — the sidecar is never worth reading directly.

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
| `fortificationCode` | integer | Yes | The raw fortification word (see "Fortification code" below), not a plain percentage. |
| `populationThousands` | integer | Yes | Current population (in thousands). |
| `maxPopulationThousands` | integer | Yes | Maximum population the city can support. |
| `tribute` | integer | Yes | Tribute or tax owed from the city. |
| `garrison` | array of units | Yes | Units garrisoned in the city (may be empty). See "Units" below. |
| `_provenance` | object | No | Provenance map. |

#### Fortification code

`fortificationCode` is a dual-purpose word, not a plain 0–100 percentage (`src/IC2.Engine/Model/FortificationCode.cs`,
`[confirmed: decompiled-unit-map-orders-and-record-fields.md]`). It is decoded against the ruleset's
matching `cityOrders.orders[].maxPercent` / `.inProgressEncodingRadix` (`"fortify"` in every shipped
ruleset, both `100`):

- A value **at or below** `maxPercent` (100) is already a finished percentage, read directly.
- A value **above** `maxPercent` encodes a fortify order still in progress, written as
  `code = finishedPercent + pendingPoints × radix`. To decode it: `finishedPercent = code % radix` and
  `pendingPoints = code / radix` (integer division). For example, code `250` with `radix = 100` is 50%
  finished with 2 points still pending — **not** `(250 − 100) / 100 = 1.5`, a formula this document
  previously (and incorrectly) gave.

A city that is simply "60% fortified, nothing pending" — as every example scenario in this repository
uses — is written as the plain value `60`, which is already at or below `maxPercent` and needs no
decoding at all.

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

### Starting relations

`startingRelations` (T75), when present, is a `DiplomaticRelations` object — the same shape the engine's
own live game state uses for its relation matrix, reused here rather than inventing a second type:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `nationIds` | array of strings | Yes | Row/column order of `matrix`. Length is the world's nation count, not fixed at 16. |
| `matrix` | array of arrays of integers | Yes | Row-major N×N cells. Symmetric by construction: cell `[a][b]` always equals `[b][a]`. Values are the ruleset's `diplomacy.stateCodes` codes; negative values are cooldown counters. |

### Starting news

`startingNews` (T75), when present, is a `NewsLog` object — the same shape the engine's own live news log uses:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `mostRecentSlot` | integer | Yes | Index of the most recently written slot in `slots`; `-1` for an empty log. |
| `slots` | array of `{ "text": string }` | Yes | Log entries, slot `0` through `mostRecentSlot` inclusive. |

### Starting neighbours

`startingNeighbours` (T85), when present, is an array of adjacency entries, one per nation:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `nationId` | string | Yes | The nation this entry is about. |
| `neighbourIds` | array of strings | Yes | Every nation this one borders. An entry with an empty list, and an omitted entry, both mean "borders nobody" — the engine never falls back to geometric adjacency once a world carries `startingNeighbours` at all. |

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
| `standardBattalionSize` | integer | Yes | Default number of troops in a standard battalion of this type. |
| `shots` | integer | Yes | Number of ranged shots per combat (0 for a melee unit). |
| `range` | integer | Yes | Attack range, in tiles. |
| `recruitCost` | integer | Yes | Talents charged to place a standing-recruitment order for one battalion. |
| `quarterlyPrice` | integer | Yes | Quarterly upkeep cost (talents), and the same price a mercenary hire uses. |
| `combatPowerWeight` | integer | Yes | Per-unit weight this type contributes to a field battle's power calculation (`Strength/ArmyPower.cs`). |
| `_provenance` | object | No | Provenance map. |

### Terrain rules

The `terrain` object defines movement costs:

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `moveCosts` | array | Yes | List of terrain type movement costs. Each entry has `tileTypeId` and `moveCost`. |
| `defaultMoveCost` | integer | Yes | Move cost for terrain types not explicitly listed. |
| `_provenance` | object | No | Provenance map. |

### Economy rules

The `economy` object holds tax, upkeep, supply-purchase, treasury, population-growth, loyalty-draw and
weather constants (`src/IC2.Engine/Model/Ruleset.cs`'s `EconomyRules`; formulas cross-checked against
`src/IC2.Engine/Economy/*.cs`). Three fields (`supplyConsumption`, `supplyMorale`, `weather`) are nested
objects documented in their own subsections below.

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `taxRateDivisor` | integer | divisor | `TaxIncome`: quarterly tax income = `taxBase × taxRatePercent / this`. |
| `shipUpkeepPerQuarter` | integer | talents/ship | `ShipUpkeep`: quarterly fleet upkeep = `ships × this`. |
| `mobilizationDecayPerQuarter` | integer | percentage points | `NationUnityUpdate.DecayMobilization`: mobilization lost each quarter, floored at 0. |
| `unityCap` | integer | 0–this | The upper clamp every unity-changing formula in the engine respects. |
| `purseCapPerUnit` | integer | talents | Maximum money an army/fleet purse may hold; `AutomaticResupply` sends any excess to the treasury. |
| `supplyTonsPerTalent` | integer | tons/talent | Conversion rate for a paid supply purchase (`SupplyPurchase`, `AutomaticResupply`). |
| `armySupplyTonsPerTroops` | integer | divisor | `SupplyCapacity.ArmyCapacityTons`: an army's supply capacity = `troops / this`. |
| `fleetSupplyTonsPerShip` | integer | tons/ship | `SupplyCapacity.FleetCapacityTons`: a fleet's supply capacity = `ships × this`. |
| `supplyPercentNumerator` | integer | numerator | `SupplyCapacity.PercentFull`: the supply panel's percentage = `supplyTons × this / troops`. |
| `mercenaryDesertionSupplyDivisor` | integer | divisor | A deserting mercenary takes `troops / this` supply tons from the army on its way out. |
| `debtWealthDivisor` | integer | divisor | Debt test: a nation is in debt when `treasury < −(wealth / this)`. |
| `debtTreasuryFloor` | integer | talents | Debt test: also in debt when `treasury < this`. |
| `debtUnityThreshold` | integer | 0–unityCap | Debt test: also in debt when `unity < this`. |
| `depositionRandomDivisor` | integer | 1-in-this | An in-debt AI nation is deposed with this quarterly chance (a human nation is deposed deterministically at its turn start instead). |
| `depositionUnityGainAmount` | integer | unity points | Deposition effect: `unity = max(unity, min(depositionUnityCeiling, unity + this))`. |
| `depositionUnityCeiling` | integer | 0–unityCap | The ceiling `depositionUnityGainAmount` cannot push unity above. |
| `depositionTreasuryCredit` | integer | talents | Deposition effect: `treasury = treasury < 0 ? 0 : treasury + this`. |
| `depositionRelationResetThreshold` | integer | relation code | Deposition effect: any relation from this value up to (but not including) zero resets to zero. |
| `lowTaxLoyaltyThresholdPercent` | integer | tax rate % | `CityLoyaltyDraws`: the quarterly loyalty rise draw only runs when the owner's tax rate is below this. |
| `lowTaxLoyaltyCityThreshold` | integer | 0–100 loyalty | `CityLoyaltyDraws`: the rise draw also requires the city's own loyalty to be below this. |
| `rebellionLoyaltyThreshold` | integer | 0–100 loyalty | `CityLoyaltyDraws`: a non-capital city below this loyalty (after both draws) is flagged as a rebellion risk. |
| `supplyConsumption` | object | — | Per-turn army/fleet supply consumption. See "Supply consumption" below. |
| `supplyMorale` | object | — | The supply→strategic-morale rule. See "Supply morale" below. |
| `weather` | object | — | The weather-event frequency curve and effects table. See "Weather events" below. |
| `supplyDialogArmyCapacityBonus` | integer | tons | Added to `armySupplyTonsPerTroops`'s capacity for the supply *dialog's* transfer cap only (both the free own-city and paid foreign paths); every other supply writer uses the plain capacity. |
| `autoResupplyPurseTopUpThreshold` | integer | talents | `AutomaticResupply`: below this purse balance (and a positive treasury), the unit's purse is topped up. |
| `autoResupplyPurseTopUpAmount` | integer | talents | The flat top-up `AutomaticResupply` grants when the threshold above is met. |
| `autoResupplyRadiusTiles` | integer | tiles (Chebyshev) | Every AI turn, each AI army/fleet runs automatic resupply against every non-hostile city within this radius. |
| `commandAdjacencyRadiusTiles` | integer | tiles (Chebyshev, `<=`) | The one-tile adjacency test shared by the four supply-purchase command handlers, fleet repair/scuttle, and disembarkation's landing-tile check. |
| `taxBaseContributionMultiplier` | integer | multiplier | `NationTaxBaseRebuild`/`CityOwnershipTaxTransfer`: a city's tax-base contribution (`tribute × population / maxPopulation`) is multiplied by this before being summed into its owner's tax base. |
| `wealthPerPopulationThousand` | integer | talents per 1,000 pop | A city's wealth contribution to its owner = `populationThousands × this`. |
| `treasuryCreditTaxBaseQuarterShareDivisor` | integer | divisor | `NationTreasuryCredit`: quarterly credit includes `+ taxBase / this`. |
| `treasuryCreditPerCityUpkeep` | integer | talents/city | `NationTreasuryCredit`: quarterly credit includes `− cityCount × this`. |
| `treasuryCreditWealthDivisor` | integer | divisor | `NationTreasuryCredit`: quarterly credit includes `− wealth / this`. |
| `tradeIncomeTaxBaseDivisor` | integer | divisor | `NationTreasuryCredit`: quarterly credit includes `+ Σ(partner.taxBase / this)` over every trade or allied partner. |
| `populationGrowthGapDivisor` | integer | divisor | `CityPopulationGrowth`: `d = (maxPopulation − population) / this`. |
| `populationGrowthTaxDivisor` | integer | divisor | `CityPopulationGrowth`: `d′ = d − d × ownerTaxRatePercent / this`. |
| `populationGrowthMobilizationDivisor` | integer | divisor | `CityPopulationGrowth`: `growth = d′ − d′ × ownerMobilizedPercent / this + populationGrowthConstantAddend`. |
| `populationGrowthConstantAddend` | integer | population (thousands) | The flat addend of the same growth formula. |
| `threatenedCityAdjacencyRadius` | integer | tiles (Chebyshev) | `HostileArmyAdjacent.IsThreatened`: a city with a hostile army within this radius does not grow and is excluded from the AI's "threatened city" score bonus. |
| `unityFloor` | integer | 0–unityCap | `NationUnityUpdate`: the lower clamp of the quarterly unity update. |
| `unityBaseGainPerQuarter` | integer | unity points | `NationUnityUpdate`: flat unity gained each quarter before the tax and mobilization terms. |
| `unityTaxRateDivisor` | integer | divisor | `NationUnityUpdate`: quarterly unity update subtracts `taxRatePercent / this`. |
| `unityMobilizationDivisor` | integer | divisor | `NationUnityUpdate`: quarterly unity update subtracts `decayedMobilizedPercent / this`. |
| `loyaltyRiseRollBound` | integer | exclusive upper bound | `CityLoyaltyDraws`: the rise draw adds `Random(this)` (i.e. `0..this-1`) loyalty. |
| `loyaltyFallProbabilityDenominator` | integer | 1-in-this | `CityLoyaltyDraws`: chance the quarterly loyalty fall draw runs at all. |
| `loyaltyFallTaxDivisor` | integer | divisor | `CityLoyaltyDraws`: when it runs and `taxRate > 0`, the loss = `Random(taxRate) / this`. |
| `citySupplyBaselineSeasonValue` | integer | supply-table units | `CityWeeklySupply`: `s = population × (seasonValue − this) / citySupplyProductionDivisor`. |
| `citySupplyProductionDivisor` | integer | divisor | The divisor of the same term. |
| `citySupplyMobilizationDivisor` | integer | divisor | `CityWeeklySupply`: `inc = s − s × ownerMobilizedPercent / this`. |
| `citySupplyCapTonsPerPopulationThousand` | integer | tons per 1,000 pop | `CityWeeklySupply`: a city's supply stock is capped at `population × this`. |
| `famineLoyaltyLossProbabilityDenominator` | integer | 1-in-this | `CityWeeklySupply`: chance a Winter city whose stock just hit 0 loses loyalty. |
| `famineLoyaltyLossAmount` | integer | loyalty points | The loyalty lost when the famine-unrest roll fires. |
| `_provenance` | object | No | Provenance map. |

#### Supply consumption (`economy.supplyConsumption`)

Per-turn army/fleet supply consumption (`SupplyConsumptionRules`).

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `consumptionBaseValue` | integer | supply-table units | The `90` of `((90 − seasonValue) × troops) / consumptionDivisor`. |
| `consumptionDivisor` | integer | divisor | The `20000` divisor of the same expression. |
| `fleetEmbarkedDivisor` | integer | divisor | An army aboard a fleet consumes a flat `troops / this` instead, with no seasonal term. |
| `seasonValues` | array of integers | one per season | `seasonValue` indexed by `CalendarState.SeasonIndex` — Spring 50, Summer 80, Autumn 80, Winter 20 in the shipped rulesets. Sized to `calendar.seasonsPerYear`. |
| `_provenance` | object | No | Provenance map. |

#### Supply morale (`economy.supplyMorale`)

The supply→strategic-morale rule (`SupplyMoraleRules`), run every army tick after that turn's consumption.
Writes only an army's strategic morale, never per-unit tactical morale.

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `decayThresholdPercent` | integer | 0–100 supply % | Below this supply percentage, morale decays. |
| `deadBandUpperPercent` | integer | 0–100 supply % | At or below this (and at or above `decayThresholdPercent`), morale is unchanged; above it, morale regenerates. |
| `decayAmount` | integer | morale points | Lost per turn while starving, floored at `moraleFloor`. |
| `moraleFloor` | integer | hard floor | The lowest strategic morale can reach through this rule. |
| `regenAmount` | integer | morale points | Gained per turn while well supplied, capped at `moraleCeiling`. |
| `moraleCeiling` | integer | hard ceiling | The highest strategic morale can reach through this rule. |
| `movesPenaltyOnDecay` | integer | moves | Lost, on top of `baseMovesMax`, the turn morale decays. |
| `baseMovesMax` | integer | moves | Recomputed every turn: `baseMovesMax − min(movesReductionCap, troops / movesTroopDivisor)`. |
| `movesTroopDivisor` | integer | divisor | The troop divisor of the base-moves formula above. |
| `movesReductionCap` | integer | moves | The cap on how much the base-moves formula can reduce moves by. |
| `_provenance` | object | No | Provenance map. |

#### Weather events (`economy.weather`)

The weather-event frequency curve (`WeatherEventRules`): confirmed frequency, `[designed]` effects.

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `earlyLateWeekThreshold` | integer | week number | Below this week a season is in its "early" part; at or above it, "late". |
| `bySeason` | array of `WeatherSeasonOdds` | one per season | See table below. |
| `locationCount` | integer | count | The frequency curve is a per-location roll, made independently this many times per tick, not once. |
| `effects` | array of `WeatherEffectRule` | — | The data-driven effects table a fired event draws from. See table below. |
| `_provenance` | object | No | Provenance map. |

`WeatherSeasonOdds` (one entry per season, no `_provenance` of its own):

| Field | Type | Meaning |
|-------|------|---------|
| `seasonIndex` | integer | Which season (0-based) this entry is for. |
| `earlyNumerator` / `earlyDenominator` | integer | The event chance during the season's "early" part, as a fraction. |
| `lateNumerator` / `lateDenominator` | integer | The event chance during the season's "late" part. |

`WeatherEffectRule` (one entry per possible weather effect):

| Field | Type | Meaning |
|-------|------|---------|
| `id` | string | Stable key, e.g. `"storm-damages-fleet"`. |
| `description` | string | Human-readable description of the effect. |
| `_provenance` | object | Provenance map (each effect is individually `designed`, since the report identifies the frequency curve but not the effects). |

### Recruitment rules

The `recruitment` object (`RecruitmentRules`) covers standing recruitment, mobilization, and the mercenary economy.

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `troopsPerCostUnit` | integer | troops/talent | Conversion rate for a standing-recruitment order's troop count vs. its cost. |
| `mercenaryPoolSlots` | integer | count | Size of the mercenary hiring pool. |
| `mercenaryHireTroopDivisor` | integer | divisor | Divisor used when computing troops for a mercenary hire. |
| `mercenaryUpkeepQualityDivisor` | integer | divisor | Divisor used when computing a mercenary unit's quarterly upkeep from its quality. |
| `maxSlots` | integer | count (40 shipped) | Size of a nation's recruitment-slot table — a compacted list; occupying the last slot refuses further orders ("You have reached your limit of 40 units."). |
| `mobilizationQualityDivisor` | integer | divisor | The permanent quality a mobilized recruit is born with = `stateCode / this`, truncating toward zero. |
| `mobilizationMinStateCodeHumanSeat` | integer | state-code units | Lowest recruitment-slot state code a human seat may mobilize. |
| `mobilizationMinStateCodeAiSeat` | integer | state-code units | Lowest state code an AI seat may mobilize (compared as a minimum on this engine; an equality in the original). |
| `mobilizationReceivingArmyRangeHumanSeat` | integer | tiles (Chebyshev, `==`) | Distance from the training city at which a human seat's army may receive a mobilized recruit. |
| `mobilizationReceivingArmyRangeAiSeat` | integer | tiles (Chebyshev, `<`) | The same radius for an AI seat — wider, a confirmed AI advantage. |
| `mobilizationRateOrderStep` | integer | percentage points | Flat amount a nation's mobilization rate rises when a recruitment order is placed (and falls when cancelled). |
| `mobilizationRateWealthScale` | integer | divisor | Scales the mobilization-rate formula's troop term against the nation's wealth. |
| `mobilizationCapPercent` | integer | 0–100 | Ceiling the mobilization rate is clamped to. |
| `_provenance` | object | No | Provenance map. |

### Army management rules

The `armyManagement` object (`ArmyManagementRules`): join/split caps and what a newly split army starts with.

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `maxUnitsPerArmy` | integer | units | A join that would exceed this is refused. |
| `maxTroopsPerArmy` | integer | troops | A join that would exceed this is refused. |
| `maxArmies` | integer | count | A split (or mobilization) that would exceed this many armies in the nation's army table is refused. |
| `splitMinUnits` | integer | units | An army with fewer than this many units cannot be split. |
| `newArmyMorale` | integer | 0–100 | Strategic morale a newly split (or mobilized) army starts with. |
| `newArmyMovesHumanSeat` | integer | moves | Starting moves for a new army split from a human-controlled nation. |
| `newArmyMovesAiSeat` | integer | moves | Starting moves for a new army split from an AI-controlled nation (also used for every seat's mobilization-created army). |
| `_provenance` | object | No | Provenance map. |

### Naval rules

The `naval` object (`NavalRules`): fleet construction, transport, repair, management, and the per-turn
at-sea attrition pass. Structurally analogous to `economy.supplyMorale` but deliberately different in
every particular (trigger, decay shape, floor, regeneration, and lethality) — see the type's own remarks
in `src/IC2.Engine/Model/Ruleset.cs`.

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `buildCostPerShip` | integer | talents/ship | Cost to order one ship of a fleet build. |
| `orderMinShips` / `orderMaxShips` | integer | ships | Legal range for a single build order's ship count. |
| `constructionTicks` | integer | ticks | Countdown a new fleet build order starts at. |
| `constructionTickStep` | integer | per turn | The countdown decrements by this every turn's fleet tick (2 in the shipped rulesets, not 1). |
| `launchConditionPercent` | integer | 0–100 | Condition a fleet launches at once construction completes. |
| `launchSupplyTons` | integer | tons | Supply a fleet launches with. |
| `transportTroopsPerShip` | integer | troops/ship | Troop-carrying capacity per ship. |
| `repairCostDivisor` | integer | divisor | Divisor used to compute a repair order's talent cost from condition restored. |
| `joinMaxShips` | integer | ships | A fleet join that would exceed this many ships is refused. |
| `splitMinShips` | integer | ships | A fleet with fewer than this many ships cannot be split. |
| `maxConditionPercent` | integer | 0–100 | The condition ceiling (repair cannot exceed it). |
| `stormDamageRandomDivisor` | integer | divisor | Base storm roll: `dmg = max(1, Random(100 − condition) / this)`, run on every launched fleet every turn. |
| `stormWinterDamageMultiplier` / `stormWinterDamageCap` | integer | multiplier / cap | Doubles the roll in Winter, held to this cap, before the coast test. |
| `stormTripleDamageMultiplier` / `stormTripleDamageCap` | integer | multiplier / cap | Triples the roll instead when `stormTripleConditionTileCode`'s predicate holds. |
| `stormTripleConditionTileCode` | integer | tile-code value | `[derived]` predicate value selecting the tripling branch over the doubling one (a fleet's covered-tile-code field). |
| `stormAwayFromCoastDamageMultiplier` / `stormAwayFromCoastDamageAddend` | integer | multiplier / addend | Away from friendly coast: `dmg = dmg × multiplier + addend`. |
| `stormNearCoastDamageDivisor` | integer | divisor | Near friendly coast: halves the roll instead (`dmg / this`). |
| `stormWinterSpikeChanceDenominator` | integer | 1-in-this | Away-from-coast, Winter-only chance of spiking to `stormWinterSpikeDamage`. |
| `stormWinterSpikeDamage` | integer | dmg value | The spike value itself. |
| `stormShipLossDamageThreshold` | integer | dmg value | At or above this `dmg`, the storm costs ships as well as condition; below it, only condition is reduced, by `dmg` directly. |
| `stormShipLossRatioBase` / `stormShipLossRatioScale` | integer | formula constants | The heavier branch's proportional-damage ratio: `r = max(1, (ratioBase × ratioScale) / (dmg + ratioBase))`, `d = r² / ratioScale`. |
| `stormShipLossDivisor` | integer | divisor | Heavier branch: ships lost = `ships × d / this`; condition lost = `condition × d / this`. |
| `stormUnitLossDamageThreshold` | integer | `d` value | Above this `d`, a heavy storm also costs the carried army whole units. |
| `stormUnitLossDivisor` | integer | divisor | Whole units lost = `unitCount × d / this + 1`, chosen at random and removed. |
| `deathConditionThreshold` | integer | 0–100 condition | Below this, the fleet is lost at sea (checked after the storm pass, before the zero-supply penalty). |
| `movesBaseValue` / `movesShipOffset` / `movesShipDivisor` | integer | moves formula | `moves = movesBaseValue − (ships − movesShipOffset) / movesShipDivisor`, recomputed every turn. |
| `movesCarriedArmyTroopDivisor` / `movesCarriedArmyAddend` | integer | moves formula | A carried army further reduces moves by `troops(carried) / movesCarriedArmyTroopDivisor / ships + movesCarriedArmyAddend`. |
| `zeroSupplyMovesPenalty` | integer | moves | Lost when supply is exactly 0 (absolute, not a percentage). |
| `zeroSupplyConditionRandomBound` | integer | exclusive upper bound | Zero-supply condition roll: `condition −= Random(this)`. |
| `damageSlowdownConditionThreshold` / `damageSlowdownDivisor` | integer | condition / divisor | Below this condition, moves are further reduced by `(threshold − condition) / divisor`. |
| `friendlyCoastRadiusTiles` | integer | tiles (Chebyshev) | `[designed]` proxy for "near friendly coast": within this radius of an owned city. |
| `markerBandBaseTier1` / `markerBandStep` | integer | marker-code units | Map-marker encoding: `owner + band`, `band` starting at `markerBandBaseTier1` for the smallest ship-count tier and stepping up by `markerBandStep` per tier. |
| `_provenance` | object | No | Provenance map. |

### Combat rules

The `combat` object (`CombatRules`): the shipped instant battle resolver's field-battle constants, plus
two nested groups for the naval variant and the reserved "detailed" resolver.

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `powerTroopDivisor` | integer | divisor | `ArmyPower`: `total += type.combatPowerWeight × unit.troops / this`, summed over units. |
| `powerDivisor` | integer | divisor | `ArmyPower`: army power = `(total / this) × morale`. |
| `winnerCasualtyNumerator` | integer | ratio numerator | The `40` of `ratio = loserPower × 40 / winnerPower`, fed to the per-unit casualty formula below. |
| `casualtyDivisorBase` | integer | divisor base | Per-unit casualty: `troops -= troops / (Random(casualtyDivisorRandomSpan) + this) × ratio`. |
| `casualtyDivisorRandomSpan` | integer | random band width | Width of the random band added to `casualtyDivisorBase`. |
| `deletionDivisorNational` | integer | divisor | A surviving national unit (mercenary label 0) below `standardBattalionSize / this` troops is deleted outright. |
| `deletionDivisorMercenary` | integer | divisor | The same deletion threshold for a mercenary unit — more forgiving than the national divisor. |
| `absorbedSupplyTroopDivisor` | integer | divisor | Reserved field; not yet read by any engine code. |
| `qualityFloor` | integer | quality units | A winning unit's surviving quality is raised to at least this. |
| `qualityCap` | integer | quality units | Ceiling a promotion roll cannot push quality above. |
| `promotionChanceDenominator` | integer | 1-in-this | Chance a winning unit's quality is promoted one further tier after the floor is applied. |
| `unitySwing` | integer | unity points | Flat unity change applied to the winner (gain) and loser (loss) after a field battle. |
| `autoPeaceChanceNumerator` / `autoPeaceChanceDenominator` | integer | fraction | Chance two AI nations make peace after a qualifying battle. |
| `autoPeaceLoserUnityThreshold` | integer | unity | The auto-peace roll only applies while the loser's unity is above this. |
| `autoPeaceLoserCityThreshold` | integer | cities | The auto-peace roll only applies while the loser holds more than this many cities. |
| `naval` | object | — | The naval variant of the instant resolver. See "Naval combat" below. |
| `scatteredDefeat` | object | — | The `improved` ruleset's alternative to annihilating the loser. See "Scattered defeat" below. |
| `detailedResolver` | object | — | Reserved constants for an optional, not-yet-wired resolver. See "Detailed resolver (reserved)" below. |
| `_provenance` | object | No | Provenance map. |

#### Naval combat (`combat.naval`)

`NavalCombatRules`: the naval variant of the instant resolver.

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `conditionDivisor` | integer | divisor | Fleet power base = `ships × conditionPercent / this`. |
| `carriedArmyPowerDivisor` | integer | divisor | A carried army's siege strength adds `siegeStrength / this` to the fleet's power. |
| `randomBandCount` | integer | exclusive upper bound | Upper bound of the random draw used for the power variance band. |
| `randomBandPercent` | integer | percent of base | The random bonus band added to base power = `baseValue × this / 100`. |
| `winnerDamageDivisor` | integer | divisor | Winner's own ships/condition lost = `winner.ships × damage / this` (and the same for condition). |
| `damageRatioScale` | integer | scale | `ratio = max(1, loserPower × this / winnerPower)`; `damage = ratio² / this`. |
| `unitLossDamageThreshold` | integer | damage value | Above this damage, the carried army also loses whole units. |
| `unitLossDivisor` | integer | divisor | Divisor of the carried army's whole-unit loss formula. |
| `unitySwingShipDivisor` | integer | divisor | A naval battle moves unity by `floor(loserShips / this)`, rather than the field battle's flat `unitySwing`. |
| `_provenance` | object | No | Provenance map. |

#### Scattered defeat (`combat.scatteredDefeat`)

`ScatteredDefeatRules`: selected by `flags.combatOnDefeat == "scatter"`. The loser survives at reduced
strength and scatters instead of being annihilated.

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `survivorCasualtyNumerator` | integer | ratio numerator | The scattered survivor's own casualty ratio numerator (parallel to `winnerCasualtyNumerator`, applied to the loser instead). |
| `scatterTilesMin` / `scatterTilesMax` | integer | tiles (inclusive) | Range the survivor is displaced from the battle site; both bounds are inclusive. |
| `_provenance` | object | No | Provenance map. |

#### Detailed resolver (reserved) (`combat.detailedResolver`)

`DetailedResolverRules`: constants for an optional "detailed" tactical resolver held in reserve.
**Not used by the shipped instant resolver** — carried here so that adding it later is a ruleset change,
not a model change. Field meanings below are names only, not formulas, since the resolver that would
consume them does not exist yet.

| Field | Type | Meaning |
|-------|------|---------|
| `meleeLossCapPercent` | integer | Reserved: a cap, in percent, on melee losses. |
| `meleeLossCapOffset` | integer | Reserved: an offset applied to that cap. |
| `meleeLossHardCap` | integer | Reserved: an absolute upper bound on melee losses. |
| `meleeBasePowerFloor` | integer | Reserved: a floor on a unit's base melee power. |
| `meleePowerDivisor` | integer | Reserved: a divisor for melee power. |
| `inRangeShotMultiplier` | integer | Reserved: a multiplier applied to shots within range. |
| `typeEffectivenessOrder` | array of strings | Reserved: unit-type ids giving the row/column order of `typeEffectiveness`. |
| `typeEffectiveness` | array of arrays of integers | Reserved: a square matrix of effectiveness multipliers, indexed by `typeEffectivenessOrder`. |
| `_provenance` | object | Provenance map. |

### Siege rules

The `siege` object (`SiegeRules`): the third variant of the instant resolver. The attacker's strength is
`SiegeStrength.Attacker` (`archerStrengthMultiplier`/`powerDivisor`, at the top level below); the
defender's is `SiegeStrength.Defender`, a weighted sum of the city's loyalty, fortification and
population, then two scaling branches, in this order.

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `archerStrengthMultiplier` | integer | multiplier | The besieging army's archers count `troops × this` instead of being weighted by `combatPowerWeight`; every other unit type counts troops straight. |
| `powerDivisor` | integer | divisor | Attacker power = `(total / this) × morale` — no intermediate `/100`, unlike `ArmyPower`. |
| `defenderFortificationWeight` | integer | weight | Weight of the city's finished-fortification-percent term in the defender-strength sum. |
| `defenderLoyaltyWeight` | integer | weight | Weight of the city's loyalty term in the same sum. |
| `defenderPopulationWeight` | integer | weight | Weight of the city's population-in-thousands term in the same sum. |
| `highLoyaltyThreshold` | integer | 0–100 loyalty | Above this loyalty, and only for the city's controlling nation's capital, the weighted sum is scaled up. |
| `highLoyaltyBonusNumerator` / `highLoyaltyBonusDenominator` | integer | fraction | The `×numerator/denominator` bonus applied when the capital-and-loyalty branch fires. |
| `defenderGarrisonTroopDivisor` | integer | divisor | The garrison-troops addend's divisor, added last after both scaling branches; not yet wired into `SiegeStrength.Defender` (needs per-nation recruitment-slot state the pure function does not take). |
| `defenderNonAllegiantNumerator` / `defenderNonAllegiantDenominator` | integer | fraction | The `×numerator/denominator` penalty applied when the city's owner is not its allegiance. |
| `attackerIsAllegianceDefenderReductionPercent` | integer | percent | A separate reduction applied at the siege entry point (outside `SiegeStrength`) when the attacking nation equals the city's allegiance; already applied by `InstantBattleResolver.ResolveSiege`. |
| `attritionRatioMultiplier` | integer | ratio multiplier | The besieging attacker's own casualty ratio = `clamp(defenderStrength × this / attackerStrength, attritionRatioFloor, attritionRatioCeiling)`. |
| `attritionRatioFloor` / `attritionRatioCeiling` | integer | clamp bounds | Bounds of the same clamp — a siege always costs the attacker at least the floor, whatever the odds. |
| `erosionFloorNumerator` / `erosionFloorDenominator` | integer | fraction | Per-attempt erosion's floor term: `field × numerator / denominator`, applied to loyalty, then fortification, then population, in that order, on every attempt (win or lose). |
| `erosionCeilingNumerator` / `erosionCeilingDenominator` / `erosionCeilingAddend` | integer | fraction + addend | Erosion's ceiling term: `field × numerator / denominator + addend`. A failed siege always takes this branch. |
| `populationFloorDivisor` / `populationFloorAddend` | integer | divisor + addend | Post-erosion population floor: `population = max(population, maxPopulation / divisor + addend)`, applied once after the three erosion passes. |
| `_provenance` | object | No | Provenance map. |

See `docs/investigations/siege-defender-strength.md` for the full decompilation this section summarizes.

### Loyalty rules

The `loyalty` object (`LoyaltyRules`): the single-city capture/defection loyalty-transfer clamp bounds,
and the mass-conquest transfer's own, textually different clamp bounds.

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `forcedCaptureFloor` | integer | 0–100 loyalty | Lower bound of the non-allegiant forced-capture clamp: `max(this, min(forcedCaptureCap, nonAllegiantTransferBase − L′))`. |
| `forcedCaptureCap` | integer | 0–100 loyalty | Upper bound of the same clamp. |
| `defectionFloor` | integer | 0–100 loyalty | Upper bound of the non-allegiant defection clamp: `min(this, max(defectionFormulaFloor, nonAllegiantTransferBase − L))`. |
| `defectionFormulaFloor` | integer | 0–100 loyalty | Lower bound of the same clamp. |
| `allegiantRecaptureTarget` | integer | 0–100 loyalty | Upper bound of the allegiant clamp (shared by forced capture and defection): `min(this, allegiantRecaptureBase − L)`. |
| `allegiantRecaptureBase` | integer | formula base | Base of the same allegiant clamp. |
| `nonAllegiantTransferBase` | integer | formula base | Shared base of the non-allegiant clamps (also used by the conquest transfer's non-allegiant formula). |
| `tierDivisor` | integer | divisor | Unrelated to the transfer formulas: the loyalty-tier display divisor. |
| `conquestAllegiantCap` | integer | 0–100 loyalty | Mass-conquest transfer's own allegiant clamp: `min(this, conquestAllegiantBase − L)` — deliberately different from the single-city allegiant pair above. |
| `conquestAllegiantBase` | integer | formula base | Base of the same conquest-transfer allegiant clamp. |
| `conquestNonAllegiantCap` | integer | 0–100 loyalty | Mass-conquest transfer's non-allegiant clamp: `min(this, max(conquestNonAllegiantFloor, nonAllegiantTransferBase − L))`. |
| `conquestNonAllegiantFloor` | integer | 0–100 loyalty | Lower bound of the same clamp. |
| `_provenance` | object | No | Provenance map. |

`L`/`L′` above are the city's loyalty before/after any siege erosion already applied at transfer time.
`IC2.Engine.Cities.Capture.CityCaptureResolver` applies the single-city formulas;
`IC2.Engine.Cities.Capture.ConquestCascade` applies the conquest ones.

### Capture rules

The `capture` object (`CaptureRules`): city-capture economy and unity, the cascading-defection mechanic,
nation elimination, the capital-move fallback, and the quarterly rebellion check.

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `captureTreasuryCreditMultiplier` | integer | multiplier | New owner's treasury credit at a forced capture: `treasury += contribution × this`. |
| `captureUnityGain` | integer | unity points | New owner's unity gain at a forced capture (clamped to `economy.unityCap`). |
| `captureUnityLoss` | integer | unity points | Old owner's unity loss at a forced capture (not floored). |
| `defectionTreasuryCreditMultiplier` | integer | multiplier | New owner's treasury credit at a defection: `treasury += contribution × this`. |
| `defectionUnityGain` | integer | unity points | New owner's unity gain at a defection (clamped to `economy.unityCap`). |
| `defectionUnityLoss` | integer | unity points | Old owner's unity loss at a defection, floored at `defectionUnityLossFloor`. |
| `defectionUnityLossFloor` | integer | unity floor | Floor `defectionUnityLoss` does not push the old owner's unity below. |
| `eliminationUnityReset` | integer | unity value | A nation's unity once its last city is gone. |
| `cascadeDistanceMax` | integer | tiles (Chebyshev) | The forced-capture cascade only considers another city within this distance of the besieging army. |
| `cascadeUnityThreshold` | integer | unity | The cascade only fires while the losing nation's own unity (read live) is below this. |
| `cascadeLoyaltyThreshold` | integer | 0–100 loyalty | The cascade only considers a candidate city whose loyalty is below this. |
| `cascadeAllegiantDefenseDivisor` | integer | divisor | A candidate city's complete defender strength is divided by this when its allegiance already matches the new owner. |
| `conquestCityCountThreshold` | integer | cities | A non-capital capture leaving the loser with fewer than this many cities conquers it outright. |
| `capitalMoveUnityThreshold` | integer | unity | When the loser's capital falls, it may attempt to move its capital only while unity is over this. |
| `capitalMoveCityCountThreshold` | integer | cities | The capital-move attempt (capital-fall case only) also requires more than this many cities. |
| `capitalMoveUnityLoss` | integer | unity points | Cost of attempting to move the capital, whether or not a destination is found. |
| `capitalMoveMinDistanceTiles` | integer | tiles (Chebyshev, strict `<`) | The capital only moves to a city strictly more than this many tiles away. |
| `capitalMoveStrengthDivisor` | integer | divisor | A candidate destination's own defender strength is divided by this, then by its distance from the fallen capital, before comparing candidates. |
| `capitalMoveNewCapitalLoyaltyGain` | integer | loyalty points | New capital's loyalty gain on a successful move, capped at `capitalMoveNewCapitalStatCap`. |
| `capitalMoveNewCapitalFortificationGain` | integer | fortification-word units | New capital's raw fortification-word gain, capped the same way (applied to the stored word, not the decoded percentage). |
| `capitalMoveNewCapitalStatCap` | integer | cap | Shared cap for the loyalty and fortification gains above. |
| `capitalMoveNewCapitalPopulationGain` | integer | population (thousands) | New capital's population gain, uncapped. |
| `capitalMoveNewCapitalMaxPopulationGain` | integer | population (thousands) | New capital's maximum-population gain, uncapped. |
| `capitalMoveNewCapitalTributeGain` | integer | tribute | New capital's tribute gain, uncapped. |
| `conquestWinnerUnityGain` | integer | unity points | Winner's unity gain on conquering a nation outright (clamped to `economy.unityCap`); far larger than a single-city `captureUnityGain`. |
| `conquestLoyaltyRandomBonusMax` | integer | exclusive upper bound | `+ Random(this)` added to every moved city's loyalty during a conquest's mass transfer, one draw per city. |
| `conquestTreasuryCreditMultiplier` | integer | multiplier | Winner's per-city treasury credit during a conquest's mass transfer. |
| `rebellionArmyDistanceMax` | integer | tiles (Chebyshev, strict `<`) | An army of a nation at war with the city's owner only qualifies for the quarterly rebellion check within this distance. |
| `rebellionNeighbourScoreDistanceWeight` | integer | weight | Rebellion candidate score: `cities(n) − this × distance(city, capital(n))`. |
| `rebellionNeighbourScoreFloor` | integer | score floor | The running best rebellion candidate score starts here before any candidate is scored. |
| `_provenance` | object | No | Provenance map. |

### Diplomacy rules

The `diplomacy` object (`DiplomacyRules`): the confirmed diplomatic state machine, its cooldowns, the
reparation formula, and (nested) the AI's own direct diplomacy gates.

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `stateCodes` | object | — | The four relation codes. See "Relation state codes" below. |
| `cooldownAfterBrokenTrade` | integer | relation-column value | Relation column written when trade is broken (negative; counts up toward 0 as it thaws). |
| `cooldownAfterBrokenAlliance` | integer | relation-column value | Relation column written when an alliance is broken. |
| `cooldownAfterEndedWar` | integer | relation-column value | Relation column written when a war ends. |
| `cooldownAfterPeaceTerms` | integer | relation-column value | Relation column written to the loser's surviving trade/alliance relations by peace terms. |
| `cooldownAfterAllyPeace` | integer | relation-column value | Relation column written to an ally still at war with the other side after peace terms. |
| `thawPerQuarter` | integer | relation-column step | Every negative relation column gains this much per quarter. |
| `thawBonus` | integer | relation-column step | Extra thaw applied with probability `1/thawBonusChanceDenominator`: `min(0, v + thawBonus)`. |
| `thawBonusChanceDenominator` | integer | 1-in-this | Chance the extra thaw bonus applies. |
| `maxTradePartners` | integer | count | Maximum simultaneous trade partners a nation may have. |
| `faithfulThawColumnLimit` | integer | columns | Number of a nation's 16 relation columns the quarterly thaw loop processes when `flags.faithfulThawColumnBug` is `true` — the confirmed original bug. |
| `reparationsWealthDivisor` | integer | divisor | Reparations formula: `wealth / this + Random(wealth / this) + cityCount × reparationsPerCity`. |
| `reparationsPerCity` | integer | talents/city | Per-city term of the same formula. |
| `offerRollDenominator` | integer | 1-in-this | Chance a human-facing AI turn-start candidate becomes a pending offer at all. |
| `aiOwnDiplomacy` | object | — | The AI's own, no-consent diplomacy gates. See "AI's own diplomacy" below. |
| `_provenance` | object | No | Provenance map. |

#### Relation state codes (`diplomacy.stateCodes`)

`RelationStateCodes`: the numeric codes the relation matrix stores for each diplomatic state (no `_provenance` of its own — annotate individual entries on the parent `diplomacy._provenance` with dotted paths, e.g. `"stateCodes.war"`).

| Field | Type | Meaning |
|-------|------|---------|
| `peace` | integer | Code for the peace state. |
| `trade` | integer | Code for the trade state. |
| `alliance` | integer | Code for the alliance state. |
| `war` | integer | Code for the war state. |

#### AI's own diplomacy (`diplomacy.aiOwnDiplomacy`)

`AiOwnDiplomacyRules`: the AI's own per-turn war-targeting, alliance roll and partner search. Trade's own
cap is the parent's `maxTradePartners`, shared with the human-initiated path.

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `busyMobilizationThreshold` | integer | mobilization % | An AI already at war, or mobilized over this percent, or in the last season of the year, is "busy": declares no war and makes no alliance this turn. |
| `warTargetRatioBase` | integer | ratio floor | A war-target candidate's power ratio must exceed this (the loop's own running "best" starts here). |
| `warTargetRatioMultiplier` | integer | multiplier | War-target ratio = `this × P(me) / max(1, P(k))`. |
| `warDeclareRollDenominator` | integer | 1-in-this | Given a war target, the chance the AI actually declares this turn. |
| `allianceRollDenominator` | integer | 1-in-this | Given the AI is not busy, the chance it looks for an alliance partner this turn. |
| `allianceMaxPartnerWars` | integer | wars | The alliance partner search only considers an AI with fewer than this many active wars. |
| `powerWealthDivisor` | integer | divisor | War-target power formula: `P(n) = (wealth / this) × (unity / powerUnityDivisor)`. |
| `powerUnityDivisor` | integer | divisor | The unity-term divisor of the same formula. |
| `_provenance` | object | No | Provenance map. |

### City orders rules

The `cityOrders` object (`CityOrderRules`): a generic, data-driven list of city work orders. `fortify` is
the only shipped entry, not the only possible one.

| Field | Type | Meaning |
|-------|------|---------|
| `orders` | array of `CityOrderRule` | The order table. See below. |
| `_provenance` | object | Provenance map. |

`CityOrderRule` (one entry per order type):

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `id` | string | — | Stable key, e.g. `"fortify"`. |
| `maxPercent` | integer | 0–100 | The completed value the order may not exceed. |
| `inProgressEncodingRadix` | integer | radix | Multiplier used to encode a pending order into the stored word (`value += points × radix`); also the threshold above which the word means "in progress". See "Fortification code" under World files' Cities section. |
| `costPerPointPerPopulationThousand` | integer | talents | Cost per order point, per thousand population. |
| `refusedWhileUnderSiege` | boolean | — | Whether the order may be issued for a besieged city. |
| `wipedBySiegeAttempt` | boolean | — | Whether a siege attempt clears a pending order. |
| `_provenance` | object | — | Provenance map. |

### News log rules

The `newsLog` object (`NewsLogRules`): the news log's ring-buffer geometry, slot size, and round-header season names.

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `ringBufferSlots` | integer | slots | Ring buffer capacity (40 shipped); the oldest entry is shifted out once full. |
| `messageByteLength` | integer | bytes | Fixed slot size. The slot is a NUL-terminated string, so at most `this − 1` bytes of text ever reach the log. |
| `seasonNames` | array of strings | one per season | Season names for the round-tick week header, in `CalendarState.SeasonIndex` order (index 0 = Spring). |
| `_provenance` | object | No | Provenance map. |

### Map marker rules

The `mapMarkers` object (`MapMarkerRules`): size tiering for map markers. The original encodes owner and
size together in one marker code; these are the size-band thresholds a renderer turns into per-tier icons.

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `armyTroopTierThresholds` | array of integers | troop counts | Ascending thresholds splitting armies into size tiers (e.g. `[25000, 50000]` — 3 tiers). |
| `fleetShipTierThresholds` | array of integers | ship counts | Same, for fleets. |
| `cityPopulationTierThresholds` | array of integers | population thresholds | Same, for cities — ships empty in the shipped rulesets: a 5-value city-marker display axis is evidenced, but no decompiled banding function or boundary values exist yet. Do not invent thresholds here. |
| `_provenance` | object | No | Provenance map. |

### Victory rules

The `victory` object (`VictoryRules`): which victory condition a scenario gets when it does not state one, and the hard end year.

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `defaultCondition` | enum | `VictoryConditionType` | The condition a scenario gets if it omits its own `victory.type`. |
| `totalConquestRequiresEveryCity` | boolean | — | Whether `totalConquest` requires literally every city on the map (`true`, the original's own rule) rather than some other threshold. |
| `hardEndYearBc` | integer | year (BC positive) | The year the original's own end-game screen compares against; whether this is the actual turn-loop trigger or only that screen's wording is not re-checked. |
| `defaultTurnLimit` | integer or null | turns | Turn limit a scenario gets if it omits its own `turnLimit` (`null` in `classical-faithful`, since the original has no turn limit distinct from the year comparison above). |
| `_provenance` | object | No | Provenance map. |

### AI weights rules

The `ai` object (`AiWeightsRules`): the heuristic AI's scoring constants. **Every field here is
`[designed]`, and that is the expected answer** — the original's own AI lives in unnamed, un-decompiled
code and is out of scope for reverse-engineering by standing project decision
(`docs/design-audit.md` §1). Every field is an integer (`int` or `long`, never a floating-point type), on
one shared scale, so the AI's determinism guarantee never depends on floating-point rounding.

| Field | Type | Range / unit | Meaning |
|-------|------|--------------|---------|
| `permilleScale` | integer | denominator (1000 shipped) | Fixed-point denominator every personality parameter and every scoring ratio is expressed in (parts per thousand). |
| `defaultPersonalityPermille` | integer | 0–permilleScale | Value substituted for an AI seat's absent personality parameter (the midpoint of the declared 0–1 range). |
| `maxActionsPerTurn` | integer | commands | Hard ceiling on commands one AI seat may place in one turn, checked before anything else. |
| `minimumActionScore` | long | score | A candidate must reach this score to be worth placing at all. |
| `besiegeCityBaseScore` | long | score | Base score for besieging an adjacent enemy city — the highest of the per-phase base scores. |
| `attackArmyBaseScore` | long | score | Base score for attacking an adjacent enemy army. |
| `attackFleetBaseScore` | long | score | Base score for attacking an adjacent enemy fleet. |
| `approachCityBaseScore` | long | score | Base score for marching an army at an enemy city it is not yet adjacent to. |
| `sailAtFleetBaseScore` | long | score | Base score for sailing a fleet at an enemy fleet. |
| `reinforceCityBaseScore` | long | score | Base score for marching an army at one of its own cities a hostile army stands next to. |
| `recruitBaseScore` | long | score | Base score for placing a standing-recruitment order. |
| `mobilizeReadyRecruitBaseScore` | long | score | Base score for mobilizing a fully ready recruitment slot into an army unit. |
| `fortifyBaseScore` | long | score | Base score for ordering fortification at an owned city. |
| `requiredAttackRatioAtZeroAggressionPermille` | long | 0–permilleScale | Strength ratio an attack must show at `aggression = 0`. |
| `requiredAttackRatioAtFullAggressionPermille` | long | 0–permilleScale | Strength ratio an attack must show at `aggression = 1`. |
| `maxRatioScoreContribution` | long | score | Cap on how much the strength ratio can add to an attack's score. |
| `threatenedCityBonus` | long | score | Added to the score of every action answering a threatened city. |
| `distancePenaltyPerTile` | long | score/tile | How far a score decays per tile between an army and its target. |
| `victoryProgressWeight` | long | weight | Weight victory-awareness puts on a nation's progress toward the victory condition. |
| `treasuryCommitFloorPermille` | long | 0–permilleScale | Share of the treasury an AI with `expansionDrive = 0` will commit in one turn. |
| `treasuryCommitExpansionPermille` | long | 0–permilleScale | How much more of the treasury full `expansionDrive` unlocks (added to the floor above). |
| `maxOpenRecruitmentOrdersPerCity` | integer | orders | How many standing-recruitment orders the AI leaves open at one city at a time. |
| `maxFortifyPointsPerOrder` | integer | order points | Largest fortification order the AI will place in one command. |
| `ownWarDeclarationScore` | long | score | Score for the AI's own war-declaration candidate — comfortably above every other score. |
| `ownAllianceScore` | long | score | Score for the AI's own alliance-proposal candidate. |
| `ownTradeScore` | long | score | Score for the AI's own trade-proposal candidate. |
| `ownTradeSwapScore` | long | score | Score for the AI's own trade-partner-swap candidate, slightly below `ownTradeScore`. |
| `_provenance` | object | No | Provenance map. |

### Flags

The `flags` object (`RulesetFlags`): the formula-variant selectors. Each one picks between the original's
confirmed behaviour and a designed alternative; the two shipped presets (`classical-faithful`, `improved`)
are just two settings of this record.

| Field | Type | Values | Meaning |
|-------|------|--------|---------|
| `diplomacyModel` | enum | `"confirmedStateMachine"`, `"confirmedStateMachineWithOpinionScore"` | Whether diplomacy is the confirmed state machine alone, or with an AI opinion-score layer on top. |
| `economyPurses` | enum | `"perUnitPurses"`, `"centralTreasury"` | Whether supply/mercenary purchases are paid from per-unit purses (original) or a central treasury. |
| `seatAsymmetry` | enum | `"faithful"`, `"normalized"` | Whether confirmed human-vs-AI rule differences are reproduced (`faithful`) or normalized to one rule for every seat. |
| `combatOnDefeat` | enum | `"destroyed"`, `"scatter"` | Whether a battle's loser is destroyed outright (original) or survives reduced and scatters (`combat.scatteredDefeat`). |
| `faithfulThawColumnBug` | boolean | — | `true` reproduces the confirmed bug where the quarterly diplomatic thaw only ever touches the first `diplomacy.faithfulThawColumnLimit` of each nation's 16 relation columns; `false` thaws every column. |
| `bugPolicySiegeRatioClamp` | enum | `"reproduce16BitClamp"`, `"clamp32Bit"` | Whether the siege attrition ratio and erosion ratio term reproduce the original's signed 16-bit wraparound (`classical-faithful`) or are computed in ordinary 32-bit arithmetic, never wrapping (`improved`). |
| `_provenance` | object | No | Provenance map. |

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
| `randomSeed` | integer | Yes | RNG seed for reproducible games — an **unsigned 64-bit integer** (`ulong`), not a plain `int`; set to a fixed value for testing. |
| `_provenance` | object | No | Provenance map. |

### Seats

Each seat in `seats` is an object assigning one nation to a player. **Neither `Seat` nor `AiPersonality`
carries its own `_provenance` field** — annotate an invented personality value on the scenario's own
top-level `_provenance` map instead, using a dotted path such as `"seats[carthage].personality"` (see the
convention used by `data/scenarios/example-classical-improved.json`).

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `nation` | string | Yes | Nation `id` (must be in the world's nation list). |
| `control` | enum | Yes | Control type: `"human"` or `"ai"`. |
| `personality` | object or null | No | AI personality parameters (required if `control == "ai"`). See "AI personality" below. |

### AI personality

If a nation is controlled by the AI, the `personality` object defines its behavior. Every value here is
`[designed]` (`docs/design-audit.md` §1: the original's AI is out of scope for reverse-engineering by
standing decision), so a personality is never a "confirmed" claim about the original game.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `aggression` | number | Yes | Aggression level (0–1; higher = more aggressive). |
| `expansionDrive` | number | Yes | Desire to expand territory (0–1). |
| `loyaltyToAlliances` | number | Yes | Tendency to keep alliances (0–1; higher = more loyal). |

### Victory condition

The `victory` object specifies the win condition. `type` is serialized in camelCase, exactly like every
other enum in these files.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `type` | enum | Yes | One of exactly four `VictoryConditionType` values (`src/IC2.Engine/Model/Ruleset.cs`): `"totalConquest"` (hold every city on the map — the original's only win), `"domination"` (hold every city belonging to nations still at war with you), `"scoreAtTurnLimit"` (highest weighted score when the turn limit is reached), or `"custom"` (a goal stated by the scenario itself). There is no `"diplomaticVictory"` value. |
| `goal` | string or null | No | Custom goal text (required if `type == "custom"`, unused otherwise). |

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
    "taxRateDivisor": 80,
    "_provenance": {
      "taxRateDivisor": "designed: lowered from the shipped 100 so a given tax rate yields more income, making the economy tighter to balance around. Searched docs/reports/ and found no evidence the original ever used a different divisor, so this is a design choice, not a correction."
    }
    /* ... rest of economy (every other field from the copied ruleset, unchanged) ... */
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
