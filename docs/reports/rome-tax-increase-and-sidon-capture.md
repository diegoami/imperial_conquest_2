# Controlled tax increase and Sidon's capture across three turns

The user played a short human-controlled Rome session from the full v1.01 game, saving before and after several turns, with screenshots and a written log (`notes/1_rome.txt`) of in-game events:

```text
1_rome_270_summer_7.sav : No action
1_rome_270_summer_9.sav (missing): No action: Seleucid destroys army of Bythinia (see screenshot)
1_rome_270_summer_11.sav (missing): No action: Carthage destroys army of Celtiberia
1_rome_270_autumn_1.sav : Increased taxes from 366 to 488; Sidon (Seleucid) falls to Ptolemaic
```

Only the first and last saves in this sequence exist as files. The pair spans three turns (weeks 7, 9, 11 of Summer, then week 1 of Autumn) and mixes a player action (raising Rome's tax rate) with two AI battle outcomes and one AI city capture. This report treats it as multi-turn evidence rather than a single isolated action, and separates what each field's behavior actually supports.

| Local save | Bytes | SHA-256 | Trailer |
| --- | ---: | --- | --- |
| `saves/1_rome_270_summer_7.sav` | 129,421 | `39f2e8f9a356f2675806f89e3ba8a65bb5492722b3f260b09f18159b536ea640` | Week 7 Summer 270 BC · current nation Rome |
| `saves/1_rome_270_autumn_1.sav` | 132,071 | `4d4ac207a2d15b9e84afedd360f5b779970ecaac6b061be52363374ea3a18ded` | Week 1 Autumn 270 BC · current nation Rome |

The season trailer confirms a calendar boundary: Summer's last observed week is 11, and Autumn restarts the week counter at 1.

## Tax rate: confirmed by a controlled before/after value

The user's "Change tax level" screenshot for this action shows:

| | Current | New |
| --- | ---: | ---: |
| Tax | 15 | 20 |
| Income | 366 | 488 |

`IC2.Inspect --inspect-nation` on Rome reports:

```text
before: 25 cities · candidate population 2,535,000 · tax 15% · mobilized 65% · treasury -644 talents · unity value 822
after:  25 cities · candidate population 2,571,000 · tax 20% · mobilized 62% · treasury -759 talents · unity value 825
```

`NationRecord.TaxRatePercent` (nation record `+0x44A`) moved exactly `15 → 20`, matching the dialog. This upgrades that field from "matches static panel reading" ([rome-city-recruitment-and-nations.md](rome-city-recruitment-and-nations.md)) to a controlled-action confirmation. The dialog's "income" (366 → 488 talents) is not visibly stored as its own nation field near the known offsets; treasury instead fell further into deficit (-644 → -759) over the three turns, so weekly "income" as displayed is not the same thing as the stored treasury delta, and likely nets against recruitment/upkeep spending across those turns. This remains open.

## Sidon's capture: owner and allegiance are decoupled, with directional effects on loyalty and population

`CityRecord` for Sidon (index 263) before and after:

```text
before: Sidon at (221, 71) · controlled by Seleucid · allegiance to Seleucid
        Population 42,000 · fortification 27% · tribute 13 talents · supplies 420 tons · loyalty value 90
after:  Sidon at (221, 71) · controlled by Ptolemaic · allegiance to Seleucid
        Population 34,000 · fortification 20% · tribute 13 talents · supplies 420 tons · loyalty value 40
```

Changed record bytes were exactly `+18, +22, +26, +28` — `OwnerCode`, `LoyaltyValue`, `FortificationPercent`, `PopulationThousands`. Notably, `+20` (`AllegianceCode`) did **not** change: the record still reads "allegiance to Seleucid" after Ptolemaic took control. This is a controlled confirmation (not just a hypothesis) that owner and allegiance are separate fields, and that a freshly captured city keeps its prior allegiance rather than immediately adopting its new ruler's. Loyalty dropping 90 → 40 and population falling 42,000 → 34,000 alongside a capture are both consistent with, but not sole proof of, loyalty and population being affected by conquest; no other cause is visible in this file pair. Tribute and supplies were unchanged.

## Army/fleet counts generalize past the previously-tested value

`IC2.Data`'s nation-table locator previously threw for any save without exactly two fleets — the only case tested so far. This save pair has two fleets before and three after, which initially raised:

```text
Unhandled exception. System.IO.InvalidDataException: Nation-table layout is validated only for saves with two fleets; got 3.
```

Raw counts read directly from both files:

| | Army count | Fleet count | File length |
| --- | ---: | ---: | ---: |
| `summer_7` | 10 | 2 | 129,421 |
| `autumn_1` | 14 | 3 | 132,071 |

`(14 − 10) × 656 + (3 − 2) × 26 = 2,650`, exactly the observed file-size difference, and every nation name in the resulting table still matched `NationCatalog` for the `autumn_1` file. This confirms the army/fleet/nation offset formula generalizes beyond the fleet-count-2 case. The guard in `SaveNationLayout.Locate` was relaxed from `fleetCount != 2` to a sanity bound (`fleetCount > CityCount`), relying on the existing per-nation name check to catch a genuinely wrong offset. `AllegianceCode` and `LoyaltyValue` doc comments were updated to cite this pair.

## What this pair does not establish

- Individual attribution of the 162 changed city records (loyalty and population drift on nearly every city, consistent with ordinary per-turn dynamics already suspected for `+22`/`+28`, not analyzed further here).
- Which of the four new armies or the new fleet correspond to the Bythinia/Celtiberia battles or Sidon's capture — the news log names armies and nations, not save record indices, and this was not cross-referenced.
- The exact relationship between the tax dialog's displayed "income" and any stored nation field.

## Reproduction

```text
dotnet run --project src/IC2.Inspect -- --compare-saves saves/1_rome_270_summer_7.sav saves/1_rome_270_autumn_1.sav
dotnet run --project src/IC2.Inspect -- --inspect-nation saves/1_rome_270_summer_7.sav Rome
dotnet run --project src/IC2.Inspect -- --inspect-nation saves/1_rome_270_autumn_1.sav Rome
dotnet run --project src/IC2.Inspect -- --inspect-city saves/1_rome_270_summer_7.sav Sidon
dotnet run --project src/IC2.Inspect -- --inspect-city saves/1_rome_270_autumn_1.sav Sidon
```

## Next checks

1. Save immediately before and after a single tax change with no other turn advance, to find where (or whether) the dialog's "income" value is stored.
2. Capture a city with a screenshot at the moment of capture, then again a few turns later, to see whether allegiance later converges to the new owner.
3. Correlate the four new army records and the new fleet record with the specific battle/recruitment events named in the news log.
