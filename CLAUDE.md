# Imperial Conquest 2

Read [`docs/operating-guide.md`](docs/operating-guide.md) before doing anything — it is the entry point: current state, where everything lives, how the project is operated, and the standing preferences. This file only lists the rules that must never be forgotten.

1. **The main session is the planner.** It spawns an orchestrator with a bounded mandate and never runs `/build-tick` itself ([build-process.md §5.1](docs/build-process.md#51-who-runs-it)).
2. **Check `ListAgents` before touching the shared checkout.** If anything is running, work in a separate `git worktree` on a new branch ([operating-guide.md §3.3](docs/operating-guide.md#33-working-rules)).
3. **Branch or `main` is decided case by case.** Only routine post-merge doc sync goes straight to `main` ([operating-guide.md §3.3](docs/operating-guide.md#33-working-rules)).
4. **A question is not a request to edit files.** Answer it; propose any fix and wait ([operating-guide.md §4](docs/operating-guide.md#4-standing-user-preferences)).
5. **Upstream defects go through the bug list.** Never patch another task's Owns list ([build-process.md §4.7](docs/build-process.md#47-the-bug-list)).
6. **Relay review findings verbatim** — the full list, never a subset ([build-process.md §4.5](docs/build-process.md#45-rework)).
7. **Commit and push research-repo work without asking** ([operating-guide.md §4](docs/operating-guide.md#4-standing-user-preferences)).
8. **Merge on GitHub's side, never by pulling into a checkout an agent is using** ([operating-guide.md §3.7](docs/operating-guide.md#37-merging-to-main-without-disturbing-running-agents)).
9. **At session start, check the triage queue** — `gh issue list --label triage:needed --state open` — and triage it (or tell the user) before other work. Never spawn an orchestrator mandate while an untriaged item blocks a task in its scope ([build-process.md §4.7](docs/build-process.md#the-triage-queue)).
