# Galatia's elimination and a newly found city-resupply dialog

`2_rome.txt`'s second entry: `1_rome_270_winter_5.sav → 1_rome_270_winter_7.sav` (two full weekly ticks), with `recordings/bandicam 2026-09-13 00-04-38-377.mp4` covering "Army 2 goes back south, and supplies at Pisa" / "Army 1 moves west and supplies at Mediolanum" / a string of Galatian city losses culminating in "Seleucid conquest Galatia." This pair closes the last open item in `decompiled-defection-and-siege-attrition.md` ("decompile the nation-elimination cascade if a save ever shows a nation actually being eliminated") and, unexpectedly, surfaces a second dialog nobody had gone looking for.

## Part 1: Galatia's elimination, confirmed against real data

The save diff shows 9 cities changing owner away from Galatia (nation code 12), all to Seleucid (code 2), split into two clearly distinguishable patterns:

| City | Fortification | Population | Pattern |
| --- | --- | --- | --- |
| Laranda | 41 → 30 | 59 → 44 | **forced capture** ("falls to") |
| Gordium | 54 → 42 | 23 → 18 | **forced capture** ("falls to") |
| Synnada, Pessinus, Acroinon, Ancyra, Gangra, Nyssa, Halys | unchanged | unchanged | **defection** ("defects from") |

This is an exact match to the code read in `decompiled-defection-and-siege-attrition.md`: forced capture (`FUN_0044bb18`) always changes fortification/population; defection (`FUN_0044bed8`) "never writes to population or fortification anywhere." Real data now confirms that claim precisely, city by city.

The in-game news log (read directly from a video frame of the "Information" panel, `frames4/f_045.png`) spells out the same event in the game's own words:

```text
Week 7   Winter   270BC
Laranda  (Galatia) falls to Seleucid.
Gordium  (Galatia) falls to Seleucid.
Synnada defects from Galatia to Seleucid.
Acroinon defects from Galatia to Seleucid.
-----------------------------------------------
Seleucid conquers Galatia.
-----------------------------------------------
```

