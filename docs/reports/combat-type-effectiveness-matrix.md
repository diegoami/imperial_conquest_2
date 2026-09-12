# The combat type-effectiveness matrix, located in the DAT file

Continuing from [unit-type-stat-table-in-dat.md](unit-type-stat-table-in-dat.md), which located the flat per-unit-type stat table. The melee formula in [decompiled-combat-formula-structure.md](decompiled-combat-formula-structure.md) also reads a **two-dimensional** `[type][type]` table (`DAT_0047946c` in the decompiled code, indexed with a row stride of 10 bytes = 5 words and a column stride of 2 bytes — exactly a 5×5 word matrix for the game's 5 unit types). Like the flat table, Ghidra couldn't read this memory from the running EXE image (uninitialized/BSS). It sits in the DAT file **immediately after** the unit-type stat table ends (`0x1f3b8`), as 25 small integers (0–4) — a clean, bounded 5×5 grid, with clearly different, larger-scale data immediately following it.

## The matrix

Read as `value[attackerType][defenderType]` (candidate orientation — see caveat below):

| Attacker ↓ / Defender → | Light Inf | Heavy Inf | Archers | Light Cav | Heavy Cav |
| --- | ---: | ---: | ---: | ---: | ---: |
| **Light Inf** | 4 | 1 | 0 | 3 | 0 |
| **Heavy Inf** | 1 | 4 | 3 | 3 | 4 |
| **Archers** | 0 | 1 | 3 | 0 | 1 |
| **Light Cav** | 4 | 4 | 3 | 1 | 0 |
| **Heavy Cav** | 1 | 4 | 3 | 0 | 2 |

With this orientation, the pattern is militarily sensible: light cavalry is strong against both infantry types (4, 4) but weak against itself and heavy cavalry (1, 0) — consistent with cavalry being effective at running down infantry but not decisive against other cavalry. Heavy infantry is strong across the board (1/4/3/3/4). Archers are weak against both cavalry types moving through them (0 vs light cav) but middling elsewhere. This reading was chosen because it produces coherent unit-vs-unit relationships; the alternative orientation (`[defenderType][attackerType]`) produces a less coherent pattern (e.g. archers effectively immune to light cavalry and light infantry as defenders, which fits less well with typical wargame design).

**Caveat:** the row/attacker vs. column/defender assignment is inferred from which orientation produces sensible game design, not fully pinned down by re-tracing the decompiled index arithmetic with full confidence — the two record-pointer fields feeding the two index terms (`sVar2` from the global per-unit-type array, and a field read from the attacking unit's own in-battle record at a small offset) weren't both traced to certain semantic identity. Both are plausible reads of the same underlying data; only the axis labeling is uncertain, not the 25 values themselves.

## How it's used

From `decompiled-combat-formula-structure.md`'s melee formula:

```text
atkPower = (matrix[attackerType][defenderType] * attackerTroops * attackerQualityTerm) / 2000 + 12
defPower = (matrix[defenderType][attackerType] * targetTroops   * defenderQualityTerm)   / 2000 + 12
```

A matrix value of `0` doesn't zero out the whole term — it just removes that factor, leaving `atkPower` (or `defPower`) at whatever the `+ 12` floor plus quality contribution gives, i.e. that side still deals/takes some minimum damage even at a "0" matchup, just without the type-effectiveness multiplier.

## What this does not establish

- Definitive attacker/defender axis assignment (see caveat above) — the values are read correctly, the orientation is a reasoned guess.
- Whether this table's DAT-file position (immediately after the unit-type table) is a stable, principled layout or specific to this one checked file.
- A full numeric simulation against the complete recorded battle in `battle-observation.md` — this report only fills in the last previously-missing constant, it doesn't yet run the formula end-to-end against real data.

## Reproduction

```python
data = open("Imperial Conquest 2.dat", "rb").read()
base = data.find(b"Light infantry")
table_end = base + 5 * 0x28  # end of the unit-type stat table
import struct
matrix = struct.unpack_from("<25h", data, table_end)  # 5x5, row-major
```

## Next checks

1. **Simulate the full recorded battle** from `battle-observation.md` (complete before/after troop totals by unit class for both sides) using the now-complete formula (unit-type table + this matrix + the melee formula structure) and compare. This is the real test of both the formula and the row/column orientation — if one orientation reproduces the recorded outcome and the other doesn't, that resolves the caveat above definitively.
2. Confirm the matrix's DAT-file position is stable by checking a second DAT file if one becomes available (e.g. the demo version, if ever obtained).
3. Identify the remaining unexplained unit-type table fields (`+0x20`, `+0x26` from the prior report) — they may also feed into combat and could matter for a faithful simulation.
