# Decompiled formulas: fleet cost/capacity, tax income, and mercenary hiring

The first real decompilation pass (see [decompilation-plan.md](../decompilation-plan.md)) recovered Delphi's own method-name tables from the full v1.01 EXE and imported them into Ghidra as real function symbols, then decompiled the `TBuildFleet`, `TChangeTax`, and `TRecruitMercs` form classes — the exact dialogs behind three mechanics already probed empirically from saves. Every formula below is checked against real numbers already in hand; none are guesses from variable names alone.

## Recovering Delphi's method-name tables (task 0)

Delphi 2 stores a per-class method table in the binary: a `Word Count` followed by `Count` entries of `{Word EntrySize; Pointer CodeAddress; Byte NameLen; Char Name[NameLen]}` (`EntrySize` includes its own 2 bytes, i.e. `EntrySize = 7 + NameLen`), immediately followed by the class name as the same length-prefixed string format. This was confirmed byte-for-byte against the seven addresses `battle-code-entry-points.md` had already found by hand (e.g. `StartBattle` at `0x00436fb4`), then used to scan the entire 1,166,336-byte EXE. Every plausible `{Count, entries...}` region that parsed completely (all entries structurally valid, all addresses inside the CODE section, all names printable ASCII) was accepted; **31 tables, 282 methods, every one matching a real class name** — all 30 `TPF0` form resources from `impconq2-initial-report.md` plus `TObject`'s base table. This is a general-purpose technique, not specific to battle code: it recovered `TChangeTax`, `TBuildFleet`, and `TRecruitMercs`'s methods along with everything else, none of which had known addresses before. The 282 symbols were imported into the Ghidra project as `ClassName_MethodName`.

## Fleet order: cost, capacity, and one unexplained number

`TBuildFleet_ChangeFleetSize` clamps the ship-count spinner to `[10, 100]` via two calls that read as `min`/`max` helpers — matching the "100 ships when combining fleets" error string from `impconq2-initial-report.md`, now also confirmed as the single-order cap, with 10 as the floor (i.e. **not** a value the user chose arbitrarily when testing — it's the dialog's minimum).

`TBuildFleet_PrintNumbers` computes four displayed values from the current ship count `n`:

| Expression | Value in decompiled code | Matches |
| --- | --- | --- |
| `n` | ship count, displayed as-is | — |
| `n * 10` | **cost in talents** | Exactly the 100-talent cost for the user's 10-ship order ([fleet-order-at-caere.md](fleet-order-at-caere.md)) |
| `n * 3` | unlabelled | Not yet matched to any observation — candidate quarterly upkeep per ship, unconfirmed |
| `n * 500` | **troop capacity** | Exactly the 500-troops/ship capacity the user reported |

Two of three known quantities land exactly on formulas now confirmed from real code, and `n * 500` also reappears independently in the mercenary-hiring capacity check below (see next section) — the same constant showing up twice from two different form classes is good corroboration it's a fixed, general fleet-capacity constant rather than coincidence.

`TBuildFleet_OK` builds the city name for its "The fleet will be built at ..." message by indexing a table with a **34-byte stride** — an independent, decompiled-code confirmation of `WorldPrefix.CityRecordLength = 34` in `IC2.Data`, found only by save-diffing until now.

## Tax income: fully confirmed, exact match

`TChangeTax_PrintNewNumbers` computes the dialog's "new income" value as:

```text
income = (nationTaxBase * newTaxPercent) / 100
```

where `nationTaxBase` is a per-nation field read at a fixed stride of `0x24a` words (**1,172 bytes** — exactly `SaveNationLayout.NationRecordLength` in `IC2.Data`, another independent confirmation from code). Solving this formula against Rome's own two data points from [rome-tax-increase-and-sidon-capture.md](rome-tax-increase-and-sidon-capture.md) gives the same base both times:

```text
366 = base * 15 / 100  ⇒  base = 2,440
488 = base * 20 / 100  ⇒  base = 2,440
```

Both resolve to **exactly 2,440**, with no rounding needed — as clean a confirmation as this project has produced. `nationTaxBase` itself is not yet identified as any specific known field (it isn't simply population or treasury; a good next check is comparing it against the "candidate population" estimate already computed in `IC2.Inspect`).

## Mercenary hiring: confirms the sentinel mechanic in code, and explains `Label`

`TRecruitMercs_RecruitMercUnit` operates on the same 12-byte-stride offer records already reverse-engineered from saves ([mercenary-pool-record.md](mercenary-pool-record.md)) — confirmed here independently, since the decompiled code indexes the offer array with stride `0xc` (12) matching `SaveMercenaryTable.RecordLength`. Three things fall directly out of this function:

1. **The empty-sentinel mechanic, confirmed in code, not just by save-diffing.** On a successful hire, the code sets the offer's troop-count field to `0xFFFF` (`*(undefined2 *)(&DAT_0049d0ac + offset) = 0xffff;`) — exactly the sentinel value found in the winter_1/winter_3 save pair.
2. **The 100,000-troop army cap is a hard `≥ 100,001` rejection**, tied directly to the literal error string `"An army can not contain more than 100,000 troops."` — confirms the constant from `impconq2-initial-report.md` at the code level.
3. **`Label` is an index into a name table, not a nation code** — explaining why it never matched the hiring/source nation's own `NationCatalog` code in either the Felsina (`label=11`) or the older Alexandria (`label=35`) case. The code builds the mercenary's displayed name via `&DAT_0049cc94 + Label * 0x14` (a 20-byte-stride table), then uses a *second* field from the same 12-byte record as an index into a 40-byte-stride price table to compute per-troop cost, and a third as a quality multiplier. The name table's runtime address didn't resolve to a stable file location in this pass (it may be populated at startup from the DAT file's own still-unmapped "peoples"/name region noted in `impconq2-initial-report.md` around file offset `0x18a5c`), so the exact label→name mapping (e.g. confirming `11 → "Gallic"`) remains open, but the *mechanism* — a separate ethnicity/culture catalog indexed by `Label`, exactly as `mercenary-pool-record.md` guessed — is now confirmed.
4. The fleet-transport capacity check for boarding mercenaries divides by `500` — the same per-ship troop capacity found independently in the `TBuildFleet` formulas above.

## What this does not establish

- The exact meaning of `nationTaxBase` (2,440 for Rome) as a named field.
- The `ShipCount * 3` fleet value.
- The literal mercenary label→name strings (the name table's DAT-file backing, not just its in-memory use).
- Mercenary cost's exact numeric formula (the shape — `troops * priceTable[type] / 1000 * qualityFactor` — is visible in the decompiled code but wasn't solved for concrete table values against the 51-quarterly Felsina hire).

## Reproduction

The Delphi RTTI scanner, Ghidra symbol importer, and address-based decompiler scripts are described in `decompilation-plan.md`. Ghidra project files and decompiled text stay outside this repository (`%LOCALAPPDATA%\ReTools`), matching every other original-asset constraint in this project.

## Next checks

1. Decompile `TPolitics_MakePeace` for the diplomatic reparation formula — the next item in the priority queue, with the Ptolemaic `-2,269` exact-match data point already in hand from `diplomatic-reparations-and-more-captures.md`.
2. Locate `nationTaxBase`'s source: compare it against `IC2.Inspect`'s candidate population estimate, city-tribute sums, or another per-nation SAV field, to name it properly.
3. Trace the mercenary price table (`&DAT_00478fd4`, 40-byte stride) and quality-multiplier field against the Felsina hire's 51-quarterly cost to solve the cost formula numerically.
4. Resolve the mercenary name table's DAT-file backing to confirm literal label→name strings.
