# Operating guide

This is the entry point for anyone operating the Imperial Conquest 2 project: the user, and every agent session. It says where to find the build's status, where everything lives, how tasks are run, and which rules and preferences apply. The process contract is [build-process.md](build-process.md), and the tasks are in [task-catalogue.md](task-catalogue.md).

---

## 1. Where the build stands

**GitHub labels are the only status.** No document carries a snapshot ([build-process.md §5](build-process.md#5-status-lives-on-github)). To see where things stand:

```bash
gh issue list --label task --state all --json number,title,labels --jq '.[] | "\(.number)\t\(.title)\t\([.labels[].name | select(startswith("status:"))] | join(","))"'
gh issue list --label task --label status:ready          # what can run next
gh issue list --label triage:needed --state open         # untriaged bugs and follow-ups
gh issue list --label bug --state open                   # all open bugs
```

On the web, use [task issues](https://github.com/diegoami/imperial_conquest_2/issues?q=label%3Atask), [pull requests](https://github.com/diegoami/imperial_conquest_2/pulls), [milestones](https://github.com/diegoami/imperial_conquest_2/milestones) (one per phase), and [Actions](https://github.com/diegoami/imperial_conquest_2/actions), which runs build and test on every push and PR. A release gate's progress is `gh issue list --label task --label release:<version> --state all` ([release-plan.md](release-plan.md)).

### 1.1 What becomes runnable, and when

| After | What exists | Can you run it? |
| --- | --- | --- |
| Phase 0 (T01–T05, T30, T31) | Solution, CI, domain model, fixtures corpus, engine seams, hardened `IC2.Data` | `dotnet build` / `dotnet test` only |
| The rest of Phases 1–2 | The rule subsystems, then recruitment, naval, battle, diplomacy and the AI. Also the exported 334-city world, the `classical-faithful` ruleset and classical scenario (T29), and the `improved` preset (T36) | Only through their tests |
| **T41** | A thin `IC2.Cli` demo on the toy 3-city world: move, buy supply, end turns, read the news | **The first thing you can run**: a text walking skeleton of the rules built so far |
| **T23** | `IC2.Cli` as the full scriptable harness: every command type, plus the view models Godot binds to | Play any scenario from the command line |
| **T24** | Godot main screen, New Game flow, the ruleset chooser | **The first thing that looks like a game** |
| T25–T28 | The remaining screens, packaging, the nightly gate | A complete, playable build |

---

## 2. Where things live

### 2.1 The two repositories

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
| [task-catalogue.md](task-catalogue.md) | The tasks, the dependency graph, and the task index |
| [game-design.md](game-design.md) | What is being built |
| [design-audit.md](design-audit.md) | What the evidence supports, and the design questions Q1–Q10 |
| [release-plan.md](release-plan.md) | Versions, release gates, release notes, the release checklist |
| [evidence-pipeline.md](evidence-pipeline.md) | The `/process-evidence` pipeline and its skill text |
| [investigations/README.md](investigations/README.md) | Index of this repository's own evidence write-ups |

### 2.2 The original game files

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

The root holds:
- `Imperial Conquest 2.exe`, `.dat` and `.hlp`;
- `WAVS/`, converted to 16-bit/44.1 kHz PCM; the untouched originals are in `WAVS - Copy/`;
- `patch_exe.py`, which builds instant-battle and message-pumping EXE variants for recording.

The in-game battle pacing delay is a per-nation preference (the `TBattleDelays` dialog) and can be set to zero from inside the game.

### 2.3 Local toolchain (outside both repositories)

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

## 3. How to operate the project

### 3.1 Who does what

The **main session runs on Opus** and is the one the user talks to. It plans, runs tasks, triages bugs and follow-ups, runs `/process-evidence`, and brings design decisions and escalations to the user. There is no orchestrator agent.

| Role | Dispatched as | Works in | Reference |
| --- | --- | --- | --- |
| Main session | — | The main checkout. Plan and design changes go on a branch for the user's review. | [build-process.md §3.1](build-process.md#31-the-roles) |
| Implementer | Subagent, model per the catalogue | Its own worktree under `ic2-work\` | [build-process.md Appendix A](build-process.md#appendix-a-implementer-prompt-template) |
| Reviewer | Subagent, a different model per the catalogue | Its own worktree at the PR head | [build-process.md Appendix B](build-process.md#appendix-b-reviewer-prompt-template) |
| Researcher | Opus subagent: the `/process-evidence` stages and targeted research passes | The research repo's checkout; stage 2 in its own worktree here | [evidence-pipeline.md](evidence-pipeline.md) |

### 3.2 Running tasks

`/run-task [T<nn> ...]` runs tasks end to end, one at a time ([build-process.md Appendix C](build-process.md#appendix-c-the-run-task-skill)):
1. The implementer builds the task.
2. An independent reviewer checks it.
3. Any rework goes back to the implementer, at most two rounds.
4. The main session merges it.
5. The main session applies the doc claims the merge made stale, and reports to the user.

Give it task ids to run them in order, or nothing to take the next ready task. It stops at any escalation.

### 3.3 The two skills

Both are **local, git-ignored installs** under `.claude/skills/`, and the fenced text in the repository is the source of truth. If a skill is missing, reinstall it verbatim from its fenced block. Skills load when a session starts, so a newly installed skill needs a fresh session.

| Skill | Installed at | Reinstall from | What it does |
| --- | --- | --- | --- |
| `/run-task [T<nn> ...]` | `.claude/skills/run-task/SKILL.md` | [build-process.md Appendix C](build-process.md#appendix-c-the-run-task-skill) | Runs build tasks end to end |
| `/process-evidence [path]` | `.claude/skills/process-evidence/SKILL.md` | [evidence-pipeline.md](evidence-pipeline.md#the-actual-skill-file) | Turns new saves, recordings and notes into research findings, then into design implications |

### 3.4 Working rules

- **One task in flight at a time.** Agents never work in the main checkout ([build-process.md §7](build-process.md#7-concurrency-single-instance-and-local-only)).
- **Branch or `main`, case by case.** A merge's routine doc claims go straight to `main`. New or substantive content goes to a branch for review: a design correction, a new mechanism, catalogue changes. When unsure, ask.
- **Review is a label, not a GitHub review.** The reviewer applies `status:approved` or `status:rework`, and the main session reads the label.
- **Relay reviewer findings in full** on rework, never a hand-picked subset.
- **Merging** happens on GitHub's side: `gh pr merge --squash`. Afterwards, `git pull` in the main checkout is safe, because no agent uses it.
- **If a session is interrupted**, nothing is lost. The facts are in the labels and the PRs, and implementers push work in progress to their task branch. A new session picks up any task left in flight ([build-process.md §5](build-process.md#5-status-lives-on-github)).

### 3.5 Bugs and follow-ups

- **Bugs.** A defect in already-merged code is filed as a `bug` issue, and the task that found it is suspended. It is never patched from inside another task's Owns list.
- **Follow-ups.** Non-blocking review findings go into one `T<nn> follow-up` issue per merge.
- **Triage.** Both are filed with `triage:needed`, which is the main session's queue. Triage decides one of three outcomes: a correction task, folding the item into an upcoming task, or closing it with a reason. It records the outcome in a comment and removes the label. When a bug blocks a task, the catalogue records the dependency ([build-process.md §4.6](build-process.md#46-bugs-and-follow-ups)).

### 3.6 New evidence and how it reaches the build

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

## 4. Standing user preferences

These are kept in step with the auto-memory feedback notes. When a preference changes, update it here in the same session.

- **Commit and push reverse-engineering work without asking.** New reports, roadmap updates and research-repo fixes go straight to the research repo's `main`. Destructive git operations (force-push, amend, `reset --hard`) still need an explicit request.
- **A question is not a request to change files.** Answer it. If a fix turns up along the way, propose it and wait.
- **Branch or `main`, case by case.** Routine doc claims go straight to `main`; novel content goes on a branch for review; ask when unsure.
- **Upstream defects go through the bug list**: suspend, file, plan, resume. Never an ad-hoc patch across Owns lists.
- **Evidence goes through the two-stage pipeline**: `/process-evidence`, stage 1 then stage 2, never combined.
- **Relay reviewer findings in full** on rework.
- **Non-blocking review findings become one follow-up issue per merge**, and each item is folded into the next task that touches those files.
- **The main session runs the build directly** with `/run-task`. There is no orchestrator layer; it was retired on 2026-09-14 as more overhead than value for serial execution.
- **No status snapshots in documents.** Status lives in GitHub labels only.
- When correcting a claim after user feedback, fix the document or report text itself, not only the chat.
- Do not re-suggest a Windows 9x VM on this machine: WSL2's Hyper-V claims VT-x, and the user will not disable WSL2.

---

## 5. Collaboration norms

- **Keep the user informed while working.**
  - Before starting, say what you intend to do.
  - Give short progress updates: what you examined, what the evidence shows, what changed and why, and what remains uncertain.
  - End with the outcome, the verification done, the remaining limitations, and where to see the result.
  - Don't go more than about a minute of active work without an update, and don't dump raw command output.
- **In reverse-engineering work, separate observations from inferences.** Name the save, screenshot, recording or binary structure that supports each conclusion. Record the exact file, hash, address or screenshot for each new field or formula, and mark an inferred meaning as a candidate until it's checked.
- **For routine save-format checks, sample three or four representative saves, not every save.** Pick samples that cover the relevant before/after event or format variation. Widen the sample only when a discrepancy or a specific question requires it, and say why.

---

## 6. Practical caveats

- **Godot headless churn.** Running Godot headless against `godot/` (`"<Godot install>\Godot_..._console.exe" --headless --path godot --quit-after 2`) regenerates `godot/project.godot`'s header and flips `godot/MapViewer.cs`'s line endings. `scripts/check-godot-churn.ps1` reverts the two files when the diff is header/whitespace-only. Run it after any headless Godot run, before committing.
- **Build order for the inspector.** Build `IC2.Data` before `IC2.Inspect` when both have changed, because they share an `obj` directory. The `IC2.Inspect` commands are listed in the [README](../README.md#the-research-inspector-tools-ic2inspect).
- **Ghidra's reference manager misses some string references** in this Delphi build, for example the DAT filename and `"falls to"`. Grep `all_app_functions.txt` instead; the decompiler inlines string literals even when no reference was recorded. Call xrefs work, except across virtual method calls.
- **Tables loaded from the DAT at runtime** (unit-type stats, the combat matrix, mercenary names) are uninitialised in the EXE. Search the DAT by the data's known name strings instead.
- **Mid-turn saves** can legitimately contain `0xFFFF` army tombstones: combat writes them, and the end-of-turn tick compacts them. `IC2.Data` skips and reports them.
- **Local corpus drift.** `IC2.Data.Tests`' corpus sweep compares the configured saves folders against a committed table of expected outcomes. Moving saves between `saves/` and `saves-processed/`, or adding new ones, makes it fail locally until the table is regenerated (bug #57, fixed by T34). CI skips it.

---

## 7. What's still open

These are research-level items not yet established. None of them blocks a dispatched task.

- **Melee:** the un-capped melee formula's exact random-roll term. A full simulation of a recorded multi-round battle is also still to do; validate it as a distribution over seeds, since the same save fought twice gives 63,282 and 75,536 survivors.
- **Rout:** the cascade (`FUN_00438fb0`) hasn't been observed directly. This is reserve research only; the shipped instant resolver doesn't use it.
- **Promotion:** the tactical post-battle promotion rule is a uniform 1-in-4 roll, measured over 30 units in two battles. Its implementing code hasn't been located.
- **Rebellion:** the new owner's choice (`FUN_0044c204`) is only partly traced, because it depends on an unidentified bitmask at nation `+0x46`.
- **Weather:** the event effects (`FUN_004511BC`) haven't been decompiled.
- **Controlled-save checks the user has offered**, none of them urgent. The recipes are in the reports:
  - a second supply-dialog fill;
  - a foreign purchase;
  - the tax and mobilization divisors of population growth;
  - mercenary desertion.
- **DAT static tables:** the ones at `0x1F2F0` (fourteen small tables) and `0x1F8C6` (four larger ones) are mostly unidentified. Only the season table and the leader-name pool are known ([investigations/dat-file-layout.md](investigations/dat-file-layout.md)).
- **Storm attrition:** two predicates are inferred from magnitudes, not decompiled: `FUN_004494E4` (the away-from-friendly-coast doubling) and `FleetRecord +24 == 1` (the tripling).
- **News log:** a ~6-byte reconciliation gap in the SAV news-log region sizing.
- **AI:** `ComputerGeneral`'s actual decision-making; only its dispatch chain has been traced.
- **City icons:** whether the original draws different city icons by population. Armies and fleets are confirmed three-tier; cities are not.
