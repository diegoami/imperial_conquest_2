# Mobilization, army movement, and two capture modes across four turns

The user continued the same Rome session across four more saves, again with screenshots and a written log:

```text
1_rome_270_autumn_1.sav: Increased taxes from 366 to 488; Sidon (Seleucid) falls to Ptolemaic
1_rome_270_autumn_3.sav: Mobilized troops. An army was refilled and another created. Both were supplied from Rome. Byblos (Seleucid) falls to Ptolemaic
1_rome_270_autumn_5.sav: Both armies moved towards north. First army supplied from Arretium. Carthage destroys army of Celtiberia. Media wants to trade with Rome
1_rome_270_autumn_7.sav: Both armies moved towards north. First army takes Felsina. Modena defects to Rome. Aradus (Seleucid) falls to Ptolemaic
```

Each pair spans one full turn (two weeks) and mixes the noted player action with ordinary AI activity, so — as in the prior [tax/capture report](rome-tax-increase-and-sidon-capture.md) — this is turn-level evidence, not perfectly isolated single-action evidence. A new `--list-armies <save> <nation>` command was added to `IC2.Inspect` to find a nation's armies by owner code instead of guessing coordinates.

| Save | Bytes | Army/fleet count | Trailer |
| --- | ---: | --- | --- |
| `autumn_1` | 132,071 | 14 / 3 | Week 1 Autumn |
| `autumn_3` | 132,727 | 15 / 3 | Week 3 Autumn |
| `autumn_5` | 133,383 | 16 / 3 | Week 5 Autumn |
| `autumn_7` | 134,039 | 17 / 3 | Week 7 Autumn |

Army count rises by exactly one every single turn, including turns where the notes describe no army creation by Rome — some other nation recruits an army almost every turn, so a rising army count alone does not identify whose army changed.

## Mobilization: exact troop conservation between garrison and field armies

Rome's city-unit garrison (`autumn_1 → autumn_3`), from `--inspect-city`:

```text
before: fortification 78% (85,000 city-unit troops) · supplies 1810 tons
        11 unit entries: 3× light infantry 15,000, heavy infantry 6,000, light cavalry 7,000,
        heavy cavalry 2,500, 5× archers 3,500 (state code 24), plus 1× light cavalry 7,000 (state code 8)
after:  fortification 78% (7,000 city-unit troops) · supplies 1457 tons
        1 unit entry: light cavalry 7,000 troops (state code 10)
```

The "before" list matches the user's "Units at Rome" mobilize/disband screenshot exactly (same units, same troop counts, same "average" quality label repeated in both). `--list-armies saves/1_rome_270_autumn_1.sav Rome` and the `autumn_3` equivalent give:

```text
before: Army 0 at (100, 42) · 48,173 troops · 410 tons supply · 296 money · moves 8 · morale 9
after:  Army 0 at (100, 42) · 83,173 troops · 791 tons supply · 296 money · moves 6 · morale 9
        Army 14 at (102, 44) · 43,000 troops · 410 tons supply · 0 money · moves 8 · morale 2
```

Garrison troops fell by 85,000 − 7,000 = **78,000**. Army 0 gained 83,173 − 48,173 = **35,000** troops ("refilled"), and new Army 14 was created with **43,000** troops. `35,000 + 43,000 = 78,000`, an exact match. This is a clean, controlled confirmation that mobilizing city-unit garrison troops into field armies conserves troop count exactly, moving them from the `SaveRecruitmentTable` city-unit slots into `SaveArmyTable` records.

A freshly created army (Army 14) has a recognizable fingerprint: round troop count, **410 tons supply**, **0 money**, **8 moves**, **morale 2**. Fortification percentage (78%) did not change even though the garrison troop count collapsed, confirming it is a separate, non-troop-derived stat from the parenthetical troop total, consistent with [prior findings](city-units-army-transfer-and-mercenaries.md).

Rome's nation-level `MobilizedPercent` stayed at **62%** across `autumn_1/3/5` and only the city count changed at `autumn_7`. The in-game "Mobilize" button on a city's garrison panel (used here) is therefore a different mechanic from the nation record's `MobilizedPercent` field — the latter did not move despite an explicit "Mobilized troops" action, so it should not be assumed to track this UI action.

## Army movement, resupply, and morale: partial patterns, not yet confirmed rules

Tracking both Roman armies across all four saves:

| Save | Army 0 (main) | Army 14/2 (new) |
| --- | --- | --- |
| `autumn_3` | (100,42) · 83,173 troops · 791 supply · moves 6 · morale 9 | (102,44) · 43,000 troops · 410 supply · moves 8 · morale 2 |
| `autumn_5` | (99,37) · 83,173 troops · 791 supply · moves 6 · morale **2** | (104,38) · 43,000 troops · 389 supply · moves 8 · morale 2 |
| `autumn_7` | (98,32) · 82,437 troops · 750 supply · moves 6 · morale 2 | (99,31) · 43,000 troops · 368 supply · moves 8 · morale 4 |

Observations, kept separate from confirmed fields:

- **Moves** stayed constant per army (6 for Army 0, 8 for Army 14/2) across turns even though both moved every turn. This is consistent with `Moves` being a per-army maximum (plausibly set by unit composition/speed) rather than a this-turn-remaining budget, but that is inferred from two data points and not confirmed.
- **Morale** dropped sharply for Army 0 (9 → 2) exactly in the turn the notes describe as pure movement with a resupply, no reported combat. Army 14/2's morale sat at 2 then rose to 4 after the Felsina capture turn. No cause is established for either change; flagging as open.
- Army 0's troop count fell 83,173 → 82,437 (−736) in the turn it took Felsina, plausibly siege/combat attrition, but no separate battle record was inspected to confirm the mechanism.

