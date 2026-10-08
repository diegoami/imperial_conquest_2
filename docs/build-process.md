# Build process: how the build runs

This document is the **process contract** for building the reimplementation. It covers:
- who does what;
- how a task goes from `status:ready` to merged;
- how defects and follow-ups are handled;
- the prompt templates and the skill text the agents run from.

**What** gets built (every task's scope, Owns list, Definition of Done and dependencies) lives in the task entries, one file per task under [`tasks/`](tasks/), indexed by [task-catalogue.md](task-catalogue.md). **Why** (the design and the evidence behind it) lives in [game-design.md](game-design.md) and [design-audit.md](design-audit.md). Day-to-day operation, and where everything lives, is in [operating-guide.md](operating-guide.md).

The rules here are stated without their history. The incidents that produced them are numbered on the wiki's [Process incidents](https://github.com/diegoami/imperial_conquest_2/wiki/Process-incidents) page, and a rule cites its incident as "(incident N)".

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
2. **Godot exists only on the user's machines; the original game files are on the user's machines and in the private fixtures repository CI fetches** ([§7](#7-concurrency-single-instance-and-local-only), [§8](#8-two-machines)). GitHub Actions builds and tests everything, the Godot project included: since 2026-09-28 the `godot-headless` job ([#477](https://github.com/diegoami/imperial_conquest_2/pull/477)) downloads the Godot .NET editor, builds `godot/IC2.MapViewer.csproj` and runs the T24 and T25 check scenes. Only the screenshot tours and the visual sign-off stay on the user's machines.
   - Anything that launches or exports Godot is **single-instance**.
   - Anything that reads the user's original `.sav`/`.dat` files is **local-only**. Those files are, and stay, outside the repository.
3. **Merge conflicts are a real cost**, not agent time. The foundation tasks (T01–T03) bought conflict-free seams so that later tasks touch disjoint files.
4. **Fidelity is the thing that can silently go wrong.** `design-audit.md` §2 is a catalogue of plausible-looking claims that did not survive checking. §4.5 of that audit ends in a hard rule, promoted here to a review gate: *a `[designed]` tag is only valid if the document says what was searched and came up empty*. A pipeline that merges wrong constants quickly is worse than a slow one. Most corrections have come from reviews and research, not from the implementers (incident 1).

---

## 2. How the build avoids conflicts

**2.1 Pre-committed seams (T03).** T03 provides:
- the seeded RNG service;
- the turn coordinator's ordered phase pipeline;
- command dispatch with a typed rejection type;
- the domain-event/news sink, plus T40's read-only view of the run's published events;
- the ruleset accessor.

Every system is written against these interfaces and registers itself, so no two tasks edit a shared wiring file.

**2.2 Declared file ownership.** Every task has an **Owns** list of paths. An implementer may only create or modify files inside its Owns list plus its own tests and the other files [§4.2](#42-what-the-reviewer-checks) gate 4 treats as implicitly owned; anything else is a review failure. A defect found in another task's files goes through the bug list ([§4.6](#46-bugs-and-follow-ups)), never an inline patch.

**2.3 No shared registry files.** Three kinds of file would otherwise be edited by nearly every task:
- **`IC2.sln`**: T01 pre-declared every project the backlog needs, so no later task edits the solution.
- **A system-registration list**: replaced by assembly-scanned registration. Each system declares itself with an attribute, so adding a system touches only that system's own file.
- **Documentation**: task branches never edit a Markdown file anywhere under `docs/` (`docs/**/*.md`, which includes the task entries in `docs/tasks/`) or `README.md`. The PR lists the document claims its change makes stale ("Docs affected"), and the main session applies them on `main` after the merge ([§4.7](#47-after-a-merge)).
- **The CLI demo's golden transcript** (`tests/fixtures/cli/demo.golden.txt`): it records what the engine prints, so any task that legitimately changes what the engine prints changes it too. Rather than name it in every Owns list, **any task whose change alters the demo's output may regenerate it** — by running the demo (`dotnet run --project src/IC2.Cli -- --script tests/fixtures/cli/demo.txt`), never by hand — and its PR shows the resulting diff and explains every changed line. A diff with a line the change doesn't explain is a review finding. This rule supersedes the older per-task permissions (incident 2).

  The same applies to the **assertions that record the demo's behaviour**, in `tests/IC2.Engine.Tests/Presentation/GameSessionTests.cs`: the golden comparison, and the seed-sensitivity test that names which lines a different seed may change. A task that legitimately adds a random draw, or changes what a line prints, may adjust those assertions under the same conditions, and **must not weaken them**: the seed test still has to assert that the differing lines are *exactly* the known random-driven ones, never that differences are ignored. Widening that list without naming the new draw, or replacing an equality with a looser check, is a review finding (incident 2).

- **The data-file field reference** (`docs/scenario-authoring.md`, T26): a reflection test asserts that it names every public field of `World`, `Ruleset` and `Scenario`, including their nested types, so the reference cannot silently go stale. Any task that legitimately adds, renames or removes such a field therefore changes it too. Rather than name it in every Owns list, **any task whose diff adds, renames or removes a public field of those types may edit that file**, but only the entries for those fields: the field, what it does, its unit or range, and its `_provenance` convention. It is the **one** exception to the documentation rule above. The PR names each entry it touched, and an edit to any other part of the file is a review finding. Added 2026-09-27 by the user's decision (incident 3).

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
| **Main session** | The session the user talks to, on Opus | Plans and runs the build. It owns the task entries (`docs/tasks/T<nn>.md`, indexed by [task-catalogue.md](task-catalogue.md)) and this document: scope, Definitions of Done, dependencies and order. It triages bugs and follow-ups, coordinates `/process-evidence`, and brings design questions and escalations to the user. **It runs tasks with `/run-task`** ([Appendix C](#appendix-c-the-run-task-skill)): it dispatches the implementer, then an independent reviewer, relays rework, merges approved PRs, and applies the doc claims each merge makes stale. |
| **Implementer** | An OpenCode run (`scripts/external-implement.ps1`) on the model the entry names, the cheap tier by default; a Claude subagent only on an architecture task ([§3.3](#33-model-selection)) | One task, one branch, one PR, **in its own worktree**. Writes code and tests, runs the DoD commands, pushes work in progress as it goes, and opens the PR with evidence and a "Docs affected" list. |
| **Reviewer** | An OpenCode run (`scripts/external-review.ps1`) or a Claude subagent, by the PR's review tier, never of the implementer's model family ([§3.4](#34-why-the-reviewers-model-differs-from-the-implementers)) | Independently re-runs the DoD commands at the PR head **in its own worktree**, audits provenance and scope, posts its findings as a PR comment, and applies `status:approved` or `status:rework` (with two or three reviews, the main session applies it from all of them). It is never the agent that implemented. |
| **Researcher** | Opus subagents | Evidence work in the research repository: the two `/process-evidence` stages ([evidence-pipeline.md](evidence-pipeline.md)) and targeted research passes. |

**Only one task is in flight at a time per machine** ([§8](#8-two-machines)): its implementer, then its reviewer, then any rework. Pipeline agents never work in the main checkout (`C:\Users\diego\projects\imperial_conquest_2`). Each creates its own worktree ([§7](#7-concurrency-single-instance-and-local-only)). The main checkout belongs to the main session.

### 3.2 The effort scale

| Level | Use when | Typical shape |
| --- | --- | --- |
| **Low** | Fully specified, mechanical, single-file, no judgment. A wrong answer is obvious immediately. | Config, templates, a workflow file. |
| **Medium** | Multi-file, but the design is fully given; judgment is limited to code structure. | Implementing a described state machine with given constants. |
| **High** | Needs evidence reconciled across several reports, or exact numeric and integer-semantics fidelity, where a subtle error passes a casual reading. | Economy, naval, siege, diplomacy. |
| **Ultrahigh** | An error propagates across the entire remaining backlog, or the solution space is genuinely open. | Only T03 (engine seams) and T22 (AI). |

### 3.3 Model selection

Models are chosen per task, in the task's entry, by what an error would cost:

- **OpenCode, on the model the quota tracker chooses** (the user's decision of 2026-10-08, CLAUDE.md rule 17, replacing the fixed chain of 2026-10-05; Claude credit stays the scarce resource, and OpenCode Go has been `opencode-go/…` since fix #551). The main session reads `/recommend?tier=heavy` and passes `-Model <alias>` explicitly, with rule 17's exclusions. Since [T152](tasks/T152.md), `-Model auto` does the same through `scripts/Choose-Model.ps1`. A chain model with no route that has quota is skipped. GLM ended long implementer runs early (#557, #562), and Go's Luna failed long runs with `Bad Request` (#553). OpenCode runs use effort `high`, not `max`, except where a model offers no `high` or is heavy: Qwen3.8 Flash runs at `medium` (it offers `low`, `medium`, `xhigh`), and the heavy reviewers run light (GLM-5.3 and Qwen3.8 Max at `low`, Sol at `low`, DeepSeek V4 Pro at its lightest, `high`). No free or Zen model is used. An entry that still says Sonnet reads as the default.
- **No separate tier for larger entries** (fix 573, its GLM exclusion lifted by the user's decision of 2026-10-08): a High-effort entry, an entry that says Opus and is not an architecture task, and a task that failed a rework round all run the model rule 17 takes from `/recommend?tier=heavy`. `glm`, `glm-flash` and `luna` stay valid as an explicit `-Model` value. The reviewer is set by the PR's review tier ([§3.4](#34-why-the-reviewers-model-differs-from-the-implementers), the user's decision of 2026-10-03): Luna for a simple PR only, GPT-6 Sol for a complex one, a cold Claude Opus for a very complex one (Sol plus the OpenCode pair when Claude implemented it). An entry's **Reviewer** field, and the catalogue index's Reviewer column, were written under the earlier rule and now name only the tier's default (an "Opus" there is the very complex tier's reviewer, or the fallback); the tier rule decides, and a `+ human visual review` or `+ ultra` suffix still applies. A "Luna pair" in an entry or the index reads as the OpenCode pair (the user's decision of 2026-10-04).
- **Claude Opus only on an architecture task**, where an error is not local: the domain model and the engine seams, battle resolution, the AI (T02, T03, T16, T22). Sonnet implements only as the fallback when OpenCode is unavailable (the script exits 3), and then for every task OpenCode would have run, whatever model the entry names (the user's decision of 2026-09-29); otherwise it reviews only as the OpenCode pair's fallback second reviewer ([§3.4](#34-why-the-reviewers-model-differs-from-the-implementers)); structural tasks are complex and go to GPT-6 Sol.
- **Haiku** is retired (the user's decision of 2026-09-27) and is never assigned; a task small enough for Haiku is cheap enough on Sonnet (incident 4). Two tasks merged on Haiku before that, T36 and T77.
- **Fable** for pure templates and configuration, never for anything that must compile against the domain model.

### 3.4 Why the reviewer's model differs from the implementer's

1. **Independence.** A reviewer on the same model tends to repeat the implementer's misreading of the same report; `design-audit.md` §2.1 records this happening twice to the *same* claim. A different model reading the same evidence is the cheapest independent perspective available.
2. **Cost asymmetry.** Reviewing a diff costs a fraction of producing it, so moving the reviewer up one tier is cheap leverage.
3. **Task fit.** The reviewer's job is close reading and verification: "does this constant match the report it cites? does this integer division truncate the way Delphi's did?"

**The review tiers** (the user's decision of 2026-10-03, which replaces the Claude Opus reviewer every code PR had since 2026-10-02). Before a PR is reviewed, the main session sets its tier ([Appendix C](#appendix-c-the-run-task-skill) step 2 for a task or a fix, [§4.9](#49-plan-prs-two-tiers) for a plan PR): Jev classifies it with `scripts/jev/review-complexity.json`, and the main session confirms or overrides the answer, acting on it alone at a probability of 0.9 or above (operating-guide §3's Jev preference). The tier is set once per PR; a rework round's re-review keeps it, and may raise it, never lower it (a re-check of named fixes goes to a cheaper reviewer, below).

| Tier | Which PRs | Reviewed by |
| --- | --- | --- |
| **Simple** | A documentation PR that changes no task contract: evidence-only doc claims, and routine-tier bookkeeping ([§4.9](#49-plan-prs-two-tiers): the catalogue's stub, index row and graph edges to merged tasks, a "never in flight with" constraint, a merge-after on an already merged task) when it is reviewed at all. A routine-tier fold that adds Done-when lines, and an Owns amendment, change a task's contract and are **complex** | Luna alone (`external-review.ps1 -Reviewer auto`), the only tier Luna reviews (the user's decision of 2026-10-04: Luna reviews only small, simple PRs); when the OpenAI account is out of quota, GLM-5.3, then DeepSeek V4 Pro, then Qwen3.8 Max (below); a cold Claude Opus on any other exit 3 |
| **Complex** | Every task or fix PR, and every plan PR that adds or changes a task contract or edits a process document, unless it is very complex | GPT-6 Sol (`-Reviewer sol`, `openai/gpt-6-sol`) at `low` effort, or `medium` where below justifies it, never `high`, whoever implemented it unless OpenAI did. When Sol cannot review: its substitutes (below: GLM-5.3, then DeepSeek V4 Pro, then Qwen3.8 Max), then a cold Claude Opus unless Claude implemented, and escalation if it did. An OpenAI implementer: a cold Claude Opus directly |
| **Very complex** | A task or fix PR that meets a very-complex criterion below | A cold Claude Opus, unless Claude (Sonnet or Opus) implemented it: then Sol at `medium` **and** the OpenCode pair, three reviews; when Sol cannot review (the OpenAI account out of quota, or any other cause), Qwen3.8 Max stands in for Sol beside the pair: GLM-5.3, DeepSeek V4 Pro and Qwen3.8 Max, still three reviews, and escalation when fewer can run (the owner's decision of 2026-10-05, which replaces the two-review form of 2026-10-04) |

**The OpenCode pair** (the Luna pair until the user's decision of 2026-10-04, which took Luna off every review but the simple tier's) is two independent reviews from two model **families**, neither of them the implementer's nor OpenAI: GLM-5.3 (`-Reviewer glm`, `zai-coding-plan/glm-5.3`: GLM runs on the Z.AI Coding Plan since the user's decision of 2026-10-04, outside OpenCode Go) and DeepSeek V4 Pro (`-Reviewer deepseek-pro`, `opencode-go/deepseek-v4-pro`; V4.1 Flash, `-Reviewer deepseek`, stays a valid reviewer but is not the pair's, the user's decision of 2026-10-03). Both must approve. The pair's reviews are taken in the order GLM-5.3, DeepSeek V4 Pro, Qwen3.8 Max (`-Reviewer qwen`, `alibaba-token-plan/qwen3.8-max`), Claude Sonnet (a subagent on [Appendix B](#appendix-b-reviewer-prompt-template)), each skipped when it shares the implementer's family (the user's decision of 2026-10-05): when the implementer's family excludes one of the first two, or one exits 3, the next in that order takes its place; Sonnet only when Claude did not implement the PR, and when no second family is left the pair has failed. It is used on a very complex PR that Claude implemented, beside Sol, where its two reviews are always GLM-5.3 and DeepSeek V4 Pro. **A PR with two or three reviews** is labelled by the main session, not by a reviewer: each run goes without `-ApplyLabel` (a Claude reviewer's brief says not to label), and the main session applies `status:approved` only when every review approves, `status:rework` when any asks for rework, relaying every review in full ([§4.4](#44-rework)), and escalates on any `user decision`.

**The families** (the user's decision of 2026-10-03): OpenAI is `luna` and `sol`; GLM is `glm` and `glm-flash`; DeepSeek is `deepseek`, `deepseek-pro` and `deepseek-flash`; Qwen is `qwen` and `qwen-flash` (the user's decision of 2026-10-05); Claude is Sonnet and Opus; MiMo has no reviewer. A route is not a family: DeepSeek and GLM on the Alibaba Token Plan (`-Route alibaba`, below) keep their names and their families. What follows from them: an OpenAI implementer (`luna`) excludes Sol, so its complex and very complex reviews are both a cold Claude Opus; a Claude implementer (the Sonnet fallback, or Opus on an architecture task) is never reviewed by Opus or Sonnet, so its complex review is Sol and falls back to Sol's substitutes (below), and its very complex review is Sol plus the OpenCode pair, or, when Sol cannot review, the pair plus Qwen3.8 Max in Sol's place (three reviews either way). When no reviewer of a permitted family can run (a Claude implementer while OpenCode is unavailable), the main session escalates ([§4.5](#45-when-to-escalate-to-the-user)) rather than break the family rule. [Appendix C](#appendix-c-the-run-task-skill) step 2 spells out every path as commands.

**The criteria Jev scores** (the user's decisions of 2026-10-03: the scope of simple and complex, the very-complex triggers (a)–(g) with their size limits, and High effort alone not being one). A task or fix PR is never simple, and a plan PR is never very complex.
- **Simple**: the PR's `--stat` touches nothing under `src/`, `tests/`, `godot/`, `scripts/`, `data/`, `.github/` or `.opencode/`; it adds no task, changes no task's Owns, Scope or Done-when line, adds no dependency on an unmerged task (a merge-after on an already merged task is bookkeeping), and does not edit this document, `operating-guide.md` or `CLAUDE.md`. A routine-tier fold that adds Done-when lines is therefore complex.
- **Very complex**: at least one of (a) an architecture task (T02, T03, T16, T22) or an Ultrahigh effort; (b) a new subsystem: a new project, a new top-level folder under `src/IC2.Engine/` or `src/IC2.Data/`, or a new turn-pipeline phase; (c) a save-format or serialization change: the save reader or writer, the JSON round-trip, a schema version, or a field added to a serialized model type; (d) a widening of the shared domain model (`src/IC2.Engine/Model/`) that other tasks build on; (e) a change to a battle resolver's or the AI's outcomes under a seed that re-baselines existing seeded expectations (values in `tests/fixtures/corpus.json` or `expected-corpus-outcomes.json`, existing golden lines, a soak or a seeded measurement) rather than only adding new ones; (f) a correction task whose entry says its evidence is still open; (g) size: an Owns list of more than 15 files, or a `--stat` of more than 20 files or 1,500 changed lines outside `tests/fixtures/`. A High-effort entry alone is complex, not very complex: 59 of the catalogue's 120 entries are High, and counting them would bring Opus back to half the code PRs.
- **Complex**: everything else.

**Sol's effort and use** (the user's decision of 2026-10-03: "we use Sol sparingly, never with effort high, at most medium and even better light"). Sol runs at `low` effort (`external-review.ps1` gives `sol` the variant `low` by default; `-Effort` admits only `low` and `medium`, so `high` and above cannot be passed). It runs at `medium` only where it earns it: a very complex PR in which Sol takes part (one that Claude implemented), and a complex PR whose earlier low-effort Sol review missed something a later review found. Sol runs only where the tier names it: the first review of a complex PR, and the very complex Claude-implemented case. **A re-check of named fixes** (the one-line confirmation after `approve after named fixes`, or a rework re-review that only checks the named findings) goes to a cheaper reviewer of a permitted family, with the earlier reviews linked: Luna on a simple PR; on a complex or very complex PR, the first of GLM-5.3, DeepSeek V4 Pro and Qwen3.8 Flash (`-Reviewer qwen-flash`) that the implementer's family does not exclude (the user's decision of 2026-10-04, Qwen Flash added on 2026-10-05); the order is Luna, GLM, DeepSeek Pro, Qwen Flash; it goes back to Sol only when the fixes rewrote more than the named findings.

**When Sol cannot review** (the user's policy of 2026-10-03). Sol is the hard tiers' reviewer: wherever a complex or very complex review names Sol, a substitute takes it rather than the review waiting, unless the user says to wait. It supersedes (the user's decision of 2026-10-03), for the order of fallbacks, the "Sol, then a cold Claude Opus" of the user's first rule that day: Sol's substitutes come first, and a cold Claude Opus comes after them, only when Claude did not implement the PR; when Claude did, the main session escalates ([§4.5](#45-when-to-escalate-to-the-user)) and never falls back to a Claude reviewer.
1. **Diagnose.** When Sol (or Luna) exits 3, read the run's files, which the script keeps on a failure and names in its output, and the newest OpenCode log under `%USERPROFILE%\.local\share\ic2-opencode-1x\data\opencode\log\`. Read quota-tracker first (`curl -s localhost:8765/quota/openai?refresh`, [environment.md](environment.md)). Sol's `7d` window at 95% or more, or the text **`The usage limit has been reached`** (or OpenAI's `insufficient_quota`), means Sol is out of quota. Luna (GPT-5.6 Luna) has its own `gpt-5.6-luna:7d` window and stays usable while it is under 95%, so an answer from Luna proves nothing about Sol. Any other cause, or the user asking to avoid Sol's cost, means only Sol is unavailable, and Luna can still review a simple PR. A hard review's substitutes are the same either way; the diagnosis is recorded (step 4) and decides the simple tier's fallback.
2. **Substitutes, in order**, each skipped when it shares the implementer's family:

   | Situation | Hard review (complex, very complex) | Easy review (simple) |
   | --- | --- | --- |
   | Only Sol is unavailable | GLM-5.3 (`-Reviewer glm`), then DeepSeek V4 Pro (`-Reviewer deepseek-pro`, `opencode-go/deepseek-v4-pro`), then Qwen3.8 Max (`-Reviewer qwen`), with the reason in its header | Luna, as usual |
   | The OpenAI account is out of quota | GLM-5.3, then DeepSeek V4 Pro, then Qwen3.8 Max | GLM-5.3, then DeepSeek V4 Pro, then Qwen3.8 Max |
   | The implementer is GLM | DeepSeek V4 Pro, then Qwen3.8 Max | the same, when OpenAI is out of quota |
   | The implementer is DeepSeek (the default `deepseek-flash`) | GLM-5.3, then Qwen3.8 Max | the same, when OpenAI is out of quota |
   | The implementer is Qwen (`qwen-flash`) | GLM-5.3, then DeepSeek V4 Pro | the same, when OpenAI is out of quota |

   **Which substitute goes first** (the user's decision of 2026-10-08, CLAUDE.md rule 17): the table says *who* may substitute. The order among them is quota-tracker's `/recommend?tier=light` ranking, a skipped or negative-scoring one last, instead of the fixed order above. Since [T152](tasks/T152.md), `Choose-Model.ps1 -Role reviewer -ExcludeModel <implementer>` prints them in that order.

   Qwen3.8 Max was added after GLM-5.3 and DeepSeek V4 Pro by the user's decision of 2026-10-05. **Routes**: GLM-5.3 and DeepSeek V4 Pro (and V4.1 Flash) also run on the Alibaba Token Plan, the same model under the same name and family: `external-review.ps1 -Route auto` (the default) takes Z.AI or OpenCode Go unless quota-tracker's `/avoid` lists `zai` or `opencode_go`, then Alibaba (`alibaba-token-plan/glm-5.3`, `alibaba-token-plan/deepseek-v4-pro`); `-Route alibaba` forces it. The route is named in the posted review's signature line. Qwen runs only on Alibaba ([environment.md](environment.md)).

   Luna is a light reviewer and never takes Sol's place on a hard review (the user's decision of 2026-10-04: Luna reviews only small, simple PRs). On a very complex PR that Claude implemented, Sol runs first and the pair is dispatched only once Sol's outcome is known (its review posted, or an exit 3 diagnosed), so no review is posted before the count is settled. Sol posted: the pair follows, three reviews. Sol out, whether only Sol or the whole OpenAI account (a quota outage, which takes Luna out with Sol): Qwen3.8 Max, a third family, stands in for Sol, so GLM-5.3, DeepSeek V4 Pro and Qwen3.8 Max review, three reviews from three families, each skipping the implementer's (the owner's decision of 2026-10-05, which replaces the two-review form of 2026-10-03 and 2026-10-04). The PR escalates when fewer reviews than that can run.
3. **How a substitute runs.** Probe it first with `-WhatIf` (below). Reuse Sol's brief unchanged except that its header line names the substitute ("T<nn> review (GLM)"), one sentence says it reviews in Sol's place and why, and Sol's earlier reviews on the PR are linked so that it re-takes their attacks. A brief for GLM or DeepSeek is never shortened: it always carries the "Blocking means" section written for that PR and the "Report every blocking finding in this one review" section, both in full, whether the model reviews a plan PR, stands in for Sol, is a pair member or re-checks named fixes (the user's decision of 2026-10-04: GLM especially has struggled to report every blocking item). Run it; move to the next substitute only on exit 3 (exit 4 is read and decided, as always).
4. **Record it.** For a task PR, the main session commits a one-line note to the Reviewer field of `docs/tasks/T<nn>.md` on `main`, as a routine doc claim ("Docs: T<nn> reviewer"), with the date, the substitute and the reason; a fix or a plan PR records it in the tier comment. It also appends one entry to the wiki's [Model trials](https://github.com/diegoami/imperial_conquest_2/wiki/Model-trials) page (append-only, like Process incidents): the error, the probe results, the substitute, and what it caught or missed against Sol's earlier rounds.
5. **Probes.** The free probe is `pwsh scripts/external-review.ps1 -Pr <pr> -Reviewer <x> -ExcludeModel <implemented by> -BriefFile <brief> -WhatIf`: it runs the family check first (a reviewer of the implementer's family is refused with exit 1), then checks the CLI and the argument line, and bills nothing; it does not reach the provider, so a green probe says nothing about quota. When a quota diagnosis needs the provider, the one-prompt probe bills a little: with `XDG_DATA_HOME` set to `%USERPROFILE%\.local\share\ic2-opencode-1x\data` and stdin closed (operating-guide §3 says why), `$null | opencode run --model openai/gpt-5.6-luna "Reply with the word OK."`; an `OK` means Luna is served. Quota is read first and for free from quota-tracker ([environment.md](environment.md), CLAUDE.md rule 17): GPT-5.6 Luna has its own weekly window (`gpt-5.6-luna:7d`), so Luna answering no longer proves Sol has quota; Sol's is openai's `7d` window.
6. **Prevention.** One provider's quota must never block a hard PR's last review: before every complex or very complex review, not at session start (the user's decision of 2026-10-03) ([Appendix C](#appendix-c-the-run-task-skill) step 2c), the main session runs the `-WhatIf` probes of `sol`, `glm`, `deepseek-pro` and `qwen`, and a red probe is fixed, or recorded in the tier comment, before it is needed.

Unchanged by the tier: `/code-review --effort ultra` on the four architecture PRs and at rework round 2 ([§3.5](#35-where-the-code-review-skill-fits)), the user's own ultra review of T16 and T22, and the human visual review of the Godot screens ([§9](#9-standing-governance-decisions) Q-B). A machine's model order may swap a tier's Claude form for its OpenCode form when Claude credit is short (Opus for Sol plus the OpenCode pair), and never lowers a tier.

**The reviewer's model family is never the implementer's.** `scripts/external-review.ps1 -ExcludeModel <name>` (the name on `external-implement.ps1`'s `implemented by:` line, or a `model:<name>` label on the PR or issue) drops every reviewer of that family from the chain and refuses an explicit `-Reviewer` of it; when no model is left, the script exits 3 and the tier's fallback takes the review. The script has no Claude reviewer: `-ExcludeModel sonnet` or `opus` names the Claude family and excludes no OpenCode reviewer, so a Claude implementer's runs pass it like any other, and the main session never picks Sonnet or Opus to review that PR; `model:sonnet` and `model:opus` labels exclude nothing.

**Advisory reviews** (the owner's decision of 2026-10-06). The main session **may** add an advisory review on a small PR or a plan PR as a second opinion, from one of the free OpenRouter models: `-Reviewer nemotron` (the stronger), `north-mini` (coding-focused, faster), `inkling` or `laguna` ([environment.md](environment.md#the-free-openrouter-models-advisory-only)). It runs after, or beside, the tier's own reviews and never replaces one: it is never counted toward a tier, never labels (`-ApplyLabel` is refused), and is in no family, so it never stands in for a family's reviewer. Its comment is headed "<Task or Plan> review (<Name>, advisory — not counted)". A finding in it is weighed like any unreviewed contribution: the main session verifies a claimed defect before relaying it as a finding. The script skips it (exit 3) when the shared free allowance is at 50 requests or fewer, when quota-tracker does not answer, or on a 429 (never retried), kills it after 300 s idle or 1800 s in all (OpenCode retries a rate-limited call inside the run, and the script cannot stop that), and refuses a brief that names the private fixtures, `assets.local.ini`, `IC2_FIXTURES_DIR`, a `.dat` or `.sav` path, or anything key-like ([environment.md](environment.md#the-free-openrouter-models-advisory-only) lists the rules); nothing private ever goes into its brief.

### 3.5 Where the `/code-review` skill fits

The purpose-built reviewer agent is the **default gate**, not `/code-review`. Three of the five review checks are project-specific:
- tracing provenance to the reports;
- the `[designed]`-tag rule;
- the Owns-list scope check.

The reviewer must also re-run DoD commands locally, including Godot-headless runs and local-only fixtures that a cloud reviewer cannot reach.

> **The skill is not used from inside a reviewer agent.** Invoked there without an explicit target it forks; the fork runs in the main checkout rather than the reviewer's worktree, so `origin/main...HEAD` is empty and it falls back to `HEAD~1..HEAD`, reviewing whatever `main` merged last (incident 5). **Reviewers sweep the diff inline instead**, and [Appendix B](#appendix-b-reviewer-prompt-template)'s gate 0 makes the target verifiable. If the skill is invoked at all, it gets the PR number as an explicit target, and its output is discarded unless every finding names a file from that PR's diff.

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

Before the gates, **gate 0: the reviewer proves it is looking at the right code** — its HEAD equals the PR's head, and its `origin/main...HEAD` file list (`origin/release/0.4...HEAD` for a PR against a maintenance line, [release-plan.md §2.2.2](release-plan.md#222-two-release-lines-a-maintenance-branch-per-patched-minor)) equals the PR's own file list, with both pasted into the review ([Appendix B](#appendix-b-reviewer-prompt-template); incident 5). An empty diff means the wrong tree. Every finding must name a file from that diff; anything else is a separate report.

Five gates, in order. Any failure means `status:rework`. **What makes a finding blocking** is spelled out in every review brief, in a "Blocking means" section written for that task ([Appendix B](#appendix-b-reviewer-prompt-template); harness lesson L47, adopted by the user's decision of 2026-10-04). The rule it encodes: **a proven way past what the task protects is blocking**, never "follow-up hardening". The boundary of what a task protects is set before review, in its entry's `Protects:` line (which may name what it explicitly does not cover); a review cannot reclassify a proven bypass inside that boundary. In the harness's PR 29 replay a reviewer proved three bypasses of the guard under review, rated them all non-blocking and approved, while two other reviewers asked for rework on the same head. Every brief, a plan PR's included, also carries the section "Report every blocking finding in this one review", pasted in full right after "Blocking means" ([Appendix B](#appendix-b-reviewer-prompt-template); the user's decision of 2026-10-04, after a reviewer in ic2-conquest PR #38 asked for rework eight times, one blocking finding per round): one review lists every blocking finding, numbered R1, R2, …, and says "Final pass done" just before its closing verdict.

1. **DoD, independently reproduced.** The reviewer runs the commands itself at the PR head. The PR body's evidence is a convenience, never the proof. A DoD line with no runnable check is itself a finding.
2. **Provenance.** Every constant traces to a `tests/fixtures` entry, a cited report, or a `docs/investigations/` document. Any `[designed]` value must say *what was searched and came up empty* (`design-audit.md` §4.5).
3. **Determinism.** Gameplay paths use no `System.Random`, no wall clock, no `Guid.NewGuid`, and no order-dependent iteration. Every random draw goes through `IRng`, and a seeded test proves reproducibility.
4. **Scope.** Every changed file is inside the task's Owns list. A change outside it is a finding even if it's a good change. The PR's "Docs affected" list is plausible for what the diff does.
   **Owned implicitly** (the user's decision of 2026-09-28): the tests of files in the task's Owns list, the fixtures and goldens those tests read, and the `<Compile Include>` lines that register them. When a reviewer's **only** gate-4 finding is such a file, the main session widens Owns by a comment on the task's issue, and the reviewer proceeds. That needs no plan PR. Any change to a DoD line, the Scope or a dependency still goes through a plan PR ([§4.3](#43-the-dod-is-not-negotiable-by-an-agent)).
5. **Correctness sweep**, in the reviewer's own context ([§3.5](#35-where-the-code-review-skill-fits) says why the skill is not used here). It is a read of the PR's own diff, hunk by hunk, plus the surrounding code the diff doesn't show, hunting the classes that have actually bitten this project:
   - integer truncation and operation order (the original truncates at every step);
   - off-by-one in a cap or threshold, and the boundary either side of it;
   - a division or modulo whose denominator can be zero;
   - an unguarded null, empty collection or missing id;
   - order-dependent iteration, or a dictionary where order would leak into a result;
   - a branch that can never be taken (incident 6);
   - **a delete that leaves something behind** — three questions, every time an entity is removed from `GameState`: does anything still **reference** it, are its **resources** conserved, and does a **cap** still hold afterwards? The symptom, every time this class has struck (incident 7), is a dangling id that `GameDataValidation.ValidateState` rejects, so the game writes a save it **cannot reload** — and the code paths in between degrade silently, which is why nothing surfaces until the load. `FleetState.CarriedArmyId` is the model's one cross-reference to an army id and the usual culprit; the reviewer's sweep is the whole model, not just that field;
   - **a test that would still pass if the behaviour were deleted** — the most common finding here, and the reason mutation is the proof below.
   - **a comment that asserts behaviour at an edge no test visits.** In this codebase a comment is load-bearing: it is how a `[designed]` value justifies itself to a reviewer who cannot check it against a report, which makes an **unverified comment the same defect class as an unverified constant**. A false comment about an edge no fixture reaches fails no test (incident 8). The rule: *a comment asserting behaviour at an edge arrives with the test that visits that edge, or it is not written.* Cheap at the keyboard. **A comment is covered only if a test visits every path that reaches it**: a mutation killed on one path does not cover a comment that a second path also reaches (incident 8).

   A candidate is **proved before it is reported**: run it, or delete the behaviour and watch exactly which test fails. A finding with neither is labelled as unverified.

   **A mutation result is only admissible after `touch` and an explicit clean rebuild.** `--no-build` and incremental builds are **never** admissible after a mutation cycle. Restoring a mutated file — `mv file.bak file`, `git checkout --`, a `cp` from a copy — can give the restored source an **mtime older than the DLL built from the mutated version**, so MSBuild's up-to-date check skips the rebuild and the next run tests the **stale assembly**. A clean rebuild of this solution costs about a second; there is no cost argument against mandating it.

   **`git status` clean and `grep` showing the correct source are not evidence the binary matches** (incident 9).

   **A mutation helper lives in the worktree it mutates, or takes that worktree as a required argument with no default.** A shared script whose target path points at another worktree mutates one checkout and tests another, and produces the same phantom green as a stale binary (incident 9).

   **The dangerous direction is the quiet one.** A stale *mutated* binary produces a phantom **red** — alarming, and it announces itself. A stale *clean* binary produces a phantom **green**, which gets written into a PR as *“mutation M-n: no test caught this”* — a **false finding**, either an invented coverage gap or a real gap declared harmless and never closed. It is silent, it is durable, and a reviewer reading the results table has no way to tell a genuine negative from a stale one. **A negative mutation result — a claim that nothing failed — therefore carries the same burden as a positive one, and is the entry a reviewer should re-take rather than read** (incident 9).

   **Fanning out is allowed, and is how the sweep scales**: the reviewer may dispatch one verification agent per candidate, each given the explicit claim, the file and line, and what evidence would confirm or refute it — never left to infer a target from its working directory ([§7](#7-concurrency-single-instance-and-local-only)). Verdicts come back confirmed, plausible or refuted, and "plausible" is reported as plausible.

A defect the reviewer finds in **another task's already-merged** code is not a finding against this PR. It goes to the bug list ([§4.6](#46-bugs-and-follow-ups)).

### 4.3 The DoD is not negotiable by an agent

An implementer that can't satisfy a DoD line **stops and reports**. It never edits the DoD, never weakens an assertion to a range, never marks a test `Skip`, and never deletes a failing assertion. A DoD line changes only by a commit to the task's entry, `docs/tasks/T<nn>.md`, after a decision by the user. One exception is Owns widening for implicitly owned files (tests, their fixtures and goldens, and their `<Compile Include>` lines), which the main session records by a comment on the issue ([§4.2](#42-what-the-reviewer-checks) gate 4). The others are a fold at the routine tier of [§4.9](#49-plan-prs-two-tiers), which adds Done-when lines to a task at `status:blocked`, and a contract-tier plan PR merged on a cross-session review's approval ([§4.9](#49-plan-prs-two-tiers)); the user's standing decision is the tier. DoD, Scope and dependency changes still go through a plan PR. The reviewer treats any diff from a task branch to `task-catalogue.md`, to any file under `docs/tasks/` (the task's own entry included), or to this document as an automatic `status:rework`.

### 4.4 Rework

When the reviewer asks for changes, the main session:
1. records the round on the issue (`review-round:1`, then `review-round:2`);
2. sends the implementer the **full findings, linked or verbatim, never a hand-picked subset**. It uses `SendMessage` if the implementer is still reachable. Otherwise it spawns a fresh implementer with the PR, the review comment URL and the task entry; the branch holds the earlier work.

The implementer pushes to the same branch, and the main session dispatches the reviewer again.

**A brief never widens Owns** (the user's decision of 2026-10-08, from #854). When a finding can be fixed only in a file, or a part of a file, outside both the task's Owns and the implicit set that [§4.2](#42-what-the-reviewer-checks) gate 4 and [§4.3](#43-the-dod-is-not-negotiable-by-an-agent) let the main session widen by an issue comment (tests, fixtures, goldens, compile registrations), the main session first opens an Owns amendment ([§4.9](#49-plan-prs-two-tiers): routine when no other open task names the path, contract otherwise) and dispatches the rework only after it merges. The brief cites the merged amendment. On T140 a round-1 brief authorised an edit to `MapClickCheck.cs` without one. The review had it reverted, and the same check failed two rounds later (plan PR #855).

**One blocker per round** (the user's decision of 2026-10-04): when a reviewer names exactly one blocking finding in each of two rounds running, the main session stops after the second, goes through the whole diff itself for the same class of problem, and records the pattern on the wiki's [Model trials](https://github.com/diegoami/imperial_conquest_2/wiki/Model-trials) page ([Appendix C](#appendix-c-the-run-task-skill) step 3). What the sweep finds goes where that review's findings go: into the next rework; at `review-round:2`, into the escalation comment; on a `fix` at `review-round:1`, into the correction task's entry.

**Rework round 2 is the last.** A review that fails while the issue carries `review-round:2` escalates ([§4.5](#45-when-to-escalate-to-the-user)). For a `fix` ([§4.10](#410-the-fix-lane)), round 1 is the last: a review that fails while the issue carries `review-round:1` files a correction task instead of escalating.

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
3. **Plan.** The main session triages it. A new bug arrives at triage with Jev's proposed `fix`-versus-task label and playability answer ([operating-guide.md §3](operating-guide.md#3-standing-user-preferences)), which the main session confirms or overrides. It picks one of:
   - a **fix** ([§4.10](#410-the-fix-lane)): when the fix stays within the files the bug names and changes no rule's outcome. No catalogue entry, no `T` number; the bug issue is the contract;
   - a **correction task**: the next free `T` number, in full catalogue shape, when the fix needs its own Owns list, model and reviewer;
   - **folding it into an upcoming task's DoD**: the default for small fixes and for follow-ups, folded into the next task that touches those files. **A task at `status:ready` gains no new DoD items** (the user's decision of 2026-09-28): a later finding folds into a follow-on task, or a new one, never into a ready task's entry;
   - **deferring it**: close it as *not planned* with the reason. A bug that blocks a task is deferred only on the user's decision.

   Blocking is recorded in the blocked task's entry (`docs/tasks/T<nn>.md`, with its index row and graph edge in [task-catalogue.md](task-catalogue.md)): either as a `merge-after` dependency on the correction, or as a DoD line in the blocked task. After that, the normal ready check enforces it. Catalogue changes go to a plan PR, and [§4.9](#49-plan-prs-two-tiers) says which tier merges it. When triage is done, the main session removes `triage:needed` and comments where the item went.
4. **Resume.** The suspended task rebases onto the merged correction and continues.

**Follow-ups.** A reviewer may approve with findings that fail no gate. At merge, the main session collects them into one `T<nn> follow-up` issue labelled `triage:needed`, which links the review comments. A follow-up is not a bug, and no task is suspended for it. It is triaged the same way, usually folded into the next task that touches the files concerned.

**The queue** is `gh issue list --label triage:needed --state open`. It is checked at the start of every session, and before dispatching any task that an item names.

### 4.7 After a merge

In the same turn as the merge, the main session:

1. **Unblocks.** Every `status:blocked` task whose merge-after dependencies are now all merged, and which isn't suspended on an open bug, becomes `status:ready`. **Until v0.5.0 is tagged, the next task is the next ready task of the v0.5.0 gate**, once its labels have moved ([release-plan.md §2](release-plan.md#2-the-release-ladder), "The labels move first"), taken before any other task, on whichever machine is running ([§8](#8-two-machines), "Which task next").
2. **Files the follow-up** ([§4.6](#46-bugs-and-follow-ups)), if the review had non-blocking findings, and proposes where each item folds.
3. **Makes any Owns widening durable.** A widening recorded by an issue comment ([§4.2](#42-what-the-reviewer-checks) gate 4) is added to the task's entry, `docs/tasks/T<nn>.md`, in this step's `Docs:` commit, so a later disjointness check ([§8](#8-two-machines)) reads the entry, not a comment.
   **Records the PR's "Docs affected" list**, and applies only what would otherwise leave a document **factually wrong**: a formula the code now implements differently, an `[open]` item the merge closed, a mis-attributed citation. Those go straight to `main` in a small `Docs:` commit, because a wrong provenance claim is what the review gates exist to catch. **Everything else waits for the release docs pass** ([release-plan.md §5](release-plan.md#5-release-checklist)): re-wording, counts, narrative and anything about where the build stands. The living pages are in the [wiki](https://github.com/diegoami/imperial_conquest_2/wiki), where they carry no contractual force (incident 10).
4. **Leaves the agents' worktrees in place.** A worktree holds a run's diagnostics and any work it did not push, so nothing removes one at a merge or after a failure (the user's decision of 2026-10-08). They are removed on a schedule instead, by the main session, once they are safe to remove (merged or closed, nothing unpushed, nothing uncommitted).
5. **Reports to the user**: the merge commit, what the review found, the follow-ups filed, and what is ready next.

### 4.8 The playability gate, until v0.5.0

Adopted 2026-09-28 by the user's decision, to reach v0.4.0, the playable Godot UI, and extended to v0.5.0 by the user's decisions of 2026-10-03 (v0.4.0 was tagged on 2026-09-28, and the label was renamed from `post-v0.4.0` to `post-v0.5.0`). **Redefined on 2026-10-04**, when the user, after playing v0.4.1, made v0.5.0 the playable release: *"go with A, make 0.5.0 playable and move battles to 0.6.0"* ([release-plan.md §2](release-plan.md#2-the-release-ladder)). The gate ends at the v0.5.0 tag. Until then, it sorts every bug and follow-up into two classes.

**`release:v0.5.0`: it breaks play, or it blocks a normal game.** Such an item becomes a correction task, a fold or a fix ([§4.6](#46-bugs-and-follow-ups), [§4.10](#410-the-fix-lane)) and is labelled `release:v0.5.0`, so the v0.5.0 gate waits for it. It **breaks play** when it is a crash, an AI stall, a save that will not load, or an order that can never succeed. It **blocks a normal game** when it fails one of these three tests:

1. **An order the original has cannot be issued, or does nothing.** An order of the original's menus, toolbar, dialogs or map clicks (the UI command audit's rows, [`original-ui-command-audit.md`](investigations/original-ui-command-audit.md); `GameCommandTable`'s rows) has no working path in the Godot app, or its path is refused, ignored or clamped to no effect in a state where the original carries it out. A control that offers a choice the original does not, so that the order fails, counts (bug [#697](https://github.com/diegoami/imperial_conquest_2/issues/697)'s slider, which lists armies too far away to buy). *Not* this test: an order that works but looks different (layout, wording, step size, a missing keyboard shortcut); a gap in the CLI only; the three seat commands T100 left out.
2. **Feedback is missing or wrong, so that play is guesswork.** After an order, an end of turn or an AI phase, a player who uses only the Godot app cannot tell from the screen whether the order took effect, why it was refused, or what changed. Examples: a refusal shown nowhere; a figure on a panel that is wrong for the rule the engine applies (an army's supply, purse, troops or moves, a city's stock or loyalty, the treasury); a battle, capture, defection, elimination, deposition or the game's end that happens with no report on screen; one of the original's *End turn* warnings missing. *Not* this test: a difference of wording or format, a missing flavour text, the news log's layout, and a fact the original also hides.
3. **A rule makes a normal game unwinnable, or makes a choice in it pointless.** Under either shipped preset, a human seat that plays sensibly cannot reach the scenario's victory condition, or loses, or is stopped, for a reason the original does not have: an order refused where the original allows it (T115's treasury, which the original lets run into debt), a resource that cannot be replenished, an AI seat that never acts or never ends. *Not* this test: a constant or formula that differs from the original's but whose effect stays inside normal play (a casualty a few percent off, a price off by rounding). Such a difference stays `post-v0.5.0` unless the user decides otherwise.

**A normal game** is a human seat, or hotseat seats, in a shipped scenario under either shipped preset, played through the Godot app's menus, toolbars, map clicks and dialogs, from New Game or a Load, to the scenario's victory condition or the seat's fall. The CLI, an imported original save, a custom world and a soak run are not normal games for this test.

**`post-v0.5.0`: everything else.** It keeps its issue open, loses `triage:needed`, and gains the label `post-v0.5.0`. Evidence findings outside the first class are recorded in the research repository, with an issue here labelled `post-v0.5.0` that points at the report. No catalogue entry is written for them until the v0.5.0 tag, except where the user decides otherwise (as for T114–T119 at the triage of 2026-10-03). After the tag, the `post-v0.5.0` issues are triaged under [§4.6](#46-bugs-and-follow-ups) as usual.

**How an item is sorted.** At triage, the main session asks Jev the playability question on the issue's title and body ([operating-guide.md §3](operating-guide.md#3-standing-user-preferences)), applies the three tests itself, and confirms or overrides Jev's answer. It labels a clear case. It brings a borderline case to the user as a question, one per issue, with the test it half-meets. An item the user calls a blocker is `release:v0.5.0` whatever the tests say.

**The triage pass, once.** After the plan PR that adopted this redefinition merges, the main session runs one pass over **every** open issue labelled `post-v0.5.0`, sorting each as above. An issue that moves gets `release:v0.5.0`, loses `post-v0.5.0`, gets one comment naming the test it fails and this section, and is then triaged under §4.6 into a fix, a fold or a correction task. An issue that stays gets no comment. The borderline issues go to the user together, at the end of the pass, each as its own question. **The v0.5.0 cut waits for every answer**: no `v0.5.0` tag is cut while a borderline question from the pass, or from any later triage under this section, is unanswered, so no issue that might block a normal game is left unclassified at the tag ([release-plan.md §5](release-plan.md#5-release-checklist)). The pass's result lives on the issues' labels and comments; no document records it ([§5](#5-status-lives-on-github)). The research session's gap check (the original against the clone, outside the battle) files its findings as issues, and each is sorted the same way when it arrives.

Until v0.5.0, the fix lane ([§4.10](#410-the-fix-lane)) takes only bugs in the first class, and a fix never goes ahead of a ready v0.5.0 gate task unless it is in the gate itself ([§8](#8-two-machines)). **An item the user puts on a v0.4.x patch list bypasses this gate** (the user's decision of 2026-10-04): it becomes a task or a fix at once, labelled with its patch's `release:v0.4.x` label, and ships in that patch ([release-plan.md §2.2.1](release-plan.md#221-v04x-patches)).

### 4.9 Plan PRs: two tiers

A plan PR is any PR that changes `docs/tasks/**`, `task-catalogue.md`, `release-plan.md` or a process document (this one, `operating-guide.md`, `CLAUDE.md`). A PR that is neither a task's nor a fix's (a workflow, a script, a tooling change) is merged as a contract-tier plan PR. Two tiers, decided by the user on 2026-09-28. The tier decides who merges, wherever the PR was opened ([§8](#8-two-machines)).

- **Routine tier: the main session merges it after green CI and reports it in its next message to the user.** A plan PR is routine when **every** change in it is one of:
  - a fold of a bug or follow-up into a task at `status:blocked` ([§4.6](#46-bugs-and-follow-ups) step 3) that adds Done-when lines, and Owns paths no other open task's entry names, and changes no existing Scope, Done-when or dependency line;
  - a "never in flight with" constraint;
  - an Owns amendment that adds only paths no other open task's entry names;
  - a merge-after dependency on a task that is already merged;
  - bookkeeping: the catalogue's stub, the index row, and graph edges to tasks already merged. A graph edge to an unmerged task is a dependency, and contract.

  The title starts `Plan (routine):`. A fold adds Done-when lines, so a DoD changes here without a fresh decision by the user ([§4.3](#43-the-dod-is-not-negotiable-by-an-agent)): the decision is this tier, made once. A routine-tier PR is merged by the session that opened it, on either machine.
- **Contract tier: a cross-session review merges it.** Everything else: a new task; a change to an existing Done-when line's assertion, or its removal; a Scope change; a merge-after dependency on an unmerged task; a change to `release-plan.md`'s gates; any edit to this document, `operating-guide.md` or `CLAUDE.md`; and any routine-tier change the main session is unsure about. The title starts `Plan:`.

  **How a contract-tier PR merges** (the user's decision of 2026-09-28). The session that opened it never reviews it. A different session reviews it, by its review tier ([§3.4](#34-why-the-reviewers-model-differs-from-the-implementers), the user's decision of 2026-10-03): a contract-tier plan PR is **complex**, so GPT-6 Sol through `scripts/external-review.ps1 -Reviewer sol` at `low` effort (`medium` only where §3.4 justifies it, never `high`), then, when Sol cannot review, GLM-5.3, then DeepSeek V4 Pro, then Qwen3.8 Max (`-Reviewer qwen`, added on 2026-10-05), each skipped when it shares the plan PR author's family, never Luna (the user's decisions of 2026-10-04); an evidence-only doc-claim PR, and routine-tier bookkeeping, when reviewed at all, are **simple**, so Luna alone (`-Reviewer auto`); a routine-tier fold that adds Done-when lines, or an Owns amendment, changes a task's contract and is **complex** when it is reviewed (the review tier says who reviews; it does not take away the routine tier's merge on the opener's authority). Jev proposes the tier and the main session confirms it. When none of Sol, GLM-5.3, DeepSeek V4 Pro and Qwen3.8 Max can run (each exits 3 or is skipped for the author's family or for quota, or OpenCode is suspended for reviews after two failures of the same cause, operating-guide §3), a cold Claude Opus reviewer that the main session dispatches in its own worktree counts as the cross-session review, and so does another main session's review ([§8](#8-two-machines)), which now takes the Opus fallback's place rather than coming first, but only when Claude did not write the plan PR: both are Claude reviews, and the family rule ([§3.4](#34-why-the-reviewers-model-differs-from-the-implementers)) excludes them. A plan PR that Claude wrote (a main session or a Claude subagent) then waits as a `user decision`, escalated ([§4.5](#45-when-to-escalate-to-the-user)) with the failures recorded; any plan PR waits so when no permitted reviewer can run (the user's decision of 2026-10-04). The main session records every fallback in the PR comment. The reviewer posts one comment whose second line is the verdict. On **approve**, the opener merges after green CI. On **approve after named fixes**, the opener applies them, replies with one comment mapping each finding to its change, and the reviewer answers that reply with one line confirming them; then the opener merges. If a fix is missing or wrong, the reviewer names the unresolved finding and the opener gets one more round; a second miss makes the verdict `user decision`. The user reads the merge in the opener's next report, and a revert is one contract-tier PR. **The user merges only when the verdict says `user decision`**: a design question, a release gate, a `[designed]` value, a change to Q-A to Q-G, anything [§4.5](#45-when-to-escalate-to-the-user) escalates, or something the reviewer cannot verify. A reviewer that would need the user for part of a PR says so in the verdict, and the whole PR waits.

A routine PR the user later disagrees with is reverted by a contract-tier PR. That is the tier's cost, and cheaper than the wait it replaces (incident 12).

### 4.10 The fix lane

A bug qualifies for the fix lane when its fix **stays within the files the bug names and changes no rule's outcome**: it corrects a message, a guard, a rejection, a parser, a view, a validation, a test, a save path or a data file's provenance. A change that alters what a rule computes, a gameplay constant, a resolver's result or an AI decision is a correction task under [§4.6](#46-bugs-and-follow-ups) step 3, however small. The main session decides at triage and labels the bug `fix` and `status:ready`. The test the triager applies from the bug, and the reviewer checks from the diff: **a fix changes no ruleset key, no `tests/fixtures/corpus.json` value, no seeded measurement, and no CLI golden line outside the bug's own reproduction.** If it would, it is a correction task; the list above is examples, not the test. Until v0.5.0 the lane takes only bugs in [§4.8](#48-the-playability-gate-until-v050)'s class.

A fix:
- **has no catalogue entry and no `T` number.** The bug issue is the contract. Its Owns is the files the bug names, read from the issue body, plus gate 4's implicit set ([§4.2](#42-what-the-reviewer-checks)); once the PR exists, its file list is the authority for [§8](#8-two-machines)'s disjointness check; its DoD is the bug's reproduction turned into a test that fails before the change and passes after it, plus a green `dotnet build IC2.sln` and `dotnet test IC2.sln`.
- **runs through the same labels as a task** (`status:in-progress`, `in-review`, `approved` or `rework`, `merged`), the same `machine:*` claim, and `local-only` or `single-instance` where they apply. It is the machine's one task while it runs ([§7](#7-concurrency-single-instance-and-local-only), [§8](#8-two-machines)), and its files must be disjoint from every task in flight.
- **is dispatched by `/run-task #<issue>`** ([Appendix C](#appendix-c-the-run-task-skill)): implementer the default OpenCode chain (`deepseek-flash`, then `qwen-flash`, then Claude Sonnet, [§3.3](#33-model-selection)) in worktree `ic2-work\fix-<issue>` on branch `fix/<issue>-<slug>`, with the bug body in place of the task entry in Appendix A's brief, commit subject `fix <issue>: <subject>` (no `#`, so the squash closes nothing early), PR body `Closes #<issue>` as Appendix A already allows, reviewer by the PR's review tier ([§3.4](#34-why-the-reviewers-model-differs-from-the-implementers): complex at least, so GPT-6 Sol by default) at gates 0, 1, 3 and 4 plus a read of the diff. Gate 2 reduces to confirming no constant changed; the mutation protocol does not apply.
- **on a maintenance line** (a bug labelled for a `v0.4.x`, [release-plan.md §2.2.2](release-plan.md#222-two-release-lines-a-maintenance-branch-per-patched-minor)) branches from `origin/release/0.4`, targets it with `--base release/0.4`, says `Refs #<issue>` in place of `Closes`, and is then ported forward to `main` by a cherry-pick PR that closes the issue. A v0.4.x task runs the same way. [Appendix C](#appendix-c-the-run-task-skill) lists the overrides.
- **gets one rework round** ([§4.4](#44-rework)). A review that fails while the issue carries `review-round:1` turns it into a correction task: the main session files the task at the contract tier, keeps the branch, and stops.

Why the lane exists: incident 12.

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
| Issue body | **A pointer, not a copy**: a link to the task's entry, `docs/tasks/T<nn>.md`, its branch, and the bugs it closes. The entry is the contract, so nothing needs to be kept in step. |
| GitHub milestone | One per phase: `Phase 0 Foundation`, `Phase 1 Pure rules`, `Phase 2 Systems`, `Phase 3 Delivery` |
| Labels | `task`; `bug`; `phase:0..3`; `lane:engine\|data\|ui\|infra`; `status:*`; `review-round:1\|2`; `triage:needed`; `model:*`; `effort:*`; `release:*`; `local-only`; `single-instance`; `needs-human` |

Retired labels from the orchestrator era: `docs:pending`, `orchestrator:pause`, `triage:scheduled`, `triage:deferred`, `blocking`. They stay on old issues as history and are no longer applied (incident 13).

---

## 7. Concurrency, single-instance, and local-only

- **One task in flight at a time per machine**: its implementer, its reviewer, and its rework, one after another. With two machines, two tasks may be in flight at once under §8's conditions.
- **A subagent cannot be pointed at a worktree.** Every agent starts in the session's working
  directory, the main checkout; an agent works in its worktree only because its brief tells it to
  use `git -C <worktree>` or to `cd` there first. That shell directory is **not inherited** by
  anything it spawns, so a skill or agent it forks starts back in the main checkout, where
  `origin/main...HEAD` is empty. That is incident 5 ([§3.5](#35-where-the-code-review-skill-fits)). Two consequences:
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
  - For an OpenCode implementer, `scripts/external-implement.ps1` creates the worktree and branch before the run. A Claude implementer runs `git -C C:\Users\diego\projects\imperial_conquest_2 worktree add C:\Users\diego\projects\ic2-work\T<nn> task/T<nn>-<slug>`, creating the branch from `origin/main` if it doesn't exist yet.
  - A reviewer checks out the PR head detached, in `...\ic2-work\T<nn>-review`.
  - An implementer detaches its worktree (`git checkout --detach`) before it finishes, so the branch is free for the next checkout.
- **`local-only` tasks** need the user's original DAT and saves via `assets.local.ini`. The file is git-ignored, so the agent copies it from the main checkout into its worktree's root. The tasks that launch Godot need a Godot install. These tests **skip explicitly** when the prerequisite is absent, so CI on GitHub's runners stays green.

  For `IC2.Data.Tests` that is not the whole picture: since T53, **CI fetches the fixtures** from the private `ic2-test-fixtures` repository and runs those tests for real, while a **worktree** without `assets.local.ini` still skips them, because the file is per-checkout. A brief never quotes a skip count (incident 11). The Godot checks no longer skip: since 2026-09-28 the `godot-headless` job runs the T24 and T25 check scenes in CI, and a check scene a Godot task adds gets its own step there (a one-line follow-up once the task merges, since `.github/workflows/ci.yml` is outside a task's Owns). Only the screenshot tours skip in CI, since the headless driver cannot capture.
- **`single-instance` tasks** may not run two at once on one machine: the ones that launch or export Godot 4.7.2, and any whose entry says why. The Godot tasks form one serial chain, ordered by their merge-after and never-in-flight lines.
- **Two tasks that both write `tests/fixtures/**`** (corpus top-ups) never run back to back without a rebase.
- **Tasks that redefine engine seams** (T03, T40) run with nothing else in flight.

---

## 8. Two machines

Two computers can work on the build at once. Each runs **its own main session**, with its own clone,
its own `ic2-work\` worktrees and its own local skill installs. GitHub is the only thing they share:
issues, labels, PRs and CI. Adopted 2026-09-27 by the user's decision. A **cloud session** (Claude Code
on the web, with its own clone and no Godot or original files) is a third main session: it triages
nothing, claims no task, opens its plan PRs on `plan/<name>` branches, and merges only its own PRs
under [§4.9](#49-plan-prs-two-tiers).

**Identity.** Each machine has a short name, set once in its environment as `IC2_MACHINE` (for example
`desktop` or `laptop`), and a matching `machine:<name>` label. Both machines authenticate as
`diegoami`, so a GitHub assignee cannot tell them apart; the label is the claim.

**Claiming a task.** Before dispatching an implementer, the main session:
1. checks that the issue carries **no** `machine:*` label;
2. adds `machine:$IC2_MACHINE`;
3. re-reads the labels. If another machine's label is also there, the machine that claimed second
   removes its label and picks something else.

The claim stays on the issue after the merge as a record of where the task ran. A task left in
flight is resumed only by the machine that claimed it, unless the user moves it.

**Capability.** Existing labels say what a task needs:
- `local-only`: the original DAT and saves through `assets.local.ini`, including any task that
  re-runs `scripts/export-classical-world.cs`;
- `single-instance`: a Godot 4.7.2 install, and never two Godot processes on one machine.

A machine claims only tasks whose labels it can satisfy. A task that turns out to need the original
files or Godot without carrying the label gets the label added, and the claim moves to a machine
that can run it.

**What may run at once.** Each machine runs **one task at a time** (§7). Two tasks on two machines may
be in flight together only when:
- their Owns lists are disjoint, counting the §2.3 shared files (the CLI goldens, the
  `scenario-authoring.md` field reference) as overlapping if both might touch them. Each task's Owns
  includes its implicitly owned files ([§4.2](#42-what-the-reviewer-checks) gate 4: the tests of its
  Owns files, their fixtures and goldens, and their `<Compile Include>` lines) and any widening recorded
  on its issue;
- neither task's entry says it is "never in flight with" the other;
- neither redefines an engine seam (§7).

When both PRs touch a shared file anyway, the second to merge brings `main` in and re-runs CI. It never
force-pushes.

**Which task next, until v0.5.0** (the user's decision of 2026-10-05, *"Gate first"*; it replaces the UI chain, T24, then T25, then T27, which ran first until v0.4.0 by the user's decisions of 2026-09-28 and 2026-10-03). The rule takes effect once the main session has applied the label moves the plan PR of 2026-10-04 lists under "After the merge" ([release-plan.md §2](release-plan.md#2-the-release-ladder), "The labels move first"); that is the first step after that PR merges. From then on, when a main session looks for its next task, it takes the next ready task or fix labelled `release:v0.5.0` before any other task, on whichever machine is running. Two kinds of item may run alongside the gate, never displacing a gate task that is ready and disjoint: a v0.4.x patch item (a task or fix labelled `release:v0.4.x`, such as T133 and T134, and whatever the user lists after playing a v0.4.x), on the `release/0.4` line ([release-plan.md §2.2.2](release-plan.md#222-two-release-lines-a-maintenance-branch-per-patched-minor)); and a task the user names on its issue. Every other task waits until the tag, T131 included unless the user lists it. No machine is bound to a lane. `single-instance` still means only one Godot task is in flight, so a second machine that comes online while a Godot gate task runs takes the next ready gate task that is not `single-instance`, or a v0.4.x item. A `fix` labelled `release:v0.5.0` counts as a gate task here.

**Who does what.**
- **Each machine merges only the PRs of tasks it claimed**, after its own review and green CI (§4),
  and runs §4.7's after-merge steps for them.
- **Triage and planning stay with one machine**, named in the operating guide as the primary. Its
  session triages `triage:needed` and opens plan PRs. The other machine files bugs and follow-ups
  with `triage:needed` and leaves them. That keeps plan PRs from colliding in the catalogue.
  **One exception (the user's decision of 2026-09-28, after plan PR #460):** a machine may open a
  plan PR that edits **only** `docs/tasks/T<nn>.md` for a task it has claimed, for example to settle
  a contract finding its own review raised. Anything else goes through the primary: other tasks'
  entries, new tasks, the catalogue index, the process documents and triage. Whoever opened it,
  [§4.9](#49-plan-prs-two-tiers)'s tier decides who reviews and merges a plan PR: the session that opened a
  routine-tier PR merges it; a contract-tier PR merges on another session's approval, and waits for the
  user only when that review says so.
- **CI is the authority on test results.** A reviewer's local run adds the `local-only` subset that CI
  cannot run.

**Setting up a machine.**
1. Clone this repository. Install the .NET 10 SDK, `gh` (logged in as `diegoami`) and `jq`.
2. Set `IC2_MACHINE`, and create its `machine:<name>` label if it is new.
3. Install the local skills verbatim from their fenced blocks ([operating-guide.md §2.3](operating-guide.md#23-the-two-skills)).
   Skills load at session start, so start the session after installing them.
4. Only for `local-only` tasks: the original game files, and `assets.local.ini` copied from
   `assets.example.ini`.
5. Only for `single-instance` tasks: Godot 4.7.2 mono ([operating-guide.md §1.3](operating-guide.md#13-local-toolchain-outside-both-repositories)).
6. Only for research work: a checkout of the research repository and the ReTools toolchain.

The agent briefs in Appendices A and B name this machine's paths
(`C:\Users\diego\projects\imperial_conquest_2`, `...\ic2-work\`). On another machine, the main
session substitutes its own paths when it fills in a template.

No queue, scheduler or service is built: the labels are the whole protocol.

---

## 9. Standing governance decisions

Decided by the user; in force until changed.

- **Q-A, merge autonomy.** The main session squash-merges any PR that has an approving review and green CI without asking, **except** the architecture PRs T16 and T22. Those wait for the user's thumbs-up and the user's own `/code-review --effort ultra`. T02 and T03 are already merged.
- **Q-B, Godot visual review.** T24 and T25 post a screenshot of every new screen to their PR as they land, and "looks right" is the user's call on each one. The published mockup (`game-design.md` §UI) is the layout intent.
- **Q-C, cost profile.**
  - Opus implements the tasks [§3.3](#33-model-selection)'s criteria name, and reviews the very complex PRs that Claude did not implement ([§3.4](#34-why-the-reviewers-model-differs-from-the-implementers)'s review tiers, the user's decision of 2026-10-03; it reviewed the fidelity-critical PRs before).
  - `/code-review --effort ultra` runs on the architecture PRs.
  - One task is in flight at a time per machine; two machines may run two file-disjoint tasks at once (§8).
  - There is no orchestrator layer and no per-merge documentation agent (incident 13).
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
- **Q-F, plan PR tiers** (2026-09-28). The routine tier of [§4.9](#49-plan-prs-two-tiers) merges on the opener's authority after green CI. The contract tier merges on a cross-session review's approval, and waits for the user only when the verdict says `user decision`.
- **Q-G, the fix lane** (2026-09-28). A bug labelled `fix` runs without a catalogue entry under [§4.10](#410-the-fix-lane), and merges under Q-A.

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
    the worktree root. It is git-ignored; never commit it. (For an OpenCode run this setup is
    already done: `scripts/external-implement.ps1` created the worktree and `-LocalOnly` copied the
    file, so skip this whole block.)

SAY WHERE YOU ARE WORKING. Your very first tool call, before reading anything, prints these four
lines, and your final report repeats them:

  git -C <your worktree> rev-parse --show-toplevel     # must be your worktree, NOT the main checkout
  git -C <your worktree> rev-parse --short HEAD
  git -C <your worktree> rev-parse --abbrev-ref HEAD   # your task branch, or "HEAD" if detached
  git -C <your worktree> diff --name-only origin/main...HEAD

If the first line is C:\Users\diego\projects\imperial_conquest_2, you are in the main session's
checkout: STOP and fix that before doing anything else. Nothing you spawn inherits your shell
directory, so always pass `git -C <your worktree>` explicitly rather than relying on `cd`.

STAY INSIDE YOUR WORKTREE. Once it exists (the setup block above is the only exception, and an
OpenCode run skips it), never read, list, write or run anything by a path outside it: not
%TEMP% or $env:TEMP, not ~ or $env:USERPROFILE, not C:\Program Files, not the NuGet cache, not the
main checkout, and not another worktree. OpenCode's permission guard auto-rejects such a call, and
the rejection ENDS your run, stranding any unpushed work (issue #501). Invoke tools by name from
PATH (`dotnet`, `git`, `gh`, `python`, and `godot` in bash or `godot.cmd` in PowerShell), and never
inspect their installs. Outside the worktree the guard allows only the tool shims in `C:\Users\diego\.local\bin\`
(`godot`, `gh`, `jq`, …) and `%TEMP%\opencode\`, with any `..` path denied (T150). A slip there no
longer ends the run, but it is still against this rule, and the run's report lists every outside
path it touched.
Scratch files go inside your worktree, under the git-ignored `rendered/`
or deleted before you commit, and never in TEMP (the user's rule of 2026-09-29). A mutation check
runs in place and uncommitted: mutate, rebuild clean, test, then `git checkout -- <file>`, touch
it, rebuild clean and test again (§4.2 gate 5).

Your brief is the prompt you were given. Never re-read a brief file by a path outside your
worktree: the main checkout's `rendered/` is outside it, and the guard ends the run there
(T140, #854).

Never use `git stash`: the stash is shared by every worktree in the repository, so another
agent's push or pop can swap entries with yours. To test the base without your change, commit
your work, `git -C <your worktree> checkout --detach origin/main`, test, and check your branch out
again; to set work aside, commit it.

Your task entry is reproduced in full at the end of this brief — it is the contract, and you
should not need to open the catalogue at all. If you do need a different entry, extract that one
rather than reading the file (CLAUDE.md rule 11):
  Read docs/tasks/T<nn>.md

Read first, in order:
  docs/build-process.md   — §2 (ownership), §4 (the loop, the review gates, the bug list).
  docs/game-design.md     — design milestone M<n> and the Design principles at the top.
  docs/design-audit.md    — what the evidence actually supports. §2 lists plausible-looking
                            claims that did not survive checking.
<extra context: the previous attempt's review URLs for a resumed task, research reports to read, etc.>

Your task entry gives Scope, Owns and Done when. All three are binding:
  - Create or modify files ONLY inside your Owns list, plus your own tests.
  - Satisfy every Done-when line with a runnable check.
  - Do NOT edit any Done-when line, weaken an assertion, skip a test, or edit any Markdown file under
    docs/ (docs/**/*.md, which includes your own entry in docs/tasks/).
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
    between degrade silently (incident 7). The review will find it.

When done:
  1. `dotnet build IC2.sln` and `dotnet test IC2.sln` pass in your worktree.
  2. Run each Done-when check and capture its command and output.
  3. Commit everything on task/T<nn>-<slug> (subject "T<nn>: <subject>", trailer "Refs #<issue>")
     and push.
  4. Open a PR with `gh pr create`, using the repository PR template. The body must contain:
     "Closes #<issue>"; the files you touched; a fenced DoD-evidence block with one command+output
     per Done-when line; and "Docs affected", which lists the document claims this merge makes
     stale (file and what changes) or says "none".
     Never put close/closes/fix/fixes/resolve/resolves directly before "#<n>" in a commit, the PR
     body or a comment, except your own "Closes #<issue>": GitHub closes whatever it names, so
     write "bug #276" or "see #199".
  5. Run `git checkout --detach` in your worktree so the branch is free for the reviewer.
  6. Report back: what you built, the DoD results, anything you couldn't verify, and any evidence
     conflict you found. Don't merge, and don't review your own PR.

<task entry: the contents of docs/tasks/T<nn>.md, pasted in full —
 this is the contract, and the reason you should not need to open that file>
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

Scratch files, a mutation copy included, go inside your own worktrees under
C:\Users\diego\projects\ic2-work\ (the git-ignored `rendered/` is the place), never in TEMP (the
user's rule of 2026-09-29). An OpenCode reviewer (`scripts/external-review.ps1`) may not reach
outside the worktree the script made for it, except the tool shims in `C:\Users\diego\.local\bin\` (the Godot
shim among them) and `%TEMP%\opencode\`, with `..` paths denied (T150): its permission guard rejects
any other call, and the rejection ends the review (issue #501).

Never use `git stash`: the stash is shared by every worktree in the repository, so another
agent's push or pop can swap entries with yours. To test the base without the change, in your own
worktree: `git -C <your worktree> checkout --detach origin/main`, test, then
`git -C <your worktree> checkout --detach <the PR head>`, and confirm HEAD equals the PR's
headRefOid again (gate 0) before you write any finding.

The task entry is reproduced in full at the end of this brief; you should not need to open the
catalogue. Read docs/build-process.md §4.2 "What the reviewer checks", docs/game-design.md
(milestone M<n>) and docs/design-audit.md — each by section, not in full (CLAUDE.md rule 11).
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
    finding even if the change is good. Any diff to a Markdown file under docs/ (docs/**/*.md, including docs/tasks/) that
    the Owns list does not name is an automatic rework. The PR's
    "Docs affected" list matches what the diff actually changes.
 5. Correctness. Sweep the diff for ordinary bugs YOURSELF, in your own context: read it hunk by
    hunk, plus the surrounding code it doesn't show, and hunt integer truncation and operation
    order, off-by-one caps and their boundaries, division by a zero denominator, unguarded nulls
    and empty collections, order-dependent iteration, unreachable branches, and tests that would
    pass even with the behaviour deleted (build-process.md §4.2 gate 5 lists these). A comment
    is covered only if a test visits every path that reaches it.
    If the diff DELETES an entity from GameState, ask all three: does anything still reference
    it, are its resources conserved, does a cap still hold? A dangling id makes a save that
    cannot be reloaded (incident 7). Probe it with two entities — one
    deleted, one surviving — since an over-broad clear passes every single-entity test.
    PROVE a candidate before reporting it: run it, or delete the behaviour and watch exactly which
    test fails. Say so when a finding is unverified.
    You MAY fan out — one verification agent per candidate, each given the explicit claim, file and
    line, and what would confirm or refute it. Never let such an agent infer its target from a
    working directory; it starts in the main checkout, not here.
    Do NOT invoke the /code-review skill: from inside a reviewer agent it forks, the fork runs in
    the MAIN CHECKOUT rather than your worktree, its `origin/main...HEAD` is empty there, and it
    silently falls back to reviewing main's last commit (§3.5). If you invoke it anyway, pass the
    PR number as an explicit target, then discard the run unless every finding names a file from
    gate 0's diff.

A defect you find in ANOTHER task's already-merged code is not a finding against this PR. Report
it separately in your summary, so the main session files it as a bug (build-process.md §4.6).
Mark each finding as blocking or non-blocking, by this section:

Blocking means (any one is enough; a blocking finding means rework, never approve):
1. A Done-when line fails, or cannot be run as written.
2. What this task protects can be got past: <the main session names it, from the Scope's
   "Protects:" line: the guard, check, invariant, rule value or file the task exists to protect;
   on a guard task, the list of forbidden actions or results it must stop. An entry written
   before 2026-10-04 has no such line: the main session derives it from the entry's Scope and
   Done-when and writes "(derived by the main session)" after it. For a fix, it is the bug's
   reproduction: the wrong result the bug describes must not recur by any path>. A bypass you proved
   is blocking, even when it looks like an edge case. Never rate it "follow-up hardening" or
   "outside the threat model": the boundary is the one named here, set before review, and only a
   path this item explicitly excludes is outside it.
3. Behaviour the task forbids, or behaviour nobody asked for, inside a file the task requires.
4. Any failure of the gates this brief asks for (gates 2 to 5; a fix-lane brief asks for gates
   3 and 4 plus a read of the diff, and only those count here): a constant with no evidence; a
   random draw outside IRng or an order-dependent result; a file outside Owns, including a
   docs/**/*.md file the Owns list does not name; a test that passes with the behaviour deleted; a comment or doc claim that asserts
   behaviour no test or report supports; a status written into a document.
Not blocking: wording and style that assert nothing false. A defect in code the PR did not change
is not a finding against this PR: report it separately for the bug list (§4.6).
When unsure, rate it blocking and say why. An approve with a proven bypass is the costliest
mistake a review can make.

## Report every blocking finding in this one review

This review is your only pass before the author fixes. Do not stop at the first blocking finding: finish reading the whole diff and the task file, check every Done-when line and every item under "Blocking means", and report all blocking findings together.

- Before you write the verdict, make one last pass over the full diff for anything you have not yet rated, and say "Final pass done" as the last line before the verdict.
- Number the findings R1, R2, … in order of severity. A finding you held back because an earlier one was already blocking is a review defect: if two problems share a cause, list both and say so.
- Do not rely on a later round. The author fixes everything you list, and the next review checks those fixes and new code only, not anything you saw but did not report.
- If you ran out of time or context before covering the whole diff, say which files or sections you did not cover. Do not approve in that case.

Post your findings as a PR comment (`gh pr comment <pr> --body-file ...`): specific, actionable,
with file and line, covering all five gates explicitly, numbered R1, R2, … as the section above
says. Its last two lines are "Final pass done" and then the verdict (approve, approve after named
fixes, rework, or user decision); an OpenCode reviewer also puts the verdict on line 2, and
"Final pass done" goes just before the closing verdict, never before line 2's. Never put close/closes/fix/fixes/resolve/
resolves directly before "#<n>" in it: GitHub closes whatever it names, so write "bug #276". Then apply the label yourself:
  all five gates pass → gh issue edit <issue> --add-label status:approved --remove-label status:in-review
  any gate fails      → gh issue edit <issue> --add-label status:rework --remove-label status:in-review
Do NOT use `gh pr review`: every agent shares one GitHub account, and GitHub won't let an
account review its own PR, so the label is the approval signal.
Don't merge and don't fix the code yourself. When you finish, leave your worktree in place: the main
session removes worktrees on a schedule, once they are safe to remove (§4.7).

<task entry: the contents of docs/tasks/T<nn>.md, pasted in full —
 this is the contract, and the reason you should not need to open that file>
```

## Appendix C: the `/run-task` skill

The skill is installed locally (git-ignored) at `.claude/skills/run-task/SKILL.md`; reinstall it verbatim from this block if it's missing. Skills load when a session starts. It is run by the main session.

```markdown
---
name: run-task
description: Run Imperial Conquest 2 build tasks end to end — dispatch the implementer, then an
  independent reviewer, relay rework, merge, apply doc claims, report. Main session only.
---

# /run-task [T<nn> | #<issue> ...]

Read docs/build-process.md §4 first, and **extract** each task's entry rather than reading the
catalogue whole (CLAUDE.md rule 11):
  Read docs/tasks/T<nn>.md
**Paste that extracted entry into every brief you dispatch** (CLAUDE.md rule 15). An agent given
a pointer instead reads the index, the entry and whatever the entry links to, once per agent per
round (incident 14). Given task
ids, run them in that order; given none, take the first status:ready task in the catalogue index.
Report to the user after each task; stop at any escalation.
Given `#<issue>` of a bug labelled `fix`, run the fix lane (build-process.md §4.10) through the
same steps, with these substitutions: triage has labelled the bug status:ready, so step 0's
check holds as written; the bug body replaces the task entry in Appendix A's brief; Owns = the
files it names plus gate 4's implicit set; DoD = the bug's reproduction as a test that fails
before and passes after, plus green build and test; worktree ic2-work\fix-<issue>, branch
fix/<issue>-<slug>, commit subject "fix <issue>: <subject>" (no #), PR body "Closes #<issue>";
implementer from quota-tracker's `/recommend`, as step 1 says; reviewer by step 2's tier (complex at least) with the line "Fix lane:
gates 0, 1, 3 and 4 plus a read of the diff; no mutation protocol" at the top of its brief. In step 3, a failing
review while the issue carries review-round:1 files a correction task at the contract tier and
stops; there is no review-round:2. In step 4 the follow-up issue is "#<issue> follow-up", and
the docs item applies only if the review named a claim.

0. CHECK. Skip any task that carries another machine's `machine:*` label; claim this one as
   §8 describes (add `machine:$IC2_MACHINE`, re-read, back off if another machine's label appears).
   `gh issue list --label triage:needed --state open`: triage anything that names this
   task, or ask the user. Confirm every merge-after dependency is status:merged and the issue is
   status:ready. Don't start a local-only or single-instance task whose prerequisite is missing.
   Run `bash scripts/unevaluated-reports.sh` (evidence-pipeline.md, "What triggers it"); the
   session-start count may be stale. If a listed report concerns a rule the task's Owns or Scope
   touches (the report's title or key terms match the task's), hold the task, tell the user why,
   and run /process-evidence first. A report that does not concern this task never holds it, and
   a failed script holds nothing: report the failure and go on.
1. IMPLEMENT. Label status:in-progress. Fill build-process.md Appendix A from the task entry
   (plus any review URLs from an earlier attempt). Then, by the entry's model (§3.3: Sonnet reads
   as the default, and so do a non-architecture Opus and a High-effort entry; a fix or a Low-effort task
   is the default too):
   - an OpenCode model (luna, glm-flash, deepseek-flash, glm, qwen-flash, mimo-pro, mimo-flash): write the brief to a file and run
     `pwsh scripts/external-implement.ps1 -Task T<nn> -Slug <slug> -Issue <n> -Model <alias>
     -BriefFile <file>` (`-Fix <issue>` for a fix; `-LocalOnly` where the label says), the alias taken
     from quota-tracker's `/recommend?tier=heavy` with CLAUDE.md rule 17's exclusions (the user's
     decision of 2026-10-08; since T152 `-Model auto` makes the same choice). Log the chosen row's `reasons` on the issue. It creates
     the worktree and branch, runs OpenCode there, and returns with the PR number or a warning;
     read its tail. Run it in the background and watch it (operating-guide §3): the session must
     start and keep making progress. On a failure, READ BEFORE RETRY (CLAUDE.md rule 19, the
     owner's rule of 2026-10-05): before retrying the run, re-routing it to another model, or calling
     it a failure, read its stderr, the OpenCode log and its final message, with
     `python scripts/read-opencode-session.py <ses_…>` (add `--alibaba` for a run on an
     alibaba-token-plan/… model, whose data folder is `data-alibaba`; the id is the
     `opencode: session ses_… started` line that scripts/external-implement.ps1 and
     scripts/external-review.ps1 print; for a Claude agent, read its hand-back in full). A run that
     stopped and reported a blocker is not an early end: post its report on the task's issue and
     answer it (amend the task, decide, or escalate) instead of re-running it on another model.
     Only a run that really ended early or failed falls back: record the cause in one comment, then
     fall back (exit 3: Claude Sonnet).
   - a Claude model: Agent(general-purpose, model = the catalogue's, run_in_background, prompt =
     the brief). Wait for its completion notification; don't poll.
   If the implementer reports a defect in merged code, go to step 5 (bug).
2. REVIEW. Check the PR exists and CI has run. Label status:in-review. Set the review tier, then
   dispatch by it (build-process.md §3.4, the user's decision of 2026-10-03; a plan PR is
   reviewed per §4.9, not here).
   a. CLASSIFY. Build the state file and ask Jev (for a fix, write "PR kind: fix" and the bug
      body, `gh issue view <issue> --json body --jq .body`, in place of the entry):
        mkdir -p rendered/review-tier
        git fetch -q origin pull/<pr>/head
        { echo "PR kind: task"; cat docs/tasks/T<nn>.md; echo; echo "--- git diff --stat ---";
          git diff --stat origin/main...FETCH_HEAD; } > rendered/review-tier/<pr>.txt
        pwsh scripts/jev-ask.ps1 -StateFile rendered/review-tier/<pr>.txt -QuestionsFile scripts/jev/review-complexity.json
   b. CONFIRM. Bucket act (probability 0.9 or above): take Jev's tier unless §3.4's criteria say
      otherwise; bucket claude: decide by §3.4's criteria. A task or fix PR is never simple,
      whatever Jev says. A failed Jev call blocks nothing: decide by the criteria. Record the tier,
      Jev's answer and probability, and any override in one PR comment.
   c. WHO IMPLEMENTED, AND PROBES. IMPL is the name on external-implement.ps1's "implemented by:"
      line (deepseek-flash, glm, luna, ...), or sonnet or opus when Claude implemented (the Sonnet
      fallback, or Opus on an architecture task). Families: OpenAI = luna, sol; GLM = glm,
      glm-flash; DeepSeek = deepseek, deepseek-pro, deepseek-flash; Qwen = qwen, qwen-flash;
      Claude = sonnet, opus. A route (-Route go|zai|alibaba) never changes a family. Every
      script run passes `-ExcludeModel <IMPL>` (sonnet and opus are accepted and exclude no
      OpenCode reviewer); never pick a reviewer of IMPL's family, Claude included. Each brief is
      Appendix B filled in, written to rendered/review-tier/<pr>-<reviewer>.md (before dispatch,
      `grep -n '<the main session' <brief>` must print nothing: item 2 of its "Blocking means" is
      named, never left as the placeholder), its first line the
      header "T<nn> review (<Name>)", <Name> being Sol, Luna, GLM, DeepSeek, DeepSeek Pro, Qwen or
      Qwen Flash
      (always pass -Reviewer, never a -ModelIds override). A brief for GLM or DeepSeek is never
      shortened (nor for Qwen): it always carries both sections in full, "Blocking means" written for this PR
      and "Report every blocking finding in this one review", for a substitute, a pair member and
      a re-check alike (the user's decision of 2026-10-04). Before a complex or very complex
      review (not at session start; the user's decision of 2026-10-03), probe Sol's substitutes so
      that one provider's quota cannot block it:
        pwsh scripts/external-review.ps1 -Pr <pr> -Reviewer sol -ExcludeModel <IMPL> -BriefFile rendered/review-tier/<pr>-sol.md -WhatIf
        pwsh scripts/external-review.ps1 -Pr <pr> -Reviewer glm -ExcludeModel <IMPL> -BriefFile rendered/review-tier/<pr>-sol.md -WhatIf
        pwsh scripts/external-review.ps1 -Pr <pr> -Reviewer deepseek-pro -ExcludeModel <IMPL> -BriefFile rendered/review-tier/<pr>-sol.md -WhatIf
        pwsh scripts/external-review.ps1 -Pr <pr> -Reviewer qwen -ExcludeModel <IMPL> -BriefFile rendered/review-tier/<pr>-sol.md -WhatIf
      They bill nothing (each reads quota-tracker's /avoid for its route and prints a "route:" line). -WhatIf runs the family check first: exit 1 "Refused" means that
      reviewer is of IMPL's family and is skipped, which is not a red probe; exit 0 means the CLI,
      the argument line and the family are fine, not that the provider will serve the run. Note
      a red probe (any other failure) in the tier comment. Run every review
      script in the background and watch it (operating-guide §3). While OpenCode is suspended for
      reviews (two failures of the same cause), treat every script command below as exit 3.
   d. DISPATCH. The commands:
        SOL    pwsh scripts/external-review.ps1 -Pr <pr> -Reviewer sol -Effort low -ExcludeModel <IMPL> -BriefFile rendered/review-tier/<pr>-sol.md -Issue <n> -ApplyLabel
               (-Effort medium only for a very complex PR that Claude implemented, or a complex
               PR whose earlier low-effort Sol review missed what a later review found; never
               high: the user's decision of 2026-10-03, "Sol sparingly, at most medium")
        LUNA   pwsh scripts/external-review.ps1 -Pr <pr> -Reviewer luna -ExcludeModel <IMPL> -BriefFile rendered/review-tier/<pr>-luna.md
        GLM    pwsh scripts/external-review.ps1 -Pr <pr> -Reviewer glm -ExcludeModel <IMPL> -BriefFile rendered/review-tier/<pr>-glm.md
        DSP    pwsh scripts/external-review.ps1 -Pr <pr> -Reviewer deepseek-pro -ExcludeModel <IMPL> -BriefFile rendered/review-tier/<pr>-deepseek-pro.md
        QWEN   pwsh scripts/external-review.ps1 -Pr <pr> -Reviewer qwen -ExcludeModel <IMPL> -BriefFile rendered/review-tier/<pr>-qwen.md
        QWF    pwsh scripts/external-review.ps1 -Pr <pr> -Reviewer qwen-flash -ExcludeModel <IMPL> -BriefFile rendered/review-tier/<pr>-qwen-flash.md
               (GLM, DSP and SUB glm/deepseek-pro take -Route auto by default: Z.AI or OpenCode
               Go, or Alibaba's glm-5.3 / deepseek-v4-pro when /avoid lists zai or opencode_go;
               pass -Route alibaba to force it. The route is printed and signed on the review.)
        SUB x  pwsh scripts/external-review.ps1 -Pr <pr> -Reviewer x -ExcludeModel <IMPL> -BriefFile rendered/review-tier/<pr>-x-for-sol.md -Issue <n> -ApplyLabel
               (x is glm, deepseek-pro or qwen, never luna: Luna reviews only the simple tier, the
               user's decision of 2026-10-04; the brief is Sol's, unchanged except that its
               header names x, one sentence says it
               reviews in Sol's place and why, and Sol's earlier reviews on the PR are linked so it
               re-takes their attacks; probe x with -WhatIf first)
        ADV x  pwsh scripts/external-review.ps1 -Pr <pr> -Reviewer x -BriefFile rendered/review-tier/<pr>-x.md
               (x is nemotron, north-mini, inkling or laguna: an ADVISORY review, optional, never
               -Issue/-ApplyLabel; see ADVISORY below)
        OPUS   Agent(model opus, prompt = Appendix B filled in): a cold reviewer, which applies the label
        SONNET Agent(model sonnet, prompt = Appendix B filled in, plus "Do not apply a status label;
               the main session applies it from every review.")
      A trailing "-" (SOL-, SUB- x) means the same command without -Issue <n> -ApplyLabel: one of
      several reviews.
      QUOTA FIRST (CLAUDE.md rule 17, the user's decision of 2026-10-08, which replaces the /avoid
      check of 2026-10-05): before choosing or dispatching any model, read quota-tracker's
      `/recommend?tier=heavy` (implementers) or `tier=light` (Sol's substitutes), take its `ranking`
      in order with rule 17's exclusions, pass the choice explicitly, and log the chosen row's
      `reasons`. A provider in `skipped` is passed over as if it had exited 3, and the tier comment
      or PR body says so. Luna stays the simple tier's reviewer while its own `gpt-5.6-luna:7d`
      window (`curl -s localhost:8765/quota/openai`) is under 95%. If the service does not answer,
      restart it (`systemctl --user restart quota-tracker`), wait and retry; if it still fails, ask
      the user instead of guessing.
      DIAGNOSE, when SOL or LUNA exits 3: search the files the script kept and named, and the newest
      log in %USERPROFILE%\.local\share\ic2-opencode-1x\data\opencode\log\, for "The usage limit has
      been reached" (or "insufficient_quota", or Z.AI's "Usage limit reached for 5 hour"), and read
      `curl -s localhost:8765/quota/openai?refresh`. Sol's 7d window at 95% or more, or the text
      found, means QUOTA (Sol is out); otherwise SOL-ONLY. Luna's own window decides only Luna.
      SOL'S SUBSTITUTES, skipping any of IMPL's family, the next only on exit 3:
        SOL-ONLY (or the user asked to avoid Sol's cost) and QUOTA alike: SUB glm, then SUB
        deepseek-pro, then SUB qwen (the user's decision of 2026-10-05). The diagnosis is recorded, and decides only the simple tier's fallback.
      After the last one: OPUS when IMPL is not Claude; escalate (step 5) when it is (the user's
      decision of 2026-10-03). Never wait for
      Sol unless the user says to.
      THE PAIR (the OpenCode pair, the Luna pair until the user's decision of 2026-10-04; no Luna):
      two reviews from the order GLM, DSP, QWEN, SONNET (the user's decision of 2026-10-05), each
      skipped when it is of IMPL's family (SONNET when IMPL is Claude); one that exits 3 is replaced
      by the next in that order. The pair has failed when it cannot reach two reviews.
      - complex, IMPL DeepSeek, GLM or MiMo: SOL. Exit 3: DIAGNOSE, then SOL'S SUBSTITUTES, then
        OPUS.
      - complex, IMPL Claude: SOL. Exit 3: DIAGNOSE, then SOL'S SUBSTITUTES, then escalate.
      - complex or very complex, IMPL OpenAI (luna): OPUS.
      - very complex, IMPL not Claude: OPUS.
      - very complex, IMPL Claude (the user's decision of 2026-10-03), in this order:
        1. Run SOL- alone (with -Effort medium) and wait for its outcome. Start nothing else yet.
        2. SOL- posted: run THE PAIR. Count: SOL- + the pair's two = three.
        3. SOL- exits 3: DIAGNOSE (for the record). SOL-ONLY and QUOTA alike (under QUOTA Luna is
           out with Sol): SUB- qwen stands in for Sol, beside GLM and DSP, each skipping IMPL's
           family. Count: GLM + DSP + SUB- qwen = three (the owner's decision of 2026-10-05,
           which replaces the two-review form of 2026-10-04).
        4. Escalate (step 5) when the count cannot be reached: THE PAIR fails.
      Labels: with one review, its reviewer labels (SOL, SUB x or OPUS). With two or three, the
      main session labels: status:approved only when every review approves, status:rework when any
      asks for rework (step 3 relays every review in full), and it escalates on any user decision.
      ADVISORY (the owner's decision of 2026-10-06): on a small PR or a plan PR the main session
      MAY add ADV x as a second opinion, after or beside the tier's reviews. It is never counted,
      never labels, never replaces a family's review, and its brief carries nothing private (the
      script refuses one naming ic2-test-fixtures, assets.local.ini, IC2_FIXTURES_DIR, a .dat or
      .sav path, or anything key-like: docs/environment.md lists the rules). Its findings are weighed like any unreviewed contribution: verify a claimed defect
      before relaying it as a finding. Exit 3 (free allowance at 50 or fewer, tracker silent, or
      "rate-limited, skipped") means no advisory review: go on without it, never retry.
      RECORD a substitute: for a task, commit on main a one-line note in docs/tasks/T<nn>.md's
      Reviewer field ("2026-MM-DD: <substitute> reviewed in Sol's place: <reason>"), subject
      "Docs: T<nn> reviewer", a routine doc claim; for a fix, put it in the tier comment. Append
      one entry to the wiki page Model trials: the error, the probe results, the substitute, and
      what it caught or missed against Sol's earlier rounds.
   Exit 5 means GitHub did not take the comment (gh pr comment failed twice, or returned no
   comment URL; bug #645): nothing was posted and no label was set; read the saved file the
   script names (rendered\review-not-posted-pr<pr>-<reviewer>-<time>.md), post it by hand with
   `gh pr comment <pr> --body-file <that file>`, and act on its verdict as if the script had.
   Exit 4 means a review was posted flagged (cut off, verdict unreadable, or findings after the
   closing verdict) and no label was set: read it on the PR and decide (count it by its content,
   or run that path's next reviewer). Exit 1 with "Refused" means a reviewer of IMPL's family was
   named: fix the command. A rework round's re-review keeps the tier and its reviewers, except
   Sol, which runs only where the tier names it (the user's decision of 2026-10-03): a re-check
   of named fixes (the one-line confirmation after "approve after named fixes", or a rework
   re-review that only checks the named findings) goes, with the earlier reviews linked in its
   brief, to LUNA on a simple PR, and on a complex or very complex PR to the first of GLM, DSP and
   QWF that IMPL's family does not exclude (the user's decision of 2026-10-04; QWF added on
   2026-10-05: the re-check order is Luna, GLM, DeepSeek Pro, Qwen Flash), and back to
   SOL only when the fixes rewrote more than the named findings.
3. DECIDE on the label the reviewer applied:
   - Before acting on an approve that carries "not blocking" findings, read every one. If one is a
     proven way past what the brief's "Blocking means" item 2 names, treat the review as rework:
     say so on the PR, set status:rework, and record it on the wiki page Model trials (harness
     lesson L47, the user's decision of 2026-10-04).
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
     ONE BLOCKER PER ROUND, checked first: if this review and the one before it each named
     exactly one blocking finding (two rounds running), stop: go through the whole diff yourself
     for the same class of problem, mark what you find as the main session's findings, and
     append the pattern to the wiki page Model trials: the reviewer, the PR, the rounds and the
     class (the user's decision of 2026-10-04). Your findings go with the review's, on whichever
     path follows: into this rework; at review-round:2, into the escalation comment; on a fix at
     review-round:1, into the correction task's entry.
     Then, on a fix whose issue carries review-round:1, file the correction task at the contract tier
     with the review's findings and yours, keep the branch, and stop (§4.10: a fix gets one rework
     round). Otherwise, if the issue carries review-round:2, escalate (step 5). Otherwise set the next round
     (none → review-round:1 → review-round:2) and send the implementer the FULL review comment
     URL: SendMessage if it is reachable, else a fresh implementer resuming the branch. Then go
     back to step 2.
4. AFTER THE MERGE (build-process.md §4.7):
   - Unblock: each status:blocked task whose merge-after deps are all merged, and which isn't
     suspended on an open bug, becomes status:ready.
   - Follow-up: if the review had non-blocking findings, file one "T<nn> follow-up" issue
     (triage:needed, linking the reviews) and propose where each item folds.
   - Docs: apply the PR's "Docs affected" claims on main ("Docs: after T<nn>"). Write no status.
   - Leave the task's worktrees under C:\Users\diego\projects\ic2-work\ in place: they are removed on
     a schedule, once safe (§4.7 step 4).
   - Report: the merge commit, the review's findings, the follow-ups, and what's ready next.
5. ESCALATE or BUG.
   - Escalate (build-process.md §4.5): label status:escalated, comment the evidence on the
     issue, and bring it to the user with options and a recommendation. Stop the run.
   - Bug in merged code (§4.6): label the task status:blocked with "suspended on #N". File the
     bug (bug, triage:needed; the body opens "Blocks: T<nn>"). Triage it, or bring it to the
     user. Stop the run.

A MAINTENANCE-LINE ITEM (a task or fix labelled release:v0.4.x after v0.4.1, release-plan.md
§2.2.2) runs through the same steps with these overrides. Before step 1, the main session creates
and pushes its branch from origin/release/0.4 (git branch <branch> origin/release/0.4; git push -u
origin <branch>), so every implementer resumes it. In Appendix A's and B's briefs, in gate 0 and in
step 2's state file, every origin/main reads origin/release/0.4, and the brief's first line says so.
The PR is opened with --base release/0.4 and says "Refs #<issue>", never "Closes". In step 3, the
merge is followed at once by the forward port: a port/<issue>-<slug> branch from origin/main with
git cherry-pick -x <the squash commit>, a PR against main saying "Closes #<issue>", reviewed per
release-plan §2.2.2 and merged on an approving review and green CI. The issue is status:merged when
the port merges, or when the recorded no-port reason has been told to the user.

Never: merge without an approving review and green CI; weaken a DoD; let the implementing agent
review its own PR; force-push; delete a task branch that holds unmerged work; patch a defect in
another task's Owns list; relay only part of a review.
```
