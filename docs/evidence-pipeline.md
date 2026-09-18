# The evidence-processing pipeline: `/process-evidence`

A second pipeline, separate from the build pipeline in [build-process.md](build-process.md). That one turns a settled design into code; this one turns new play evidence (saves, recordings, session notes) into settled design — the step that has to happen *before* the task catalogue can be trusted. It runs as a Claude Code skill (`.claude/skills/process-evidence/SKILL.md`), invoked as `/process-evidence [path or description]`. This document is the source of truth for the skill's content, the same relationship [build-process.md Appendix C](build-process.md#appendix-c-the-run-task-skill) has to `/run-task`: the skill is a local, git-ignored install, reinstalled verbatim from the fenced block below if it is ever missing.

## What triggers it

New files in the **unprocessed** side of the original game directory's evidence folders — the ones with a `-processed/` sibling (`saves/`, `recordings/`, `screenshots/`) — plus new or updated files under `notes/`, where the user writes free-text session notes correlating a save pair with what happened between them (see `notes/2_rome_s.txt` for the shape: a save pair, an optional recording filename, a list of observed events). A note file is the usual trigger, since it tells a save-diff *what to look for*; raw saves or recordings with no note are lower priority and can wait until one is written.

## Prerequisites, and where to get each one

| Prerequisite | What it's for | Where it comes from |
| --- | --- | --- |
| **The research repo**, `diegoami/imperial-conquest-2-research` | Stage 1 writes new or updated reports here. | Read via `gh api repos/diegoami/imperial-conquest-2-research/contents/<path>` (no clone needed). **Writing uses the local checkout** at `C:\Users\diego\projects\RE-imperial-conquest-2`: run `git pull --ff-only` first, then commit and push to its `main` (the standing rule is to do this without asking). |
| **`assets.local.ini`** (this repo's root, git-ignored) | Points at the user's own copy of the original game, where all raw evidence lives. | Supplied by the user — copy `assets.example.ini`, set `directory` to the installation path. Claude cannot obtain the original files itself; if this file is missing, stop and ask the user rather than guessing a path. |
| **The evidence folders**, under that `directory` | The saves, recordings, screenshots and notes to process. | `saves/` + `saves-processed/`, `recordings/` + `recordings-processed/`, `screenshots/` + `screenshots-processed/`, and `notes/` (no `-processed` sibling). Move a file to its `-processed` sibling once a report cites it — don't invent a new convention. |
| **The Ghidra/JDK decompilation toolchain** (only when the evidence needs code-level confirmation, not just a save-diff) | `%LOCALAPPDATA%\ReTools\` — the full inventory and the headless command are in [operating-guide.md §2.3](operating-guide.md#23-local-toolchain-outside-both-repositories). Look an address up in `delphi_symbols.tsv` and grep `all_app_functions.txt` before running Ghidra at all. | Already set up on this machine. Elsewhere it is a significant one-time setup the skill cannot bootstrap. Most evidence (a save pair plus a note) only needs save-diffing with `IC2.Inspect`. |
| **`IC2.Inspect`**, this repo's read-only CLI | Comparing saves, listing armies/fleets/nations, rendering the map. | `dotnet build src/IC2.Inspect/IC2.Inspect.csproj`, then `--compare-saves`, `--inspect-army`, `--list-armies`, `--inspect-turn`, etc. — the full list is in the [README](../README.md#the-research-inspector-tools-ic2inspect). |

## The two stages

**Stage 1 — evidence → RE findings.** One Opus agent, dispatched fresh (no shared context assumed). Give it: the specific note file (or a description of what's new), the research-repo write instructions above, the local evidence-folder paths, and the toolchain paths in case it needs them. Its job: read the note, correlate the named saves/recording, run controlled comparisons, decompile further only if the note's claim needs code-level confirmation and isn't already covered by an existing report, write a new report or update an existing one in the research repo following its `[confirmed]`/`[derived]`/`[designed]` discipline (any existing report shows the house style), commit and push to the research repo's `main` directly, and move the now-cited save/recording files to their `-processed/` siblings. Report back: which report(s) changed, the commit hash, a plain summary of what was newly confirmed or corrected, and anything it could not settle.

**Stage 2 — RE findings → this repository**, dispatched only after stage 1 reports back (never both at once — a real sequential dependency). A second, fresh Opus agent. Give it exactly what stage 1 changed (report names and commit hash — don't make it re-discover this). Its job is to check **every document claim** the new evidence touches, applied to the new evidence instead of a merged task. Each finding takes exactly one of four routes:

1. **Claims.** For each document — `design-audit.md` and `game-design.md` claims, the investigations index, release-plan gates, the operating guide's §7 open items, the README — decide whether the new evidence confirms, corrects or closes something, and draft the fix in place (current fact only, cited).
2. **Defects in merged code.** A merged constant, field or rule the evidence shows to be wrong is **filed as a `bug` issue, labelled `triage:needed`**, with the evidence ([build-process.md §4.6](build-process.md#46-bugs-and-follow-ups)), never patched. The main session's triage decides what happens to it.
3. **Tasks not yet dispatched.** A scope or DoD change to a task that has not started is drafted as a [task-catalogue.md](task-catalogue.md) edit on the same review branch; a DoD only changes by a reviewed commit ([build-process.md §4.3](build-process.md#43-the-dod-is-not-negotiable-by-an-agent)).
4. **Design decisions.** A genuine judgment call is **not decided**: pose it plainly, the way `design-audit.md` §3's questions were raised, and stop.

Stage 2 writes no status, because status lives only in GitHub labels. It works in **its own worktree on a new branch** off `origin/main`, never in the main checkout, pushes that branch, and does not merge: evidence-driven changes are new content, so they go through review rather than straight to `main`. **Report back to the main session**: the branch, what changed and why, any bug issues filed, and any open question for the human. The planner triages the bugs ([build-process.md §4.6](build-process.md#46-bugs-and-follow-ups)), takes the branch and the open questions to the user, and decides what enters the build.

**The main session invokes `/process-evidence`** and coordinates both dispatches: dispatch stage 1, wait for its completion notification (don't poll), dispatch stage 2 with stage 1's actual output as input, then relay the result to the user. Neither stage dispatches build tasks. If stage 1 finds nothing worth writing up, stop there and say so — no stage 2 over nothing.

## The actual skill file

```markdown
---
name: process-evidence
description: Process new save/recording/note evidence into research-repo findings, then evaluate what they change in this repository. Two sequential Opus dispatches.
---

# /process-evidence [path or description]

Full context and prerequisites: `docs/evidence-pipeline.md` in `imperial_conquest_2` — read it in
full before dispatching anything. It has the repo and toolchain paths and the standing conventions
(commit and push to the research repo without asking; a review branch for anything in this repo).

## Input

If given a path (e.g. a `notes/*.txt` file) or a description, that's the evidence to process. If
given nothing, scan the original game directory's `notes/` folder (path from this repo's
`assets.local.ini`) for a note not yet cited by any research-repo report — check the research repo's
existing reports for the note's referenced save names before assuming it's new.

