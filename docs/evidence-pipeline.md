# The evidence-processing pipeline: `/process-evidence`

A second pipeline, separate from the build pipeline in [build-process.md](build-process.md). That one turns a settled design into code; this one turns new play evidence (saves, recordings, session notes) and the results of experiments on the original game into settled design — the step that has to happen *before* the task catalogue can be trusted. It runs as a Claude Code skill (`.claude/skills/process-evidence/SKILL.md`), invoked as `/process-evidence [path or description]`. This document is the source of truth for the skill's content, the same relationship [build-process.md Appendix C](build-process.md#appendix-c-the-run-task-skill) has to `/run-task`: the skill is a local, git-ignored install, reinstalled verbatim from the fenced block below if it is ever missing.

## What triggers it

New files in the **unprocessed** side of the original game directory's evidence folders — the ones with a `-processed/` sibling (`saves/`, `recordings/`, `screenshots/`) — plus new or updated files under `notes/`, where the user writes free-text session notes correlating a save pair with what happened between them (see `notes/2_rome_s.txt` for the shape: a save pair, an optional recording filename, a list of observed events). A note file is the usual trigger, since it tells a save-diff *what to look for*.

**A recording needs no note.** This paragraph used to end *"raw saves or recordings with no note are lower priority and can wait until one is written"* — which was wrong, and expensively so: three recordings sat unannotated for a week and were nearly left out of the evidence releases on the grounds that nothing mapped them, when in fact four reports cite them. A recording plus the saves either side plus **rough timestamps** is a complete input, handled by [`/parse-recording`](recording-analysis.md). Writing notes by hand is the most expensive part of producing evidence and the first thing skipped, so the pipeline no longer depends on it.

## Prerequisites, and where to get each one

