# Auto-resolve tournament: the scorecard

**Status: data plus analysis, not a decision.** This document records what T59
([#250](https://github.com/diegoami/imperial_conquest_2/issues/250), [`docs/tasks/T59.md`](../tasks/T59.md))
measured when it ran the five candidates of [`auto-resolve-approaches.md`](auto-resolve-approaches.md)
against that document's metric suite (§8). **It does not pick a winner and it does not rank the
candidates.** The user decides from the scorecard. Every table lists the candidates in the neutral key
order C1–C5, which is T58's Done-when 1 order and not an order of merit. No table has a verdict column
other than each metric band's own pass or fail, and no row totals the passes.

Nothing here changes how the game resolves a battle. `InstantBattleResolver` is untouched, no ruleset
gains a field or a flag, and nothing in the game calls the candidates.

> **Corrected 2026-10-04: re-measured on the original's melee matrix.** T59 measured C2–C5 on the
> rulesets' `combat.detailedResolver.typeEffectiveness`, and those 25 values are two AI tables, not the
> original's melee matrix ([`2026-10-04-decompiled-tactical-battle-rules.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-10-04-decompiled-tactical-battle-rules.md) §5, a static
> decompile; [#647](https://github.com/diegoami/imperial_conquest_2/issues/647)). T120
> ([#654](https://github.com/diegoami/imperial_conquest_2/issues/654)) put DAT `0x1F7A6`'s real matrix
> in the rulesets, and T121 ([#655](https://github.com/diegoami/imperial_conquest_2/issues/655)) re-ran
> the whole tournament on it; the numbers below are that run's, at the commit named in §1. C1 reads no
> matrix and changed too: the merged resolver draws one fewer time per battle since `bc1a843` (T88),
> not because of the matrix, so its draw-formula diagnostic is stale although its battle numbers stand
> (bug [#659](https://github.com/diegoami/imperial_conquest_2/issues/659); Done-when 3). C2's and C5's
> D06 (shooting distance) and D07 (morale comparison) are still the `[designed]` placeholders
> ([`auto-resolve-approaches.md`](auto-resolve-approaches.md)'s correction of 2026-10-04;
> [#648](https://github.com/diegoami/imperial_conquest_2/issues/648)), superseded by the v0.5.0 port of
> `TBattleMap`, so C2's and C5's numbers measure the real matrix but not the original's tactical morale
> and shooting rules.

## Contents

1. [What was run](#1-what-was-run)
2. [The scorecard](#2-the-scorecard)
3. [The baseline's real numbers](#3-the-baselines-real-numbers)
4. [Supporting tables](#4-supporting-tables)
5. [Cost against T22's soak budget](#5-cost-against-t22s-soak-budget)
6. [The advance statements, checked](#6-the-advance-statements-checked)
7. [The smoke test (§9)](#7-the-smoke-test-9)
8. [Findings](#8-findings)
9. [How to reproduce](#9-how-to-reproduce)

## 1. What was run

- **The candidates**, all five, behind one interface, `IAutoResolveCandidate`
  (`src/IC2.Engine/Battle/Candidates/`, code at `5e35cfb`):

  | Key | Class | What it is (survey section) |
  | --- | --- | --- |
  | C1 | `MergedInstantCandidate` | `InstantBattleResolver.ResolveField`, wrapped unchanged (§6.1) |
  | C2 | `HeadlessTacticalCandidate` | the original's tactical arithmetic, run headless by the designed driver (§6.2) |
  | C3 | `TypeWeightedInstantCandidate` | C1's one-shot shape with a matrix-weighted power and exposure-weighted losses (§6.3) |
  | C4 | `RoundBasedCandidate` | fire and shock rounds with an army morale pool (§6.4) |
  | C5 | `MoraleRetreatCandidate` | C2 with flight, pursuit and an ordered withdrawal (§6.5) |

  C2 and C5 share one engine (`TacticalBattle`), so they differ only in what happens after a break, as
  §6.5 requires. Every constant is either read from the ruleset or is a candidate-local constant cited
  to its K or D row (`CandidateConstants`). Every draw goes through the `IRng` the battle is handed.
- **C1 is the merged code.** It is `ResolveField` at `562e608`, the T63 merge, which **includes #289's
  small-unit deletion pass** (`BattleCasualties.DeleteBelowThreshold`). C1 covers exactly the
  **field-battle** resolver, whose one game call site is `AttackArmyCommandHandler`. `ResolveSiege` and
  `ResolveNaval` are not wrapped, and no metric scores a siege or a naval battle (survey §1).
- **C1's draws are the merged count, not the original's.** `BattleCasualties.Apply` draws one
  `Random(15)` per unit-list entry, where the original's `FUN_0044AE20` always draws 20 (#290; survey §6.1
  "Draws"). Every C1 draw number below is the merged count.
- **C1 changed since T59, and not because of the matrix.** C1 reads no matrix, yet its `det-C1-1.txt`
  differs from T59's: `bc1a843` (T88) draws the post-battle peace roll only when both sides are AI, and
  the tournament's toy scenario has one human seat, so C1's battles take one draw fewer (25, where T59
  measured 26) than its own stated formula assumes. The matrix commit does not move it: `det-C1-1.txt`
  at `845a510^` and at this run's commit are byte-identical, SHA-256 `2522BC44…BF7A` (Done-when 3).
  Because the stated formula still counts the peace draw, the scorecard's C1 "draw-formula mismatches
  (all battles)" reads 186,000 of 186,000; that diagnostic is stale and tracked as bug
  [#659](https://github.com/diegoami/imperial_conquest_2/issues/659). The battle numbers below are the
  re-run's, and `smoke.md`'s C1 rows are as the run regenerates them.
- **The ruleset** is the shipped `classical-faithful`, with `combat.onDefeat` set to `scatter` the way
  `BattleTestbed.Scatter` sets it, so that §8.8 has survivors to measure. The `combat` and `unitTypes`
  blocks of `classical-faithful` and `improved` are identical in every value (checked with `jq`; the
  `combat` blocks differ only in two `_provenance` strings), so this is also
  `improved`'s battle arithmetic. `onDefeat` changes no winner and no winner's loss for any candidate: it
  only adds C1's and C3's loser-side draws, and it decides whether `survivors` is kept or discarded.
- **The schedules** are §8.0's, with `S = 200` seeds for every candidate:
  - CS-T: 441 ordered matchups at T-scale, 88,200 battles;
  - CS-P: 441 ordered matchups at P-scale, 88,200 battles (NT, CD, EN, SV and COST read these too);
  - UR: 6 mirror compositions × 4 values of κ × 2 seats × 200 seeds, 9,600 battles.

  That is 186,000 battles per candidate, 930,000 in all. No candidate needed a lower `S`.
- **Determinism (Done-when 3).** The whole scorecard (`tools/AutoResolveTournament/results/scorecard.json`)
  is a pure function of the seed set. T121 produced it twice at the run's commit with the same bytes,
  SHA-256 `07A4C3D499D4E4921925141ED5C3F5D0E71B809F999BA5DFAF763FAAC1DB7B98`: a full timed run, and a
  second run that plays the CS-P schedule in parallel rather than in order. Both runs printed that hash.
  (It supersedes T59's hash, whose run measured C2–C5 on the wrong matrix.) The test
  `TournamentTests.The_same_seed_set_reproduces_the_whole_scorecard_byte_for_byte` proves the same on a
  reduced seed set, playing the schedule forwards and then backwards, and
  `TournamentTests.The_reduced_seed_scorecard_is_pinned` pins that reduced scorecard's SHA-256, so any change
  to a candidate's rules, the harness or a metric fails a test. Wall-clock cost is kept out of the
  JSON scorecard, because it is a measurement of the machine and not of the seed set.

## 2. The scorecard

Every clause of §8, measured, with its band as §8.9 states it. "n/a" is §8's own *not applicable*
(recorded, not a pass). "undefined" means the clause applies but its procedure excludes every battle, so
there is no number to judge (finding 7). DET was run in two separate processes per candidate; COST was timed in a
Release build (§5).

| Clause | Band | C1 | C2 | C3 | C4 | C5 |
| --- | --- | --- | --- | --- | --- | --- |
| CS-T | ≥ 0.20 | 1.0000 — **pass** | 0.9440 — **pass** | 1.0000 — **pass** | 0.9604 — **pass** | 0.9454 — **pass** |
| CS-P | ≥ 0.20 | 0.0000 — **FAIL** | 0.9188 — **pass** | 0.9500 — **pass** | 0.9526 — **pass** | 0.9156 — **pass** |
| NT | ≥ 1 three-cycle and no dominant mix | 0 three-cycles; 0 edges; dominant: none — **FAIL** | 10 three-cycles; 173 edges; dominant: none — **pass** | 60 three-cycles; 210 edges; dominant: none — **pass** | 33 three-cycles; 206 edges; dominant: LI — **FAIL** | 0 three-cycles; 174 edges; dominant: none — **FAIL** |
| UR(0.90) | [0.1, 0.45] | 0.0000 (0/2400) — **FAIL** | 0.2846 (683/2400) — **pass** | 0.0000 (0/2400) — **FAIL** | 0.1342 (322/2400) — **pass** | 0.2829 (679/2400) — **pass** |
| UR(0.80) | [0.03, 0.35] | 0.0000 (0/2400) — **FAIL** | 0.1813 (435/2400) — **pass** | 0.0000 (0/2400) — **FAIL** | 0.0063 (15/2400) — **FAIL** | 0.1796 (431/2400) — **pass** |
| UR(0.67) | [0.005, 0.2] | 0.0000 (0/2400) — **FAIL** | 0.0767 (184/2400) — **pass** | 0.0000 (0/2400) — **FAIL** | 0.0000 (0/2400) — **FAIL** | 0.0692 (166/2400) — **pass** |
| UR(0.50) | [0, 0.08] | 0.0000 (0/2400) — **pass** | 0.0088 (21/2400) — **pass** | 0.0000 (0/2400) — **pass** | 0.0000 (0/2400) — **pass** | 0.0000 (0/2400) — **pass** |
| UR-monotone | non-increasing as κ falls (±0.02) | non-increasing — **pass** | non-increasing — **pass** | non-increasing — **pass** | non-increasing — **pass** | non-increasing — **pass** |
| UR | every UR clause | outside a band — **FAIL** | all bands — **pass** | outside a band — **FAIL** | outside a band — **FAIL** | all bands — **pass** |
| CD-a | ≥ 0.15 | 0.0333 (median of 25200) — **FAIL** | 0.8928 (median of 24169) — **pass** | 0.3632 (median of 19200) — **pass** | 0.2039 (median of 23490) — **pass** | 0.9124 (median of 25157) — **pass** |
| CD-b | ≥ 2 distinct clear-top types | 1 distinct clear-top types (LC) — **FAIL** | 4 distinct clear-top types (A, HC, LC, LI) — **pass** | 1 distinct clear-top types (A) — **FAIL** | 1 distinct clear-top types (A) — **FAIL** | 4 distinct clear-top types (A, HC, LC, LI) — **pass** |
| EN-a | cap ≤ 0.05 | n/a | 0.0000 — **pass** | n/a | 0.0000 — **pass** | 0.0000 — **pass** |
| EN-b | collapse + withdrawal in [0.10, 0.90] | n/a | 0.7801 — **pass** | n/a | 1.0000 — **FAIL** | 0.9999 — **FAIL** |
| EN-c | cascade in [0.10, 0.90] | n/a | 0.5975 — **pass** | n/a | n/a | 0.3257 — **pass** |
| SV-a | ≥ 0.10 | 0.6463 (median σ) — **pass** | 0.0000 (median σ) — **FAIL** | 0.5591 (median σ) — **pass** | 0.5980 (median σ) — **pass** | 0.2936 (median σ) — **pass** |
| SV-b | ≥ 0.10 | -0.0008 (median 1−σ 0.3537 − median ω 0.3545) — **FAIL** | 0.6370 (median 1−σ 1.0000 − median ω 0.3630) — **pass** | 0.1641 (median 1−σ 0.4409 − median ω 0.2768) — **pass** | 0.2211 (median 1−σ 0.4020 − median ω 0.1809) — **pass** | 0.3699 (median 1−σ 0.7064 − median ω 0.3365) — **pass** |
| SV-c | ≥ 0.05 | -0.0001 (σ(Z) 0.6461 over 25200 − σ(H) 0.6462 over 50400) — **FAIL** | 0.0000 (σ(Z) 0.0000 over 23126 − σ(H) 0.0000 over 53563) — **FAIL** | -0.0424 (σ(Z) 0.4649 over 26800 − σ(H) 0.5073 over 50400) — **FAIL** | 0.0657 (σ(Z) 0.6168 over 30864 − σ(H) 0.5510 over 43819) — **pass** | 0.0731 (σ(Z) 0.3467 over 23927 − σ(H) 0.2736 over 51740) — **pass** |
| SV-d | ≥ 0.05 | 0.0029 (mean τ over 88200 battles with survivors) — **FAIL** | undefined (mean τ over 0 battles with survivors) — **undefined** | 0.1770 (mean τ over 86997 battles with survivors) — **pass** | 0.0888 (mean τ over 88200 battles with survivors) — **pass** | 0.3029 (mean τ over 88200 battles with survivors) — **pass** |
| DET | 100/100 byte-identical (two processes) and 100/100 draws as stated; no other randomness source | 100/100 byte-identical across two processes; draw count as stated: 0/100 — **FAIL** | 100/100 identical; 100/100 draws — **pass** | 100/100 identical; 100/100 draws — **pass** | 100/100 identical; 100/100 draws — **pass** | 100/100 identical; 100/100 draws — **pass** |
| COST mean t_c | (reported) | 0.0099 ms | 0.0159 ms | 0.0040 ms | 0.0056 ms | 0.0126 ms |
| COST p99 | ≤ 50 ms | 0.0456 ms — **pass** | 0.0482 ms — **pass** | 0.0075 ms — **pass** | 0.0128 ms — **pass** | 0.0304 ms — **pass** |
| COST E_c = E0 + B × (t_c − t_1) | ≤ 240 s | 2.3700 s — **pass** | 2.3703 s — **pass** | 2.3697 s — **pass** | 2.3698 s — **pass** | 2.3701 s — **pass** |

The draw-formula check also ran on **every** battle, not only on §8.5's 100: 0 mismatches in 186,000
battles for C2–C5. C1's reads 186,000 of 186,000 because its stated formula still counts the peace
draw that `bc1a843` skips for its one-human-side battles; that is bug
[#659](https://github.com/diegoami/imperial_conquest_2/issues/659), recorded not fixed (§4, Diagnostics).

## 3. The baseline's real numbers

T59's Done-when 4 aside says that the baseline *"is expected to"* score about zero on composition
sensitivity. That holds for casualty shape and for the power-matched variant, and it does not hold for
the metric as Done-when 5 words it (bug [#288](https://github.com/diegoami/imperial_conquest_2/issues/288),
B1; survey §2.1). The measured numbers, both variants:

- **CS-T (total troops held equal): 1.0000, a pass.** The merged `armyPower` weights the types
  20/100/40/60/120 (K01), so with troops held equal the pure heavy-cavalry army wins every battle
  (`s_T = 1.000`) and the pure light-infantry army loses every battle (`s_T = 0.000`). The baseline is
  composition-sensitive with a fixed price per type.
- **CS-P (baseline power held equal): 0.0000, a fail.** Every P-scale battle is an exact tie, which the
  defender wins (all 88,200; the P-scale attacker win rate is 0.0000), so every `p(A, B) = 0.5`.
- On casualty shape, CD-a's median per-type spread in the winner is **0.0333**, a fail against 0.15.

The line *"failures are reported, not smoothed"* stands as written: every FAIL above is in the table.

## 4. Supporting tables

Per-composition score s(A), T-scale / P-scale:

| Composition | C1 s_T / s_P | C2 s_T / s_P | C3 s_T / s_P | C4 s_T / s_P | C5 s_T / s_P |
| --- | --- | --- | --- | --- | --- |
| LI | 0.000 / 0.500 | 0.102 / 0.333 | 0.000 / 0.750 | 0.099 / 1.000 | 0.020 / 0.350 |
| HI | 0.900 / 0.500 | 0.966 / 0.922 | 0.900 / 0.900 | 0.889 / 0.605 | 0.965 / 0.921 |
| A | 0.125 / 0.500 | 0.022 / 0.003 | 0.100 / 0.150 | 0.016 / 0.287 | 0.079 / 0.005 |
| LC | 0.375 / 0.500 | 0.642 / 0.792 | 0.700 / 0.950 | 0.721 / 0.663 | 0.558 / 0.770 |
| HC | 1.000 / 0.500 | 0.850 / 0.498 | 1.000 / 0.550 | 0.976 / 0.071 | 0.914 / 0.487 |
| LI+HI | 0.375 / 0.500 | 0.533 / 0.522 | 0.550 / 0.550 | 0.538 / 0.807 | 0.503 / 0.498 |
| LI+A | 0.050 / 0.500 | 0.044 / 0.230 | 0.050 / 0.550 | 0.053 / 0.820 | 0.057 / 0.261 |
| LI+LC | 0.125 / 0.500 | 0.256 / 0.391 | 0.250 / 0.600 | 0.340 / 0.883 | 0.196 / 0.361 |
| LI+HC | 0.575 / 0.500 | 0.544 / 0.461 | 0.200 / 0.200 | 0.327 / 0.684 | 0.538 / 0.428 |
| HI+A | 0.575 / 0.500 | 0.677 / 0.732 | 0.500 / 0.300 | 0.497 / 0.189 | 0.702 / 0.806 |
| HI+LC | 0.675 / 0.500 | 0.741 / 0.824 | 0.800 / 0.850 | 0.812 / 0.592 | 0.695 / 0.823 |
| HI+HC | 0.950 / 0.500 | 0.915 / 0.832 | 0.950 / 0.850 | 0.939 / 0.387 | 0.908 / 0.811 |
| A+LC | 0.250 / 0.500 | 0.180 / 0.185 | 0.350 / 0.350 | 0.276 / 0.364 | 0.194 / 0.140 |
| A+HC | 0.675 / 0.500 | 0.409 / 0.180 | 0.400 / 0.000 | 0.232 / 0.047 | 0.471 / 0.139 |
| LC+HC | 0.800 / 0.500 | 0.775 / 0.726 | 0.850 / 0.700 | 0.824 / 0.315 | 0.789 / 0.705 |
| uniform | 0.500 / 0.500 | 0.540 / 0.411 | 0.450 / 0.350 | 0.486 / 0.482 | 0.533 / 0.446 |
| LI-heavy | 0.200 / 0.500 | 0.262 / 0.450 | 0.150 / 0.500 | 0.275 / 0.885 | 0.230 / 0.483 |
| HI-heavy | 0.750 / 0.500 | 0.709 / 0.715 | 0.750 / 0.650 | 0.720 / 0.363 | 0.730 / 0.746 |
| A-heavy | 0.300 / 0.500 | 0.258 / 0.199 | 0.300 / 0.150 | 0.215 / 0.366 | 0.290 / 0.263 |
| LC-heavy | 0.450 / 0.500 | 0.400 / 0.504 | 0.600 / 0.550 | 0.597 / 0.558 | 0.417 / 0.466 |
| HC-heavy | 0.850 / 0.500 | 0.677 / 0.592 | 0.650 / 0.050 | 0.670 / 0.132 | 0.712 / 0.591 |

CD-b clear-top type in the uniform winner, per opponent:

| Opponent | C1 | C2 | C3 | C4 | C5 |
| --- | --- | --- | --- | --- | --- |
| LI | LC | none | no data | no data | LI |
| HI | LC | no data | no data | none | no data |
| A | LC | LC | A | A | LC |
| LC | LC | none | no data | none | LI |
| HC | LC | none | no data | none | LI |
| LI+HI | LC | none | no data | no data | none |
| LI+A | LC | LI | no data | no data | LI |
| LI+LC | LC | none | no data | no data | none |
| LI+HC | LC | none | A | A | LI |
| HI+A | LC | none | A | A | no data |
| HI+LC | LC | none | no data | none | LI |
| HI+HC | LC | none | no data | none | LI |
| A+LC | LC | A | A | A | A |
| A+HC | LC | A | A | none | A |
| LC+HC | LC | LI | no data | none | LI |
| uniform | LC | LI | A | A | LI |
| LI-heavy | LC | LI | no data | no data | LI |
| HI-heavy | LC | none | no data | none | none |
| A-heavy | LC | LI | A | A | LI |
| LC-heavy | LC | LI | no data | none | LI |
| HC-heavy | LC | HC | A | none | HC |

Endings over the P-scale schedule, and mean σ by ending (§8.8 diagnostic, no band):

| Ending | C1 share / mean σ | C2 share / mean σ | C3 share / mean σ | C4 share / mean σ | C5 share / mean σ |
| --- | --- | --- | --- | --- | --- |
| annihilation | 0.0000 | 0.2199 / 0.0000 | 0.0000 | 0.0000 | 0.0001 / 0.0819 |
| cap | 0.0000 | 0.0000 | 0.0000 | 0.0000 | 0.0000 |
| collapse | 0.0000 | 0.7801 / 0.0000 | 0.0000 | 1.0000 / 0.5818 | 0.0135 / 0.3586 |
| decided | 1.0000 / 0.6462 | 0.0000 | 1.0000 / 0.4999 | 0.0000 | 0.0000 |
| withdrawal | 0.0000 | 0.0000 | 0.0000 | 0.0000 | 0.9864 / 0.2956 |

Diagnostics (no band):

| Diagnostic | C1 | C2 | C3 | C4 | C5 |
| --- | --- | --- | --- | --- | --- |
| attacker win rate, P-scale | 0.0000 | 0.3912 | 0.4762 | 0.4971 | 0.3867 |
| attacker win rate, T-scale | 0.4671 | 0.4238 | 0.4762 | 0.4991 | 0.4289 |
| battle-phase draws per P-scale battle (mean) | 18.6 | 406.7 | 18.6 | 85.3 | 385.4 |
| battle-phase draws per P-scale battle (min..max) | 8..36 | 144..1296 | 8..36 | 6..222 | 95..1382 |
| draw-formula mismatches (all battles) | 186000 of 186000 | 0 of 186000 | 0 of 186000 | 0 of 186000 | 0 of 186000 |
| rounds per P-scale battle (mean) | 0.00 | 12.99 | 0.00 | 5.31 | 9.91 |

## 5. Cost against T22's soak budget

The procedure is §8.6's, and it was run on one machine (32 logical processors, .NET 10.0.12, Release
build), with nothing else running.

- **Per battle.** 1,000 warm-up battles, then all 88,200 CS-P battles timed one by one on one thread.
  The time includes building the battle's input record and reducing its outcome, identically for every
  candidate. For C1 it also includes building the two-army `GameState` the wrapper hands to
  `ResolveField`, and `ResolveField`'s own post-battle phase. The other candidates' times cover the battle
  phase only (§3 of the survey: the post-battle phase is shared and not part of a candidate).
- **Soak baseline.** `AiSoakTests` (50 seeds, `SoakTurnCap = 1200`), run in Release on the same machine,
  printed **`E0 = 2.37 s` of a 300 s budget** (48,000 turns, 50 games expired at the hard end year). The
  tool's own reproduction of the same 50 games (`soak`, final-state hashes checked against
  `AiGameRunner.Run`'s) took 1.51 s and counted **`B = 50` field battles**, with 0 sieges and 0 naval
  battles, one per `BattleResolved` of kind `Field`.
- **Projection.** `E_c = E0 + B × (t_c − t_1)`.

| | C1 | C2 | C3 | C4 | C5 |
| --- | --- | --- | --- | --- | --- |
| mean `t_c` | 0.0099 ms | 0.0159 ms | 0.0040 ms | 0.0056 ms | 0.0126 ms |
| p99 (band ≤ 50 ms) | 0.0456 ms — **pass** | 0.0482 ms — **pass** | 0.0075 ms — **pass** | 0.0128 ms — **pass** | 0.0304 ms — **pass** |
| slowest single battle | 16.2 ms | 13.4 ms | 16.3 ms | 6.0 ms | 13.0 ms |
| `E_c` (band ≤ 240 s) | 2.3700 s — **pass** | 2.3703 s — **pass** | 2.3697 s — **pass** | 2.3698 s — **pass** | 2.3701 s — **pass** |

What these numbers can and cannot carry:
- **No candidate is visibly disqualified on cost.** The headless-tactical engine that T59's hazard
  asked to budget for first (C2, and C5 on the same engine) runs at about 15 µs per P-scale battle, over
  a mean of 13.0 rounds and 407 draws. So it did not have to be cut, and the finding the hazard feared
  does not arise.
- **The soak cannot tell the candidates apart.** With `B = 50` field battles over 50 games, even a
  candidate 1 ms slower per battle than C1 would move `E_c` by 0.05 s. The toy soak exercises very few
  battles, so the p99 clause is the one with any discriminating power here, and every candidate is
  three orders of magnitude inside it.
- The slowest single battles (6–16 ms) occur in every candidate, including the one-shot C1 and C3,
  whose work per battle is fixed. That suggests pauses of the process rather than slow battles
  **[inference, not measured]**. The band reads the 99th percentile, which is 0.049 ms or less for all
  five.

## 6. The advance statements, checked

The survey states some results in advance, as facts about each candidate's arithmetic (§6, §8 "Fixed by
construction"). Each is checked against the measurement here. Where one did not hold, that is reported
rather than explained away.

| Statement (survey section) | Measured | Held? |
| --- | --- | --- |
| C1 CS-P = 0 exactly; every P-scale battle a defender win (§8.1) | 0.0000; attacker win rate 0.0000 | yes |
| C1 passes CS-T and fails CS-P (§8.1) | 1.0000 pass; 0.0000 fail | yes |
| C1 has no NT edges, so no cycle (§8.2) | 0 edges, 0 cycles | yes |
| C1 and C3 have UR = 0 at every κ (§8.3) | 0/2400 at every κ, for both | yes |
| C1's CD spread is about 1 point, so it fails CD-a (§8.4) | median 3.33 points; fails | the fail held; the spread is 3.3 points, not about 1 |
| C1 has no clear-top type, so it fails CD-b (§8.4) | **light cavalry is the clear top against all 21 opponents**; 1 distinct type, so it still fails | **no**, see below |
| C2 has σ = 0 in every battle, so it fails SV-a (§8.8) | median σ 0.0000; no battle with survivors | yes |
| C1's survivor mix equals its starting mix, so it fails SV-d (§8.8) | mean τ 0.0029 | yes |
| C1's `(1 − σ) ≈ ω` on P-scale, so it fails SV-b; Z and H alike, so it fails SV-c (§8.8) | −0.0008; −0.0001 | yes |
| C4 fails EN-b essentially always, because its endings are collapse (or cap) (§8.7) | EN-b **1.0000, a FAIL**; no annihilation (0.0000), collapse 1.0000, cap 0.0000, so EN-a passes | yes |

**C1's clear top is light cavalry, and that is T63's deletion pass acting on §8.0's armies.** The
survey's CD statements were written for a resolver whose per-type spread comes only from the
`[105, 120)` divisor draw. The merged C1 also deletes, after its casualties, any winner unit left below
`standardBattalionSize / 10` (#289, merged in T63). §8.0's split gives the uniform P-scale army a
**1,000-troop light-cavalry remainder unit** (8,000 = 7,000 + 1,000). At the tie ratio of 40 it loses
about 36%, keeps 640–680 troops, and falls below LC's deletion threshold of 700, so it is deleted whole.
That raises light cavalry's loss in every battle the uniform mix wins from about 36% to about 44%, which
clears CD-b's 0.05 margin against every opponent. No other §8.0 all-five-type army has a remainder that
the deletion pass reaches (checked by hand for the uniform and the five one-heavy P-scale armies). C1
still fails both CD clauses. The number moved, and the reason is the merged code, not the wrapper.

**C4 now fails EN-b, as the survey predicted.** All 88,200 of its P-scale battles end in collapse
(cap 0.0000), so EN-b's collapse share is 1.0000, above the 0.90 bound, and it is EN-a (cap ≤ 0.05)
that now passes at 0.0000. T59's run drew the opposite verdicts before T120's matrix changed the
round-based arithmetic; no C4 battle now reaches the 30-round cap. The survey's premise still holds as
measured: 0 annihilations. Neither band was moved.

## 7. The smoke test (§9)

Each candidate played each observed battle for seeds `k = 0 … 999`. §9.4's only failing conditions are
an exception, a unit with negative troops or more troops than it started with, an incomplete output
record, or one of the first 10 seeds failing a byte-identity check. **No candidate failed any of them on
any instance.** (The byte-identity check here re-ran each of the first 10 seeds on a fresh generator in
the same process. §8.5's two-process check is the DET row above.) Everything else is reported beside
the observation with no band, and, as §9 requires, **nothing was tuned against it**. A candidate that
does not produce the observed winner is not thereby failed (§9.4).

The per-type loss columns are the attacker's own losses, over the seeds in which the attacker won. The
whole-arm column is over all seeds, for whichever side won.

### §9.1 Seleucid 45,100 v Ptolemaic 27,700 ([designed] rosters: §8.0 split, q 6, M 59, Seleucid attacks)

Seleucid units: light_infantry 15,000, light_infantry 12,300, heavy_infantry 6,000, heavy_infantry 2,400, archers 3,500, archers 1,700, light_cavalry 1,800, heavy_cavalry 2,400; Ptolemaic units: light_infantry 15,000, light_infantry 6,300, heavy_infantry 3,200, light_cavalry 2,300, heavy_cavalry 900

| Candidate | smoke verdict | Seleucid (attacker) wins | attacker total loss p5 / p50 / p95 | LI loss p5/p50/p95 | HI loss p5/p50/p95 | A loss p5/p50/p95 | LC loss p5/p50/p95 | HC loss p5/p50/p95 | winner loses a whole arm (which) |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | pass | 1.000 | 0.172 / 0.177 / 0.182 | 0.171 / 0.178 / 0.186 | 0.169 / 0.176 / 0.186 | 0.165 / 0.173 / 0.185 | 0.167 / 0.178 / 0.189 | 0.167 / 0.175 / 0.183 | 0.000 |
| C2 | pass | 1.000 | 0.646 / 0.706 / 0.748 | 1.000 / 1.000 / 1.000 | 0.026 / 0.082 / 0.235 | 0.250 / 0.613 / 1.000 | 0.000 / 0.006 / 1.000 | 0.000 / 0.000 / 0.033 | 0.974 (LC 9, LI 789, LI+A 114, LI+A+LC 1, LI+LC 61) |
| C3 | pass | 1.000 | 0.180 / 0.186 / 0.193 | 0.220 / 0.229 / 0.239 | 0.052 / 0.054 / 0.057 | 0.252 / 0.264 / 0.281 | 0.093 / 0.099 / 0.105 | 0.050 / 0.052 / 0.055 | 0.000 |
| C4 | pass | 1.000 | 0.075 / 0.121 / 0.187 | 0.093 / 0.150 / 0.230 | 0.018 / 0.033 / 0.055 | 0.103 / 0.172 / 0.264 | 0.051 / 0.074 / 0.107 | 0.021 / 0.035 / 0.055 | 0.000 |
| C5 | pass | 1.000 | 0.217 / 0.321 / 0.368 | 0.359 / 0.531 / 0.608 | 0.000 / 0.000 / 0.000 | 0.000 / 0.000 / 0.000 | 0.000 / 0.000 / 0.000 | 0.000 / 0.000 / 0.000 | 0.000 |

Seat swap (measured, no band): the same two rosters and seeds `k = 0 … 999`, Seleucid attacking and then defending.

| Candidate | Seleucid wins attacking | Seleucid wins defending |
| --- | --- | --- |
| C1 | 1.000 | 1.000 |
| C2 | 1.000 | 1.000 |
| C3 | 1.000 | 1.000 |
| C4 | 1.000 | 1.000 |
| C5 | 1.000 | 1.000 |

### §9.2 Rome v Gaul, `1_rome_270_winter_7.sav` (rosters read from the save; Rome attacks)

Roster assertions: all passed (totals, HC 755 + 2,432, 4th Bowmen 3,312, slot qualities, Rome M = 68). Owners: army 0 Rome, army 13 Gaul. Gaul's +14 = 62.

| Candidate | smoke verdict | Rome (attacker) wins | attacker total loss p5 / p50 / p95 | LI loss p5/p50/p95 | HI loss p5/p50/p95 | A loss p5/p50/p95 | LC loss p5/p50/p95 | HC loss p5/p50/p95 | winner loses a whole arm (which) | observed final total(s): percentile in the attacker-won distribution |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | pass | 1.000 | 0.182 / 0.186 / 0.189 | 0.180 / 0.187 / 0.193 | 0.180 / 0.186 / 0.190 | 0.179 / 0.184 / 0.191 | 0.176 / 0.188 / 0.198 | 0.171 / 0.178 / 0.191 | 0.000 | 63,282: 0.000; 75,536: 0.000 (range 80,751–81,948) |
| C2 | pass | 1.000 | 0.247 / 0.285 / 0.334 | 0.249 / 0.310 / 0.393 | 0.030 / 0.069 / 0.110 | 0.399 / 0.404 / 0.416 | 0.273 / 0.473 / 0.733 | 0.776 / 0.802 / 0.847 | 0.004 (HC 4) | 63,282: 0.006; 75,536: 0.963 (range 60,032–79,672) |
| C3 | pass | 1.000 | 0.173 / 0.177 / 0.181 | 0.225 / 0.233 / 0.241 | 0.057 / 0.058 / 0.060 | 0.267 / 0.275 / 0.284 | 0.101 / 0.108 / 0.113 | 0.052 / 0.054 / 0.058 | 0.000 | 63,282: 0.000; 75,536: 0.000 (range 81,555–82,898) |
| C4 | pass | 1.000 | 0.104 / 0.171 / 0.247 | 0.140 / 0.227 / 0.323 | 0.028 / 0.054 / 0.085 | 0.155 / 0.260 / 0.374 | 0.084 / 0.121 / 0.164 | 0.033 / 0.056 / 0.084 | 0.000 | 63,282: 0.000; 75,536: 0.056 (range 70,287–90,801) |
| C5 | pass | 1.000 | 0.153 / 0.185 / 0.224 | 0.152 / 0.205 / 0.263 | 0.001 / 0.004 / 0.013 | 0.162 / 0.210 / 0.248 | 0.295 / 0.511 / 0.750 | 0.579 / 0.697 / 0.762 | 0.000 | 63,282: 0.000; 75,536: 0.008 (range 73,937–87,137) |

### §9.3 Rome v Gaul, `7.sav → 8.sav`

Exact-total lookup in `7.sav`: 1 army record(s) match Rome's start column, 1 match Gaul's.
Path taken: **[confirmed by exact match]** — Rome's column matches army 0 (Rome) at (102,44), M 70; Gaul's matches army 9 (Gaul) at (103,43), M 68.

| Candidate | smoke verdict | Rome (attacker) wins | attacker total loss p5 / p50 / p95 | LI loss p5/p50/p95 | HI loss p5/p50/p95 | LC loss p5/p50/p95 | HC loss p5/p50/p95 | winner loses a whole arm (which) | observed final total(s): percentile in the attacker-won distribution |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| C1 | pass | 1.000 | 0.130 / 0.132 / 0.135 | 0.126 / 0.133 / 0.138 | 0.130 / 0.133 / 0.136 | 0.119 / 0.125 / 0.138 | 0.124 / 0.128 / 0.134 | 0.000 | 39,941: 0.000 (range 43,770–44,235) |
| C2 | pass | 1.000 | 0.188 / 0.229 / 0.263 | 0.367 / 0.539 / 0.565 | 0.042 / 0.055 / 0.078 | 1.000 / 1.000 / 1.000 | 0.423 / 0.558 / 0.718 | 1.000 (LC 1000) | 39,941: 0.804 (range 33,998–43,404) |
| C3 | pass | 1.000 | 0.069 / 0.070 / 0.072 | 0.176 / 0.185 / 0.192 | 0.044 / 0.045 / 0.046 | 0.071 / 0.075 / 0.083 | 0.039 / 0.040 / 0.043 | 0.000 | 39,941: 0.000 (range 46,982–47,266) |
| C4 | pass | 1.000 | 0.080 / 0.129 / 0.187 | 0.219 / 0.336 / 0.460 | 0.044 / 0.079 / 0.122 | 0.133 / 0.181 / 0.232 | 0.052 / 0.083 / 0.119 | 0.000 | 39,941: 0.001 (range 39,769–47,277) |
| C5 | pass | 1.000 | 0.114 / 0.154 / 0.171 | 0.174 / 0.358 / 0.416 | 0.009 / 0.022 / 0.032 | 0.961 / 0.983 / 0.994 | 0.193 / 0.300 / 0.520 | 0.000 | 39,941: 0.000 (range 41,458–46,003) |

## 8. Findings

Done-when 1 asks that a candidate the survey does not specify well enough be reported rather than
completed by invention. **All five candidates were implemented from the survey's register and its
labelled placeholders.** None needed a new constant or a new mechanism. In seven places the text allowed
more than one reading, and each reading taken is stated here, so that a reviewer can disagree with it.
None of them was chosen by looking at a result.

1. **D04's compass for the defender (C2, C5).** D04 defines `N` as *"toward the enemy home row"* and
   breaks ties in the order N, NE, E, SE, S, SW, W, NW. It does not say whether `E` also turns for the
   defender. The implementation keeps `E` as increasing column for both sides, so the defender's compass
   is the attacker's reflected, not rotated. This decides only a tie between two equally good steps.
2. **One pursuit flag or two (C5).** D41's summary says that each enemy unit *"pursues or shoots at most
   once per round"*. §6.5.2's algorithm keeps two flags, `pursuedThisRound` and
   `firedPursuitThisRound`, and its steps 3 and 4 each read one of them. The algorithm was followed, so a
   light-cavalry unit (a cavalry type with shots) may both pursue and fire in pursuit in one round.
3. **C4's pursuit round.** §6.4 distributes the pursuit *"onto the loser as a shock round"*. The
   implementation recomputes the shares over the live units after the round's losses, since the
   pursuit is one more round and shares are *"recomputed … at the start of every round"*. It weights
   the loser's units by `Xm_winner` over the winner's whole live mix, which is how a shock round
   weights them.
4. **UR's seeds.** §8.3 names no seed rule. §8.0's rule is stated to be *"shared by every metric"*, so
   UR uses `1,000,003 × i + k` with `i` the mirror matchup's own index (`22 × index(c)`). The same 200
   seeds are therefore reused across the four values of κ and both seats.
5. **Medians and ties in the metrics.** A median over an even count is the mean of the two middle
   values. In CD-b, types with equal mean loss are ordered by type index, and an opponent against which
   the uniform mix never won is recorded as *no data* and counts as no clear top. That affects C3 (9
   opponents) and C4 (6), whose uniform army loses every battle against those opponents.
6. **K27's placeholder (C2, C5)** was kept as the survey specifies: +3 to **both** sides, used only to
   seed `m` and not written back. The original gives +3 to a computer-controlled side only (research
   `1762c84`). The survey leaves that choice to the user, and so does this document.
7. **SV-d with no survivors is *undefined*, not a fail.** §8.8 excludes a battle with no survivors from
   SV-d. For C2 every battle is excluded (its σ is 0 by construction), so SV-d has no number. The first
   submission scored that as a FAIL. It is now recorded as *undefined*, which is neither a pass nor a
   fail. That changes no verdict: C2's SV already fails on SV-a (review round 1, N2).

Further findings, each reported as data:

8. **T16's reserve-research guard and T59's Owns list conflicted; the user granted an exemption.**
   `tests/IC2.Engine.Tests/Battle/BattleDeterminismTests.cs`,
   `NoReserveTacticalResearchAppearsInTheBattleNamespacesCode`, scanned **every** `.cs` file under
   `src/IC2.Engine/Battle/` recursively for `DetailedResolver`, `TypeEffectiveness`, `MeleeLossCap`,
   `MeleeBasePowerFloor`, `MeleePowerDivisor` and `InRangeShotMultiplier`. T59's Owns list places the
   candidates at `src/IC2.Engine/Battle/Candidates/**`, and the survey (§3) says candidates *"should read
   those values from"* `combat.detailedResolver`, so C2–C5 tripped it. Renaming properties or
   hard-coding ruleset values to dodge the guard was rejected. The user granted a narrow edit (main
   `8d3d298`): the guard now skips `Battle/Candidates/` and says why, and a new test,
   `NoProductionCodeOutsideCandidatesReferencesTheCandidates`, fails if any production source outside
   `src/IC2.Engine/Battle/Candidates/` names the candidates' namespace or any of their types. It scans all
   of `src/` and `godot/`, skipping build output (widened in review round 1, N1). Both were proved by
   mutation: a banned name placed in `Battle/` outside `Candidates/` fails the first test, and a
   reference to the candidates placed in `src/IC2.Engine/Core/` or in `src/IC2.Cli/` fails the second.
9. **C5 ends 98.6% of P-scale battles by ordered withdrawal** (EN-b 0.9999, above the 0.90 bound). §8.7
   says in advance that EN-b counts `withdrawal` with `collapse` on purpose, and that whether *"never
   fought to the end"* is acceptable is **the user's call, not T59's**. The number is reported and the
   band is unchanged.
10. **Dominant compositions.** On the P-scale, C4 has pure light infantry dominant (`s_P = 1.000`) and
    fails NT on that clause. C3 no longer has a dominant composition, so it passes NT with 60
    three-cycles. C5, the candidate §8.2 names as most at risk on NT, now has **0 three-cycles and no
    dominant composition**, so it fails NT's `≥ 1 three-cycle` clause (as does C1, also with 0); C2 has
    10 three-cycles and none, and passes.
11. **Seat asymmetry in the headless driver.** C2's attacker wins 39.1% of P-scale battles and C5's
    38.7%, below the 50% that the mirror-symmetric schedule would give a seat-neutral model. §8's
    `p(A, B)` is symmetrised over seats, so no band reads this directly. The driver's seat-dependent
    parts are all placeholders (D01 rows, D02 attacker first, D04, D05), and the measurement does not
    say which of them causes it. The one direct measurement is §9.1's seat swap (§7): with the same two
    rosters and seeds, C2 and C5 each now win **100% attacking and 100% defending**. C1, C3 and C4 also
    give 100% in both seats.
12. **§10's open item on `7.sav` is settled.** The §9.3 exact-total lookup found exactly one army record
    per start column in `7.sav`: Rome's army 0 at (102,44), owner Rome, M 70, and Gaul's army 9 at
    (103,43), owner Gaul, M 68. §9.3 therefore ran on the save's own rosters, qualities and morale
    **[confirmed by exact match]**, not on the designed fallback. (This settles the survey's §10 row
    *"which army records in `7.sav` fought"*, for the research repository to pick up.)
13. **The §9.2 roster assertions all passed** against `1_rome_270_winter_7.sav`: the per-type totals,
    Rome's heavy cavalry at 755 and 2,432, 4th Bowmen at 3,312, the slot qualities, and Rome's M = 68.
    Gaul's `+14` read 62, as §9.2 records.

## 9. How to reproduce

From the repository root, on any machine (`--corpus` only for §9.2 and §9.3, which need the local asset
corpus's saves):

```text
dotnet build tools/AutoResolveTournament -c Release
dotnet run -c Release --no-build --project tools/AutoResolveTournament -- soak
dotnet test tests/IC2.Engine.Tests -c Release --filter "FullyQualifiedName~AiSoakTests.Fifty_fixed_seeds" --logger "console;verbosity=detailed"
dotnet run -c Release --no-build --project tools/AutoResolveTournament -- run --e0 2.37 --b 50
dotnet run -c Release --no-build --project tools/AutoResolveTournament -- run --no-timing --e0 2.37 --b 50
dotnet run -c Release --no-build --project tools/AutoResolveTournament -- smoke --corpus <asset directory>
dotnet run -c Release --no-build --project tools/AutoResolveTournament -- trace --candidate C5 --attacker 15 --defender 4 --seed 3
```

The two `run` commands were the run: the first timed (it wrote the committed files), the second with
`--no-timing` into a separate directory. Both printed the same SHA-256,
`07A4C3D499D4E4921925141ED5C3F5D0E71B809F999BA5DFAF763FAAC1DB7B98`. `--e0 2.37` is this machine's
`AiSoakTests` E0 and `--b 50` is its soak's `B`.

`run` writes `tools/AutoResolveTournament/results/`: `scorecard.json` (the deterministic scorecard; its
SHA-256 is printed), `scorecard.md` (the tables above), `cost-and-det.json` (the timings and the DET
outcome) and `det-Cn-1.txt` (each candidate's first 100 CS-P output records, in §8.5's canonical JSON).
`--no-timing` plays every schedule in parallel and produces the same `scorecard.json`.
