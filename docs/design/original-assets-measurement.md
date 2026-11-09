# Original assets: first measurement (T292a, #1849 / #1740)

**Scope and honesty.** A bounded, read-only census of one installation (the owner's `Imperial Conquest 2.exe`, `.dat` and `WAVS` folder), made with `tools/OriginalAssets/pe_census.py` and `form_bitmaps.py`. The scripts print counts only and write nothing, so no original bytes, pixels or captures are in the repository. This measures **what is physically there and in what container**. It does **not** show that any category already renders in the game, nor map each image to a game key; that is T292b. Other builds (`fast`, `watch`, rolling-save executables) were not compared.

## What the installation holds

| Container | Finding |
|---|---|
| `Imperial Conquest 2.exe`, PE resources | 29 Delphi form streams (`TPF0`, `RCDATA`, about 733 KB), 10 plain `BITMAP` resources (36x18 or 36x19, 4-bit, 4.6 KB), 6 cursors, 1 icon, 20 string tables, a version block. No fonts, no wave resources. |
| Bitmaps embedded in the form streams | 85 images found by header scan: 47 at 20x20 4-bit, 16 at 40x20 4-bit, and larger 8-bit images each paired with a 1-bit image of the same size (the mask): 128x160 (6 pairs), 128x64 (2), 128x128, 32x32, and a 1280x280 strip in `TAREAMAP`. They sit in `TAREAMAP` (17), `TUNITMAP` (31), `TPREMIERFORM` (26), `TBATTLEMAP` (10) and `TCELLAUTO` (1); 24 other forms hold none. |
| `WAVS/` | 10 files, all mono, 44.1 kHz, 16-bit PCM. |
| `Imperial Conquest 2.dat` | 140,706 bytes, mostly zero or small values; a data table, not artwork. Not analysed further here. |
| `.hlp` / `.cnt` | Windows help; manual text, not game art. |

## Category verdicts (provisional, to be confirmed by T292b)

| Category | Verdict | Basis and gap |
|---|---|---|
| Sounds (10) | **Loads directly** | Standard PCM WAV; the shipped game has ten sound keys and the specification records ten original sounds. Mapping order still needs the research report. |
| Terrain tiles | **Needs conversion** | Most likely the 1280x280 8-bit strip plus mask in `TAREAMAP`; extract from the form stream, cut into tiles, convert to PNG in the user cache. Tile order and the river-edge set are not yet verified against the game's terrain codes. |
| Unit and battle pieces | **Needs conversion** | The 128x160 and 128x64 8-bit images with masks in `TUNITMAP` and `TBATTLEMAP`; palette recolouring per nation is a separate, documented behaviour (research reports of 2026-10-09). |
| Toolbar and button glyphs | **Needs conversion** | 20x20 and 40x20 4-bit images in the form streams; 16-colour palettes, so the transparent colour and the per-form palette must be decided. |
| City, army, fleet markers | **Unknown** | The 36x18 bitmaps and the research reports suggest marker art and drawing both exist; whether markers are bitmaps, drawn shapes or both has to be read from the research repo before any claim. |
| Cursors | **Possible, optional** | Six cursor resources; not in the authored game's key set. |
| Fonts | **Not embedded** | Not in the executable, so the original look uses system fonts; the remake keeps its approved fonts. |
| Dialog and form layout | **Out of scope** | 29 form streams record original layout, but decision 1 of the contract is pack only. |

## Consequences for the contract

- A **pack** is feasible for sounds, terrain, unit pieces and glyphs; fallback per key to `authored` stays essential because markers and some newer scenario keys (Diadochi, Near East names) have no original counterpart.
- The extractor must read PE resources and Delphi form streams, not loose files: conversion needs a private step and a cache, as the contract already assumes. A palette decision is needed for the 4-bit images.
- Next (T292b): map each embedded image to an `AssetKeys` key with research-repo evidence, then write the cache builder with synthetic fixtures. Nothing in this page has been run in the game.

## Update: terrain tiles implemented (T292b, images slice)

The research extraction spec (`docs/reports/2026-11-07-original-assets218-extraction-spec.md` in the research repo) gives the image-list format and the key crosswalk, so the first image category is implemented: `TUnitMap.TerrainList` cells 0 to 5 become `terrain.sea_calm`, `sea_rough`, `plain`, `desert`, `forest` and `mountain` tiles (32x32, opaque, no mask used). The stored bitmap is `[u32 colour-BMP length][u32 image count][8-bit BMP][1-bit mask BMP]`; cells are read row-major from the top left. They are written all-or-none because the map draws the six from one texture array that needs equal sizes. Verified on the owner's installation locally (6 of 6, appearance matches the sea/plain/desert/forest/mountain crosswalk) and by `OriginalTerrainTileTests` with a synthetic executable. Not yet built: rivers (six shapes behind one key), city/capital markers (16 owner pictures), army/fleet markers and battle pieces (nation recolouring), glyphs (palette and transparency), mask polarity. Open research gap: diegoami/imperial-conquest-2-research#223.
