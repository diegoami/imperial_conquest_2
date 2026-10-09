# Imperial Conquest 2

Read [`docs/operating-guide.md`](docs/operating-guide.md) before doing anything. It is the entry point: where everything lives, how the project is operated, and the standing preferences. This file lists only the rules that must never be forgotten.

1. **The main session runs the build.** It plans, runs tasks with `/run-task` (an implementer, then an independent reviewer, then the merge), triages bugs, and talks to the user. There is no orchestrator ([build-process.md §3.1](docs/build-process.md#31-the-roles), [Appendix C](docs/build-process.md#appendix-c-the-run-task-skill)).
2. **Agents never work in the main checkout.** Implementers and reviewers use their own worktrees under `C:\Users\diego\projects\ic2-work\`, and the main checkout is the main session's ([build-process.md §7](docs/build-process.md#7-concurrency-single-instance-and-local-only)).
3. **Status lives on GitHub labels only.** No document carries a status snapshot, so never add one ([build-process.md §5](docs/build-process.md#5-status-lives-on-github)). **Contracts live in the repository** (the catalogue, the process, the design documents and the investigations) because a review diffs them against a commit; **living reference lives in the [wiki](https://github.com/diegoami/imperial_conquest_2/wiki)** and is edited there, not here.
4. **Plan and design changes go to a plan PR.** The routine tier merges on the opener's authority; the contract tier merges on another session's review, and waits for the user only when that review says `user decision` ([build-process.md §4.9](docs/build-process.md#49-plan-prs-two-tiers)). A merge's routine doc claims go straight to `main`. When unsure, ask.
5. **A question is not a request to edit files.** Answer it; propose any fix and wait.
6. **Upstream defects go through the bug list.** Suspend, file, plan, resume; never patch another task's Owns list. A bug whose fix stays in the files it names and changes no rule's outcome runs as a `fix`, not a task ([build-process.md §4.6](docs/build-process.md#46-bugs-and-follow-ups), [§4.10](docs/build-process.md#410-the-fix-lane)).
7. **Relay review findings in full**: the whole list, linked or verbatim, never a subset.
8. **Merge only with an approving review and green CI.** T16 and T22 also need the user's thumbs-up.
9. **Commit and push research-repo work without asking.**
10. **At session start**, check the triage queue (`gh issue list --label triage:needed --state open`) and any task left in flight (`status:in-progress`, `in-review`, `rework`, `escalated`), and tell the user where things stand. A task carrying another machine's `machine:*` label is that machine's: report it, never resume it ([build-process.md §8](docs/build-process.md#8-two-machines)). Also count the **unevaluated research reports** with `bash scripts/unevaluated-reports.sh` ([evidence-pipeline.md](docs/evidence-pipeline.md#what-triggers-it)). Report the count in the session-start message only, never in a document, and offer to run `/process-evidence` when it is not zero. If the script fails, report the failure and go on: it blocks nothing.

---

## Token economy

These bound the cost of following rules 1–10. Where one appears to conflict with *"read
[`docs/operating-guide.md`](docs/operating-guide.md) before doing anything"*, it does not: that means
**read the operating guide**, not everything it links to.

Agreed on [#264](https://github.com/diegoami/imperial_conquest_2/issues/264), which measured the cost.

11. **Never read a large file in full. Extract the slice.** The files below are excerpt-only unless
    their row says otherwise, and reading an excerpt-only file whole is a defect rather than
    thoroughness:

    | File | Whole | Read it like this |
    | --- | ---: | --- |
    | `docs/task-catalogue.md` | ~9,000 tok (index only, since [T61](https://github.com/diegoami/imperial_conquest_2/issues/273)) | small enough to `Read` whole; a task's own entry is `docs/tasks/T<nn>.md` — a plain `Read`, **~1,300 tok**, no `awk` needed |
    | `data/worlds/classical-mediterranean.json` | ~70,000 tok (terrain moved out by [T62](https://github.com/diegoami/imperial_conquest_2/issues/274)) | `jq 'del(.cities)'` · `jq '.cities[] \| select(.name=="Rome")'` |
    | `data/rulesets/*.json` | ~20,000–23,000 tok | `jq '.<block>'` — the one block the task touches |
    | `tests/fixtures/corpus.json` | ~31,000 tok | `jq` the one failing entry |
    | `docs/build-process.md`, `game-design.md`, `design-audit.md`, `asset-specification.md` | ~13,000–16,000 tok | `grep -n '<heading>'`, then `sed -n 'A,Bp'` |

    The terrain grid is the sidecar `classical-mediterranean.terrain.b64`, a 119,468-character base64
    string on one line. It is never worth reading; its schema is in `src/IC2.Engine/Model/`.

    What stays excerpt-only is what is large by nature: the world's city list, the rulesets, the fixtures
    corpus and the long design documents above ([T61](https://github.com/diegoami/imperial_conquest_2/issues/273)
    split the catalogue per task and [T62](https://github.com/diegoami/imperial_conquest_2/issues/274) moved the
    terrain grid to a sidecar).

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
    it again. A brief that points at the contract instead of carrying it makes **every agent, every round**
    read the index, the entry and whatever the entry links to (incident 14 on the wiki's
    [Process incidents](https://github.com/diegoami/imperial_conquest_2/wiki/Process-incidents) page). So **paste the extracted task entry into the brief**, name the exact files in its Owns
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

---

## Model choice

17. **The quota tracker chooses the model** (harness_imperial L50; the user's decision of
    2026-10-08). Before choosing, recommending or delegating to a model (an OpenCode implementer or
    reviewer, a Claude agent or subagent), read `curl -s 'localhost:8765/recommend?tier=heavy'`
    (implementation) or `tier=light` (small tasks and reviews) and take its `ranking` in order:
    `pick` is the first, `score` is spare calls per day until the pool's reset after projected
    demand and reserves, and `skipped` lists exhausted or failing providers. **Never rank providers
    by `headroom_pct` or `pace_pct`**: the pools differ hugely in size, some are monthly and some
    weekly, and Claude sessions draw on some as their main model. Map each row's provider and tier to
    our alias (minimax + heavy → `mm-m3`) and keep our exclusions on top:
    - Claude is the orchestrator, so it is not used for a delegated task unless nothing else has a
      positive score.
    - `gpt-5.6-luna` stays the simple-PR reviewer.
    - A reviewer is never of the implementer's family.
    - A negative score means that pool runs out before its reset: avoid it unless nothing else is left.
    - Alibaba is never used unless the user asks (the user's decision of 2026-10-09: its monthly
      pool is 91% used until 2026-11-06). `/recommend` never ranks it, and the scripts neither
      choose it nor move a route to it; with the user's say-so, `-AllowAlibaba` or `-Route alibaba`.

    Pass the choice explicitly (`-Model`/`-Reviewer`, or the Agent call's model) and log the chosen
    row's `reasons` on the task's issue or in the PR body. Right after a restart `note` says the
    statistics are loading and `ranking` is empty: wait and retry, never fall back to percentages.
    If the service does not answer, run `systemctl --user restart quota-tracker`, wait and retry; if
    it still fails, tell the user instead of guessing. Model ids come from the provider's live list
    (`opencode models <provider>`), never memory. Heavy models run at `medium` effort rather than
    `high`, or lighter when medium is not needed (the user's decision of 2026-10-05).
    `pwsh scripts/Choose-Model.ps1 -Role implementer` ranks by `/recommend` with these exclusions, and
    `-Role pair` gives an implementer and a reviewer of another family from `pair` and `ranking`
    (a null reviewer means no other family has quota: tell the user). Every
    `external-implement.ps1` run takes the explicit `-Model` so chosen: `-Model auto` was removed
    ([#893](https://github.com/diegoami/imperial_conquest_2/issues/893), the user's request of 2026-10-09).

18. **The light OpenAI model, and the reviewer `luna`, is GPT-5.6 Luna** (harness_imperial L51) on
    the direct OpenAI route: `openai/gpt-5.6-luna`, effort `high`. It draws on OpenAI's main quota like
    Sol, and is judged on that window. Only when OpenAI is exhausted does Luna's own limit matter:
    `/quota/openai` then lists `gpt-5.6-luna` under `when_exhausted.usable_models`, and Luna alone
    can still run (the user's decision of 2026-10-09; the scripts read that field). It is not
    GPT-6 Luna (`openai/gpt-6-luna`), which is never the reviewer. Never use a Luna on OpenCode Go (`opencode-go/…`): a proxy behind it returns
    `Bad Request` in long agent loops.

20. **Use the providers' pricing windows deliberately, not by chance** (the owner's decision of
    2026-10-06). Alibaba runs only when the user asks (rule 17); when they do, for a Qwen or DeepSeek
    route (`ali-qwen-*`, `ali-deepseek-*`; `-Route alibaba` or a `qwen*` model), read
    `curl -s localhost:8765/quota/alibaba | jq .pricing`: when `discount_now` is false, prefer
    another entry for a long run, or start it after `next_change_at`. Alibaba's GLM (`ali-glm`) has
    no discount, so the time does not matter for it. Likewise read
    `curl -s localhost:8765/quota/zai | jq .pricing`: when `peak_now` is true (Mon–Fri 14:00–18:00
    UTC+8; a promotion keeps it off-peak until the tracker's `promo_off_peak_until`, 2026-10-07 16:00
    UTC, so peaks start on 8 October, as the owner's note says), `glm-5.3` costs 3× quota, so prefer another provider for a
    long run then (not `ali-glm` unless the user asks). If the tracker does not answer, run as usual; never block on it.

---

## Delegated runs

19. **Read what a delegated run returned before you retry it, re-route it or call it a failure.
    Never retry blind** (the owner's rule of 2026-10-05).
    - **OpenCode runs** (`scripts/external-implement.ps1`, `scripts/external-review.ps1`): an exit
      with no PR or no posted review looks the same for an early end and for a run that stopped and
      reported a blocker, and the log's tail shows only the last tool output, not the model's final
      message. Read the final message from the session record:
      `python scripts/read-opencode-session.py <ses_…>` (the id is the log's
      `opencode: session ses_… started` line; add `--alibaba` for an `alibaba-token-plan/…` run). It opens the database read-only; never read
      `auth.json` beside it.
    - **Claude agents**: read the agent's final report (its hand-back) in full before acting.
    - **A run that stopped and reported gets an answer**: amend the task, decide, or escalate, and
      post the report on the task's issue so it is kept.
    - Treat any earlier "model X ends runs early" verdict as unconfirmed until its runs' final
      messages have been read.
