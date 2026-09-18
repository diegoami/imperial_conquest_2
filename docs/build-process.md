# Build process: how the build runs

This document is the **process contract** for building the reimplementation. It covers:
- who does what;
- how a task goes from `status:ready` to merged;
- how defects and follow-ups are handled;
- the prompt templates and the skill text the agents run from.

**What** gets built (every task's scope, Owns list, Definition of Done and dependencies) lives in [task-catalogue.md](task-catalogue.md). **Why** (the design and the evidence behind it) lives in [game-design.md](game-design.md) and [design-audit.md](design-audit.md). Day-to-day operation, and where everything lives, is in [operating-guide.md](operating-guide.md).

This document changes **no design decision**. Every rule, constant and Done-when line traces back to `game-design.md` and `design-audit.md`. Where a task would need a design decision those documents don't make, it escalates to the user rather than inventing one ([§4.5](#45-when-to-escalate-to-the-user)).

> **These documents are the intent. GitHub is the state.**
>
> The following live in this repository and change only by a deliberate commit to `main`:
> - the process;
> - task scope and Definitions of Done;
> - models, effort, dependencies and branch names.
>
> Progress lives **only** in GitHub issue and PR labels: what is ready, in flight, in review, merged, blocked or escalated. No document carries a status snapshot, so there is nothing to keep in sync and nothing to drift ([§5](#5-status-lives-on-github)).

---

## 1. Constraints the pipeline works around

1. **The milestone list has real sequential dependencies.** Strength functions (M5) feed M8, M9 and M12. Economy (M3) gates recruitment (M4), naval (M7) and army management (M14). Battle resolution (M8) gates siege (M9), diplomacy's reparation trigger (M11) and the AI (M12). The real graph is in [task-catalogue.md §1](task-catalogue.md#1-the-dependency-graph).
2. **One Windows machine.** Godot 4.7.2 (.NET edition) and the .NET 10 SDK are installed locally. GitHub Actions can build and test everything *except* the Godot project.
   - Anything that launches or exports Godot is **single-instance**.
   - Anything that reads the user's original `.sav`/`.dat` files is **local-only**. Those files are, and stay, outside the repository.
3. **Merge conflicts are a real cost**, not agent time. The foundation tasks (T01–T03) bought conflict-free seams so that later tasks touch disjoint files.
4. **Fidelity is the thing that can silently go wrong.** `design-audit.md` §2 is a catalogue of plausible-looking claims that did not survive checking. §4.5 of that audit ends in a hard rule, promoted here to a review gate: *a `[designed]` tag is only valid if the document says what was searched and came up empty*. A pipeline that merges wrong constants quickly is worse than a slow one. Most of the corrections so far (T31–T35, T38–T40) came from reviews and research, not from the implementers.

---

## 2. How the build avoids conflicts

**2.1 Pre-committed seams (T03).** T03 provides:
- the seeded RNG service;
- the turn coordinator's ordered phase pipeline;
- command dispatch with a typed rejection type;
- the domain-event/news sink, plus T40's read-only view of the run's published events;
- the ruleset accessor.

Every system is written against these interfaces and registers itself, so no two tasks edit a shared wiring file.

**2.2 Declared file ownership.** Every task has an **Owns** list of paths. An implementer may only create or modify files inside its Owns list plus its own tests; anything else is a review failure. A defect found in another task's files goes through the bug list ([§4.6](#46-bugs-and-follow-ups)), never an inline patch.

**2.3 No shared registry files.** Three kinds of file would otherwise be edited by nearly every task:
- **`IC2.sln`**: T01 pre-declared every project the backlog needs, so no later task edits the solution.
- **A system-registration list**: replaced by assembly-scanned registration. Each system declares itself with an attribute, so adding a system touches only that system's own file.
- **Documentation**: task branches never edit `docs/*.md` or `README.md`. The PR lists the document claims its change makes stale ("Docs affected"), and the main session applies them on `main` after the merge ([§4.7](#47-after-a-merge)).
- **The CLI demo's golden transcript** (`tests/fixtures/cli/demo.golden.txt`): it records what the engine prints, so any task that legitimately changes what the engine prints changes it too. Rather than name it in every Owns list, **any task whose change alters the demo's output may regenerate it** — by running the demo (`dotnet run --project src/IC2.Cli -- --script tests/fixtures/cli/demo.txt`), never by hand — and its PR shows the resulting diff and explains every changed line. A diff with a line the change doesn't explain is a review finding. T38 and T42 carry the older per-task permission in their entries; this rule supersedes it.

  The same applies to the **assertions that record the demo's behaviour**, in `tests/IC2.Engine.Tests/Presentation/GameSessionTests.cs`: the golden comparison, and the seed-sensitivity test that names which lines a different seed may change. A task that legitimately adds a random draw, or changes what a line prints, may adjust those assertions under the same conditions, and **must not weaken them**: the seed test still has to assert that the differing lines are *exactly* the known random-driven ones, never that differences are ignored. Widening that list without naming the new draw, or replacing an equality with a looser check, is a review finding. T35 is the first case: its quarterly loyalty draws are a second random consumer alongside weather, so the list becomes weather **and** loyalty rather than weather alone.

**2.4 The fixtures corpus (T04).** Every exact number from the research reports is transcribed once into a typed JSON corpus (`tests/fixtures/corpus.json`), with provenance on each entry. Later tasks assert against `FixtureCorpus.Get("rome.taxBase")` instead of each re-reading the reports, with each re-read being another chance to misread them.

> **Keeping the corpus current.** When a new research report lands, its constants are added by **whichever task owns the mechanic the report describes**, not by reopening T04. T04's Owns list (`tests/fixtures/**`) already covers the addition, and the top-up must keep T04's four DoD checks green. Adding the report's filename to `tests/fixtures/known-reports.json` is part of the top-up.

**2.5 Each system owns its own news messages.** T10 delivers three things: the ring buffer, the message catalog, and the single writer that carries news-worthy events into `GameState.NewsLog`. **Each gameplay task's DoD includes emitting its own confirmed message literal.** Emission is per-task and conflict-free. The path into state is exactly one piece of code, so the news log's order never depends on which system wrote first.

**2.6 Ruleset schema changes.** `Ruleset.cs` is shared: T02 created it, and each subsystem reads its own record. The loader rejects a missing or unknown field, so changing a record breaks every committed ruleset file that doesn't change with it. Two rules follow:

- A task whose DoD needs a constant its record doesn't carry adds the constant itself. Its Owns list names the record and the matching JSON block, for example `Ruleset.cs` (the `NavalRules` record only) and `toy-ruleset.json` (the `naval` block only). It never edits another task's record or block.
- The shipped rulesets are `classical-faithful.json`, generated by T29, and `improved.json`, which T36 authors from it. T29 merges after the last task that changes the schema (T15, T17, T19, T37). Any task that changes the schema after T29 has merged must do two things:
  - update T29's export mapping and re-run it, which makes the task `local-only`; the main session adds that to its entry;
  - update `improved.json` to match.

---

## 3. Roles, models, and the effort scale

### 3.1 The roles

| Role | Who | What it does |
| --- | --- | --- |
| **Main session** | The session the user talks to, on Opus | Plans and runs the build. It owns [task-catalogue.md](task-catalogue.md) and this document: scope, Definitions of Done, dependencies and order. It triages bugs and follow-ups, coordinates `/process-evidence`, and brings design questions and escalations to the user. **It runs tasks with `/run-task`** ([Appendix C](#appendix-c-the-run-task-skill)): it dispatches the implementer, then an independent reviewer, relays rework, merges approved PRs, and applies the doc claims each merge makes stale. |
| **Implementer** | A subagent, model per the catalogue | One task, one branch, one PR, **in its own worktree**. Writes code and tests, runs the DoD commands, pushes work in progress as it goes, and opens the PR with evidence and a "Docs affected" list. |
| **Reviewer** | A subagent, a different model per [§3.4](#34-why-the-reviewers-model-differs-from-the-implementers) | Independently re-runs the DoD commands at the PR head **in its own worktree**, audits provenance and scope, posts its findings as a PR comment, and applies `status:approved` or `status:rework`. It is never the agent that implemented. |
| **Researcher** | Opus subagents | Evidence work in the research repository: the two `/process-evidence` stages ([evidence-pipeline.md](evidence-pipeline.md)) and targeted research passes. |

**Only one task is in flight at a time**: its implementer, then its reviewer, then any rework. Pipeline agents never work in the main checkout (`C:\Users\diego\projects\imperial_conquest_2`). Each creates its own worktree ([§7](#7-concurrency-single-instance-and-local-only)). The main checkout belongs to the main session.

### 3.2 The effort scale

| Level | Use when | Typical shape |
| --- | --- | --- |
| **Low** | Fully specified, mechanical, single-file, no judgment. A wrong answer is obvious immediately. | Config, templates, a workflow file. |
| **Medium** | Multi-file, but the design is fully given; judgment is limited to code structure. | Implementing a described state machine with given constants. |
| **High** | Needs evidence reconciled across several reports, or exact numeric and integer-semantics fidelity, where a subtle error passes a casual reading. | Economy, naval, siege, diplomacy. |
| **Ultrahigh** | An error propagates across the entire remaining backlog, or the solution space is genuinely open. | Only T03 (engine seams) and T22 (AI). |

### 3.3 Model selection

Models are chosen per task, not uniformly:

- **Opus: 4 implementation tasks** (T02, T03, T16, T22), where an error is not local.
  - The domain model and engine seams are consumed by every other task.
  - Battle resolution feeds five downstream systems and is the most integer-semantics-sensitive code in the project.
  - The AI has the most design latitude and the hardest failure mode: a soak that never terminates.
- **Sonnet: 34 tasks.** The default for "the design document already says what to build, and the hard part is building it correctly".
  - Medium: correction tasks whose evidence is fully pinned in the entry (T31, T33, T34, T40, T42), and T37, a rule no task owned.
  - Low: a test-only correction (T32).
  - High: tasks that must read a format off decompiled code (T30), widen the shared domain model (T35), or rework merged economy code (T38, T39).
  - T10 moved from Haiku to Sonnet after its first attempt didn't converge. Integration design across the engine's seams is not Haiku work.
- **Haiku: 4 tasks** (T18, T26, T28, T36) that are small, fully specified and CI-gated.
- **Fable: 1 task** (T05): pure templates and configuration. Never used for anything that must compile against the domain model.

### 3.4 Why the reviewer's model differs from the implementer's

1. **Independence.** A reviewer on the same model tends to repeat the implementer's misreading of the same report; `design-audit.md` §2.1 records this happening twice to the *same* claim. A different model reading the same evidence is the cheapest independent perspective available.
2. **Cost asymmetry.** Reviewing a diff costs a fraction of producing it, so moving the reviewer up one tier is cheap leverage.
3. **Task fit.** The reviewer's job is close reading and verification: "does this constant match the report it cites? does this integer division truncate the way Delphi's did?"

| Implementer | Reviewer | Plus |
| --- | --- | --- |
| Opus (T02, T03, T16, T22) | Opus / High | `/code-review --effort ultra` ([§3.5](#35-where-the-code-review-skill-fits)) |
| Sonnet on fidelity-critical tasks (T04, T07, T08, T10, T13, T14, T17, T19, T20, T21, T29, T30, T31, T33, T34, T37, T38, T39, T40, T42, T43) | **Opus / Medium** | — |
| Sonnet widening the shared domain model (T35) | **Opus / High** | — |
| Sonnet on structural tasks (T01, T06, T09, T11, T12, T15, T23, T24, T25, T27, T32, T41) | Sonnet / High | human visual review on T24 and T25 |
| Haiku / Fable (T05, T18, T26, T28, T36) | Sonnet / Medium | — |

### 3.5 Where the `/code-review` skill fits

The purpose-built reviewer agent is the **default gate**, not `/code-review`. Three of the five review checks are project-specific:
- tracing provenance to the reports;
- the `[designed]`-tag rule;
- the Owns-list scope check.

The reviewer must also re-run DoD commands locally, including Godot-headless runs and local-only fixtures that a cloud reviewer cannot reach.

> **The skill cannot be used from inside a reviewer agent in this session.** Invoked there without an explicit target it forks; the fork runs in the main checkout rather than the reviewer's worktree, so `origin/main...HEAD` is empty and it falls back to `HEAD~1..HEAD` — reviewing whatever `main` merged last. On 2026-09-18 that wasted four review passes and twice produced nine confident findings about an unrelated commit, caught only because the main session noticed the findings named files outside the PR. **Reviewers sweep the diff inline instead**, and [Appendix B](#appendix-b-reviewer-prompt-template)'s gate 0 makes the target verifiable. If the skill is invoked at all, it gets the PR number as an explicit target, and its output is discarded unless every finding names a file from that PR's diff.

The correctness sweep runs **inside** every reviewer's run. `/code-review --effort ultra` adds scrutiny on the four Opus architecture PRs and on any PR at rework round 2. Every agent shares one GitHub account, so an ultra review launched from inside the pipeline is not independent. For **T16 and T22**, the user runs `/code-review --effort ultra` personally, from their own session.

---

## 4. The task loop

### 4.1 The path a task takes

```text
issue status:ready, every merge-after dependency merged
  → the main session labels it status:in-progress and dispatches the implementer
      (Agent, model per catalogue, Appendix A). It works in its own worktree.
  → implementer: code + tests, runs the DoD commands, pushes task/T<nn>-*, opens the PR.
      PR body: Closes #N, the Owns paths it touched, a fenced DoD-evidence block (the
      exact commands run and the tails of their output, one per DoD line), "Docs affected"
  → the main session labels status:in-review and dispatches the reviewer
      (a different model per §3.4, Appendix B), in its own worktree at the PR head
  → reviewer re-runs every DoD command itself, posts its findings as a PR comment
      (§4.2's five gates), then applies the label itself:
      all five gates pass → status:approved
      any gate fails      → status:rework (→ §4.4)
  → status:approved + CI green + mergeable
      → the main session squash-merges, labels status:merged, and runs §4.7
```

**Review is a comment plus a label, not a native GitHub review.** Every agent authenticates as the same GitHub account, and GitHub won't let an account approve or request changes on its own pull request, so `gh pr review` can't work here. The reviewer applies `status:approved` or `status:rework` itself, and the main session reads the label.

### 4.2 What the reviewer checks

Before the gates, **gate 0: the reviewer proves it is looking at the right code** — its HEAD equals the PR's head, and its `origin/main...HEAD` file list equals the PR's own file list, with both pasted into the review ([Appendix B](#appendix-b-reviewer-prompt-template)). An empty diff means the wrong tree. Every finding must name a file from that diff; anything else is a separate report.

Five gates, in order. Any failure means `status:rework`.

1. **DoD, independently reproduced.** The reviewer runs the commands itself at the PR head. The PR body's evidence is a convenience, never the proof. A DoD line with no runnable check is itself a finding.
2. **Provenance.** Every constant traces to a `tests/fixtures` entry, a cited report, or a `docs/investigations/` document. Any `[designed]` value must say *what was searched and came up empty* (`design-audit.md` §4.5).
3. **Determinism.** Gameplay paths use no `System.Random`, no wall clock, no `Guid.NewGuid`, and no order-dependent iteration. Every random draw goes through `IRng`, and a seeded test proves reproducibility.
4. **Scope.** Every changed file is inside the task's Owns list. A change outside it is a finding even if it's a good change. The PR's "Docs affected" list is plausible for what the diff does.
5. **Correctness sweep**, in the reviewer's own context ([§3.5](#35-where-the-code-review-skill-fits) says why the skill is not used here). It is a read of the PR's own diff, hunk by hunk, plus the surrounding code the diff doesn't show, hunting the classes that have actually bitten this project:
   - integer truncation and operation order (the original truncates at every step);
   - off-by-one in a cap or threshold, and the boundary either side of it;
   - a division or modulo whose denominator can be zero;
   - an unguarded null, empty collection or missing id;
   - order-dependent iteration, or a dictionary where order would leak into a result;
   - a branch that can never be taken (T12's domination win was dead code that shipped);
   - **a delete that leaves something behind** — three questions, every time an entity is removed from `GameState`: does anything still **reference** it, are its **resources** conserved, and does a **cap** still hold afterwards? This class has blocked three tasks (T14's fleet-to-fleet transfer, T39's mercenary desertion, and T46 was specified from the first). Both blockers produced the same symptom: a dangling id that `GameDataValidation.ValidateState` rejects, so the game writes a save it **cannot reload** — and the code paths in between degrade silently, which is why nothing surfaces until the load. `FleetState.CarriedArmyId` is the model's one cross-reference to an army id and the usual culprit; the reviewer's sweep is the whole model, not just that field;
   - **a test that would still pass if the behaviour were deleted** — the most common finding here, and the reason mutation is the proof below.

   A candidate is **proved before it is reported**: run it, or delete the behaviour and watch exactly which test fails. A finding with neither is labelled as unverified.

   **Fanning out is allowed, and is how the sweep scales**: the reviewer may dispatch one verification agent per candidate, each given the explicit claim, the file and line, and what evidence would confirm or refute it — never left to infer a target from its working directory ([§7](#7-concurrency-single-instance-and-local-only)). Verdicts come back confirmed, plausible or refuted, and "plausible" is reported as plausible.

A defect the reviewer finds in **another task's already-merged** code is not a finding against this PR. It goes to the bug list ([§4.6](#46-bugs-and-follow-ups)).

### 4.3 The DoD is not negotiable by an agent

An implementer that can't satisfy a DoD line **stops and reports**. It never edits the DoD, never weakens an assertion to a range, never marks a test `Skip`, and never deletes a failing assertion. A DoD line changes only by a commit to [task-catalogue.md](task-catalogue.md) after a decision by the user. The reviewer treats any diff to `task-catalogue.md` or this document from a task branch as an automatic `status:rework`.

### 4.4 Rework

When the reviewer asks for changes, the main session:
1. records the round on the issue (`review-round:1`, then `review-round:2`);
2. sends the implementer the **full findings, linked or verbatim, never a hand-picked subset**. It uses `SendMessage` if the implementer is still reachable. Otherwise it spawns a fresh implementer with the PR, the review comment URL and the task entry; the branch holds the earlier work.

The implementer pushes to the same branch, and the main session dispatches the reviewer again.

**Rework round 2 is the last.** A review that fails while the issue carries `review-round:2` escalates ([§4.5](#45-when-to-escalate-to-the-user)).

### 4.5 When to escalate to the user

The main session stops work on the task, labels its issue `status:escalated`, posts the evidence on the issue, and brings the decision to the user with the options and a recommendation, when:

1. Rework round 3 would be needed.
2. The task needs an answer to a `design-audit.md` §3 open question that its ruleset-flag workaround doesn't cover.
3. A DoD would have to be weakened to pass ([§4.3](#43-the-dod-is-not-negotiable-by-an-agent)).
4. Two reports disagree and the code must pick one. A short research pass often settles it; offer one.
5. A merge conflict needs a **semantic** decision, because two branches changed the same behaviour.
6. A change would touch any of these:
   - original game files;
   - `assets.local.ini`;
   - `.gitignore`'s exclusion policy;
   - anything in the research repo's `docs/reports/` other than through a research pass.
7. A new external dependency (NuGet package, CDN asset, tool) would be added.
8. CI can't be made green for a reason outside the task (toolchain, runner, Godot).
9. Two escalations in a row in one run: stop and ask before dispatching anything else.
10. Anything destructive: force-push, `reset --hard`, amending a pushed commit, rewriting `main`, deleting an issue.

Everything else merges without the user, subject to [§9](#9-standing-governance-decisions) Q-A. That includes every `[designed]` placeholder that `game-design.md` documents as a deliberate, revisitable choice.

### 4.6 Bugs and follow-ups

**A defect in already-merged code is never patched by the task that found it, even narrowly, even when the fix is one line.** The process is **suspend, file, plan, resume**:

1. **Suspend.** The task that found it goes to `status:blocked` and its issue says "suspended on #N". It doesn't touch the upstream Owns list.
2. **File.** The defect becomes a GitHub issue labelled `bug` and `triage:needed`. It states what is wrong, the exact evidence, and the affected files or fields. If it blocks tasks, its body opens with `Blocks: T<nn>[, T<nn>]`.
3. **Plan.** The main session triages it. It picks one of:
   - a **correction task**: the next free `T` number, in full catalogue shape, when the fix needs its own Owns list, model and reviewer;
   - **folding it into an upcoming task's DoD**: the default for small fixes and for follow-ups, folded into the next task that touches those files;
   - **deferring it**: close it as *not planned* with the reason. A bug that blocks a task is deferred only on the user's decision.

   Blocking is recorded in [task-catalogue.md](task-catalogue.md): either as a `merge-after` dependency on the correction, or as a DoD line in the blocked task. After that, the normal ready check enforces it. Catalogue changes go to a branch for the user's review. When triage is done, the main session removes `triage:needed` and comments where the item went.
4. **Resume.** The suspended task rebases onto the merged correction and continues.

**Follow-ups.** A reviewer may approve with findings that fail no gate. At merge, the main session collects them into one `T<nn> follow-up` issue labelled `triage:needed`, which links the review comments. A follow-up is not a bug, and no task is suspended for it. It is triaged the same way, usually folded into the next task that touches the files concerned.

**The queue** is `gh issue list --label triage:needed --state open`. It is checked at the start of every session, and before dispatching any task that an item names.

### 4.7 After a merge

In the same turn as the merge, the main session:

1. **Unblocks.** Every `status:blocked` task whose merge-after dependencies are now all merged, and which isn't suspended on an open bug, becomes `status:ready`.
2. **Files the follow-up** ([§4.6](#46-bugs-and-follow-ups)), if the review had non-blocking findings, and proposes where each item folds.
3. **Records the PR's "Docs affected" list**, and applies only what would otherwise leave a document **factually wrong**: a formula the code now implements differently, an `[open]` item the merge closed, a mis-attributed citation. Those go straight to `main` in a small `Docs:` commit, because a wrong provenance claim is what the review gates exist to catch. **Everything else waits for the release docs pass** ([release-plan.md §5](release-plan.md#5-release-checklist)): re-wording, counts, narrative and anything about where the build stands. Per-merge prose syncing was retired on 2026-09-18 — it was the step that kept drifting anyway, and the living pages now live in the [wiki](https://github.com/diegoami/imperial_conquest_2/wiki) where they carry no contractual force.
4. **Cleans up** the agents' worktrees for the task.
5. **Reports to the user**: the merge commit, what the review found, the follow-ups filed, and what is ready next.

---

## 5. Status lives on GitHub

Labels are the only status. The documents say what each task is. GitHub says where it stands:

```bash
gh issue list --label task --state all --json number,title,labels --jq '.[] | "\(.number)\t\(.title)\t\([.labels[].name | select(startswith("status:"))] | join(","))"'
gh issue list --label task --label status:ready          # what can run next
gh issue list --label triage:needed --state open         # untriaged bugs and follow-ups
gh issue list --label bug --state open                   # all open bugs
gh issue list --label task --label release:v0.2.0 --state all   # a release gate's progress
```

| Label | Meaning |
| --- | --- |
| `status:ready` | Every merge-after dependency is merged, and it isn't suspended on a bug. |
| `status:in-progress` / `status:in-review` / `status:rework` / `status:approved` | In flight. |
| `status:blocked` | Waiting on a dependency, or suspended on a bug (the issue says which). |
| `status:escalated` | Waiting on the user. |
| `status:merged` | Done; the issue is closed. |
| `review-round:1` / `review-round:2` | Rework rounds used ([§4.4](#44-rework)). |

**An interrupted session loses nothing.**
- The facts are in the labels and the PRs.
- An implementer pushes work in progress to its task branch as it goes.
- A new session reads the labels, finds any task that is in flight with no live agent, and continues it:
  - a pushed branch with no PR → resume the implementer from the branch;
  - a PR with no verdict → dispatch the reviewer;
  - `status:rework` → dispatch the implementer with the review URL;
  - no branch at all → back to `status:ready`.

---

## 6. Git and GitHub conventions

| Thing | Convention |
| --- | --- |
| Branch | `task/T<nn>-<slug>`, e.g. `task/T09-movement`. One per task, never reused, deleted on merge. |
| Worktrees | Agents create their own under `C:\Users\diego\projects\ic2-work\` ([§7](#7-concurrency-single-instance-and-local-only)). The main session removes them after the merge. |
| Commit subject | `T09: <imperative subject>` |
| Commit trailer | `Refs #<issue>`, plus the attribution lines each agent's harness provides |
| PR title | `T09 Movement and terrain` |
| PR body | `.github/pull_request_template.md`: `Closes #<issue>`, the Owns paths touched, the fenced DoD-evidence block, the provenance checklist, the determinism checkbox, **Docs affected** |
| Review | A PR comment with the five-gate findings, plus a `status:approved`/`status:rework` label applied by the reviewer |
| Merge | Squash, by the main session, after CI is green and the review approves |
| Issue title | `T09 Movement and terrain` |
| Issue body | **A pointer, not a copy**: a link to the task's entry in [task-catalogue.md](task-catalogue.md), its branch, and the bugs it closes. The catalogue entry is the contract, so nothing needs to be kept in step. |
| GitHub milestone | One per phase: `Phase 0 Foundation`, `Phase 1 Pure rules`, `Phase 2 Systems`, `Phase 3 Delivery` |
| Labels | `task`; `bug`; `phase:0..3`; `lane:engine\|data\|ui\|infra`; `status:*`; `review-round:1\|2`; `triage:needed`; `model:*`; `effort:*`; `release:*`; `local-only`; `single-instance`; `needs-human` |

Retired labels from the orchestrator era: `docs:pending`, `orchestrator:pause`, `triage:scheduled`, `triage:deferred`, `blocking`. They stay on old issues as history and are no longer applied. Tracking issue #29 is closed.

---

## 7. Concurrency, single-instance, and local-only

- **One task in flight at a time**: its implementer, its reviewer, and its rework, one after another.
- **A subagent cannot be pointed at a worktree.** Every agent starts in the session's working
  directory, the main checkout; an agent works in its worktree only because its brief tells it to
  use `git -C <worktree>` or to `cd` there first. That shell directory is **not inherited** by
  anything it spawns, so a skill or agent it forks starts back in the main checkout, where
  `origin/main...HEAD` is empty. That is the whole cause of the 2026-09-18 review failures
  ([§3.5](#35-where-the-code-review-skill-fits)). Two consequences:
  - an agent that needs a sweep of its diff does it **inline**, or passes the target explicitly
    (`/code-review --effort high <pr>`), never bare;
  - **every agent states where it worked**, so a wrong location is visible rather than inferred
    (below).
- **Say where you are working.** Each implementer and reviewer prints, in its **first** tool call
  and again in its **final report**, the four lines its brief asks for: the worktree's
  `git rev-parse --show-toplevel`, its `HEAD`, its branch or `detached`, and its
  `git diff --name-only origin/main...HEAD`. The main session checks that block before it relays a
  review or merges a PR: a report without it, or one naming the main checkout, is not acted on.
- **Agents work in their own worktrees**, never in the main checkout:
  - An implementer runs `git -C C:\Users\diego\projects\imperial_conquest_2 worktree add C:\Users\diego\projects\ic2-work\T<nn> task/T<nn>-<slug>`, creating the branch from `origin/main` if it doesn't exist yet.
  - A reviewer checks out the PR head detached, in `...\ic2-work\T<nn>-review`.
  - An implementer detaches its worktree (`git checkout --detach`) before it finishes, so the branch is free for the next checkout.
- **`local-only` tasks** (T21, T29, T30, T34) need the user's original DAT and saves via `assets.local.ini`. The file is git-ignored, so the agent copies it from the main checkout into its worktree's root. T24, T25 and T27 need a Godot install. These tests **skip explicitly** when the prerequisite is absent, so CI on GitHub's runners stays green.
- **`single-instance` tasks** (T24, T25, T27) launch or export Godot 4.7.2. The Godot lane is one serial chain.
- **Two tasks that both write `tests/fixtures/**`** (corpus top-ups) never run back to back without a rebase.
- **Tasks that redefine engine seams** (T03, T40) run with nothing else in flight.

---

## 8. Adding a second machine later

**What already generalises:**
- all pipeline state: issues, labels, PRs and milestones;
- branch-per-task, with a worktree per agent;
- the fixtures corpus and the toy world;
- the CI gate and the review contract.

**What is tied to this machine:** the Godot install and export templates (T24, T25, T27), the original saves and `assets.local.ini` (T21, T29, T30, T34), and in-process `Agent` dispatch.

**To add a second machine**, make three changes:
1. Use `gh issue edit --add-assignee` as an atomic claim on a task.
2. Add capability labels (`requires:godot`, `requires:original-saves`).
3. Make CI the authority on test results, with the reviewer's local run covering only the local-only subset.

No queue, scheduler or service is built now.

---

## 9. Standing governance decisions

Decided by the user; in force until changed.

- **Q-A, merge autonomy.** The main session squash-merges any PR that has an approving review and green CI without asking, **except** the architecture PRs T16 and T22. Those wait for the user's thumbs-up and the user's own `/code-review --effort ultra`. T02 and T03 are already merged.
- **Q-B, Godot visual review.** T24 and T25 post a screenshot of every new screen to their PR as they land, and "looks right" is the user's call on each one. The published mockup (`game-design.md` §UI) is the layout intent.
- **Q-C, cost profile.**
  - Opus implements four tasks, and reviews the fidelity-critical PRs ([§3.4](#34-why-the-reviewers-model-differs-from-the-implementers)).
  - `/code-review --effort ultra` runs on the architecture PRs.
  - One task is in flight at a time.
  - There is no orchestrator layer and no per-merge documentation agent (simplified 2026-09-14).
- **Q-D, open audit questions become ruleset flags.** Every affected task ships the confirmed behaviour behind a named ruleset flag. The flags are grouped into two user-facing presets, `classical-faithful` and `improved`, chosen at New Game (`game-design.md` "Two shipped presets"). The mapping:
  - Q3 → T19's `diplomacy.model`;
  - Q4 → T08's `economy.purses`;
  - Q5 → T12's `victory.default`;
  - Q6 → `seatAsymmetry` (T09, T14, T15);
  - Q7 → T18's generic `cityOrders` table;
  - Q8 → T19's `bugPolicy.diplomaticThaw`;
  - Q9 → T08 and T38's supply-purchase rule: free at your own cities, costs money elsewhere, in both presets;
  - Q1 follow-up → T16's `combat.onDefeat`;
  - Q10 → no change to `combat.onDefeat`.
- **Q-E, non-blocking review findings.** At each merge that has any, the main session files one `T<nn> follow-up` issue, and folds each item into the next task that touches those files ([§4.6](#46-bugs-and-follow-ups)).

---

## Appendix A: implementer prompt template

```text
You are implementing task T<nn> of the Imperial Conquest 2 build. The repository is
C:\Users\diego\projects\imperial_conquest_2 (GitHub diegoami/imperial_conquest_2). Do NOT work in
that directory: it is the main session's checkout. Work in your own worktree:

  git -C C:\Users\diego\projects\imperial_conquest_2 fetch origin
  - If origin has task/T<nn>-<slug>, an earlier attempt exists: continue it, and don't start over.
    Read its commits and any open PR first.
      git -C C:\Users\diego\projects\imperial_conquest_2 worktree add C:\Users\diego\projects\ic2-work\T<nn> task/T<nn>-<slug>
      git -C C:\Users\diego\projects\ic2-work\T<nn> merge --ff-only origin/task/T<nn>-<slug>   (a stale local copy catches up)
  - Otherwise create the branch from origin/main, then push it at once:
      git -C C:\Users\diego\projects\imperial_conquest_2 worktree add -b task/T<nn>-<slug> C:\Users\diego\projects\ic2-work\T<nn> origin/main
      git -C C:\Users\diego\projects\ic2-work\T<nn> push -u origin task/T<nn>-<slug>
  - <local-only tasks only> copy C:\Users\diego\projects\imperial_conquest_2\assets.local.ini into
    the worktree root. It is git-ignored; never commit it.

SAY WHERE YOU ARE WORKING. Your very first tool call, before reading anything, prints these four
lines, and your final report repeats them:

  git -C <your worktree> rev-parse --show-toplevel     # must be your worktree, NOT the main checkout
  git -C <your worktree> rev-parse --short HEAD
  git -C <your worktree> rev-parse --abbrev-ref HEAD   # your task branch, or "HEAD" if detached
  git -C <your worktree> diff --name-only origin/main...HEAD

If the first line is C:\Users\diego\projects\imperial_conquest_2, you are in the main session's
checkout: STOP and fix that before doing anything else. Nothing you spawn inherits your shell
directory, so always pass `git -C <your worktree>` explicitly rather than relying on `cd`.

Read first, in order:
  docs/task-catalogue.md  — your task entry (#t<nn>-<slug>). It is the contract.
  docs/build-process.md   — §2 (ownership), §4 (the loop, the review gates, the bug list).
  docs/game-design.md     — design milestone M<n> and the Design principles at the top.
  docs/design-audit.md    — what the evidence actually supports. §2 lists plausible-looking
                            claims that did not survive checking.
<extra context: the previous attempt's review URLs for a resumed task, research reports to read, etc.>

Your task entry gives Scope, Owns and Done when. All three are binding:
  - Create or modify files ONLY inside your Owns list, plus your own tests.
  - Satisfy every Done-when line with a runnable check.
  - Do NOT edit any Done-when line, weaken an assertion, skip a test, or edit any docs/*.md file.
    If a line can't be satisfied, STOP and report why.
  - If you find a defect in another task's already-merged code, do NOT patch it. STOP and report
    it; the main session files it as a bug (build-process.md §4.6).

Commit and push after every meaningful step, at least once per Done-when line you complete.
Never hold work only locally. Pushed commits are what a later attempt resumes from.

Rules for all engine code:
  - Every gameplay constant comes from the Ruleset or tests/fixtures, never a C# literal.
  - Every random draw goes through IRng. No System.Random, DateTime.Now or Guid.NewGuid.
  - Every number must trace to a research-repo report, a docs/investigations/ document or the
    fixtures corpus. If you can't find evidence for a number, don't invent one: report it.
    A [designed] value is acceptable only if you state what you searched and came up empty.
  - Original game files (EXE/DAT/SAV/WAV/screenshots/recordings) never enter the repository.
  - If your change DELETES an entity (an army, a fleet, a city, a unit slot), sweep your own
    diff before you finish: does anything still reference it, are its resources conserved, does
    a cap still hold? A dangling id makes a save that cannot be reloaded, and the paths in
    between degrade silently. This has blocked three tasks; the review will find it.

When done:
  1. `dotnet build IC2.sln` and `dotnet test IC2.sln` pass in your worktree.
  2. Run each Done-when check and capture its command and output.
  3. Commit everything on task/T<nn>-<slug> (subject "T<nn>: <subject>", trailer "Refs #<issue>")
     and push.
  4. Open a PR with `gh pr create`, using the repository PR template. The body must contain:
     "Closes #<issue>"; the files you touched; a fenced DoD-evidence block with one command+output
     per Done-when line; and "Docs affected", which lists the document claims this merge makes
     stale (file and what changes) or says "none".
  5. Run `git checkout --detach` in your worktree so the branch is free for the reviewer.
  6. Report back: what you built, the DoD results, anything you couldn't verify, and any evidence
     conflict you found. Don't merge, and don't review your own PR.
```

## Appendix B: reviewer prompt template

```text
You are reviewing PR #<pr> for task T<nn> of the Imperial Conquest 2 build. You did not write it.
Do NOT work in C:\Users\diego\projects\imperial_conquest_2 (the main session's checkout). Use your
own worktree at the PR head:

  git -C C:\Users\diego\projects\imperial_conquest_2 fetch origin task/T<nn>-<slug>
  git -C C:\Users\diego\projects\imperial_conquest_2 worktree add --detach C:\Users\diego\projects\ic2-work\T<nn>-review origin/task/T<nn>-<slug>
  <local-only tasks only> copy assets.local.ini from the main checkout into the worktree root.

GATE 0 — PROVE YOU ARE LOOKING AT THE RIGHT CODE. Before reading or judging anything, run these
five commands in your worktree and paste their output into your review comment, under a heading
"Where I reviewed". Repeat them in your final report:

  git rev-parse --show-toplevel                       # must be your worktree, NOT the main checkout
  git rev-parse HEAD                                  # must equal the PR's headRefOid
  gh pr view <pr> --json headRefOid --jq .headRefOid
  git diff --name-only origin/main...HEAD              # the files you are reviewing
  gh pr view <pr> --json files --jq '.files[].path'    # the files the PR says it changed

The two SHAs must match and the two file lists must match. If either differs, or if the diff is
EMPTY, STOP: you are in the wrong tree or at the wrong commit. Say so and ask, rather than
reviewing whatever you can see. An empty diff almost always means you are in the main checkout,
where `origin/main...HEAD` resolves to nothing.

Every finding you report must name a file from that diff. A finding about any other file is a
separate report, never a finding against this PR (§4.2).

Nothing you spawn inherits your shell directory: a forked skill or agent starts in the MAIN
CHECKOUT, not here. So pass `git -C <your worktree>` explicitly rather than relying on `cd`, and
see gate 5 before considering any forked tool.

Read: docs/task-catalogue.md (the task entry), docs/build-process.md §4.2 "What the reviewer
      checks", docs/game-design.md (milestone M<n>), docs/design-audit.md.
<extra context: earlier review rounds' URLs, if this is a re-review.>

Run five gates, in order. Any failure is status:rework:
 1. DoD, reproduced by you. Run every Done-when check YOURSELF; don't trust the PR body.
    A Done-when line with no runnable check is itself a finding.
 2. Provenance. Every constant traces to tests/fixtures, a cited research-repo report, or a
    docs/investigations/ document. Re-derive a sample from the source itself. Any [designed]
    value must say what was searched and came up empty (design-audit.md §4.5). A citation that
    doesn't contain the claim is a finding.
 3. Determinism. No System.Random, wall clock, Guid.NewGuid or order-dependent iteration in
    gameplay paths. Randomness goes through IRng, and a test proves seeded reproducibility.
 4. Scope. Every changed file is inside the task's declared Owns list. A file outside it is a
    finding even if the change is good. Any diff to docs/*.md is an automatic rework. The PR's
    "Docs affected" list matches what the diff actually changes.
 5. Correctness. Sweep the diff for ordinary bugs YOURSELF, in your own context: read it hunk by
    hunk, plus the surrounding code it doesn't show, and hunt integer truncation and operation
    order, off-by-one caps and their boundaries, division by a zero denominator, unguarded nulls
    and empty collections, order-dependent iteration, unreachable branches, and tests that would
    pass even with the behaviour deleted (build-process.md §4.2 gate 5 lists these).
    If the diff DELETES an entity from GameState, ask all three: does anything still reference
    it, are its resources conserved, does a cap still hold? That class has blocked three tasks,
    twice by producing a save that cannot be reloaded. Probe it with two entities — one
    deleted, one surviving — since an over-broad clear passes every single-entity test.
    PROVE a candidate before reporting it: run it, or delete the behaviour and watch exactly which
    test fails. Say so when a finding is unverified.
    You MAY fan out — one verification agent per candidate, each given the explicit claim, file and
    line, and what would confirm or refute it. Never let such an agent infer its target from a
    working directory; it starts in the main checkout, not here.
    Do NOT invoke the /code-review skill: from inside a reviewer agent it forks, the fork runs in
    the MAIN CHECKOUT rather than your worktree, its `origin/main...HEAD` is empty there, and it
    silently falls back to reviewing main's last commit. It produced full, confident findings about
    an unrelated merged commit four times on 2026-09-18 (§3.5). If you invoke it anyway, pass the
    PR number as an explicit target, then discard the run unless every finding names a file from
    gate 0's diff.

A defect you find in ANOTHER task's already-merged code is not a finding against this PR. Report
it separately in your summary, so the main session files it as a bug (build-process.md §4.6).
Mark each finding as blocking (fails a gate) or non-blocking.

Post your findings as a PR comment (`gh pr comment <pr> --body-file ...`): specific, actionable,
with file and line, covering all five gates explicitly. Then apply the label yourself:
  all five gates pass → gh issue edit <issue> --add-label status:approved --remove-label status:in-review
  any gate fails      → gh issue edit <issue> --add-label status:rework --remove-label status:in-review
Do NOT use `gh pr review`: every agent shares one GitHub account, and GitHub won't let an
account review its own PR, so the label is the approval signal.
Don't merge and don't fix the code yourself. When you finish, remove your worktree
(`git -C C:\Users\diego\projects\imperial_conquest_2 worktree remove <path> --force`).
```

## Appendix C: the `/run-task` skill

The skill is installed locally (git-ignored) at `.claude/skills/run-task/SKILL.md`; reinstall it verbatim from this block if it's missing. Skills load when a session starts. It is run by the main session.

```markdown
---
name: run-task
description: Run Imperial Conquest 2 build tasks end to end — dispatch the implementer, then an
  independent reviewer, relay rework, merge, apply doc claims, report. Main session only.
---

# /run-task [T<nn> ...]

Read docs/build-process.md §4 and each task's entry in docs/task-catalogue.md first. Given task
ids, run them in that order; given none, take the first status:ready task in the catalogue index.
Report to the user after each task; stop at any escalation.

0. CHECK. `gh issue list --label triage:needed --state open`: triage anything that names this
   task, or ask the user. Confirm every merge-after dependency is status:merged and the issue is
   status:ready. Don't start a local-only or single-instance task whose prerequisite is missing.
1. IMPLEMENT. Label status:in-progress. Dispatch the implementer: Agent(general-purpose, model =
   the catalogue's, run_in_background, prompt = build-process.md Appendix A filled in from the
   task entry, plus any review URLs from an earlier attempt). Wait for its completion
   notification; don't poll. If it reports a defect in merged code, go to step 5 (bug).
2. REVIEW. Check the PR exists and CI has run. Label status:in-review. Dispatch the reviewer:
   Agent(model = the catalogue's reviewer model, prompt = Appendix B filled in). Wait.
3. DECIDE on the label the reviewer applied:
   - status:approved: wait for CI green. If the branch is behind main, run
     `gh pr update-branch <pr>` and wait for green again. For T16 and T22, stop and get the
     user's thumbs-up first. Then `gh pr merge <pr> --squash --delete-branch`, label
     status:merged, remove review-round:*, and make sure the issue closed. Go to step 4.
   - Before acting on any agent's report, check its "where I worked" block: a worktree path under
     ic2-work\, a HEAD, a branch, and a non-empty diff list. No block, or the main checkout's path,
     means don't act on it — ask the agent to re-state its location, and re-dispatch if it really
     worked in the wrong tree.
   - status:rework: FIRST check the review is about THIS PR — gate 0's output is present, and
     every finding names a file in the PR's diff (`gh pr view <pr> --json files`). A review naming
     files outside it reviewed the wrong tree: discard it, say so, and re-dispatch the reviewer.
     Never relay it to the implementer.
     Then, if the issue carries review-round:2, escalate (step 5). Otherwise set the next round
     (none → review-round:1 → review-round:2) and send the implementer the FULL review comment
     URL: SendMessage if it is reachable, else a fresh implementer resuming the branch. Then go
     back to step 2.
4. AFTER THE MERGE (build-process.md §4.7):
   - Unblock: each status:blocked task whose merge-after deps are all merged, and which isn't
     suspended on an open bug, becomes status:ready.
   - Follow-up: if the review had non-blocking findings, file one "T<nn> follow-up" issue
     (triage:needed, linking the reviews) and propose where each item folds.
   - Docs: apply the PR's "Docs affected" claims on main ("Docs: after T<nn>"). Write no status.
   - Remove the task's worktrees under C:\Users\diego\projects\ic2-work\.
   - Report: the merge commit, the review's findings, the follow-ups, and what's ready next.
5. ESCALATE or BUG.
   - Escalate (build-process.md §4.5): label status:escalated, comment the evidence on the
     issue, and bring it to the user with options and a recommendation. Stop the run.
   - Bug in merged code (§4.6): label the task status:blocked with "suspended on #N". File the
     bug (bug, triage:needed; the body opens "Blocks: T<nn>"). Triage it, or bring it to the
     user. Stop the run.

Never: merge without an approving review and green CI; weaken a DoD; let the implementing agent
review its own PR; force-push; delete a task branch that holds unmerged work; patch a defect in
another task's Owns list; relay only part of a review.
```
