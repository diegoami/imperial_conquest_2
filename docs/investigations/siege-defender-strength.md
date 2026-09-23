# Siege defender strength (`FUN_0044A98C`), and the three field identities T02 shipped wrong

**Status: solved.** `FUN_0044A98C` computes a besieged city's defender strength as
`loyalty × 150 + finishedFortificationPercent × 250 + populationThousands × 200`, then applies two
further scalars. T02's shipped `Ruleset.Siege` attached the 150 and 250 weights to the wrong two
fields (fortification and loyalty, swapped) and carried the third field — population — as
`DefenderUnidentifiedFieldWeight`, because the report it was transcribed from
(`decompiled-city-capture-resolution.md`) recorded the three weights without ever decompiling the
function that owns them. This task (T31) is the correction, decompiling the function directly and
cross-checking every field identity against the game's own UI code.

Same tagging convention as `design-audit.md` / `game-design.md`: **[confirmed]** (direct RE evidence,
cited), **[derived]** (extrapolation), **[designed]** (new design, no RE evidence), **[open]** (not
established either way).

## Sources and method

- **Decompiled code:** `FUN_0044A98C` (`0x0044A98C`), the defender-strength function itself, and
  `TInformation_ShowCityDetails` (`0x0043BE5C`), the city-details panel, used as an independent
  cross-check on field identity because it prints its own UI labels next to each read — it cannot be
  wrong about which field is which the way a report reconstructing meaning from bare offsets can.
- **Why this task exists rather than re-reading the cited report more carefully:**
  `decompiled-city-capture-resolution.md` states the formula as
  `fortification × 150 + loyalty × 250 + <third field> × 200` and is explicit that it never decompiled
  `FUN_0044A98C` — `design-audit.md` §2.13 flags this in as many words: *"`FUN_0044A98C` (defender
  strength) was not decompiled this pass. **[open]**"*. T04's fixtures corpus transcribed that
  unverified line verbatim (`capture.siegeDefenderStrengthFormula`), and T02 named `SiegeRules`'
  fields from the corpus, so one report's guess propagated into shipped model field names with no
  intervening decompilation. Re-deriving the fix from the same report a second time would reproduce
  the same error; the fix has to come from the function itself.

## `FUN_0044A98C`, decompiled [confirmed]

```c
int FUN_0044a98c(short city)
{
  sVar6 = (&DAT_004795aa)[city*0x11];
  if (sVar6 < 0x65) { iVar4 = sVar6; } else { iVar4 = sVar6 % 100; }   // the dual-encoding decode
  iVar4 = (short)(&DAT_004795a6)[city*0x11] * 0x96      //  loyalty      × 150
        + iVar4                             * 0xfa      //  fortification × 250  (decoded)
        + (short)(&DAT_004795ac)[city*0x11] * 200;      //  population   × 200
  if ((FUN_0044b8d0(city) != 0) && (0x3b < (short)(&DAT_004795a6)[city*0x11]))
      iVar4 = (iVar4 * 5) / 3;                          //  capital AND loyalty > 59
  if ((&DAT_004795a2)[city*0x11] != (&DAT_004795a4)[city*0x11])
      iVar4 = (iVar4 << 2) / 5;                         //  owner != allegiance → × 4/5
  /* … then, over the owner's 40 recruitment slots, += troops(+0x2e8) / 2 for slots targeting this city */
}
```

`0x96` = 150, `0xfa` = 250 — the three weights T02 already shipped correctly as *numbers*. What T02
got wrong is which of `DAT_004795a6` (loyalty), `DAT_004795aa` (fortification, dual-encoded) and
`DAT_004795ac` (population) each weight multiplies.

## Field identities, cross-checked against the city-details panel [confirmed]

`TInformation_ShowCityDetails` (`0x0043BE5C`) reads the same four `DAT_004795xx` fields this function
does and prints its own labels next to each one — the identification is not inferred from magnitude,
it is read off the panel's own strings:

