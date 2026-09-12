# Army-to-army transfer: exact conservation, confirmed by video, save diff, and code

The user's second video-plus-saves submission (`2_rome.txt`): `1_rome_270_winter_3.sav → 1_rome_270_winter_5.sav`, with `recordings/bandicam 2026-09-12 23-56-28-394.mp4` covering "reorganizing the two armies, preparing for battle." This is the first use of the "record between two saves" methodology proposed after `battle-recording-melee-cap-confirmed.md`, and it worked exactly as hoped: the video shows the "Army to army transfer" dialog directly, letting three independent sources — video, save diff, and decompiled code — all cross-check each other.

## What the saves show

Six units moved from Rome's army 2 to army 0:

```text
3rd Lancers (light cavalry, 6,695), 2nd Foot (light infantry, 5,203), 1st Guards (heavy infantry, 4,651),
5th Bowmen (archers, 3,317), 4th Bowmen (archers, 3,312), 6th Guards (heavy infantry, 4,527)
```

| | Before | After | Δ |
| --- | ---: | ---: | ---: |
| Army 0 troops | 72,177 | 99,882 | **+27,705** |
| Army 2 troops | 55,932 | 28,227 | **−27,705** |
| Army 0 money | 186 | 256 | **+70** |
| Army 2 money | 110 | 40 | **−70** |

Both troops and money are **exactly reciprocal** — nothing lost, nothing gained, in either transfer. Sum of the six transferred units' troops (6,695+5,203+4,651+3,317+3,312+4,527) = 27,705, matching the army-level delta exactly.

## The video: literally the dialog in action

Frame extraction (1 frame/3s across the ~4-minute recording) caught the **"Army to army transfer"** dialog directly, mid-use: two scrollable unit lists ("Units in first army" / "Units in second army"), `Transfer`/`Disband` buttons under each, and supply/money boxes with `10s`/`100s` stepper buttons. Across three sampled frames, the first army's unit count visibly drops (12 → 11 → 9 units) while the second's rises (13 → 14 → 16), confirming units are moved one at a time by clicking `Transfer` on a selected list entry — exactly the stepwise mechanic the decompiled code implies (see below). The displayed supply/money figures (365/470 and 110/186) match the saves' *before* values exactly.

## The code: confirms the mechanism precisely

`TArmyToArmy`'s methods (already in the recovered RTTI symbol list, decompiled for the first time here):

- **`MoveUnit`** copies one 32-byte unit-slot record into the destination army's next free slot in a working buffer, zeroes the source slot's troop count, and calls `RemoveUnit` to drop it from the source's displayed list — confirming transfers are staged in a working copy, not applied live to the real army records.
- **`ChangeMoney`/`ChangeSupply`** implement the `10s`/`100s` steppers seen in the video: each click adjusts the transfer amount by 10 or 100, clamped so you can never move more than the other army actually has (`min(otherArmyAmount, ...)`), and the two displayed totals are updated as an exact `+n`/`−n` pair — the reciprocal-conservation behavior observed in the saves is not incidental, it's how the dialog is built.
- **`OK`** commits the whole working buffer back into the real global army-table slots for both armies, then does something not visible in this particular example: it checks whether either army's unit count reads zero after the transfer, and if so, **merges that now-empty army's supply and money into the other and disbands it** (`FUN_0044ab90`, the same disband routine seen in the nation-elimination cascade in `decompiled-defection-and-siege-attrition.md`). Neither army went to zero units here, so this branch didn't fire, but it's a real, previously-unknown consequence of transferring *every* unit out of an army via this dialog.
- `OK` also contains a supply-rebalancing check comparing each army's stock against a readiness-derived threshold (`FUN_0044a698`, the same helper used in the nation-tax-base and mercenary-capacity code) and can silently move supply between the two armies if one falls short — not observed triggering in this example (both saves' supply values are consistent with ordinary weekly consumption from the surrounding turn, not a corrective transfer), but a mechanic worth watching for in future controlled saves.

## What this does not establish

- The auto-disband-on-empty-army branch — not exercised in this example, only read from code.
- The supply-rebalancing branch's exact trigger threshold.
- Why these specific six units were chosen (a player tactical decision, not a formula).
- The `moves` field's post-merge recomputation (army 0's max moves went 7→6, army 2's went 8→9) — plausibly tied to the changed unit-type mix, but the exact formula wasn't traced this pass.

## Reproduction

```text
ffmpeg -i "recordings/bandicam 2026-09-12 23-56-28-394.mp4" -vf fps=1/3 frames3/f_%03d.png
dotnet run --project src/IC2.Inspect -- --to-json saves/1_rome_270_winter_3.sav w3.json
dotnet run --project src/IC2.Inspect -- --to-json saves/1_rome_270_winter_5.sav w5.json
```
Compare Rome's `armies` arrays between the two JSON files.

## Next checks

1. A recording where a transfer empties one army completely would confirm the auto-disband branch directly.
2. A recording with a deliberate supply/money slider adjustment plus a same-day save (no other turn activity) would let the `10s`/`100s` step amounts be checked against the save diff precisely, isolating them from ordinary weekly consumption.
3. The army `moves` recomputation after a unit-composition change is still an open formula.
