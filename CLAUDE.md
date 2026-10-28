# Imperial Conquest 2

Cloud-first. The previous, desktop-only process (worktrees, delegated OpenCode runs, mandatory
cross-family review) is frozen on the `local` branch and is **not** in force here. Read
[`docs/cloud-mode.md`](docs/cloud-mode.md) first: it is the whole process on one page. Then
[`docs/operating-guide.md`](docs/operating-guide.md) for where things live.

## Rules that must never be forgotten

1. **Work in the checkout you are given.** No git worktrees, no second clones. Branch, commit, push, open a PR.
2. **Review is optional and belongs to the harness.** Use the default harness (`/code-review`, a subagent, or none) when it helps. No required reviewer family, fresh checkout, worktree, or posted verdict. Merge needs green CI; nothing else.
3. **Milestones get one big review, by another model.** The session's job ends at the freeze: claims in the plan, the `review/` and `review-base/` branches, and the review PR whose description is the brief. Never run the review yourself, start a reviewer CLI or `scripts/review-milestone.sh`, or post a verdict; the owner hands the PR to the other model ([docs/milestone-review.md](docs/milestone-review.md)).
4. **Status lives on GitHub labels only.** No document carries a status snapshot.
5. **Everything we produce stays reachable**: small files in the repo, big ones in a release or the Drive location ([docs/artifacts.md](docs/artifacts.md)). Original-game files, and anything derived from them, never enter the repo or a release (copyright).
6. **A question is not a request to edit files.** Answer it; propose a fix and wait.
7. **Upstream defects go through the bug list** (file an issue; do not patch another task's files).
8. **Research-repo work** (`imperial-conquest-2-research`): commit and push without asking.
9. **At session start**, check the triage queue (`gh issue list --label triage:needed --state open`; if GraphQL is blocked, the REST form in [docs/cloud-mode.md](docs/cloud-mode.md#github-from-the-cloud-rest-only)) and anything `status:in-progress`, and tell the user where things stand.
10. **Use the manual before GUI work.** Read the relevant original-game manual sections and screenshots through [the operating-guide entry point](docs/operating-guide.md#original-game-ui-reference-private). Cite the expected behavior and any specifically approved exception; do not infer permission for a different screen from a narrow convenience change. Find and record discrepancies proactively before asking the owner to rediscover them through playtesting.

## Token economy

- Never read a large file whole: `docs/task-catalogue.md` is an index (a task is `docs/tasks/T<nn>.md`); use `jq` on `data/worlds/*.json` and `data/rulesets/*.json` (`jq 'del(.cities)'`); never open `*.terrain.b64` or `tests/fixtures/corpus.json`; for the long design docs `grep -n` a heading, then `sed -n`.
- Scope searches (`grep -rn x src tests`), never `.`. Skip `bin/ obj/ .godot/ rendered/ screenshots/ recordings/ assets/packs/`.
- Quiet terminal: `dotnet build|test IC2.sln --nologo -v q`; report counts and the failing assertion with file:line.
- Minimal diffs; do not paste files back.
