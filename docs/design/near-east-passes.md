# Near East passes and foothills (#1822)

**Status: owner-approved 2026-11-09 and implemented as terrain revision 2.** The generator (`tools/NearEast/build_campaigns.py`, `terrain()`), the PNG, the new sidecar `data/worlds/near-east-r2.terrain.b64` and the worlds `near-east-1350bc-r2` / `near-east-750bc-r2` are committed together. Rule: [terrain-authoring.md](terrain-authoring.md). Compatibility decision (owner, 2026-11-09): a **new world/terrain revision**; existing saves keep the revision they were created with.

**How that works.** A save records its `WorldId`, and the load path resolves the world from the save, not from the scenario. The original `near-east-1350bc` and `near-east-750bc` worlds and their shared sidecar are therefore **untouched** and still load old saves. The scenarios keep their IDs (guides, menus and tests are unaffected) but now point at the `-r2` worlds, so new games use the corrected terrain. City, army and fleet positions are identical in both revisions (checked by test). Rulesets are unchanged.

## What changes

1. Inside each ridge band, **mountain only on the crest** (the inner 50% of the band's radius, where the existing noise still allows it); the rest of the band is **forest** (lower-hill proxy, cost 2).
2. Seven authored **plain corridors** (radius 0.1°, about two cells wide), applied after the ridge pass and before lakes and rivers, so water topology is untouched. Each is an abstraction named for the route it approximates, not a surveyed road:

| Pass | Belt |
|---|---|
| Cilician Gates | Taurus |
| Kermanshah–Hamadan road | Zagros |
| Susa to the plateau | Zagros |
| Greater Zab to Lake Urmia | Zagros |
| Upper Tigris to Lake Van | Taurus/Zagros |
| Tehran to the Caspian | Elburz |
| Taygetus crossing | Peloponnese |

Terrain codes, movement and combat costs, rivers and coastlines are unchanged. Cells changed: 1,363 (mountain→forest 1,151; mountain→plain 148; forest→plain 64). Mountain tiles on the map: 2,375 → 1,076. No cell becomes mountain. Before/after: [near-east-passes-before-after.png](near-east-passes-before-after.png) (left now, right proposed; plain green, forest dark green, mountain brown).

## Crossing cost under production rules

Measured by `tests/IC2.Engine.Tests/Campaigns/NearEastPassesTests.cs`: the cheapest route over the production move-cost table, then walked cell by cell through the production `MovementWalker`, which must charge the same total. Army budget 8 moves per turn. Route cost / straight-line steps, turns in brackets (before → after):

| Route | Before | After |
|---|---|---|
| Tarsus → Kanesh | 30 / 19 (4) | 19 (3) |
| Adana → Kanesh | 29 / 19 (4) | 21 (3) |
| Tarsus → Hattusa (1350) | 48 / 31 (6) | 37 (5) |
| Assur → Godin | 53 / 38 (7) | 40 (5) |
| Arrapha → Giyan | 50 / 30 (7) | 36 (5) |
| Babylon → Giyan | 48 / 30 (6) | 41 (6) |
| Susa → Sialk | 35 / 25 (5) | 31 (4) |
| Susa → Godin | 37 / 24 (5) | 26 (4) |
| Nineveh → Tushpa | 40 / 22 (5) | 22 (3) |
| Arbela → Qalaichi | 39 / 18 (5) | 18 (3) |
| Sparta → Argos | 17 / 6 (3) | 6 (1) |

Every sampled route is no dearer than before and at most 1.4x its straight-line steps. **Babylon → Giyan is unchanged at six turns**: its remaining cost is river-valley terrain near Babylon, not the Zagros belt, so a pass would not fix it. Note that a one-click move walks a straight Bresenham line and cannot route around a belt: the passes help a player who places waypoints, and the AI's own routing, not an unguided straight click across a crest. The effect on a given route depends on a waypoint reaching the corridor.

## Validation done

- Terrain revision changes only relief: every changed cell is mountain→forest/plain or forest→plain; water, rivers, desert and coast are identical; no cell becomes mountain; every centre stays on a passable tile.
- New games resolve to `-r2`; the original worlds still resolve and differ from them.
- Existing campaign tests run on the revised worlds: 200 real AI seat turns per campaign with no rejections, mismatches or full-round stalls, free land approaches for every centre, native reload, and the source PNG matching the sidecar.
- Importer: `TerrainImport` reproduces `near-east-r2.terrain.b64` byte for byte from the committed PNG.

## Not done

- Pass coordinates are my authored abstractions and have had no historical review beyond the route names. Sialk → Marlik (Elburz) is unchanged and may want its own crossing.
- Greek mainland and Anatolian foothill belts outside the four authored bands are untouched.
- No new-game playthrough on the revised worlds beyond the automated AI turns; no manual visual review of the in-game map.