## Steps

1. Verify the prerequisites in `docs/evidence-pipeline.md`'s table: `assets.local.ini` configured,
   the named evidence files exist, `gh` can reach `diegoami/imperial-conquest-2-research`. Stop and
   ask the user if any are missing rather than guessing.
2. Dispatch **stage 1** (Opus, fresh agent, a full self-contained brief per the "Stage 1" section).
   Do not dispatch stage 2 yet.
3. Wait for stage 1's completion notification. Do not poll.
4. If stage 1 found nothing worth writing up, stop and report that to the user — no stage 2.
5. Otherwise dispatch **stage 2** (Opus, fresh agent, given exactly what stage 1 changed) per the
   "Stage 2" section: every document claim the new evidence touches, defects in merged code
   filed as `bug` issues labelled `triage:needed` (never patched), catalogue edits only for tasks
   not yet dispatched, design decisions posed and not made, no status edits. It works in its own
   worktree (`git worktree add <sibling-path> -b evidence/<slug> origin/main`), never in the main
   checkout, and pushes that branch without merging.
6. Wait for stage 2's completion notification; stage 2 reports back to you, the main session.
7. Relay to the user: what stage 1 found and where (research-repo commit), what stage 2 proposes and
   where (this repo's branch), the bug issues it filed (in your `triage:needed` queue,
   build-process.md §4.6), and any open question for a human decision.

This skill is run by the main session. It never dispatches build tasks; `/run-task` does that.
```

Install this at `.claude/skills/process-evidence/SKILL.md` (local, git-ignored — reinstall from the block above if it is ever missing).
