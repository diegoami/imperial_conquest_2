# The fleet-record owner field, confirmed — no new experiment needed

The user asked what in-game action would isolate fleet ownership, the last real blocker on the SAV fleet record (`docs/HANDOVER.md`'s open-items list: "the rest of the fleet record (owner...)"). It turned out the answer was "none — we already have the evidence," by combining two facts already established in separate reports, both from `1_rome_270_winter_7.sav`.

## The two independently-known fleets

- **Rome**: the 10-ship fleet at Caere (city 82) is the exact fleet from the controlled order in `fleet-order-at-caere.md` — a fleet built by the human player (Rome) with no ambiguity.
- **Carthage**: the 49-ship fleet at Andematunum (city 49, at map position (50, 53)) is confirmed Carthage's because the very next save's news log literally reports **"A fleet belonging to Carthage is lost at sea"** for this exact record (it's the one fleet that disappears between `winter_7` and `winter_9`, and the size table shrinks by exactly one fleet record — see `galatia-elimination-and-city-resupply-confirmed.md`).

## The finding

Word 4 of the 26-byte fleet record (**byte offset +8**) reads exactly the expected nation code for both:

| Fleet | Word @ +8 | Expected (nation code) | Match |
| --- | --- | --- | --- |
| Caere (Rome) | 0 | Rome = 0 | yes |
| Andematunum (Carthage) | 1 | Carthage = 1 | yes |

Checking all 6 fleets in the same save against the current owner of the city at their `CityIndex`:

| Fleet | `OwnerCode` @ +8 | City @ `CityIndex` | City's current owner | Match |
| --- | --- | --- | --- | --- |
| Andematunum | 1 (Carthage) | Andematunum | 6 (Gaul) | no — but this fleet is deployed at sea (50,53), far from (0,0) |
| Cales fleet | 3 (Ptolemaic) | Cales | 0 (Rome) | no — also deployed at sea (189,93) |
| Athens fleet | 7 (Greece) | Athens | 7 (Greece) | **yes** — docked in port (0,0) |
| Caere fleet | 0 (Rome) | Caere | 0 (Rome) | **yes** — docked in port (0,0) |
| Pynda fleet | 4 (Macedonia) | Pynda | 4 (Macedonia) | **yes** — docked in port (0,0) |
| Siga fleet | 5 (Numidia) | Siga | 5 (Numidia) | **yes** — docked in port (0,0) |

The two "mismatches" are not contradictions: both are the only two fleets actually out at sea (real map coordinates instead of the (0,0) in-port sentinel), and `field-recruitment-uniform-attrition-and-fleet-drift.md` already established that `CityIndex` drifts away from a fixed home port as a fleet acts — a stale `CityIndex` pointing to a city that has since changed hands (or was just a past waypoint) is exactly what that finding predicts. Every fleet still sitting in its home port matches its port's owner exactly, and every fleet with independent identity evidence (Rome, Carthage) matches too. Six for six, no exceptions once the already-known caveat is accounted for.

## What changed in code

- `SaveFleetTable.cs`: added `FleetRecord.OwnerCode` (word @ +8).
- `IC2.Inspect --list-fleets` now prints the owning nation's name.
- `SaveJsonExporter`'s `fleets` array now includes an `owner: { code, name }` object.

## What this does not establish

- Whether `OwnerCode` is read/written the same way for a fleet mid-construction (all our in-port examples are already-complete fleets; the original Caere order was captured with no turn advance, so it's a completed-order record, not a partially-built one — though `ShipCount`/`CityIndex` were already confirmed the same way and nothing suggests owner would behave differently).
- The remaining unlabelled bytes in the record (several still-unidentified words remain, including one — word 3 — that was nonzero only for the Carthage fleet in this sample, not yet explained).

## Reproduction

```text
dotnet run --project src/IC2.Inspect -- --list-fleets saves/1_rome_270_winter_7.sav
dotnet run --project src/IC2.Inspect -- --to-json saves/1_rome_270_winter_7.sav w7.json
# compare the "owner" field against each fleet's cityIndex's owner in the cities array
```
