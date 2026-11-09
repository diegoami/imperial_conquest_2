# Original assets: first measurement (T292a, #1849 / #1740)

**Scope and honesty.** A bounded, read-only census of one installation (the owner's `Imperial Conquest 2.exe`, `.dat` and `WAVS` folder), made with `tools/OriginalAssets/pe_census.py` and `form_bitmaps.py`. The scripts print counts only and write nothing, so no original bytes, pixels or captures are in the repository. This measures **what is physically there and in what container**. It does **not** show that any category already renders in the game, nor map each image to a game key; that is T292b. Other builds (`fast`, `watch`, rolling-save executables) were not compared.

## What the installation holds

| Container | Finding |
|---|---|
| `Imperial Conquest 2.exe`, PE resources | 29 Delphi form streams (`TPF0`, `RCDATA`, about 733 KB), 10 plain `BITMAP` resources (36x18 or 36x19, 4-bit, 4.6 KB), 6 cursors, 1 icon, 20 string tables, a version block. No fonts, no wave resources. |
| Bitmaps embedded in the form streams | 85 images found by header scan: 47 at 20x20 4-bit, 16 at 40x20 4-bit, and larger 8-bit images each paired with a 1-bit image of the same size (the mask): 128x160 (6 pairs), 128x64 (2), 128x128, 32x32, and a 1280x280 strip in `TAREAMAP`. They sit in `TAREAMAP` (17), `TUNITMAP` (31), `TPREMIERFORM` (26), `TBATTLEMAP` (10) and `TCELLAUTO` (1); 24 other forms hold none. |
| `WAVS/` | 10 files, all mono, 44.1 kHz, 16-bit PCM. These are the owner's **converted working copies**. The shipped files (sibling `WAVS - Copy/`) are eight 8-bit PCM at 11,025 Hz (`SOUND8` 5,512 Hz) and two 16-bit at 22,050 Hz (`SOUND2`, `SOUND9`) (asset-specification §1.4). |
| `Imperial Conquest 2.dat` | 140,706 bytes, mostly zero or small values; a data table, not artwork. Not analysed further here. |
| `.hlp` / `.cnt` | Windows help; manual text, not game art. |

## Category verdicts (provisional, to be confirmed by T292b)

| Category | Verdict | Basis and gap |
|---|---|---|
| Sounds (10) | **Needs conversion on a stock installation** | Standard PCM WAV, but the shipped files are mostly 8-bit, and the game's WAV loader takes mono 16-bit only (#1870). The order SOUND1–10 → `sfx.*` is settled by the extraction spec's sound crosswalk (research `963c58d`) and matches asset-specification §4.6. |
| Terrain tiles | **Needs conversion** (built, see below) | `TUnitMap.TerrainList`, 32x32 cells: 0–5 the six terrain codes, 6–11 the six river shapes, 12 the strategic selection. The 1280x280 strip in `TAREAMAP` is not terrain: it is `MapsList`, the two 320x140 Area-map images (research `963c58d`, #223 crosswalk §D). |
| Unit and battle pieces | **Needs conversion** | `TUnitMap.ArmiesList`/`FleetsList` (3 templates each) and `TBattleMap.BatMapList` (15 unit templates, ground, cursor), 32x32, 8-bit with masks; nation recolour of purple/white/blue (research reports of 2026-10-09; extraction spec point 5). |
| Toolbar and button glyphs | **Needs conversion** | 20x20 (47) and 40x20 nation (16) 4-bit glyphs. All use the standard 16-colour Windows palette; transparency is the bottom-left pixel's colour, no mask. All 44 `ui.command.*`/`battle.toolbar.*` keys have one original glyph each (research `963c58d`, #223 crosswalk §A–B). |
| City, army, fleet markers | **Needs conversion** | Bitmaps in `TUnitMap` image lists: `Cities1List..Cities4List` and `CapitalsList` (16 owner pictures each, never recoloured), `ArmiesList`/`FleetsList` (recolour templates). The ten 36x18/36x19 `BITMAP` resources are not identified as markers. |
| Cursors | **Possible, optional** | Six cursor resources; not in the authored game's key set. |
| Fonts | **Not embedded** | Not in the executable, so the original look uses system fonts; the remake keeps its approved fonts. |
| Dialog and form layout | **Out of scope** | 29 form streams record original layout, but decision 1 of the contract is pack only. |

## Consequences for the contract

- A **pack** is feasible for sounds, terrain, unit pieces and glyphs; fallback per key to `authored` stays essential because some keys (chariot icons, owner art outside the classical world) and newer scenario keys (Diadochi, Near East names) have no original counterpart.
- The extractor must read PE resources and Delphi form streams, not loose files: conversion needs a private step and a cache, as the contract already assumes. The 4-bit images use the standard Windows palette with a bottom-left colour key (#223 crosswalk §B), so no palette decision is left.
- Next (T292b): map each embedded image to an `AssetKeys` key with research-repo evidence, then write the cache builder with synthetic fixtures. Nothing in this page has been run in the game.

## Update: terrain tiles implemented (T292b, images slice)

The research extraction spec (`docs/reports/2026-11-07-original-assets218-extraction-spec.md` in the research repo) gives the image-list format and the key crosswalk, so the first image category is implemented: `TUnitMap.TerrainList` cells 0 to 5 become `terrain.sea_calm`, `sea_rough`, `plain`, `desert`, `forest` and `mountain` tiles (32x32, opaque, no mask used). The stored bitmap is `[u32 colour-BMP length][u32 image count][8-bit BMP][1-bit mask BMP]`; cells are read row-major from the top left. They are written all-or-none because the map draws the six from one texture array that needs equal sizes. Verified on the owner's installation locally (6 of 6, appearance matches the sea/plain/desert/forest/mountain crosswalk) and by `OriginalTerrainTileTests` with a synthetic executable. Not yet built: rivers (six shapes behind one key), city/capital markers (16 owner pictures), army/fleet markers and battle pieces (nation recolouring), glyphs (palette and transparency), mask polarity. Research #223 is answered at `963c58d` (next section).

## Update: remaining categories mapped (research `963c58d`, 2026-11-09)

The #223 crosswalk and the corrected extraction spec settle the source component, index, palette and transparency rule for every remaining image category. The per-category table, the consumer changes and the owner questions are in [original-assets-implementation-plan.md](original-assets-implementation-plan.md). Research #223 is answered there; mask polarity and native composition remain open.
