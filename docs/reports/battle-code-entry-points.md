# Battle code: first static entry points

The full-version `Imperial Conquest 2.exe` (SHA-256 `9d753d5de78801f2368d06125cc87f296b92f671b686c39fd6ac149838ebba31`) was inspected as a PE32 file. It was **not executed**. Battles need not be serialized in `.sav` files for their rules to be recoverable: the tactical state and algorithms can exist only in memory while the game runs.

The executable retains Delphi-style method-name tables in its `CODE` section. One table belongs to `TBattleMap` and associates these names with executable virtual addresses:

| Method | Virtual address | Likely role from its name; behavior not yet verified |
| --- | ---: | --- |
| `StartBattle` | `0x00436FB4` | Initialize tactical battle |
| `PlaceUnit` | `0x004375EC` | Place a tactical unit |
| `MoveHumanUnit` | `0x0043755C` | Handle player movement |
| `EndTurn` | `0x00437A94` | Advance a tactical turn or phase |
| `ComputerGeneral` | `0x00437AB8` | AI battle decisions |
| `Surrender` | `0x00437AD8` | Surrender action |
| `FinishBattle` | `0x00437B8C` | Finish tactical battle |

A separate named-method sequence contains `StartBattle` at `0x0045C178`, `BattleConclusion` at `0x0045C1C8`, and `ResetBattle` at `0x0045C208`. Its owning class and exact relationship to `TBattleMap` still need confirmation.

The battle text is tied to code references, not merely to inert resources. For example, code at `0x00439205` references the `SHOOTS AT` message at `0x004393BC`, and code at `0x004396A7` references `ATTACKS` at `0x0043992C`. Nearby strings report troop losses. These are useful anchors for locating ranged and melee resolution, but the damage formulas, random-number calls, unit statistics, initiative, terrain effects, morale, and victory rules have **not** yet been reconstructed.

The [battle recording](battle-observation.md) supplies observable checks: placement and movement phases, a 14 × 12 tactical grid, examples of shooting and melee losses, and final casualty and capture totals. It can validate a recovered rule but cannot by itself establish all combat formulas. The absence of intermediate battle saves is therefore a validation challenge, not a blocker to static code analysis.

## Next static work

1. Trace calls and data accesses from `TBattleMap.StartBattle`, `MoveHumanUnit`, `EndTurn`, `ComputerGeneral`, and `FinishBattle`; establish battle-state and unit-record layouts.
2. Trace backward from the shooting/melee message references to their loss calculations and random-number use. Distinguish UI formatting from combat calculation.
3. Recover turn/phase order, movement ranges, terrain modifiers, morale changes, surrender, victory, casualties, and captured supplies/money. Record each rule with its code address and confidence level.
4. Implement the recovered simulation as a deterministic C# module with an injectable random source. Compare it with the recorded battle and future controlled observations before calling it compatible.
