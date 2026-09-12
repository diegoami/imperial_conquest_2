# A controlled fleet order decodes the fleet table and confirms map markers 333/335

The user placed an order for 10 ships at Caere and saved immediately, with no turn advance: `1_rome_270_autumn_7_fleet.sav` against the existing `1_rome_270_autumn_7.sav`. This is the cleanest controlled pair in the project so far. The order's UI also showed "5,000 soldiers"; the user clarified this is the ships' transport capacity (500 troops/ship × 10), not a stored troop count, which explains why no such value appears anywhere in the diff below.

| Save | Bytes | SHA-256 |
| --- | ---: | --- |
| `1_rome_270_autumn_7.sav` | 134,039 | `d5b3813ce669cba5ebaee678faf2c90de1a42c892b319828a88d9b70110dbfed` |
| `1_rome_270_autumn_7_fleet.sav` | 134,065 | `b5e3eebbed9c04ec2cbc9c8b28a3c58c3762729b95bfb0c54b694d1aaeb77e9b` |

Both are Week 7 Autumn 270 BC. `IC2.Inspect --compare-saves` reports **zero** changed map cells, **zero** changed city records (Caere's own population/fortification/tribute/supplies/loyalty are all untouched), and a file-size difference of exactly **+26 bytes** — one `FleetRecordLength`. The only other change anywhere in the save is Rome's nation-record treasury: **−695 → −795 talents**, a flat **100-talent** cost for the order. No population, garrison, or city-stock cost was deducted anywhere, unlike the land-unit mobilization in the [previous report](mobilization-movement-and-city-capture-modes.md).

## The new 26-byte fleet record, decoded

Hex dump of the appended record (index 3), as 13 little-endian words:

```text
00 00 00 00 00 00 00 00 00 00 18 00 00 00 00 00 00 00 0A 00 52 00 FF FF 00 00
```

| Word offset | Value | Field |
| --- | ---: | --- |
| `+0`/`+2` | 0, 0 | X, Y — see below |
| `+18` | **10** | Ship count |
| `+20` | **82** | City-table index (Caere) |
| `+22` | 0xFFFF | Unchanged sentinel across every fleet record seen |
| others | 0 (one word is 24 at `+10`) | Unlabelled |

`ShipCount` (10) and `CityIndex` (82 → Caere) match the user's action exactly. `IC2.Inspect --list-fleets` before and after:

```text
before: Fleet 0 at (46, 69)   · ship count 90 · city index 97  (Cales)
        Fleet 1 at (189, 93)  · ship count 70 · city index 100 (Capua)
        Fleet 2 at (0, 0)     · ship count 71 · city index 166 (Athens)
after:  Fleet 0 at (46, 69)   · ship count 90 · city index 97  (Cales)
        Fleet 1 at (189, 93)  · ship count 70 · city index 100 (Capua)
        Fleet 2 at (0, 0)     · ship count 71 · city index 166 (Athens)
        Fleet 3 at (0, 0)     · ship count 10 · city index 82  (Caere)
```

The three pre-existing fleets were byte-identical before and after — only the new record was appended. Their `CityIndex` values resolve to plausible home-port cities (Cales, Capua, Athens) rather than garbage, which cross-validates the field meaning beyond just the one controlled case.

## X/Y at (0,0) marks a fleet still under construction

Fleets 0 and 1 have real, distinct map coordinates; fleets 2 and 3 — the AI's Athens order and the user's fresh Caere order — both read `(0, 0)`. The simplest reading is that a fleet under construction has no map position yet and is placed on the map only once built, while an already-built fleet's `X`/`Y` are its real position.

## Map markers 333 and 335 are now confirmed as fleet positions, not just candidates

The [rivers-and-map-markers report](rivers-and-map-markers.md) flagged codes `333` and `335` as "candidate armies and fleets" from sparse-cell analysis alone. Reading the map cells at the two deployed fleets' exact coordinates:

```text
(46, 69)  — Fleet 0, based at Cales — map cell value 333
(189, 93) — Fleet 1, based at Capua — map cell value 335
```

Both match exactly. This directly confirms `333` and `335` are fleet map markers (not armies), closing that part of the roadmap's open item. It does not yet distinguish what the two different values mean (e.g. two different owners, or fleet-with-cargo vs. empty) — only two fleets were available to check, and both differ in value and in owner-ambiguous fields, so that distinction is still open.

## `IC2.Data`/`IC2.Inspect` changes

- Added `src/IC2.Data/SaveFleetTable.cs`: a `SaveFleetTable`/`FleetRecord` model exposing only the two fields with controlled-action evidence, `ShipCount` (+18) and `CityIndex` (+20), plus `X`/`Y` (+0/+2) as candidates per above. All other bytes stay unlabelled (`RawByteAt`).
- Added `IC2.Inspect --list-fleets <save>` to list every fleet with its coordinates, ship count, and resolved city name.

## What this does not establish

- Fleet ownership: no field was confidently identified as owner code from this single order (the record has several all-zero words besides the two confirmed fields, and the pre-existing fleets differ from each other in ways not yet explained).
- Whether the 100-talent cost is flat or per-ship (`10 talents/ship` is consistent with this single data point but unverified).
- The exact meaning of the `333` vs `335` distinction.

## Reproduction

```text
dotnet run --project src/IC2.Inspect -- --compare-saves saves/1_rome_270_autumn_7.sav saves/1_rome_270_autumn_7_fleet.sav
dotnet run --project src/IC2.Inspect -- --list-fleets saves/1_rome_270_autumn_7.sav
dotnet run --project src/IC2.Inspect -- --list-fleets saves/1_rome_270_autumn_7_fleet.sav
dotnet run --project src/IC2.Inspect -- --inspect-nation saves/1_rome_270_autumn_7_fleet.sav Rome
```

## Next checks

1. Save again once the 10-ship order at Caere finishes construction, to see the record's X/Y populate.
2. Place a second, differently-sized order (e.g. 5 ships) at a different city to test whether the 100-talent cost is flat or scales with ship count (500 troops/ship capacity implies 5 ships ⇒ 2,500-troop capacity, worth confirming in the UI too), and to get a second independent `CityIndex` confirmation.
3. Correlate a fleet's owner: compare an AI nation's fleet order (fleet 2, at Athens) against that nation's identity to find which unexplained word is the owner code.