Only 4 of the 9 transferred cities get an individual news line. The other 5 (Pessinus, Ancyra, Gangra, Nyssa, Halys) — all showing the same owner-only "defection" byte pattern — are folded silently into the final `"Seleucid conquers Galatia."` banner. This is a new detail beyond what `decompiled-defection-and-siege-attrition.md`'s code reading established: the cascading-defection loop (`FUN_0044ba1c`) evidently keeps generating individual news events city-by-city only until the losing nation's last city goes; whatever remaining cities are swept up in the same instant as the final elimination check are transferred without a per-city announcement, under the umbrella nation-elimination event instead. (The dashed-line banner formatting itself is also new information for a future pass at the news-log's exact layout.)

### Nation-record signature of elimination

Galatia's (index 12) nation record before/after:

| Field | winter_5 | winter_7 |
| --- | --- | --- |
| Capital city index | 218 (Ancyra) | **65535 (0xFFFF sentinel)** |
| Unity | 668 | **0** |
| Cities (recorded) | 9 | 5 |
| Treasury | -911 | -911 (unchanged) |
| Mobilized % | 100 | 100 (unchanged) |

Capital reading the `0xFFFF` sentinel and unity dropping to exactly 0 are clean, unambiguous elimination markers. The `cities` field is not: Galatia owns exactly 0 cities after this turn (confirmed by scanning the whole 334-city table for `owner == 12`), yet the record reads 5. This looks like a genuine quirk of the elimination code — plausibly the `cities` counter is only decremented once per processed defection/capture event in the cascade loop, and the loop exits (or takes a different branch) once elimination is detected, before every transferred city's decrement lands. Left open; not investigated further this pass since it doesn't affect any save built from a live (non-eliminated) nation.

**Real bug found and fixed along the way:** `SaveNationTable.Parse` rejected any `capitalCity >= CityCount`, which is correct for a live nation but wrongly threw on the `0xFFFF` sentinel — this save was unparseable before the fix. `SaveNationTable.NoCapitalSentinel` (0xFFFF) is now accepted explicitly, and `NationRecord.IsEliminated` exposes the condition. `SaveJsonExporter.CityRef` was adjusted to emit `null` instead of indexing out of range when a nation has no capital.

## Part 2: A previously undocumented mechanic — city-to-army resupply

The recording shows a dialog never previously identified: selecting an army sitting on/near a friendly city and choosing to resupply it opens a transfer dialog structurally identical to `TArmyToArmy`, but between a **city's supply stock** and **an army**, plus a **national treasury ↔ army money** pair, each with the same `10s`/`100s` stepper controls.

Three consecutive frames of Army 0 (the "Army 1" of the user's notes, 19 units, 99,882 troops, money 256) resupplying at Mediolanum:

| Frame | Mediolanum's supply | Army's supply | Army's money | National balance |
| --- | ---: | ---: | ---: | ---: |
| `f_028.png` (dialog just opened) | 226 | 204 | 256 | −818 |
| `f_030.png` (mid-transfer) | 126 | 304 | 256 | −818 |
| `f_035.png` (after `OK`, map view) | — | 344 (34%) | 256 talents | — |

`226 → 126` and `204 → 304` are an **exact reciprocal 100-ton transfer** — the same conservation property already confirmed for `TArmyToArmy`. The final committed value (344) implies further stepper clicks after the sampled frame (204→304→344 is consistent with one `100s` click then four `10s` clicks). Money and national balance were untouched in this example — the player only moved supply, not talents, even though the dialog offers both.

This also gives the first direct-from-UI numbers for supply *capacity*: the panel shows supply as a percentage (`"204 tons (20%)"` before, `"344 tons (34%)"` after), implying a capacity of roughly 1,010–1,020 tons for this 19-unit/99,882-troop army. A separate frame (`f_010.png`) shows Army 2 (6 units, 28,227 troops) at `"184 tons (65%)"`, implying a capacity around 283 tons for that army. Both ratios land close to **1 ton of capacity per ~98–100 troops** — a plausible candidate formula, not yet confirmed against a controlled troop-count change.

Despite this mid-session top-up to 344 tons, the eventual `winter_7.sav` shows Army 0's supply at **0**. This is not a contradiction: the save is two full weekly ticks after `winter_5`, and the recording only captures part of that window — further marching (Army 0 moved 5 tiles west, from (95,28) to (90,28), ending near but not exactly on Mediolanum at (91,27)) and ordinary weekly consumption plausibly account for the rest. Not fully attributed this pass.

## Part 3: Army movements match the notes

| | winter_5 pos | winter_7 pos | Nearby city | Notes |
| --- | --- | --- | --- | --- |
| Army 0 ("Army 1") | (95, 28) | (90, 28) | Mediolanum (91, 27) | "moves west and supplies at Mediolanum" — confirmed |
| Army 2 ("Army 2") | (94, 28) | (94, 36) | Pisae (93, 35) | "goes back south, and supplies at Pisa" — confirmed |

Both armies end within ~1.4 tiles of the named city, matching the notes' direction and destination exactly. Army 2's supply barely moved (184 → 185 tons) despite an 8-tile march and a claimed resupply at Pisae — unlike Army 0's clean, isolated example, this delta is muddied by the two-week gap and wasn't independently isolated via video (no dialog frame for Army 2 was found in the sampled set).

## What this does not establish

- The exact formula/trigger for the `cities`-field-doesn't-reach-0 anomaly on nation elimination.
- The full news-log/cascade boundary rule (why some transfers get individual lines and others don't) beyond what's observed here.
- The supply-capacity-per-troop ratio, beyond two rough data points suggesting ~98–100.
- What fully drained Army 0's supply from 344 back to 0 by the save point.
- Whether the resupply dialog is a distinct RTTI class or a shared/parameterized form with `TArmyToArmy` — not yet located in the symbol table or decompiled.

## Reproduction

```text
ffmpeg -i "recordings/bandicam 2026-09-13 00-04-38-377.mp4" -vf fps=1/2 frames4/f_%03d.png
dotnet run --project src/IC2.Inspect -- --compare-saves saves/1_rome_270_winter_5.sav saves/1_rome_270_winter_7.sav
dotnet run --project src/IC2.Inspect -- --to-json saves/1_rome_270_winter_5.sav w5.json
dotnet run --project src/IC2.Inspect -- --to-json saves/1_rome_270_winter_7.sav w7.json
```

## Next checks

1. Locate and decompile the resupply dialog's RTTI class/methods (likely near `TArmyToArmy` in the method table) to confirm the transfer-amount clamping rule and whether it's the same code path parameterized by source (city vs. army).
2. A controlled single-week save pair isolating one resupply action (no other army activity) would pin down the supply-capacity-per-troop ratio precisely.
3. A save pair spanning a nation's very last city being lost by a *single* capture (not a multi-city cascade) would help resolve the `cities`-field anomaly.
