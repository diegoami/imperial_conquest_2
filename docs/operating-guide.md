# Operating guide

The agent-facing half of operating this project: where everything lives, how tasks are run, the
working rules, and the standing preferences. The process contract is [build-process.md](build-process.md)
and the tasks are in [task-catalogue.md](task-catalogue.md).

**Living reference is in the [wiki](https://github.com/diegoami/imperial_conquest_2/wiki)**, not here, because it goes stale faster than the code:
- [Where the build stands](https://github.com/diegoami/imperial_conquest_2/wiki/Where-the-build-stands) — how to read the board, and what becomes runnable when
- [Open questions](https://github.com/diegoami/imperial_conquest_2/wiki/Open-questions) — research-level items not yet established
- [Practical caveats](https://github.com/diegoami/imperial_conquest_2/wiki/Practical-caveats) — Godot headless churn, build order, Ghidra quirks, local corpus drift
- [Dated reviews](https://github.com/diegoami/imperial_conquest_2/wiki/Review-2026-09-18-repository-and-direction)

Status itself lives only in GitHub labels ([build-process.md §5](build-process.md#5-status-lives-on-github)):

```bash
gh issue list --label task --label status:ready          # what can run next
gh issue list --label triage:needed --state open         # untriaged bugs and follow-ups
```

---

## 1. Where things live

### 1.1 The two repositories

- **This repository**, [`diegoami/imperial_conquest_2`](https://github.com/diegoami/imperial_conquest_2): design, plans, and all code (`IC2.Data`, `IC2.Inspect`, `IC2.Engine`, `IC2.Cli`, `godot/`).
  - The main checkout is `C:\Users\diego\projects\imperial_conquest_2` and belongs to the main session.
  - Agents' worktrees live under `C:\Users\diego\projects\ic2-work\`.
- **The research repository**, [`diegoami/imperial-conquest-2-research`](https://github.com/diegoami/imperial-conquest-2-research): every reverse-engineering report (`docs/reports/`), the roadmap, the decompilation plan and the research notes.
  - Its local checkout is `C:\Users\diego\projects\RE-imperial-conquest-2`. Run `git pull --ff-only` before writing to it.
  - For static-analysis work, start from its `docs/decompilation-plan.md`, the live record of what has been decompiled.

| Document | What it is |
| --- | --- |
| [README.md](../README.md) | The front door: what the project is, how to build it, the inspector tools |
| [operating-guide.md](operating-guide.md) | This document |
| [CLAUDE.md](../CLAUDE.md) | Auto-loaded into every Claude Code session: a pointer to this guide, and the rules that must never be forgotten |
| [build-process.md](build-process.md) | The process contract: roles, the task loop, review gates, bugs and follow-ups, prompt templates, `/run-task` |
| [task-catalogue.md](task-catalogue.md) | The index: the dependency graph, the waves, and a stub per task linking to its entry |
| [tasks/](tasks/) | One file per task, `T<nn>.md`: the task's contract (Owns, Scope, Done when) |
| [game-design.md](game-design.md) | What is being built |
| [design-audit.md](design-audit.md) | What the evidence supports, and the design questions Q1–Q10 |
| [release-plan.md](release-plan.md) | Versions, release gates, release notes, the release checklist |
| [milestone-review.md](milestone-review.md) | A portable process: how a milestone is defined, frozen on a review branch, reviewed by an independent reviewer through a never-merged PR, and tagged |
| [evidence-pipeline.md](evidence-pipeline.md) | The `/process-evidence` pipeline and its skill text |
| [recording-analysis.md](recording-analysis.md) | Reading a screen recording: the `/parse-recording` pipeline, the ffmpeg recipe, and what each in-game panel is worth |
| [investigations/README.md](investigations/README.md) | Index of this repository's own evidence write-ups |

### 1.2 The original game files

The original game files are never in either repository, and neither is anything derived from them. The Ghidra project and the decompiled-text dumps under `%LOCALAPPDATA%\ReTools` stay local too.

`assets.local.ini` at this repository's root points at the user's own installation. It is git-ignored and copied from `assets.example.ini`. The installation is currently `C:\Users\diego\Documents\imp_conq_original`, which is a local git repository of its own. If the file is missing, ask the user to configure it; never guess a path.

Inside that directory:

| Folder | Holds |
| --- | --- |
| `saves/` + `saves-processed/` | Save files. Move a save to `saves-processed/` once a report cites it. |
| `recordings/` + `recordings-processed/` | Screen recordings (`.mp4`), same convention |
| `screenshots/` + `screenshots-processed/` | Screenshots (`<save-number>.<image-number>.png`), same convention |
| `notes/` | The user's session notes: a save pair, an optional recording, and the events observed between them |
| (root) | See below |

**Every `IC2.Data.Tests` test that names a fixture resolves it by name, never by folder** (T53, issue
#204): a save cited as `1_rome_270_winter_7.sav` is found by searching `saves-processed/`, then
`saves/`, then `releases/<tag>/` under the configured directory, first hit wins (one search order shared by `FixtureResolver` and `IC2.Inspect`'s `CorpusFileLocator` since T64; byte-identical copies of the same name count as one file, and differing copies are an error) — so moving a save into
`saves-processed/` once a report cites it, exactly the convention above, changes no test outcome. CI
never touches this directory; it sets `IC2_FIXTURES_DIR` to a fetched clone of the private
[`diegoami/ic2-test-fixtures`](https://github.com/diegoami/ic2-test-fixtures) repository, which the same
by-name resolver checks first when it is set.

**That repository holds the whole corpus, not a named subset** (issue #207, correcting T53's own first
attempt at this paragraph): it was originally scoped to just the saves referenced by name as string
literals, which missed that `CorpusSweepTests` and its siblings sweep **whatever corpus is configured**
against the full `CorpusFixtures/expected-corpus-outcomes.json` table — reading by directory, not by
name. A named-subset fixtures repository silently loses that coverage in CI the moment a test reads by
directory instead of by name, the same shape as the defect T53 exists to fix. The repository is now the
DAT plus all 54 saves the committed corpus table enumerates (7 MB) — see its own README for the same
reasoning. Nothing downstream — a test, a review, or CI — cares which folder a fixture currently sits
in, and nothing in CI has to guess which subset of the corpus a test will read by directory next.

**The evidence is published as GitHub releases, one per play-through**, in [`diegoami/imp_conquest_fixtures`](https://github.com/diegoami/imp_conquest_fixtures/releases) — `run-1-ptolemy`, `run-1-rome`, `run-1-cartago`, `run-1-thracia` and `legacy-probes`. Each release holds that run's saves, screenshots and any recording. **A recording no longer needs a note to be usable**: [recording-analysis.md §1](recording-analysis.md#1-align-the-saves-to-the-recordings-before-extracting-anything) aligns saves to recordings from their file timestamps alone.

This matters because **reports cite bare filenames** (`11_supply.sav`, `1_rome_270_winter_7.sav`), never paths. [`docs/evidence-index.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/evidence-index.md) in the research repository maps every cited filename to the release holding it, with a `gh release download` recipe — so a citation can be resolved **without this machine's local copy**. Read it before hunting for a file on disk.

**Evidence made on another machine lives only in a release.** The originals repo has a git-ignored `releases/<tag>/` cache and `scripts/fetch-release.sh` to fill it: saves, screenshots and notes by default (**under 10 MB for a whole run**), `--video` to add the recordings. The cache is disposable — GitHub is the source of truth.

The releases change nothing about the standing rule: **no save, screenshot, recording or game file enters either repository.** A release in a private repository is not this repository, and the index is a pointer, never a copy.

The root holds:
- `Imperial Conquest 2.exe`, `.dat` and `.hlp`;
- `WAVS/`, converted to 16-bit/44.1 kHz PCM; the untouched originals are in `WAVS - Copy/`;
- `patch_exe.py`, which builds instant-battle and message-pumping EXE variants for recording.

The in-game battle pacing delay is a per-nation preference (the `TBattleDelays` dialog) and can be set to zero from inside the game.

### 1.3 Local toolchain (outside both repositories)

- **General tools:** `gh` (authenticated as `diegoami`), `jq`, Python 3.14, Node, the .NET 10 SDK, Godot 4.7.2 (.NET).
- **ffmpeg** is at `%LOCALAPPDATA%\ReTools\ffmpeg-master-latest-win64-gpl\bin\ffmpeg.exe`. To extract frames: `ffmpeg -ss <startSeconds> -i "<recording.mp4>" -vf fps=1 -frames:v <N> <outdir>/f_%03d.png`, then read the frames. The tactical battle's combat-resolution panel gives exact per-exchange troop counts, and the user will record more battles on request.
- **`%LOCALAPPDATA%\ReTools\`** holds:
  - Temurin JDK 21 (`jdk-21.0.12.1+1\`) and Ghidra 12.1.3 (`ghidra_12.1.3_PUBLIC\`);
  - the imported and analysed project (`ghidra_projects\IC2\`);
  - `scripts\`: `ExportFunctions.java`, `ExportAddresses.java` (works on mid-function addresses), `ExportAllInRange.java`, `FindXrefs.java`, `FindCallers.java`, `FindString.java`, `DumpMemory.java`, `DumpListing.java` (machine-code listings), `ImportDelphiSymbols.java`;
  - `delphi_symbols.tsv`/`.json`: 282 method names across 31 classes. Look an address up here before deriving one.
  - `all_app_functions.txt`: every function in 0x401000–0x460000, decompiled. Grep this before running Ghidra.
- **To run Ghidra headless:** set `$env:JAVA_HOME` to the JDK, then run `ghidra_12.1.3_PUBLIC\support\analyzeHeadless.bat %LOCALAPPDATA%\ReTools\ghidra_projects IC2 -process "Imperial Conquest 2.exe" -noanalysis -scriptPath <scriptsDir> -postScript <Script>.java <args...>`.

---

## 2. How to operate the project

### 2.1 Who does what

The **main session runs on Opus** and is the one the user talks to. It plans, runs tasks, triages bugs and follow-ups, runs `/process-evidence`, and brings design decisions and escalations to the user. There is no orchestrator agent.

| Role | Dispatched as | Works in | Reference |
| --- | --- | --- | --- |
| Main session | — | The main checkout. Plan and design changes go on a branch for the user's review. | [build-process.md §3.1](build-process.md#31-the-roles) |
| Implementer | Subagent, model per the catalogue | Its own worktree under `ic2-work\` | [build-process.md Appendix A](build-process.md#appendix-a-implementer-prompt-template) |
| Reviewer | Subagent, a different model per the catalogue | Its own worktree at the PR head | [build-process.md Appendix B](build-process.md#appendix-b-reviewer-prompt-template) |
| Researcher | Opus subagent: the `/process-evidence` stages and targeted research passes | The research repo's checkout; stage 2 in its own worktree here | [evidence-pipeline.md](evidence-pipeline.md) |

### 2.2 Running tasks

`/run-task [T<nn> ...]` runs tasks end to end, one at a time ([build-process.md Appendix C](build-process.md#appendix-c-the-run-task-skill)):
1. The implementer builds the task.
2. An independent reviewer checks it.
3. Any rework goes back to the implementer, at most two rounds.
4. The main session merges it.
5. The main session applies the doc claims the merge made stale, and reports to the user.

Give it task ids to run them in order, or nothing to take the next ready task. It stops at any escalation.

### 2.3 The two skills

Both are **local, git-ignored installs** under `.claude/skills/`, and the fenced text in the repository is the source of truth. If a skill is missing, reinstall it verbatim from its fenced block. Skills load when a session starts, so a newly installed skill needs a fresh session.

| Skill | Installed at | Reinstall from | What it does |
| --- | --- | --- | --- |
| `/run-task [T<nn> ...]` | `.claude/skills/run-task/SKILL.md` | [build-process.md Appendix C](build-process.md#appendix-c-the-run-task-skill) | Runs build tasks end to end |
| `/process-evidence [path]` | `.claude/skills/process-evidence/SKILL.md` | [evidence-pipeline.md](evidence-pipeline.md#the-actual-skill-file) | Turns new saves, recordings and notes into research findings, then into design implications |
| `/parse-recording [recording] [saves] [timestamps]` | `.claude/skills/parse-recording/SKILL.md` | [recording-analysis.md](recording-analysis.md#the-actual-skill-file) | Reads a screen recording into findings — frame extraction, panel reading, correlation against the saves either side. **Needs no written notes**, only rough timestamps |

### 2.4 Working rules

- **One task in flight at a time.** Agents never work in the main checkout ([build-process.md §7](build-process.md#7-concurrency-single-instance-and-local-only)).
- **Branch or `main`, case by case.** A merge's routine doc claims go straight to `main`. New or substantive content goes to a branch for review: a design correction, a new mechanism, catalogue changes. When unsure, ask.
- **Review is a label, not a GitHub review.** The reviewer applies `status:approved` or `status:rework`, and the main session reads the label.
- **Relay reviewer findings in full** on rework, never a hand-picked subset.
- **Merging** happens on GitHub's side: `gh pr merge --squash`. Afterwards, `git pull` in the main checkout is safe, because no agent uses it.
- **If a session is interrupted**, nothing is lost. The facts are in the labels and the PRs, and implementers push work in progress to their task branch. A new session picks up any task left in flight ([build-process.md §5](build-process.md#5-status-lives-on-github)).

### 2.5 Bugs and follow-ups

- **Bugs.** A defect in already-merged code is filed as a `bug` issue, and the task that found it is suspended. It is never patched from inside another task's Owns list.
- **Follow-ups.** Non-blocking review findings go into one `T<nn> follow-up` issue per merge.
- **Triage.** Both are filed with `triage:needed`, which is the main session's queue. Triage decides one of three outcomes: a correction task, folding the item into an upcoming task, or closing it with a reason. It records the outcome in a comment and removes the label. When a bug blocks a task, the catalogue records the dependency ([build-process.md §4.6](build-process.md#46-bugs-and-follow-ups)).

### 2.6 New evidence and how it reaches the build

`/process-evidence` runs two sequential Opus stages ([evidence-pipeline.md](evidence-pipeline.md)):
1. Stage 1 writes the research-repo report, straight to that repo's `main`.
2. Stage 2 checks every document claim the new evidence touches, on a review branch.

Each finding takes one of four routes:

- a corrected fact, or a closed `[open]` item, in `design-audit.md`, `game-design.md` or another document;
- a defect in merged code → a `bug` issue labelled `triage:needed`;
- a change to a not-yet-dispatched task's scope or DoD → a catalogue edit on the review branch;
- a design decision → posed to the user, never decided.

Targeted research passes (for example "what does the original do when upkeep can't be paid?") are dispatched the same way, straight to a researcher, when a task or a review needs an answer.

---

## 3. Standing user preferences

These are kept in step with the auto-memory feedback notes. When a preference changes, update it here in the same session.

- **Commit and push reverse-engineering work without asking.** New reports, roadmap updates and research-repo fixes go straight to the research repo's `main`. Destructive git operations (force-push, amend, `reset --hard`) still need an explicit request.
- **A question is not a request to change files.** Answer it. If a fix turns up along the way, propose it and wait.
- **Branch or `main`, case by case.** Routine doc claims go straight to `main`; novel content goes on a branch for review; ask when unsure.
- **Upstream defects go through the bug list**: suspend, file, plan, resume. Never an ad-hoc patch across Owns lists.
- **Evidence goes through the two-stage pipeline**: `/process-evidence`, stage 1 then stage 2, never combined.
- **Relay reviewer findings in full** on rework.
- **An external reviewer posts to the PR.** Any review prompt handed to another model (a plan PR, a code PR, a milestone) tells it to post its result as one PR comment. The main session reads it from GitHub, so the user never relays a review by hand.
- **Non-blocking review findings become one follow-up issue per merge**, and each item is folded into the next task that touches those files.
- **The main session runs the build directly** with `/run-task`. There is no orchestrator layer; it was retired on 2026-09-14 as more overhead than value for serial execution.
- **No status snapshots in documents.** Status lives in GitHub labels only.
- When correcting a claim after user feedback, fix the document or report text itself, not only the chat.
- Do not re-suggest a Windows 9x VM on this machine: WSL2's Hyper-V claims VT-x, and the user will not disable WSL2.

---

## 4. Collaboration norms

- **Keep the user informed while working.**
  - Before starting, say what you intend to do.
  - Give short progress updates: what you examined, what the evidence shows, what changed and why, and what remains uncertain.
  - End with the outcome, the verification done, the remaining limitations, and where to see the result.
  - Don't go more than about a minute of active work without an update, and don't dump raw command output.
- **In reverse-engineering work, separate observations from inferences.** Name the save, screenshot, recording or binary structure that supports each conclusion. Record the exact file, hash, address or screenshot for each new field or formula, and mark an inferred meaning as a candidate until it's checked.
- **For routine save-format checks, sample three or four representative saves, not every save.** Pick samples that cover the relevant before/after event or format variation. Widen the sample only when a discrepancy or a specific question requires it, and say why.

---
