# City capture, decompiled: the siege-outcome formula and the ownership-transfer routine

The best result of this decompilation pass. Two string searches (the DAT filename, `"falls to"`) both showed **zero references** via Ghidra's own reference analysis — a genuine tooling gap, not a dead end in the data. The workaround: decompile every function in the application's address range (`0x401000`–`0x460000`, 1,881 functions) to one text file and grep the *decompiled output* for string literals, since Ghidra's decompiler inlines a string literal operand into the pseudocode even when its reference-tracking database never recorded a formal xref to it. This found the exact function containing `"falls to"` immediately, and following its calls reached the complete siege-resolution and city-ownership-transfer logic.

## Siege outcome: `FUN_0044b27c(armyIdx, cityIdx)`

```text
attackerStrength = FUN_0044a930(armyIdx)   // besieging army's strength
defenderStrength = FUN_0044a98c(cityIdx)   // city's defensive strength
if defenderStrength < attackerStrength:
    "{CityName}   ({OldOwner})  falls to {NewOwner}."
    → FUN_0044bb18(cityIdx, armyIdx)  // the actual transfer
else:
    "{AttackerNation} fails to capture {CityName}   ({DefenderNation})."
```

### Attacker strength (`FUN_0044a930`)

Sums a per-unit contribution across the army's 20 unit slots (independently reconfirming the 20-units/army cap again), **tripling the contribution of one particular unit type** (type check `== 2`, i.e. archers), then scales by a per-army factor. Archers getting a 3× siege-strength bonus is a sensible, specific game-design fact this project didn't have before.

### Defender strength (`FUN_0044a98c`)

```text
strength = fortification × 150 + loyalty × 250 + <third field> × 200
if <condition> and fortification > 59: strength = strength × 5 / 3
if owner != allegiance: strength = strength × 4 / 5   // −20% if held by a non-allegiant power
strength += (garrison troops assigned to this city) / 2
```

Fortification and loyalty both directly raise a city's defense, as expected. The `owner != allegiance` penalty is new and important: **a captured city is measurably easier to attack again** while its population hasn't accepted the new ruler — a real mechanical consequence of the owner/allegiance split this project has tracked since `rome-tax-increase-and-sidon-capture.md`, now shown to matter beyond just the displayed fields.

## Ownership transfer: `FUN_0044bb18(cityIdx, armyIdx)` — confirms almost everything found by save-diffing

```text
newOwner = attacking army's nation;  oldOwner = city's current owner
city.owner = newOwner                                    // the actual OwnerCode write

newOwner.unity     += 9,  clamped to a max (0x3de = 990)
newOwner.wealth     += cityFortification × 3000
newOwner.cityCount  += 1
newOwner.taxBase    += (per-army value) × 4               // the exact field from decompiled-fleet-tax-and-mercenary-formulas.md's "nationTaxBase = 2,440" finding

oldOwner.unity     -= 15   (bigger penalty than the winner's gain — asymmetric)
oldOwner.wealth     -= cityFortification × 3000
oldOwner.cityCount  -= 1
oldOwner.taxBase    -= (per-army value) × 4

for each of oldOwner's 40 recruitment slots:
    if that slot's city == this city: clear it            // old owner's garrison here is wiped

FUN_0044ba1c(cityIdx, armyIdx)   // not yet decompiled — the likely population/fortification damage step

city.owner = newOwner   // written again (redundant with the FUN_0044b8f4 call above, or a second field)

if city.allegiance == newOwner:
    loyalty → pulled toward 90 (0x5a)
else:
    loyalty → pulled toward a floor of 40 (0x28)
```

This is a direct, code-level explanation for findings from three separate save-diffing reports:

- **`nationTaxBase` (the exact field solved to `2,440` for Rome in `decompiled-fleet-tax-and-mercenary-formulas.md`) is incremented/decremented by captures** — ties the tax-income formula and the capture mechanic together as parts of one economic system, not two unrelated things.
- **The loyalty rule exactly matches the Taurasia same-turn defect-then-reconquer finding** from `field-recruitment-uniform-attrition-and-fleet-drift.md`: when a city ends up under its own allegiant nation, loyalty is pulled toward 90 (Taurasia's observed value after Gaul recaptured it); when captured by a non-allegiant nation, loyalty is pulled toward a 40 floor (matching every forced-capture example's observed loyalty in the 40–48 range).
- **Garrison clearing** explains why every captured city has always shown zero city-unit troops in this project's saves — it's not incidental, the old owner's garrison at that specific city is explicitly wiped on transfer.
- **`CityCount` moving by exactly ±1** matches every `NationRecord.CityCount` change observed across every capture this session.

## A related, distinct mechanic noticed but not pursued: nation collapse

`FUN_0044c528` (reached from `"conquer"` and called conditionally from `FUN_0044bb18` when a losing nation's unity/city-count drop below thresholds) loops **all 334 cities** and transfers every remaining city belonging to one nation to another in one pass — evidently a "nation eliminated, annex everything" cascade distinct from single-city capture. Not decompiled in depth this pass.

## What this does not establish

- `FUN_0044ba1c` — the step most likely responsible for the population/fortification percentage drops observed in every capture — is not yet decompiled. This report explains ownership, economy, and loyalty, but not yet the exact population/fortification-loss formula.
- The exact meaning of the "third field" in defender strength, and the boolean condition gating the ×5/3 fortification bonus.
- `FUN_0044c528`'s (nation-collapse) full trigger conditions and effects.
- Why Ghidra's reference analysis misses these string usages in the first place — worked around, not fixed.

## Reproduction

```text
# One-time: decompile the whole application range to one file, then grep it.
analyzeHeadless <project> IC2 -process "Imperial Conquest 2.exe" -noanalysis -scriptPath <scripts>
  -postScript ExportAllInRange.java all_app_functions.txt 00401000 00460000
```
Then search the output text for a literal string (e.g. `"falls to"`) to find its containing function, even when `FindXrefs.java`/`getReferencesTo()` finds nothing.

## Next checks

1. Decompile `FUN_0044ba1c` for the population/fortification loss formula — the last major open piece of the capture mechanic.
2. Cross-check the `unity +9/−15` and `wealth ±fortification×3000` formulas against a controlled same-turn save pair (a single capture, no other activity) to verify exactly, the way the tax and fleet formulas were verified.
3. Investigate why Ghidra's `ReferenceManager` doesn't find these string xrefs, since fixing it would make every future string-based search in this project faster than the whole-range-dump workaround.
