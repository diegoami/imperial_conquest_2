# A mercenary hire, a uniform attrition signature, and a fleet-record correction

One more turn in the same session, `1_rome_270_winter_1.sav → 1_rome_270_winter_3.sav`:

```text
1_rome_270_winter_3.sav: One army recruits 6438 Gallic Lit Inf very good in Felsina, for costs of 51 quarterly.
    The other army takes Mediolanum. Mediolanum falls to Rome. Taurasia defects from Gaul to Rome. Both armies
    resupply from Mediolanum and Felsina. Rome trades with Dacia. Seleucid destroys army of Galatia.
    Phoenice (Greece) falls to Illyria. Taurasia (Rome) falls to Gaul.
```

Note this packs two Taurasia events into one turn — it defects to Rome, then falls back to Gaul — so its *net* state in the save reflects only the final outcome.

## This was a mercenary hire, not generic field recruitment

Army 0's unit list gained a 13th slot between `winter_1` and `winter_3` that wasn't there before, with every pre-existing unit's troop count unchanged:

```text
new slot 12: "Gallic" · light infantry · 6,438 troops · quality 8 ("very good")
```

`6,438` and `"very good"` match the note exactly, and `light infantry` matches "Lit Inf." **The user confirmed this was a mercenary hire**, not a standing recruitment mechanic — correcting this report's original framing. This connects directly to the older, never-followed-up [mercenary finding](city-units-army-transfer-and-mercenaries.md): a candidate 12-byte record (`x, y, label, type, troops, quality`) located once, just after the nation table, reproducing Alexandria's "9,056 good Egyptian light infantry" listing, but never confirmed with a before/after pair or built into a parser. A hired mercenary group becomes a regular `ArmyUnit` slot in the hiring army's 656-byte record — named after the local nation ("Gallic") instead of a Roman battalion name — exactly like the newly observed slot 12 here. Unlike mobilizing a city's own garrison ([conserves troops exactly](mobilization-movement-and-city-capture-modes.md)) or creating a fresh army (fixed 410-supply/0-money template), hiring adds a new unit slot at the hired quantity and quality directly. Rome's treasury moved `-904 → -818` (+86) that turn, but the note's "51 quarterly" reads as a recurring upkeep charge rather than a one-time hiring cost, and a single turn isn't enough to isolate a quarterly charge from ordinary income — left open. See the [follow-up mercenary-pool investigation](mercenary-pool-record.md) prompted by this correction.

## A uniform ~2.7% troop loss across every unit in the other army

Army 2 (which took Mediolanum this turn) lost troops in **every one of its twelve units**, by a strikingly consistent percentage:

| Unit | Before | After | Change |
| --- | ---: | ---: | ---: |
| 3rd Lancers | 6,878 | 6,695 | −2.66% |
| 9th Guards | 5,896 | 5,743 | −2.59% |
| 5th Foot | 14,748 | 14,328 | −2.85% |
| 2nd Foot | 5,353 | 5,203 | −2.80% |
| 2nd Lancers | 876 | 852 | −2.74% |
| 1st Guards | 4,774 | 4,651 | −2.58% |
| 7th Guards | 2,517 | 2,448 | −2.74% |
| 5th Bowmen | 3,404 | 3,317 | −2.56% |
| 4th Bowmen | 3,408 | 3,312 | −2.82% |
| 6th Guards | 4,656 | 4,527 | −2.77% |
| 2nd Dragoons | 1,503 | 1,464 | −2.59% |
| 5th Guards | 3,479 | 3,392 | −2.50% |

Every unit lost between 2.50% and 2.85% of its troops — a band under half a point wide, across unit types (infantry, cavalry, archers) and quality levels (average through very good). This is the first quantitative evidence toward a battle/attrition formula (previously only static entry points were located, see [battle-code-entry-points.md](battle-code-entry-points.md)): it strongly suggests a single percentage applied uniformly to a whole army that turn, rather than losses concentrated in whichever units were "in the front line." This could be siege/combat losses from taking Mediolanum, ordinary attrition from low supply, or both combined into one multiplier — this pair can't separate those causes, only establish that the loss is proportional and army-wide rather than per-unit.

## Mediolanum: a fifth capture confirming the same signature

