# Static decompilation plan (Ghidra)

This plans the roadmap's still-open item: *"Analyze the full v1.01 EXE statically, starting from DAT/SAV I/O, end-turn, city changes, movement, combat, diplomacy, AI, and victory messages"* (`roadmap.md` §3). It exists because several mechanics cannot be recovered from save-file diffing at all — combat resolution, AI decisions, and exact cost/income formulas are computed at runtime and never fully serialized. Tooling: Ghidra 12.1.3 + Temurin JDK 21, installed locally under `%LOCALAPPDATA%\ReTools`, kept outside this repository like the game assets. **The executable is not run** — this is static analysis of the same read-only file already hashed in `impconq2-sha256.txt`, consistent with every prior report and the roadmap's explicit constraint that running the original binary is a separate, not-yet-authorized step.

## Why this is tractable

The binary is Delphi (Delphi 2 era), which embeds method-name tables and form resources far more legible than a typical stripped C/C++ build — already proven useful without a decompiler in `battle-code-entry-points.md` (found `TBattleMap.StartBattle`, `MoveHumanUnit`, `EndTurn`, `ComputerGeneral`, `Surrender`, `FinishBattle` and code-to-string cross-references purely from RTTI, before Ghidra was ever installed) and `menu-and-toolbar-inventory.md` (found event handlers `StrategicDecision`, `ChangeNation`, `ShowOnAreaMap`, `UnitMapAction` and the `TMainMenu` stream). Ghidra's headless mode can auto-analyze and export decompiled pseudocode without any GUI interaction, so this is fully drivable end-to-end.

## Existing static leads to start from

| Lead | Source |
| --- | --- |
| `TBattleMap.StartBattle` `0x00436FB4`, `PlaceUnit` `0x004375EC`, `MoveHumanUnit` `0x0043755C`, `EndTurn` `0x00437A94`, `ComputerGeneral` `0x00437AB8`, `Surrender` `0x00437AD8`, `FinishBattle` `0x00437B8C` | [battle-code-entry-points.md](reports/battle-code-entry-points.md) |
| `SHOOTS AT` string `0x004393BC` referenced at `0x00439205`; `ATTACKS` string `0x0043992C` referenced at `0x004396A7` | same |
| `TMainMenu` stream near file offset `0xEF3A6`; handlers `StrategicDecision` (bound to `sb_Pols`, "International relations"), `ChangeNation`, `ShowOnAreaMap`, `UnitMapAction` | [menu-and-toolbar-inventory.md](reports/menu-and-toolbar-inventory.md) |
| Form classes `TAreaMap`, `TUnitMap`, `TBattleMap`, `TPolitics`, `TBuildFleet`, `TArmyRecruits`, `TToEndTurn` (30 `TPF0` resources total) | [impconq2-initial-report.md](reports/impconq2-initial-report.md) |
| Literal error strings: 20 units/army, 100,000 troops/army, 100 ships when combining fleets | same |

Each form class is a direct handle onto a subsystem we've already probed empirically by save-diffing, which is the core strategy below: find the form's event-handler code the same way `TBattleMap`'s methods were found, decompile it, and check the recovered formula against real numbers already in hand.

## Setup status: working, with one gap found

Ghidra 12.1.3 + Temurin JDK 21 are installed under `%LOCALAPPDATA%\ReTools` (outside this repo). Headless import and auto-analysis of the full v1.01 EXE succeeds cleanly in about 30 seconds. Decompiling by address works and produces genuine, readable pseudocode — verified by decompiling the seven known `TBattleMap` addresses from `battle-code-entry-points.md`; `StartBattle` (`0x00436fb4`) contains the literal constant `0x1c0` (448), matching the tactical grid's known 14-tile width at 32 px/tile from `battle-observation.md`.

**Gap:** Ghidra's own analyzer does not recover Delphi's RTTI method-name tables automatically — every function comes back auto-named `FUN_xxxxxxxx` unless targeted by an address already known from prior manual byte-level reading (as `battle-code-entry-points.md` and `menu-and-toolbar-inventory.md` did without any decompiler at all). This means:

- **0. Recover Delphi RTTI symbols in Ghidra first**, before working the priority queue below — import the class/method-name tables the same way those two reports read them manually, but apply the result as real Ghidra symbol names across the whole binary. This turns every future target from "decompile this one address I already know" into "search by name and follow real cross-references," which is far less brittle and unlocks targets we haven't manually found addresses for yet (e.g. `TPolitics`, `TBuildFleet`, `TArmyRecruits`, `TToEndTurn`'s own methods have no known addresses yet). One-time investment, benefits every item below.
- Until that's done, each item in the priority queue still needs its own address found by hand (string cross-reference or nearby RTTI table), the same way `TBattleMap` was found.

## Priority queue

Ordered by (value of the unknown) × (how directly a known form/string points at it), each with the empirical numbers already available to cross-check a recovered formula against.

