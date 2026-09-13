# The evidence-processing pipeline: `/process-evidence`

A second pipeline, separate from [`build-orchestration-plan.md`](build-orchestration-plan.md)'s build pipeline. That one turns a settled design into code; this one turns new play evidence (saves, recordings, session notes) into settled design — the step that has to happen *before* the build pipeline's task catalogue can be trusted. Installed as a Claude Code skill (`.claude/skills/process-evidence/SKILL.md`), invoked as `/process-evidence [path or description]`. This document is the durable source of truth for the skill's content — same relationship `build-orchestration-plan.md` §Appendix C has to `/build-tick`; if the local install is ever lost, reinstall it from the fenced block below.

## What triggers it

New files appearing in the **unprocessed** side of the original game directory's evidence folders — the ones with a `-processed/` sibling (`saves/`, `recordings/`, `screenshots/`) — plus new or updated files under `notes/`, which is where the user writes free-text session notes correlating a save pair with what happened between them (see `notes/2_rome_s.txt` for the shape: a save-pair, an optional recording filename, a list of observed events). A note file is the usual trigger, since it's what tells a save-diff *what to look for*; raw saves/recordings with no note are lower-priority and can sit until one is written.

## Prerequisites, and where to get each one

| Prerequisite | What it's for | Where it comes from |
| --- | --- | --- |
| **The research repo**, `diegoami/imperial-conquest-2-research` | Stage 1 writes new/updated reports here. | A public GitHub repo. Read via `gh api repos/diegoami/imperial-conquest-2-research/contents/<path>` (no local clone needed for reading). **Writing needs a real clone** — `gh api` can't commit — so stage 1 clones it fresh into a scratch directory (`git clone https://github.com/diegoami/imperial-conquest-2-research`), commits, and pushes directly to `main` there, matching this project's standing rule for RE work (commit and push without asking, established across many prior sessions). Do not reuse a stale local clone without verifying it has no uncommitted, unrelated state — see the `RE-imperial-conquest-2-work` incident in this repo's history for exactly what goes wrong when that's skipped. |
| **`assets.local.ini`** (this repo's root, git-ignored) | Points at the user's own copy of the original game, where all raw evidence lives. | Supplied entirely by the user — copy `assets.example.ini`, set `directory` to the original installation path. Claude cannot obtain the original game files itself; if this file is missing, stop and ask the user to configure it rather than guessing a path. |
| **The evidence folders themselves**, under that `directory` | The actual saves/recordings/screenshots/notes to process. | `saves/` + `saves-processed/`, `recordings/` + `recordings-processed/`, `screenshots/` + `screenshots-processed/`, `notes/` (no `-processed` sibling as of this writing — check current convention before assuming one exists). Unprocessed evidence sits in the non-`-processed` folder; move a file to its `-processed` sibling once a report cites it, matching the existing convention — don't invent a new one. |
| **The Ghidra/JDK decompilation toolchain** (only if stage 1's evidence needs new code-level confirmation, not just a save-diff) | `%LOCALAPPDATA%\ReTools\` — JDK 21, Ghidra 12.1.3, the already-analyzed `ghidra_projects\IC2\` project, `delphi_symbols.tsv`/`.json` (282 known Delphi method names — look up an address here before hand-deriving one), and prior decompiled-function dumps. Headless analyzer: `support\analyzeHeadless.bat`. | Already set up on this machine. On a machine without it, this is a significant one-time setup (Ghidra + JDK install, import and auto-analyze the EXE) that the skill cannot bootstrap on its own — most evidence (a save-pair + a note) only needs save-diffing via `IC2.Inspect`, not decompilation, so don't reach for Ghidra unless the note's claim genuinely can't be settled by comparing saves. |
| **`IC2.Inspect`**, this repo's own read-only CLI | The actual tool for comparing saves, listing armies/fleets/nations, rendering the map. Already built. | `dotnet build src/IC2.Inspect/IC2.Inspect.csproj`, then `--compare-saves`, `--inspect-army`, `--list-armies`, `--inspect-turn`, etc. — see this repo's `README.md` for the full command list. |

## The two stages

**Stage 1 — evidence → RE findings.** One Opus agent, dispatched fresh (no shared context assumed). Give it: the specific note file (or a description of what's new, if invoked without one), the research-repo clone/write instructions above, the local evidence-folder paths, and the Ghidra toolchain paths in case it needs them. Its job: read the note, correlate the named saves/recording, run controlled comparisons, decompile further only if the note's claim needs code-level confirmation and isn't already covered by an existing report, write a new report or update an existing one in the research repo following its established `[confirmed]`/`[derived]`/`[designed]` discipline (see any existing report for the house style), commit and push to the research repo's `main` directly (standing rule — no branch, no asking), move the now-cited save/recording files to their `-processed/` siblings. Report back: which report(s) changed, the commit hash, a plain summary of what was newly confirmed or corrected, and anything it could not settle.

**Stage 2 — RE findings → game design**, dispatched only after stage 1 reports back (never both at once — this is a real sequential dependency, not two parallel jobs). A second, fresh Opus agent. Give it: exactly what stage 1 changed (report names + commit hash — don't make it re-discover this), and this repo's `game-design.md`, `design-audit.md`, and `build-orchestration-plan.md`. Its job: read the new/changed report(s), decide whether they affect any existing `[confirmed]`/`[derived]`/`[designed]` claim, open question, or task DoD line in this repo. If they do and it's a plain factual correction (a wrong offset, a superseded guess), draft the fix. If it's a genuine judgment call (a design decision, not a fact), **do not decide it** — pose the question plainly, the same way `design-audit.md` §3's open questions were raised, and stop. Either way: push the work to a new branch (never `main` directly — this repo's convention throughout is draft-then-review for anything beyond a routine plan-catalogue correction), not merged. Report back: the branch, what changed and why, and any open question for the human.

**Whoever invokes `/process-evidence`** (the interactive session, not a subagent) is the one coordinating both dispatches — dispatch stage 1, wait for its async completion notification (don't poll), then dispatch stage 2 with stage 1's actual output as input, then relay the final branch/summary to the user. If stage 1 finds nothing worth writing up (the note doesn't add anything beyond what's already confirmed), stop there and say so — don't dispatch stage 2 over nothing.

## The actual skill file

```markdown
---
name: process-evidence
description: Process new save/recording/note evidence into research-repo findings, then evaluate game-design implications. Two sequential Opus dispatches.
---

# /process-evidence [path or description]

Full context and prerequisites: `docs/evidence-pipeline.md` in `imperial_conquest_2` — read it in full before dispatching anything, it has the exact repo/toolchain paths and the standing conventions (commit-and-push to the research repo without asking; draft-then-review branch for anything in this repo).

## Input

If given a path (e.g. a `notes/*.txt` file) or a description, that's the evidence to process. If given nothing, scan the original game directory's `notes/` folder (path from this repo's `assets.local.ini`) for a note not yet cited by any research-repo report — check the research repo's existing reports for the note's referenced save names before assuming it's new.

## Steps

1. Verify prerequisites from `docs/evidence-pipeline.md`'s table: `assets.local.ini` configured, the named evidence files actually exist, `gh` can reach `diegoami/imperial-conquest-2-research`. Stop and ask the user if any are missing rather than guessing.
2. Check `ListAgents`. If anything is running in `imperial_conquest_2`'s shared checkout, note it — stage 1/2 mostly work in the *research* repo (a separate clone, not this shared checkout) so this is lower-risk than a build-pipeline dispatch, but stage 2's this-repo changes still need the isolated-worktree treatment if the build orchestrator is active.
3. Dispatch **stage 1** (Opus, fresh agent, full self-contained brief per `docs/evidence-pipeline.md`'s "Stage 1" section). Do not dispatch stage 2 yet.
4. Wait for stage 1's async completion notification. Do not poll.
5. If stage 1 found nothing worth writing up, stop and report that to the user — no stage 2.
6. Otherwise dispatch **stage 2** (Opus, fresh agent, given exactly what stage 1 changed) per `docs/evidence-pipeline.md`'s "Stage 2" section.
7. Wait for stage 2's async completion notification.
8. Relay to the user: what stage 1 found and where (research-repo commit), what stage 2 proposes and where (this repo's branch, if any), and any open question stage 2 raised for a human decision.
```

Install this at `.claude/skills/process-evidence/SKILL.md` (local, git-ignored, machine-specific — reinstall from the block above if it's ever missing).
