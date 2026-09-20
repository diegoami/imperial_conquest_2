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

---

## Token economy

These bound the cost of following rules 1–10. Where one appears to conflict with *"read
[`docs/operating-guide.md`](docs/operating-guide.md) before doing anything"*, it does not: that means
**read the operating guide**, not everything it links to.

Agreed on [#264](https://github.com/diegoami/imperial_conquest_2/issues/264), which measured the cost.

11. **Never read a large file in full. Extract the slice.** These are excerpt-only, and reading one
    whole is a defect rather than thoroughness:

    | File | Whole | Read it like this |
    | --- | ---: | --- |
    | `docs/task-catalogue.md` | ~85,000 tok | `awk '/^#### T31 /{f=1} f&&/^#### /&&!/^#### T31 /{exit} f' docs/task-catalogue.md` — **~1,450 tok** |
    | `data/worlds/classical-mediterranean.json` | ~100,000 tok | `jq 'del(.terrain.data)'` · `jq '.cities[] \| select(.name=="Rome")'` |
    | `data/rulesets/*.json` | ~20,000–23,000 tok | `jq '.<block>'` — the one block the task touches |
    | `tests/fixtures/corpus.json` | ~31,000 tok | `jq` the one failing entry |
    | `docs/build-process.md`, `game-design.md`, `design-audit.md`, `asset-specification.md` | ~13,000–16,000 tok | `grep -n '<heading>'`, then `sed -n 'A,Bp'` |

    `.terrain.data` is a 119,468-character base64 string on one line. It is never worth reading; its
    schema is in `src/IC2.Engine/Model/`.

    **This rule is a workaround for a layout problem, and should die.** Splitting the catalogue into
    per-task files and moving `.terrain.data` to a sidecar are the real fixes — planned, with the
    38-anchor rewrite costed, rather than deferred indefinitely.

12. **Scope every search.** `grep -rn "<pattern>" src tests`, never `grep -rn "<pattern>" .`. Prefer
    `git ls-files` over `find`: it honours `.gitignore` for free. Never traverse `bin/`, `obj/`,
    `.godot/`, `.vs/`, `.vscode/`, `.claude/`, `/rendered/`, `/screenshots/`, `/recordings/`, or
    `assets/packs/**/*.bmp` — git-ignored but present on disk, and a `.` search walks all of them.

13. **Surgical output.** Propose changes as minimal diffs or single functions. Never paste a file
    back to show a one-line edit — name the file and line, show the hunk. `Ruleset.cs` is 1,211
    lines and `InstantBattleResolver.cs` is 966; quoting either back costs four figures and tells the
    user nothing they lack. Do not restate a task's Owns/Scope/Done-when block that the user can read
    in the issue.

14. **Quiet terminal.** Raw command output is the cheapest way to waste a context window.
    `dotnet build|test IC2.sln --nologo -v q` and report counts; `git status --porcelain`;
    `git diff --stat` before any full diff; pipe anything long through `tail`/`head`/`grep`. On a red
    build or test, report the **failing assertion and its file:line**, never the whole run. Never
    `cat` a JSON data file — `jq` the path.

15. **A subagent brief carries the excerpt, not the pointer.** `/run-task` dispatches an implementer
    and then an independent reviewer, each cold, each in its own worktree, and a rework round doubles
    it again. A brief saying *"read `docs/task-catalogue.md`"* costs ~85,000 tokens **per agent, per
    round**. So **paste the extracted task entry into the brief**, name the exact files in its Owns
    list, and quote the `build-process.md` section the brief depends on with an anchor for the rest.
    On a rework round, relay the reviewer's findings in full (rule 7) — the findings, not the diff
    they refer to. **This is the highest-leverage rule here**: 11–14 save one agent's budget; this
    one saves every agent's, on every round.

16. **Offer a fresh session only at a true seam** — when a session ends on a completed task **and
    nothing is in flight**. Do not propose one on turn count, and never after a merge that a live
    investigation runs through: the harness already compacts long conversations, which drops the
    replay cost while keeping the reasoning, and a forced restart discards the half worth keeping.
    Any handoff names **issue numbers and labels only**; a prose summary of where the build stands is
    the status snapshot rule 3 forbids.