1. **`TBuildFleet` — fleet order cost/capacity.** Cross-check: 10 ships cost exactly 100 talents ([fleet-order-at-caere.md](reports/fleet-order-at-caere.md)); ship capacity is 500 troops/ship (user-confirmed). Small, self-contained dialog — good first target to validate the whole workflow.
2. **`TArmyRecruits` — recruitment and mercenary-hire cost formulas.** Cross-check: 1,400 light cavalry costs 105 initial / 21 quarterly ([menu-and-toolbar-inventory.md](reports/menu-and-toolbar-inventory.md)); a 15,000-troop transfer raised Rome's quarterly cost 442 → 517 ([city-units-army-transfer-and-mercenaries.md](reports/city-units-army-transfer-and-mercenaries.md)); 6,438 mercenaries cost 51 quarterly ([mercenary-pool-record.md](reports/mercenary-pool-record.md)). Also the source of the still-unidentified mercenary `Label` field — decompiling the hire code should reveal what catalog it indexes.
3. **`TPolitics` — tax income and diplomatic reparation formulas.** Cross-check: Rome's tax 15%→20% raised displayed income 366→488 ([rome-tax-increase-and-sidon-capture.md](reports/rome-tax-increase-and-sidon-capture.md)); Ptolemaic's treasury dropped exactly −2,269 paying a reparation, a clean payer-side match with no clean receiver-side match ([diplomatic-reparations-and-more-captures.md](reports/diplomatic-reparations-and-more-captures.md)) — decompiling should explain that asymmetry.
4. **`TBattleMap`/`ComputerGeneral`/`FinishBattle` — combat loss formula.** Cross-check: a full recorded battle with exact before/after troop totals by class for both sides, one single shot (9 losses from a 2,590-troop unit hitting a 3,682-troop unit) and one single melee (attacker 1,121→loses 154, defender 1,103→loses 134) ([battle-observation.md](reports/battle-observation.md)); separately, a uniform ~2.7% loss across every unit in a whole army after a siege turn ([field-recruitment-uniform-attrition-and-fleet-drift.md](reports/field-recruitment-uniform-attrition-and-fleet-drift.md)) — worth checking whether that's the same mechanic as per-engagement combat or a distinct occupation/attrition pass. Also recover captured spoils (96 talents + 619 tons supply from destroying a ~62,000-troop army) and the morale swing (9→2 in one turn with no recorded combat) that's currently unexplained.
5. **City capture/allegiance/loyalty resolution.** Cross-check: five consistent forced captures (population −25–29%, fortification and loyalty drop, tribute/allegiance untouched) versus two peaceful defections (nothing but loyalty moves) versus one same-turn defect-then-reconquer (siege damage persists, loyalty jumps to the final owner) — all in the capture reports. Also the still-unresolved Felsina screenshot anomaly (tribute/supply values matching neither adjacent save) may simply be a UI-refresh-timing quirk visible in this code.
6. **DAT/SAV I/O routines — validate/extend the byte-offset model wholesale.** This is the highest-leverage target for the parser itself: finding the actual save/load routines could confirm every offset in `IC2.Data` at once, and directly reveal the remaining unknowns — fleet record owner code and the meaning of its unexplained zero words, the mercenary `Label` field, city-unit `StateCode`, and the ~2,442 still-unidentified bytes immediately after the mercenary table.
7. **`TToEndTurn` — turn/season/quarter sequencing.** Cross-check: week resets to 1 after week 11 at both observed season boundaries; the "51 quarterly" mercenary upkeep and "quarterly" recruitment costs need a real quarter boundary in-code to explain when they're actually charged.

## Explicitly out of scope for this pass

- Full AI opponent strategy — the roadmap's own scope decision favors faithful *rules* with a redesigned interface/AI, not a byte-for-byte AI port; only decode enough of `ComputerGeneral` to understand war/peace/recruit triggers if it falls out cheaply from the combat work above.
- Victory conditions — no current urgency.
- Tactical battle grid rendering and other UI-only code.

## Method, per target

1. Locate the form/procedure the same way prior reports did: Delphi RTTI method-name tables, the `TMainMenu`/form-adjacent streams, or a literal-string cross-reference (e.g. searching for dialog text like "quarterly", "Current tax", "New income") to find the owning code.
2. Decompile with a Ghidra headless script exporting C-like pseudocode for that function and its immediate callees to a text file.
3. Read the pseudocode and translate it to a plain-language rule, citing the function address(es) — same evidentiary standard as every existing report (`docs/reports/*.md`), which already cite save offsets and screenshot values this way.
4. Check the recovered formula against the empirical numbers listed above. State explicitly whether it matches, and if not, say so rather than adjusting the empirical reading to fit.
5. Write a new `docs/reports/*.md` and update `IC2.Data` doc comments / `docs/roadmap.md` only once a formula is actually confirmed this way — candidate/unconfirmed decompiled readings get the same "candidate" language already used throughout this project.

## Deliverables

- This plan (kept up to date as targets are completed or reprioritized).
- One `docs/reports/*.md` per confirmed subsystem, in the existing style.
- Ghidra project files stay outside Git (added to `.gitignore` alongside other local-only paths); only small, reusable headless export scripts get committed if they prove generally useful.
