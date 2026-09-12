# A cascading defection mechanic, and where siege losses actually come from

Following up on `decompiled-city-capture-resolution.md`'s open item: `FUN_0044ba1c` turned out not to be a population/fortification-loss formula, but something more interesting.

## `FUN_0044ba1c`: cascading defection after a capture

Called at the end of the forced-capture transfer, this loops **every other city** and, for each one that shared the *same previous owner* as the city just captured:

```text
if other_city.owner == just_captured_city's_old_owner and other_city != just_captured_city:
    if not already contested:
        distance = grid_distance(other_city, attacking_army_position)
        if distance < 10:
            otherDefense = defender_strength(other_city)
            if other_city.allegiance == new_owner: otherDefense /= 3   // rebellious sympathy weakens it further
            if new_owner.unity < 650 and otherDefense < attacker_strength and other_city.loyalty < 65:
                FUN_0044bed8(other_city, new_owner)   // the city defects, no siege needed
```

A single successful capture can cause **nearby, weakly-defended, low-loyalty cities of the same defeated nation to defect automatically** — no army or battle involved for the secondary cities. This is very plausibly the mechanism behind every "X defects to Y" news event seen throughout this project's saves (Modena, Taurasia), which previously had no known trigger condition.

## `FUN_0044bed8`: the defection routine itself — confirms Modena's finding exactly

Structurally similar to the forced-capture transfer (`FUN_0044bb18`), but with real differences:

- **Never writes to population or fortification anywhere.** This is an exact, code-level confirmation of the Modena defection in `mobilization-movement-and-city-capture-modes.md`, where population and fortification were completely untouched while only owner and loyalty changed.
- Unity change is **+3/−20** (vs. +9/−15 for forced capture) — losing a city to defection costs the losing nation *more* unity than losing it in a straight fight.
- Loyalty, if the city ends up with a non-allegiant owner, is pulled toward a floor of **65** (vs. 40 for forced capture) — defection is gentler on loyalty than conquest, consistent with it being a "voluntary" switch.
- The garrison at that city is still cleared (same as forced capture).
- **New mechanic found: nation elimination.** If the losing nation's city count reaches 0 after this defection, the nation is disabled (`TPremierForm_DisableNation`), its armies are processed via another routine, and its diplomatic/mercenary state is cleaned up — a previously unknown "last city lost" cascade, not decompiled in depth this pass.

## Where does population/fortification loss actually come from, then?

Not from either transfer routine. Back in the siege function `FUN_0044b27c` (from `decompiled-city-capture-resolution.md`), two calls happen **unconditionally, on every attack attempt — win or lose**:

- **`FUN_0044ae20(armyIdx, ratio)`**: applies a small random-percentage troop loss to *every unit in the attacking army* (`troops -= troops / (Random(15) + 105) * ratio`), plus a check that can trigger a unit-state change if ammunition/readiness drops below a threshold from the unit-type table. This is the **attacking army's own casualties from attempting a siege**, separate from anything happening to the city.
- **`FUN_0044b230`**, called three times on three different city-stat fields, is a smoothing/decay helper: it nudges a field toward a target value derived from the current defense calculation, rather than setting it directly. Because this runs on *every* attack attempt regardless of success, it's the most likely source of the population/fortification decline observed in every successful capture in this project's data — but which of the three fields is fortification vs. population vs. something else, and the exact numeric target/rate, weren't pinned down this pass.

**What this means for the existing empirical readings:** the population/fortification drop seen at the moment of a successful capture may be the *accumulated* effect of however many attack attempts preceded the win, not a one-time "capture penalty" applied at the transfer step — a materially different picture than assumed in earlier reports, and worth keeping in mind if a future controlled experiment tries to isolate a single-attack population-loss formula.

## What this does not establish

- Which of the three `FUN_0044b230`-adjusted fields is fortification, which (if either) is population, and the exact smoothing formula's target/rate.
- The nation-elimination cascade's full effects (armies, diplomacy, mercenaries).
- Whether a *failed* capture attempt (city successfully defends) still measurably erodes its fortification via this same mechanism — no controlled save pair isolating a single failed attack exists yet to check.

## Reproduction

Both functions were already present in the whole-application-range dump from `decompiled-city-capture-resolution.md` (`all_app_functions.txt`); no new Ghidra invocation was needed this pass, just further reading of already-decompiled output.

## Next checks

1. A controlled save pair around a single *failed* capture attempt (no other activity that turn) would test whether the smoothing mechanism erodes a defended city's fortification even without a successful capture.
2. Identify the three `DAT_004795a6/aa/ac` fields definitively against known SAV city-record offsets, to confirm which is fortification.
3. Decompile the nation-elimination cascade (`FUN_0044ab90`, `FUN_0044ad38`, `TPremierForm_DisableNation`) if a save ever shows a nation actually being eliminated.
