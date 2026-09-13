# The terrain table, located in the DAT file — and a correction to the river-only movement rule

`decompiled-army-movement-and-river-cost.md` decompiled `FUN_0044d420` and read its guard `if (2 <= cellValue <= 11)` as "the confirmed river-value range", concluding that **only rivers cost movement points**. That conclusion is wrong, and this report corrects it: the guard covers *all land terrain*, the lookup table it indexes is now located in the DAT file, and its literal values are below. Plain and Desert cost 1, Forest 2, Mountains 4, and every river code 4.

The mistake was a legitimate one to make — `rivers-and-map-markers.md` and `roadmap.md` §2 both record rivers as cell values **6–11**, so `2..11` looked like "rivers plus a couple of unmapped codes". It wasn't checked against the actual table, which is the exact failure mode `docs/design-audit.md` was written to hunt for.

## Locating the table

`TInformation_ShowArmyDetails` (`0x0043c33c`) prints the army-information panel's `Terrain -` line from

```c
FUN_00405bc8(&DAT_0049f13d, &DAT_004792e4 + (short)(&DAT_0047c1f4)[army * 0x148] * 0xe);
```

i.e. a **string** table at `0x004792e4` with a 14-byte stride, indexed by the same cell code that `FUN_0044d420` feeds to the cost table at `0x004792f0`. `0x4792f0 − 0x4792e4 = 12`, so one record is `[12-byte NUL-padded name][2-byte move cost]` — and the names are the anchor string the earlier pass said it didn't have.

Applying the `unit-type-stat-table-in-dat.md` method (search the DAT file for the literal names) finds the table at **DAT offset `0x1F622`**, 0x332 bytes after the unit-type table at `0x1F2F0` — matching the in-memory gap `0x4792E4 − 0x478FB0 = 0x334` to within the 2-byte alignment difference already implied by the record boundaries.

## The table, fully decoded

| Cell code | Name | Move cost |
| ---: | --- | ---: |
| 0 | `Sea` | 1 |
| 1 | `Sea` | 3 |
| 2 | `Plain` | 1 |
| 3 | `Desert` | 1 |
| 4 | `Forest` | 2 |
| 5 | `Mountains` | 4 |
| 6 | `River` | 4 |
| 7 | `River` | 4 |
| 8 | `River` | 4 |
| 9 | `River` | 4 |
| 10 | `River` | 4 |
| 11 | `River` | 4 |

Twelve entries, exactly covering the cell-code range `0..11` that the two movement functions between them accept. Entry 12 onward is the unrelated `"not ready"` / quality-tier string block, so the table ends at 11.

Two previously-open roadmap items close here:

- **"Determine the remaining tile meanings, including water value `1`."** Value `1` is a second `Sea` type costing **3** move points to cross versus `0`'s **1** — a deep/open-water versus coastal-water distinction, in cost if not in name.
- **The `DAT_004792f0` table values**, listed as next-check #1 in `decompiled-army-movement-and-river-cost.md`.

It also explains a detail already in the record: `army-records-and-roman-roster.md` reported the Roman army at `(100, 42)` displaying terrain **River** with a DAT cell value of **9** — table entry 9 is `River`. (That same report read the army record's `+8` word as a "candidate morale value" of `9`; see the correction note in that report — `+8` is the saved terrain cell, which is why it read 9.)

## The corrected movement rule

`FUN_0044d420`, one step of the Bresenham walk (`TUnitMap_CheckForMove` → `TUnitMap_MoveHumanArmy` → `FUN_0044d734` → here):

```c
cell = map[nx][ny];
if (cell < 12 && cell > 1                                  // ALL land terrain: 2..11
    && terrain[cell].moveCost <= army.moves
    && FUN_0044d31c(nx, ny) < 10) {
    swap(map[nx][ny], army.savedCell);                     // army+8 holds the covered cell
    army.x = nx; army.y = ny;
    army.moves -= terrain[cell].moveCost;
} else {
    if (nationIsAI && 2 <= cell && cell < 12 && army.moves < terrain[cell].moveCost)
        army.moves = 0;
    abort the walk;
}
```

Three corrections to the earlier reading:

1. **Every land tile costs its table value**, not just rivers. Plain and Desert 1, Forest 2, Mountains 4, River 4. The generic-4X intuition that forests and mountains slow you down turns out to be right for this game after all — but the numbers are the game's, not a guess, and rivers are *equal to* mountains rather than uniquely expensive.
2. **The "insufficient moves zeroes the whole turn's movement" rule fires only for AI nations.** The guard is `(&DAT_00474b00)[DAT_004a0320 * 0x494] == '\0'`, i.e. the active nation's human/computer flag at nation record `+0x490` is `0` (computer). For a human player the walk simply aborts at the blocking tile with moves intact. The earlier report stated this as a universal rule.
3. **Codes ≥ 12 are not steppable at all** by this walk — a city (20–99), army (200–247) or fleet (300–347) marker in the path aborts the move rather than costing anything. Interactions with those are handled at the destination (`TUnitMap_SelectUnit`, `FUN_0044d734`'s tail), not mid-path.

## Fleet movement uses the same table

`TUnitMap_MoveHumanFleet` → `FUN_0044e094` → `FUN_0044dd70` is the same walker with one changed guard:

```c
if (cell < 2 && terrain[cell].moveCost <= fleet.moves && FUN_0044dcac(...) < 10) { ... }
```

Fleets traverse **only** cell codes 0 and 1 (the two `Sea` entries), paying 1 and 3 respectively, and store the covered cell in fleet record `+24` (`DAT_0049c284`). A fleet carrying an army drags the army's coordinates along with it (`fleet+22 >= 0` → write the fleet's new x/y into that army record). The same AI-only moves-zeroing applies.

## What this does not establish

- Whether the DAT offset `0x1F622` is stable across DAT builds — only the one v1.01 DAT was checked, same caveat as `unit-type-stat-table-in-dat.md`.
- What `FUN_0044d31c(...) < 10` / `FUN_0044dcac(...) < 10` actually measure (a distance-style check against the destination; it gates each step but was not decompiled here).
- Which cell codes the *original map* actually uses for Desert/Forest/Mountains — `rivers-and-map-markers.md` matched five common values to screenshot terrain, but this report only establishes what the engine's table says each code costs and is called.
- What sets an army's weekly `Moves` maximum (still open, as before).

## Reproduction

```python
import struct
d = open(r"Imperial Conquest 2.dat", "rb").read()
base = d.find(b"Sea")          # 0x1f622 in the checked file
for k in range(12):
    rec = d[base + k*14 : base + k*14 + 14]
    print(k, rec[:12].split(b"\0")[0].decode(), struct.unpack("<h", rec[12:14])[0])
```

Ghidra: `FUN_0044d420 @ 0044d420` (army step), `FUN_0044dd70 @ 0044dd70` (fleet step), `TInformation_ShowArmyDetails @ 0043c33c` (the `DAT_004792e4` name lookup that anchored the search).

## Next checks

1. Render the original map coloured by these 12 codes and compare against the registered screenshots in `rivers-and-map-markers.md`, to tie each code to a visible terrain type and confirm the Desert/Forest/Mountains assignment on the real map.
2. Confirm the human-vs-AI asymmetry in rule 2 with a controlled save pair: march a human army at a mountain/river tile with 1–3 moves left and check whether `Moves` survives.
