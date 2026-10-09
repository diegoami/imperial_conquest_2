# Running notes: an implementer's progress file

The user's decision of 2026-10-10, adopting the convention landed on the goal2-archaeology project (commit `007ac3a`) and in Goal3, without its GitHub tracking-issue half. An implementer keeps a short, append-only notes file in its worktree. A later round, the main session and the reviewer read it to learn what was planned, what landed and what was deferred.

## The file

- **Path:** `rendered/RUN-NOTES.md` in the implementer's worktree. `rendered/` is git-ignored in every worktree, so the file is never staged, even by `git add .`, and needs no `.git/info/exclude` line.
  - **Why not the worktree root:** the goal2 form excludes the root file in `.git/info/exclude`. In a linked worktree that file lives in the main checkout's shared git directory, which is outside the worktree, so the OpenCode guard denies the write ([#931](https://github.com/diegoami/imperial_conquest_2/issues/931)).
- **Who creates it:** the implementer, on its first step. A fresh worktree has none, and that is normal.
- **A rework round** runs in the same worktree (`scripts/external-implement.ps1` resumes the branch there), so it finds the file and continues it. A Claude agent taking over a run is pointed at it by its brief.
- **Never commit it, and never paste it into the PR body:** the PR carries the result; the notes carry the narrative of getting there.

## Its shape

```markdown
# RUN-NOTES — <task id>

## Plan
1. <step one>
2. <step two>

## Progress
- <timestamp> <one line: what was done, with the commit hash>

## Notes
- <timestamp> <one line: anything the next round or the reviewer needs>

## END
- last commit: <sha>
- PR state: <none / open / draft>
- deferred: <bullets, or "none">
```

**Plan** is written once, at the start, from the brief's Done-when lines. **Progress** gets a line after each commit. **Notes** gets a line whenever something surfaces that the next round needs: a design choice and why, a dead end, a question for the main session. **END** is the very last block, written before `git checkout --detach` or before stopping for any reason.

## The implementer's three rules

1. **Read it first.** A previous round may have left it. On creating it, **verify it is ignored**: `git check-ignore -q rendered/RUN-NOTES.md`. Exit 1 means it is not (someone removed `/rendered/` from `.gitignore`): delete the file, stop and report, rather than risk committing it.
2. **Append after every commit** a one-line Progress entry. Append only: an entry that turned out wrong gets a new entry saying it was reverted and why, never an edit.
3. **Write the END block before you stop**, whether finished, stopped to report, or out of time: the last commit, the PR state, and what is deferred.

## The reviewer's rule

**Read the implementer's notes before the diff.** A reviewer works in its own worktree at the PR head, where the implementer's uncommitted file does not exist. So the main session pastes it into the review brief, as it pastes the task entry (CLAUDE.md rule 15), and the reviewer reads it there. It gives the design and the deferred items, so the review grades against the design, not only the final state. A claim in the notes is the implementer's word, never evidence: the diff and the checks decide.

## Why it helps

- **An interrupted run** (an idle kill, a timeout, a denied write before [#934](https://github.com/diegoami/imperial_conquest_2/issues/934)): the next round, or the main session answering a stop-report, knows what was tried, what landed and what did not, without reading the model's database.
- **A long run:** anyone can read the file mid-run to see where it is.
- **The reviewer:** it starts from the design and the deferred list instead of reconstructing them from the diff.

## Hazards

- **Append only.** An edited Progress line hides what happened.
- **Never staged.** `rendered/` keeps it out; never move it elsewhere in the worktree.
- **Not a status record.** It lives only in the worktree and is never a document claim (CLAUDE.md rule 3): status stays on GitHub labels.
- **Not in the PR body.** Mixing the two bloats the PR and hands the reviewer the implementer's framing as if it were evidence.
