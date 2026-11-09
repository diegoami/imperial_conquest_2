# Near East passes and foothills: proposal for #1822

**Status: proposal, awaiting owner approval.** Authoring source changed (`tools/NearEast/build_campaigns.py`, `terrain()`); the generated PNG, shared terrain sidecar, world revision and saves are **not** regenerated yet. Rule: [terrain-authoring.md](terrain-authoring.md). Compatibility decision (owner, 2026-11-09): ship as a **new world/terrain revision**; existing saves keep the revision they were created with.

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

## Crossing cost (plain 1, desert 1, forest 2, mountain 4; cheapest 8-neighbour path vs. straight-line steps)

| Route | Before | After |
|---|---|---|
| Tarsus → Kanesh | 30 / 19 steps (1.58×) | 19 (1.00×) |
| Tarsus → Hattusa | 48 / 31 (1.55×) | 37 (1.19×) |
| Assur → Godin | 52 / 38 (1.37×) | 39 (1.03×) |
| Arrapha → Giyan | 50 / 30 (1.67×) | 36 (1.20×) |
| Babylon → Giyan | 45 / 30 (1.50×) | 38 (1.27×) |
| Susa → Sialk | 35 / 25 (1.40×) | 31 (1.24×) |
| Sialk → Marlik | 36 / 29 (1.24×) | 36 (1.24×) |
| Nineveh → Tushpa | 40 / 22 (1.82×) | 22 (1.00×) |
| Arbela → Qalaichi | 39 / 18 (2.17×) | 18 (1.00×) |
| Sparta → Argos | 18 / 6 (3.00×) | 6 (1.00×) |
| Pylos → Sparta | 16 / 6 (2.67×) | 6 (1.00×) |

Note: before this change the cheapest path already threaded the noise-made forest gaps, so the main crossings cost 1.4–1.7×, not a sealed wall. The worst cases were the local ones (Peloponnese, Zab, Tigris). Sialk → Marlik is unchanged (the Elburz crossing is not the binding cost there).

## Not yet done (needs the owner's approval first)

- The numbers above come from a standalone cost search over the terrain grid, **not** the production pathfinder, normal army budgets, AI navigation or opening-access checks. The engine check is the next step once the pass plan is approved.
- Regenerating `near-east.png` and the sidecar, the new revision identifier, and the new-game/old-save behaviour test.
- Whether the passes are historically placed well enough: coordinates are mine and should be reviewed. Sialk → Marlik may want its own pass.
- Greek mainland and Anatolian foothill belts outside the four authored bands are untouched.
