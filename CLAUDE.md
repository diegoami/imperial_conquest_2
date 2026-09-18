# Imperial Conquest 2

Read [`docs/operating-guide.md`](docs/operating-guide.md) before doing anything. It is the entry point: where everything lives, how the project is operated, and the standing preferences. This file lists only the rules that must never be forgotten.

1. **The main session runs the build.** It plans, runs tasks with `/run-task` (an implementer, then an independent reviewer, then the merge), triages bugs, and talks to the user. There is no orchestrator ([build-process.md §3.1](docs/build-process.md#31-the-roles), [Appendix C](docs/build-process.md#appendix-c-the-run-task-skill)).
2. **Agents never work in the main checkout.** Implementers and reviewers use their own worktrees under `C:\Users\diego\projects\ic2-work\`, and the main checkout is the main session's ([build-process.md §7](docs/build-process.md#7-concurrency-single-instance-and-local-only)).
3. **Status lives on GitHub labels only.** No document carries a status snapshot, so never add one ([build-process.md §5](docs/build-process.md#5-status-lives-on-github)). **Contracts live in the repository** (the catalogue, the process, the design documents and the investigations) because a review diffs them against a commit; **living reference lives in the [wiki](https://github.com/diegoami/imperial_conquest_2/wiki)** and is edited there, not here.
4. **Plan and design changes go to a branch for the user's review**; a merge's routine doc claims go straight to `main`. When unsure, ask.
5. **A question is not a request to edit files.** Answer it; propose any fix and wait.
6. **Upstream defects go through the bug list.** Suspend, file, plan, resume; never patch another task's Owns list ([build-process.md §4.6](docs/build-process.md#46-bugs-and-follow-ups)).
7. **Relay review findings in full**: the whole list, linked or verbatim, never a subset.
8. **Merge only with an approving review and green CI.** T16 and T22 also need the user's thumbs-up.
9. **Commit and push research-repo work without asking.**
10. **At session start**, check the triage queue (`gh issue list --label triage:needed --state open`) and any task left in flight (`status:in-progress`, `in-review`, `rework`, `escalated`), and tell the user where things stand.
