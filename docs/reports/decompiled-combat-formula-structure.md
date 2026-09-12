# Combat resolution, decompiled: structure confirmed, constants partly open

Continuing from [decompiled-recruitment-cost-formula.md](decompiled-recruitment-cost-formula.md). This traces the battle AI dispatch chain from `TBattleMap_ComputerGeneral` down to the actual shooting and melee resolution code — the part of the game that has never been recoverable from save files at all, since tactical battles aren't serialized.

## The AI dispatch chain

`TBattleMap_ComputerGeneral` (`0x00437ab8`) itself does almost nothing — it just sets a "which side is thinking" flag and calls a shared routine:

```text
TBattleMap_ComputerGeneral → FUN_00439c84 (per-turn loop: repeat FUN_00439ce8 until a human's turn or battle ends, then FinishBattle)
                            → FUN_00439ce8 (dispatches to one of two move-generation modes)
                              → FUN_004381a4 (unit placement — see below)
```

`FUN_004381a4`, reached this way, turned out to be **unit placement**, not combat: it iterates up to 20 unit slots (`0x13 + 1`, independently reconfirming the "20 units per army" limit from `impconq2-initial-report.md`'s error strings) and assigns each a grid position in a formation — column position cycling through a 3-wide block, row position starting at row 2 (one side) or row 9 (the other) of the 14×12 grid and stepping inward, with unit-type ordering read from a lookup table. This documents AI formation behavior but not damage.

The actual combat math was reached directly instead, via the known string cross-references from `battle-code-entry-points.md` (`SHOOTS AT` at `0x004393bc`, referenced at `0x00439205`; `ATTACKS` at `0x0043992c`, referenced at `0x004396a7`) — Ghidra's `getFunctionContaining()` resolved both references to their containing functions, `FUN_0043910c` (shooting) and `FUN_004393ec` (melee).

## Shooting (`FUN_0043910c`)

```text
range      = FUN_0043845c(shooterSlot, targetSlot)     // not yet decompiled
loss       = Random(range) + Random(range)
target.troops -= loss
secondaryHit = clamp(loss * 35 / (target.troops + 1), floor≈3)
target.<field @ +0x350> -= secondaryHit
```

Troop loss applied directly to the target is the sum of two random rolls bounded by a range computed from both units (`FUN_0043845c`, not yet traced). A second, smaller value hits a different per-unit field (not the troop count) — plausibly a morale or cohesion stat, not yet identified. The single recorded shot in `battle-observation.md` (2,590-troop light infantry shooting a 3,682-troop heavy infantry unit, 9 troop losses) is consistent with a small-range random sum, but `FUN_0043845c`'s exact output for that pairing wasn't solved.

## Melee (`FUN_004393ec`): the more complete result

Iterates the same up-to-20 unit slots; for each attacker with a live assigned target:

```text
defFactor   = clamp(FUN_00438420(targetSlot), floor≈4)          // FUN_00438420 not yet decompiled

atkPower = (typeTable[attackerType][targetType] * attackerTroops * (attackerQualityTerm)) / 2000 + 12
defPower = (typeTable[targetType][attackerType] * targetTroops  * (targetQualityTerm))    / 2000 + 12

exchanges_atk = (attackerTroops * defPower / atkPower) / 12 + 1
roll_atk      = Random(exchanges_atk) + Random(exchanges_atk)
raw_atk_loss  = roll_atk * (5 - defFactor) / 5,  capped at 30,000
atk_loss      = min(raw_atk_loss, attackerTroops * 4 / 10) + 1   // capped at 40% of attacker's own troops

exchanges_def = (targetTroops * atkPower / defPower) / 10 + 1
roll_def      = Random(exchanges_def) + Random(exchanges_def)
raw_def_loss  = roll_def * (defFactor * 2 + 5) / 5,  capped at 30,000
def_loss      = min(raw_def_loss, targetTroops * 4 / 10) + 1     // capped at 40% of defender's own troops

attackerTroops -= atk_loss
targetTroops   -= def_loss
attacker.<field @ +0x350> += (whichever side had the better troops/power ratio ? +2 : -3)
target.<field @ +0x350>   += (the other adjustment)
```

**Both sides lose troops simultaneously in one melee resolution** — not a one-way "attacker deals damage" model. This matches the structure (not just the scale) of the single recorded melee in `battle-observation.md`: a 1,121-troop attacker and a 1,103-troop defender both took losses in the same exchange (154 and 134 respectively — 13.7% and 12.1% of their own troops, both comfortably under the 40%-of-own-troops cap derived above, consistent with the loss being random-roll-driven rather than cap-driven in that example). The per-side `+2`/`−3` adjustment to the unidentified `+0x350` field (whichever side had the better power ratio gains, the other loses more) is a strong candidate for the morale mechanic — the field is clamped to `[?, 99]`, and morale in this project's data has only ever been observed as small integers in roughly that range.

`typeTable` (`DAT_0047946c`) is indexed by `[attackerUnitType][targetUnitType]` (and the reverse for defense), giving a unit-type effectiveness matrix — not yet extracted, but its existence and indexing direction are now confirmed structurally.

## What this does not establish

- `FUN_0043845c` (shooting range), `FUN_00438420` (melee defense factor), and the `typeTable` matrix's actual values — needed to predict a specific numeric outcome rather than just confirm the formula's shape.
- Whether the `+0x350` field is morale specifically, or something else.
- Terrain, initiative/turn-order, and surrender/rout conditions beyond "troops reach 0."
- The random-number source itself (`FUN_0040284c`) — assumed to be a bounded uniform RNG call from its usage pattern, not independently verified.

## Reproduction

Same toolchain as prior decompilation reports. This pass required `getFunctionContaining()` instead of `getFunctionAt()`/`createFunction()` in `ExportAddresses.java`, since the known string-reference addresses (`0x00439205`, `0x004396a7`) point mid-function, not to function entry points.

## Update: the 40%-cap confirmed exactly against new recorded exchanges

`battle-recording-melee-cap-confirmed.md` extracted six fresh combat-resolution panels from `bandicam 2026-09-12 05-27-11-464.mp4` (a stretch never sampled in `battle-observation.md`) via frame extraction. Four of five melee exchanges hit the defender-loss cap `floor(0.4 × defenderTroops) + 1` **exactly**, and the fifth falls below it exactly where the formula predicts it should (attacker too weak relative to defender to force the cap). This is the strongest numeric confirmation of any combat constant in this project — not simulated, directly observed.

## Next checks

1. Decompile `FUN_0043845c`, `FUN_00438420`, and extract the `DAT_0047946c` type-effectiveness table to get concrete numbers, then simulate the full recorded battle from `battle-observation.md` (which has complete before/after troop totals by class for both sides) and compare. **Update: `FUN_0043845c`/`FUN_00438420` are now decompiled and the matrix is now located — see `unit-type-stat-table-in-dat.md` and `combat-type-effectiveness-matrix.md`.**
2. Identify the `+0x350` field by cross-referencing it against a save-observable morale value across a controlled single-battle save pair.
3. Trace `FUN_0043a31c` (the other branch `FUN_00439ce8` can take) to see whether it's an alternate AI mode or the human-move-confirmation path.
4. A closer, more evenly-matched recorded fight (not one side heavily favored) would exercise the un-capped formula branch and could resolve the effectiveness matrix's attacker/defender axis ambiguity, which this lopsided dataset couldn't.
