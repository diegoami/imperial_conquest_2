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
