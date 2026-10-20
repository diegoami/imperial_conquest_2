# Imperial Conquest 2

Cloud-first. The previous, desktop-only process (worktrees, delegated OpenCode runs, mandatory
cross-family review) is frozen on the `local` branch and is **not** in force here. Read
[`docs/cloud-mode.md`](docs/cloud-mode.md) first: it is the whole process on one page. Then
[`docs/operating-guide.md`](docs/operating-guide.md) for where things live.

## Rules that must never be forgotten

1. **Work in the checkout you are given.** No git worktrees, no second clones. Branch, commit, push, open a PR.
2. **Review is optional and belongs to the harness.** Use the default harness (`/code-review`, a subagent, or none) when it helps. No required reviewer family, fresh checkout, worktree, or posted verdict. Merge needs green CI; nothing else.
3. **Milestones get one big review**, run directly from the CLI by `scripts/review-milestone.sh`, not by the harness ([docs/milestone-review.md](docs/milestone-review.md)).
4. **Status lives on GitHub labels only.** No document carries a status snapshot.
5. **Everything we produce stays reachable**: small files in the repo, big ones in a release or the Drive location ([docs/artifacts.md](docs/artifacts.md)). Original-game files, and anything derived from them, never enter the repo or a release (copyright).
6. **A question is not a request to edit files.** Answer it; propose a fix and wait.
7. **Upstream defects go through the bug list** (file an issue; do not patch another task's files).
8. **Research-repo work** (`imperial-conquest-2-research`): commit and push without asking.
9. **At session start**, run `gh issue list --label triage:needed --state open` and list anything `status:in-progress`, and tell the user where things stand.

## Token economy

- Never read a large file whole: `docs/task-catalogue.md` is an index (a task is `docs/tasks/T<nn>.md`); use `jq` on `data/worlds/*.json` and `data/rulesets/*.json` (`jq 'del(.cities)'`); never open `*.terrain.b64` or `tests/fixtures/corpus.json`; for the long design docs `grep -n` a heading, then `sed -n`.
- Scope searches (`grep -rn x src tests`), never `.`. Skip `bin/ obj/ .godot/ rendered/ screenshots/ recordings/ assets/packs/`.
- Quiet terminal: `dotnet build|test IC2.sln --nologo -v q`; report counts and the failing assertion with file:line.
- Minimal diffs; do not paste files back.
