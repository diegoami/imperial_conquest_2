# Build process

The loop, review policy, bug handling and milestone review are in **[cloud-mode.md](cloud-mode.md)**. This page keeps only
the conventions that outlast any tooling. The long desktop-era contract (roles, reviewer tiers, prompt
templates, two-machine rules, worktrees) is on the `local` branch.

## Gates a task must pass before merge

0. **Provenance**: every constant, rule or formula cites the report or design-doc section it comes from; where none exists, the
   task escalates to the user instead of inventing it.
1. **Done-when**: each line of the task's `docs/tasks/T<nn>.md` is met, with the command and the tail of its output in the PR body.
2. **Build and tests green**: `dotnet build IC2.sln --nologo -v q` and `dotnet test IC2.sln --nologo -v q`, plus CI.
3. **Scope**: the diff touches the task's Owns files (and tests for them); anything else is a bug or follow-up issue.
4. **Docs affected** are listed in the PR and fixed after the merge.

A reviewer, if one is used, checks these same gates. The DoD is not negotiable by the implementer: an unmet line is
reported, never reworded.

## Labels (status lives only here)

`status:ready` → `status:in-progress` → `status:in-review` (optional) → `status:merged`; side states `status:rework`,
`status:blocked`, `escalated`. Plus `task`, `fix`, `bug`, `triage:needed`, `release:<version>`.

## Git and GitHub conventions

- Branches: `task/T<nn>-<slug>`, `fix/<issue>-<slug>`, `plan/<slug>`, `docs/<slug>`. Squash-merge only.
- Commit subjects: `T<nn>: <subject>` or `fix <issue>: <subject>`. No closing keywords in commit messages or review comments;
  the PR body carries `Closes #N`.
- A task is sized to one PR; split by default when it exceeds one coherent change.
- Save format and ruleset schema may break at any pre-1.0 minor ([release-plan.md](release-plan.md)).

## Single-instance work

Only the Godot export and headless Godot checks need serialising: never run two Godot processes at once.
Tests that need the original game's files or `assets.local.ini` skip explicitly when they are absent.
