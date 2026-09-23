# Composition-aware auto-resolve: five candidate models, and how to judge them

**Status: specification, not a decision.** This document specifies five battle-resolution models
precisely enough for T59 ([#250](https://github.com/diegoami/imperial_conquest_2/issues/250),
[`docs/tasks/T59.md`](../tasks/T59.md)) to implement and measure, and it defines the metric suite
T59 measures them with. It **does not pick a winner and does not rank the candidates.** T59 measures
them, and the user decides from T59's scorecard. Task T58, [#251](https://github.com/diegoami/imperial_conquest_2/issues/251).

> **Corrected 2026-09-23** by the research pass for [#288](https://github.com/diegoami/imperial_conquest_2/issues/288)
> (research commits `1762c84`, `54b85d0`, `eb1c886`). Four facts changed, and each is corrected where it
> is stated: K27 is settled (+3 to a computer-controlled side only); the initial tactical-morale clamp
> D08 is `[60, 90]`; §5's cascade is **one level deep**, not recursive; and the original's instant
> path deletes small units after its casualties, which the merged C1 does not (§6.1,
> [#289](https://github.com/diegoami/imperial_conquest_2/issues/289)). The heavy-cavalry wording in
> §2.3, §7 and §9.2 is narrowed to what the corrected reports support. The candidates are not
> redesigned. Where a placeholder now departs from a settled fact, the text says so, and the choice is
> left to the user.
>
> **Corrected again 2026-09-23** by the research pass for [#290](https://github.com/diegoami/imperial_conquest_2/issues/290)
> (research commit `3f6ca09`). No candidate changes, because every candidate replaces only the
> field battle. Three statements are corrected where they appear. K03's ratio belongs to the field call
> site only: sieges and naval battles pass `FUN_0044AE20` different ratios (§1). `FUN_0044AE20` draws
> exactly 20 `Random(15)` per call, where the merged `BattleCasualties.Apply` draws once per unit-list
> entry (§6.1 "Draws"). The merged siege and naval paths depart from the original (§1).
>
> **Corrected again 2026-09-23** after T63 ([#295](https://github.com/diegoami/imperial_conquest_2/issues/295),
> 562e608) merged. The merged resolver now ports the small-unit deletion pass at every call site
> (#289), and the siege/naval ratios, the naval `+ 1` and the siege city erosion (#290, #292, #293).
> Every statement above, and in §1 and §6.1, saying the merged code omits or departs from these is
> corrected where it appears. The departures T63 decided on purpose remain: one draw per unit instead
> of the original's fixed 20 per slot (Decision 6), and a besieger emptied by its own casualties does
> not capture (Decision 3).

The candidates are numbered **C1–C5 in the order T58's Done-when 1 lists them**: baseline, the
original's tactical model run headless, type-weighted instant, round-based, and morale-and-retreat.
That is a neutral key. It is not an order of merit, and no table in this document has a verdict
column.

Same tagging convention as [`design-audit.md`](../design-audit.md) and
[`game-design.md`](../game-design.md): **[confirmed]** means direct RE evidence, cited.
**[derived]** means an extrapolation from confirmed evidence. **[designed]** means new design with no
RE evidence. **[open]** means not established either way. Per
[design-audit.md §4.5](../design-audit.md#45-one-structural-note-on-the-harness), *"a `[designed]` tag
is only valid if the document says what was searched and came up empty"*. Every `[designed]` entry
here therefore names its search, either inline or through §11. Where a report's claim is itself
`[derived]`, it keeps that label here and is not upgraded.

**Reading guide for T59.** §4 is the constants register. Every number any candidate uses appears
there with its source or tag, and the candidate sections refer to register IDs (`K..`, `D..`) rather
than restating values. §5 is the confirmed unit-level break trigger. §6 is the five candidates. §7 is
what each one predicts for the discriminating observation. §8 is the metric suite with its bands.
§9 is the smoke test.

## Contents

1. [What this document is not deciding](#1-what-this-document-is-not-deciding)
2. [Premise checks: four places where the evidence disagrees with the task entry](#2-premise-checks-four-places-where-the-evidence-disagrees-with-the-task-entry)
3. [The common seam](#3-the-common-seam)
4. [Constants register](#4-constants-register)
5. [The unit-level break trigger, `FUN_00438fb0` [confirmed]](#5-the-unit-level-break-trigger-fun_00438fb0-confirmed)
6. [The candidates](#6-the-candidates)
7. [The discriminating observation, candidate by candidate](#7-the-discriminating-observation-candidate-by-candidate)
8. [The metric suite](#8-the-metric-suite)
9. [The smoke test: the observed tactical battles](#9-the-smoke-test-the-observed-tactical-battles)
10. [Open items that would convert `[designed]` to `[confirmed]`](#10-open-items-that-would-convert-designed-to-confirmed)
11. [What was searched](#11-what-was-searched)

---

## 1. What this document is not deciding

- **Which candidate wins, or how the candidates rank.** No section recommends one. The pass bands in
  §8 were set before any candidate was measured, and they are the only judgement this document
  makes. Where §6 or §8 states a result in advance, the result follows from the model's own
  arithmetic (for example, "a deterministic winner rule has an upset rate of exactly 0"). It is not
  an estimate of how well the model plays.
- **Whether the game changes at all.** Nothing here touches `InstantBattleResolver`, `BattleCasualties`,
  `ScatterPlacement`, a shipped ruleset, or a `Ruleset` flag. T59 implements behind a measurement seam
  and wires nothing into the game ([T59](../tasks/T59.md) Hazards). Adopting any candidate is a
  later task, gated by the user.
- **The tactical battle screen.** It **remains a v2 item**, per the user's decision of 2026-09-20
  recorded in T58's entry. Candidate C2 runs the original's tactical *model* headless. It is not a
  screen and it does not argue for one. Nothing here should be read as scheduling a battle screen.
- **How a human is offered a withdrawal.** C5's army-level rule is specified as one symmetric policy
  that both seats follow, because T59 measures AI against AI. Whether a human seat is instead
  *offered* the choice is a presentation question for whichever task adopts C5, if any.
- **Naval battles and sieges.** Every candidate replaces only the **field-battle** resolution
  (`ResolveField`). Naval (`FUN_0044B5D0`) and siege (`FUN_0044B27C`) stay as merged. "As merged" is
  not "as the original", though. The original's siege charges the attacker
  `FUN_0044AE20(army, max(1, min(15, def × 6 / atk)))` on every attempt and erodes the city's loyalty,
  fortification and population. Its naval winner's carried army takes ratio `d = r² / 100` and, when
  `d > 70`, loses `unitCount × d / 250 + 1` units **[confirmed at instruction level:
  [`siege-attrition`][siege-attrition] §"`FUN_0044b27c`, instruction by instruction" and
  [`decompiled-diplomacy-peace-terms-and-instant-battles.md`][instant] §"`FUN_0044B5D0` and
  `FUN_0044B4F8`, instruction by instruction", research `3f6ca09`]**. The merged paths now use their
  own ratios at both call sites, include the naval `+ 1`, and erode the city (T63, #295, 562e608).
  No metric here scores a siege or a naval battle. COST's soak baseline (§8.6) runs them, but it only
  times them. So this changes no candidate.
- **Final constant values for adoption.** Every `[designed]` constant here exists so that T59 can
  measure without inventing anything. None of them is a tuned value. T59 must not retune them
  ([T59](../tasks/T59.md) Hazards), and adopting a candidate would reopen them.
- **The open RE items in §10.** This task produced no new evidence. Where the evidence runs out,
  the gap is marked `[open]` and bridged by a `[designed]` placeholder.

## 2. Premise checks: four places where the evidence disagrees with the task entry

These do not change any Done-when line, and every line is still met below. Each one does change how
a reader should interpret a result, so each is stated here once and referenced where it matters.

### 2.1 The baseline is flat in casualties, not in who wins

The entry says *"bringing cavalry is worth exactly what bringing archers is worth"*. That holds for
**casualty shape**, which is what
[`instant-resolver-cannot-reproduce-a-tactical-battle.md`][instant-cannot] measured: every type takes
the same rate, to within about one point. It does **not** hold for **who wins**. The merged
`armyPower` is `(Σ combatPowerWeight[type] × troops / 100) / 80 × armyMorale`, and the weights are
the confirmed unit-type table `+0x26` column (K01): light inf 20, heavy inf 100, archers 40, light
cav 60, heavy cav 120. A heavy-cavalry trooper is worth six light infantrymen and three archers in
the baseline's winner rule.

The baseline is therefore **composition-sensitive but not matchup-sensitive**. Each type has a fixed
price, with no counters. This matters for Done-when 5's first metric. Holding **total troops** fixed
and varying the mix, as Done-when 5 words it, gives the baseline a **large** win-rate spread, not
"about zero". §8.1 keeps that metric as worded (CS-T) and adds a power-matched companion (CS-P) on
which the baseline scores exactly zero by construction. CS-P is the metric that tests what the
user's complaint is about.

### 2.2 The headless-tactical candidate needs design as well as transcription

The entry calls running the original's tactical battle headless *"a candidate needing no new design
and no new evidence"*. That is true of the **arithmetic**. The melee exchange, the shooting
exchange, the matrix, the vulnerability weights, the caps, the tactical morale rule and the rout
check are all decompiled to the constant (§4, §5). It is **not** true of the **driver** that decides
which unit fights which, when, and from where:

- The tactical AI's dispatch, `FUN_00439ce8`, has two branches. One (`FUN_004381a4`) turned out to
  be only unit placement. The other (`FUN_0043a31c`) has never been traced, and the source leaves
  open whether it is *"an alternate AI mode or the human-move-confirmation path"*
  ([`decompiled-combat-formula-structure.md`][formula] §"The AI dispatch chain" and Next checks 3).
  Either way, no report contains the AI's move or target rules.
- No report contains target selection, movement rules on the grid, the maximum shooting distance, or
  the placement type-order lookup table.
- The exact clamp on initial tactical morale was reported only as *"upper bounds ≈ 90 then 60"*.
  It has since been settled as `[60, 90]`
  ([`battle-quality-promotion-and-morale-array-decompiled.md`][morale-array] §"The morale formula",
  research `1762c84`), so this gap is closed (D08).

This is not a new observation. [`game-design.md` §Combat](../game-design.md) already records that
an earlier draft proposed *"running the tactical exchange math headlessly with an invented 'pairing'
rule"*, which is C2's shape. It set that draft aside on the grounds that the original's placement
pairing does not translate to an instant model, and the design audit (Q1) instead adopted the
original's own instant resolver as C1. That the draft needed an invented pairing rule is the same gap
described here.

C2 is specified below with each of these gaps as an explicit placeholder. The driver and its termination are D01–D06
and D09–D11, all with `[designed]` placeholders. The morale rule's remaining gap is D07 (`[designed]`). The clamp D08 is now `[confirmed]`.
Each names what was searched. So C2 is **the original's arithmetic driven by a designed driver**, and T59
should read its measurements that way. §10 lists the RE passes that would replace the placeholders.

### 2.3 There are four observed tactical outcomes, and they do not share a per-type ordering

The entry calls the Seleucid–Ptolemaic engagement *"the one observed tactical battle"*. The
research corpus actually holds **four** tactical outcomes. Each has a full result screen, and three
have a per-type breakdown:

| # | Battle | Source | Victor's total | Victor's per-type loss |
| --- | --- | --- | --- | --- |
| 1 | Seleucid v Ptolemaic | [`ptolemy-run-ui-inventory-and-leader-draw.md`][ptolemy] §3 | 45,100 → 26,696 | archers **100%**, LI 37.6%, HI 29.6%, LC 19.3%, HC **4.0%** |
| 2 | Rome v Gaul, `7.sav → 8.sav` | [`battle-observation.md`][observation] §"Battle result" | 50,700 → 39,941 | LC 61.9%, LI 51.9%, HC 49.9%, HI 6.7%; no archers; **no whole arm lost** |
| 3 | Rome v Gaul, `1_rome_270_winter_7`, first fight | [`full-battle-resolution-rome-vs-gaul.md`][rome-gaul] | 99,882 → 63,282 | HC **100%**, other types 26–42% |
| 4 | the same save, refought | [`battle-replayed-rout-mechanic-and-combat-constants.md`][rout] | 99,882 → 75,536 | not reported by type (3 units destroyed) |

Of the three battles with a per-type breakdown, **two** show the victor losing one whole arm, with a
different arm each time (#1 archers, #3 heavy cavalry). **One** shows the victor losing no whole arm
(#2). The per-type spread of the victor's losses is 96 points in #1, 55 points in #2, and 74 points
in #3.

That **supports** the entry's central reading as a possibility: a victor *can* lose one whole arm by
breaking rather than by attrition. In #3, both Roman heavy-cavalry units (755 and 2,432 troops) were
removed by the rout check, per the correction in [`rout`][rout]. Which of its branches removed each unit
is not settled. It is not a regularity, though.
Neither the lost arm nor even whether an arm is lost repeats from battle to battle.

It also shows that the entry's remark that heavy cavalry survives because *"it has the lowest
absolute rout threshold of any type (100) and is the least likely to reach it"* **rests on a false
premise**. It is not refuted by a single counterexample. The floor is `standardBattalionSize / 25`,
4% of a full battalion for **every** type (§5), so a lower absolute number confers no protection. A
full archer unit is exactly as far above its 140 as a full heavy-cavalry unit is above its 100. The
corrected reports support less than a size rule ([`rome-gaul`][rome-gaul] correction note and
[`rout`][rout] consequence 1, both revised in research `eb1c886`). 1st Dragoons (755 troops, 30% of a
battalion, 7.6× its floor) was destroyed in both fights, which makes being small relative to the floor
a **candidate** for its break **[derived]**. 3rd Dragoons (2,432, a near-full battalion at 24× its
floor) died in #3 and lost only 95 troops in #4, and which branch removed it in #3 is **[open]**. Floor
proximity does not predict #3's destroyed set either: 1st Foot, the unit closest to its floor (6.8×),
survived, while 5th Bowmen (24×) died.

The per-type orderings disagree across #1–#3, which is one more reason, beyond Done-when 7's own,
that no single battle can be a target. §7 and §9 use all four outcomes as smoke instances and tune
to none.

### 2.4 `ratio ≈ 46` lies outside the baseline's own reachable range

Done-when 7 asks for `ratio ≈ 46` to be recorded as the aggregate calibration, and §9 records it. It
comes with one arithmetic fact, which the source report did not state when this document was written.
It now does: [`instant-cannot`][instant-cannot], corrected in research `54b85d0`, confirms from
`FUN_0044AEE4` that the ratio is the weaker power × `0x28` / the stronger in both branches, that
`FUN_0044A8CC`'s power has **no quality term**, and the 14–27 range below **[confirmed code,
derived numbers]**. In the merged winner rule,
the winner is the side with the higher `armyPower`. The casualty ratio is
`loserPower × 40 / winnerPower`, so it **cannot exceed 40**. A ratio of 46 would mean the loser was
the stronger side, and the instant path would then have declared the other winner.

For this battle, the type totals give weighted sums of 19,900 (Seleucid) and 9,920 (Ptolemaic). With
the `/ 80` truncation, those become 248 × morale and 124 × morale. At equal strategic morale the
ratio is **20**. Across the confirmed strategic-morale clamp of 51…70
([`thracia-supply-morale.md`](thracia-supply-morale.md)), it is **14 to 27**. That means per-unit
winner losses of roughly 12–24%, against the observed 40.8%.

`ratio ≈ 46` is therefore the ratio at which the baseline's *per-unit loss expression* matches this
battle's total when it is swept free of the winner rule. It is not a state the baseline can reach.
That strengthens Done-when 7's warning: the number describes a tactical outcome, and the instant
model cannot produce it with its own inputs. What `ratio ≈ 46` measures is how far the tactical
outcome (40.8%) exceeds the instant resolver's ceiling, about 35.5% at the tie ratio of 40. It
constrains neither morale nor quality ([`instant-cannot`][instant-cannot] "What follows" 2). The
original's instant path has one more loss term, `FUN_0044AE20`'s small-unit deletion (§6.1), which the
merged baseline now also applies (T63, #295, 562e608). Whether it could close part of that gap for
this battle needs a unit-level roster the report does not have **[open]**. (The sums above use type
totals, because the battle's per-unit rosters are not in the report. Per-unit truncation moves them by
less than 0.1%.)

## 3. The common seam

Every candidate is a function with the same signature, so T59 can wrap the merged resolver unchanged
as C1 and put the other four behind the same seam.

**Input**, identical for every candidate:

| Input | Meaning | Source |
| --- | --- | --- |
| `attacker`, `defender` | Two armies. Each is an ordered list of at most 20 units (K31) of `(type, troops, quality)`, plus the army's **strategic** morale `M` (army record `+14`). | the merged `ArmyState` |
| `ruleset` | `unitTypes[]` (K01, K07, K23–K25); the `combat` block (K02–K06); and `combat.detailedResolver` (K15, and parts of K16 and K21, below) | `data/rulesets/*.json` |
| `rng` | the battle's `IRng`. Every draw goes through it, in the order the candidate states. | merged `IRng` |
| `onDefeat` | `destroy` (`classical-faithful`) or `scatter` (`improved`) | `flags.combatOnDefeat` |

**What the ruleset already carries, and what it does not.** Every shipped ruleset has a
`combat.detailedResolver` block, which is the reserve [`game-design.md` §Combat](../game-design.md)
describes. The shipped resolver does not read it. It holds:
- `typeEffectiveness`, with the same 25 values as K15 in the same row-major order, and
  `typeEffectivenessOrder`;
- `meleePowerDivisor 2000`, `meleeBasePowerFloor 12`, `meleeLossCapPercent 40`, `meleeLossCapOffset 1`
  and `meleeLossHardCap 30000` (K16);
- `inRangeShotMultiplier 2` (K21).

Candidates should read those values from there. Its `_provenance` still calls the matrix orientation
a *"candidate orientation"*. [`rout`][rout] has since settled it (K15), so the provenance string is
stale but the values are right.

The ruleset does **not** carry:
- the shooting vulnerability K20 (`+0x20`), which is in no `unitTypes[]` entry;
- the rest of K16, K17 and K21;
- the quality term K18;
- the rout constants K08–K14, K19, K22 and K26;
- the grid and turn constants K30, K32 and K33.

K31, the 20-unit cap, is carried, as `armyManagement.maxUnitsPerArmy`.

T59 may not add ruleset fields ([T59](../tasks/T59.md) Hazards), so these are **candidate-local
constants**. They are cited to their reports in §4.1 and belong in T59's candidate code, not in a
ruleset.

`quality` is the tier code 5–9 (poor, average, good, very good, elite) **[confirmed: [`rout`][rout]
§"The effectiveness matrix's axis ambiguity, resolved"; `qualityFloor 6` = average in the merged
ruleset]**.

**Two morales, never merged.** `M` is the strategic army morale, which the baseline multiplies into
`armyPower`. `m` is the per-unit tactical morale that the ±2/−3 rule and the rout check operate on.
They are distinct **[confirmed: [`game-design.md` §Combat](../game-design.md), first bullet]**. In
the original, `m` is *seeded from* `M` at battle start (K26), but it is a different number with a
different life. C2 and C5 have `m`. C1, C3 and C4 have only `M`.

**Output**, one record per battle:

- `winner`: attacker or defender.
- `after[unit]`: each of the winner's units' troops after the battle. A unit at 0 is removed.
- `survivors[unit]`: the loser's units that leave the field with troops > 0 (see "scatter" below).
  Under `onDefeat = destroy` these are discarded, exactly as the merged resolver discards the loser.
- `ending`: one of `decided` (C1, C3: a one-shot comparison), `annihilation`, `collapse`,
  `withdrawal`, `cap`. §8.7 defines them.
- `draws`: the number of `IRng` draws the battle phase consumed.
- `events` (C2, C4, C5): the per-exchange log that §8.5 and §8.7 read, including each break and its
  cause (strength floor, morale floor, band, cascade).

**The post-battle phase is shared, and it is not part of any candidate.** Money and supplies
absorption, promotion (`quality = max(quality, 6)`, then 1-in-4 `+1`, capped at 9), the unity ±25
swing, the 2-in-5 peace roll and scatter placement all run **after** the candidate. They run exactly
as the merged `ResolveField` runs them, on the candidate's output
**[confirmed: [`decompiled-diplomacy-peace-terms-and-instant-battles.md`][instant]; the tactical path
shares the ±25 unity and, [derived], the 1-in-4 promotion: [`rout`][rout] §"Post-battle bookkeeping"
and §"The promotion adjacency rule does not survive"]**. T59 measures the battle phase only, and
every draw count in §6 is a battle-phase count.

**Scatter consumes survivors, not a fraction.** Under `onDefeat = scatter`, the merged code applies
a mirrored casualty ratio to the loser and relocates the result through `ScatterPlacement`. In this
seam, a candidate returns `survivors`, and the shared post-battle phase hands those to the same
`ScatterPlacement` call, unchanged. C1's survivors are the merged mirrored-ratio survivors. The
other candidates' survivors are whatever their battle leaves standing. That difference is the point
of C5 (Done-when 2).

**Integer semantics.** All arithmetic is 64-bit integer, with division truncating toward zero,
applied in the written order. Where a candidate needs a fraction, it is written as ×1000 fixed
point, truncated. The original computes troops in shorts and its products in 32 bits. For C2 and
C5, whose exchange arithmetic is the original's, no product exceeds 2³¹ at legal inputs (the largest
is the shooting base, below 2.5 × 10⁸), so 64-bit arithmetic reproduces the 32-bit results. C3 and C4
are not the original's arithmetic, and their ×1000 fixed-point products do exceed 2³¹: C3's
`mbar1000 × troops × (q×10+M)` reaches about 7 × 10⁹, and its `Fire_i` numerator about 10¹¹. That is
why 64-bit arithmetic is mandated for every candidate, not only for exactness.

**`Random(n)`** means `rng.NextInt(0, n)`, a uniform integer in `[0, n)`, and `Random(0)` returns 0
without a draw (D13). The half-open reading is the one the merged `BattleCasualties` already uses for
`Random(15) + 105 ∈ [105, 120)`. The original's RNG (`FUN_0040284c`) is *"assumed to be a bounded
uniform RNG call from its usage pattern, not independently verified"*
([`formula`][formula], §"What this does not establish"). **[derived]**

## 4. Constants register

Done-when 1 requires every constant to be named from a report or marked `[designed]` with its search.
This table lists them all. The candidate sections use these IDs, and the **Used by** column shows
which candidate depends on which constant. `K` rows are sourced from reports. `D` rows are
`[designed]` (or `[derived]`/`[open]` where stated; D08 has since been confirmed), and each names its search; §11 lists the full
search.

### 4.1 Sourced constants

| ID | Constant | Value | Tag and source | Used by |
| --- | --- | --- | --- | --- |
| K01 | `combatPowerWeight[type]`, unit-type table `+0x26` | LI 20 · HI 100 · A 40 · LC 60 · HC 120 | [confirmed] [`unit-type-stat-table-in-dat.md`][unit-table] 2026-09-19 update; ruleset `unitTypes[].combatPowerWeight` | C1; C2 and C5 through D34; §8 |
| K02 | `armyPower = (Σ weight × troops / 100) / 80 × M` | divisors 100, 80 | [confirmed] [`instant`][instant] §"The original's instant battle resolver" (`FUN_0044A8CC`); ruleset `combat.powerTroopDivisor`, `powerDivisor` | C1, §8 |
| K03 | winner casualty ratio `loserPower × 40 / winnerPower` | numerator 40 | [confirmed] [`instant`][instant]; ruleset `combat.winnerCasualtyNumerator`. **Field call site only.** The siege's ratio is `max(1, min(15, def × 6 / atk))` and the naval carried army's is `d` (research `3f6ca09`; §1) | C1, C3 |
| K04 | per-unit loss `troops / (Random(15) + 105) × ratio` | 105, span 15 | [confirmed] [`decompiled-defection-and-siege-attrition.md`][siege-attrition] via `BattleCasualties`; ruleset `casualtyDivisorBase`, `casualtyDivisorRandomSpan` | C1, C3 |
| K05 | winner rule: higher power wins, **ties to the defender** | — | [confirmed] [`instant`][instant] (`winner = (pB < pA) ? attacker : defender`) | C1, C3; tie rule reused by C2, C4, C5 |
| K06 | mirrored survivor numerator, scatter distance | 40; 2–4 tiles | **[designed]** already, in the merged ruleset (`combat.scatteredDefeat._provenance`): searched `docs/reports/` for any partial-defeat outcome in the original, found none | C1 |
| K07 | `standardBattalionSize[type]`, `+0x1A` | LI 15,000 · HI 6,000 · A 3,500 · LC 7,000 · HC 2,500 | [confirmed] [`unit-table`][unit-table] | §5 (K08), §8 army construction |
| K08 | rout strength floor `standardBattalionSize / 25` | LI 600 · HI 240 · A 140 · LC 280 · HC 100 | [confirmed] [`rout`][rout] §"The rout mechanic" (`FUN_00438fb0`); both observed routs land on it | C2, C3 (variant), C5 |
| K09 | rout morale floor: routs unless `m > 19` | 19 | [confirmed] [`rout`][rout] | C2, C5 |
| K10 | safe outright if `m > 39` | 39 | [confirmed] [`rout`][rout] | C2, C5 |
| K11 | band check: survives if `Random(m) + Random(m) > 29` | 29 | [confirmed] [`rout`][rout]; *whether the branch fired in either recorded battle is unknown* (same report, §"What this does not establish") | C2, C5 |
| K12 | cascade: every surviving friend `m −= 6` | 6 | [confirmed, decompiled; **never observed**] [`rout`][rout] Next checks 2 | C2, C5 |
| K13 | cascade removal: a friend is removed if its `m < 30` after the −6. **One level only**: the friend goes straight to `FUN_00438f78` (troops 0, square cleared), so it starts no cascade of its own and earns the enemy no +5 | 30 | [confirmed, decompiled] [`rout`][rout] consequence 3, corrected in research `eb1c886` (it first said *"recursively"*) | C2, C5 |
| K14 | reward, once per rout that `FUN_00438fb0` itself decides: every live enemy `m = min(99, m + 5)`, and its target is cleared if it was the routed unit | 5, 99 | [confirmed, decompiled] [`rout`][rout] | C2, C5 |
| K15 | effectiveness matrix `value[attackerType][defenderType]` | the 25 values in §4.3 | [confirmed] values: [`combat-type-effectiveness-matrix.md`][matrix]; orientation: [`rout`][rout] §"axis ambiguity, resolved". In the ruleset as `combat.detailedResolver.typeEffectiveness` | C2, C3, C4, C5 |
| K16 | melee exchange (`FUN_004393ec`) | the formula in §4.4: `/2000`, `+12`, `/12`, `/10`, `+1`, cap 30,000, cap `troops × 4 / 10`, `+1` | [confirmed] [`formula`][formula] §"Melee" plus its 2026-09 update; the 40% cap is exact on 11 observations, both sides ([`rout`][rout]). `/2000`, `+12`, 40%, `+1` and 30,000 are in `combat.detailedResolver` | C2, C5 |
| K17 | focus factor `defFactor = min(4, focusCount)`; attacker loss × `(5 − d)/5`, defender loss × `(2d + 5)/5` | 4 | [confirmed] [`rout`][rout] §"Two small corrections" (`FUN_00448fd0` is `min`) | C2, C5 |
| K18 | quality term in both power expressions: `q × 10 + m` | 10 | [confirmed] [`rout`][rout] | C2, C5 (with `m`); C3, C4 (with `M`, a departure) |
| K19 | tactical morale per melee exchange: `+2` to the better side, `−3` to the other; upper clamp 99 | +2, −3, 99 | [confirmed] [`formula`][formula], [`morale-array`][morale-array]. The *comparison* that picks "the better side" is not given: see D07 | C2, C5 |
| K20 | shooting vulnerability `vuln[targetType]`, `+0x20` | LI 18 · HI 2 · A 18 · LC 15 · HC 4 | [confirmed] [`rout`][rout] §"`+0x20` … identified". **In no ruleset**, so it is a candidate-local constant (§3) | C2, C3, C4, C5 |
| K21 | shooting exchange (`FUN_0043845c`, `FUN_0043910c`) | the formula in §4.4: `× 5 + 150000`, `×2` in range, `min(shooter/3, target/2)`, `+1` | [confirmed] [`rout`][rout]. `×2` is `combat.detailedResolver.inRangeShotMultiplier` | C2, C4 (base only), C5 |
| K22 | shooting morale hit `m −= min(3, loss × 35 / (troopsAfter + 1))` | 35, 3 | [confirmed] [`rout`][rout] §"Two small corrections" | C2, C5 |
| K23 | `shots[type]`, `+0x1C`, a per-battle ammunition pool | LI 7 · HI 0 · A 25 · LC 9 · HC 0 | [confirmed] value: [`unit-table`][unit-table]. [derived] per-battle pool: the info panel read `Shots 19` mid-battle for an archer unit ([`rome-gaul`][rome-gaul]) | C2, C3, C4, C5 |
| K24 | `range[type]`, `+0x1E` | LI 1 · HI 0 · A 2 · LC 1 · HC 0 | [confirmed] [`unit-table`][unit-table]; read by the shooting code for the doubling test | C2, C5 |
| K25 | `moves[type]`, `+0x18` | LI 4 · HI 2 · A 4 · LC 6 · HC 5 | [confirmed] value: [`unit-table`][unit-table]. **[derived]** as tactical moves per turn: the info panel shows a `Moves` line per unit, and two heavy-infantry panels read `2 moves`, matching `+0x18 = 2` ([`battle-observation.md`][observation] 00:22, 08:04). An archer panel reads `Moves 4`, matching `+0x18 = 4` ([`rome-gaul`][rome-gaul]) | C2, C5 |
| K26 | initial tactical morale `m = Random(q × 4) + M` | 4 | [confirmed] formula: [`morale-array`][morale-array] §"The morale formula" (`FUN_00437de4`), which now names the added term `armyMorale`, the strategic morale `+14` (`DAT_0047C1FA`); [`supply-morale`][supply-morale]'s morale-write index lists this seeding (lines 38135/38162). The clamp is `[60, 90]`: see D08 | C2, C5 |
| K27 | strategic morale `M += 3` on battle entry, **for a computer-controlled side only** | 3 | **[confirmed]** [`morale-array`][morale-array] §"The morale formula" and [`supply-morale`][supply-morale]'s write index, both corrected in research `1762c84`. `FUN_00437de4` tests each army's **own** nation (`+0x490 == 0`, computer) at `0x00437E05`/`0x00437E0F` (army A) and `0x00437E9F`/`0x00437EA9` (army B). It does not depend on the opponent. It adds exactly 3 to the persistent `+14`, unclamped, once per battle (the `DAT_004a0b7c` guard). The instant resolver handles every AI-vs-AI fight and never writes `+14`, so in the original only a tactical battle's computer side gets +3, and its human side gets nothing. Save-checked: Gaul 62 → 65, Rome 68 → 68 and 70 → 70. Rome's 68 → 66 across `winter_7 → winter_9` is the end-of-turn supply decay, not a battle effect. The same branch re-sorts the computer army's slots (`FUN_00437d10`) and copies its opponent's two battle-delay words, which only pace the display. **Placeholder for C2 and C5, unchanged:** +3 to both sides, used only to seed `m`. **That placeholder now departs from the original**, which has no headless AI-vs-AI tactical battle to copy, and whether to keep it is posed to the user, not decided here (§6.2) | C2, C5 (before K26) |
| K28 | strategic morale clamp | 51…70 | [confirmed] [`thracia-supply-morale.md`](thracia-supply-morale.md) | §8 inputs |
| K29 | new-army strategic morale | 59 (`0x3B`) | [confirmed] [`supply-morale`][supply-morale] | §8 inputs |
| K30 | grid 14 columns × 12 rows; home rows 2 and 9, stepping inward | 14, 12, 2, 9 | [confirmed] [`formula`][formula] §"The AI dispatch chain" (`FUN_004381a4`); [`battle-code-entry-points.md`][entry-points] | C2, C5 |
| K31 | at most 20 units per army | 20 | [confirmed] [`formula`][formula] (`0x13 + 1` slots) | all |
| K32 | the battle ends when either side has zero live units | — | [confirmed] [`rout`][rout] (last line of `FUN_00438fb0`) | C2, C5 |
| K33 | sides alternate turns (`"<side> to move units"`) | — | [confirmed] [`rome-gaul`][rome-gaul] §"The tactical battle screen" and the `FUN_00439c84` loop ([`formula`][formula]) | C2, C5 |

### 4.2 Designed, derived and open placeholders

Every entry below was searched for in the eight reports T58 cites, in
[`battle-code-entry-points.md`][entry-points], [`ptolemy`][ptolemy], [`supply-morale`][supply-morale]
and the research repository's `docs/decompilation-plan.md`. §11 gives the search terms. "Searched:
none" means the search found nothing in any of them.

| ID | Placeholder | Value | Tag, and what the search found | Used by |
| --- | --- | --- | --- | --- |
| D01 | Placement | Units fill the side's home row (K30: row 2 for the attacker, row 9 for the defender) in slot order, starting at column 0 and running to column 13. Units 15–20 go in the next row inward (row 3 or row 8), again from column 0. | **[designed]** `FUN_004381a4` is described as *"column position cycling through a 3-wide block … unit-type ordering read from a lookup table"* ([`formula`][formula]). The block's column origin and the lookup table are in no report, and a literal 3-wide block of 20 units would put the two sides' 7-row-deep blocks into overlapping rows (2…8 and 9…3), so it cannot be what the text describes. | C2, C5 |
| D02 | First mover | The **attacker** moves first. After that, sides alternate (K33). | **[designed]** Searched for a turn-order rule: none decompiled. [`rome-gaul`][rome-gaul] says only that the header *"cycles between"* *"Rome to place units"*, *"Gaul to move units"* and *"Rome to move units"*, and gives no order. The one ordered pair in the corpus is [`observation`][observation]'s `7.6.png` *"Rome to place units"* followed by `7.8.png` *"Rome to move units"*, with no Gaul move shown between them. That weakly suggests the side that placed moved first, but it does not say which side attacked. The attacker is the placeholder because that side, Rome, both placed and attacked in the only battle whose attacker is known (§9.2). Rome was also the **human** side in both Rome–Gaul battles (inferred, not recorded: the recordings are the user's own play, and the save is a Rome campaign file), so the same evidence fits "the human side moves first" equally well. A headless run has no human side, so that reading cannot be used, and attacker-first stays a placeholder. An earlier revision tagged a defender-first rule `[derived]` from a sequence the source does not contain, and that tag was wrong. | C2, C5 |
| D03 | Target selection | Each unit targets the nearest live enemy by Chebyshev distance. Ties go to the lowest enemy slot index. The target is re-chosen whenever it is cleared (K14) or the target is no longer live. | **[designed]** Searched for target selection: none. `FUN_0043a31c`, the untraced branch of the AI dispatch, may or may not be the AI's move mode ([`formula`][formula] Next checks 3). The info panel's *"Unit set to attack"* ([`rome-gaul`][rome-gaul]) confirms that a target *field* exists (`DAT_004a0356`, [`morale-array`][morale-array]), not the choice rule. | C2, C5 |
| D04 | Movement | A unit moves up to `moves[type]` (K25) steps. Each step goes to the 8-neighbour square that is in bounds, empty, and minimises Chebyshev distance to the target. Ties are broken in the fixed order N, NE, E, SE, S, SW, W, NW, with "N" meaning toward the enemy home row. Movement stops once the unit is adjacent to its target (distance 1) or no step reduces the distance. One unit per square. | **[designed]** Searched for grid movement or pathing rules: none (the entry-points report lists *"movement ranges"* as still to recover). | C2, C5 |
| D05 | One action per unit per side-turn | In slot order, each live unit does exactly one of the following, in this priority order. **(a)** If it is adjacent to its target, it is queued for this side-turn's melee pass. **(b)** Otherwise, if `shots > 0` (K23), `range > 0` (K24) and distance ≤ `range + 1`, it shoots its target once (D11). **(c)** Otherwise, it moves (D04), and if it ends adjacent to its target it is queued for melee. After all units have acted, the melee pass runs `FUN_004393ec` once over the queued attackers in slot order. | **[designed]** except the batched melee pass, which is **[confirmed]**: `FUN_004393ec` *"iterates the same up-to-20 unit slots; for each attacker with a live assigned target"* ([`formula`][formula]). Searched for the move/shoot/attack priority: none. | C2, C5 |
| D06 | Shooting distance and doubling | A shot is legal at Chebyshev distance `2 … range + 1`. It is doubled (K21) when `distance − 1 < range`, i.e. when the number of empty squares between the two units is less than `range`. | **[open]** The report's code says `if (gridDistance(shooter, target) < range[shooterType]) base *= 2` ([`rout`][rout]). `gridDistance` is not defined in any report, and nor is a maximum shooting distance. **[designed]** placeholder as stated. **Its consequence, stated plainly:** light infantry and light cavalry (`range 1`) never get the doubled shot under this placeholder, and only archers do, at distance 2. The literal Chebyshev reading (`distance < range`) would never double a legal shot at all, because an adjacent unit melees (D05). Neither reading is shown to be the original's, since the evidence does not settle what `gridDistance` measures. | C2, C5 |
| D07 | "Better side" in the ±2/−3 rule | The attacker gets `+2` and the defender `−3` when `atkPower ≥ defPower` (the two K16 powers of that exchange). Otherwise the attacker gets `−3` and the defender `+2`. | **[designed]** reading of a confirmed rule. The report says only *"whichever side had the better troops/power ratio"* ([`formula`][formula]). Searched for the exact operands: none. | C2, C5 |
| D08 | Tactical morale clamps | Initial `m = max(60, min(90, Random(q × 4) + M))`, with `M` taken after K27's +3. During the battle, `m` is clamped to `[0, 99]`. | **[confirmed]** initial clamp `[60, 90]`. The sum goes through `min(90, ·)` (`FUN_00448fd0`) and then `max(60, ·)` (`FUN_00448fd8`), at `0x00438029`–`0x0043804d` (side A) and `0x0043813e`–`0x00438162` (side B) ([`morale-array`][morale-array] §"The morale formula", corrected in research `1762c84`). The earlier *"upper bounds ≈ 90 then 60"* was the same two calls misread, and this row's former placeholder (90 upper, 0 lower) is withdrawn. The row keeps its D-number so that references stay valid. The in-battle upper bound 99 is confirmed (K19, K14). The in-battle 0 floor stays **[designed]**, and it never binds **[derived]**: every decrement is followed by a rout check (K09 removes any unit at `m ≤ 19`) or, for the cascade, by K13's `< 30` removal. | C2, C5 |
| D09 | Round cap | 100 rounds (one round is one turn per side). At the cap, the side with the greater `liveP` (D34) wins, with ties to the defender (K05). Under C2, the loser's live units are then destroyed. Under C5, the loser performs an ordered withdrawal (§6.5). | **[designed]** The original has no cap: it fights until a side has no live units (K32). A headless run needs a cap so that a battle always terminates. Searched for surrender or timeout rules: `TBattleMap_Surrender` exists as a human action ([`entry-points`][entry-points]) and has no AI trigger. | C2, C5 |
| D10 | Rout-check order after a melee exchange | Attacker first, then defender. | **[designed]** The report says the check is called *"on both participants"* ([`rout`][rout]), not in which order. | C2, C5 |
| D11 | Shots per turn | One shot per shooting action. A unit's shots come from its per-battle pool (K23). | **[designed]** Searched: the recording shows the same shooter firing repeatedly at one target across turns (the 3rd Lancers five times, [`rout`][rout]), and nothing about shots per turn. | C2, C5 |
| D12 | `focusCount` | The number of live enemy units whose assigned target is this defender, including the attacker itself. So `focusCount ≥ 1`. | **[derived]** `FUN_00438420` is decompiled, but its body is not published. The report describes it as *"with `focusCount` attackers on one defender"* and lists the multipliers for 1…4 ([`rout`][rout]). | C2, C5 |
| D13 | `Random(0)` | Returns 0 and consumes no draw. | **[designed]** Searched: the original's behaviour for `n = 0` is not reported. It arises when `m = 0` in the band check (impossible, since K09 routs first) and when a shooting `range_` is 0 (impossible, because of the `+1`). The rule is here for completeness only. | all |
| D14 | Shooting-loss clamp | `loss = min(loss, tt)` (§4.4) | **[designed]** Searched: the shooting reports give `loss = Random(r) + Random(r)` with `r ≤ tt / 2 + 1`, so `loss ≤ tt + 1`, and say nothing about the overshoot. | C2, C5 |
| D20 | Exposure to an enemy's melee | `Xm_E(u) = 1000 + Σ_t share1000_E(t) × matrix[t][u]`, in ×1000 fixed point. `share1000_E(t) = 1000 × troops of type t in E / total troops of E`, over live units. | **[designed]** Searched for an army-level (non-positional) use of the matrix in the original: none. The original uses the matrix only per exchange. The `1000` term (one matrix point) keeps a type that no enemy type attacks well from taking zero losses. | C3, C4 |
| D21 | Exposure to an enemy's fire | `Xf_E(u) = vuln[u] × shooterShare1000_E / 4.5`, computed as `vuln[u] × shooterShare1000_E × 2 / 9`. `shooterShare1000_E = Σ_{t: shots[t] > 0} share1000_E(t)`. | **[designed]** The `4.5` puts `vuln`'s 2…18 scale onto the matrix's 0…4 scale (18/4). Searched: the original never combines the two tables. | C3 |
| D22 | C3's per-unit effective power | §6.3, formula E. The strategic `M` stands in for tactical `m`, and the shooting term is weighted `λ = 1`. | **[designed]** Searched: the original has no one-shot model that uses the matrix. The shooting term reuses the confirmed K21 base, multiplied by `shots[type]`. | C3 |
| D30 | C4's fire phase | Rounds 1–3 are fire rounds. From round 4 on, rounds are shock rounds. | **[designed]** EU4/CK lineage (fire, then shock). Searched: the original has no phases other than its turn loop. | C4 |
| D31 | C4's shock die and pace | One die per side per shock round, `d = Random(10)`. Damage `= Σ a_i × (5 + d) / 60`. | **[designed]** The `/60` sets the pace so that an even fight between two §8 test armies breaks in roughly 5–10 shock rounds. It is set as a design target, not fitted to any battle. Searched: none. | C4 |
| D32 | C4's morale damage | `P −= 200 × lossesThisRound / troopsAtStart` (i.e. μ = 2: 1% of starting troops lost costs 2 morale points). | **[designed]** Searched: none. The original's strategic morale changes only by supply, +3 on tactical-battle entry for a computer-controlled side (K27), and new-army initialisation ([`supply-morale`][supply-morale]), so no battle-driven rule exists to copy. | C4 |
| D33 | C4's round cap and pursuit | 30 rounds. Pursuit is one round in which only the winner's cavalry deals shock damage, `× 2`, and the loser deals none. | **[designed]** Searched: none. | C4 |
| D34 | Live strength | `liveP_S = Σ_{live units of S} combatPowerWeight[type] × troops` (the K02 numerator, before its divisors). | **[designed]** use of a confirmed weight (K01). Searched for any mid-battle strength comparison in the original's tactical path: none. The only strength comparison is the instant path's `armyPower` (K02), made once, before the battle, so this reuses its numerator over live units. | C2 (D09), C5 |
| D40 | C5's disorder cost of leaving | `troops −= troops × 5 / 100` (5%) for every unit that leaves the field, pursued or not. | **[designed]** It makes an unpursued withdrawal cheap but not free, per the user's steer. Searched: the original has no withdrawal ([`instant`][instant]; K32). | C5 |
| D41 | C5's pursuers per fleeing unit | At most 2 cavalry pursuers and 2 pursuing shots per fleeing unit. Each enemy unit pursues or shoots at most once per round. | **[designed]** Searched: none. | C5 |
| D42 | C5's ordered-withdrawal discount | Pursuit losses during an **ordered** withdrawal are halved (`/ 2`, truncated). A broken unit's flight is not discounted. | **[designed]** Searched: none. | C5 |
| D43 | C5's withdrawal threshold | `liveP_S × 100 < 60 × liveP_O` | **[designed]** Searched: none. | C5 |
| D44 | C5's earliest withdrawal | End of round 3 | **[designed]** Searched: none. | C5 |
| D45 | C5's fled winners rejoin | The winner's fled units rejoin its army after the battle, with the troops they have left after pursuit. | **[designed]** Searched: the original zeroes every routed unit (K32, `FUN_00438f78`). | C5 |
| D50 | Metric test armies, seeds and bands | §8 | **[designed]** Every band is a design decision taken before measurement, and §8 gives the reasoning for each. A band is a judgement about what the game should do, not a fact about the original, so there is no RE evidence to search for. The only evidence-bearing inputs, `M = 59` (K29) and the battalion sizes (K07), are cited. | §8 |

### 4.3 The effectiveness matrix (K15)

`value[attackerType][defenderType]`, read row-major from the DAT
**[confirmed: [`matrix`][matrix]; orientation: [`rout`][rout]]**:

| Attacker ↓ / Defender → | LI | HI | A | LC | HC |
| --- | ---: | ---: | ---: | ---: | ---: |
| **LI** | 4 | 1 | 0 | 3 | 0 |
| **HI** | 1 | 4 | 3 | 3 | 4 |
| **A** | 0 | 1 | 3 | 0 | 1 |
| **LC** | 4 | 4 | 3 | 1 | 0 |
| **HC** | 1 | 4 | 3 | 0 | 2 |

### 4.4 The exchange formulas (K16–K22), transcribed

Melee, `FUN_004393ec`, for an attacker `a` with a live assigned target `d`, where `ta`/`td` are
troops, `qa`/`qd` quality and `ma`/`md` tactical morale
**[confirmed: [`formula`][formula] with the corrections in its update and in [`rout`][rout]]**:

```text
dF      = min(4, focusCount(d))                                     // K17, D12
atkPow  = matrix[type_a][type_d] × ta × (qa×10 + ma) / 2000 + 12     // K15, K18
defPow  = matrix[type_d][type_a] × td × (qd×10 + md) / 2000 + 12
exA     = (ta × defPow / atkPow) / 12 + 1
lossA   = min(min(30000, (Random(exA) + Random(exA)) × (5 − dF) / 5),  ta × 4 / 10) + 1
exD     = (td × atkPow / defPow) / 10 + 1
lossD   = min(min(30000, (Random(exD) + Random(exD)) × (2×dF + 5) / 5), td × 4 / 10) + 1
ta -= lossA;  td -= lossD
morale: +2 / −3 per K19, D07; clamp D08
rout check (§5): a, then d (D10)
```

The draw order is the attacker's two draws, then the defender's two. That follows the transcription's
own order, `exchanges_atk` before `exchanges_def` **[derived]**.

Shooting, `FUN_0043845c` and `FUN_0043910c`, for shooter `s` and target `t` **[confirmed: [`rout`][rout]]**:

```text
base    = ts × qs × ms × vuln[type_t] / (ts × 5 + 150000)          // K20, K21
if in range (D06): base ×= 2
r       = min(min(ts / 3, tt / 2), base) + 1
loss    = Random(r) + Random(r)
tt -= loss;  shots_s -= 1
mt -= min(3, loss × 35 / (tt + 1))                                  // K22, tt after the loss
rout check (§5): t
```

The report does not say whether `loss` can exceed `tt`. Since `r ≤ tt / 2 + 1`, `loss ≤ tt + 1`. It
is clamped to `tt` (D14).

## 5. The unit-level break trigger, `FUN_00438fb0` [confirmed]

Decompiled in [`battle-replayed-rout-mechanic-and-combat-constants.md`][rout] §"The rout mechanic".
The melee function calls it on **both** participants after every exchange, and the shooting function
calls it on the target after every shot:

```c
if (troops >= standardBattalionSize / 25 && morale > 19) {
    if (morale > 39) return;                            // safe outright
    if (Random(morale) + Random(morale) > 29) return;   // survived the check
}
// ---- the unit routs ----
troops = 0; its grid square is cleared;                        // FUN_00438f78
for each unit on the SAME side:  morale -= 6;  if (morale < 30) FUN_00438f78 removes it too;
                                 // one level only: no further cascade and no +5 for these
for each unit on the OTHER side: morale = min(99, morale + 5); clear its target if it was this unit;
if (either side now has zero live units) the battle ends;
```

*(Corrected 2026-09-23. This block first read "it routs too (recursively)", after the source
report, which has since been corrected: [`rout`][rout] consequence 3, research `eb1c886`. The
decompiled loop at `0x00438FB0` calls `FUN_00438f78` for a cascaded friend. That function only zeroes
the troops and clears the square, so it does not re-enter `FUN_00438fb0`.)*

The three ways a unit breaks, and the two consequences:

| | Condition | Constants |
| --- | --- | --- |
| **Strength floor** | `troops < standardBattalionSize / 25`, at any morale | K07, K08: LI 600 · HI 240 · A 140 · LC 280 · HC 100 |
| **Morale floor** | `m ≤ 19`, at any strength | K09 |
| **Probabilistic band** | `20 ≤ m ≤ 39` and `Random(m) + Random(m) ≤ 29` | K10, K11. Per check, a unit breaks with probability 88.8% at `m = 20`, 51.7% at `m = 30` and 30.6% at `m = 39` |
| **Cascade** | on every rout that `FUN_00438fb0` decides, every surviving friend loses 6 `m`, and any friend now below 30 is removed with no check. **One level**: a friend removed this way triggers no cascade of its own | K12, K13 |
| **Reward** | on every rout that `FUN_00438fb0` decides, every live enemy gains 5 `m` (capped at 99) and drops its target if it was the routed unit. A friend removed by the cascade adds no +5 | K14 |

(The band probabilities are exact counts of `X + Y ≤ 29` over `X, Y` uniform on `[0, m)`: 355/400,
465/900 and 465/1521. They are here so the reader can see how steep the band is, and nothing uses
them as a constant. The check runs after **every** exchange a unit takes part in, so a unit that sits
in the band for several exchanges is very likely to break.)

**The cascade, exactly.** When `FUN_00438fb0` routs unit X, the following steps run in order:
1. X is removed (`FUN_00438f78`: troops 0, square cleared).
2. For each of the 20 slots on X's side, in slot order, whose unit F still has troops > 0:
   `F.m −= 6`, and if `F.m < 30`, F is removed by `FUN_00438f78` alone. F's removal runs no step 2
   or step 3 of its own.
3. For each of the 20 slots on the other side, in slot order, whose unit still has troops > 0: `+5`,
   capped at 99, and clear its target if it was X. A target that points at a unit removed in step 2
   is **not** cleared here. D03 re-chooses a target that is no longer live.
4. The battle-end test (K32).

**[confirmed]**: this is the decompiled loop structure of `FUN_00438fb0` (the whole-application dump,
`all_app_functions.txt` line 38854; [`rout`][rout] consequence 3, corrected in research `eb1c886`), not
an inference. An earlier revision transcribed the cascade as recursive and tagged the loop order
`[derived]`. Both are withdrawn.

**The strength floor is 4% of a full battalion for every type**, since `size / 25`. So it is a
*relative* floor on full-strength units and an *absolute* troop count per type. A small unit sits
closer to it. The corrected reports name that proximity only as a **candidate** for Rome's 755-troop
heavy-cavalry unit breaking **[derived]**. For the 2,432-troop unit, which was 24× its floor, the
branch is **[open]** ([`rome-gaul`][rome-gaul] correction note, revised in research `eb1c886`; §2.3).

This floor is the **tactical** one. The original's **instant** path has a separate size rule at
10% and 20% of a battalion, which is not a rout (§6.1).

**Who uses it (Done-when 3, first paragraph).** Every candidate states its relationship to this
trigger in its own section, and the relationships are:

| Candidate | Unit-level break |
| --- | --- |
| C1 | **None.** The merged resolver has no unit-level break, and none is added. |
| C2 | **Unchanged**, including the consequence (`troops = 0`). |
| C3 | **Variant: the strength floor only.** C3 has no tactical morale, so the morale floor, band and cascade have nothing to read. |
| C4 | **Something else: an army-level break only**, with no unit-level rule. §6.4 gives the reason. |
| C5 | **Trigger unchanged, consequence changed.** A broken unit leaves the field and pays a withdrawal cost instead of being zeroed. |

## 6. The candidates

Each candidate section has the same parts: what the candidate is; where it is faithful to the
original and where it departs; the algorithm; the constants it uses; its unit-level break (Done-when
3); its army-level exit, if any; its survivors (what `scatter` receives); and its draw count. The
candidates are listed in Done-when 1's order. What each predicts for the discriminating observation
is in §7, side by side.

### 6.1 C1: the merged instant resolver (baseline)

**What it is.** `InstantBattleResolver.ResolveField`, wrapped **unchanged** as T59 Done-when 1
requires. It is the original's own AI-vs-AI path, `FUN_0044AEE4`
**[confirmed: [`instant`][instant]]**.

**Faithful / departs.** It is faithful to `FUN_0044AEE4`'s winner rule, ratio, per-unit loss
expression, second-pass small-unit deletion and promotion rule (T63, #295, 562e608). `FUN_0044AE20`,
the helper that applies the winner's casualties, has a **second pass** that the merged
`BattleCasualties.Apply` now also runs. For slots 19 down to 0, it deletes (`FUN_0044AC3C`) any
unit left below `standardBattalionSize / 10` if it is national, or below `/ 5` if it is a mercenary.
That is LI 1,500 / 3,000, HI 600 / 1,200, A 350 / 700, LC 700 / 1,400 and HC 250 / 500. The pass runs
before the promotion loop, so a deleted unit is neither promoted nor rolled for **[confirmed: [`siege-attrition`][siege-attrition]
§"Siege attrition" and [`instant-cannot`][instant-cannot] §"The result", both corrected in
research `54b85d0`]**. The merged code still departs in two ways:
- **Recorded, by design:** under `improved`, the loser scatters (K06) instead of being deleted.
- **Recorded, by design (T63 Decision 6):** the deletion pass draws one `Random(15)` per unit rather
  than the original's fixed 20 per slot, because `IRng` cannot replay the original's sequence; see
  **Draws** below.

T59 wraps the merged resolver unchanged (T59 Done-when 1), so C1 measures the merged code, deletion
pass included (T63, #295, 562e608).

**Algorithm** (see the merged code's own doc comment for the full transcription):

```text
pA = armyPower(attacker); pD = armyPower(defender)          // K01, K02 (strategic M)
winner = (pD < pA) ? attacker : defender                     // K05
R = loserPower × 40 / winnerPower                            // K03, so R ≤ 40
for each winner unit, in slot order: troops −= troops / (Random(15) + 105) × R        // K04
(the merged code then does the same: deletes winner units below size/10, or size/5 for a mercenary
 — T63, #295, 562e608)
loser: destroy; or, under scatter, R' = winnerPower × 40 / loserPower (K06) applied the same way,
       and the result is relocated by ScatterPlacement
```

**Constants:** K01–K06.

**Unit-level break:** none, and none is added. No unit is removed for strength or morale, and there
is no cascade. The original's instant path does remove a unit for strength: the 10% / 20% deletion
above. That rule is not a rout, has no morale term and no cascade, and the merged code now applies
it too (T63, #295, 562e608).

**Army-level exit:** none. The winner is decided in one comparison, and `ending = decided`.

**Survivors:** under `scatter`, every loser unit after the mirrored ratio. The survivor *fraction* is
the same for every type, up to the `[105, 120)` divisor band.

**Draws:** `nW` divisor draws, one per slot in the winner's unit list, **occupied or not**, as
`BattleCasualties.Apply` transcribes the decompiled loop. Because the resolver is wrapped whole, the
post-battle draws follow in the merged order: one `Random(4)` per winner unit with troops > 0, then
one `Random(5)`. Under `scatter` there are then `nL` divisor draws (again one per slot), plus **one
scatter-distance draw, made only if the loser has survivors** (`appliedToLoser < loser.TotalTroops`,
`InstantBattleResolver.ResolveField`). The number is **fixed** for a given pair of armies. T59 reports the battle-phase part (`nW`, plus `nL`
under scatter) separately from the total.
The original differs here. `FUN_0044AE20` loops over all 20 slots of the army record and draws
exactly **20** `Random(15)` per call, whatever the unit count (`0x0044AE2B`–`AE61`, the draw at
`0x0044AE43` unconditional) **[confirmed: [`siege-attrition`][siege-attrition] §"`FUN_0044b27c`,
instruction by instruction", research `3f6ca09`]**. The merged `nW` is the length of the army's unit
list, which need not be 20. So C1's draw count is the merged code's, not the original's, and a
seeded C1 run does not replay the original's random stream — a documented departure kept by design
(T63 Decision 6, #295, 562e608).

**Fixed by construction, not measured.** Its winner is a deterministic function of the two armies,
so its upset rate (§8.3) is exactly 0. On power-matched armies (§8.0), every battle is an exact tie
that the defender wins, so CS-P (§8.1) is exactly 0.

### 6.2 C2: the original's tactical model, run headless

**What it is.** The original's tactical battle (`TBattleMap`), played to completion with no screen.
Every exchange uses the decompiled arithmetic (§4.4), and every unit is checked by the decompiled
rout function (§5) after each exchange it takes part in. The moves are chosen by the designed driver
(D01–D06, D09–D11), because the original's own tactical AI is not decompiled (§2.2).

**Faithful / departs.** Faithful: the melee and shooting exchanges (K15–K22), the tactical morale
rule (K19), the initial-morale formula and its `[60, 90]` clamp (K26, D08), the rout check with its
one-level cascade and its reward (§5), the battle-end rule (K32), the grid size and home rows (K30),
alternating turns (K33), the per-battle shot pools (K23), and the loser's total loss. Departs, by
necessity: placement detail (D01), first mover (D02), targeting (D03), movement (D04), action
priority (D05), shooting distance (D06), and the morale placeholder D07. **Departs, by placeholder:
K27.** The original gives the battle-entry +3 only to a computer-controlled side, and only in a
tactical battle, which always has a human side. C2 gives +3 to both sides. A headless run has no human
side, and the original never fights an AI-vs-AI battle tactically, so the original offers no case to
copy. Whether to keep the placeholder is posed to the user, not decided here. Departs, by choice, for
termination: a round cap (D09). Departs, by the seam's scope: the original **writes** its +3 to the
army's `+14` (K27), and C2 applies it only to seed `m` and does not write it back, because T59 changes
no game state.

*(Corrected 2026-09-23. C2 was specified against §5's recursive transcription and D08's
90-upper/0-lower placeholder. Both are now replaced by the confirmed rule, so C2 stays faithful on
those two points by the same reference. K27 moved from "disputed" to "confirmed, and the placeholder
departs from it".)*

**Algorithm.**

```text
setup:
  for each side S: M'_S = M_S + 3                                   // K27 placeholder: both sides (the
                                                                    // original: a computer side only);
                                                                    // seam-local, not written back
  for each unit, attacker's slots then defender's, in slot order:
      m = max(60, min(90, Random(q × 4) + M'_S))                     // K26, D08 [confirmed]
      shotsLeft = shots[type]                                        // K23
  place both armies                                                  // D01
loop round r = 1 … 100:                                              // D09
  for side S in (attacker, defender):                                // D02, K33
    for each live unit u of S, in slot order:
      if u has no live target: pick one                              // D03
      act (D05): queue for melee | shoot once (§4.4 shooting, then §5 on the target) | move (D04)
      if the battle has ended (K32): stop
    melee pass: for each queued u, in slot order, whose target is still live and adjacent:
      §4.4 melee (u attacks its target), then §5 on u, then §5 on the target (D10)
      if the battle has ended: stop
  if r = 100 and the battle has not ended: resolve by D09
```

`focusCount` (D12) is evaluated at the moment of each exchange, from the current target
assignments.

**Result.** `winner` is the side that still has live units. Every loser unit has been removed, and a
removed unit's troops are 0 (K32, `FUN_00438f78`). The winner's live units keep their troops. The
winner's routed units are at 0 and count as lost. `ending` is set from the cause of the loser's
**last** removal, as defined in §8.7.

**Constants:** K01 and K05 (for the cap, through D09 and D34), K07–K27, K30–K33, D01–D14, D34.

**Unit-level break:** `FUN_00438fb0` **unchanged**, including its consequence (`troops = 0`).

**Army-level exit:** none. The original never withdraws. It fights until one side has no live units
(K32). The cap (D09) exists only so that a run terminates, and §8.7 fails the candidate if the cap
decides more than 5% of battles.

**Survivors:** **none, by construction.** A removed unit has 0 troops, so under `scatter` the loser
has nothing to scatter, and the shared post-battle phase deletes it exactly as `destroy` would. This
is the original's behaviour, and it is the behaviour the user objected to (*"I do not like that an
army is completely destroyed"*). §8.8's survivor metric therefore fails C2 by construction. That is
recorded, not hidden: C5 (§6.5) is the variant of C2 that changes exactly this.

**Draws:** variable. One per unit at setup (`Random(q × 4)`), 2 per shot, 4 per melee exchange, and
2 per rout check that reaches the band (`20 ≤ m ≤ 39` and above the strength floor). T59 logs each
event, and §8.5 checks that the count of draws equals the count the event log implies.

**Cost note.** A 20-against-20 battle can run for many rounds of 40 unit actions each. This is the
candidate T59's hazard names as the one to budget for first.

### 6.3 C3: type-weighted instant resolver (the matrix without rounds)

**What it is.** C1's one-shot shape, with `armyPower` replaced by an **effective power** that reads
the matrix against the enemy's actual mix, and with the winner's casualties distributed by each
type's exposure to that mix. There are no rounds, no positions, and no tactical morale.

**Faithful / departs.** Faithful: the one-shot structure, the tie rule (K05), the ratio numerator
(K03), the per-unit loss expression and its divisor draw (K04), and the matrix, vulnerability and
shooting-base constants (K15, K18, K20, K21, K23). Departs: `armyPower`'s flat `+0x26` weights are
replaced by D22. Casualties are no longer flat, because of D20 and D21. A strength floor (K08) is
added.

**Algorithm.** For side `S` against enemy `O`, with shares over `O`'s units:

```text
share1000_O(t)  = 1000 × troops_O(t) / troops_O
mbar1000_i      = Σ_t share1000_O(t) × matrix[type_i][t]                         // K15
vbar1000_O      = Σ_t share1000_O(t) × vuln[t]                                   // K20
Mel_i           = mbar1000_i × troops_i × (q_i × 10 + M_S) / 2,000,000 + 12      // K16's atkPow shape, K18 with M
Fire_i          = shots[type_i] × (troops_i × q_i × M_S × vbar1000_O / 1000) / (troops_i × 5 + 150000)
                                                                                  // K21's base, un-doubled, × K23
E_S             = Σ_i (Mel_i + Fire_i)                                           // D22, λ = 1
winner          = (E_D < E_A) ? attacker : defender                              // K05
R               = E_loser × 40 / E_winner                                        // K03, so R ≤ 40
W_i             = Xm_loser(type_i) + Xf_loser(type_i)                            // D20, D21, ×1000
Wbar            = Σ_i troops_i × W_i / Σ_i troops_i                              // winner's units
for each winner unit i, in slot order:
    loss_i  = min(troops_i, (troops_i / (Random(15) + 105)) × R × W_i / Wbar)    // K04 order: divide first
    troops_i −= loss_i
    if troops_i < standardBattalionSize[type_i] / 25: troops_i = 0               // K08
loser: destroy; or, under scatter, R' = E_winner × 40 / E_loser, with each loser unit's W against the
       winner, and the same per-unit expression and K08 floor. Units with troops > 0 are the survivors.
```

**Constants:** K03–K05, K07, K08, K15, K16 (shape only), K18, K20, K21, K23, D20–D22.

**Unit-level break:** **a variant of the confirmed trigger: the strength floor only** (K08, the same
`standardBattalionSize / 25`). The morale floor, band, cascade and reward are omitted because C3 has
no per-unit tactical morale for them to read, and inventing a stand-in would add a second designed
mechanism to a candidate whose purpose is to test the matrix alone.

**Army-level exit:** none. The winner is decided in one comparison, and `ending = decided`.

**Survivors:** under `scatter`, the loser's units after `R'` and the floor. The survivor mix differs
by type, because `W` differs by type.

**Draws:** `nW` (plus `nL` under scatter). The count is fixed, and it is the same as C1's battle
phase.

**Fixed by construction, not measured.** C3's winner, like C1's, is a deterministic function of the
two armies, so its upset rate is exactly 0. It has no morale-driven ending, so §8.7 does not apply
to it. (Its floor removes units, but it cannot end a battle.) Unlike C1, it is **not** tied on
power-matched armies, because `E` depends on the opponent's mix.

### 6.4 C4: round-based, EU4/CK lineage

**What it is.** The *Europa Universalis IV* / *Crusader Kings* shape. Two armies exchange damage in
rounds, first fire and then shock. Each side's damage depends on its composition against the
enemy's, through the matrix and the vulnerability weights. Each side has one **army morale pool**,
which casualties drain. The battle ends when a pool breaks, and the winner then gets one pursuit
round. There are no positions and no per-unit morale.

**Faithful / departs.** Faithful: the matrix (K15), the quality term's shape (K18), the shooting base
and vulnerability (K20, K21), the shot pools (K23), the tie rule (K05), and the strategic morale `M`
as the pool's starting value. Departs: everything structural (D20, D30–D33). The original has no
rounds, no army-level morale pool and no army-level break.

**Algorithm.**

```text
P_S = M_S for each side; T0_S = starting troops of S; shotsLeft as K23
for round r = 1 … 30:                                                   // D33
  // both sides' damage is computed from the state at the start of the round, then applied together
  if r ≤ 3:                                                            // D30 fire round
    for S in (attacker, defender), for each unit i of S with shotsLeft > 0, in slot order:
      base_i = troops_i × q_i × max(P_S, 0) × vbar1000_O / 1000 / (troops_i × 5 + 150000)   // K21, P in place of m
      dmg_i  = min(troops_i / 3, Random(base_i + 1) + Random(base_i + 1));  shotsLeft_i −= 1
    F_S = Σ dmg_i;  each unit j of O loses F_S × troops_j × vuln[type_j] / Σ_k troops_k × vuln[type_k]
  else:                                                                // shock round
    d_S = Random(10), attacker's die first                             // D31
    a_i = mbar1000_i × troops_i × (q_i × 10 + max(P_S, 0)) / 2,000,000 + 12   // as C3's Mel_i, with P
    H_S = (Σ_i a_i) × (5 + d_S) / 60                                   // D31: sum first, one truncation
    each unit j of O loses H_S × troops_j × Xm_S(type_j) / Σ_k troops_k × Xm_S(type_k)     // D20
  apply both sides' losses (each clamped to the unit's troops); remove units at 0
  P_S −= 200 × lossesThisRound_S / T0_S                                // D32
  if a side has 0 troops: it loses (both at 0: defender wins, K05); ending = annihilation; stop
  if P_A ≤ 0 or P_D ≤ 0: the side with the lower P loses (tie: attacker loses, K05); ending = collapse
     pursuit (D33), only if the winner has at least one live cavalry unit (otherwise no draw, no loss):
       H = (Σ_{winner's cavalry i} a_i) × (5 + Random(10)) × 2 / 60, distributed onto the
       loser as a shock round, with no return damage; stop
       (multiply by 2 before dividing by 60, deliberately: one truncation, the same order as H_S
        and as K03's ratio, so pursuit damage is exactly twice the untruncated shock damage, rounded
        down once. The other order, / 60 × 2, can differ by 1.)
after round 30: the side with the lower P loses (tie: attacker loses); ending = cap; no pursuit
```

Shares (`share1000`, `mbar1000`, `vbar1000`) are recomputed over live units at the start of every
round. "Cavalry" means `light_cavalry` and `heavy_cavalry`.

**Constants:** K05, K15, K18, K20, K21, K23, D20, D30–D33.

**Unit-level break:** **something else: none at the unit level, and an army-level break instead.**
This is a deliberate departure from the confirmed trigger. The EU4/CK lineage resolves cohesion at
army level. Grafting `FUN_00438fb0` onto it would need a per-unit tactical morale that this model
does not have, and it would make C4 a coarser copy of C2 or C5 rather than a distinct candidate. In
C4, units reach 0 only by attrition. That is possible here because C4 has no 40% per-exchange cap.

**Army-level exit, with the four answers Done-when 3 asks of any army-level rule:**
- **What** is evaluated: the army morale pool `P`, drained by casualties (D32).
- **How often**: every round, from round 1. Fire rounds drain it too.
- **Who** evaluates it: both sides, by the same rule.
- **What it costs**: one pursuit round, dealt by the winner's cavalry only, at double shock damage
  (D33). A winner with no cavalry inflicts no pursuit losses.

**Survivors:** under `scatter`, the loser's units after pursuit.

**Draws:** 2 per shooting unit per fire round (at most 3 rounds), 2 per shock round (one die per
side), and 1 for pursuit **only when the winner has a live cavalry unit**. This is variable, and is logged per round.

### 6.5 C5: morale and retreat (the *Total War* shape)

**What it is.** The user's steer of 2026-09-20, specified in full as Done-when 2 requires. **C5 is
C2 with three changes:**
1. A unit that breaks **leaves the field and pays for leaving**, instead of being zeroed.
2. What leaving costs depends on **who is chasing**: enemy cavalry pursues and enemy shooters fire at
   it, and an unpursued unit pays only a small disorder cost.
3. A side can make an **ordered army-level withdrawal**.

Building C5 on C2 rather than on a fresh engine is deliberate. The pair differs **only** in what
happens after a break, so T59's scorecard can attribute any difference between them to the retreat
rules and not to a different driver.

The user proposed this model. Per T58's hazard, that is a specification input, not a verdict. It is
specified here as carefully as the others, and §8 measures it as sceptically. The entry names
non-transitivity (§8.2) as its likeliest weakness, so T59 should look at that metric first.

**Faithful / departs.** Faithful: everything C2 keeps, and in particular **the unit-level trigger is
`FUN_00438fb0` unchanged**, with all three break conditions, the one-level −6 cascade, the <30
removal and the +5 reward (§5, corrected 2026-09-23 from "recursive"). Departs: the consequence
of a break (flight, D40–D42, instead of `troops = 0`), the ordered withdrawal (D42–D44), and fled
winners rejoining (D45). It also inherits C2's K27 placeholder, +3 to both sides, and with it C2's
departure from the original's computer-side-only rule (§6.2).

#### 6.5.1 Unit level: the trigger is confirmed and reused; only the consequence changes

Wherever §5 says *"the unit routs"*, C5 calls `Flee(X, ordered = false)` (§6.5.2) at the point where
`FUN_00438fb0` would call `FUN_00438f78`. Everything else in the routine stays as §5 gives it:
the −6 to each surviving friend, removal below 30 (each friend removed this way also calls `Flee`,
and, as in the original, runs no cascade of its own and adds no +5), the +5 and target-clear on the
other side for X alone, and the battle-end test. A fled unit is **not live**. It no
longer occupies a square, cannot be targeted, and does not count toward K32 or toward `focusCount`.

This is the departure the entry allows (*"a candidate is welcome to depart from it, but must say
that it is departing and why"*). C5 departs because in the original a broken unit's troops are lost
outright, which is exactly the annihilation the user objects to. The *trigger* is not redesigned. It
is the confirmed one, used as it stands.

#### 6.5.2 The cost of leaving: who is chasing

`Flee(X, ordered)` runs at the moment X leaves. It uses one counter per enemy unit per round,
`pursuedThisRound` and `firedPursuitThisRound`, both reset at the start of each round.

```text
1. clear X's square; X is no longer live
2. T = X.troops;  T −= T × 5 / 100                                     // D40 disorder, always paid
3. cavalry pursuit: the first up-to-2 live enemy units of type light_cavalry or heavy_cavalry with
   pursuedThisRound = false, in slot order                             // D41
   for the k-th pursuer j (k = 1, 2):
      K16's defender-side loss with j as attacker and X as defender, and dF = k:
        atkPow = matrix[type_j][type_X] × t_j × (q_j×10 + m_j) / 2000 + 12
        defPow = matrix[type_X][type_j] × T   × (q_X×10 + m_X) / 2000 + 12
        exD    = (T × atkPow / defPow) / 10 + 1
        loss   = min(min(30000, (Random(exD) + Random(exD)) × (2k + 5) / 5), T × 4 / 10) + 1
      if ordered: loss = loss / 2                                      // D42
      T −= min(T, loss); j.pursuedThisRound = true; j skips its next D05 action
      (no loss to j, no morale change, no rout check: X has already broken)
4. pursuing fire: the first up-to-2 live enemy units with shotsLeft > 0 and range > 0 and
   firedPursuitThisRound = false, in slot order                        // D41
   each fires one K21 shot at X, un-doubled (X has left the grid, so it has no distance), with
   T as the target's troops; shotsLeft −= 1
      if ordered: loss = loss / 2                                      // D42
      T −= min(T, loss); firedPursuitThisRound = true
      (no K22 morale hit, no rout check)
5. X.fledTroops = T   (T = 0 means the unit was destroyed in flight)
```

What this produces, in the steer's own terms:
- **Pursuing cavalry makes leaving expensive.** Each cavalry pursuer lands a full K16 defender hit of
  up to 40% of what is left, and a second pursuer's hit is multiplied by 9/5 through the focus
  factor, exactly as focus fire works in melee.
- **Shooting archers make it expensive.** Each shooter lands one K21 shot, and the target's `vuln`
  applies. Fleeing archers and light infantry (vuln 18) suffer more than fleeing heavy infantry
  (vuln 2).
- **An unpursued withdrawal is cheap.** With no enemy cavalry or shooters free, a unit pays only the
  5% disorder cost (D40).
- **Pursuit capacity is finite.** Each enemy unit pursues or fires at most once per round. One unit
  breaking alone is chased hard. In a mass cascade, the first units to break absorb the pursuers and
  the rest escape at 5%.

#### 6.5.3 Army level: the ordered withdrawal, with the four answers

**This part has no original analogue.** The original fights until one side has no live units
(K32). Every constant below is `[designed]`. The search was for any withdrawal, retreat, surrender
trigger or partial-defeat outcome, and it found none (§11).

- **What is evaluated: relative live strength.** At the evaluation point, side S withdraws if
  `liveP_S × 100 < 60 × liveP_O` (D34, D43). `liveP` is the confirmed `+0x26`-weighted troop sum
  (K01) over **live** units, so fled units, including those a cascade has just removed, no longer
  count. The alternatives Done-when 3 lists were considered, and each was set aside for a stated
  reason:
  - *casualties taken so far* ignores the enemy's condition, so a side that has lost 40% against an
    enemy that has lost 70% would still withdraw;
  - *aggregate morale* would evaluate the `m` array that the unit-level trigger already acts on, which
    double-counts the same signal;
  - *the cascade having started* is not a separate test here, because a cascade's effect on `liveP` is
    exactly how it reaches the army level (§6.5.4).
- **How often: at the end of every round, from the end of round 3 onward** (D44). A side therefore
  cannot decline a battle on contact. Before that point, only the unit level operates.
- **Who evaluates it: both seats, by the same policy.** The attacker is tested first, then the
  defender. Because 60 < 100, at most one side can satisfy the condition at once. A human-offered
  variant is out of scope (§1).
- **What it costs:** every live unit of S calls `Flee(X, ordered = true)`, in slot order. Before the
  withdrawal starts, each enemy unit's `pursuedThisRound` and `firedPursuitThisRound` are reset, so
  **each enemy cavalry unit pursues at most one withdrawing unit, and each enemy shooter fires at most
  one shot, across the whole withdrawal**. Every pursuit and shot loss is halved (D42). The 5% disorder
  cost is paid in full (D40). An ordered withdrawal triggers **no** −6 cascade and **no** +5 reward:
  the units leave together, and nobody breaks. `ending = withdrawal`.

Leaving is therefore **cheaper than staying, but not free**. Staying risks the cascade, whose broken
units pay pursuit in full, and each rout that starts a cascade hands the enemy +5 morale. Leaving in
order pays half-rate pursuit once per enemy unit, plus 5%.

#### 6.5.4 How the two levels interact

Done-when 3 calls this interaction *"the design, not a detail"*, so it is specified exactly:

1. **Order of evaluation.** Unit-level breaks happen inside a side-turn, the moment an exchange
   triggers them (§5). The army-level test runs only at the **end of a round**. So **a cascade that
   has started always runs to completion** before any army decision. The army rule can never
   interrupt a cascade that is under way. It can only prevent the next one.
2. **A cascade is the usual way the army rule fires.** Every unit a cascade removes lowers `liveP_S`,
   and every break that `FUN_00438fb0` itself decides adds +5 morale to the enemy, which makes the
   enemy's own units less likely to break. A friend the cascade removes adds none (§5). A cascade
   that removes enough strength during round `r` makes the round-end test true, and
   the survivors then leave in order instead of continuing to cascade in round `r + 1`. That is the
   intended coupling: **the cascade is what triggers the withdrawal, and the withdrawal is what stops
   the cascade from finishing the army.**
3. **A cascade can also pre-empt the army rule entirely.** A cascade that removes a side's last live
   unit ends the battle by K32 before any round-end test, so `ending = collapse` and every unit has
   fled at the unordered rate. Before round 3, this is the only way a battle can end early.
4. **Too early and too late are both measurable failures.** If D43's threshold is set too high,
   withdrawals fire before cascades ever get a chance, and §8.7 measures that as a cascade rate
   below 10%. If it is too low, cascades end almost every battle, and the collapse share goes above
   90%. The 60% threshold is a placeholder between the two. It is not tuned (§1), and §8.7 is where
   T59 reports which side of the band it lands on.
5. **Whether a mass cascade is cheaper than an ordered withdrawal** depends on how much pursuit
   capacity the enemy has already spent that round. That is a genuine ambiguity in the design. It is
   not resolved here. §8.8 breaks survivors down by `ending` so the scorecard shows it.

#### 6.5.5 End of battle, winner, and what `scatter` receives

- The battle ends when a side has no live units (K32; all its units have fled or been destroyed), or
  when a side withdraws, or at the D09 cap. At the cap, the side with the lower `liveP` withdraws in
  order (ties: the attacker withdraws), and `ending = cap`.
- The **winner** is the side that did not run out of live units and did not withdraw. If both sides
  reach zero live units in the same step, the defender wins (K05).
- **Winner:** its live units keep their troops. Its fled units **rejoin with `fledTroops`** (D45),
  because in the steer's model a broken unit that gets away is not destroyed. A fled winner unit at
  0 is lost.
- **Loser:** under `destroy`, discarded, as now. Under `scatter`, **the loser's fled units with
  `fledTroops > 0` are the scattered army**. They are handed, unit by unit and with their types, to
  the shared post-battle phase's `ScatterPlacement` call.

This is what makes the merged scatter mechanism *mean* something (Done-when 2). Under C1, the
scattered army is the loser's starting army times one flat fraction. Under C5, it is **whoever got
away**. How many troops that is, and of which types, is an outcome of the battle, and it depends on
three things: when the side broke or withdrew, how much enemy cavalry and archery was free to chase
it, and which of its own types are hard to shoot (`vuln`) or hard to catch (the matrix column
against the pursuer). §8.8 measures exactly that dependence.

**Design leverage (the reason this candidate is here, not an assessment of it).** Under annihilation
or a flat casualty ratio, a losing player has nothing to decide. Under C5, withdrawing early costs
the field but keeps a force in being. With the symmetric AI policy, that choice is fixed at D43. For
a human, it would be the offered choice deferred in §1.

**Constants:** everything C2 uses, plus K01 (through D34) and D40–D45.

**Unit-level break:** `FUN_00438fb0`'s trigger **unchanged**. Its consequence is changed to
`Flee`, for the reason given in §6.5.1.

**Army-level exit:** the ordered withdrawal (§6.5.3).

**Survivors:** the loser's fled units (§6.5.5).

**Draws:** C2's count, plus 2 per cavalry pursuit hit and 2 per pursuing shot, all logged per event.

## 7. The discriminating observation, candidate by candidate

**The observation** ([`ptolemy`][ptolemy] §3): the **winner** (Seleucid) lost its archers entirely,
`5,200 → 0`, while its heavy cavalry lost `97 of 2,400`, or 4%. The loser (Ptolemaic) **had no
archers**. The winner cannot have lost its archers by being run down while fleeing, since that would
predict heavy losses for the *loser's* archers.

The entry's reading is that those archers **routed**. `FUN_00438fb0` sets a routed unit's troops to
exactly 0, and that is what the summary panel shows. If that is right, the per-type spread is mostly
a **break** phenomenon. A model with no unit-break rule cannot then reproduce it at any tuning,
however carefully it models casualties.

**The other instances put it in proportion (§2.3).** Of the three observed battles with a per-type
breakdown, **two** show the victor losing one whole arm, and **one** does not:
- In Rome–Gaul `1_rome_270_winter_7` (first fight), the winner's **heavy cavalry** went
  `3,187 → 0`, with both units (755 and 2,432 troops) removed by the rout check, while every other
  Roman type kept 57–74% ([`rome-gaul`][rome-gaul] and its correction note; [`rout`][rout]).
- In Rome–Gaul `7.sav → 8.sav`, the winner lost 6.7–61.9% per type and **no** arm entirely
  ([`observation`][observation]).

So the observation to explain is not "the victor always loses an arm". It is that **losses are
lumpy**. Sometimes a whole arm goes and sometimes none does, and which arm goes varies. C2, which
breaks units and zeroes them, can produce all three shapes. C5 breaks units too, but a broken unit
reaches zero only if pursuit catches it (§6.5). A candidate that shapes losses only through fixed
per-type weights produces the same ordering against the same enemy every time. It can lose a whole
arm only as the table below says: C3 only as a limiting case, and C4 only by attrition in a long
battle.

For each candidate, the entry asks three questions: can it produce **a victorious army losing one
whole arm**; does it get there by **attrition** or by **breaking units**; and what does it predict
for this battle? One battle validates nothing (§9), so nothing below is a pass or a fail. The column
headed "for this battle" is computed only for the one-shot candidates, whose outcome shape can be
worked out by hand. For C2, C4 and C5 it is left to T59's smoke run (§9).

| | Can a victor lose one whole arm? | By attrition or by breaking? | For this battle |
| --- | --- | --- | --- |
| **C1** | **Only via the deletion pass**, when every unit of an arm falls below the threshold; **not** from attrition or ratio/tuning alone. | Attrition is flat; the deletion pass is a size effect, not a type effect. | Every type loses the same fraction from attrition, spread about 1 pp. The level is at most 40/105 ≈ 38%, and at legal morale 12–24% (§2.4). |
| **C2** | **Yes.** | **Breaking**: the strength floor, the morale floor or band, and the cascade. Attrition alone cannot reach 0, because of the 40% cap. | Left to T59. See the text below for the mechanism. |
| **C3** | **Only as a limiting case**, when a unit is pushed below 4% of a full battalion. In practice, no. | Attrition only, shaped by exposure. The strength floor can finish a unit that attrition has nearly exhausted. | Archers ≈ 0.80× the army's mean loss rate, heavy cavalry 0.36×, light infantry 1.25×. **It does not predict archers at 100%.** |
| **C4** | **Only by attrition in a long battle.** The army pool normally breaks first. | Attrition only, with no unit-level break. | Left to T59. Fire rounds hit archers as hard as light infantry (vuln 18 each), and shock rounds hit them second-least (exposure 1,690 against light infantry's 4,551). |
| **C5** | **The break, yes. The zero, only if pursuit catches the unit.** | **Breaking**, with the same trigger as C2. | Left to T59. Broken archers flee, and **rejoin** (D45) with whatever pursuit leaves them. |

**C1.** No mechanism distinguishes types by attrition alone. The flat-shape finding
([`instant-cannot`][instant-cannot]) is exactly this. Separately, the baseline cannot reach the
battle's *total* at any legal morale either (§2.4). Attrition alone cannot produce a victorious army
losing one whole arm — but the merged code's deletion pass now can (§6.1, T63, #295, 562e608),
in the same case as the original's `FUN_0044AEE4`: when every unit of that arm is left under 10% of
a battalion (20% for a mercenary) and deleted. That is a size effect, not a type effect. It does not
arise on §9.1's designed roster. Its unit furthest below a full battalion is the light cavalry, 1,800
of 7,000 (26%), and after the largest possible loss (40/105, about 38%) it still holds about 16%.

**C2.** C2 produces the observation by **breaking units**, and the matrix gives archers the profile
of a unit that breaks.
- **Archers are weak in melee.** Their attack row is `0 1 3 0 1` and their defensive column entries
  (`matrix[A][attacker]`) are `0` against light infantry and light cavalry. An archer unit in melee
  with either type defends at the `+12` floor, takes the 40% cap readily, and under D07 loses 3 `m`
  per exchange.
- **Archers are easy to shoot.** Their `vuln` is 18, and the Ptolemaic army's light infantry and
  light cavalry both shoot. Each shot costs up to 3 `m` (K22).
- **Falling morale does the rest.** Falling `m` takes the unit into the 20…39 band, where each check
  breaks it with probability 31–89% (§5). One archer break costs every friend 6 `m`, and a second
  archer unit already in the low 30s is removed with no check. That removal is one level only
  (§5): it costs the other friends nothing further and gives the enemy no +5.
- **Heavy cavalry differs from archers in exposure, not in its floor.** Its floor is 4% of a
  battalion, the same as every type's (§5), so that is no protection. The real differences are
  that it is hard to shoot (`vuln` 4 against archers' 18), and its melee row (`1 4 3 0 2`) and its
  defensive entries are stronger than the archers' (§4.3), so it loses fewer exchanges and less
  morale.

Whether a given C2 run actually breaks the archers depends on which units meet, and that is decided
by the designed driver (D01, D03, D04). So C2's reproduction of this battle is only as faithful as
those placeholders (§2.2). The same function removed Rome's two heavy-cavalry units in
`1_rome_270_winter_7`, but which branch removed each is not settled. For the 755-troop unit (30% of a
battalion), small-unit proximity to the floor is a candidate **[derived]**. For the 2,432-troop unit it
is **[open]** (§2.3). Nothing in the mechanism forces any arm to break, as in `7.sav → 8.sav`.

**C3.** The prediction follows from D20 and D21 against the Ptolemaic mix. The shares are light inf
768, heavy inf 115, archers 0, light cav 83 and heavy cav 32 (×1000), and the shooter share is 851.

| Seleucid type | `Xm` | `Xf` | `W` | `W / Wbar` (`Wbar` = 6,357) |
| --- | ---: | ---: | ---: | ---: |
| Light inf | 4,551 | 3,404 | 7,955 | 1.25 |
| Heavy inf | 2,688 | 378 | 3,066 | 0.48 |
| Archers | 1,690 | 3,404 | 5,094 | 0.80 |
| Light cav | 3,732 | 2,836 | 6,568 | 1.03 |
| Heavy cav | 1,524 | 756 | 2,280 | 0.36 |

C3 therefore predicts differentiated losses by **attrition**. The ordering is fixed by the enemy's
mix: heavy cavalry lightest, which matches, and archers *below* average, which does not. It **cannot
produce the archers' 100%.** That would need the archers' loss fraction `R × (W_A / Wbar) / divisor` to reach 96%, so that
the strength floor removes what is left. At the most favourable divisor (105) and `R ≤ 40`, that
needs `W_A / Wbar ≥ 0.96 × 105 / 40 ≈ 2.5`, against the 0.80 computed. This is
reported as the model's prediction, not as a fail. Per the hazard, the weights **must not** be
adjusted toward the observation.

**C4.** C4 has no unit-level break, so a type reaches 0 only by attrition. Its losses are shares of
the side's damage in proportion to `troops × weight`, so every unit of a type loses the same
fraction per round, and a type is exhausted only once its cumulative exposure reaches 100%. The army
pool (D32) breaks at roughly 25–35% army-wide losses at `M = 51…70`, so the most-exposed type would
need an exposure several times the army's mean to reach 0 first. **C4 is not expected to produce a
victorious army losing one whole arm**, and when it does, it gets there by attrition.

**C5.** C5 produces the **break** by the same mechanism and trigger as C2. By design, it does not
produce the **zero** unless the fleeing unit is destroyed in flight. The Seleucid archers would
break and flee. The Ptolemaic light and heavy cavalry would pursue them (the matrix gives LC→A 3 and
HC→A 3) and its light infantry and cavalry would shoot them (`vuln` 18). Whatever survives rejoins
the winner (D45). **C5 reproduces the observation's mechanism but, deliberately, not its final
number.** This is a departure from the original that follows from the user's objection, and it is
stated here so the scorecard is not read as C5 "missing" the observation by accident.

## 8. The metric suite

Every metric below has an exact procedure and a pass band, and **each states in advance what makes
a candidate fail it** (Done-when 5 and 6). All bands are **[designed]** (D50). They were set here,
before any candidate was run. The reasoning for each band is given with it, so a reader can
disagree with the reasoning rather than with a number. T59 records the measured number for every
candidate and metric, and **must not move a band after seeing a result** (T59 Done-when 4).

Where a result for a metric follows from a candidate's own arithmetic, it is stated under **"Fixed by
construction"**, so the scorecard does not present it as a discovery. These are the only advance
results in this section, and they are facts about the arithmetic, not a ranking.

### 8.0 Test armies, seats and seeds (shared by every metric)

- **Units.** Quality 6 ("average") everywhere. Strategic morale `M = 59`, the confirmed new-army
  value (K29), on both sides.
- **Splitting a type into units.** Each type's troops are split into full battalions of
  `standardBattalionSize` (K07) plus one remainder unit. A remainder below the type's strength floor
  (K08) is added to the last full battalion instead. Units are in slot order LI, HI, A, LC, HC, with
  full battalions before the remainder. Every army below has at most 18 units (K31 allows 20). Every
  remainder is above its floor except one, and the merge rule handles that one: in the upset-rate
  uniform army at `κ = 0.90`, light cavalry is `7,200 = 7,000 + 200`, and 200 is below LC's 280, so
  it becomes a single 7,200-troop unit.
- **The 21 compositions (Σ):** 5 pure; the 10 two-type 50/50 pairs; the uniform mix (20% each); and
  5 one-heavy mixes (60% of one type, 10% of each other).
- **T-scale**, used by CS-T and UR: the shares are of **troops**, with 40,000 troops per army.
- **P-scale**, used by CS-P, NT, CD, EN and SV: the shares are of **weighted power**, with
  `Σ combatPowerWeight × troops = 2,400,000` per army, so `troops_t = 2,400,000 × share_t / 100 /
  weight_t`. Every P-scale army then has a merged `armyPower` of exactly `300 × M`. The unit splits
  are multiples of 100, so the per-unit `/100` truncation in K02 is exact. P-scale armies are
  therefore **equal in the baseline's own measure**, and any difference in who wins comes from the
  candidate's own treatment of composition.
- **Schedule.** Every ordered pair `(A, B)` of Σ is played, including mirrors, with `A` attacking.
  That is 441 matchups, at **S = 200** seeds each.
- **Seed.** For matchup index `i = 21 × index(A) + index(B)` (Σ in the order listed above) and
  repetition `k`, the seed is `1,000,003 × i + k`. It is passed to a fresh `IRng` for that battle.
- **Decisive battle** means one with a winner. Every candidate always produces one, since ties go to
  the defender (K05).
- **Symmetric score.** `w(A, B)` is the fraction of the 200 battles in which `A`, attacking, wins
  against `B`. The seat-neutral probability that `A` beats `B` is
  `p(A, B) = [w(A, B) + 1 − w(B, A)] / 2`, and `s(A) = mean over B ≠ A of p(A, B)`.

If a candidate's cost makes 88,200 battles infeasible, T59 reports that as its §8.6 finding. It does
**not** lower `S` for one candidate.

### 8.1 Composition sensitivity (CS-T as Done-when 5 words it, and CS-P)

**Procedure.** Compute `s(A)` for all 21 compositions. `CS = max_A s(A) − min_A s(A)`. CS-T uses the
T-scale armies and CS-P the P-scale armies.

**Pass bands.**
- CS-T **passes if ≥ 0.20** and fails below 0.20.
- CS-P **passes if ≥ 0.20** and fails below 0.20.

A candidate must pass both.

**Why.** Twenty points between the best and worst mix is the smallest spread a player would notice
over a campaign. Below it, composition is decoration. CS-P exists because CS-T alone cannot fail the
baseline (§2.1). A resolver with a fixed per-type price passes CS-T just by charging more for heavy
cavalry.

**Fails if** either spread is below 0.20. A resolver that treated every type alike would score about
0 on both. That is the case Done-when 5 describes.

**Fixed by construction:**
- **C1, CS-P = 0 exactly.** Every P-scale battle is an exact tie, which the defender wins, so every
  `p(A, B) = 0.5`.
- **C1, CS-T is large.** The K01 weights make heavy-cavalry armies beat light-infantry armies every
  time, so C1 is expected to **pass CS-T and fail CS-P**.

### 8.2 Non-transitivity (NT)

**Procedure.** On the P-scale `p(A, B)` from §8.1, draw a directed edge `A ▷ B` when
`p(A, B) ≥ 0.55`. That threshold is 2 standard errors above an even contest at 400 battles per
unordered pair. Count the directed 3-cycles `A ▷ B ▷ C ▷ A`. Call `A` **dominant** if `A ▷ B` for
every `B ≠ A`.

**Pass band.** It **passes if** at least one 3-cycle exists **and** no composition is dominant.

**Why.** Done-when 5: a single dominant composition is a design failure. So is a strict ordering
with no counters, in which players always build the top mix. One 3-cycle among 21 compositions is
the minimum evidence that counter-play exists.

**Fails if** there is no 3-cycle (the resolver is transitive), or if any composition is dominant.

**Fixed by construction:** C1 has no edges at all (every `p = 0.5`), so no cycle, so it fails. The
entry names this as the metric on which C5 is most at risk, so T59 should report C5's cycle count
and any dominant composition prominently.

### 8.3 Upset rate (UR)

**Procedure.** Mirror matchups: the same composition on both sides, for the 5 pure compositions and
the uniform mix, at T-scale. The stronger side has 40,000 troops and the weaker side `κ × 40,000`,
split by the §8.0 rule, for `κ ∈ {0.90, 0.80, 0.67, 0.50}`. Each pair is played with the weaker side
attacking and with it defending, at 200 seeds each, so there are 2,400 battles per `κ`. `UR(κ)` is
the fraction of them the weaker side wins. Because both sides have the same composition, the only
difference is size, so `UR` measures luck, not matchup.

**Pass band.** It **passes if every one** of the following holds:
- `UR(0.90) ∈ [0.10, 0.45]`
- `UR(0.80) ∈ [0.03, 0.35]`
- `UR(0.67) ∈ [0.005, 0.20]`
- `UR(0.50) ∈ [0, 0.08]`
- `UR` does not rise as `κ` falls, with a tolerance of 0.02.

**Why.** An army 10% smaller should win sometimes, or the outcome of every battle is known before it
is fought, and it should win clearly less often than a coin flip. An army half the size should almost
never win. Each band is wide because only the shape is being judged.

**Fails if** any bound or the monotonicity is violated. A deterministic resolver fails at `κ = 0.90`
and `κ = 0.80`, and a resolver close to a coin flip fails the upper bounds.

**Fixed by construction:** C1 and C3 have `UR = 0` at every `κ`. Their winner is a deterministic
function of the armies, and the smaller mirror army is strictly weaker in both `armyPower` and `E`.
Both therefore **fail UR**. This is stated now so that it is not mistaken for a measurement.

### 8.4 Casualty differentiation (CD)

**Procedure.** Use the P-scale battles in which the **winner's** composition contains all five
types: the uniform mix and the 5 one-heavy mixes, against all 21 opponents, in both seats, at 200
seeds. For each such battle and each type `t` in the winner:

- `L_t = (start_t − end_t) / start_t`
- `end_t` counts every surviving troop of type `t`: C5's rejoined troops (D45) are included, and C2's
  routed units count as 0.
- `D = max_t L_t − min_t L_t` is the battle's spread.

**Two clauses, and both must pass:**
- **CD-a (spread):** the median `D` over those battles **≥ 0.15**.
- **CD-b (the spread depends on the enemy):** for the uniform winner against each of the 21
  opponents, find the type with the highest mean `L_t` in those battles. That type counts as a
  **clear top** only if its mean exceeds the second-highest by at least 0.05. CD-b passes if **at
  least two different types** are clear tops across the 21 opponents.

**Why.** CD-a: fifteen points is the smallest spread at which "what you lose depends on what you
brought" is visible in one battle's result screen. For scale, the baseline's own spread is about 1
point ([`instant-cannot`][instant-cannot]), and the three observed tactical battles with a per-type
breakdown show 55 to 96 points (96, 55 and 74; §2.3).
CD-b: a spread whose ordering is the same against every enemy is a fixed per-type toughness, not a
matchup. The 0.05 margin keeps noise from passing it.

**Fails if** the median spread is below 15 points, or if the same type (or no clear type) is
always the most-lost.

**Fixed by construction:** C1's spread comes only from the `[105, 120)` divisor draw, about 1 point
([`instant-cannot`][instant-cannot]), so it **fails CD-a**. Its per-type means are equal up to that
noise, so it has no clear tops and **fails CD-b**.

> **Measured, 2026-09-23 (T59, #250, 33dc106):** this prediction predates T63. With T63's small-unit
> deletion pass merged, C1 **does** show a clear top type in CD-b: light cavalry, because the pass deletes
> the uniform army's 1,000-troop light-cavalry unit. C1 still fails CD-b as scored. See
> [`auto-resolve-tournament-results.md`](auto-resolve-tournament-results.md).

### 8.5 Determinism (DET), and the draw count

**Procedure.** Take the first 100 battles of the §8.1 CS-P schedule, in order. Run each twice, in
two separate processes, with the same seed. Serialize each output record (§3: `winner`, `after`,
`survivors`, `ending`, `draws`) as canonical JSON, with keys sorted and no whitespace. Draws are
counted by a wrapper around `IRng` that increments on every call.

**Pass band.** It **passes if** all 100 pairs are byte-identical **and**, for all 100 battles, the
counted draws equal the count the candidate states in §6. For C1 and C3, that is the fixed formula
from the unit counts. For C2, C4 and C5, it is the per-event formula evaluated on the battle's own
event log. It also requires that the candidate draw from no randomness source other than the `IRng`
it is handed, which T59's review checks in the code.

**Why.** T59's hazard: *"a tournament that cannot be rerun is an anecdote"*. The draw-count check
catches any hidden or unlogged randomness.

**Fails if** any pair differs, if any count differs from the stated formula, or if any other
randomness source exists.

**Stated draw counts:** C1 §6.1, C2 §6.2, C3 §6.3, C4 §6.4, C5 §6.5.5. Of these, only C1 and C3 have
a **fixed** count per battle. The others are variable, and are stated per event.

### 8.6 Cost (COST), against T22's soak budget

**Procedure.**
- **Per-battle time.** In a Release build, time every battle of the full CS-P schedule after 1,000
  warm-up battles. Report the mean `t_c` and the 99th percentile `p99_c` of wall-clock time per
  battle for each candidate. `t_1` is C1's mean.
- **Soak baseline.** Run the merged soak (`AiSoakTests`, 50 seeds, `SoakTurnCap = 1200`) once on the
  same machine. Record its elapsed time `E0`, which the soak prints as *"… s of a 300s budget"*, and
  the number of field battles `B` resolved across the 50 seeds, counted from the battle events at one
  per `ResolveField` call.
- **Projection.** A candidate's projected soak time is `E_c = E0 + B × (t_c − t_1)`.

**Pass band.** It **passes if** `E_c ≤ 240 s` **and** `p99_c ≤ 50 ms`.

**Why.**
- T22 **asserts** a 300-second budget for the whole soak ([T22](../tasks/T22.md) Done-when 2). A
  candidate that would push the soak past it cannot ship, and 240 seconds leaves 20% of the budget
  as headroom for CI variance.
- The 50 ms per-battle ceiling exists because a real game resolves more battles per turn than the
  toy soak does, and a player waits on each one at turn end. 50 ms is a designed bound on a battle
  being noticeable.

**Fails if** either bound is exceeded. If `B = 0`, T59 reports that and applies the p99 bound alone.

**Fixed by construction:** none. Note that C1 passes the first clause only if the merged soak itself
runs inside 240 seconds. If it does not, that is a finding about the soak, not about C1.

### 8.7 Ending: collapse versus annihilation (EN)

**Applies to** the candidates with a break or withdrawal rule that can **end** a battle: C2, C4 and
C5. It does not apply to C1 (no break rule) or C3 (its floor removes units but never ends a battle).
Both are recorded as *not applicable*, not as a pass.

**Procedure.** For every P-scale battle, classify the ending:

| `ending` | Meaning |
| --- | --- |
| `annihilation` | The loser's last live unit left through the **strength floor** (K08), or its troops reached 0. For C4: a side reached 0 troops. |
| `collapse` | The loser's last live unit left through a **morale** break: the floor (K09), the band (K11), or the cascade (K13). For C4: the army pool broke (`P ≤ 0`). |
| `withdrawal` | C5's ordered army-level withdrawal (§6.5.3). |
| `cap` | The round cap decided the battle (D09, D33). |

Also record whether the battle had **at least one cascade break**, meaning a unit broken by K13's
<30 removal. This applies to C2 and C5 only, since C4 has no cascade.

**Pass band.** It **passes if** every applicable clause holds:
- **EN-a:** the `cap` fraction is ≤ 0.05.
- **EN-b:** the share of `collapse` + `withdrawal` among decisive battles is in `[0.10, 0.90]`.
- **EN-c** (C2 and C5): the fraction of battles with at least one cascade break is in `[0.10, 0.90]`.

**Why.** Done-when 5: *"a cascade that never fires and one that fires every time are both
failures"*. A mechanism that never fires is dead weight. One that always fires makes every battle end
the same way, and gives neither player anything to read or anticipate. The cap clause exists
because a cap that decides battles means the model does not terminate on its own terms.

**EN-b counts `withdrawal` with `collapse`, and that is deliberate.** It means a C5 that *never*
ends a battle by annihilation fails EN-b's upper bound, even though fewer annihilations is the
direction of the user's objection (*"I do not like that an army is completely destroyed"*). The band
encodes a design judgement: a model in which no battle is ever fought to the end has lost the
decisive-victory outcome altogether, and "sometimes" is what the suite asks for. Under C5 the
annihilated side's last unit still flees with its few remaining troops (§6.5), so annihilation there
does not mean total loss. If the user's intent is instead "never", this band conflicts with it. **That
conflict is for the user to settle, not T59**, and T59 must report the number rather than move the
band.

**Fails if** any applicable clause is outside its band.

**Fixed by construction:**
- **C4 fails EN-b, essentially always.** C4's `annihilation` needs a side to reach 0 troops while its
  pool is still above 0. The pool breaks once cumulative losses reach `M / 2` percent (D32), which is
  25–35% at `M = 51…70` and 29.5% at the §8.0 `M = 59`. So a side can only be annihilated first by
  losing more than about 70% of its troops in a single round. At D31's pace, P-scale battles do not
  do that. C4's endings are therefore `collapse` (or `cap`) in essentially every battle. This follows
  from C4 being an army-morale model, and it is stated here so that it is not read as a discovery.
  **Measured, 2026-09-23 (T59, #250, 33dc106): the prediction did not hold.** C4 **passes** EN-b
  (0.8223), because 17.8% of its battles end at the 30-round cap rather than by collapse; it fails EN-a
  instead. See [`auto-resolve-tournament-results.md`](auto-resolve-tournament-results.md).
- **C5, expected direction only (not a construction result):** the withdrawal test (60% relative
  strength, from round 3) pushes C5's endings toward `withdrawal`. Whether that pushes it past EN-b's
  0.90 is what T59 measures.

### 8.8 Survivors: fraction and composition (SV)

**Applies under `onDefeat = scatter`** to every candidate that produces survivors. C1 is included as
the reference, since its mirrored ratio is the mechanism C5 is meant to give meaning to. Done-when 5
asks for this metric for candidates with a break or withdrawal rule, and those are all covered;
measuring C1 as well is an addition.

**Procedure.** On the P-scale battles, for each decisive battle:
- `σ` = the loser's surviving troops ÷ the loser's starting troops.
- `ω` = the winner's troops lost ÷ the winner's starting troops. This counts C5's rejoined troops as
  kept and C2's routed winner units as lost.
- `τ` = the total-variation distance between the loser's **surviving** type mix and its **starting**
  type mix, `Σ_t |surv_t / surv − start_t / start| / 2` (0 when the survivors have the starting mix).
  If there are no survivors, `τ` is undefined and the battle is excluded from SV-d.

For SV-c, the battles are split by the **winner's** cavalry (light plus heavy) share of weighted
power:
- **H**: the share is ≥ 40%. That covers pure LC, pure HC, the seven pairs containing a cavalry
  type, the uniform mix, and the two cavalry-heavy mixes: 12 compositions.
- **Z**: the share is zero. That covers pure LI, HI and A, and the three pairs among them: 6
  compositions.

**Four clauses, and all must pass:**
- **SV-a (a force in being):** the median `σ` is ≥ 0.10.
- **SV-b (defeat costs more than victory):** the median `(1 − σ)` minus the median `ω` is ≥ 0.10.
- **SV-c (who is chasing matters):** the mean `σ` over group Z minus the mean `σ` over group H is
  ≥ 0.05. Losing to a cavalry-rich army must leave fewer survivors than losing to one with no
  cavalry.
- **SV-d (survivors are an outcome, not a fraction):** the mean `τ` is ≥ 0.05.

**Why.** Done-when 2 and 5: survivors are what `scatter` consumes, and C5's claim is that how many
survive, and of which types, becomes an outcome of the battle.
- SV-a fails pure annihilation.
- SV-b fails a model in which losing is no worse than winning.
- SV-c tests the steer's pursuit claim directly.
- SV-d fails a flat fraction.

**Fails if** any clause fails. In addition, T59 reports `σ` broken down by `ending` (for §6.5.4
point 5) as a diagnostic with no band.

**Fixed by construction:**
- **C2, `σ = 0` in every battle** (§6.2), so it **fails SV-a**. This is the original's own
  annihilation.
- **C1's survivor mix equals its starting mix** up to the divisor noise, so `τ ≈ 0` and it **fails
  SV-d**.
- **C1 on P-scale armies:** the winner's ratio and the mirrored ratio are both exactly 40 (`R = R'`),
  so `(1 − σ) ≈ ω` and it **fails SV-b**. The same ratio holds whatever the winner's composition,
  so group Z and group H have the same expected `σ`, and it **fails SV-c**.

### 8.9 The suite at a glance

| ID | Measures | Pass band (fails outside it) | Applies to |
| --- | --- | --- | --- |
| CS-T | win-rate spread across mixes, troops held equal | ≥ 0.20 | all |
| CS-P | win-rate spread across mixes, baseline power held equal | ≥ 0.20 | all |
| NT | counters exist, and no mix dominates | ≥ 1 three-cycle **and** no dominant mix | all |
| UR | how often the smaller mirror army wins | `κ` 0.9: 10–45% · 0.8: 3–35% · 0.67: 0.5–20% · 0.5: 0–8%; non-increasing | all |
| CD | per-type loss spread in the winner, and whether it depends on the enemy | median spread ≥ 15 pp **and** ≥ 2 distinct clear-top types | all |
| DET | same seed gives the same bytes; draw count as stated | 100/100 **and** 100/100 **and** no other randomness source | all |
| COST | T22 soak projection and per-battle p99 | `E_c ≤ 240 s` **and** p99 ≤ 50 ms | all |
| EN | how battles end | cap ≤ 5%; collapse + withdrawal 10–90%; cascade fires in 10–90% | C2, C4, C5 |
| SV | the survivors `scatter` receives | median σ ≥ 0.10; `(1−σ) − ω ≥ 0.10`; `σ(Z) − σ(H) ≥ 0.05`; mean τ ≥ 0.05 | C1, C2, C3, C4, C5 under `scatter` |

## 9. The smoke test: the observed tactical battles

> **One instance cannot validate a model, and it must not be tuned against.** The battle below is
> one engagement. It was fought tactically, on a patched EXE, with unrecorded quality and morale. Its
> `ratio ≈ 46` was recovered by sweeping past morale and quality rather than by measuring them, and
> that ratio lies outside the baseline's own reachable range (§2.4). The same save refought gave a
> different result, by a third of the total loss ([`rout`][rout]). **No constant in this document
> was set from it, and T59 must not change any constant because of it.** It is recorded as a smoke
> test: a check that a candidate runs, behaves sanely and produces the *kind* of outcome it claims.
> It is not a target.

### 9.1 The Done-when 7 instance: Seleucid 45,100 against Ptolemaic 27,700

**[confirmed: [`ptolemy`][ptolemy] §3, `IP1 000` t=25]**

| Type | Seleucid start | Seleucid finish | Seleucid loss | Ptolemaic start | Ptolemaic finish |
| --- | ---: | ---: | ---: | ---: | ---: |
| Light infantry | 27,300 | 17,025 | 37.6% | 21,300 | 0 |
| Heavy infantry | 8,400 | 5,916 | 29.6% | 3,200 | 0 |
| Archers | 5,200 | 0 | 100.0% | 0 | 0 |
| Light cavalry | 1,800 | 1,452 | 19.3% | 2,300 | 0 |
| Heavy cavalry | 2,400 | 2,303 | 4.0% | 900 | 0 |
| **Total** | **45,100** | **26,696** | **40.8%** | **27,700** | **0** |

**Aggregate calibration, as recorded:** the merged per-unit loss expression reproduces the 40.8%
total at `ratio ≈ 46`, with a per-type spread of 1.2 points against the observed 96
([`instant-cannot`][instant-cannot]). As §2.4 shows, that ratio is not one the baseline can reach
with Seleucid as the winner. The report now says so itself (research `54b85d0`). The ratio is at most
40, and 14–27 for these two armies over strategic morale 51–70, which means a winner's loss of about
12–24%. So `ratio ≈ 46` is not a calibration of `armyPower`. It measures how far this tactical outcome
exceeds the instant resolver's ceiling, which is about 35.5%, reached only at a tie.

**Inputs for the smoke run.** The report gives only type totals and names no pre-battle save for this
battle (its save references are `IP021.sav` and other play-throughs; searched: `.sav` in the report).
So every per-unit input here is **[designed]**:
- Units are split by the §8.0 rule. Seleucid gets 8 units: LI 15,000 + 12,300; HI 6,000 + 2,400;
  A 3,500 + 1,700; LC 1,800; HC 2,400. Ptolemaic gets 5 units: LI 15,000 + 6,300; HI 3,200;
  LC 2,300; HC 900.
- Quality is 6 and `M = 59` on both sides.
- Seleucid attacks. The report does not say which side attacked.

### 9.2 Rome against Gaul, `1_rome_270_winter_7`, fought twice from one save

**[confirmed: [`rome-gaul`][rome-gaul] (first fight), [`rout`][rout] (second fight)]**

| Type | Rome start | Rome finish, first fight | Gaul start | Gaul finish |
| --- | ---: | ---: | ---: | ---: |
| Light infantry | 45,087 | 26,032 | 57,973 | 0 |
| Heavy infantry | 28,057 | 20,675 | 7,635 | 0 |
| Archers | 16,856 | 11,883 | 3,867 | 0 |
| Light cavalry | 6,695 | 4,692 | 10,899 | 0 |
| Heavy cavalry | 3,187 | **0** | 2,974 | 0 |
| **Total** | **99,882** | **63,282** | **83,348** | **0** |

The second fight reached **75,536** from the same 99,882, with a different set of destroyed units.
That report gives no per-type breakdown for it.

**Inputs for the smoke run: read from the save, not designed.** The pre-battle save
`1_rome_270_winter_7.sav` holds both armies. It is in the local asset corpus, and, per issue #207, in
`ic2-test-fixtures`, which ships the whole corpus. (`FixtureResolutionTests` names its sibling
`1_rome_270_winter_7_b.sav`.) T59 reads, through `IC2.Data`'s army table, Rome's army 0 at (90,28)
and Gaul's army 13 at (88,25) ([`rome-gaul`][rome-gaul] §"The save-level result"). For every unit it
takes the type, troops and quality from the save, and for each army it takes strategic `M` from
`+14`. **[confirmed: save data]**

Before running the instance, T59 asserts that the roster is the one the reports describe:
- Rome's and Gaul's per-type totals equal the table above.
- Rome's two heavy-cavalry units are **755** and **2,432** troops ([`rome-gaul`][rome-gaul]
  §"Unit-level detail" and the correction note), and 4th Bowmen has 3,312 ([`rome-gaul`][rome-gaul]).
- Rome's slot qualities match [`morale-array`][morale-array] §"The quality-promotion rule":
  - slots 1, 2 and 12 (2nd, 3rd and 6th Guards) are good (slot 1, 2nd Guards, heavy infantry,
    4,782 troops, was restored to the report's table in research `1762c84`);
  - slot 4 (4th Guards) is elite;
  - slot 15 (1st Guards) is very good;
  - slots 0, 6, 7, 8, 9, 13, 16, 17 and 18 are average.
- Rome's `M = 68` ([`morale-array`][morale-array]; this repository's `ModelCoverageTests` reads the
  same 68 for both Roman armies).

For the record, not as an assertion: Gaul's pre-battle `+14` is **62**, and in the original only
Gaul, the computer side, received K27's +3 (62 → 65, read from Gaul's tombstoned record in
`1_rome_270_winter_7_b.sav`). Rome, the human side, seeded its tactical morale from 68 unchanged
([`morale-array`][morale-array] §"The morale formula", research `1762c84`). C2's and C5's K27
placeholder instead seeds Rome from 71 and Gaul from 65 (§6.2).

If the save cannot be resolved, or any assertion fails, T59 reports it and **does not** run the
instance on substitute data.

The earlier draft of this section built the roster with the §8.0 split, and that would have been
wrong. It turns Rome's heavy cavalry into `2,500 + 687` and removes the condition the reports name
as a candidate for one break: a 755-troop unit sitting at 30% of a battalion (§2.3). Designing a roster the reports partly give
is what design-audit.md §4.5 forbids.

**Attacker:** Rome. **[derived]**: Rome's army moved onto the battle tile ([`rome-gaul`][rome-gaul]).

### 9.3 Rome against Gaul, `7.sav → 8.sav`: the victor that lost no whole arm

**[confirmed: [`battle-observation.md`][observation] §"Battle result"]**

| Type | Rome start | Rome finish | Rome loss | Gaul start | Gaul finish |
| --- | ---: | ---: | ---: | ---: | ---: |
| Light infantry | 8,900 | 4,282 | 51.9% | 55,518 | 0 |
| Heavy infantry | 34,700 | 32,392 | 6.7% | 4,531 | 0 |
| Archers | 0 | 0 | — | 0 | 0 |
| Light cavalry | 2,400 | 914 | 61.9% | 1,298 | 0 |
| Heavy cavalry | 4,700 | 2,353 | 49.9% | 555 | 0 |
| **Total** | **50,700** | **39,941** | **21.2%** | **61,902** | **0** |

This is a different battle from §9.2 (different totals, and a recording from 2026-09-12). It is the
only fully tabulated outcome in which the victor lost no whole arm, so it is the natural foil to
§9.1 and §9.2.

**Inputs for the smoke run.** `7.sav` and `8.sav` are both in the committed corpus table
(`tests/IC2.Data.Tests/CorpusFixtures/expected-corpus-outcomes.json`), but no report has identified
which army records fought ([`observation`][observation] Next checks 1). T59 therefore runs one
exact lookup:
- **If exactly one army per nation** in `7.sav` has per-type totals equal to the start columns
  above, T59 takes those two armies' rosters, qualities and `+14` morale. **[confirmed by exact
  match]** 
- **Otherwise**, it falls back to the §8.0 split, quality 6 and `M = 59`, all **[designed]**.
  [`observation`][observation] does give three Roman units' qualities: 1st Lancers, average (`7.7.png`);
  2nd Guards, good (00:22); 3rd Guards, good (08:04). The fallback cannot use them, because the
  §8.0 split builds units that do not correspond to named units, and it applies quality 6
  throughout.

T59 reports which path it took. Rome attacks, **[designed]**: the report does not say which side
attacked.

### 9.4 Procedure, and what the smoke test can and cannot fail

**Run.** Each candidate plays each instance for seeds `k = 0 … 999`.

**The smoke test fails a candidate only on these conditions** (none of them is about the outcome):
- any exception;
- any unit ending with negative troops, or with more troops than it started with (C5's rejoined
  troops included);
- an incomplete output record (§3);
- any of the first 10 seeds failing the §8.5 byte-identity check.

**Reported beside the observations, with no band:**
- the fraction of seeds the observed winner wins;
- the 5th, 50th and 95th percentiles of the winner's per-type loss rate and of its total loss;
- for §9.2, where 63,282 and 75,536 fall in the candidate's distribution of Rome's final total;
- for §9.3, where 39,941 falls in that distribution;
- the fraction of seeds in which the winner loses one whole arm, and which arm.

A candidate that never produces the observed winner is **not** thereby failed. Some inputs are
designed, and none of the battles records the battle-local tactical morale that drove it.

## 10. Open items that would convert `[designed]` to `[confirmed]`

These are the RE questions whose answers would replace placeholders in C2 and C5. None was worked
on here, because this task produces no new evidence. Whether to commission any of them is the main
session's call, and T59 does not need any of them to run.

| Item | Replaces | Where to look |
| --- | --- | --- |
| The tactical AI's move and target rules: `FUN_00439ce8`'s untraced branch `FUN_0043a31c` (an AI mode or the human-move path, still open), and wherever the AI's choices actually live | D03, D04, D05 | [`formula`][formula] Next checks 3 |
| `FUN_004381a4`'s type-order lookup table and column block | D01 | [`formula`][formula] §"The AI dispatch chain" |
| `gridDistance` inside `FUN_0043845c`, and any maximum shooting distance | D06 | [`rout`][rout] §"`+0x20` … identified" |
| The operands of the ±2/−3 comparison in `FUN_004393ec` | D07 | [`formula`][formula] §"Melee" |
| `FUN_00438420`'s body (`focusCount`) | D12 | [`rout`][rout] §"Two small corrections" |
| The cascade's −6 and +5, observed rather than only decompiled | (strengthens K12–K14) | [`rout`][rout] Next checks 2 |
| `FUN_0040284c`, the RNG's range semantics | (strengthens the `Random(n)` reading, §3) | [`formula`][formula] §"What this does not establish" |
| ~~Which army records in `7.sav` fought the `7.sav → 8.sav` battle~~ **Answered 2026-09-23 by T59's exact-total lookup (#250, 33dc106) [derived]:** Rome's army 0 at (102,44) and Gaul's army 9 at (103,43) | §9.3 inputs | [`observation`][observation] Next checks 1 |

**Answered since this document merged** (the research pass for
[#288](https://github.com/diegoami/imperial_conquest_2/issues/288), 2026-09-23). Both rows are
removed from the table above:
- **The initial-morale clamp (D08)** is `[60, 90]`: `min(90, ·)` then `max(60, ·)`. D08 is now
  `[confirmed]` (research `1762c84`).
- **The +3 on battle entry (K27)** goes to a computer-controlled side only, and depends on the army's
  own nation, not the opponent. It is written to `+14`, unclamped, once per battle. Rome's 68 → 66 is
  the supply tick. K27 is now `[confirmed]`, and its C2/C5 placeholder is a stated departure
  (research `1762c84`).
- **Not asked here, found alongside:** the cascade is one level deep (§5, research `eb1c886`), and the
  instant path deletes small units (§6.1, research `54b85d0`).

## 11. What was searched

This is the search behind every `[designed]` tag here, per
[design-audit.md §4.5](../design-audit.md#45-one-structural-note-on-the-harness). It covers the
research repository at `origin/main`, read without touching its working tree.

**Read in full:**
- [`instant-resolver-cannot-reproduce-a-tactical-battle.md`][instant-cannot]
- [`battle-replayed-rout-mechanic-and-combat-constants.md`][rout]
- [`combat-type-effectiveness-matrix.md`][matrix]
- [`decompiled-combat-formula-structure.md`][formula]
- [`battle-quality-promotion-and-morale-array-decompiled.md`][morale-array]
- [`unit-type-stat-table-in-dat.md`][unit-table]
- [`full-battle-resolution-rome-vs-gaul.md`][rome-gaul]

**Read by section:**
- [`decompiled-diplomacy-peace-terms-and-instant-battles.md`][instant] (§"The original's instant
  battle resolver", §"What this does not establish")
- [`ptolemy-run-ui-inventory-and-leader-draw.md`][ptolemy] §3
- [`supply-driven-morale-and-fleet-attrition.md`][supply-morale] (the morale-write index)
- [`battle-code-entry-points.md`][entry-points]
- [`battle-observation.md`][observation] (the timeline and grid)

**Searched across the research repository's `docs/`**, case-insensitive:

| Term | Result |
| --- | --- |
| `FUN_0043a31c` | Only [`formula`][formula], as an untraced next check |
| `pursu` | 4 files, none about battle pursuit ("pursue"/"not pursued" in the ordinary sense: movement, supply, recruitment, city capture) |
| `retreat` | 1 hit: [`rout`][rout], on the cascade as the reason there is *no* fighting retreat |
| `withdraw` | 6 files, none a battle withdrawal (a withdrawn promotion rule, city-stock withdrawals, the auto-resupply path, movement) |
| `surrender` | `TBattleMap_Surrender` as a named human action; no AI trigger, no terms |
| `partial defeat` | none |
| `target select` | none |
| `gridDistance` | only the shooting helper's `< range` test in [`rout`][rout], with no definition |
| `initiative`, `turn order` | alternating sides observed ([`observation`][observation], [`rome-gaul`][rome-gaul]); no first-mover rule |
| `lookup table` | placement's type order is named in [`formula`][formula] and not extracted |
| `focusCount`, `FUN_00438420` | the multipliers and the `min(4, ·)` ([`rout`][rout]); no published body |
| `then 60`, `initial morale`, `FUN_00437de4` | only the one formula line in [`morale-array`][morale-array] |

**In this repository:**
- [`game-design.md` §Combat and §"The defeated side's fate"](../game-design.md). These describe the
  merged resolver and the reserve, and the scatter search already recorded in K06.
- `src/IC2.Engine/Battle/` (`InstantBattleResolver`, `BattleCasualties`, `ScatterPlacement`) and
  `src/IC2.Engine/Strength/ArmyPower.cs`, for the confirmed formulas as merged.
- `data/rulesets/*.json` `.combat` and `.unitTypes`, for the values and their provenance strings.
- [`T22`](../tasks/T22.md) Done-when 2 and `AiSoakTests`/`AiTestbed`, for the budget.
- [`T59`](../tasks/T59.md), for what this document must give its implementer.

No battle-withdrawal, pursuit or retreat mechanic, no partial-defeat outcome, and no army-level
break exists anywhere in the corpus. Every C4 and C5 army-level constant, and every C5 flight
constant, is therefore `[designed]` with this search as its record.

<!-- sources: research-repository reports, cited by GitHub URL like the rest of this directory -->

[instant-cannot]: https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/instant-resolver-cannot-reproduce-a-tactical-battle.md
[rout]: https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/battle-replayed-rout-mechanic-and-combat-constants.md
[matrix]: https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/combat-type-effectiveness-matrix.md
[formula]: https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-combat-formula-structure.md
[morale-array]: https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/battle-quality-promotion-and-morale-array-decompiled.md
[instant]: https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-diplomacy-peace-terms-and-instant-battles.md
[unit-table]: https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/unit-type-stat-table-in-dat.md
[rome-gaul]: https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/full-battle-resolution-rome-vs-gaul.md
[ptolemy]: https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/ptolemy-run-ui-inventory-and-leader-draw.md
[supply-morale]: https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/supply-driven-morale-and-fleet-attrition.md
[entry-points]: https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/battle-code-entry-points.md
[siege-attrition]: https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-defection-and-siege-attrition.md
[observation]: https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/battle-observation.md