## Resupply does not show the same clean conservation as the single-turn case

The [controlled-army-supply-transfer report](controlled-army-supply-transfer.md) found an exact tons-for-tons transfer between a city and an army with **no turn advance** between saves. Here, across full turns, the picture is messier:

| | Rome supplies | Arretium supplies | Army 0 supplies |
| --- | ---: | ---: | ---: |
| `autumn_1` | 1810 | 330 | 410 |
| `autumn_3` (mobilize + refill + create, "both supplied from Rome") | 1457 (−353) | 330 (0) | 791 (+381) |
| `autumn_5` (Arretium resupply noted) | 1810 (+353) | 330 (0) | 791 (0) |
| `autumn_7` (no resupply noted) | 1810 (0) | 330 (0) | 750 (−41) |

Rome's supply drop at `autumn_3` (−353) does not equal the combined amount Army 0 and Army 14 gained (381 + 410 = 791), and it fully reverses by `autumn_5` with no noted action on Rome itself — consistent with ordinary production/consumption changing a city's stock across a full turn, separate from any transfer. Arretium's own supply field never changes at all despite being named as Army 0's resupply source at `autumn_5`, while Army 0's supply stayed flat that same turn (791 → 791) despite moving (which cost Army 0 41 tons the following turn with no resupply). The simplest reading is that **multi-turn pairs conflate transfer, movement consumption, and city production**, so the earlier report's clean per-ton conservation should not be assumed to hold once any turns elapse between saves; isolating the resupply mechanic again will need a same-day, single-action pair as before, ideally at a city other than the capital.

## Two capture modes produce different footprints

Notes: "First army takes Felsina" (a hostile capture) vs. "Modena defects to Rome" (a peaceful defection), both between `autumn_5` and `autumn_7`.

| | Felsina (captured by force) | Modena (defected) |
| --- | ---: | ---: |
| Owner | Gaul → Rome | Gaul → Rome |
| Allegiance | Gaul → Gaul (unchanged) | Gaul → Gaul (unchanged) |
| Population | 26,000 → 19,000 (**−27%**) | 20,000 → 20,000 (unchanged) |
| Fortification | 68% → 51% (**−17 pts**) | 51% → 51% (unchanged) |
| Loyalty | 79 → 41 | 63 → 50 |
| Tribute | 18 → 18 (unchanged) | 2 → 2 (unchanged) |
| Supplies | 260 → 190 | 200 → 200 (unchanged) |

This is a controlled confirmation that population and fortification losses are specific to forced capture, not a general side effect of an owner change — a bloodless defection leaves both fields untouched. Loyalty drops in both cases (more sharply under force), and allegiance stays with the original nation either way, matching the [Sidon finding](rome-tax-increase-and-sidon-capture.md). Rome's `NationRecord.CityCount` went 25 → 27, exactly accounting for both new cities.

## An unresolved anomaly: Felsina's info-panel screenshot

The user's Felsina screenshot (taken around the capture) reads: population 19,000 (73%), loyalty "very low", fortification 51%, tribute **13** talents, supply **260** tons. Population, fortification, and loyalty match the `autumn_7` (post-capture) save exactly. But tribute is 18 in *both* `autumn_5` and `autumn_7` (never 13), and supply reads 260 in `autumn_5` (pre-capture) and 190 in `autumn_7` (post-capture) — the screenshot's 260 matches the *before* state, not the same state as its own population/fortification/loyalty numbers. No single save state explains all six displayed values together. This is left as an open discrepancy rather than resolved; possible causes include a UI panel that does not refresh every field at the same time after a capture, or the screenshot being taken at a moment not captured by either save file.

## Reproduction

```text
dotnet run --project src/IC2.Inspect -- --list-armies saves/1_rome_270_autumn_1.sav Rome
dotnet run --project src/IC2.Inspect -- --inspect-city saves/1_rome_270_autumn_1.sav Rome
dotnet run --project src/IC2.Inspect -- --inspect-city saves/1_rome_270_autumn_5.sav Felsina
dotnet run --project src/IC2.Inspect -- --inspect-city saves/1_rome_270_autumn_7.sav Felsina
dotnet run --project src/IC2.Inspect -- --inspect-city saves/1_rome_270_autumn_5.sav Modena
dotnet run --project src/IC2.Inspect -- --inspect-city saves/1_rome_270_autumn_7.sav Modena
```

## Next checks

1. Repeat the original same-day, no-turn-advance supply transfer at a non-capital city to see whether that city's own stock actually decreases, since Arretium's did not here.
2. Save immediately before and after a single Mobilize click (no other actions, no turn advance) to isolate whether garrison-to-army conservation and the new-army template (410 supply / 0 money / 8 moves / morale 2) hold with no other confound.
3. Inspect the same garrison unit's `StateCode` across more turns (`24`, `8 → 10 → 12 → 14` seen here, incrementing by 2 per turn) to test whether it is a duration/turn counter rather than a fixed enum value.
4. Take a Felsina-style detailed info screenshot immediately upon capture and again after ending that turn, to localize which fields are stale in the immediate-capture UI.
