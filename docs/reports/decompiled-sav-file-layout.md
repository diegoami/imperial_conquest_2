# The complete SAV file layout, from the actual read/write code

The single strongest confirmation this project has produced. Tracing forward from `TPremierForm_OpenGameFile`/`SaveGameFile(As)` (found directly in the recovered RTTI symbol list — no string search needed this time) reached `FUN_004487c4` (load) and `FUN_004484d0` (save), a matched pair of functions that read/write the exact same fields in the exact same order via a stream object's `Read`/`Write` virtual methods (vtable slot 0 and slot +4 respectively). This is, byte-count for byte-count, the authoritative SAV file format — not inferred from diffing, but read directly from the code that produces the files.

## The full sequence, in order

| Field | Byte count | Matches |
| --- | ---: | --- |
| Map | 89,600 | `WorldPrefix.MapByteLength` (320×140×2) — exact |
| City table | 11,356 | `334 × 34` (`WorldPrefix.CityCount × CityRecordLength`) — exact |
| Army count | 2 | — |
| Army records × count | 656 each | `SaveArmyTable.RecordLength` — exact |
| Fleet count | 2 | — |
| Fleet records × count | 26 each | `SaveFleetTable.RecordLength` — exact |
| Nation table | 18,752 | `16 × 1,172` (`SaveNationLayout.NationRecordLength`) — exact |
| **Mercenary table** | **50 × 12 bytes, fixed** | **`SaveMercenaryTable`'s 50-slot, 12-byte-record structure — confirmed directly in code**, not just inferred from where plausible data stopped in `mercenary-pool-record.md`. This is no longer a "candidate" finding. |
| A count field, then `(count+1)` records | 61 bytes each | **New, previously unidentified.** See below. |
| Fixed block | 32 | unidentified |
| Fixed block | 4 | unidentified |
| Current nation | 2 | matches `DAT_004a0320`, used throughout this project's decompiled code as "current nation index" |
| Fixed fields | 2+2+2+2 | unidentified |
| Calendar/turn block | 8 | plausibly related to the known week/season/year/active-nation trailer fields — the save code reads these directly from UI state (`FUN_00412880`, map scroll position getters) rather than from a stored struct, suggesting this may be display state, not the turn trailer itself |
| **Battle-in-progress flag** | 1 | **New finding: if set, additional battle-state data follows** |
| *(conditional, only if battle flag set)* | 2+2+2+1+2+1,760+336 | Tactical battle state — unit placement array (`0x6e0` = 1,760 bytes) and grid state (`0x150` = 336 bytes). **This means a save made mid-battle is a different length than every sample this project has examined so far** — none of the sampled saves were taken mid-battle. |

## A brand-new record type: 61 bytes, count-prefixed

Immediately after the (now code-confirmed) mercenary table, the save reads a 2-byte count, then `count + 1` records of exactly 61 bytes each. This wasn't visible in prior save-diffing work, which only established that *something* occupied the ~2,442 bytes between the mercenary table and the trailer, without knowing its shape. 61 bytes per record is large enough to plausibly hold a name plus several numeric stats — a leader record is one candidate, given the `TPickLeaders` form already recovered in the RTTI symbol scan, though this isn't confirmed.

**Reconciliation gap, stated honestly:** the previously-measured constant 3,042-byte gap (mercenary-table-end to trailer-start, from `mercenary-pool-record.md`) should equal `600 (mercenary table) + 2 (count field) + 55 (the fixed tail: 32+4+10+8+1) + (count+1)×61`. Solving gives `(count+1) = 2385/61 ≈ 39.1` — **not a whole number**, off by about 6 bytes from the nearest fit (39 records = 2,379 bytes). This is left unresolved rather than forced to fit; a small field was likely miscounted on one side of this reconciliation (either in this report's byte tally or the original 3,042 measurement), and it should be re-checked before treating either number as final.

## Why this matters

This single pair of functions independently reconfirms, from the actual serialization code rather than inference, nearly every structural fact this project has built up from save-diffing over many reports: the map size, city table size and stride, the army/fleet/nation table record lengths, and — most importantly — the mercenary table's 50-slot capacity that `mercenary-pool-record.md` had to discover empirically by testing where plausible data stopped. It also explains something no prior report could: **why a save's total length might vary in ways not accounted for by army/fleet/city-unit counts alone** — a save taken during an active battle carries an entire extra block of tactical state that every sample examined so far (none mid-battle) simply never had.

## What this does not establish

- The identity of the 61-byte record type, or what the count field (`DAT_004a031e`) actually counts.
- The identity of the 32-byte and 4-byte fixed blocks, or the four remaining 2-byte fields before the calendar block.
- Whether the 8-byte "calendar" block here is the same data as the known 55-byte trailer's week/season/year/nation fields, or separate UI display state.
- The 6-byte reconciliation gap noted above.

## Reproduction

Found via the recovered RTTI symbol list directly (`grep -i "open\|save\|load\|game" delphi_symbols.tsv`), then:
```text
analyzeHeadless <project> IC2 -process "Imperial Conquest 2.exe" -noanalysis -scriptPath <scripts>
  -postScript ExportAddresses.java out.txt 0045aad4 0045ab84 0045abb0 004487c4 004484d0
```

## Next checks

1. Resolve the 6-byte reconciliation gap by recounting both the code-derived tail and the original 3,042-byte measurement carefully.
2. Identify the 61-byte record type — cross-reference against `TPickLeaders`'s fields, or find a save with a known number of leaders/other countable entities to solve for what `DAT_004a031e` counts.
3. If a save is ever taken mid-battle, use it to confirm the conditional battle-state block's exact layout (unit placement array, grid state) against the tactical battle mechanics already decompiled in `decompiled-combat-formula-structure.md`.
