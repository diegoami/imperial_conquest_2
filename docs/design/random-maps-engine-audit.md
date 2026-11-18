# Random maps: engine audit (T294a / #1842)

**Status: draft for review.** Feeds the T294 chain (docs/tasks/T294.md): what the engine already gives a generated world, what constrains the generator and validator, and what T294b-g must measure rather than assume. Every claim cites the file it was verified against. Contract: the audit covers terrain/river encodings, limits, city/port rules, pathfinding and AI assumptions (docs/design/v011-campaigns-and-random-maps.md: "Before implementing, audit the engine's terrain/river encodings, limits, city/port rules, pathfinding and AI navigation").

## 1. Terrain and river encodings

- A world is plain data: `data/worlds/<id>.json` with `terrain.encoding="base64"` and a `dataFile` sidecar of `<H>*W` little-endian `ushort` codes (tools/China/build_campaigns.py `write_sidecar`; `TerrainGrid`, `TerrainEncoding.Base64`). A generated world can be exactly this value with `dataFile` dropped and `data` inlined for save embedding (T294e) — the encoding supports both (WorldTerrainSidecarTests covers dot-dataFile rejection and sidecar decode).
- Tile codes are an open set keyed by the world's own `tileTypes` table: 0 `sea_calm`, 1 `sea_rough` (fleet-only), 2 `plain`, 3 `desert`, 4 `forest`, 5 `mountains`, 6-11 `river_1..6` (army-passable directional river cells whose codes encode flow exits; fleet-impassable — verified on the china/italy tables: `passableByArmies` true for 2-11, `passableByFleets` true only for 0-1). A generator may define its own tile table, but every shipped ruleset's move-cost table keys on tile **ids** (`rules.terrain.moveCosts` by `tileTypeId`), so a generated world should reuse the canonical ids or ship matching costs.
- Move costs (italy/china tables): plain 1, desert 1, forest 2, mountains 4, rivers 4, sea_calm 1 (fleets), sea_rough 3 (fleets); plus `defaultMoveCost` fallback. The walker charges per entered cell; the authored-pass convention is a cost corridor, not a connectivity gate (ChinaRoutesTests doc comment).
- Rivers are drawn as directional exit codes (`italy.EXIT_CODE`, `italy.line_cells` in tools/Italy/build_italy.py). Their game meaning is army terrain and presentation; **fleets cannot enter them**, so an inland river network never substitutes for a coast. A generator wanting river harbours must either extend `passableByFleets` (a rules change — owner decision, out of T294a scope) or place ports on the sea.

## 2. Limits

- Map size: `GameDataValidation` requires only positive width/height (Serialization/GameDataValidation.cs:44); no maximum is enforced anywhere in validation. MapFitCheck exercises {160x200, 400x120, 400x220, 272x320, 208x220}; the authored family runs 8x10 tiles per degree, but nothing in the engine requires it.
- Nation/city/army counts: no engine cap; the authored worlds run 11-16 nations and 74-79 cities at these sizes with one army per seat and 0-8 fleets. The practical limit is AI round cost, which scales with seats x cities — T294g must measure a generated world's round time rather than extrapolate.
- `GameDataValidation` checks every city/army/fleet coordinate inside the map (GameDataValidation.cs:1143), city-tile army-passability, one-owner capitals, unique city tiles and free approaches — the validator (architecture item 4) can call the same routines before a generated world is accepted.

## 3. City and port rules

- A fleet order requires a city whose tile passes `CoastalCity.FirstAdjacentSeaTile` — **sea adjacency, not rivers** ( Naval/CoastalCity.cs; verified by ChinaCampaignTests' coastal-fleet test and the china build's sea-only ports). A recipe that promises naval play must generate sea-adjacent settlements; rivers are cosmetic-plus-army-terrain only.
- Landing rules (`Naval/LandingTile.cs`) need army-passable landing tiles beside fleet positions — generated coasts need landable banks, which the shared-sidecar "landing apron" pattern already guarantees for authored maps and the validator must demand of generated ones.
- Barbarian arrivals spawn on map-edge tiles (army at the boundary, optionally fleet-carried; BarbarianArrivalSystem with `ChancePercent=100` verified in ChinaCampaignTests) — a generated map must keep edge tiles that are either water (for fleet arrivals) or landable land.
- Capitals: every regular seat needs its capital city owner match and city tiles distinct (validation + the campaign tests' pinning pattern).

## 4. Pathfinding and movement

- The production walker (`Movement/MovementWalker.cs:100`) walks cell-to-cell with per-tile costs and typed stop reasons; there is no global pathfinder for armies — the AI issues step-wise orders (the India/China route tests walk production routes and assert the walker charges the same cost).
- The authored acceptance pattern ("practical crossing cost between settlement clusters within the ordinary 8-move army budget") is executable as-is for generated worlds: ChinaRoutesTests' CheapestRoute/WalkRoute pair is the template the validator's "practical crossing cost" check can generalise.
- The AI's own movement never consumes campaign RNG beyond its own seeded streams (AiGameRunner determinism verified per seed in both India and China campaign tests).

## 5. AI assumptions

- The classical navigation tables are hard-gated to `world.Id == "classical-mediterranean" && Width==320 && Height==140 && AiEconomy==Original` (Ai/ClassicalArmyNavigation.cs:22-25; the fleet side gates the same way in ClassicalFleetNavigation). **Any generated world automatically uses the generic policy** — no code change, no new tables; this is the verified basis of the T294 contract's claim.
- The generic policy's terrain interaction is proven on authored non-classical maps (Italy's 160x200-style geography, the Near East's 276x185, India's 272x320, China's 208x220 — each with 200-turn zero-rejection AI runs on two seeds). **Generated terrain is still unmeasured**: disconnected landmasses, one-tile straits and landlocked starts are generator failure modes the authored maps never produce — T294b's validator and property tests must reject them, and T294g must measure real rounds.
- The off-map barbarian role and the neighbour-relations matrix are world-data driven (`startingRelations` explicit; `NeighbourGeography` derives classical neighbour pairs from shipped data only) — a generated world ships its own relations the same way.

## 6. Implications for the chain (T294b-g)

1. The generator can emit plain `World` values; nothing in the engine needs to change for storage or validation.
2. The validator should reuse `GameDataValidation` plus the route-walk pattern; the new checks are connectivity/straits/port-adjacency/expansion-room, all decidable from the world value alone.
3. The recipe rules that matter are: every seat's capital on army-passable land with free approaches and expansion room; at least the promised number of sea-adjacent settlements; landing aprons on hostile coasts; edge tiles suitable for barbarian arrivals; no disconnected components; crossings within the 8-move budget.
4. Fleet-at-sea only: any "river navy" idea is out of scope without an owner-approved rules change.
5. Measure, don't assume: T294g's acceptance is AI rounds on generated worlds (round time, rejections, stalls), not table compatibility claims.

## Open questions for the owner (none block T294b)

- None new: the one-size, three-preview, save-embedding and authored-faction decisions are recorded (docs/tasks/T294.md, 2026-11-09).
