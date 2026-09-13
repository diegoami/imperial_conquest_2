# Army movement, decompiled: rivers cost moves, plain terrain apparently doesn't

The user asked directly whether I actually knew the game's movement rules (does army size or terrain affect how far an army can move) before we designed around an assumption. Good challenge — I hadn't decompiled this at all; `mobilization-movement-and-city-capture-modes.md` only had two unconfirmed data points suggesting `Moves` might be a per-army constant. This traces the real strategic-map movement code for the first time.

## The dispatch chain

`TUnitMap_CheckForMove` → `TUnitMap_MoveHumanArmy` → `FUN_0044d734` (the actual path walker) → `FUN_0044d420` (executes one step of the walk). `FUN_0044d734` implements a **Bresenham line walk** from the army's current tile toward the clicked destination — the whole multi-tile move is issued and processed in one player click, not one tile at a time.

## The finding: only specific cell codes cost movement points, and only rivers are confirmed among them

In `FUN_0044d420`, for each intermediate step of the walk:

```text
cellValue = mapCell[nextStep.x][nextStep.y]
if (2 <= cellValue <= 11) {                      // matches the confirmed river-value range (rivers-and-map-markers.md)
    cost = DAT_004792f0[cellValue]                // a small lookup table, 14-byte stride, keyed by cell code
    if (cost <= army.movesRemaining) {
        // move onto this tile, then:
        army.movesRemaining -= cost
    } else {
        army.movesRemaining = 0                  // not enough moves to cross: the whole move is aborted THIS turn
    }
}
// else: no cost check, no moves decrement at all in this function
```

Two real, previously-undocumented rules fall out of this:

1. **Crossing a river-coded tile costs a specific number of move points, looked up by the exact cell code** (not a flat "rivers cost 2" rule — different river-crossing-difficulty codes plausibly cost different amounts, since 10 distinct codes 2–11 each get their own table entry).
2. **Insufficient moves to cross a river doesn't just block that step — it zeroes the army's remaining moves for the turn.** Attempting a crossing you can't afford ends your movement entirely, not just at the river's edge.
3. **Ordinary terrain (plain, and whatever the other non-river cell codes represent) triggers none of this.** No cost lookup, no decrement, in this function, for any cell code outside 2–11. This directly contradicts the "generic per-tile terrain cost" assumption I'd have designed from a typical-wargame prior without checking — the evidence says rivers are the *only* terrain feature that costs movement points in the original, not a general terrain-difficulty system.

## What this doesn't establish

- **The actual `DAT_004792f0` table values** — its location wasn't pinned to a DAT-file offset this pass (unlike the unit-type stat table and combat matrix, which were found by string search; this table has no obvious anchor string). It sits 380 bytes before the known combat-effectiveness-matrix address in memory (`0x4792f0` vs. `0x47946c`), which may or may not reflect DAT-file adjacency.
- **Whether "Moves" itself depends on army composition or size at all.** This pass found *what consumes* moves (only river crossings, per this function), not what *sets* the weekly maximum — `decompiled-turn-and-calendar-sequencing.md` already found moves are recomputed weekly and reduced by low "readiness," but never tied moves to troop count or unit-type mix. The per-unit `Moves` stat in `unit-type-stat-table-in-dat.md` (light infantry 4, heavy infantry 2, archers 4, light cavalry 6, heavy cavalry 5) governs the *tactical battle* grid, not the strategic map — conflating the two would be a mistake worth flagging explicitly, since they're easy to confuse and this project hadn't previously distinguished them clearly.
- Whether other non-plain terrain (forest, mountain — codes not yet mapped to a specific value range) hits some *other* cost-check elsewhere in the code, since this function's `if (2 ≤ cellValue ≤ 11)` guard only self-evidently covers the confirmed river range. Not ruled out, just not found.
- What happens when the destination is a city/army/fleet marker mid-path rather than at the final tile — only the arrival-at-final-destination case (`FUN_0044d734`'s own tail logic: entering a city triggers a siege attempt via `FUN_0044b27c` if hostile, or an unidentified `FUN_0044f6d8` if friendly; landing on another army's marker triggers `FUN_0044aee4`) was read this pass, not the full set of mid-path interactions.

## Practical implication for `game-design.md`

The movement section should be corrected: don't invent a generic per-terrain-type movement-cost table as a `[designed]` placeholder modeled on typical 4X conventions — the actual original rule (rivers specifically cost moves, looked up by exact river-code; nothing else does, per this code path) is more specific and worth preserving as `[confirmed structure, exact costs unextracted]`, with the true numbers backfilled once the table is located, rather than replaced by an invented "forests cost 2, mountains cost 3" rule that has no evidence behind it.

## Reproduction

```text
grep -n "TUnitMap_" delphi_symbols.tsv
# then in all_app_functions.txt: TUnitMap_CheckForMove -> TUnitMap_MoveHumanArmy -> FUN_0044d734 -> FUN_0044d420
```

## Next checks

1. Locate `DAT_004792f0` in the DAT file to extract the actual per-river-code costs (no anchor string found yet — may need a memory-layout-relative search from the now-known combat-matrix offset, or a brute-force scan for a plausible small-integer table of the right size).
2. Determine what (if anything) governs a strategic army's weekly `Moves` maximum beyond the already-confirmed readiness-based reduction — specifically, whether troop count or unit-type composition factors in at all, since two data points in `mobilization-movement-and-city-capture-modes.md` were consistent with "constant regardless of composition" but not conclusive.
3. Check whether forest/mountain cell codes hit a cost check elsewhere in the movement code, or are genuinely free to cross.
