# The unit-type stat table, located in the DAT file and fully decoded

Following up on `decompiled-recruitment-cost-formula.md`'s open price-table values and `decompiled-combat-formula-structure.md`'s open range/type constants: the decompiled recruitment and shooting-range code both reference a per-unit-type table indexed by a `0x28`-byte (40-byte) stride. Trying to read it directly out of the running EXE's memory in Ghidra failed — `MemoryAccessException: Unable to read bytes`, meaning that memory is uninitialized (BSS) in the EXE and is populated at runtime from an external file. Searching `Imperial Conquest 2.dat` for the five known unit-type names found it directly.

## Locating and structure

```text
Light infantry @ 0x1f2f0
Heavy infantry @ 0x1f318   (+0x28 from the previous)
Archers        @ 0x1f340   (+0x28)
Light cavalry  @ 0x1f368   (+0x28)
Heavy cavalry  @ 0x1f390   (+0x28)
```

Exactly 40 bytes (`0x28`) apart, matching the stride already seen in the decompiled recruitment/shooting code. Each 40-byte record is `[16-byte full name][8-byte abbreviation][16 bytes / 8 words of stats]`, both string fields NUL-padded to their fixed width (confirmed on both the longest name, "Light infantry" exactly filling 14+2 padding bytes, and the shortest, "Archers" filling 7+9). The abbreviations — `Lit inf`, `Hvy inf`, `Archers`, `Lit cav`, `Hvy cav` — are exactly the strings already seen in this project's own save data (e.g. the Gallic mercenary hire's "Lit Inf" from `field-recruitment-uniform-attrition-and-fleet-drift.md`).

## The 8 stat words, decoded and cross-checked

| Offset | Light inf | Heavy inf | Archers | Light cav | Heavy cav | Field |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| `+0x18` | 4 | 2 | 4 | 6 | 5 | **Moves** |
| `+0x1A` | 15,000 | 6,000 | 3,500 | 7,000 | 2,500 | **Standard battalion size** |
| `+0x1C` | 7 | 0 | 25 | 9 | 0 | **Shots** (0 = melee-only) |
| `+0x1E` | 1 | 0 | 2 | 1 | 0 | **Range** |
| `+0x20` | 18 | 2 | 18 | 15 | 4 | unidentified |
| `+0x22` | 2 | 20 | 4 | 15 | 30 | **Recruit cost, initial** |
| `+0x24` | 1 | 2 | 1 | 3 | 4 | **Recruit cost, quarterly** |
| `+0x26` | 20 | 100 | 40 | 60 | 120 | unidentified |

Every bolded field is an exact match to independent evidence already in this project, not a guess:

- **Standard battalion size** matches *every* observed garrison/army unit troop count for that type across every save examined this session (15,000 light infantry, 6,000 heavy infantry, 3,500 archers, 7,000 light cavalry, 2,500 heavy cavalry — these exact numbers recur throughout `rome-city-recruitment-and-nations.md`, `city-units-army-transfer-and-mercenaries.md`, and every army roster since).
- **Shots**: light cavalry's value (9) matches the "1st Lancers Battalion... 9 shots" screenshot value from `battle-observation.md` exactly. Melee-only types (heavy infantry, heavy cavalry) read 0, consistent with never being observed shooting.
- **Range** correlates with shots (0 wherever shots is 0), and is exactly the field the decompiled `SHOOTS AT` code reads for the shooter's own type to decide whether a shot is "in range" and gets doubled — see `decompiled-combat-formula-structure.md`.
- **Recruit cost** (initial/quarterly): light cavalry's values (15, 3) and light infantry's quarterly value (1) are exactly what `decompiled-recruitment-cost-formula.md` had already *solved for* algebraically from two independent real save-diffs (1,400 light cavalry costing 105 initial/21 quarterly; a 15,000-troop transfer's +75 quarterly delta), without knowing the table's actual contents at the time. Finding the literal table values match the previously-solved-for values exactly, from a completely independent source (the DAT file rather than a save-diff), is about as strong a confirmation as this project produces.

`+0x20` and `+0x26` remain unidentified — plausible candidates include a combat-power/toughness stat and a base value used elsewhere, but neither is matched to an observation yet.

## What this does not establish

- `+0x20` and `+0x26`'s meanings.
- The full `[attackerType][targetType]` combat-effectiveness matrix used by melee (a *different* table from this one — this is a flat per-type array, the melee matrix is two-dimensional and still unlocated).
- Whether the mercenary cost table (a related but not-yet-confirmed-identical lookup used in `TRecruitMercs_RecruitMercUnit`) is this same table or a separate one.
- Whether this table's DAT-file offset (`0x1f2f0`) is a coincidence of this specific DAT build/version or has a principled derivation (e.g. a fixed distance from the end of the city table) — only one DAT file was checked.

## Reproduction

```python
data = open("Imperial Conquest 2.dat", "rb").read()
base = data.find(b"Light infantry")  # 0x1f2f0 in the checked file
for t in range(5):
    row = data[base + t*0x28 : base + t*0x28 + 0x28]
    # bytes 0x18-0x27 are the 8 stat words, little-endian
```

## Next checks

1. Decompile the `[attackerType][targetType]` combat matrix table to complete the melee formula's remaining constants.
2. Identify `+0x20` and `+0x26` against a controlled observation.
3. Confirm or refute whether the mercenary price lookup uses this same table.
4. If this offset formula is stable, consider a minimal `IC2.Data` parser for it (it's DAT-file data, consistent with this project's read-only file-parsing scope) — not added yet since only one file was checked.