| Prerequisite | What it's for | Where it comes from |
| --- | --- | --- |
| **The research repo**, `diegoami/imperial-conquest-2-research` | Stage 1 writes new or updated reports here. | Read via `gh api repos/diegoami/imperial-conquest-2-research/contents/<path>` (no clone needed). **Writing uses the local checkout** at `C:\Users\diego\projects\RE-imperial-conquest-2`: run `git pull --ff-only` first, then commit and push to its `main` (the standing rule is to do this without asking). |
| **`assets.local.ini`** (this repo's root, git-ignored) | Points at the user's own copy of the original game, where all raw evidence lives. | Supplied by the user — copy `assets.example.ini`, set `directory` to the installation path. Claude cannot obtain the original files itself; if this file is missing, stop and ask the user rather than guessing a path. |
| **The evidence folders**, under that `directory` | The saves, recordings, screenshots and notes to process. | `saves/` + `saves-processed/`, `recordings/` + `recordings-processed/`, `screenshots/` + `screenshots-processed/`, and `notes/` (no `-processed` sibling). Move a file to its `-processed` sibling once a report cites it — don't invent a new convention. |
| **The Ghidra/JDK decompilation toolchain** (only when the evidence needs code-level confirmation, not just a save-diff) | `%LOCALAPPDATA%\ReTools\` — the full inventory and the headless command are in [operating-guide.md §2.3](operating-guide.md#13-local-toolchain-outside-both-repositories). Look an address up in `delphi_symbols.tsv` and grep `all_app_functions.txt` before running Ghidra at all. | Already set up on this machine. Elsewhere it is a significant one-time setup the skill cannot bootstrap. Most evidence (a save pair plus a note) only needs save-diffing with `IC2.Inspect`. |
| **`IC2.Inspect`**, this repo's read-only CLI | Comparing saves, listing armies/fleets/nations, rendering the map. | `dotnet build src/IC2.Inspect/IC2.Inspect.csproj`, then `--compare-saves`, `--inspect-army`, `--list-armies`, `--inspect-turn`, etc. — the full list is in the [README](../README.md#the-research-inspector-tools-ic2inspect). |

## The stages

Stage 0 runs before the skill and outside it; stages 1 and 2 are the skill's two dispatches, unchanged. An experiment's output enters stage 1 the way a note does, as the path or description the skill is given.

**Stage 0 — experiments: a rule question → a controlled run of the original.** The original game runs headless under Wine and Xvfb in a cloud container, and its fast variant finishes turns with no human at the keyboard ([autosave-hook feasibility report](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/2026-09-28-autosave-hook-feasibility.md), §5). An experiment uses that to answer a question by running the original on purpose, instead of waiting for a play-through to happen to show the answer.

- **What triggers it.** An open rule question in the research repository's [roadmap](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/roadmap.md); a corpus mismatch, where the clone's reading of a save or its next turn disagrees with the original's; or a new save whose next turn would settle something.
- **What it produces.** One of two shapes, each small enough for a report to cite:
  - a **before-and-after pair**: a save, the original's next turn run on it, and the save the original writes at the end of that turn, both parsed into the clone's model (`IC2.Inspect`, [prerequisites](#prerequisites-and-where-to-get-each-one)) and diffed;
  - a **sweep table**: one save crafted several times, with one field varied across a range at the offset [decompiled-sav-file-layout.md](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/decompiled-sav-file-layout.md) gives, each copy run through one turn, one row per value with the fields that moved.
- **Where it runs, and where its outputs go.** The runner lives in the research repository, under `scripts/`, in the same repository as the [evidence index](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/evidence-index.md) that maps its outputs. It runs in a cloud environment for the research repository with Wine, Xvfb and the fixtures repository attached ([operating-guide.md §1.3](operating-guide.md#13-local-toolchain-outside-both-repositories)). The saves it starts from, the crafted ones and the ones the original writes are game files, so they go to a release in [`diegoami/imp_conquest_fixtures`](https://github.com/diegoami/imp_conquest_fixtures/releases), as saves do today, and the evidence index gains their entries. The diff or the table is what the report cites.
- **What stage 1 receives.** When an experiment can answer the question, stage 1 receives the diff or the table, never a recording. A recording stays the input for what an experiment cannot produce, above all the tactical battle panel's per-exchange counts ([recording-analysis.md](recording-analysis.md)), and **a recording still needs no note**.
- **Wine is not the desktop.** A result seen only under Wine gets one confirmation on the desktop, the user's Windows machine where the game is played, before it settles a rule. Until then the report carries it as a candidate.
- **Prerequisites.** Stage 0 runs only once all three exist, and cannot build any of them for itself: (a) the **fast** variant of the executable; (b) the **autosave and log hook**, which saves at the end of every player turn and logs each firing (the feasibility report above is its design); (c) a **batch** variant that ends the turn on load, autosaves and exits. The variants are built by `patch_exe.py` in the originals folder.

**Stage 1 — evidence → RE findings.** One Opus agent, dispatched fresh (no shared context assumed). Give it: the specific note file (or a description of what's new), the research-repo write instructions above, the local evidence-folder paths, and the toolchain paths in case it needs them. Its job: read the note, correlate the named saves/recording, run controlled comparisons, decompile further only if the note's claim needs code-level confirmation and isn't already covered by an existing report, write a new report or update an existing one in the research repo following its `[confirmed]`/`[derived]`/`[designed]` discipline (any existing report shows the house style), commit and push to the research repo's `main` directly, and move the now-cited save/recording files to their `-processed/` siblings. Report back: which report(s) changed, the commit hash, a plain summary of what was newly confirmed or corrected, and anything it could not settle.

**Stage 2 — RE findings → this repository**, dispatched only after stage 1 reports back (never both at once — a real sequential dependency). A second, fresh Opus agent. Give it exactly what stage 1 changed (report names and commit hash — don't make it re-discover this). Its job is to check **every document claim** the new evidence touches, applied to the new evidence instead of a merged task. Each finding takes exactly one of four routes:

1. **Claims.** For each document — `design-audit.md` and `game-design.md` claims, the investigations index, release-plan gates, the operating guide's §7 open items, the README — decide whether the new evidence confirms, corrects or closes something, and draft the fix in place (current fact only, cited).
2. **Defects in merged code.** A merged constant, field or rule the evidence shows to be wrong is **filed as a `bug` issue, labelled `triage:needed`**, with the evidence ([build-process.md §4.6](build-process.md#46-bugs-and-follow-ups)), never patched. The main session's triage decides what happens to it.
3. **Tasks not yet dispatched.** A scope or DoD change to a task that has not started is drafted as an edit to that task's entry, `docs/tasks/T<nn>.md`, on the same review branch; a DoD only changes by a reviewed commit ([build-process.md §4.3](build-process.md#43-the-dod-is-not-negotiable-by-an-agent)).
4. **Design decisions.** A genuine judgment call is **not decided**: pose it plainly, the way `design-audit.md` §3's questions were raised, and stop.

Stage 2 writes no status, because status lives only in GitHub labels. It works in **its own worktree on a new branch** off `origin/main`, never in the main checkout, pushes that branch, and does not merge: evidence-driven changes are new content, so they go through review rather than straight to `main`. **Report back to the main session**: the branch, what changed and why, any bug issues filed, and any open question for the human. The planner triages the bugs ([build-process.md §4.6](build-process.md#46-bugs-and-follow-ups)), takes the branch and the open questions to the user, and decides what enters the build.

**The main session invokes `/process-evidence`** and coordinates both dispatches: dispatch stage 1, wait for its completion notification (don't poll), dispatch stage 2 with stage 1's actual output as input, then relay the result to the user. Neither stage dispatches build tasks. If stage 1 finds nothing worth writing up, stop there and say so — no stage 2 over nothing.

## Findings intake from ic2-conquest

[`diegoami/ic2-conquest`](https://github.com/diegoami/ic2-conquest) is a bot that plays the original game headless and drafts rule discoveries in its `findings/` folder, on any branch. A draft is a claim, not evidence. The research repository is the gate: its project skill `retrieve-findings` (`.claude/skills/retrieve-findings/SKILL.md` on its `main`) reviews each draft against the saves and the code, and promotes or corrects it into `docs/reports/`. Its ledger `docs/findings-intake.md` records every outcome: promoted, corrected, deferred or rejected. **A draft with no ledger row is unreviewed and is not citable; only promoted reports are.**

**Step 1, at every session start** (CLAUDE.md rule 10), and on demand ("retrieve findings"). List every draft under `findings/` on every remote branch of ic2-conquest, minus `README` and `PROMPT`. Drop those whose file name is in the ledger and whose content is unchanged since the reviewed commit; what remains is pending.
- **With the clone.** On the desktop it is in WSL, so run the research skill's step 1 there: `wsl.exe -e bash -lc 'cd ~/projects/ic2-conquest && git fetch --all --prune && for r in $(git for-each-ref --format="%(refname:short)" refs/remotes/origin); do git ls-tree -r $r -- findings/ | grep -v -e README -e PROMPT; done'`.
- **Without a clone.** Read the same branches through `gh api repos/diegoami/ic2-conquest/branches` and each branch's `git/trees/<sha>?recursive=1`.

Report the pending count, and the source used (the clone, the API, or neither reachable), in the session-start message only, never in a document (CLAUDE.md rule 3). Pull the research repository first, so the ledger is current.

**When drafts are pending**, `/retrieve-findings` runs the intake. It dispatches one fresh Opus Researcher in the research repository's checkout (`C:\Users\diego\projects\RE-imperial-conquest-2`). The main session reads the research skill's text from that checkout's `main` at dispatch time and pastes it into the brief (CLAUDE.md rule 15). The brief also says:
- **commit and push** to the research repository's `main` (operating-guide §3). This is the "asked" the research skill's own "do not push unless asked" waits for;
- **never write to ic2-conquest**: read it through the WSL clone, read-only;
- **promote nothing it could not verify**: deferrals and rejections go in the ledger with their reason.

**When it promotes or corrects a report**, stage 2 above runs on that change exactly as `/process-evidence` dispatches it. Its input is the promoted or corrected reports and the research commit. There is no second brief: stage 2 evaluates what the change means here, files defects in merged code as `bug` issues labelled `triage:needed` (never patching them), poses design decisions without deciding them, and edits only tasks not yet dispatched.

**The gate** (build-process.md Appendix C, step 0). Before `/run-task` dispatches a task, the main session compares each pending draft's headline and key terms with the task's Owns and Scope. If a draft concerns a rule the task touches, the task is held, the user is told why, and the intake runs first. Otherwise the intake never blocks work.

**Cadence.** Every session start, and on demand. A scheduled run is proposed to the user only if it fits how they work; none is created unasked.

### The retrieve-findings skill file

```markdown
---
name: retrieve-findings
description: Run the research repository's findings intake on pending ic2-conquest drafts, then stage 2 on anything it promoted or corrected. A thin dispatcher; main session only.
---

# /retrieve-findings

Context: the "Findings intake from ic2-conquest" section of `docs/evidence-pipeline.md` in
`imperial_conquest_2`.

1. Pull the research repository (`git -C C:\Users\diego\projects\RE-imperial-conquest-2 pull --ff-only`) and run
   step 1 of the intake (the clone in WSL, else the GitHub API). If nothing is pending, say so and stop.
2. Read `.claude/skills/retrieve-findings/SKILL.md` from the research repository's `main`. Dispatch ONE fresh
   Opus Researcher in that checkout with a brief that PASTES that text in full, names the pending drafts
   and their branches, and adds:
   - commit and push to the research repository's main;
   - never write to ic2-conquest (read it through `wsl.exe -e bash -lc 'cd ~/projects/ic2-conquest && …'`);
   - promote nothing you could not verify, and record every deferral and rejection in the ledger;
   - report each draft's outcome, the research commit, and every report created or corrected.
3. Wait for its completion notification (don't poll).
4. If it promoted or corrected anything, dispatch stage 2 exactly as /process-evidence does (the same
   brief, from docs/evidence-pipeline.md "Stage 2"), with those reports and the commit as input.
5. Relay to the user: each draft's outcome, the research commit, stage 2's proposals, and every bug it
   filed or decision it posed. Write no status anywhere.
```

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
