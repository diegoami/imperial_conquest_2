# The mercenary pool: a fixed 50-slot table, confirmed by a real hire

The user corrected an earlier report: a new army unit that appeared to be "field recruitment" ([mobilization/attrition report](field-recruitment-uniform-attrition-and-fleet-drift.md)) was actually a **mercenary hire** — 6,438 "Gallic" light infantry, quality "very good," hired at Felsina for 51 talents/quarter, between `1_rome_270_winter_1.sav` and `1_rome_270_winter_3.sav`. This connects directly to an older, unconfirmed finding: a candidate 12-byte mercenary record located once in `city-units-army-transfer-and-mercenaries.md`, reproducing Alexandria's "9,056 good Egyptian light infantry" offer, but never verified with a before/after pair or built into a parser. This session had exactly that pair.

## Locating the table, and a correction made along the way

The region between the end of the 16-record nation table and the 55-byte turn trailer is **exactly 3,042 bytes in every save checked**, regardless of turn, army count, or fleet count — a fixed-size region, not something that grows with game state. Reading it as consecutive 12-byte records, the first pass assumed the whole 3,042 bytes was one table (253 records, with 6 leftover bytes) and got a very strong hit at record 33. Systematically checking every record's plausibility (coordinates within the 320×140 map, unit type 0–4, quality 0 or 5–9) across all 7 saves in this session found the same result every time: **records 0–49 are always plausible, record 50 onward is always garbage** (out-of-range coordinates, type/quality codes in the thousands). The mercenary table is **50 fixed slots (600 bytes)**, not the whole 3,042-byte region. The remaining ~2,442 bytes hold a separate, still-unidentified structure. `SaveMercenaryTable` in `IC2.Data` was corrected to this before being committed.

## The confirming pair

Slot 33 in `winter_1` (before the hire):

```text
x=98, y=31, label=11, type=0 (light infantry), troops=6438, quality=8 (very good)
```

`(98, 31)` is Felsina's exact coordinates. `6,438` and `"very good"` match the note exactly, and `light infantry` matches "Lit Inf." The identical slot in `winter_3` (immediately after):

```text
x=98, y=31, label=11, type=0, troops=65535, quality=8
```

Only `troops` changed, to `0xFFFF` (65,535) — the same sentinel value already used in several other empty slots across the table (e.g. slot 45 in the same save). No other slot in the table lost its `(98, 31)` coordinates or changed to this sentinel between the two saves. This is a complete, exact, three-field match (coordinates, troop count, quality) plus a clean consume-on-hire mechanic, about as strong as controlled-pair evidence gets in this project so far.

The hired unit itself appears as a new slot in the hiring army's own record ([`SaveArmyTable`](../../src/IC2.Data/SaveArmyTable.cs)), named after the local nation ("Gallic") rather than a Roman battalion name — the mercenary table is the *offer*, and hiring moves it into the army as a normal unit.

## What `Label` is not

Slot 33's `label` is `11`. Gaul (the hiring/source nation for this mercenary group) is nation code `6` in `NationCatalog`, not `11` — so label is not simply the source nation's own code. This matches the older Alexandria case, where label `35` likewise didn't match Ptolemaic's code (`3`). The listing above shows many repeated label values across different coordinates (e.g. `label 2` appears at five different cities with different unit types), consistent with label being a larger, separate catalog — plausibly an ethnicity/culture selector distinct from the 16 playable nations — but its meaning remains unidentified.

## `IC2.Data`/`IC2.Inspect` changes

- Added `src/IC2.Data/SaveMercenaryTable.cs`: `SaveMercenaryTable`/`MercenaryRecord`, fixed at 50 records, exposing all six fields (`X`, `Y`, `Label`, `TypeCode`, `Troops`, `QualityCode`) plus `IsEmpty` for the 0/0xFFFF sentinel.
- Added `IC2.Inspect --list-mercenaries <save>` to list all non-empty offers.
- Added a `mercenaryOffers` section to `--to-json`.

## What this does not establish

- The meaning of `Label`.
- The remaining ~2,442 bytes after the mercenary table (the roadmap now lists this as a distinct open region rather than assuming it's more mercenary data).
- Whether hiring only part of an offer (rather than the whole 6,438, as happened here) decrements `Troops` instead of setting the sentinel — this pair only shows a full-offer hire.
- Whether/how often the 49-of-50 non-empty offers reshuffle turn-to-turn on their own (only one slot's change was attributed here, to the confirmed hire; ordinary market turnover wasn't ruled out for other slots).

## Reproduction

```text
dotnet run --project src/IC2.Inspect -- --list-mercenaries saves/1_rome_270_winter_1.sav
dotnet run --project src/IC2.Inspect -- --list-mercenaries saves/1_rome_270_winter_3.sav
```

## Next checks

1. Hire only part of an available offer (if the game allows it) to see whether `Troops` decrements or the slot still sentinels out entirely.
2. Compare `Label` values against city/nation identity more broadly (e.g. does every offer near Gaul's territory share a small set of label values?) to narrow down what catalog it indexes.
3. Investigate the ~2,442 leftover bytes after the mercenary table with the same systematic plausibility-scan approach used here.
