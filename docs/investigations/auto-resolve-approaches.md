# Composition-aware auto-resolve: five candidate models, and how to judge them

**Status: specification, not a decision.** This document specifies five battle-resolution models
precisely enough for T59 ([#250](https://github.com/diegoami/imperial_conquest_2/issues/250),
[`docs/tasks/T59.md`](../tasks/T59.md)) to implement and measure, and it defines the metric suite
T59 measures them with. It **does not pick a winner and does not rank the candidates.** T59 measures
them, and the user decides from T59's scorecard. Task T58, [#251](https://github.com/diegoami/imperial_conquest_2/issues/251).

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
  (`ResolveField`). Naval (`FUN_0044B5D0`) and siege (`FUN_0044B27C`) stay as merged.
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

- The tactical AI's move generation is `FUN_00439ce8`, which dispatches to two modes. One mode
  (`FUN_004381a4`) is only unit placement. The other (`FUN_0043a31c`) has never been traced
  ([`decompiled-combat-formula-structure.md`][formula] §"The AI dispatch chain" and Next checks 3).
- No report contains target selection, movement rules on the grid, the maximum shooting distance, or
  the placement type-order lookup table.
- The exact clamp on initial tactical morale is reported only as *"upper bounds ≈ 90 then 60"*
  ([`battle-quality-promotion-and-morale-array-decompiled.md`][morale-array]).

C2 is specified below with each of these as an explicit `[designed]` placeholder (D01–D12), and each
names what was searched. So C2 is **the original's arithmetic driven by a designed driver**, and T59
should read its measurements that way. §10 lists the RE passes that would replace the placeholders.

### 2.3 There are three observed tactical outcomes, and they disagree about heavy cavalry

The entry calls the Seleucid–Ptolemaic engagement *"the one observed tactical battle"*. The
research corpus holds three tactical outcomes: that battle
([`ptolemy-run-ui-inventory-and-leader-draw.md`][ptolemy] §3), and the Rome–Gaul battle fought
**twice** from the identical save ([`full-battle-resolution-rome-vs-gaul.md`][rome-gaul],
[`battle-replayed-rout-mechanic-and-combat-constants.md`][rout]).

In Rome–Gaul's first fight, the **winner's heavy cavalry went `3,187 → 0`**, a 100% loss, while
every other Roman type kept 57–74%. That result **supports** the entry's central reading, that a
victorious army can lose one whole arm by breaking rather than by attrition: both Roman heavy-cavalry
units were removed by the rout check, per the correction in [`rout`][rout]. It **contradicts** the
entry's supporting remark that heavy cavalry survives because *"it has the lowest absolute rout
threshold of any type (100) and is the least likely to reach it"*. In the other battle, heavy
cavalry was the arm that broke.

The two battles give opposite per-type orderings. That is one more reason, beyond Done-when 7's own,
that no single battle can be a target. §7 and §9 use all three outcomes as smoke instances and tune
to none.

### 2.4 `ratio ≈ 46` lies outside the baseline's own reachable range

Done-when 7 asks for `ratio ≈ 46` to be recorded as the aggregate calibration, and §9 records it. It
comes with one arithmetic fact, which the source report does not state. In the merged winner rule,
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
model cannot produce it with its own inputs. (The sums above use type totals, because the battle's
per-unit rosters are not in the report. Per-unit truncation moves them by less than 0.1%.)

## 3. The common seam

Every candidate is a function with the same signature, so T59 can wrap the merged resolver unchanged
as C1 and put the other four behind the same seam.

**Input**, identical for every candidate:

| Input | Meaning | Source |
| --- | --- | --- |
| `attacker`, `defender` | Two armies. Each is an ordered list of at most 20 units (K31) of `(type, troops, quality)`, plus the army's **strategic** morale `M` (army record `+14`). | the merged `ArmyState` |
| `ruleset` | `unitTypes[]` (K01, K07, K20, K22–K24) and the `combat` block (K02–K06) | `data/rulesets/*.json` |
| `rng` | the battle's `IRng`. Every draw goes through it, in the order the candidate states. | merged `IRng` |
| `onDefeat` | `destroy` (`classical-faithful`) or `scatter` (`improved`) | `flags.combatOnDefeat` |

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
point, truncated. The original computes troops in shorts and its products in 32 bits. No product
in §6 exceeds 2³¹ at legal inputs (the largest is the shooting base, below 2.5 × 10⁸), so 64-bit
arithmetic reproduces the 32-bit results.

**`Random(n)`** means `rng.NextInt(0, n)`, a uniform integer in `[0, n)`, and `Random(0)` returns 0
without a draw (D13). The half-open reading is the one the merged `BattleCasualties` already uses for
`Random(15) + 105 ∈ [105, 120)`. The original's RNG (`FUN_0040284c`) is *"assumed to be a bounded
uniform RNG call from its usage pattern, not independently verified"*
([`formula`][formula], §"What this does not establish"). **[derived]**

## 4. Constants register

Done-when 1 requires every constant to be named from a report or marked `[designed]` with its search.
This table lists them all. The candidate sections use these IDs, and the **Used by** column shows
which candidate depends on which constant. `K` rows are sourced from reports. `D` rows are
`[designed]` (or `[derived]`/`[open]` where stated), and each names its search; §11 lists the full
search.

### 4.1 Sourced constants

| ID | Constant | Value | Tag and source | Used by |
| --- | --- | --- | --- | --- |
| K01 | `combatPowerWeight[type]`, unit-type table `+0x26` | LI 20 · HI 100 · A 40 · LC 60 · HC 120 | [confirmed] [`unit-type-stat-table-in-dat.md`][unit-table] 2026-09-19 update; ruleset `unitTypes[].combatPowerWeight` | C1, C5 (D34), §8 |
| K02 | `armyPower = (Σ weight × troops / 100) / 80 × M` | divisors 100, 80 | [confirmed] [`instant`][instant] §"The original's instant battle resolver" (`FUN_0044A8CC`); ruleset `combat.powerTroopDivisor`, `powerDivisor` | C1, §8 |
| K03 | winner casualty ratio `loserPower × 40 / winnerPower` | numerator 40 | [confirmed] [`instant`][instant]; ruleset `combat.winnerCasualtyNumerator` | C1, C3 |
| K04 | per-unit loss `troops / (Random(15) + 105) × ratio` | 105, span 15 | [confirmed] [`decompiled-defection-and-siege-attrition.md`][siege-attrition] via `BattleCasualties`; ruleset `casualtyDivisorBase`, `casualtyDivisorRandomSpan` | C1, C3 |
| K05 | winner rule: higher power wins, **ties to the defender** | — | [confirmed] [`instant`][instant] (`winner = (pB < pA) ? attacker : defender`) | C1, C3; tie rule reused by C2, C4, C5 |
| K06 | mirrored survivor numerator, scatter distance | 40; 2–4 tiles | **[designed]** already, in the merged ruleset (`combat.scatteredDefeat._provenance`): searched `docs/reports/` for any partial-defeat outcome in the original, found none | C1 |
| K07 | `standardBattalionSize[type]`, `+0x1A` | LI 15,000 · HI 6,000 · A 3,500 · LC 7,000 · HC 2,500 | [confirmed] [`unit-table`][unit-table] | §5 (K08), §8 army construction |
| K08 | rout strength floor `standardBattalionSize / 25` | LI 600 · HI 240 · A 140 · LC 280 · HC 100 | [confirmed] [`rout`][rout] §"The rout mechanic" (`FUN_00438fb0`); both observed routs land on it | C2, C3 (variant), C5 |
| K09 | rout morale floor: routs unless `m > 19` | 19 | [confirmed] [`rout`][rout] | C2, C5 |
| K10 | safe outright if `m > 39` | 39 | [confirmed] [`rout`][rout] | C2, C5 |
| K11 | band check: survives if `Random(m) + Random(m) > 29` | 29 | [confirmed] [`rout`][rout]; *whether the branch fired in either recorded battle is unknown* (same report, §"What this does not establish") | C2, C5 |
| K12 | cascade: every surviving friend `m −= 6` | 6 | [confirmed, decompiled; **never observed**] [`rout`][rout] Next checks 2 | C2, C5 |
| K13 | cascade re-rout: a friend routs if its `m < 30` after the −6 | 30 | [confirmed, decompiled] [`rout`][rout] | C2, C5 |
| K14 | reward: every enemy `m = min(99, m + 5)`, and its target is cleared if it was the routed unit | 5, 99 | [confirmed, decompiled] [`rout`][rout] | C2, C5 |
| K15 | effectiveness matrix `value[attackerType][defenderType]` | the 25 values in §4.3 | [confirmed] values: [`combat-type-effectiveness-matrix.md`][matrix]; orientation: [`rout`][rout] §"axis ambiguity, resolved" | C2, C3, C4, C5 |
| K16 | melee exchange (`FUN_004393ec`) | the formula in §4.4: `/2000`, `+12`, `/12`, `/10`, `+1`, cap 30,000, cap `troops × 4 / 10`, `+1` | [confirmed] [`formula`][formula] §"Melee" plus its 2026-09 update; the 40% cap is exact on 11 observations, both sides ([`rout`][rout]) | C2, C5 |
| K17 | focus factor `defFactor = min(4, focusCount)`; attacker loss × `(5 − d)/5`, defender loss × `(2d + 5)/5` | 4 | [confirmed] [`rout`][rout] §"Two small corrections" (`FUN_00448fd0` is `min`) | C2, C5 |
| K18 | quality term in both power expressions: `q × 10 + m` | 10 | [confirmed] [`rout`][rout] | C2, C5 (with `m`); C3, C4 (with `M`, a departure) |
| K19 | tactical morale per melee exchange: `+2` to the better side, `−3` to the other; upper clamp 99 | +2, −3, 99 | [confirmed] [`formula`][formula], [`morale-array`][morale-array]. The *comparison* that picks "the better side" is not given: see D07 | C2, C5 |
| K20 | shooting vulnerability `vuln[targetType]`, `+0x20` | LI 18 · HI 2 · A 18 · LC 15 · HC 4 | [confirmed] [`rout`][rout] §"`+0x20` … identified" | C2, C3, C4, C5 |
| K21 | shooting exchange (`FUN_0043845c`, `FUN_0043910c`) | the formula in §4.4: `× 5 + 150000`, `×2` in range, `min(shooter/3, target/2)`, `+1` | [confirmed] [`rout`][rout] | C2, C4 (base only), C5 |
| K22 | shooting morale hit `m −= min(3, loss × 35 / (troopsAfter + 1))` | 35, 3 | [confirmed] [`rout`][rout] §"Two small corrections" | C2, C5 |
| K23 | `shots[type]`, `+0x1C`, a per-battle ammunition pool | LI 7 · HI 0 · A 25 · LC 9 · HC 0 | [confirmed] value: [`unit-table`][unit-table]. [derived] per-battle pool: the info panel read `Shots 19` mid-battle for an archer unit ([`rome-gaul`][rome-gaul]) | C2, C3, C4, C5 |
| K24 | `range[type]`, `+0x1E` | LI 1 · HI 0 · A 2 · LC 1 · HC 0 | [confirmed] [`unit-table`][unit-table]; read by the shooting code for the doubling test | C2, C5 |
| K25 | `moves[type]`, `+0x18` | LI 4 · HI 2 · A 4 · LC 6 · HC 5 | [confirmed] value: [`unit-table`][unit-table]. **[derived]** as tactical moves per turn: the info panel shows a `Moves` line per unit (`Moves 4` for archers, matching; `Moves 1` for heavy infantry mid-turn) ([`rome-gaul`][rome-gaul]) | C2, C5 |
| K26 | initial tactical morale `m = Random(q × 4) + M` | 4 | [confirmed] [`morale-array`][morale-array] §"The morale formula" (`FUN_00437de4`); the clamp is **[open]**: see D08 | C2, C5 |
| K27 | strategic morale `M += 3` for each side on battle entry | 3 | [confirmed] [`supply-driven-morale-and-fleet-attrition.md`][supply-morale] (index of morale writes, lines 38084/38092) | C2, C5 (before K26) |
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
| D02 | First mover | The **defender** moves first. After that, sides alternate (K33). | **[derived]** from one observation: the Rome–Gaul header sequence reads *"Rome to place units"*, then *"Gaul to move units"*, then *"Rome to move units"*, and Rome was the attacker ([`rome-gaul`][rome-gaul]). Searched for a decompiled turn-order rule: none. | C2, C5 |
| D03 | Target selection | Each unit targets the nearest live enemy by Chebyshev distance. Ties go to the lowest enemy slot index. The target is re-chosen whenever it is cleared (K14) or the target is no longer live. | **[designed]** Searched for target selection or `FUN_0043a31c`: untraced ([`formula`][formula] Next checks 3). The info panel's *"Unit set to attack"* ([`rome-gaul`][rome-gaul]) confirms that a target *field* exists (`DAT_004a0356`, [`morale-array`][morale-array]), not the choice rule. | C2, C5 |
| D04 | Movement | A unit moves up to `moves[type]` (K25) steps. Each step goes to the 8-neighbour square that is in bounds, empty, and minimises Chebyshev distance to the target. Ties are broken in the fixed order N, NE, E, SE, S, SW, W, NW, with "N" meaning toward the enemy home row. Movement stops once the unit is adjacent to its target (distance 1) or no step reduces the distance. One unit per square. | **[designed]** Searched for grid movement or pathing rules: none (the entry-points report lists *"movement ranges"* as still to recover). | C2, C5 |
| D05 | One action per unit per side-turn | In slot order, each live unit does exactly one of the following, in this priority order. **(a)** If it is adjacent to its target, it is queued for this side-turn's melee pass. **(b)** Otherwise, if `shots > 0` (K23), `range > 0` (K24) and distance ≤ `range + 1`, it shoots its target once (D11). **(c)** Otherwise, it moves (D04), and if it ends adjacent to its target it is queued for melee. After all units have acted, the melee pass runs `FUN_004393ec` once over the queued attackers in slot order. | **[designed]** except the batched melee pass, which is **[confirmed]**: `FUN_004393ec` *"iterates the same up-to-20 unit slots; for each attacker with a live assigned target"* ([`formula`][formula]). Searched for the move/shoot/attack priority: none. | C2, C5 |
| D06 | Shooting distance and doubling | A shot is legal at Chebyshev distance `2 … range + 1`. It is doubled (K21) when `distance − 1 < range`, i.e. when the number of empty squares between the two units is less than `range`. | **[open]** The report's code says `if (gridDistance(shooter, target) < range[shooterType]) base *= 2` ([`rout`][rout]). With `range = 1` and distance ≥ 1, a literal Chebyshev reading would never double, so `gridDistance` is not plain Chebyshev distance, and its definition is not published. **[designed]** placeholder as stated. There is no report of a maximum shooting distance. | C2, C5 |
| D07 | "Better side" in the ±2/−3 rule | The attacker gets `+2` and the defender `−3` when `atkPower ≥ defPower` (the two K16 powers of that exchange). Otherwise the attacker gets `−3` and the defender `+2`. | **[designed]** reading of a confirmed rule. The report says only *"whichever side had the better troops/power ratio"* ([`formula`][formula]). Searched for the exact operands: none. | C2, C5 |
| D08 | Tactical morale clamps | Initial `m = min(90, Random(q × 4) + M)`, with `M` taken after K27's +3. During the battle, `m` is clamped to `[0, 99]`. | **[open]** The initial clamp is reported as *"upper bounds ≈ 90 then 60"* ([`morale-array`][morale-array]). "Then 60" is unexplained, and no lower bound is reported. The 99 upper bound is confirmed (K19, K14). **[designed]** placeholder: 90 upper and 0 lower, with "then 60" left unapplied. | C2, C5 |
| D09 | Round cap | 100 rounds (one round is one turn per side). At the cap, the side with the greater `liveP` (D34) wins, with ties to the defender (K05). Under C2, the loser's live units are then destroyed. Under C5, the loser performs an ordered withdrawal (§6.5). | **[designed]** The original has no cap: it fights until a side has no live units (K32). A headless run needs a cap so that a battle always terminates. Searched for surrender or timeout rules: `TBattleMap_Surrender` exists as a human action ([`entry-points`][entry-points]) and has no AI trigger. | C2, C5 |
| D10 | Rout-check order after a melee exchange | Attacker first, then defender. | **[designed]** The report says the check is called *"on both participants"* ([`rout`][rout]), not in which order. | C2, C5 |
| D11 | Shots per turn | One shot per shooting action. A unit's shots come from its per-battle pool (K23). | **[designed]** Searched: the recording shows the same shooter firing repeatedly at one target across turns (the 3rd Lancers five times, [`rout`][rout]), and nothing about shots per turn. | C2, C5 |
| D12 | `focusCount` | The number of live enemy units whose assigned target is this defender, including the attacker itself. So `focusCount ≥ 1`. | **[derived]** `FUN_00438420` is decompiled, but its body is not published. The report describes it as *"with `focusCount` attackers on one defender"* and lists the multipliers for 1…4 ([`rout`][rout]). | C2, C5 |
| D13 | `Random(0)` | Returns 0 and consumes no draw. | **[designed]** Searched: the original's behaviour for `n = 0` is not reported. It arises when `m = 0` in the band check (impossible, since K09 routs first) and when a shooting `range_` is 0 (impossible, because of the `+1`). The rule is here for completeness only. | all |
| D20 | Exposure to an enemy's melee | `Xm_E(u) = 1000 + Σ_t share1000_E(t) × matrix[t][u]`, in ×1000 fixed point. `share1000_E(t) = 1000 × troops of type t in E / total troops of E`, over live units. | **[designed]** Searched for an army-level (non-positional) use of the matrix in the original: none. The original uses the matrix only per exchange. The `1000` term (one matrix point) keeps a type that no enemy type attacks well from taking zero losses. | C3, C4 |
| D21 | Exposure to an enemy's fire | `Xf_E(u) = vuln[u] × shooterShare1000_E / 4.5`, computed as `vuln[u] × shooterShare1000_E × 2 / 9`. `shooterShare1000_E = Σ_{t: shots[t] > 0} share1000_E(t)`. | **[designed]** The `4.5` puts `vuln`'s 2…18 scale onto the matrix's 0…4 scale (18/4). Searched: the original never combines the two tables. | C3 |
| D22 | C3's per-unit effective power | §6.3, formula E. The strategic `M` stands in for tactical `m`, and the shooting term is weighted `λ = 1`. | **[designed]** Searched: the original has no one-shot model that uses the matrix. The shooting term reuses the confirmed K21 base, multiplied by `shots[type]`. | C3 |
| D30 | C4's fire phase | Rounds 1–3 are fire rounds. From round 4 on, rounds are shock rounds. | **[designed]** EU4/CK lineage (fire, then shock). Searched: the original has no phases other than its turn loop. | C4 |
| D31 | C4's shock die and pace | One die per side per shock round, `d = Random(10)`. Damage `= Σ a_i × (5 + d) / 60`. | **[designed]** The `/60` sets the pace so that an even fight between two §8 test armies breaks in roughly 5–10 shock rounds. It is set as a design target, not fitted to any battle. Searched: none. | C4 |
| D32 | C4's morale damage | `P −= 200 × lossesThisRound / troopsAtStart` (i.e. μ = 2: 1% of starting troops lost costs 2 morale points). | **[designed]** Searched: none. The original's strategic morale changes only by supply, +3 on battle entry, and new-army initialisation ([`supply-morale`][supply-morale]), so no battle-driven rule exists to copy. | C4 |
| D33 | C4's round cap and pursuit | 30 rounds. Pursuit is one round in which only the winner's cavalry deals shock damage, `× 2`, and the loser deals none. | **[designed]** Searched: none. | C4 |
| D34 | Live strength | `liveP_S = Σ_{live units of S} combatPowerWeight[type] × troops` (the K02 numerator, before its divisors). | **[designed]** use of a confirmed weight (K01). | C2 (D09), C5 |
| D40 | C5's disorder cost of leaving | `troops −= troops × 5 / 100` (5%) for every unit that leaves the field, pursued or not. | **[designed]** It makes an unpursued withdrawal cheap but not free, per the user's steer. Searched: the original has no withdrawal ([`instant`][instant]; K32). | C5 |
| D41 | C5's pursuers per fleeing unit | At most 2 cavalry pursuers and 2 pursuing shots per fleeing unit. Each enemy unit pursues or shoots at most once per round. | **[designed]** Searched: none. | C5 |
| D42 | C5's ordered-withdrawal discount | Pursuit losses during an **ordered** withdrawal are halved (`/ 2`, truncated). A broken unit's flight is not discounted. | **[designed]** Searched: none. | C5 |
| D43 | C5's withdrawal threshold | `liveP_S × 100 < 60 × liveP_O` | **[designed]** Searched: none. | C5 |
| D44 | C5's earliest withdrawal | End of round 3 | **[designed]** Searched: none. | C5 |
| D45 | C5's fled winners rejoin | The winner's fled units rejoin its army after the battle, with the troops they have left after pursuit. | **[designed]** Searched: the original zeroes every routed unit (K32, `FUN_00438f78`). | C5 |
| D50 | Metric test armies, seeds and bands | §8 | **[designed]** Every band is a design decision taken before measurement. §8 gives the reasoning for each. | §8 |

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
is clamped to `tt`. **[designed]**: the reports are silent on the case, and a routed or
zero-troop unit is removed either way (§5).

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
troops = 0; its grid square is cleared;
for each unit on the SAME side:  morale -= 6;  if (morale < 30) it routs too (recursively);
for each unit on the OTHER side: morale = min(99, morale + 5); clear its target if it was this unit;
if (either side now has zero live units) the battle ends;
```

The three ways a unit breaks, and the two consequences:

| | Condition | Constants |
| --- | --- | --- |
| **Strength floor** | `troops < standardBattalionSize / 25`, at any morale | K07, K08: LI 600 · HI 240 · A 140 · LC 280 · HC 100 |
| **Morale floor** | `m ≤ 19`, at any strength | K09 |
| **Probabilistic band** | `20 ≤ m ≤ 39` and `Random(m) + Random(m) ≤ 29` | K10, K11. Per check, a unit breaks with probability 88.8% at `m = 20`, 51.7% at `m = 30` and 30.6% at `m = 39` |
| **Cascade** | on every rout, every surviving friend loses 6 `m`, and any friend now below 30 routs with no check, recursively | K12, K13 |
| **Reward** | on every rout, every enemy gains 5 `m` (capped at 99) and drops its target if it was the routed unit | K14 |

(The band probabilities are exact counts of `X + Y ≤ 29` over `X, Y` uniform on `[0, m)`: 355/400,
465/900 and 465/1521. They are here so the reader can see how steep the band is, and nothing uses
them as a constant. The check runs after **every** exchange a unit takes part in, so a unit that sits
in the band for several exchanges is very likely to break.)

**Recursion, exactly.** When unit X routs, the following steps run in order:
1. X is removed.
2. For each other live unit F on X's side, in slot order: `F.m −= 6`, and if `F.m < 30`, F routs
   (this step, recursively, before moving to the next F).
3. For each live unit on the other side, in slot order: `+5`, capped at 99, and clear its target if
   it was X. A unit that has already routed in the recursion is not live and is skipped.
4. The battle-end test (K32).

This order is a transcription of the pseudocode, with the recursion at the point where it is
written. **[derived]**: the pseudocode is decompiled, but loop order under recursion is inferred from
its shape.

**The strength floor is 4% of a full battalion for every type**, since `size / 25`. So it is a
*relative* floor on full-strength units and an *absolute* troop count per type. A small unit sits
closer to it, which is the mechanism behind Rome's two small heavy-cavalry units breaking
([`rome-gaul`][rome-gaul], correction note).

**Who uses it (Done-when 3, first paragraph).** Every candidate states its relationship to this
trigger in its own section, and the relationships are:

| Candidate | Unit-level break |
| --- | --- |
| C1 | **None.** The merged resolver has no unit-level break, and none is added. |
| C2 | **Unchanged**, including the consequence (`troops = 0`). |
| C3 | **Variant: the strength floor only.** C3 has no tactical morale, so the morale floor, band and cascade have nothing to read. |
| C4 | **Something else: an army-level break only**, with no unit-level rule. §6.4 gives the reason. |
| C5 | **Trigger unchanged, consequence changed.** A broken unit leaves the field and pays a withdrawal cost instead of being zeroed. |

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
