# Operating guide

The entry point for anyone operating the Imperial Conquest 2 project — the user, and every agent session. It says where the build stands, where everything lives, how the sessions and skills are run, and which rules and preferences apply. The process contract is [build-process.md](build-process.md); the tasks are in [task-catalogue.md](task-catalogue.md).

---

## 1. Current state

A snapshot written by the documentation step ([build-process.md §4.8](build-process.md#48-documentation-update-after-every-merge), location A2) and checked against the labels every tick; between syncs, [tracking issue #29](https://github.com/diegoami/imperial_conquest_2/issues/29) and the GitHub labels are authoritative.

- **As of**: `a53eaa5` (T08 merged).
- **Phase**: Phase 0 (foundation) merged; Phase 1 (pure rules) under way.
- **Merged — 12 of 38**: T01, T02, T03, T04, T05, T06, T07, T08, T09, T30, T31, T32. Per-task status and merge commits: [task index](task-catalogue.md#3-task-index).
- **In progress**: T10 (#10) — news log ring buffer and message catalog, in review ([issue #10](https://github.com/diegoami/imperial_conquest_2/issues/10)).
- **Ready**: T11, T12, T33, T34, T35, T38. T14 waits for T38.
- **Next (planner)**: T08 (#8) merged as `a53eaa5` after user-authorized rework round 3 (scoped to review finding R1). Its non-blocking review findings are collected as [#76](https://github.com/diegoami/imperial_conquest_2/issues/76), `triage:needed`; a stale corpus attribution the round-3 reviewer found outside the PR is filed as bug [#75](https://github.com/diegoami/imperial_conquest_2/issues/75), `triage:needed`, not blocking. T08's merge unblocked T14 and T35, both now `status:ready`. The orchestrator mandate continues: T10 is in review; T11, T12, T14, T33, T34, T35 remain to dispatch ([build-process.md §5.1](build-process.md#51-who-runs-it)).
- **Open bugs** ([build-process.md §4.7](build-process.md#the-triage-queue)): [#46](https://github.com/diegoami/imperial_conquest_2/issues/46) `triage:scheduled` → T33 (Blocks: T16, T17); [#47](https://github.com/diegoami/imperial_conquest_2/issues/47) `triage:scheduled` → T33 (Blocks: T16, T17); [#52](https://github.com/diegoami/imperial_conquest_2/issues/52) `triage:scheduled` → T33 (not blocking); [#57](https://github.com/diegoami/imperial_conquest_2/issues/57) `triage:scheduled` → T34 (not blocking); [#58](https://github.com/diegoami/imperial_conquest_2/issues/58) `triage:scheduled` → T35 (`blocking` — Blocks: T13, T17, T19); [#59](https://github.com/diegoami/imperial_conquest_2/issues/59) `triage:scheduled` → T35 (not blocking); [#68](https://github.com/diegoami/imperial_conquest_2/issues/68) `triage:scheduled` → T35 (not blocking); [#69](https://github.com/diegoami/imperial_conquest_2/issues/69) `triage:scheduled` → T37 (not blocking); [#75](https://github.com/diegoami/imperial_conquest_2/issues/75) (T04's corpus, found by T08's round-3 review) `triage:needed` — `supply.capacityFormula.army` misattributes `troops / 100` to `TAFSupply_ChangeBuyAmount` (the dialog cap is `troops / 100 + 1`; T08 added the correct entries alongside it), not blocking, not yet triaged.
- **Open review follow-ups** (non-blocking, [build-process.md §4.5](build-process.md#45-rework)): [#40](https://github.com/diegoami/imperial_conquest_2/issues/40) (T30) `triage:scheduled` → T34 (items 1–7) and T24 (item 2's `MapViewer.cs` half, DoD 6); [#43](https://github.com/diegoami/imperial_conquest_2/issues/43) (T06) `triage:scheduled` → T32 (test-infra items, DoD 3) and T17 (seat-rotation elimination handling, DoD 8); [#49](https://github.com/diegoami/imperial_conquest_2/issues/49) (T07) `triage:scheduled` → T33 (DoD 6); [#67](https://github.com/diegoami/imperial_conquest_2/issues/67) (T32) `triage:deferred` — 1 non-blocking review item (testbed registry caching asymmetry), deliberately deferred (documented, intentional asymmetry vs. T03's `CoreTestbed`); [#72](https://github.com/diegoami/imperial_conquest_2/issues/72) (T08) `triage:scheduled` → T35 (exact quarterly loyalty draws, added once T08 merges); [#74](https://github.com/diegoami/imperial_conquest_2/issues/74) (T09) `triage:needed` — 2 non-blocking review items (N1: `TerrainCostLookup`/`Ruleset.MoveCostFor` duplication, candidate T14 or T23; N2: missing steep-Bresenham test case, candidate the next task touching `tests/IC2.Engine.Tests/Movement/**`), not yet triaged; [#76](https://github.com/diegoami/imperial_conquest_2/issues/76) (T08) `triage:needed` — 9 non-blocking review items across rounds 1 and 3 (N1–N3, N8, N9–N13) plus 3 items the round-3 authorization deferred (surplus take-back above cap, the foreign-purchase credit destination, the automatic-resupply path), not yet triaged.
- **Runnable today**: tests only. `dotnet build IC2.sln`; `dotnet test IC2.sln` — 420 tests (342 engine, 78 data); 65 of the data tests read the original files and skip without `assets.local.ini`. The first runnable program is T23's CLI; the first UI is T24.

### 1.1 What becomes runnable, and when

| After | What exists | Can you run it? |
| --- | --- | --- |
| Phase 0 (T01–T05, T30, T31) | Solution, CI, domain model, fixtures corpus, engine seams, hardened `IC2.Data` | `dotnet build` / `dotnet test` only |
| Phases 1–2 (T06–T22, T29, T32–T36) | The rule subsystems, then recruitment, naval, battle, diplomacy, AI; the exported 334-city world, `classical-faithful` ruleset and classical scenario (T29), and the `improved` preset (T36) | Only through their tests |
| **T23** | `IC2.Cli`, a scriptable headless play harness | **First thing you can run**: load a scenario, issue orders, end turns, text output |
| **T24** | Godot main screen, New Game flow, the ruleset chooser | **First thing that looks like a game** |
| T25–T28 | Remaining screens, packaging, the nightly gate | A complete, playable build |

### 1.2 Where to look for live progress

| Where | What it shows |
| --- | --- |
| [Issue #29](https://github.com/diegoami/imperial_conquest_2/issues/29), pinned | The dashboard: one status table per orchestrator tick. |
| #29's orchestrator-state comment (starts `<!-- orchestrator-state -->`) | The orchestrator's mandate, current task and phase, and open escalations — intent only; the labels are the facts ([build-process.md §5.2](build-process.md#52-where-the-state-lives)). |
| Task issues, `status:*` labels | Each task's exact stage (`gh issue list --label status:in-review`, etc.). |
| `bug` label | Defects in merged code (`gh issue list --label bug --state open`). |
| `triage:*` labels | The planner's queue of bugs and follow-ups: `triage:needed` (untriaged — `gh issue list --label triage:needed --state open`), `triage:scheduled` (folded into a task or given a correction task), `triage:deferred` (deferred with a reason) — [build-process.md §4.7](build-process.md#the-triage-queue). |
| `blocking` label | Bugs that block at least one task; each opens with a `Blocks: T<nn>` line ([build-process.md §4.7](build-process.md#what-blocking-means)). `gh issue list --label blocking --state open`. |
| Pull requests | One per dispatched task, with the reviewer's findings comment and CI. |
| Milestones | One per phase, with GitHub's progress bar. |
| `release:*` labels | Which release a task gates ([release-plan.md](release-plan.md)). |
| [Actions](https://github.com/diegoami/imperial_conquest_2/actions) | Build and test on every push and PR. |

---

## 2. Where things live

### 2.1 The two repositories

- **This repository**, [`diegoami/imperial_conquest_2`](https://github.com/diegoami/imperial_conquest_2) — design, plans, and all code (`IC2.Data`, `IC2.Inspect`, `IC2.Engine`, `IC2.Cli`, `godot/`). Local checkout: `C:\Users\diego\projects\imperial_conquest_2`.
- **The research repository**, [`diegoami/imperial-conquest-2-research`](https://github.com/diegoami/imperial-conquest-2-research) — every reverse-engineering report (`docs/reports/`, 47 reports), the roadmap, the decompilation plan, research notes. Read with `gh api repos/diegoami/imperial-conquest-2-research/contents/<path>`; writing needs a fresh clone into a scratch directory. For static-analysis work, start from its `docs/decompilation-plan.md`, the live record of what has been decompiled.

| Document | What it is |
| --- | --- |
| [README.md](../README.md) | Front door: what the project is, current state in brief, how to build, the inspector tools |
| [operating-guide.md](operating-guide.md) | This document |
| [CLAUDE.md](../CLAUDE.md) | Auto-loaded into every Claude Code session: a pointer to this guide and the must-never-forget rules |
| [build-process.md](build-process.md) | Process contract: roles, review and merge, orchestrator loop, bug list, documentation step, prompt templates, `/build-tick` |
| [task-catalogue.md](task-catalogue.md) | The 38 tasks, the dependency graph, the task index and status |
| [game-design.md](game-design.md) | What is being built |
| [design-audit.md](design-audit.md) | What the evidence supports, and the design questions Q1–Q10 |
| [release-plan.md](release-plan.md) | Versions, release gates, release notes, the release checklist |
| [evidence-pipeline.md](evidence-pipeline.md) | The `/process-evidence` pipeline and its skill text |
| [investigations/README.md](investigations/README.md) | Index of this repo's evidence write-ups |

### 2.2 The original game files

Never in either repository — and neither is anything derived from them: the Ghidra project and the decompiled-text dumps under `%LOCALAPPDATA%\ReTools` stay local too. `assets.local.ini` at this repo's root (git-ignored, copied from `assets.example.ini`) points at the user's own installation, currently `C:\Users\diego\Documents\imp_conq_original` (a local git repository of its own). If it is missing, ask the user to configure it; never guess a path. Inside that directory:

| Folder | Holds |
| --- | --- |
| `saves/` + `saves-processed/` | Save files; move a save to `saves-processed/` once a report cites it |
| `recordings/` + `recordings-processed/` | Screen recordings (`.mp4`), same convention |
| `screenshots/` + `screenshots-processed/` | Screenshots (`<save-number>.<image-number>.png`), same convention |
| `notes/` | The user's session notes: a save pair, an optional recording, the events observed between them |
| (root) | `Imperial Conquest 2.exe`/`.dat`/`.hlp`; `WAVS/` (converted to 16-bit/44.1 kHz PCM — the untouched originals are in `WAVS - Copy/`); `patch_exe.py` (builds instant-battle and message-pumping EXE variants for recording). The in-game battle pacing delay is a per-nation preference (`TBattleDelays` dialog) and can be set to zero from inside the game. |

### 2.3 Local toolchain (outside both repositories)

- `gh` (authenticated as `diegoami`), `jq`, Python 3.14, Node, the .NET 10 SDK, Godot 4.7.2 (.NET).
- **ffmpeg** at `%LOCALAPPDATA%\ReTools\ffmpeg-master-latest-win64-gpl\bin\ffmpeg.exe` — `ffmpeg -ss <startSeconds> -i "<recording.mp4>" -vf fps=1 -frames:v <N> <outdir>/f_%03d.png`, then read the frames. The tactical battle's combat-resolution panel gives exact per-exchange troop counts, and the user will record more battles on request.
- **`%LOCALAPPDATA%\ReTools\`** — Temurin JDK 21 (`jdk-21.0.12.1+1\`), Ghidra 12.1.3 (`ghidra_12.1.3_PUBLIC\`), the imported and analysed project (`ghidra_projects\IC2\`), and `scripts\`: `ExportFunctions.java`, `ExportAddresses.java` (works on mid-function addresses), `ExportAllInRange.java`, `FindXrefs.java`, `FindCallers.java`, `FindString.java`, `DumpMemory.java`, `ImportDelphiSymbols.java`, plus `delphi_symbols.tsv`/`.json` (282 method names across 31 classes — look an address up here before deriving one) and `all_app_functions.txt` (every function in 0x401000–0x460000, decompiled — grep this before running Ghidra).
- To run Ghidra headless: set `$env:JAVA_HOME` to the JDK, then `ghidra_12.1.3_PUBLIC\support\analyzeHeadless.bat <projectDir> IC2 -process "Imperial Conquest 2.exe" -noanalysis -scriptPath <scriptsDir> -postScript <Script>.java <args...>`.

---

## 3. How to operate the project

### 3.1 Sessions and roles

The **main session runs on Opus and is the planner.** It talks to the user, owns the task catalogue and the process, triages bugs and follow-ups, runs `/process-evidence`, and brings design decisions to the user. **At the start of every session, and before spawning any orchestrator mandate, it checks the triage queue** (`gh issue list --label triage:needed --state open`) and triages it or tells the user; it never spawns a mandate while an untriaged `blocking` bug (`gh issue list --label blocking --label triage:needed --state open`) names a task in that mandate's scope in its `Blocks:` line ([build-process.md §4.7](build-process.md#what-blocking-means)). To advance the build it **spawns an orchestrator agent with a bounded mandate** — a scope, stop conditions, and a report-back contract ([build-process.md §5.1](build-process.md#51-who-runs-it), template in [Appendix D](build-process.md#appendix-d-orchestrator-mandate-template)). **It never runs `/build-tick` itself, and never a `/loop` of it.**

| Role | Dispatched as | Works in | Reference |
| --- | --- | --- | --- |
| Planner | The main session | Its own worktree for anything it writes; plan changes go to a branch for review | [build-process.md §3.1](build-process.md#31-the-roles), [§4.7](build-process.md#47-the-bug-list) |
| Orchestrator | Agent spawned by the planner, one at a time, bounded mandate | GitHub only (labels, PRs, #29); writes no repository files | [build-process.md §5](build-process.md#5-the-orchestrator), [Appendix C](build-process.md#appendix-c-the-build-tick-skill) |
| Implementer | Subagent of the orchestrator, model per catalogue | The shared main checkout, alone | [build-process.md Appendix A](build-process.md#appendix-a-implementer-prompt-template) |
| Reviewer | Subagent of the orchestrator, model per catalogue | The shared main checkout, alone | [build-process.md Appendix B](build-process.md#appendix-b-reviewer-prompt-template) |
| Documentation | Sonnet subagent of the orchestrator, after every merge | Its own worktree; pushes straight to `main` | [build-process.md §4.8](build-process.md#48-documentation-update-after-every-merge) |
| Researcher | Opus subagents of the planner, the two `/process-evidence` stages | A research-repo clone; stage 2 in its own worktree here | [evidence-pipeline.md](evidence-pipeline.md) |

The orchestrator escalates to the planner, never to the user; the planner brings the decision to the user and replies to the orchestrator.

### 3.2 The two skills

Both are **local, git-ignored installs** under `.claude/skills/`; the fenced text in the repository is the source of truth. If a skill is missing, reinstall it verbatim from its fenced block. Skills load when a session starts, so a newly installed skill needs a fresh session.

| Skill | Installed at | Reinstall from | What it does |
| --- | --- | --- | --- |
| `/build-tick` | `.claude/skills/build-tick/SKILL.md` | [build-process.md Appendix C](build-process.md#appendix-c-the-build-tick-skill) | One orchestration tick — run only by the orchestrator agent, never by the main session |
| `/process-evidence [path]` | `.claude/skills/process-evidence/SKILL.md` | [evidence-pipeline.md](evidence-pipeline.md#the-actual-skill-file) | New saves/recordings/notes → research findings → design implications |

### 3.3 Working rules

- **One code-modifying pipeline agent at a time.** Implementers and reviewers work alone in the shared main checkout; no worktrees for pipeline agents ([build-process.md §7](build-process.md#7-concurrency-single-instance-and-local-only)).
- **The planner's own writes go in a worktree on a new branch.** Before touching the shared checkout at all — any file, tracked or git-ignored — check `ListAgents`. If a pipeline agent is live there, do the work in `git worktree add <sibling-path> -b <new-branch> origin/main`. Even a read-only pass gets its own worktree and branch, because the edits that follow will need one. The orchestrator writes no repository files at all.
- **Branch or `main` is decided case by case.** Routine post-merge documentation sync goes straight to `main` (from a worktree); new or substantive content (a design correction, a new mechanism, catalogue changes) goes to a branch for review. When unsure, ask.
- **Review is a label, not a GitHub review.** The reviewer applies `status:approved` or `status:rework`; the orchestrator reads the label ([build-process.md §4.1](build-process.md#41-the-path-a-task-takes)).
- **Relay reviewer findings verbatim.** On rework, the implementer gets the reviewer's full findings, never a hand-picked subset.
- **Pausing**: `gh issue edit 29 --add-label orchestrator:pause`, from any session. To resume, remove the label, then have the planner spawn or resume an orchestrator with a bounded mandate ([build-process.md §5.5](build-process.md#55-user-initiated-pause)).
- **If a session is interrupted**, nothing is lost: the facts are in the labels (`status:*`, `review-round:*`, `docs:pending`, and `triage:*` for the bug/follow-up queue), the orchestrator's mandate and phase are in #29's state comment, and implementers push work in progress to their task branch. The planner spawns a fresh orchestrator with the recorded mandate, and it runs the recovery procedure first ([build-process.md §5.6](build-process.md#56-recovery-after-an-interruption)).

### 3.4 Bugs

A defect in already-merged code is filed as a `bug` issue and the task that found it is suspended — never patched from inside another task's Owns list. Non-blocking review findings go to a `T<nn> follow-up` issue instead ([build-process.md §4.5](build-process.md#45-rework)). **Both are filed with `triage:needed`** — by the orchestrator, the documentation subagent, or `/process-evidence` stage 2 — and that label is the planner's queue. Triage replaces it with `triage:scheduled` (folded into a task — for a follow-up, the next task that touches its files — or a new correction task, named in a comment) or `triage:deferred` (reason in a comment); nothing is closed without one of the two ([build-process.md §4.7](build-process.md#the-triage-queue)). A bug that blocks a task also carries `blocking` and opens with `Blocks: T<nn>` — set by the finder for a task in flight, by triage for a future task, which also records the dependency in the task catalogue ([build-process.md §4.7](build-process.md#what-blocking-means)). The open ones and their triage state are listed in [§1](#1-current-state).

### 3.5 New evidence and how it reaches the build

`/process-evidence` runs two sequential Opus stages ([evidence-pipeline.md](evidence-pipeline.md)): stage 1 writes the research-repo report (straight to that repo's `main`); stage 2 applies the post-merge documentation checklist ([build-process.md §4.8](build-process.md#48-documentation-update-after-every-merge)) to the new evidence, on a review branch. Each finding takes one of four routes:

- a corrected fact or closed `[open]` item in `design-audit.md` / `game-design.md` (or another part-B document);
- a defect in merged code → a `bug` issue labelled `triage:needed`, triaged by the planner;
- a change to a not-yet-dispatched task's scope or DoD → a catalogue edit on the review branch (a DoD only changes by a reviewed commit, [build-process.md §4.4](build-process.md#44-the-dod-is-not-negotiable-by-an-agent));
- a design decision → posed to the user, never decided.

### 3.6 Keeping documentation current

Every merge is followed, in the same tick, by the documentation step ([build-process.md §4.8](build-process.md#48-documentation-update-after-every-merge)), which syncs all status locations — the catalogue's entries and index, [§1](#1-current-state) of this guide, the README's current-state summary, and [release-plan.md §2.1](release-plan.md#21-gate-progress) — and applies its claim checklist. Every tick also checks the catalogue's status against the GitHub labels and resyncs on drift.

### 3.7 Merging to main without disturbing running agents

For any merge while a pipeline agent may be working — a docs branch, a planner branch, a task PR:

1. **Check first.** Run `ListAgents`. If an implementer or reviewer is live in the shared checkout, don't touch that directory at all: no checkout, no pull, no file writes.
2. **Confirm it's safe.**
   - `git merge-tree $(git merge-base origin/main <branch>) origin/main <branch>` shows no conflicts.
   - CI on the PR is green.
   - The diff doesn't touch the running task's Owns paths ([task-catalogue.md](task-catalogue.md)).
3. **Merge on GitHub's side.** Run `gh pr create` if there's no PR yet, then `gh pr merge --squash`. This touches no local working tree.
4. **Prepare in isolation.** Any conflict resolution or fix-up happens in a separate `git worktree` on its own branch ([§3.3](#33-working-rules)). Push it, then merge through GitHub.
5. **After merging:**
   - Remove the worktree and delete the branch.
   - Don't pull the new `main` into the shared checkout while an agent is live there. The running task's branch catches up by rebasing before its own merge, or through the drain step's `gh pr update-branch` ([build-process.md §5.3](build-process.md#53-one-tick)).
   - The post-merge documentation update also runs in its own worktree and pushes straight to `main` ([build-process.md §4.8](build-process.md#48-documentation-update-after-every-merge)).
6. **Caveat.** A merge can still reach a running task by changing a document it reads, for example a doc split or rename while an implementer is using it. Leave a redirect, or point the running agent at the new location.

---

## 4. Standing user preferences

Kept in step with the auto-memory feedback notes; when a preference changes, update it here in the same session.

- **Commit and push reverse-engineering work without asking** — new reports, roadmap updates and research-repo fixes go straight to the research repo's `main`. Still no destructive git operations (force-push, amend, `reset --hard`) without an explicit request.
- **A question is not a request to change files.** Answer it; if a fix turns up along the way, propose it and wait.
- **Isolate your own writes when anything is running**: worktree plus new branch ([§3.3](#33-working-rules)).
- **Branch or `main`, case by case**: routine post-merge doc sync straight to `main`; novel content on a branch for review; ask when unsure.
- **Upstream defects go through the bug list** — suspend, file, plan, resume; never an ad-hoc cross-Owns-list patch.
- **Evidence goes through the two-stage pipeline** — `/process-evidence`, stage 1 then stage 2, never combined.
- **Relay reviewer findings verbatim** on rework.
- **Non-blocking review findings become one follow-up issue per merge**, filed by the orchestrator; the planner folds each item into the next task that touches those files ([build-process.md §4.5](build-process.md#45-rework)).
- **The main session is Opus and is the planner.** It spawns an orchestrator with a bounded mandate to run the build, and researcher subagents for evidence; it never runs `/build-tick` or dispatches implementers and reviewers itself.
- When correcting a claim after user feedback, fix the document or report text itself, not only the chat.
- Do not re-suggest a Windows 9x VM on this machine: WSL2's Hyper-V claims VT-x, and the user will not disable WSL2.

---

## 5. Collaboration norms

- Keep the user informed while working: say what you intend to do, give short progress updates (what you examined, what the evidence shows, what changed and why, what remains uncertain), and end with the outcome, the verification done, the remaining limitations, and where to see the result. Do not go more than about a minute of active work without an update, and do not dump raw command output.
- In reverse-engineering work, distinguish observations from inferences, and name the save, screenshot, recording or binary structure that supports each conclusion. Record exact file, hash, address or screenshot evidence for each new field or formula, and mark an inferred meaning as a candidate until it is checked.
- For routine save-format checks, read and validate three or four representative saves, not every save. Choose samples that cover the relevant before/after event or format variation; widen the sample only when a discrepancy or specific question requires it, and say why.

---

## 6. Practical caveats

- **Godot headless churn.** Running Godot headless against `godot/` (`"<Godot install>\Godot_..._console.exe" --headless --path godot --quit-after 2`) regenerates `godot/project.godot`'s header and flips `godot/MapViewer.cs`'s line endings. `scripts/check-godot-churn.ps1` reverts the two files when the diff is header/whitespace-only; run it after any headless Godot run, before committing.
- **Build order for the inspector.** Build `IC2.Data` before `IC2.Inspect` when both changed (shared `obj` directory). `IC2.Inspect` commands are listed in the [README](../README.md#the-research-inspector-tools-ic2inspect).
- **Ghidra's reference manager misses some string references** in this Delphi build (for example the DAT filename and `"falls to"`). Grep `all_app_functions.txt` instead — the decompiler inlines string literals even when no reference was recorded. Call xrefs work, except across virtual method calls.
- **Tables loaded from the DAT at runtime** (unit-type stats, the combat matrix, mercenary names) are uninitialised in the EXE; search the DAT by the data's known name strings instead.
- **Mid-turn saves** can legitimately contain `0xFFFF` army tombstones (combat writes them, the end-of-turn tick compacts them); `IC2.Data` skips and reports them.
- **Local corpus drift.** `IC2.Data.Tests`' corpus sweep compares the configured saves folders against a committed expected-outcome table; moving saves between `saves/` and `saves-processed/`, or adding new ones, makes it fail locally until the table is regenerated (CI skips it).

---

## 7. What's still open

Research-level items not yet established — none blocks a dispatched task.

- The un-capped melee formula's exact random-roll term; a full simulation of a recorded multi-round battle (validate as a distribution over seeds — the same save fought twice gives 63,282 and 75,536 survivors).
- The rout mechanic's cascade (`FUN_00438fb0`) has not been observed directly — reserve research only; the shipped instant resolver does not use it.
- The tactical post-battle promotion rule is a uniform 1-in-4 roll, empirical over 30 units in two battles; its implementing code has not been located.
- The rebellion check (`FUN_0044C204`) and the weather-event effect (`FUN_004511BC`) — not decompiled.
- The reparation formula has not been checked against the one observed payment's field values.
- `design-audit.md` Q9's paid case: where a supply purchase at a foreign city sends its talents.
- The DAT's static tables at `0x1F2F0` (fourteen small tables) and `0x1F8C6` (four larger ones) are mostly unidentified — only the season table and the leader-name pool are ([investigations/dat-file-layout.md](investigations/dat-file-layout.md)).
- Storm-attrition predicates: `FUN_004494E4` (the away-from-friendly-coast doubling) and `FleetRecord +24 == 1` (the tripling) are inferred from magnitudes, not decompiled.
- A ~6-byte reconciliation gap in the SAV news-log region sizing.
- `ComputerGeneral`'s actual AI decision-making (only its dispatch chain was traced).
- Whether the original draws different city icons by population (armies and fleets are confirmed three-tier; cities are not).