```text
before: Gaul, allegiance Gaul, population 37,000, fortification 66%, loyalty 77, tribute 16, supply 370
after:  Rome, allegiance Gaul, population 27,000, fortification 49%, loyalty 43, tribute 16, supply 265
```

Same rule as [four prior captures](diplomatic-reparations-and-more-captures.md): owner flips, allegiance and tribute untouched, population/fortification/loyalty drop.

## Taurasia: recaptured the same turn separates "siege damage" from "loyalty to current owner"

Taurasia's owner is `Gaul` in both `winter_1` and `winter_3` — the defection-then-reconquest cancels out at the byte level. But its other fields moved as if it *had* been fought over:

```text
before: Gaul, population 21,000, fortification 22%, loyalty 54, tribute 7, supply 210
after:  Gaul, population 15,000, fortification 16%, loyalty 90,  tribute 7, supply 180
```

Population (−29%) and fortification dropped just like a real capture, consistent with actual fighting having taken place there. But **loyalty rose sharply, to 90** — the opposite of every forced capture so far. The simplest reading: population/fortification damage comes from the siege/combat event itself regardless of who ends up owning the city, while loyalty reflects sentiment toward whichever nation ends the turn in control — here, the population is glad to be back under its original ruler (Gaul) rather than freshly conquered by a new one. This is a genuinely new distinction the earlier one-directional capture examples couldn't reveal, since none of them had reverted ownership within the same turn.

## Fleet `CityIndex` is not a fixed home port — it changes as fleets act

The [previous fleet report](fleet-order-at-caere.md) treated `CityIndex` as roughly "the city a fleet was built at." This turn contradicts that:

```text
before: Fleet 0 at (46, 68)  · 90 ships · city index 97  (Cales)
        Fleet 1 at (189, 93) · 70 ships · city index 100 (Capua)
after:  Fleet 0 at (65, 69)  · 67 ships · city index 73  (Brixia)
        Fleet 1 at (189, 93) · 70 ships · city index 99  (Ovilava)
```

Fleet 0 moved a long distance *and* dropped from 90 to 67 ships, with its `CityIndex` switching to Brixia (a city Rome only just captured, on the same side of the map it moved toward). Fleet 1 **did not move at all** (identical coordinates) yet its `CityIndex` still changed, from Capua to Ovilava. Since a stationary fleet's "nearest city" wouldn't change, `CityIndex` is better read as something like "the city this fleet most recently interacted with (e.g. resupplied at)" rather than a fixed construction origin or strict nearest-port value — both are consistent with the under-construction-fleet case from the previous report (where it never changes because the fleet never moves or interacts with anything until built), but neither is confirmed. `IC2.Data`'s `FleetRecord.CityIndex` doc comment should be read as "candidate, evidently not fixed" going forward. Two more fleets also appeared this turn (indices 4 and 5, at Pynda and Siga, both `(0,0)` under construction) with no corresponding note — presumably other nations' orders, another reminder that a rising fleet/army count isn't attributable to Rome by itself.

## What this does not establish

- Whether the uniform ~2.7% loss is combat, supply attrition, or both, and whether the exact percentage is a fixed constant or situational.
- The "51 quarterly" upkeep cost's actual accounting (needs tracking across a full quarter/season boundary).
- `CityIndex`'s precise rule (last resupply point vs. something else) — this report only narrows out "fixed home port."
- Felsina's supply fell sharply (190 → 64) while being the recruitment/resupply source, but *both* Roman armies' own supply also fell rather than rose this turn — another instance of multi-turn confounding, not a clean transfer to report.

## Reproduction

```text
dotnet run --project src/IC2.Inspect -- --to-json saves/1_rome_270_winter_1.sav out/winter_1.json
dotnet run --project src/IC2.Inspect -- --to-json saves/1_rome_270_winter_3.sav out/winter_3.json
```
Compare `armies`, `cities` (Mediolanum, Taurasia, Felsina), and `fleets` between the two files.

## Next checks

1. A same-day before/after around a single battle (no other turn activity) would test whether the ~2.7% uniform-loss pattern holds in isolation and whether it varies with the target city's fortification or the attacker's troop ratio.
2. Track a fleet's `CityIndex` turn-by-turn against where it actually resupplies to test the "last-interacted-with city" hypothesis directly.
3. Save across a full quarter boundary to see whether the "51 quarterly" recruitment upkeep produces a matching periodic treasury deduction.
