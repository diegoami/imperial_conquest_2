# Repository and direction audit, 2026-09-18

A point-in-time review of this repository and of where the build is heading, written on a review branch for the user. It is **dated and not maintained**: the numbers below describe the tree at `cde9b52` and the GitHub board as read on 2026-09-18 around 01:15 UTC. Status still lives only in GitHub labels ([build-process.md §5](build-process.md#5-status-lives-on-github)); this document quotes the board once, as evidence for its findings, and nothing here should be kept in sync.

What was examined: every document under `docs/`, the README, `CLAUDE.md`, the `.github/` templates and workflow, all of `src/IC2.Engine`, `src/IC2.Cli`, `src/IC2.Data`, `src/IC2.Inspect`, both test projects and `tests/fixtures/`, the git history, and the GitHub issues, pull requests, milestones and Actions runs. What could not be examined: the research repository (out of this session's scope), and a local build or test run (no .NET SDK in this environment, so CI's 15 consecutive green runs on `main` stand in for it).

---

## 1. Summary

The project is in good shape, and its direction is sound. The engine core is unusually well built for a project six days into its git history: immutable value-equal state, a closed turn-phase pipeline with attribute-registered systems, named deterministic RNG streams, a strict typed loader, and a 397-entry evidence corpus that every gameplay number is checked against. Test code outstrips engine code. CI is green. Nothing found in the engine is a correctness bug in a shipped gameplay path.

The risks are not in the code. They are in the surrounding machinery and in what the plan defers:

1. **The first release gate is met and nothing was cut.** Every task in `release:v0.1.0`'s original set is merged, no tag exists, and the next task was dispatched anyway, against the release plan's own rule. The gate has since been widened by one task (T43, on an unmerged plan branch), which is fine, but the release checklist has never been exercised and its first run will find problems.
2. **The catalogue is growing by correction.** 43 tasks now against the 31 the design named; 11 of the 12 additions correct or complete already-merged work. That is the review gates working, but it means the remaining 23 tasks, which include every hard one, will spawn more, and the throughput estimate should assume it.
3. **Triage debt is accumulating quietly.** 12 open follow-up issues, 7 of them with no triage label at all; 9 open bugs; two milestones finished but open; seven tasks on no milestone.
4. **The process is tied to one Windows machine by hard-coded paths**, so it cannot be run from anywhere else, including this session.
5. **The Godot lane is the largest untested risk and sits at the very end.** The UI is excluded from CI, the existing viewer is read-only code from the research phase, and the first Godot task waits on the exported real world. A thin Godot slice on the toy world, the way T41 was for the CLI, would de-risk it early.

Sections 3 and 4 give the evidence. Section 5 is the directional assessment, section 6 a prioritised action list, section 7 the decisions that are the user's.

---

## 2. Where the build stood when this was written

Quoted once, from the labels, as evidence for the findings. Do not update it here.

| Measure | Value |
| --- | --- |
| Tasks (`label:task`) | 43: 20 `status:merged`, 20 `status:blocked`, 2 `status:ready` (T35 #63, T43 #110), 1 `status:in-progress` (T38 #78, no open PR from its branch) |
| Phases | Phase 0 and Phase 1 milestones fully closed (7 and 10 issues); Phase 2 13 open; Phase 3 6 open; T37–T43 on no milestone |
| Release labels | `v0.1.0` 21 (20 merged + T43 open), `v0.2.0` 8, `v0.3.0` 9, `v0.4.0` 3, `v1.0.0` 2 |
| Open bugs | 9 (4 `triage:needed`: #98, #99, #108 and gap #105; 5 `triage:scheduled`, two of them `blocking`: #58 → T35, #80 → T39) |
| Open follow-ups | 12; closed 3 |
| Open PRs | 1: #111 "Plan T43", a plan change awaiting the user's review (CLAUDE.md rule 4), CI green |
| CI on `main` | last 15 runs all `success`, all `push` events |
| Tags / releases | none |
| Commits | 148 on `main` since 2026-09-12 (this clone is shallow at 63); active days 09-12, 09-13, 09-14, 09-17, 09-18 |
| Code | engine 84 files / 10,486 lines; engine tests 93 files / 12,592 lines; `IC2.Data` 1,343; `IC2.Inspect` 937; `IC2.Cli` 133; Godot viewer 637 |
| Tests (static count) | 430 `[Fact]` + 26 `[Theory]` (123 inline rows) in the two test projects, plus 22 `[SkippableFact]` and one 55-row `[SkippableTheory]` that skip without `assets.local.ini` |
| Corpus | 397 entries, 388 `confirmed`, 9 `derived`, 0 `designed`, from 36 reports; the pinned report manifest lists 49 filenames, generated 2026-09-13 |

---

## 3. What is working well

These are worth naming so that the findings below are read in proportion.

- **The engine architecture matches the design principles and will scale to the remaining tasks.** `GameState` and every node are sealed records over `ValueList<T>`, so deep equality, canonical JSON and SHA-256 state hashing come for free (`src/IC2.Engine/Model/ValueList.cs`, `Core/State/GameStateHash.cs`). Systems are stateless, discovered by attribute, and totally ordered (phase, then `Order`, then ordinal id: `Core/Pipeline/SystemRegistry.cs:162-172`); the phase list is a closed enum. Adding a subsystem touches only its own folder, which is exactly what the Owns-list discipline needs.
- **Determinism is not a slogan here.** SplitMix64 with Lemire bounded draws, per-system and per-command named sub-streams derived from `(seed, name)`, so a new system or an extra draw cannot shift another system's rolls (`Core/Rng/RngStreams.cs`, `Core/Pipeline/TurnCoordinator.cs:144`, `Core/Commands/CommandDispatcher.cs:200,215`). A 494-line source scanner bans `System.Random`, wall-clock reads, `Guid.NewGuid` and unordered enumeration, has 16 self-tests, and asserts that zero suppressions exist (`tests/IC2.Engine.Tests/Core/Determinism/`).
- **Every number is data, and the tests prove it.** `RulesetNumbers.Enumerate` walks every numeric leaf of the ruleset and `NoHardcodedConstantsTests` mutates each one and checks the model tracks it. The corpus is cited by id in 70 places across 11 test files rather than retyped; `FixturesCorpusTests` checks that every cited report exists in the pinned manifest, so an invented citation fails CI offline. No `TODO`, `FIXME` or `HACK` exists anywhere in `src/`, `tests/` or `godot/`.
- **The review gates catch real defects.** The two defects the code review in this audit found independently in `GameSession` (the one-entry-per-event slice, and the season-name duplication) were already on the board as #98 and #99, filed by the reviewers of T41 and T42. `design-audit.md` §2 and the correction tasks T31–T35 show the same pattern: the process finds wrong constants before they ship.
- **Sensible course corrections have already been made.** The orchestrator layer was retired on 2026-09-14 when it proved to be overhead; T41 inserted a walking skeleton so something runs before Phase 3; T10 was moved from Haiku to Sonnet when it did not converge; T40 added a seam T03 had missed instead of patching around it.
- **The documents are honest.** `[confirmed]` / `[derived]` / `[designed]` / `[open]` tagging is applied consistently, `design-audit.md` records what was wrong with earlier versions of the design rather than erasing it, and operating-guide §7 lists what is still unknown.

---

## 4. Findings

### 4.1 The `v0.1.0` gate is met and no tag exists

[release-plan.md §2](release-plan.md#2-the-release-ladder) gates `v0.1.0` on T01–T12, T30–T34 and T40–T42. All 20 are `status:merged` as of T34's merge (#107, 2026-09-18 00:54). [§3.3](release-plan.md#33-how-this-interacts-with-branch-per-task-and-squash-merge) says the tag "is cut right after the `/run-task` merge of the last gating task" and "nothing else is dispatched until the tag exists". Instead: no tag (`git tag` is empty locally and on `origin`), no GitHub Release, and T38 is `status:in-progress`.

PR #111 (open, unmerged) adds T43 with `release:v0.1.0`, which retroactively widens the gate so that it is no longer met. That is a legitimate planning call, but it was made on the issue (#110 already carries the label and `status:ready`) before the plan PR merged, and the release plan was not amended to say the gate moved.

Why it matters: the 19-line release checklist ([§5](release-plan.md#5-release-checklist)) has never run. Its first run will surface friction the plan cannot see yet, for example item 5 (a fresh clone builds and tests green) against the LF/CRLF situation in §4.8, item 6 (the determinism guard "named individually in the output"), and the eight-section release note generated from `gh` queries. Better to find that at `v0.1.0`, which is the cheapest tag on the ladder, than at `v0.2.0` after T16.

### 4.2 The catalogue grows by correction

| | Count |
| --- | --- |
| Tasks the design named ("the 31 agent-run tasks", game-design.md §"The build harness") | 31 |
| Tasks in the catalogue now | 43 |
| Of the 12 added: corrections or completions of merged work (T31, T32, T33, T34, T35, T38, T39, T40, T42, T43) | 10 |
| Rules no task owned (T37) | 1 |
| Deliberate scope insertion (T41, the walking skeleton) | 1 |

Ten of twelve additions exist because a review or a research report showed merged code was incomplete or wrong. Merged tasks so far are the pure-rules ones, each a single subsystem with fully pinned constants. The 23 that remain include every integration-heavy task (T14 naval, T16 battle, T17 capture, T19 diplomacy, T20 save/load, T22 AI, T29 export, T24–T27 Godot). The rate of correction tasks per merged task should be expected to rise, not fall, from here.

This is not an argument against the review gates. It is an argument for two things: planning the remaining stretch with that ratio in mind, and making the corrections cheaper. Several correction tasks (T32, T40, T43) were each a day of process for a small diff because a correction gets the full task shape: catalogue entry, issue, branch, implementer, reviewer, merge, doc claims. The process could allow a lighter "fix" shape for a bug whose fix is one file and whose evidence is already pinned, still reviewed and still on a branch, without a catalogue entry.

Related: the pinned report manifest (`tests/fixtures/known-reports.json`) was generated on 2026-09-13 and lists 49 reports. The next ready task, T35, cites three reports that are not in it (`city-population-growth.md`, `upkeep-payment-and-desertion.md`, `nation-tax-base-and-city-economy-fields.md`). The process says the task tops the manifest up; it will, but every task that cites new research pays this, and the manifest cannot be regenerated by CI because the research repo is not cloned there.

### 4.3 Triage debt and label hygiene

- **Seven open follow-ups carry no `triage:*` label** (#87, #95, #100, #102, #104, #106, #109), so they are invisible to the queue query in operating-guide §1 (`--label triage:needed`) and to `triage:scheduled` alike. Three have zero comments. PR #111's body says #100, #106 and #109 are routed, but nothing on the issues records it.
- **Two retired labels are still load-bearing.** `triage:scheduled` sits on 5 bugs and 4 follow-ups, and `triage:deferred` on #67, although build-process.md §6 lists both as retired. The queue query therefore does not see the scheduled items either. Either the labels come back into the process, or those issues get the outcome comment and lose the label as §4.6 describes.
- **Milestones are stale.** "Phase 0 Foundation" and "Phase 1 Pure rules" have zero open issues but are still open. T37, T38, T39 (phase:2) and T40–T43 (phase:1) are on no milestone, so the milestone view under-reports Phase 2 by three and shows Phase 1 as complete while T43 is open.
- **T38 is `status:in-progress` with no PR from a `task/T38-*` branch.** Either an implementer is mid-work in a worktree, or the label was left behind by an interrupted session. build-process.md §5's recovery rule ("no branch at all → back to `status:ready`") applies at the next session start; CLAUDE.md rule 10 asks for exactly this check at session start.
- The issue template `.github/ISSUE_TEMPLATE/build-task.yml` (lines 11–12, 27) still says the issue is "the orchestrator's copy" and that "the orchestrator manages the `status:*` labels". The orchestrator was retired on 2026-09-14.
- Label hygiene on tasks themselves is clean: 43 of 43 carry exactly one status, one release, one phase, one lane, one model and one effort label.

### 4.4 The process runs on one machine only

`CLAUDE.md` rule 2, operating-guide §2.1 and build-process.md §7 and Appendices A–C hard-code `C:\Users\diego\projects\imperial_conquest_2` and `C:\Users\diego\projects\ic2-work\`. Both skills are git-ignored local installs. The consequence is that `/run-task` and `/process-evidence` can only be run from the user's Windows checkout: not from a cloud session such as this one, not from a second machine, and not by anyone reviewing the process from the outside. build-process.md §8 ("Adding a second machine later") acknowledges the Godot install and the original files are machine-bound, but the paths are the binding that would be cheapest to remove.

The repository already has the right pattern for this: `assets.local.ini`, a git-ignored file copied from a committed example. A `process.local.ini` (or two environment variables) naming the main checkout and the worktree root, read by the prompt templates and the skill, would make the templates portable without changing anything else.

A second effect of the same choice: the two skills' source of truth is a fenced block inside a document, reinstalled by hand. Committing them under `.claude/skills/` (currently excluded by `.gitignore` line 3) would remove the "reinstall verbatim from the fenced block" step and let a fresh clone run the process. If the exclusion is deliberate (the folder also holds local settings), an allow-list entry for the two `SKILL.md` files would do.

### 4.5 Documentation drift

Small and cheap, but the documents are the contract, so they are listed in full.

| Where | What | Fix |
| --- | --- | --- |
| `README.md` line 9 | "The foundation (Phase 0) is merged, and the pure-rules subsystems (Phase 1) are being built task by task" is a status snapshot (CLAUDE.md rule 3) and is stale: Phase 1 is complete. | Drop the sentence; keep the pointer to the labels. |
| `docs/release-plan.md` line 1 | Title says "the 31-task build"; the body says 42. The catalogue says 43. | Remove the count from the title. |
| `docs/release-plan.md` line 3 | Describes build-process.md as "an implementer/reviewer/orchestrator pipeline". | Drop "orchestrator". |
| `docs/game-design.md` line 340 | "the 31 agent-run tasks". | "the tasks in task-catalogue.md". |
| `docs/task-catalogue.md` line 1, line 6 | "the 42 build tasks", "42 tasks: … one rule no task owned (T37) …" | Will be 43 once #111 merges; consider dropping the count from the title so it stops going stale. |
| `docs/evidence-pipeline.md` line 3 | "the same relationship build-process.md Appendix C has to `/build-tick`". | `/run-task`. |
| `docs/operating-guide.md` §1.1 | "Phases 1–2 (T06–T22, T29, T32–T40)" omits T42 and T43 (T41 has its own row). | Adjust the range or say "the Phase 1 and 2 tasks". |
| `docs/release-plan.md` §6 | The `release:v0.1.0` row lists the gate without T43. | Add #110 when #111 merges, or say the gate is whatever the label says. |
| `.github/ISSUE_TEMPLATE/build-task.yml` lines 11–12, 27 | Orchestrator wording (§4.3). | Main session. |
| `src/IC2.Engine/IC2.Engine.csproj` lines 8–9 | "intentionally contains only a placeholder class". | Delete with `Placeholder.cs` (§4.8). |

Not drift, but worth knowing: `docs/build-orchestration-plan.md` is kept only so that old anchors resolve. That is a good decision, and the file says so.

### 4.6 CI

- **Every PR from a same-repo branch runs CI twice.** `.github/workflows/ci.yml` triggers on `push` to `branches: ["**"]` and on `pull_request`, so PR #111's single commit produced two `build-and-test` runs on the same SHA. Restricting `push` to `main` (PRs still get their run) or adding a `concurrency` group halves the minutes and removes the duplicate check rows.
- **The Godot project has no CI at all**, by design (the SDK is not on hosted runners). That is acceptable while `godot/` is a read-only viewer, but T24–T27 will make it the product. A workflow that installs Godot .NET on a runner (the official 4.x builds are downloadable, and headless export works on Linux) would let T24's DoD 1–4 run in CI instead of only on the user's machine. This is worth deciding before T24 is dispatched, because T24's DoDs are written around a local console binary.
- **The nightly gate (T28) is the last task in the build**, yet the 50-seed AI soak (T22 DoD 2) is meant to run in CI from `v0.3.0`. Nothing prevents adding the scheduled workflow earlier as a no-op that grows; T28's Haiku/Low sizing suggests it was always meant to be small.
- No dependency pinning problem was found: the engine has zero package references; tests use xunit 2.9.2 and the SDK test host.

### 4.7 Engine and CLI code

None of these is a shipped-gameplay bug. They are ordered by how much they matter to the remaining tasks.

1. **One gameplay default lives in C#.** `EconomyRules` declares `SupplyDialogArmyCapacityBonus = 1` after the `_provenance` parameter (`src/IC2.Engine/Model/Ruleset.cs:157-158`). Because `JsonContract` treats any parameter with a default as optional (`Model/JsonContract.cs:140`), a ruleset that omits the field loads silently with `1`, against the loader's stated "defaults nothing" contract. The toy ruleset carries the field, so nothing breaks, and `NoHardcodedConstantsTests` does not see constructor defaults. Make it required, and add a test that no rule-record constructor parameter has a default.
2. **Per-command full terrain decode.** `MoveArmyCommandHandler` calls `world.Terrain.Decode(...)` on every move (`Movement/Commands/MoveArmyCommandHandler.cs:58`), and `RenderMap` does the same; `IsBlocked` scans every city, army and fleet per step. Free on 3 cities; on the 320×140 world that is 44,800 cells per order, and the AI (T22) will issue hundreds per turn inside a 5-minute soak budget. A decoded-grid cache on `World` (it is immutable) fixes it in one place. Best done before T14 or T22, not after the soak fails its budget.
3. **Presentation duplicates ruleset data.** `GameSessionRendering.cs:18` hard-codes `{"Spring","Summer","Autumn","Winter"}` while the ruleset ships `newsLog.seasonNames`; line 185 hard-codes the `"fortify"` order id. Already filed as #99 in part; T23 should read both from the ruleset.
4. **The news tail slice in `GameSession.HandleEnd`** (`Presentation/GameSession.cs:227-234`) takes `newsAdded` entries while the writer appends two header entries per round and three for dash-wrapped kinds. Latent only because no engine event is `NewsWorthy = true` yet (the only occurrence in `src/` is a doc-comment example in `Core/Events/DomainEvent.cs:30`). Filed as #98; it becomes live with the first news-worthy event, which is T14's or T16's.
5. **Test probes ship in the production news catalog.** `NewsMessageCatalog.cs:141,260-262` carries `test.news-log.pipeline-probe.*` entries because the registered news systems resolve a static catalog with no injection seam. Give `NewsLogWriterSeatEnd` / `RoundEnd` a catalog seam (the writer's `Append` already takes a `templateFor` override) and move the probes into the test assembly.
6. **Both news writers use `Order = int.MaxValue`** (`NewsLogWriter.cs:485,536`). Documented and pinned by a whole-assembly test, but any future `SeatEnd`/`RoundEnd` system with the same order and a later-sorting id silently loses its news lines. A named constant one below, with the pin, would make the intent enforceable.
7. **`Assets/` is in a different style from the rest of the engine**: no argument validation, duplicated sync/async bodies (`AssetLoader.cs:24-53`), no manifest schema validation, and tier thresholds documented in comments (`AssetKeys.cs:39-77`) that `MapMarkerRules` already carries as data. T24 consumes this; worth aligning before it does.
8. **`IC2.Cli` finds the repository root by walking up to `IC2.sln`** (`Program.cs:117-132`) and hard-codes the `toy-3city` scenario id (`:67`). Correct for T41; T23 and T27 must replace the root-finding convention (shared with the test `TestPaths`/`FixturePaths` helpers) before a published binary can run.
9. **`SupplyCapacity.PercentFull` divides by troops** and `GameSessionRendering.cs:53` calls it unguarded, so a zero-troop army in state would crash `status`. Not reachable with shipped data; a floor at the call site closes it.
10. **Effectively the whole engine is public**: 189 public types against 5 internal, including test-oriented helpers. Harmless now; an `InternalsVisibleTo` for the test assembly would let later tasks keep seams internal.
11. **`IC2.Inspect`'s catch filters miss `InvalidDataException`** in all 13 command blocks (`src/IC2.Inspect/Program.cs`), the exception every `IC2.Data` parser throws on malformed input, so a corrupt save prints a stack trace instead of the friendly message. `IC2.Data` itself is bounds-safe throughout: `SaveFormat.Detect` and `SaveNationLayout.Locate` check every offset before later parsers read it.

The `IC2.Data` and `IC2.Inspect` projects deliberately stay outside `TreatWarningsAsErrors` (Directory.Build.props). That was right for T01. T29 and T21 will build on `IC2.Data` directly, so bringing it under the strict settings, in its own small task, would be cheap insurance before those.

### 4.8 Repository hygiene

- **Line endings contradict the editorconfig.** `.editorconfig` says `end_of_line = crlf`, but all 211 tracked `.cs` files and every `.md` are LF in the store; there is no root `.gitattributes`, and the only one (`tests/fixtures/cli/.gitattributes`) exists because the author's machine has global `core.autocrlf=true`. Windows working copies see CRLF, CI and Linux see LF, and an editorconfig-aware editor on either side will try to "fix" files. Decide once (LF in the store is the usual answer), add a root `.gitattributes` with `* text=auto` and explicit `eol=lf` for source, and change the editorconfig line to match. This is also what makes release-checklist item 5 ("a fresh clone builds") reproducible on any machine.
- Two files carry a UTF-8 BOM against `charset = utf-8` (`src/IC2.Inspect/Program.cs`, `src/IC2.Inspect/SaveJsonExporter.cs`); two generated JSON files lack a final newline (`assets/packs/placeholder/manifest.json`, `tests/IC2.Data.Tests/CorpusFixtures/expected-corpus-outcomes.json`), so their generators omit it.
- `src/IC2.Engine/Placeholder.cs` and both `PlaceholderTests.cs` files are T01 scaffolding that 40 tasks later still exist.
- The Godot viewer is excluded from the solution and from `Directory.Build.props` with clear comments in three places; that is consistent and well explained.

---

## 5. The direction

### 5.1 The near-term path is right

The critical path from here is `T35 → T38 (in progress) → T14 → T16 → T17 → T29 → T36 → T24 → T25 → T27`, with T39 and T13 alongside. T35 is ready and gates four tasks; T38 gates T14; T16 needs T14. The ordering in the catalogue is correct, and the decision to put the two Opus tasks (T16, T22) behind the user's own `/code-review --effort ultra` is the right place to spend human attention. Nothing here suggests re-sequencing the next five tasks.

Two observations about that stretch:

- **T16 is the first task where the evidence is deliberately partial.** Its hazards section holds the type-effectiveness matrix, the melee cap and the rout mechanic in reserve, and operating-guide §7 lists the melee random term as still unknown. The DoD is written around the instant resolver, which is fully decompiled, so T16 is safe; but the reserved research is exactly what a tactical battle screen would need, and `game-design.md` keeps that screen as a post-1.0 option. Nothing to change now, only to keep the `BattleResult` seam presentation-agnostic as T16's scope already says.
- **T29 freezes the ruleset schema** (build-process.md §2.6). After it, every schema change costs a local-only re-export plus an `improved.json` edit. T15, T17, T19 and T37 must merge before it, and T20 (save/load) does not, which means the save format is designed after the schema is frozen. That is fine as long as T20 adds no ruleset fields; its entry should say so explicitly.

### 5.2 The Godot lane is the largest untested risk, and it is last

Everything that makes this project unusual (evidence-pinned rules, determinism, the two presets) is proven by tests. What is not proven by anything yet is that the engine's `Presentation` layer can drive a Godot scene, that `MapViewer.cs` (637 lines of read-only research-phase code) can be extended rather than rewritten, that a headless Godot script can exercise the command layer, and that the New Game flow's two-card chooser is buildable in the time T24 is given (Sonnet / High, one task). T24 also waits on T29's exported world, so the first Godot work begins after every engine task.

T41 solved the equivalent problem for the CLI: a thin walking skeleton, inserted early, that later tasks extend rather than start over. The same move is available for Godot and does not need the real world: **a thin Godot slice on the toy 3-city world**, showing the map from `GameState` rather than from a `.sav`, issuing one `MoveArmy` through the dispatcher, ending a turn, and printing the news log. It would need only T02, T03, T09, T10 and T41, all merged. Its value is the seam it forces into existence (the engine-to-scene binding, the headless script, the churn guard in a real workflow) months before T24 has to build a whole screen on it, and it would settle the CI question in §4.6 while it is still cheap. It would be `single-instance` and human-reviewed like T24, and it would be the first thing the user can see. Recommended as a new task with the same shape as T41, sequenced anywhere before T16.

### 5.3 The AI soak proves less than it seems

T22's DoD runs 50 seeds of an all-AI toy scenario to a victory condition or a turn cap in five minutes. On three cities that proves termination and the absence of crashes; it says nothing about the AI on 334 cities, where the original's victory condition is total conquest and T22's own hazard note says the risk is a non-terminating game. The nightly gate T28 is where a classical-world soak belongs, and it is the last task in the build, so no long game will have been played end to end before `v1.0.0`'s checklist requires one. Consider adding a "the classical scenario runs 100 turns all-AI inside N minutes" line to T28, or to T22 as a `local-only` line, so the performance concern in §4.7 item 2 is measured, not discovered.

### 5.4 Keep the process; make corrections and portability cheaper

The process was simplified once already and the result is visibly better than the pre-simplification history ("Docs: status resync" appears five times on 2026-09-14 alone, and never after). The remaining overhead is concentrated in three places, all addressed above: the full task shape for one-file corrections (§4.2), the untriaged follow-ups (§4.3), and the machine-bound paths and skills (§4.4). None requires a new document or a new role.

One rule deserves a second look. build-process.md §4.6 says a defect in merged code is "never patched by the task that found it, even narrowly, even when the fix is one line", and the task is suspended. That is the right default for a fidelity defect. For a defect that blocks the task's own DoD and whose fix is mechanical (T43's "eliminated-skip makes the branch dead code" is the shape), suspending the task, filing, planning a T-number, running it through review, and resuming costs a day of wall time to protect against a risk the reviewer's Owns-list check would catch anyway. A one-line allowance ("a blocking mechanical fix may ride the same PR, declared under 'Scope' and reviewed as an Owns-list exception") would keep the audit trail and lose the day.

### 5.5 What the design does not yet decide

`game-design.md`'s "left for later" list is short and honest. Two things are not on it and will become decisions before `v0.4.0`:

- **Save-format policy for the `improved` preset.** T20's versioning covers the schema; nothing says whether a save started under `classical-faithful` may be loaded under `improved` (the ruleset id is in `SaveGame`). Probably "no", but it should be written down before T20.
- **What "Load" on the main menu loads.** Original `.sav` import (T21) is `local-only` and imports onto `classical-faithful` only by policy; the packaged build (T27) ships without original files. So the Load screen in a packaged build sees native saves only, and the import path is a developer feature. T24's scope should say so, or the packaged build will look like it lost a feature.

---

## 6. Recommended actions

In priority order. "Owner" is who the process says decides; none of these was applied by this audit.

| # | Action | Owner | Cost |
| --- | --- | --- | --- |
| 1 | Decide whether `v0.1.0` is cut now or after T43, amend release-plan.md §2/§6 accordingly, and run the 19-line checklist for real (§4.1). | User, then main session | Small; the checklist run is the work |
| 2 | At the next session start, resolve T38's `status:in-progress` per build-process.md §5 (resume the worktree or reset to `ready`) (§4.3). | Main session | Minutes |
| 3 | Triage the seven unlabelled follow-ups; retire or re-adopt `triage:scheduled`/`deferred`; close the Phase 0/1 milestones; put T37–T43 on milestones (§4.3). | Main session | An hour |
| 4 | Root `.gitattributes`, editorconfig `end_of_line` aligned, BOMs and final newlines fixed, `Placeholder.cs` and its tests and the csproj comment removed (§4.8). | Main session, one hygiene PR | Small |
| 5 | The documentation drift table in §4.5, in one "Docs:" commit on `main` (routine claims) except the release-plan gate row, which belongs with action 1. | Main session | Small |
| 6 | CI: run `push` only on `main`, or add a `concurrency` group (§4.6). | Main session | One line |
| 7 | Make `SupplyDialogArmyCapacityBonus` required and add the "no rule-record defaults" test (§4.7 item 1). Fold into T35, which owns `Ruleset.cs`'s economy block. | T35 | Small |
| 8 | Cache the decoded terrain grid on `World` (§4.7 item 2). Fold into T14 (first heavy caller) or a small correction before T22. | Planner | Small |
| 9 | Plan a thin Godot slice on the toy world, T41-shaped, before T16 (§5.2). Decide the Godot-on-CI question with it (§4.6). | User (new task, plan branch) | Medium |
| 10 | Parametrise the machine paths (`process.local.ini` or env vars) and commit the two `SKILL.md` files (§4.4). | User (process change, plan branch) | Small |
| 11 | Add a classical-world all-AI soak line to T28 or T22 (§5.3), and write down the two undecided policies in §5.5 before T20/T24. | User (catalogue change) | Small |
| 12 | Consider the lighter correction shape and the blocking-mechanical-fix allowance (§4.2, §5.4). | User (process change) | A paragraph in build-process.md |
| 13 | Bring `IC2.Data` under the strict build settings in its own small task before T29/T21; fix `IC2.Inspect`'s catch filters with it (§4.7 item 11). | Planner | Small |
| 14 | The remaining §4.7 items (news catalog seam, `Order` constant, `Assets/` style, CLI root-finding, `PercentFull` guard, `InternalsVisibleTo`) as follow-ups folded into T23, T24 and the next news-touching task. | Main session (follow-up issue) | Small each |

---

## 7. Questions for the user

Posed, not decided, in the style of design-audit.md §3.

- **Q-1. Cut `v0.1.0` now, or after T43?** The plan as written says now (the gate was met at #107); the label as applied says after T43. Either is defensible; the plan and the label should agree, and the checklist should run once at this cheap tag.
- **Q-2. Should a thin Godot slice be inserted before T16?** It is the one piece of the build with no early proof and it is at the end of the critical path. Cost is one medium task; the gain is that T24 extends something instead of building everything.
- **Q-3. Should the process become portable?** Parametrised paths and committed skills would let the build run from a second machine or a cloud session. If the answer is "one machine is fine", build-process.md §8 should say so explicitly rather than describing a future that the hard-coded paths rule out.
- **Q-4. Should one-file corrections get a lighter shape?** Ten of the last twelve tasks were corrections. The full shape costs about a day each; a reviewed fix PR without a catalogue entry would keep the trail and the gates.
- **Q-5. Godot in CI: yes or no?** If yes, it should exist before T24 is dispatched so its DoDs can be written against it.