| Panel label | Field read | Matches `FUN_0044A98C` |
| --- | --- | --- |
| `"Loyalty -"` | `loyaltyNames[DAT_004795a6 / 10]` | the `/ 10` tiering is the same divisor `LoyaltyRules.TierDivisor` already ships |
| `"Fortification -"` | `DAT_004795aa`, guarded `< 0x65` else `% 100`, appending `"  (under construction)"` on the `% 100` branch | the identical guard `FUN_0044A98C` applies before weighting it × 250 |
| `"Population -"` | `DAT_004795ac × 1000`, and `DAT_004795ac × 100 / DAT_004795ae` as `"% of maximum"` | the same `DAT_004795ac`, weighted × 200 in `FUN_0044A98C` |
| `"Controlled by -"` | `DAT_004795a2` | the owner field `FUN_0044A98C` compares against allegiance |
| `"Allegiance to -"` | `DAT_004795a4` | the allegiance field `FUN_0044A98C` compares owner against |
| `"  (capital of …)"` | gated on `FUN_0044B8D0(city)` | the same function gates the `× 5/3` branch |

`FortificationCode.AfterSiegeAttempt` — merged in T02, unrelated to this defect — already cites
`FUN_0044B27C`'s `if (fort > 100) fort = fort % 100` on the same word, corroborating the fortification
identity independently of this task's own decompilation.

## What T02 shipped, and the correction

| Ruleset field | T02 (wrong) | T31 (corrected) | Evidence |
| --- | ---: | ---: | --- |
| `DefenderLoyaltyWeight` | 250 | **150** | `FUN_0044A98C`: `DAT_004795a6 (loyalty) * 0x96 (150)` |
| `DefenderFortificationWeight` | 150 | **250** | `FUN_0044A98C`: decoded `DAT_004795aa (fortification) * 0xfa (250)` |
| `DefenderUnidentifiedFieldWeight` → `DefenderPopulationWeight` | 200, field unidentified | **200, field = population in thousands** | `FUN_0044A98C`: `DAT_004795ac (population) * 200`; independently, `TInformation_ShowCityDetails`'s `"Population -"` label on the same `DAT_004795ac` |

Only field *identity* changed. All three weight magnitudes (150 / 250 / 200) are unchanged from T02 —
the defect was never in the numbers, only in which city field each one multiplies. The fortification
term is decoded through the guarded rule `FortificationCode.FinishedPercent` already implements
(`code > MaxPercent ? code % radix : code`) — never the raw stored word and never an unguarded
`% 100`. The raw word is wrong for a city with a fortification order in progress: it stores
`finishedPercent + pendingPoints × radix` (e.g. 250 for 50% finished with an order pending), so reading
it raw overstates the finished amount by a full order. An unguarded `% 100` is wrong at exactly one
point instead: a fully-finished city stores 100, and `100 % 100 = 0` would silently turn a finished
100% fortification into 0%. `FortificationCode.cs` itself needed no change: it was already correct, and
this task's corrected provenance now points callers at it explicitly.

## Two further defects surfaced here, since fixed by T33

