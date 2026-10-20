# Cloud mode: the whole process

This replaces the desktop-only process frozen on the `local` branch (worktrees, delegated OpenCode
runs, mandatory cross-family review, per-task model choice). Where
[build-process.md](build-process.md) or [operating-guide.md](operating-guide.md) disagree with this
page, **this page wins**; they are being cut down to what still applies.

## The loop

1. Take a task (`gh issue list --label task --label status:ready`; the entry is `docs/tasks/T<nn>.md`).
2. Label it `status:in-progress`. Branch from `main` in the checkout you have: `task/T<nn>-<slug>`.
3. Build it, run its Done-when commands, commit and push as you go.
4. Open a PR (body: `Closes #N`, the Done-when evidence, "Docs affected").
5. **Review, if you want one**: the harness's own (`/code-review`, a subagent) or none. A reviewer needs
   no different model family, no fresh checkout, no worktree, and posts nothing. Findings, if any, are
   fixed on the same branch.
6. CI green → `gh pr merge --squash`, label `status:merged`, apply the doc claims the merge made stale.

A task may run on any model the session happens to use. Nothing in the repository names one.

## What is gone

Git worktrees and `ic2-work/`; `external-implement.ps1` / `external-review.ps1` style dispatch as a
requirement; reviewer tiers, families, Jev routing, quota recommendations; run notes and watch
monitors; the "one task in flight per machine" and `machine:*` rules (use separate branches; a Godot
check that needs the machine's one Godot process is the only serialisation left).

## Milestone review (the one mandatory review)

Per release milestone, one big review, run **directly from a CLI** and not through the harness:
`scripts/review-milestone.sh v0.7.0`. See [milestone-review.md](milestone-review.md).

## Artifacts

[artifacts.md](artifacts.md): what we produce lives in the repo, a release, or the Drive location.

## Cloud environment notes

- Needs: .NET 10 SDK, `gh`, `jq`, Python 3; Godot 4.7.2 mono only for the Godot tasks.
- `assets.local.ini` and the original game files do not exist in the cloud. Tests that need them skip;
  CI sets `IC2_FIXTURES_DIR` from the private `ic2-test-fixtures` repository, and cloud sessions with
  access can do the same (`gh repo clone diegoami/ic2-test-fixtures`, then export the variable).
