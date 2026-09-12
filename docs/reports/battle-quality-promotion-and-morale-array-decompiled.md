# Digging into the promotion effect: the morale array identified, a clean promotion rule, and a new open field

Follow-up to `full-battle-resolution-rome-vs-gaul.md`'s open question — why did three surviving units (all "average" quality) get bumped to "good" after the Rome/Gaul battle, including one (4th Bowmen) that took zero troop losses? This traces the runtime battle-unit data structure in the decompiled code to answer it, and along the way pins down the identity of the "unidentified `+0x350` field" flagged as open in `decompiled-combat-formula-structure.md`.

## The runtime battle-unit struct, mapped field by field

`TBattleMap_StartBattle` calls `FUN_00437de4` the first time a battle is entered. That function copies both armies' units from the **persistent SAV-shaped army table** (`&DAT_0047c1ec + armyIdx×0xa4`, the same 656-byte-per-army / 32-byte-per-unit-slot layout `TArmyToArmy` writes back to — see `army-to-army-transfer-confirmed.md`) into a **flat 20-slot working array per side** (`DAT_004a0344` for side A's 20 slots, `DAT_004a06b4` for side B's, each unit occupying 0x16 = 22 words = 44 bytes). Reading the copy-in code field by field against the known SAV unit-slot layout (type at +2, troops at +4, quality at +6, name at +8) pins down every global Ghidra assigned in the combat functions:

| Runtime array | Word offset in the 44-byte slot | Field |
| --- | --- | --- |
| `DAT_004a0344` / `DAT_004a06b4` | +0 | battle-grid X |
| +2 | +1 | battle-grid Y |
| `DAT_004a0348` | +2 | (copied from SAV slot+0, meaning not yet identified) |
| `DAT_004a034a` | +3 | unit type code |
| `DAT_004a034c` | +4 | **troops** (matches every prior combat report's reading) |
| `DAT_004a034e` | +5 | **quality** (copied directly from the SAV's persisted quality byte) |
| `DAT_004a0350` | +6 | **morale** — see below |
| ... | +9 | `DAT_004a0356`, the assigned-target slot (`0xffff` = none) |
| ... | +10 | `DAT_004a0358`, the unit's name string |

This corrects `decompiled-combat-formula-structure.md`'s notation: the "`+0x350` field" isn't a struct-offset from some base — it's simply the array `DAT_004a0350`, a flat per-slot morale value, distinct from (and copied independently of) the persisted `quality` field. **`DAT_004a0350` is now confirmed as the exact source of the "Morale" line in the per-unit combat info panel** documented in `full-battle-resolution-rome-vs-gaul.md` (`"Morale: normal"`, `"Morale: very low"`), closing that report's open question about the field's identity.

## The morale formula, in full

At battle start (still in `FUN_00437de4`), each unit's initial morale is:

```text
morale = clamp( random(quality × 4) + armyExperience[armyIdx],  upper bounds ≈ 90 then 60 )
```

where `armyExperience` is `DAT_0047c1fa`, read with the same per-army stride as the persistent army table — i.e. it lives at **byte offset +14 within the 16-byte army header**, immediately after `Money` (+12) and before the first unit slot (+16). `SaveArmyTable.cs`'s `ArmyRecord` currently treats bytes 14–15 as unused padding (`HeaderLength = 16` with only `X/Y/Owner/Moves/MoraleValue/Supplies/Money` — 14 bytes — actually exposed). **That's wrong**: a direct read of both saves in this pair shows a real, changing value there — `68` in `1_rome_270_winter_7.sav`, `66` in `1_rome_270_winter_9.sav`, for Rome's army 0. Not zero, not padding. Its exact meaning (a per-army veterancy counter, a readiness stat, something else) isn't identified this pass — `FUN_00437de4` increments a same-shaped array by 3 under a specific condition tied to the opponent nation's alive/dead status, which doesn't obviously explain a value that *decreased* here — but the byte is real and `IC2.Data` should stop treating it as padding.

During melee (`FUN_004393ec`), `DAT_004a0350` is adjusted by exactly `+2`/`−3` per exchange depending on which side had the better power ratio, clamped to a `[?, 99]` range — this part was already correctly described in `decompiled-combat-formula-structure.md`, just mislabeled as a struct offset.

## The quality-promotion rule: fully explained by one clean pattern

None of the recovered battle-related functions (`TBattleMap_StartBattle`'s copy-in, `TBattleMap_FinishBattle`, `TBattleMap_EndTurn`, `TBattleMap_ComputerGeneral`, `TBattleMap_Surrender`, the melee function, the shooting function) contain an explicit "increment quality" instruction — a thorough read of all of them turned up nothing. But the save data itself gives an unambiguous empirical rule. Tabulating all 19 of Rome's pre-battle units by their original army-slot index, quality, and outcome:

| Slot | Unit | Quality before | Adjacent slot wiped? | Outcome |
| --- | --- | --- | --- | --- |
| 3, 5, 10, 11, 14 | (5 units) | various | — | **wiped** (0 troops) |
| 6 | 1st Bowmen | average | yes (slot 5) | **promoted → good** |
| 9 | 3rd Foot | average | yes (slot 10) | **promoted → good** |
| 13 | 4th Bowmen | average | yes (slot 14) | **promoted → good** |
| 2, 12 | 3rd Guards, 6th Guards | good | yes (slot 3 / 11) | unchanged |
| 4 | 4th Guards | elite | yes (slot 3) | unchanged (already max tier) |
| 15 | 1st Guards | very good | yes (slot 14) | unchanged |
| 0, 7, 8, 16, 17, 18 | (6 average-quality units) | average | **no** | unchanged |

The rule that fits all 13 non-wiped units with zero exceptions: **an "average"-quality unit occupying an army slot immediately adjacent to a slot whose unit was destroyed in the same battle is promoted to "good"; units that already started above "average," and "average" units not adjacent to a casualty, are unaffected.** This is not a losses-based or morale-based effect — 4th Bowmen took zero troop losses and had "very low" morale mid-battle, yet was still promoted, while several harder-hit non-adjacent units (3rd Foot's neighbor 3rd Bowmen, 2nd Foot, etc.) were not.

This reads like a genuine "closing ranks" veterancy mechanic (a survivor next to a fallen comrade's slot gains experience) rather than a bug, but the implementing code wasn't located — it's most likely inside a unit-slot compaction/write-back routine that runs when destroyed units are removed from the army table, which wasn't among the 282 recovered RTTI method names and wasn't reachable by tracing calls from the known battle-lifecycle functions.

## What this does not establish

- The exact code implementing the adjacency-promotion rule — only its precise external behavior, from one battle's worth of data (13 relevant units, 0 exceptions).
- Whether the rule generalizes past "average → good" (e.g., would a "good" unit adjacent to a casualty ever reach "very good"? The one candidate case here, slot 2/12, sits at "good" and didn't change — consistent with a rule that only fires from the "average" tier, or with a rule that fires from any tier but happened not to trigger here for an unrelated reason).
- The meaning of the newly-flagged `ArmyRecord`+14 field, or of `DAT_004a0348` (the SAV unit-slot+0 field, copied into the runtime struct but never referenced in any decompiled combat function read so far).
- Why `DAT_0047c1fa` (the army-experience-shaped array feeding into initial morale) decreased rather than increased across this session.

## Reproduction

```text
dotnet run --project src/IC2.Inspect -- --to-json saves/1_rome_270_winter_7.sav w7.json
dotnet run --project src/IC2.Inspect -- --to-json saves/1_rome_270_winter_9.sav w9.json
# compare Rome's army-0 units by slot index and quality code between the two files
```
Ghidra: `grep -n "DAT_004a03" all_app_functions.txt` and `grep -n "0047c1ec" all_app_functions.txt` to re-trace the copy-in/runtime-array mapping; `FUN_00437de4 @ 00437de4` is the key function.

## Next checks

1. A second battle with destroyed units (ideally one with a "good"-or-above unit adjacent to a casualty and nothing else confounding) would test whether the promotion rule is "average-only" or applies at every tier.
2. Locate the slot-compaction/write-back routine directly — searching for code that shifts unit slots down (removing gaps left by destroyed units) in the region between `TBattleMap_FinishBattle` and wherever the strategic-map army display next reads the table.
3. Track `ArmyRecord`+14 across a longer save sequence with no battles at all, to see whether it drifts on its own (ordinary weekly ticks) or only changes around combat.