> **Update (2026-09-18):** both were filed and are now fixed. T33 merged as `4d4ba20`, renaming the fields as described below and closing [#46](https://github.com/diegoami/imperial_conquest_2/issues/46) and [#47](https://github.com/diegoami/imperial_conquest_2/issues/47). The names below are the pre-fix ones; the shipped fields are now `HighLoyaltyThreshold` / `BonusNumerator` / `BonusDenominator` and `DefenderNonAllegiantNumerator` / `DefenderNonAllegiantDenominator`.

Reading `FUN_0044A98C` end to end surfaces two more real defects in the currently-shipped
`SiegeRules`, both filed as bugs ([build-process.md §4.6](../build-process.md#46-bugs-and-follow-ups)) rather than folded into this
task's scope, per the project's standing rule that a task fixes only what is in its Owns list:

- **[issue #46](https://github.com/diegoami/imperial_conquest_2/issues/46)** —
  `HighFortificationThreshold` / `HighFortificationBonusNumerator` / `HighFortificationBonusDenominator`
  are misnamed: the branch they gate (`iVar4 = (iVar4 * 5) / 3`) tests **loyalty `> 59`**
  (`0x3b < (short)(&DAT_004795a6)[city*0x11]`), not fortification. The capital guard on that same
  branch, previously recorded as an unrecovered condition, is now identifiable as
  `FUN_0044B8D0(city)` — "is this city its controlling nation's capital" — confirmed independently by
  `TInformation_ShowCityDetails`'s `"  (capital of …)"` panel branch, gated on the same function.
- **[issue #47](https://github.com/diegoami/imperial_conquest_2/issues/47)** —
  `DefenderOwnerNotAllegiancePenaltyPercent` (20, read as "subtract 20%") does not match the function's
  actual operation, `iVar4 = (iVar4 << 2) / 5` — a `× 4/5` that truncates differently than a
  20%-subtraction reading at some input values, because `(x << 2) / 5` and `x - x/5` round down at
  different points for the same `x`.

## The owner≠allegiance / attacker==allegiance question, now resolved

`design-audit.md` §2.13 left open whether `decompiled-city-capture-resolution.md`'s
"−20% defender penalty when owner ≠ allegiance" and `FUN_0044B27C`'s "×9/10 (−10%) when the attacking
nation equals the city's allegiance" were the same adjustment described twice, or two separate ones.
They are **two separate adjustments in two different functions**, both real, both applying:

- `FUN_0044A98C` (this function, defender strength): `if (owner != allegiance) strength = (strength << 2) / 5`.
- `FUN_0044B27C` (the siege entry point, outside this function — T17's `AttackerIsAllegianceDefenderReductionPercent`,
  untouched by this task): a separate ×9/10 reduction gated on the *attacking* nation equalling the
  city's allegiance.

This closes `design-audit.md` §2.13's `[open]` tag on `FUN_0044A98C`, and settles the "known-open item
to record, not resolve" T17 carries for the same question — both are doc edits on `main` for the
orchestrator, escalated rather than made directly by this task (see this PR's body).

## The siege entry point around this function, at instruction level [confirmed]

*Added 2026-09-23 from research `3f6ca09`*
([`decompiled-defection-and-siege-attrition.md` §"`FUN_0044b27c`, instruction by instruction"](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-defection-and-siege-attrition.md#fun_0044b27c-instruction-by-instruction-2026-09-23),
a machine-code listing rather than the pseudocode dump). It uses this document's field table: city
`+0x12` owner, `+0x14` allegiance, `+0x16` loyalty, `+0x1a` fortification, `+0x1c` population and
`+0x1e` maximum population.

- **The `× 9/10` compares the army's owner (army `+4`) with the city's allegiance, `+0x14`**
  (`0x0044B2CD`–`B2E9`: `CMP AX,[EBX+0x14]`), then computes `def × 9 / 10`, multiplying before it
  divides. It is applied after `FUN_0044A98C` returns, and so after the garrison addend, which
  confirms the order weighted sum → × 5/3 → × 4/5 → + garrison → × 9/10.
- **Everything downstream uses the reduced `def`**: the erosion of loyalty, fortification and
  population (`FUN_0044B230`), the attacker's casualty ratio `max(1, min(15, def × 6 / atk))`, and the
  outcome test `atk > def`, which gives ties to the defender.
- **The fortification strip does not change `def`.** The siege discards an in-progress order
  (`fort > 100 → fort % 100`, `0x0044B2A4`) before it calls this function, and this function's own
  decode at `0x0044A9A7` applies the same guard.
- **The defender's troops take no casualties.** `FUN_0044B27C` writes no recruitment slot (nation
  `+0x2E4`) and no army other than the attacker. The garrison addend above is read and never
  written. On the defender's side a siege changes only the three eroded fields, the population floor
  (`maxPopulation / 6 + 1`) and the fortification strip.
- **Neither strength function calls `Random`.**

The merged code applies the `× 9/10` exactly this way (`InstantBattleResolver.ResolveSiege`). It does
not apply the city steps ([#293](https://github.com/diegoami/imperial_conquest_2/issues/293)) or the
siege's own casualty ratio ([#290](https://github.com/diegoami/imperial_conquest_2/issues/290)).

## One unrelated defect found in the same file while it was open

While decompiling `FUN_0044A930`'s call graph to confirm the siege-strength identities above, the
`combat.naval._provenance.carriedArmyPowerDivisor` entry in `data/rulesets/toy-ruleset.json` was found
to misdescribe its own citation: it reads *"a carried army adds armyPower / 50"*, but `FUN_0044AA54`
(naval battle resolution) calls `FUN_0044A930` — **siege** strength — not `FUN_0044A8CC`'s `armyPower`.
A carried army adds *siege* strength / 50, not army power / 50. The value (50) is unchanged; only the
provenance text was wrong. Corrected in the same commit, per this task's Done-when line 5.
