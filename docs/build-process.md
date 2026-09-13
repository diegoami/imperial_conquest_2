# Build process: how the multi-agent build runs

This document is the **process contract** for building the reimplementation: who does what, how a task goes from `status:ready` to merged, how the orchestrator loop works, how defects and documentation are handled, and the prompt templates and skill text the agents run from. **What** gets built — every task's scope, Owns list, Definition of Done and dependencies — lives in [task-catalogue.md](task-catalogue.md). **Why** — the design and the evidence behind it — lives in [game-design.md](game-design.md) and [design-audit.md](design-audit.md). How to operate the project day to day, and where everything lives, is in [operating-guide.md](operating-guide.md).

It changes **no design decision**. Every rule, constant and *done when* traces back to `game-design.md` and `design-audit.md`; where a task would need a design decision those documents do not make, the task escalates to the user rather than inventing one ([§4.6](#46-when-to-escalate-to-the-human)).

> **These documents are the intent. GitHub is the state.**
>
> Process, task scope, Definitions of Done, models, effort, dependencies and branch names live in this repository and change only by a deliberate commit to `main`. Progress — what is queued, in flight, in review, merged, blocked or escalated — lives in GitHub issue and PR labels, summarised per tick on the pinned **[tracking issue #29](https://github.com/diegoami/imperial_conquest_2/issues/29)**, and is authoritative there. The documents carry a **status snapshot** in exactly four named places, rewritten only by the post-merge documentation step and checked against the labels every tick ([§4.8](#48-documentation-update-after-every-merge)); task branches never touch it.

---

## 1. Constraints the pipeline works around

1. **The milestone list has real sequential dependencies.** Strength functions (M5) feed M8/M9/M12; economy (M3) gates recruitment (M4), naval (M7) and army management (M14); battle resolution (M8) gates siege (M9), diplomacy's reparation trigger (M11) and the AI (M12). The real graph is in [task-catalogue.md §1](task-catalogue.md#1-the-dependency-graph).
2. **One Windows machine.** Godot 4.7.2 (.NET edition) and the .NET 10 SDK are installed locally; GitHub Actions can build and test everything *except* the Godot project. Anything that launches or exports Godot is **single-instance**; anything that reads the user's original `.sav`/`.dat` files is **local-only** (those files are, and stay, outside the repository).
3. **Merge conflicts are a real cost**, not agent time. The foundation tasks (T01–T03) bought conflict-free seams so later tasks touch disjoint files.
4. **Fidelity is the thing that can silently go wrong.** `design-audit.md` §2 is a catalogue of plausible-looking claims that did not survive checking, and §4.5 concluded with a hard rule promoted here to a review gate: *a `[designed]` tag is only valid if the document says what was searched and came up empty*. A pipeline that merges wrong constants quickly is worse than a slow one.

---

## 2. How the build avoids conflicts

**2.1 Pre-committed seams (T03).** The seeded RNG service, the turn coordinator's ordered phase pipeline, command dispatch with a typed rejection type, the domain-event/news sink, and the ruleset accessor. Every system is written *against these interfaces* and registers itself, so no two tasks edit a shared wiring file.

**2.2 Declared file ownership.** Every task has an **Owns** list of paths. An implementer may only create or modify files inside its Owns list plus its own tests; anything else is a review failure and an escalation. A defect found in another task's files goes through the bug list ([§4.7](#47-the-bug-list)), never an inline patch.

**2.3 No shared registry files.** Three files would otherwise be edited by nearly every task:

- `IC2.sln` — T01 pre-declared every project the backlog needs, so no later task edits the solution.
- A system-registration list — replaced by assembly-scanned registration (each system declares itself via an attribute), so adding a system touches only that system's own file.
- Documentation — `README.md`, `docs/*.md` and the catalogue index are updated by the post-merge documentation step ([§4.8](#48-documentation-update-after-every-merge)) directly on `main`, never by a task branch.

**2.4 The fixtures corpus (T04).** Every exact number from the research reports is transcribed once into a typed JSON corpus (`tests/fixtures/corpus.json`) with per-entry provenance. Later tasks assert against `FixtureCorpus.Get("rome.taxBase")` rather than each re-reading the reports and each getting a chance to misread them.

> **Keeping the corpus current.** When a new research report lands, its constants are added by **whichever task owns the mechanic the report describes** — not by reopening T04, whose Owns list (`tests/fixtures/**`) already covers the addition and whose four DoD checks the top-up must keep green. Adding the report's filename to `tests/fixtures/known-reports.json` is part of that top-up. The 47th report, `supply-driven-morale-and-fleet-attrition.md`, is added by **T08** (DoD 13).

**2.5 Each system owns its own news messages.** T10 delivers the ring buffer, the message catalog, and the single writer that carries a news-worthy event from T03's event sink into `GameState.NewsLog`; **each gameplay task's DoD includes emitting its own confirmed message literal**. Emission is per-task and conflict-free; the path into state is exactly one piece of code, so news-log ordering never depends on which system wrote first.

---

## 3. Roles, models, and the effort scale

### 3.1 The three roles

| Role | Count | What it does |
| --- | --- | --- |
| **Implementer** | One at a time | One task, one branch, one PR, working directly in the main checkout. Writes code + tests, runs the DoD commands, pushes, opens the PR with evidence and a "Docs affected" list. |
| **Reviewer** | One at a time, never concurrent with an implementer | Independently re-runs the DoD commands on the PR head, audits provenance and scope, posts findings as a PR comment and applies a `status:approved`/`status:rework` label. Never the same agent instance that implemented. |
| **Orchestrator** | exactly 1 | Dispatches, tracks, merges, handles conflicts and escalations, triggers the post-merge documentation step. Does not write task code. |

Exactly **one code-modifying agent** (implementer or reviewer) runs at a time, in the shared main checkout, with no worktree isolation for pipeline agents ([§7](#7-concurrency-single-instance-and-local-only)). Planner, researcher and documentation subagents never touch the shared checkout — they work in their own worktree ([operating-guide.md §3.3](operating-guide.md#33-working-rules)).

### 3.2 The effort scale

| Level | Use when | Typical shape |
| --- | --- | --- |
| **Low** | Fully specified, mechanical, single-file, no judgment. A wrong answer is obvious immediately. | Config, templates, a workflow file. |
| **Medium** | Multi-file but the design is fully given; judgment limited to code structure. | Implementing a described state machine with given constants. |
| **High** | Requires reconciling evidence across several reports, or exact numeric/integer-semantics fidelity where a subtle error passes casual reading. | Economy, naval, siege, diplomacy. |
| **Ultrahigh** | An error propagates across the entire remaining backlog, or the solution space is genuinely open. | Only T03 (engine seams) and T22 (AI). |

### 3.3 Model selection

Per task, not uniform:

- **Opus** — 4 implementation tasks (T02, T03, T16, T22) where an error is not local: the domain model and engine seams are consumed by every other task; battle resolution is consumed by five downstream systems and is the most integer-semantics-sensitive code in the project; the AI has the most design latitude and the hardest failure mode (a soak that never terminates).
- **Sonnet** — 21 tasks. The default for "the design document already says what to build, and the hard part is building it correctly". Correction tasks whose evidence is fully pinned in the entry (T31) sit at Medium; ones that must read a format off decompiled code (T30) sit at High.
- **Haiku** — 5 tasks (T10, T11, T18, T26, T28) that are small, fully specified, and CI-gated.
- **Fable** — 1 task (T05), pure templates and configuration. Never used for anything that must compile against the domain model.

### 3.4 Why the reviewer's model differs from the implementer's

1. **Independence.** A reviewer running the same model tends to re-make the implementer's misreading of the same report — the failure mode `design-audit.md` §2.1 documents happening twice to the *same* claim. A different model reading the same evidence is the cheapest available independent perspective.
2. **Cost asymmetry.** Reviewing a diff costs a fraction of producing it; upgrading the reviewer one tier is cheap leverage.
3. **Task fit.** The reviewer's job is close reading and verification ("does this constant match the report it cites? does this integer division truncate the way Delphi's did?").

| Implementer | Reviewer | Plus |
| --- | --- | --- |
| Opus (T02, T03, T16, T22) | Opus / High | `/code-review --effort ultra` — see [§3.5](#35-where-the-code-review-skill-fits) |
| Sonnet on fidelity-critical tasks (T04, T07, T08, T13, T14, T17, T19, T20, T21, T29, T30, T31) | **Opus / Medium** | — |
| Sonnet on structural tasks (T01, T06, T09, T12, T15, T23, T24, T25, T27) | Sonnet / High | — |
| Haiku / Fable (T05, T10, T11, T18, T26, T28) | Sonnet / Medium | — |

### 3.5 Where the `/code-review` skill fits

The purpose-built reviewer agent is the **default gate**, not `/code-review`: three of the five review checks are project-specific (provenance-to-report tracing, the `[designed]`-tag rule, the Owns-list scope check), and the reviewer must re-run DoD commands locally, including Godot-headless runs and local-only fixtures a cloud reviewer cannot reach.

`/code-review --effort high` runs **inside** every reviewer's run as a correctness sweep. `/code-review --effort ultra` is extra scrutiny on the four Opus architecture PRs and on any PR at rework round 2. **When run by the orchestrator it is not independent** — every agent shares one GitHub account, so it falls back to a local pass under the same context. For **T16 and T22**, the user runs `/code-review --effort ultra` personally, from their own session, to get a genuinely independent pass.

---

## 4. The review and merge pipeline

### 4.1 The path a task takes

```text
issue status:ready
  → orchestrator spawns implementer (Agent, model per catalogue, working in the main checkout —
      no other code-modifying agent runs until this one finishes)
  → implementer: code + tests, runs DoD commands, pushes task/T<nn>-*, opens PR
      PR body: Closes #N, the Owns list it touched, a fenced DoD-evidence block (the exact
      commands run and their output tails, one per DoD line), and "Docs affected"
  → label status:in-review
  → orchestrator spawns reviewer (different model per §3.4, checks out the PR head in the
      main checkout — the implementer has finished and pushed)
  → reviewer re-runs every DoD command itself, posts findings as a PR comment (§4.2's five
      gates), then applies the label itself:
      all five gates pass  → status:approved
      any gate fails       → status:rework
  → status:approved + CI green + mergeable + all merge-after deps merged
      → orchestrator squash-merges, closes the issue, deletes the branch, label status:merged
      → orchestrator recomputes the ready set, dispatches the next task, then dispatches the
        documentation update (§4.8), all in the same tick
```

**Review is a comment plus a label, not a native GitHub review.** Every agent authenticates as the same GitHub account, and GitHub refuses to let an account approve or request changes on its own pull request, so `gh pr review` cannot work here. The reviewer applies `status:approved` or `status:rework` itself; the orchestrator reads the label, never `reviewDecision`. A label is not independently attributable the way a native review is — an accepted weakening.

### 4.2 What the reviewer checks

Five gates, in order; any failure is `status:rework`:

1. **DoD, independently reproduced.** The reviewer runs the commands itself at the PR head. The PR body's evidence is a convenience, never the proof. A DoD line with no runnable check is itself a finding.
2. **Provenance.** Every constant traces to a `tests/fixtures` entry, a cited report, or a `docs/investigations/` document. Any `[designed]` value must say *what was searched and came up empty* (`design-audit.md` §4.5).
3. **Determinism.** No `System.Random`, wall-clock, `Guid.NewGuid`, or order-dependent iteration in gameplay paths; every random draw goes through `IRng`; a seeded test proves reproducibility.
4. **Scope.** Every changed file is inside the task's Owns list. A change outside it is a finding even if it is a good change. The PR's "Docs affected" list is plausible for what the diff does.
5. **Correctness sweep.** `/code-review --effort high` over the diff.

A defect the reviewer finds in **another, already-merged task's** code is not a finding against this PR: it goes to the bug list ([§4.7](#47-the-bug-list)).

### 4.3 Is a third agent needed to merge?

**No third reviewer, but the reviewer does not merge — the orchestrator does.** The reviewer's judgment is local to one PR; the merge decision is global (merge order, waiting dependents, whether `main` moved). Separating judge from executor leaves a two-party audit trail on every merge. The only third voice is `/code-review --effort ultra` on the architecture PRs and at rework round 2 ([§3.5](#35-where-the-code-review-skill-fits)) — an additional opinion, not a gate; the human is the tiebreaker.

### 4.4 The DoD is not negotiable by an agent

An implementer that cannot satisfy a DoD line **escalates**. It never edits the DoD, never weakens an assertion to a range, never marks a test `Skip`, never deletes a failing assertion. A DoD line changes only by a commit to [task-catalogue.md](task-catalogue.md) on `main` after a human decision. The reviewer treats any diff to `task-catalogue.md` or this document from a task branch as an automatic `status:rework`.

### 4.5 Rework

Reviewer requests changes → the orchestrator sends the reviewer's findings to the **same implementer agent** via `SendMessage` — **the full findings list, verbatim or linked, never a hand-picked subset** → the agent pushes to the same branch and re-requests review. If that agent is gone, a fresh implementer is spawned with the PR, the review, and the task entry as input.

**Rework round 2 is the last one.** A third failing round escalates to the human with the task entry, the diff, both reviews, and the stated disagreement.

**Non-blocking findings.** A reviewer may approve with findings that fail no gate. The orchestrator collects them, at merge, into one `T<nn> follow-up` issue (the task's `phase:*`/`lane:*` labels, no `bug` label) linking the review comment. A follow-up is not a bug — nothing merged is wrong enough to block anything — and no task is suspended for it; the planner pass ([§4.7](#47-the-bug-list)) considers open follow-ups alongside bugs and folds each item into the next task that touches the files concerned. Open follow-ups: `gh issue list --search "follow-up in:title" --state open`.

### 4.6 When to escalate to the human

The orchestrator stops and asks when:

1. Rework round 3 would be needed ([§4.5](#45-rework)).
2. The task needs an answer to a `design-audit.md` §3 open question that its ruleset-flag workaround does not cover.
3. A DoD would have to be weakened to pass ([§4.4](#44-the-dod-is-not-negotiable-by-an-agent)).
4. Two reports disagree and the code must pick one.
5. A merge conflict needs a **semantic** decision — two branches changed the same behaviour.
6. A change would touch original game files, `assets.local.ini`, `.gitignore`'s exclusion policy, or anything in the research repo's `docs/reports/`.
7. A new external dependency (NuGet package, CDN asset, tool) would be added.
8. CI cannot be made green for a reason outside the task (toolchain, runner, Godot).
9. **Circuit breaker**: three consecutive tasks fail review, or two consecutive escalations occur — stop dispatching and report.
10. Anything destructive: force-push, `reset --hard`, amending a pushed commit, rewriting `main`, deleting an issue.

Everything else — including every `[designed]` placeholder `game-design.md` documents as a deliberate, revisitable choice — merges autonomously, subject to the architecture-PR sign-off in [§9](#9-standing-governance-decisions) Q-A.

### 4.7 The bug list

**A defect found in already-merged code is never patched by the task that found it, even narrowly, even when the fix is one line.** The process is **suspend, file, plan, resume**:

1. **Suspend.** The task that found the defect is set to `status:blocked` (not `status:rework` — the defect isn't its own) and stays blocked until the correction merges. It does not touch the upstream Owns list.
2. **File.** The defect becomes its own GitHub issue labelled `bug` plus the `lane:*`/`phase:*` labels that route it, stating what is wrong, the exact evidence (function, address, cross-check), the affected files or fields, and which tasks it blocks. It is not a catalogue entry by default — most bugs are smaller than a task.
3. **Plan.** A **planner pass** — a dedicated Opus subagent dispatch, not a routine tick — reviews open `bug` issues, triggered by a new one being filed or at a wave transition, and for each decides:
   - **a correction task** (next free `T` number, full catalogue shape) when the fix needs its own Owns list, model and reviewer;
   - **fold into an upcoming, not-yet-dispatched task's DoD** when the fix is naturally that task's territory and small;
   - **defer explicitly**, with a stated reason, when it is genuinely non-blocking. A filed bug is never silently dropped.

   The planner pushes its catalogue changes to a branch for human review, not straight to `main`.
4. **Resume.** The suspended task rebases onto the merged correction and continues.

The tick ([Appendix C](#appendix-c-the-build-tick-skill)) does not scan the bug list; it only files bugs and suspends. Current open bugs: `gh issue list --label bug --state open`.

### 4.8 Documentation update after every merge

Every merged task is followed by a **documentation update**, in the same tick as the merge. It is part of the merge, not an optional follow-up: a merge without its documentation update is an incomplete tick.

**Who runs it.** The orchestrator **dispatches a documentation subagent** (Sonnet / Medium) rather than editing inline, so the orchestrator's context stays small. The documentation subagent does not modify code, so it may run while the next implementer is working — but it **never touches the shared checkout**: it works in its own worktree and pushes straight to `main`, as routine post-merge sync:

```text
git -C <repo> fetch origin main
git -C <repo> worktree add <scratch>/docs-sync-<id> -b docs/sync-<id> origin/main
  … edit, commit ("Docs: sync after T<nn> merged" or "Docs: status resync") …
git -C <scratch>/docs-sync-<id> push origin HEAD:main
git -C <repo> worktree remove <scratch>/docs-sync-<id>; git -C <repo> branch -D docs/sync-<id>
```

`<id>` is `T<nn>` after a merge, or `resync-<yyyymmdd-hhmm>` for a status resync. If `main` moved meanwhile, rebase and push again; never force-push.

**When it runs.** In the tick that merged the task, **after** that tick's unblock and dispatch steps, so the labels it snapshots are the ones the tick leaves behind ([Appendix C](#appendix-c-the-build-tick-skill) step 6). A tick that merges two tasks dispatches one documentation update covering both. The same step with only part A — a **status resync** — runs in any tick that changed a task's document value without merging (a dispatch, an unblock, a suspension, an escalation), and in any tick whose drift check fails (below).

**Input.** The merged PR's **"Docs affected"** list (the implementer declares it, the reviewer checks it — [§4.2](#42-what-the-reviewer-checks) gate 4), the merge commit, the task entry, and the current labels (`gh issue list --label task --state all --json number,labels,state`).

#### Part A — the status snapshot

Status appears in the documents in exactly these four places and nowhere else. Every one is rewritten from the labels on every run, even when only one task moved:

| # | Location | What it holds |
| --- | --- | --- |
| A1 | [task-catalogue.md](task-catalogue.md): each entry's `- **Status**:` line, the [task index](task-catalogue.md#3-task-index)'s **Status** column, and the index's **Totals** line | One value per task (mapping below); "`N` merged as of `<sha>`" |
| A2 | [operating-guide.md §1 Current state](operating-guide.md#1-current-state) | As-of commit, phase, merged count and list, in progress, the ready set, open bugs and follow-ups, test counts |
| A3 | [README.md "Current state"](../README.md#current-state) | Phase, merged count, what's next, how to build and test now, with the test counts |
| A4 | [release-plan.md §2.1 Gate progress](release-plan.md#21-gate-progress) | Per release tag: the merged count and gate tasks, and every other gate task's value, the same mapping as A1 |

**Label → document value** (the only mapping; anything else in a Status field is drift):

| GitHub label | Document value |
| --- | --- |
| `status:merged` | `Merged (<short sha of the squash-merge commit>)` |
| `status:in-progress`, `status:in-review`, `status:rework`, `status:approved` | `In progress` |
| `status:ready` | `Ready` |
| `status:blocked` | `Blocked` (append `— suspended on #<bug>` when suspended by §4.7) |
| `status:escalated` | `Escalated` |

The "as of" commit in A1–A4 is the newest merge commit the snapshot includes. Test counts are re-counted after every merge by running `dotnet test IC2.sln` in the documentation worktree — never copied from a PR body; a status-only resync leaves them unchanged.

#### Part B — claims

Every item is checked, even when the answer is "no change":

1. **[task-catalogue.md](task-catalogue.md)** — dependency edges, wave notes or the critical path, if the task changed them.
2. **[design-audit.md](design-audit.md) and [game-design.md](game-design.md)** — if the task closed an `[open]` item, confirmed or corrected a claim, or answered a question, update the claim in place (current fact only, no narrative). A change that would alter a *design decision* rather than record a fact is not made here: it is raised with the user.
3. **[investigations/README.md](investigations/README.md)** — add a row if the task landed a new `docs/investigations/` document.
4. **[release-plan.md](release-plan.md)** — the gate list itself, if the task adds or removes a gate (for example, a new correction task that gates a release).
5. **[operating-guide.md §7 What's still open](operating-guide.md#7-whats-still-open)** — if the task closed or opened a research-level item.
6. **[README.md](../README.md)** — the build/test instructions and the runnable surface, when they change (the first runnable CLI, the first UI).

If the documentation subagent finds a defect in merged code while doing this, it files a bug ([§4.7](#47-the-bug-list)) rather than fixing it. The `/process-evidence` pipeline's stage 2 applies part B to new evidence ([evidence-pipeline.md](evidence-pipeline.md)); it never touches part A.

#### How a missed update is caught

- **By the tick.** Every tick's RECONCILE step ([Appendix C](#appendix-c-the-build-tick-skill) step 2) reads the task index on `origin/main` and compares each task's **Status** cell (ignoring a `— suspended on #<bug>` suffix) with its label through the mapping above, and the Totals line's merged count with the number of `status:merged` issues. A mismatch is **drift**: the tick lists it in its #29 report and dispatches a status resync (part A only) in step 6. The check is skipped while a documentation subagent is still running, since its push is the fix in flight. Because every tick syncs the changes it makes itself, drift at the start of a tick always means something was missed — a documentation subagent that failed, or a label changed outside the tick.
- **By the documentation subagent, before it commits, and by any later reader.** A1–A4 must agree with each other and name the same as-of commit. A disagreement, a Status value outside the mapping, or status written anywhere other than A1–A4 is a defect in the documentation commit that introduced it: fix it in the next documentation update, and report it in #29.
- **By the Docs affected list.** Every part-B document the merged PR's "Docs affected" list names must either appear in the documentation commit or be stated in its message as checked with no change needed.

---

## 5. The orchestrator

### 5.1 Who runs it

**An interactive Claude Code session on Opus, in the repository root, running `/loop 15m /build-tick`.** The same session is the user's main session: it dispatches every other role as a subagent (implementer, reviewer, planner, documentation, researcher) and keeps its own context small by doing so.

- `/loop` re-runs `/build-tick` every 15 minutes as a safety net; a subagent completing already wakes the session. The interval catches the cases notifications do not: nothing in flight, or a missed notification.
- `/build-tick` is a project skill whose full text is [Appendix C](#appendix-c-the-build-tick-skill). It is a local, git-ignored install at `.claude/skills/build-tick/SKILL.md`; reinstall it verbatim from Appendix C if missing. Skills load when a session starts, so a newly installed skill needs a fresh session.

Alternatives considered and not used as the driver: a scheduled cloud agent (cannot run the local-only and Godot DoDs; fine later for T28's nightly gate) and a PowerShell driver invoking `claude -p` per task (kept as the escape hatch a second machine would use — [§8](#8-adding-a-second-machine-later)).

### 5.2 Where the state lives

In GitHub, as issue and PR labels — the only authoritative record. The repository carries nothing but the status snapshot derived from these labels ([§4.8](#48-documentation-update-after-every-merge) part A), which never feeds back into a decision: the tick reads labels, not documents.

| Label | Meaning |
| --- | --- |
| `status:ready` | Dependencies merged, not yet dispatched |
| `status:blocked` | A `merge-after` dependency is unmerged, or suspended on a bug ([§4.7](#47-the-bug-list)) |
| `status:in-progress` | An implementer is running or a PR is open without a review |
| `status:in-review` | A reviewer is running |
| `status:rework` | Changes requested; round count in the issue's comments |
| `status:approved` | Reviewed and approved, awaiting merge |
| `status:merged` | Closed and merged |
| `status:escalated` | Waiting on the human |

Dependencies are recorded in each issue body as a task list of issue references. The pinned **tracking issue #29** carries one status-table comment per tick. A fresh session reconstructs the whole pipeline from `gh issue list` and `gh pr list` — no local state.

### 5.3 One tick

One tick is exactly the text of [Appendix C](#appendix-c-the-build-tick-skill), which is the single source: check pause → reconcile crashes and check documentation drift → drain finished PRs (merge, rework, conflicts, reviewer dispatch, bug filing) → unblock → dispatch at most one task → sync the docs (§4.8) → report to #29 → escalate. Two details worth knowing without reading it:

- **Stale green.** If a PR's branch predates the merge of one of its merge-after dependencies, its green CI ran against a stale base. Run `gh pr update-branch <number>` and wait for the fresh run before treating it as green.
- **Merge order is dependency order.** A task is never merged while a merge-after dependency is unmerged, even if its PR is green.

### 5.4 Merge conflicts

Prevention first — Owns lists, the pre-declared solution, attribute registration, per-system news messages and documentation-only-on-`main` ([§2](#2-how-the-build-avoids-conflicts)) mean most task pairs cannot conflict. When one happens:

1. **Mechanical conflict** (same file, different concerns): the implementer rebases on current `main` and re-runs its DoD. If the rebase changes nothing semantic, the approval stands.
2. **Semantic conflict** (both branches changed the same behaviour): escalate ([§4.6](#46-when-to-escalate-to-the-human) case 5).
3. **Repeated conflicts on one file** mean the Owns lists are wrong: fix the catalogue rather than re-resolving each time.

### 5.5 User-initiated pause

The human can stop dispatching for any reason, without finding the orchestrator's agent.

**Mechanism**: the label `orchestrator:pause` on tracking issue #29. `/build-tick` checks it first, every tick. If present: dispatch nothing, post a status comment to #29 saying what is open for manual review, and stop the loop.

- **To pause**: `gh issue edit 29 --add-label orchestrator:pause`, from any session. Optionally `SendMessage` the orchestrator to trigger the check sooner.
- **To resume**: `gh issue edit 29 --remove-label orchestrator:pause`, then restart `/loop 15m /build-tick`.
- **Nothing already running is killed.** In-flight subagents finish; their PRs wait unmerged. Interrupting the session also works, without the status comment.

---

## 6. Git and GitHub conventions

| Thing | Convention |
| --- | --- |
| Branch | `task/T<nn>-<slug>`, e.g. `task/T09-movement`. One per task, never reused, deleted on merge. |
| Commit subject | `T09: <imperative subject>` |
| Commit trailer | `Refs #<issue>`, plus the attribution lines each agent's harness provides |
| PR title | `T09 Movement and terrain` |
| PR body | `.github/pull_request_template.md`: `Closes #<issue>`, the Owns list touched, the fenced DoD-evidence block, the provenance checklist, the determinism checkbox, **Docs affected** |
| Review | A PR comment with the five-gate findings plus a `status:approved`/`status:rework` label applied by the reviewer ([§4.1](#41-the-path-a-task-takes)) |
| Merge | Squash, by the orchestrator, after CI green + approval + dependency order |
| Issue title | `T09 Movement and terrain` |
| Issue body | Scope, Owns, DoD as a checklist, model/effort, branch, `Blocked by #x` task list, a link to the task's anchor in [task-catalogue.md](task-catalogue.md), and the design milestone |
| GitHub milestone | One per phase: `Phase 0 Foundation`, `Phase 1 Pure rules`, `Phase 2 Systems`, `Phase 3 Delivery` |
| Labels | `task`; `bug`; `phase:0..3`; `lane:engine\|data\|ui\|infra`; `status:*`; `model:*`; `effort:*`; `release:*`; `local-only`; `single-instance`; `needs-human`; `orchestrator:pause` (issue #29 only) |
| Documentation sync | `Docs: sync after T<nn> merged` (or `Docs: status resync`), pushed straight to `main` from a worktree ([§4.8](#48-documentation-update-after-every-merge)) |

Issues link to their catalogue anchor (`docs/task-catalogue.md#t09-movement-and-terrain`); the [task index](task-catalogue.md#3-task-index) links back to each issue. Task issues #1–#28 match their task ids; T29 is #32, T30 is #37, T31 is #45.

---

## 7. Concurrency, single-instance, and local-only

- **Exactly one code-modifying pipeline agent at a time** — no concurrent implementers, no implementer alongside a reviewer, all in the shared main checkout, no worktrees for pipeline agents. The rules below are subsets of this one.
- **Documentation, planner and researcher subagents** do not modify code and never touch the shared checkout; they work in their own worktree and may run alongside the one code-modifying agent.
- **`single-instance` tasks** — T24, T25, T27 launch or export Godot 4.7.2 and share its `.godot` import cache and the `project.godot` header rewrite; the Godot lane is one serial chain.
- **`local-only` tasks** — T21, T29 and T30 need the user's original DAT/saves via `assets.local.ini`; T24/T25/T27 need a Godot install. Their tests **skip explicitly** when the prerequisite is absent, so CI on GitHub's runners stays green.
- **Two tasks that write `tests/fixtures/**`** (a corpus top-up) never run back to back without a rebase.
- **While T03 was in flight nothing else was dispatched**; the same applies to any future task that redefines engine seams.

---

## 8. Adding a second machine later

**Already generalises:** all pipeline state (issues, labels, PRs, milestones), branch-per-task, the fixtures corpus and toy world, the CI gate, the review contract. GitHub Actions is already a second machine running a subset of the DoDs.

**Coupled to this machine:** the orchestrator's single checkout, the Godot install and export templates (T24, T25, T27), the original saves and `assets.local.ini` (T21, T29, T30), and in-process `Agent` dispatch.

**To add a second machine** — three additive changes: replace in-process dispatch with claim-based pull (`gh issue edit --add-assignee` as the atomic claim, each machine running a `claude -p` worker loop); add capability labels (`requires:godot`, `requires:original-saves`) that workers match against; make CI the authority on test results, with the reviewer's local run supplementing only the local-only subset. No queue, scheduler or service is built now.

---

## 9. Standing governance decisions

Decided by the user; in force until changed.

- **Q-A — merge autonomy.** The orchestrator squash-merges any PR with reviewer approval and green CI without human sign-off, **except** the four architecture PRs (T02, T03, T16, T22), which wait for a human thumbs-up.
- **Q-B — Godot visual review.** T24 and T25 post a screenshot of every new screen to their PR as they land; "looks right" is the user's call on each screenshot. The published mockup (`game-design.md` §UI) is the layout intent.
- **Q-C — cost profile.** Opus on four implementation tasks and on the reviewer seat for the fidelity-critical PRs ([§3.4](#34-why-the-reviewers-model-differs-from-the-implementers)); `/code-review --effort ultra` on the architecture PRs ([§3.5](#35-where-the-code-review-skill-fits)); one code-modifying agent at a time ([§7](#7-concurrency-single-instance-and-local-only)).
- **Q-D — open audit questions become ruleset flags.** Every affected task ships the confirmed behaviour behind a named ruleset flag, grouped into two user-facing presets, `classical-faithful` and `improved`, chosen at New Game (`game-design.md` "Two shipped presets"). Mapping: Q3 → T19's `diplomacy.model`; Q4 → T08's `economy.purses`; Q5 → T12's `victory.default`; Q6 → `seatAsymmetry` (T09, T14, T15); Q7 → T18's generic `cityOrders` table; Q8 → T19's `bugPolicy.diplomaticThaw`; Q9 → T08's supply-purchase rule (free at your own cities, costs money elsewhere, in both presets); Q1 follow-up → T16's `combat.onDefeat`; Q10 → no change to `combat.onDefeat`.

---

## Appendix A: implementer prompt template

```text
You are implementing task T<nn> of the Imperial Conquest 2 build, working directly in the main
checkout. No other code-modifying agent runs at the same time as you — you have exclusive use of
the working tree until you finish, push, and open your PR.

Read first, in order:
  docs/task-catalogue.md     — find your task entry (#t<nn>-<slug>). It is the contract.
  docs/build-process.md      — §4 (review pipeline) and §4.7 (the bug list).
  docs/game-design.md        — design milestone M<n> and the Design principles at the top.
  docs/design-audit.md       — what the evidence actually supports; §2 is a list of
                               plausible-looking claims that did not survive checking.
  docs/operating-guide.md    — §5, collaboration norms (progress updates, save sampling).

Your task entry gives Scope, Owns, and Done when. All three are binding:
  - Create or modify files ONLY inside your Owns list, plus your own tests.
  - Satisfy every "Done when" line with a runnable check.
  - You may NOT edit any "Done when" line, weaken an assertion, skip a test, or edit
    docs/task-catalogue.md or docs/build-process.md. If a line cannot be satisfied, STOP and
    report why.
  - If you find a defect in another task's already-merged code, do NOT patch it. STOP and report
    it; the orchestrator files it as a bug (build-process.md §4.7).

Rules that apply to all engine code:
  - Every gameplay constant comes from the Ruleset or tests/fixtures, never a C# literal.
  - Every random draw goes through IRng. No System.Random, DateTime.Now, Guid.NewGuid.
  - Every number must trace to a research-repo report, a docs/investigations/ document, or the
    fixtures corpus. If you cannot find evidence for a number, do NOT invent one: report it.
    A [designed] value is only acceptable if you state what you searched and came up empty.
  - Original game files (EXE/DAT/SAV/WAV/screenshots/recordings) never enter the repository.

When done:
  1. `dotnet build IC2.sln` and `dotnet test IC2.sln` must pass.
  2. Run each "Done when" check and capture its command and output.
  3. Commit to branch task/T<nn>-<slug> with subject "T<nn>: <subject>" and trailer "Refs #<issue>".
  4. Push and open a PR with `gh pr create`, using the repository PR template. The body must
     contain "Closes #<issue>", the files you touched, a fenced DoD-evidence block with one
     command+output per "Done when" line, and a "Docs affected" list: which of task-catalogue.md,
     design-audit.md, game-design.md, investigations/README.md, release-plan.md,
     operating-guide.md and README.md this task's merge should update, and why (or "none").
  5. Report back: what you built, the DoD results, anything you could not verify, and any
     evidence conflict you found. Do not merge. Do not review your own PR.
```

## Appendix B: reviewer prompt template

```text
You are reviewing PR #<pr> for task T<nn> of the Imperial Conquest 2 build. You did not write it.
Check out the PR head directly in the main checkout (`gh pr checkout <pr>`) — the implementer has
already finished and pushed, so no other code-modifying agent is running concurrently with you.

Read: docs/task-catalogue.md (the task entry), docs/build-process.md §4.2 "What the reviewer
      checks", docs/game-design.md (milestone M<n>), docs/design-audit.md.

Run five gates, in order. Any failure is status:rework:
 1. DoD, reproduced by you. Run every "Done when" check YOURSELF. Do not trust the PR body.
    A "Done when" line with no runnable check is itself a finding.
 2. Provenance. Every constant traces to tests/fixtures, a cited research-repo report, or a
    docs/investigations/ document. Re-derive a sample from the source itself. Any [designed]
    value must say what was searched and came up empty (design-audit.md §4.5). A citation that
    does not contain the claim is a finding.
 3. Determinism. No System.Random / wall clock / Guid.NewGuid / order-dependent iteration in
    gameplay paths; randomness through IRng; seeded reproducibility proven by a test.
 4. Scope. Every changed file is inside the task's declared Owns list. Outside it is a finding
    even if the change is good. Any diff to docs/task-catalogue.md or docs/build-process.md is an
    automatic rework. The PR's "Docs affected" list matches what the diff actually changes.
 5. Correctness. Run /code-review --effort high over the diff for ordinary bugs.

A defect you find in ANOTHER, already-merged task's code is not a finding against this PR:
report it separately in your summary so the orchestrator files it as a bug (build-process.md §4.7).

Post your findings as a PR comment (`gh pr comment <pr> --body-file ...`) — specific, actionable,
file and line — covering all five gates explicitly, then apply the label yourself:
all five gates pass → `gh issue edit <issue> --add-label status:approved --remove-label status:in-review`
any gate fails      → `gh issue edit <issue> --add-label status:rework --remove-label status:in-review`
(NOT `gh pr review` — every agent shares one GitHub account, which GitHub refuses to let review
its own PR; the label is the approval signal.) Do not merge — the orchestrator merges. Do not fix
the code yourself. If you and the implementer are on round 2 of disagreement, say so explicitly
so the orchestrator escalates rather than starting a round 3. Leave the main checkout on `main`,
tree clean, when you finish.
```

## Appendix C: the `/build-tick` skill

Installed locally (git-ignored) at `.claude/skills/build-tick/SKILL.md`; reinstall verbatim from this block if missing. One invocation = one tick; `/loop 15m /build-tick` runs the pipeline.

```markdown
---
name: build-tick
description: Run one orchestration tick of the Imperial Conquest 2 multi-agent build — reconcile
  GitHub state, merge approved PRs and sync the docs, dispatch implementers and reviewers,
  escalate blockers.
---

Read `docs/build-process.md` (the process) and `docs/task-catalogue.md` (the tasks) first; they
are the contract. You are the orchestrator. You do not write task code yourself. You never touch
the shared checkout while a code-modifying agent is running in it.

1. CHECK PAUSE. `gh issue view 29 --json labels`. If it carries `orchestrator:pause`: post a
   status comment to #29 (what's open for manual review/merge, nothing new will be dispatched),
   then stop the loop entirely (do not schedule the next tick). Skip steps 2-8. This check runs
   before anything else, every tick. See build-process.md §5.5.

2. RECONCILE. `gh issue list --label task --json number,title,labels` and
   `gh pr list --label task --json number,headRefName,statusCheckRollup,mergeable,labels`.
   Compare with `ListAgents`. Any issue status:in-progress / status:in-review with no live agent
   and no open PR → reset to status:ready, delete the stale branch if one exists.
   DRIFT CHECK (skip it while a documentation subagent is still running): fetch origin/main and
   read the Status column of docs/task-catalogue.md §3. Map each task's label
   (`gh issue list --label task --state all --json number,labels`) to its document value, per
   build-process.md §4.8: merged → "Merged (<sha>)"; in-progress / in-review / rework /
   approved → "In progress"; ready → "Ready"; blocked → "Blocked", optionally with a
   "— suspended on #<bug>" suffix; escalated → "Escalated". Any row that differs, or a Totals
   merged count that differs from the number of status:merged issues, is drift: note it for
   step 7 and make step 6 run.

3. DRAIN. For each open task PR, read its issue's status:* label (the reviewer applies it itself;
   `reviewDecision` is never read):
   - status:approved + checks green + MERGEABLE and all merge-after deps status:merged
       → if the branch predates a merge-after dependency's merge, `gh pr update-branch` first and
         wait for fresh green. Then `gh pr merge --squash --delete-branch`, close the issue,
         label status:merged. If the review approved with non-blocking findings, collect them in
         one "T<nn> follow-up" issue (build-process.md §4.5). The merge's documentation update is
         dispatched in step 6 of this same tick — a merge without it is an incomplete tick.
   - status:rework → SendMessage the reviewer's FULL findings (verbatim or linked, never a
     subset) to the live implementer, or spawn a fresh one with the PR + review comment.
     Round 3 → escalate (build-process.md §4.5).
   - CONFLICTING → conflict protocol (build-process.md §5.4). Semantic conflict → escalate.
   - Neither label yet, no live reviewer → spawn the reviewer (Appendix B, model per the catalogue).
   - A defect found in ALREADY-MERGED code (not this PR's own task) → do not patch it, even
     narrowly. Set the finding task's issue to status:blocked (not status:rework), file a
     `bug`-labelled issue with the evidence, and leave triage to a planner pass
     (build-process.md §4.7). Bug triage is not a per-tick step.

4. UNBLOCK. Any status:blocked issue whose merge-after deps are all status:merged → status:ready.
   An issue suspended on a bug waits for that bug's correction, not just its merge-after deps.

5. DISPATCH at most one task, and only if no other code-modifying agent (implementer or reviewer)
   is running (check `ListAgents`). Nothing local-only on a machine without the prerequisite.
     Agent(subagent_type: "general-purpose", model: <catalogue>,
           prompt: build-process.md Appendix A filled in from the task entry)
   Label the issue status:in-progress. Do not poll for completion; the completion notification or
   the next tick picks up from there.

6. SYNC DOCS (build-process.md §4.8). Run this step if, in this tick, anything merged, any task's
   document value changed (dispatch, unblock, suspension, escalation), or step 2 found drift.
   Dispatch ONE documentation subagent (Agent, model sonnet) with: the merged PRs and their
   "Docs affected" lists (if any), the drift found (if any), and the instruction to work in its
   own worktree off origin/main, never the shared checkout; rewrite all four status locations
   (§4.8 part A: catalogue entry Status lines + index Status column + Totals, operating-guide §1,
   README "Current state", release-plan §2.1 gate progress) from the labels as they are now;
   for merges, also run §4.8 part B; commit "Docs: sync after T<nn> merged" (or "Docs: status
   resync") and push straight to main. It may run alongside a code-modifying agent, because it
   never touches the shared checkout.

7. REPORT. Post a status table (task, state, PR, agent, blocked-by) as a comment on tracking
   issue #29, plus any drift found in step 2, and summarise to the user: what merged, what
   started, what is blocked, what drifted, what needs them.

8. ESCALATE anything in build-process.md §4.6. If the circuit breaker tripped (3 consecutive
   review failures or 2 consecutive escalations), stop dispatching and report.

Never: merge without an approving review; weaken a Definition of Done; edit docs yourself
(status and claims go through the step-6 documentation subagent; plan changes — scope, DoD,
dependencies — are a planner pass on a review branch); force-push; review a PR yourself; patch a
defect in another task's Owns list — file it per build-process.md §4.7 instead.
```
