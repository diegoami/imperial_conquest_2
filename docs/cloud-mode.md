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

## Bugs, follow-ups and triage

- **Bugs.** A defect in merged code is a `bug` issue labelled `triage:needed`. The task that found it is
  suspended, never patched across another task's files.
- **Triage** (the main session's queue: `gh issue list --label triage:needed --state open`) picks one
  outcome, records it in a comment and removes the label:
  1. **fix**: stays within the files the bug names and changes no rule's outcome. Branch `fix/<issue>-<slug>`,
     a test that fails before and passes after, commit `fix <issue>: <subject>`, PR body `Closes #<issue>`.
  2. **correction task**: a new `docs/tasks/T<nn>.md` and issue.
  3. **fold** into an upcoming task that touches those files.
  4. **close** with a reason.
- **Follow-ups.** Non-blocking review findings become one `T<nn> follow-up` issue per merge.
- **Plan and design changes** go through a PR; wording and routine doc claims go straight to `main`.
  A design decision is the user's, never decided by an agent.

## GitHub from the cloud: REST only

The cloud sandbox may block GraphQL, and `gh issue`, `gh pr` and `gh repo view` all use it, so they fail even with a valid
`GH_TOKEN` (`gh auth status` fails the same way). `gh api` on REST endpoints works. Use these:

```bash
R=repos/diegoami/imperial_conquest_2
# triage queue / ready tasks (issues endpoint also returns PRs: filter them out)
gh api "$R/issues?labels=triage:needed&state=open&per_page=100" --jq '.[]|select(.pull_request|not)|"#\(.number) \(.title)"'
gh api "$R/issues?labels=task,status:ready&state=open" --jq '.[]|"#\(.number) \(.title)"'
# labels
gh api -X POST "$R/issues/<n>/labels" -f 'labels[]=status:in-progress'
gh api -X DELETE "$R/issues/<n>/labels/triage:needed"
# comment
gh api "$R/issues/<n>/comments" -F body=@file.md
# open a PR, then squash-merge it
gh api "$R/pulls" -f title='T<nn>: <subject>' -f head=<branch> -f base=main -F body=@pr.md
gh api -X PUT "$R/pulls/<n>/merge" -f merge_method=squash
# CI state of a PR's head
gh api "$R/commits/<sha>/check-runs" --jq '.check_runs[]|"\(.name) \(.conclusion)"'
```

`git push`, clone and fetch are unaffected. In a session with the GitHub connector, its tools can replace these.
