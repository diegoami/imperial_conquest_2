# Decompiled recruitment cost formula, and a dead end on diplomacy

Continuing the decompilation pass from [decompiled-fleet-tax-and-mercenary-formulas.md](decompiled-fleet-tax-and-mercenary-formulas.md), using the same recovered Delphi symbols.

## `TPolitics_MakePeace`: not where the reparation formula lives

Decompiling `TPolitics_MakePeace` (`0x00452d94`), the natural next target given the exact `-2,269` Ptolemaic reparation data point from [diplomatic-reparations-and-more-captures.md](diplomatic-reparations-and-more-captures.md), turned out to be a dead end for that purpose: the function only checks a per-nation diplomatic-state matrix (rejects with "*[Nation] does not want to make peace at this time.*" if the target isn't at war, i.e. state `3`) and clears a pending-request field. It contains no treasury arithmetic at all. This makes sense in hindsight — the observed reparation was an AI-to-AI event, not something the player's own "propose peace" dialog would compute. The actual reparation calculation is presumably inside `ComputerGeneral` or another AI-decision routine that has no published (Delphi RTTI) name, so it won't turn up by name the way `TPolitics`'s own methods did. Left open rather than chased further into unnamed code this pass.

## Recruitment cost: confirmed, and independently matches two different prior reports

`TArmyRecruits_PrintNumbers` (`0x00454c10`) computes the recruit dialog's displayed costs as:

```text
initialCost   = (troopSize / 200) * priceTable[unitType]           // DAT_00478fd2, stride 0x28 (40) per type
quarterlyCost = (troopSize / 200) * quarterlyPriceTable[unitType]  // DAT_00478fd4, same stride
```

`DAT_00478fd4` is the *same* per-type quarterly-price table `TRecruitMercs_RecruitMercUnit` reads for mercenary cost, confirming it's a shared, general "quarterly upkeep per unit type" constant table rather than something specific to either mechanic.

This solves cleanly against two unrelated empirical data points from two different reports:

| Source | Troops | Type | Observed cost | Solved table value |
| --- | ---: | --- | --- | --- |
| [menu-and-toolbar-inventory.md](menu-and-toolbar-inventory.md) | 1,400 | light cavalry | initial 105, quarterly 21 | `1400/200=7`; `105/7=15` initial, `21/7=3` quarterly |
| [city-units-army-transfer-and-mercenaries.md](city-units-army-transfer-and-mercenaries.md) | 15,000 | light infantry | quarterly cost rose exactly **+75** | `15000/200=75`; `75/75=1` quarterly |

Both resolve to small, clean whole-number table entries (light cavalry: 15 initial/3 quarterly per 200 troops; light infantry: 1 quarterly per 200 troops), with no rounding needed in either case — the same standard of exact-match confirmation as the tax and fleet formulas.

`TArmyRecruits_NewUnitType` sets the dialog's default troop-count spinner to `baseTable[unitType] / 5` from a separate table (`DAT_00478fca`, stride `0x14`/20) — not yet matched to an observation.

## Mobilization and new-recruit creation: read, not fully formalized

`TArmyRecruits_MobilizeUnits` delegates the actual garrison→army transfer to an unnamed helper (`FUN_0044a4e0`), gated by a per-slot check requiring a state-like field `> 15` before a unit can be mobilized — plausibly connected to the city-unit `StateCode` values already observed (a freshly recruited unit reads `24`, comfortably above this threshold; whether a *lower* state code exists and is what this gate excludes is untested). `TArmyRecruits_RecruitUnit` confirms mobilization rate is capped at exactly 100% (`"Your mobilisation rate is already 100%."`) and scans a 40-entry array for an empty slot when creating a new recruit order — consistent with, but not offset-for-offset verified against, `SaveRecruitmentTable`'s known 40-slot structure. Not pursued further this pass since it requires decompiling an unnamed function.

## What this does not establish

- The diplomatic reparation formula (needs tracing from `ComputerGeneral` or other unnamed AI code, not from `TPolitics`).
- The exact in-memory field layout backing `MobilizeUnits`'/`RecruitUnit`'s offsets against the on-disk `SaveRecruitmentTable` offsets.
- The `NewUnitType` default-troop-count table.

## Reproduction

Same toolchain as `decompiled-fleet-tax-and-mercenary-formulas.md`; targeted addresses in this pass: `TPolitics_MakePeace` (`0x00452d94`) and all ten `TArmyRecruits` methods (`0x0045489c`–`0x004555a0`, listed in `delphi_symbols.tsv`).

## Next checks

1. Find the diplomatic reparation formula by tracing calls into `ComputerGeneral` (`0x00437ab8`) or searching for treasury-field writes near war/peace-state transitions, rather than by class name.
2. Decompile `FUN_0044a4e0` to see the exact garrison-to-army transfer and the `StateCode > 15` gate's meaning.
3. Confirm `NewUnitType`'s default-troop-count table against an observed recruit dialog default value.
