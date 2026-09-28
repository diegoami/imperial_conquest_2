# Workflow audit, 2026-09: what to inherit for the next reverse-engineer-and-rebuild project

A report for the user, not a contract. It changes no rule and no document, and it is not meant to
merge. It answers the question "what should the next project of this kind inherit from this one, and
is the process over-engineered for the models available now?" in six parts: the timeline, a verdict on
each rule, the smallest process that would have produced v0.3.0, a starter kit, cost, and which files
can be reused as they are.

**Sources.**
- `CLAUDE.md`, `docs/operating-guide.md`, `docs/build-process.md` (read section by section),
  `docs/evidence-pipeline.md`, `docs/release-plan.md`, `docs/milestone-review.md` and the
  catalogue index.
- The task entries `docs/tasks/T06.md` (early), `T91.md` and `T95.md` (late).
- The wiki pages *Process incidents* and *Review 2026-09-18*.
- `git log origin/main` (371 commits) and the three tags.
- All 226 PRs and 267 issues, fetched through the GitHub API, with each issue's label events.
- The descriptions and reviews of #264, #465, #471, #475, #477, #479, #480 and #482.

The raw pulls sit in the auditing session's scratchpad and are not committed; every count below can be
re-derived from the API. "Incident N" means entry N on the
[Process incidents](https://github.com/diegoami/imperial_conquest_2/wiki/Process-incidents) page.

**One bias to state up front.** The auditor is a Claude model of the same family as this project's
implementers and reviewers. Where section 2 asks "would a current model still make this mistake", the
answer is written against that bias: I assume I would, unless the incident's cause is plainly
something other than the model.

---

## 1. Timeline

### 1.1 The dates

| Point | When | Calendar days from first commit | Days with commits |
| --- | --- | ---: | ---: |
| First commit (`c81ad78`) | 2026-09-12 04:33 | 0 | 1 |
| Build plan: 28 tasks, graph and review pipeline | 2026-09-13 | 1 | 2 |
| First task merged (T05, #30) | 2026-09-13 | 1 | 2 |
| `v0.1.0`, "the rules run" (tag only; no GitHub Release) | 2026-09-18 | 6 | 4 |
| `v0.3.0`, "headless playable" | 2026-09-24 19:25 | 12.6 | 9 |
| `v0.4.0`, "playable with a UI, from source", the first build a person plays with a UI | 2026-09-28 21:52 | 16.7 | 12 |

`v0.2.0` was never cut (see §2.4, disagreement 4). The first time anyone played the UI is recorded
on #491: "found by the user's first play of v0.4.0 (2026-09-28)".

### 1.2 The PRs

| Interval | Task PRs | Plan/process PRs | `Docs:` commits on `main` | Commits in total |
| --- | ---: | ---: | ---: | ---: |
| Start → `v0.1.0` | 25 | 22 | 26 | 168 |
| `v0.1.0` → `v0.3.0` | 35 | 51 | 20 | 108 |
| `v0.3.0` → `v0.4.0` | 24 | 49 | 19 | 93 |
| **Start → `v0.4.0`** | **84** | **~122** | **65** | **369** |

The interval columns count commit subjects on `main`. Across the whole repository, the API gives:
- 84 task PRs;
- 127 plan PRs (125 merged, #279 closed, #482 open);
- 8 `Docs:` PRs;
- 1 fix PR (#492, the only one the fix lane has run);
- 2 review PRs (#113, and #343, which was never merged by design);
- 2 evidence PRs and 2 other PRs.

Plan PRs outnumbered task PRs in every week after the first. By ISO week (task / plan):
- W37: 9 / 4
- W38: 39 / 63
- W39: 33 / 49
- W40, 09-28 only: 3 / 11

The 127 plan PRs split by title into:
- 48 that add or change a task or its Done-when;
- 29 on process or tooling;
- **27 that grant or widen an Owns list**;
- 23 triage or fold PRs.

### 1.3 Corrections before the first UI task

The catalogue grew from the 28 tasks planned on 09-13 to 95. By the catalogue's own list, as it stood
before #471 deleted the count, 48 of the 95 are corrections to already-merged code.

| First UI work | Merged | Task PRs before it | Corrections among them (catalogue's list) |
| --- | --- | ---: | ---: |
| T47, a thin Godot slice | 2026-09-18 | 27 | 11 |
| T24, the main game screen, the first real UI task | 2026-09-28 09:34 | 81 | 41 |

My own sort of the 57 non-original tasks merged before T24 is stricter:

| Kind | Tasks | Count |
| --- | --- | ---: |
| Correction or hygiene of merged code | T31–T34, T43–T45, T50, T52, T64–T66, T68, T70, T74, T77, T78, T90–T92 | 20 |
| A rule the plan never owned, found by later evidence | T35, T37–T39, T46, T54, T55, T57, T60, T63, T69, T73, T75, T82, T84–T89 | 20 |
| Infrastructure and seams | T29, T30, T36, T40, T42, T53, T79 | 7 |
| Slices, CLI and assets | T41, T47–T49, T80, T83 | 6 |
| Layout | T61, T62 | 2 |
| Research | T58, T59 | 2 |

Under either count, roughly half the engine work between the plan and the first real UI task was
fixing or completing the plan's own first pass.

### 1.4 Where the calendar went

Of the 17 calendar days, 12 had commits. 09-15, 09-16 and 09-21 had no commits and no PRs; 09-17 and
09-22 had only PRs opened.

- **Reverse engineering: about 1.5 days up front, then continuous.**
  - 09-12 is 48 direct commits: save and DAT decoding, Ghidra setup, and a full decompilation pass (turn
    order, combat, capture, billing, the save layout).
  - The design and its audit followed on the morning of 09-13, and the research moved to its own
    repository that day.
  - Research never stopped. Evidence passes (#291, #294 and others) and research-repo commits run
    through the whole build, and 20 of the 81 pre-T24 tasks exist because later evidence found a rule the
    plan did not have.
  - The research repository's own history is outside this audit's access, so time spent there before
    09-12 or in parallel is **not measured**.
- **Engine: 09-13 to 09-27.**
  - `v0.3.0` came after 9 active days.
  - The four days after `v0.3.0` (09-25 to 09-27) went to 17 fidelity tasks (diplomacy, conquest,
    rebellion, rebirth) and 37 plan PRs, with no UI work beyond T48's assets.
- **Process.**
  - Plan PRs plus `Docs:` commits (about 190) outnumber task PRs (84) by more than two to one.
  - The process itself was redesigned on 09-14 (an orchestrator added in #54 and retired the same day in
    #85), 09-18 (gate 0, the wiki split, the demo rules), 09-20 (token economy), 09-27 (two machines)
    and 09-28 (#465, #471, #475, #479, #480 and #490 in one day).
- **UI: one day.**
  - #465 introduced the playability gate and the UI-first order at 07:30 on 09-28.
  - T24 merged at 09:34, T25 later that day, T95 in the evening, and `v0.4.0` was tagged at 21:52.
- **Waiting: less than the incidents page implies.**
  - Plan PRs had a **median of 6 minutes** from open to merge (75th percentile 30 minutes, maximum 66
    hours). Task PRs had a median of 48 minutes from open to merge; implementation happens before the PR
    is opened, so that is review time.
  - The five idle calendar days are the dominant wait. Plan PRs cost attention, turns and tokens more
    than wall-clock time. Time spent waiting before a PR was opened is not measured.

**The one-line version.** The calendar went to fidelity work that the plan did not foresee, and then
to four days of that work after "headless playable". Process ceremony did not take the calendar, and
waiting for approvals did not either. The biggest calendar lever was the order in which work was done.
Two warnings came earlier: the 2026-09-18 review (§5.2) said the Godot lane was "the largest untested
risk, and it is last", and T47 proved the seam that same day. The UI chain still started only after
#465, ten days later.

---

## 2. Rule by rule

### 2.1 How to read the verdicts

**Origin**, the kind of failure the rule prevents:
- **Model**: a mistake an LLM agent makes.
- **Shape**: this project's configuration (two machines, the Godot lane, a private fixtures repository,
  the research repository, Windows paths).
- **Harness**: how the Claude Code agent tooling behaves.
- **Process-made**: a failure that an earlier rule of this process created.

**Verdict**:
- **Keep**.
- **Cond.**: keep only if the new project shares the stated condition.
- **Drop**.

### 2.2 CLAUDE.md rules 1–16

| # | Rule, in brief | Produced by | Origin | Verdict and reasoning |
| --- | --- | --- | --- | --- |
| 1 | The main session runs the build; no orchestrator | Incident 13: the orchestrator was added in #54 and retired in #85, both on 09-14 | Process-made | **Keep, as a default, in one line.** A nested orchestrator was pure overhead for serial execution. |
| 2 | Agents never work in the main checkout | Two shared-checkout incidents (a doc fix landed on an implementer's branch; a skill file was written mid-review), and incident 5 | Harness | **Keep the rule; drop the hand-built mechanics.** The Agent tool now takes `isolation: "worktree"`, which removes most of Appendix A's path boilerplate. Keep "never `git stash`" (#283): the stash is shared by every worktree. |
| 3 | Status on labels only; contracts in the repo; living reference in the wiki | Incident 10: five "Docs: status resync" commits on 09-14 alone, and per-merge prose sync that kept drifting | Model + process | **Keep "no status snapshot in documents".** An agent will write "where things stand" prose unless told not to, and I would. **Cond. for the wiki**: only once living pages are large enough that agents read them by accident. |
| 4 | Plan PRs in two tiers, merged on a cross-session review | Incident 12 | Process-made | **Drop.** The tiers treat a symptom. 27 of the 127 plan PRs were Owns grants and 48 were Done-when edits. They exist because Owns lists were drawn at function and line level (T91's Owns cites `~:158`, `~:830`) and because every DoD edit needed a PR. Remove the cause (§2.3, Owns and DoD) and the tiers have little left to sort. What I would accept instead: the main session edits a task file directly on `main`, with the reason in the commit message, and the reviewer of the task that implements it sees the edit. The risk is a wrongly loosened DoD reaching an implementer unreviewed; the task's reviewer is the backstop. |
| 5 | A question is not a request to edit files | User preference | Model | **Keep.** Current models still act on questions; I would too. It costs one line. |
| 6 | Upstream defects go through the bug list; a small fix runs as a `fix` | §4.6, the 09-18 review §4.2 and §5.4, and incident 12 | Model + process | **Keep the fix lane from day one, lighter.** **Drop "suspend, file, plan, resume" for a mechanical blocker.** The 09-18 review costed that at a day of wall time per instance, and it recommended the fix lane ten days before #471 adopted it. What I would accept: a blocking one-file fix rides the PR that found it, declared under Scope, and the reviewer checks it. A fidelity defect (a rule's outcome) still goes to the bug list. |
| 7 | Relay review findings in full | User preference, and the rework loop | Model | **Keep.** A model relaying a review summarises and filters by default. I would, and a dropped finding is invisible afterwards. |
| 8 | Merge only with an approving review and green CI; T16 and T22 need the user's thumbs-up | Q-A | Model | **Keep the first half.** The second half is spent: T16 and T22 merged on 09-19. **Cond.**: name a user sign-off only for the handful of architecture tasks in the new project. |
| 9 | Commit and push research-repo work without asking | User preference | Shape | **Cond.**: a separate research repository. |
| 10 | At session start, check the triage queue and in-flight tasks; respect `machine:*` | §5 and §8 | Harness + shape | **Keep the start-of-session check** (a new session has no memory). **Cond. for the machine clause**: more than one machine. |
| 11 | Never read a large file whole; extract the slice | #264's measurements: catalogue 84,809 tokens, world 99,897 | Process-made (layout) | **Drop as a rule; keep the layout.** T61 and T62 fixed the cause: one file per task, and the terrain blob moved to a sidecar. Start the new project that way and the rule has nothing to police. Cond.: any data file over ~20k tokens that agents must touch gets one `jq` recipe line. |
| 12 | Scope every search; never traverse ignored directories | Windows checkouts with `bin/`, `.godot/` and recordings on disk | Harness + shape | **Keep, in one line.** |
| 13 | Surgical output: diffs, not files | Token cost | Model | **Keep, in one line.** |
| 14 | Quiet terminal | Token cost | Model | **Keep, in one line.** |
| 15 | A brief carries the extracted entry, not a pointer | Incident 14: ~85,000 tokens per agent per round to reach ~1,450 of contract | Harness + process | **Keep.** It is the highest-leverage rule here and it stays true with any model: a cold agent given a pointer reads everything the pointer reaches. |
| 16 | Offer a fresh session only at a true seam | Harness compaction | Harness | **Drop.** It is advice about the harness, not a failure. |

### 2.3 build-process.md mechanisms

| Mechanism | Produced by | Origin | Verdict and reasoning |
| --- | --- | --- | --- |
| **Roles**: main session, implementer, reviewer, researcher | §3.1 | — | **Keep all four.** The evidence is the strongest in the audit. About 69 of the 84 merged tasks (82%) went through at least one rework round, and about 33 reached a second. Reviewers filed 64 of the 112 `bug` issues, implementers 23, research 11, and the user 4. |
| **Reviewer on a different model** | §3.4, and `design-audit.md` §2.1 (a same-model reviewer twice repeated the same misreading) | Model | **Keep, as one line.** The six-row pairing table becomes: "the reviewer is the other of Opus and Sonnet; fidelity-critical and shared-model work gets Opus on one side". |
| **Effort scale and per-task model and effort** | §3.2 and §3.3 | — | **Keep in the entry; drop the `model:*` and `effort:*` labels**, which duplicate it. "No Haiku for integration work" is incident 4. |
| **Worktrees, and "say where you work"** (four `git` lines first and last) | Incident 5, §7 | Harness | **Keep a two-line version**: the agent prints its toplevel and HEAD. The rest exists because agents were pointed at worktrees by prose; `isolation: "worktree"` does this for you. |
| **Owns lists** | §2.2 | Process + model | **Cond.** The failure they prevent is two in-flight tasks colliding in one file. Under §7's one-task-per-machine rule that cannot happen on one machine. The second job, stopping an implementer from wandering, is real (incident 4: T26's Haiku implementer wrote outside Owns). A directory-level Owns list plus a reviewer who asks "is every changed file needed for this task?" does that job. Function- and line-level Owns produced the 27 grant PRs and the implicit-ownership patch of #465. Keep file- or directory-level Owns. Go finer only when two tasks run concurrently in the same file. |
| **The DoD is immutable to the implementer** | §4.3 | Model | **Keep; this is the one to never drop.** A model under pressure to go green weakens an assertion, marks a test skipped, or reads a range where an equality was asked for. I would. "Stop and report" is how #482's T93 trial ended correctly. **Change who may amend a DoD**: the main session, directly, with a reason, instead of a plan PR. |
| **Gate 0**: the reviewer's HEAD and file list equal the PR's | Incident 5: four wasted passes, and nine confident findings about an unrelated commit | Harness + model | **Keep (three lines).** The harness caused the wrong tree. The confident findings are a model trait I share: I trust my tool's output. |
| **Gates 1–4**: DoD re-run, provenance, determinism, scope | §4.2 | Model | **Keep 1 and 2.** Re-running the DoD and tracing every constant to evidence are the core of an RE rebuild. **Cond. for 3**: the product has seeded randomness or a replay requirement. **Soften 4**, as with Owns above. |
| **Gate 5, the correctness sweep list** | Incidents 6, 7 and 8 | Model | **Keep the method; grow the list per project.** "A test that would still pass if the behaviour were deleted" (prove it by mutation), "a branch that can never be taken" and "a comment asserting an edge no test visits" are generic model failures, and I make all three. "A delete that leaves a dangling id" is **Cond.**: an entity graph with cross-references. Integer truncation is **Cond.**: porting integer-semantics code. |
| **Mutation admissibility**: `touch` and a clean rebuild, and the helper lives in its own worktree | Incident 9 | Harness/toolchain | **Cond.**: MSBuild or any incremental build keyed on mtime. It is one line. |
| **Task branches never edit `docs/`; "Docs affected"; the main session applies the claims after the merge** | §2.3, §4.7, incident 10 | Process-made | **Drop.** This rule produced the 65 `Docs:` commits and incident 3's exception. Its purpose was conflict avoidance between concurrent tasks and keeping status out of documents. Under serial execution, let the task PR fix the documents its change makes wrong, and let the reviewer check them. Keep "no status in documents". |
| **The regenerable CLI golden and the seed-test rules** | Incident 2 | Process-made | **Cond.** Needed only when a golden transcript exists. The underlying rule is generic: regenerate a golden by running the program, never by hand, and explain every changed line. |
| **Pre-committed seams, assembly-scanned registration, no shared registry file** | §2.1 and §2.3 (T01, T03) | — | **Keep.** Good engineering at almost no cost. No merge-conflict incident was ever recorded. |
| **Fixtures corpus with per-entry provenance** | §2.4 (T04) | Model | **Keep.** It removes a re-reading step, and every re-read is a chance to misread. It is the RE-specific idea most worth carrying. |
| **`[confirmed]`/`[derived]`/`[designed]`, with "what was searched"** | `design-audit.md` §4.5, incident 1 | Model | **Keep.** Incident 1 happened: wrong constants merged fast, and reviewers and research caught them. Implementers did catch some later (23 bugs, #467 among them), but the reviewer remains the main catch. |
| **Open questions become ruleset flags, two presets (Q-D)** | §9 | — | **Cond.**: the product wants both faithful and improved behaviour. It was a cheap way to never block on an unanswered question. |
| **Rework cap (two rounds, one for a fix) and the escalation list** | §4.4 and §4.5 | Model | **Keep, shortened.** Ten escalation triggers become four: design question, weakened DoD, a third round, anything destructive. 17 tasks escalated at some point, so the cap fires. |
| **One follow-up issue per merge** | Q-E | — | **Keep.** It is cheap and it is the triage queue's feed. |
| **The triage queue** (`triage:needed`) | §4.6 | — | **Keep.** One label, checked at session start. |
| **The playability gate** | #465, 09-28 | — | **Keep from day one, in a stronger form.** It was the single most effective change in the project: `v0.4.0` landed the day it was adopted. In the new project it is an ordering rule from the start. First a walking skeleton through to a screen a person can play (T41 and T47 were exactly this, early). Then, until the first playable build, only a bug that breaks play is scheduled. |
| **Plan-PR tiers** (§4.9) and the **reviewer order** (another main session → GLM → Luna → DeepSeek → cold Opus → user) | Incident 12, #471, #479 | Process-made | **Drop on day one** (see rule 4). |
| **The fix lane** (§4.10) | Incident 12, #471; recommended 09-18 | Process-made | **Keep from day one.** It should have existed from the start. It has run once (#492). Its test is sound for an RE project: no ruleset key, corpus value, seeded measurement or golden line changes. |
| **Labels-only status** | §5 | Model + harness | **Keep, with about eight labels**: `task`, `bug`, `triage:needed`, `status:{ready,in-progress,in-review,rework,merged}`, `review-round:{1,2}`. Drop `phase:*`, `model:*`, `effort:*`, `lane:*` (while there is one lane), `release:*` (use a GitHub milestone per release, if anything), and the phase milestones. |
| **Two machines** (§8, `machine:*` claims, one primary for triage) | #445, 09-27 | Shape | **Cond., and not on day one.** Seven tasks carry a `machine:*` label, two of them on the second machine. Two tasks of parallelism cost a 100-line section and three plan PRs (#445, #462, and part of #479). |
| **`local-only` and `single-instance` labels** | §7 | Shape | **Cond.**: original files outside CI, or a single-instance tool. Since T53 CI fetches the fixtures, so `local-only` now matters only for work that needs the original executable. |
| **The external reviewer script** (OpenCode: GLM → Luna → DeepSeek) | #480, #490, and the user preference of 09-25 | Model | **Cond.** Its case is independence: a different vendor's model. It bought one confirmed catch in the PRs read here: on #477, Luna found that `--quit-after` could give a false green. Against that stand two startup hangs (the stdin EOF bug), a cut-off first review (#370) and 538 lines of PowerShell. Adopt it when a defect class has twice escaped the Claude reviewer and a trial shows the other model catches it. |
| **`/code-review --effort ultra`**, and the user's own for T16 and T22 | §3.5 | Harness | **Cond.**: architecture-critical PRs only, always with an explicit PR target (incident 5). |
| **Milestone review** (a never-merged review PR, freeze and re-freeze) | `milestone-review.md`, #343 | — | **Keep for the first public release, not before.** The document is already portable. |
| **The release plan** (286 lines, five rungs, SemVer reasoning) | 09-13 | — | **Drop the document; keep two rules**: tag capability jumps, not phases; notes are generated from GitHub at cut time. |
| **The evidence pipeline** (two stages, four routes) | `evidence-pipeline.md` | Model | **Keep, if evidence keeps arriving during the build**, as it will in an RE project. The four routes (fact, bug, task edit, user decision) stop a research agent from deciding design. "A recording needs no note" is worth carrying. |
| **Wiki for living reference** | #120, 09-18 | Process-made | **Cond.** (rule 3). |
| **"Don't start a rework round as the session winds down"** | User preference, 09-19 | — | **Keep** as a user preference, not a process rule. |

### 2.4 Where the incidents page and the documents disagree

1. **Incident 12's premise.**
   - The page says about 125 plan PRs against about 75 task PRs, "each plan PR waiting for the user".
   - On 09-28, before #471, the count was about 118 plan PRs merged against 81 task PRs.
   - The **median plan PR merged 6 minutes after it opened**. So the user was not a calendar bottleneck
     at the PR. The real cost was the user's attention and the session's turns, and the page should say
     so, because it changes the remedy. The tiers speed up merges; a coarser contract removes the PRs.
2. **Incident 4's list.** It names T10, T18 and T26 as moves away from Haiku. `git log` also shows
   "Docs: after T11 — model moves from Haiku to Sonnet" on 09-18.
3. **Incident 10's date.**
   - The page dates the retirement of prose syncing to 09-18.
   - Status snapshots were retired on 09-14 ("Simplify the process: retire the orchestrator and status
     snapshots", #85). The wiki split was 09-18 (#120). The page conflates two steps.
4. **`release-plan.md` against the tags.**
   - §1.4 says every rung "gets a GitHub Release". `v0.1.0` has none, and `v0.2.0` was never cut.
   - §1.2 says "the engine is feature-complete at `v0.3.0`". After that tag, 17 tasks added rules the
     original has (T69, T75, T82, T84–T89 among them).
5. **Incident 1** is stated in build-process §1 as a standing fact: "most corrections have come from
   reviews and research". That is still true (75 of 112 bugs), but implementers found 23. The rule holds;
   the "never the implementer" of the incident's title does not.
6. **The operating guide's external-reviewer bullet** says the reviewer "is GPT-6 Luna". The same bullet
   then makes GLM-5.3 the default, per #480.
7. **Operating guide §2.3** is headed "The two skills" and lists three.
8. **CLAUDE.md rule 8** and Q-A still carry the T16 and T22 sign-off, which cannot fire again.

---

## 3. The over-engineering test

### 3.1 The smallest process that would have delivered the same `v0.3.0`

- **One main session.**
  - It plans in `docs/tasks/T<nn>.md` files of 150–400 words each: goal, evidence links, Owns at
    directory level, Done-when.
  - It edits those files directly on `main` with a reason in the commit message.
  - It brings only design questions to the user.
- **Per task.**
  - One implementer agent in `isolation: "worktree"`.
  - One reviewer agent on the other model, at gates 0, 1, 2 and 5 (plus 3 where the product is
    seeded).
  - The PR edits the docs its change makes wrong, and CI must be green.
  - Two rework rounds, then escalate.
- **Per bug.**
  - A fix PR with a failing-then-passing test, and one review round.
  - A defect that changes a rule's outcome becomes a task.
  - A mechanical blocker rides the PR that found it, declared.
- **Kept from this project:** the fixtures corpus, the provenance tags, the seams, the two-stage
  evidence pipeline, and a walking skeleton to a screen before the wide engine fan-out.
- **Not set up at all:** plan PRs, `Docs:` commits, the wiki, two-machine claims, the external reviewer,
  the release plan, and every label beyond about eight.

### 3.2 Counted against the real one

| | Real, to `v0.3.0` | Minimal, to `v0.3.0` (estimate) |
| --- | ---: | ---: |
| Task PRs | 60 | 45–55. Feature work unchanged; about 20 corrections become 10–20 fix PRs, some batched. |
| Plan PRs | ~73 | 0. About 30–40 direct commits to task files instead. |
| `Docs:` commits | 46 | ~5, for the release docs pass only |
| Commits to `main` | 276 | ~100–120 |
| Process documents | `CLAUDE.md` (78 lines), `operating-guide.md` (222), `build-process.md` (760), `release-plan.md` (286), `milestone-review.md` (128), `evidence-pipeline.md` (80), `recording-analysis.md` (216), the catalogue index (933), and 6 wiki pages: **about 2,700 lines** | `CLAUDE.md` (≤40), `process.md` (≤150), `evidence-pipeline.md` (80), a task index (~100): **about 370 lines** |

The minimal process would not have produced fewer defects. The reviewer-driven rework rate (82%) and
the evidence-driven correction tasks are the cost of fidelity, and they stay. What it removes is the
layer of contract bookkeeping on top: roughly two thirds of the PR count, and more than 85% of the
process text.

### 3.3 What the real costs bought

**Bought something:**
- **Independent review on every task.** 64 bugs filed and about 69 tasks sent back. It is the most
  productive mechanism in the project.
- **Research passes and the evidence pipeline.** 11 bugs, and the source of the 20 missing-rule tasks.
  An RE rebuild without this is a guess.
- **Gate 0, the mutation rules and the delete sweep.** Each is a real incident (5, 9, 7) turned into a
  cheap check.
- **Triage and fold plan PRs.** 23 of them recorded where each bug went, a decision record the user can
  audit. Direct commits with issue comments would have recorded the same.
- **Done-when corrections that caught a wrong contract.** #145 (T14 DoD 3 contradicted Q6), #207 (T53
  DoD 4 asked for an unreachable number), #266 and #271 (T60). The contract was wrong and the plan PR
  caught it. The task's reviewer would have caught it in the same place.
- **T61 and T62.** They cut orientation from ~85k tokens to ~1.4k per agent per round. That fixed a
  cost the layout had created.
- **The playability gate.** `v0.4.0` in one day.
- **The external reviewer.** One real finding, on #477.

**Bought nothing, or less than it cost:**
- **The 27 Owns-grant PRs.** Each granted permission for a change the task already needed.
- **About 60 `Docs:` sync commits**, and the per-merge prose sync they served, retired by incident 10.
- **The orchestrator**, retired the day it was introduced (#54 → #85).
- **The two-machine protocol, so far.** Two tasks on the second machine.
- **The contract-tier reviewer chain for plan PRs** (#479). It solves a queue that coarser contracts
  would not have had.
- **Structure that duplicates GitHub or goes stale**: the frozen wave table, the phase milestones, the
  `model:*`, `effort:*` and `phase:*` labels, and the 286-line release plan for three tags.
- **The token-economy excerpt table** (rules 11 and 14 as written). Most of it policed a layout that T61
  and T62 then fixed.

### 3.4 Is it over-engineered for current models?

In part. The rules that guard against **model** failures are not over-engineered, and I would not
drop them for a current model:
- the DoD immutability;
- independent review on a different model, re-running the DoD;
- provenance tags;
- mutation-backed "a test that fails without the behaviour";
- relaying findings in full;
- gate 0.

The failures they prevent (weakened assertions, vacuous tests, confident comments about unvisited
edges, trusting a tool's output about the wrong tree) are ones I make. The 82% rework rate on Sonnet
and Opus implementers is the evidence that they still happen.

What is over-engineered is the **contract bookkeeping** built on top:
- line-level Owns;
- plan PRs for every contract change;
- the docs-never-on-task-branches rule and its after-merge sync;
- the label taxonomy;
- tiers and reviewer chains to speed that bookkeeping up.

Most of it is process-made. Each layer was a patch on a failure the layer beneath it caused (incidents
2, 3, 12 and 14). A current model does not need any of it to stay inside a task's scope; a reviewer
asking "is this change needed?" does that job.

---

## 4. The starter kit

### 4.1 CLAUDE.md (40 lines)

```markdown
# <Project>

Rebuild of <original> from reverse-engineered evidence. `docs/process.md` is the process; read it
before running a task. Design is `docs/design.md`; what the evidence supports is `docs/audit.md`.

## Rules
1. The main session plans, runs tasks with /run-task (implementer, then a reviewer on the other
   model, then merge), triages bugs, and talks to the user. No orchestrator.
2. Agents run with worktree isolation; nobody works in the main checkout; never `git stash`.
3. Status lives in GitHub labels. No document carries a status snapshot.
4. A question is not a request to edit files. Answer it; propose any fix and wait.
5. The Done-when is not negotiable by the implementer. It stops and reports; it never weakens an
   assertion, skips a test or edits its own task file. The main session amends a Done-when on
   `main` with the reason in the commit message.
6. Merge only with an approving review and green CI. Rework is capped at two rounds, then escalate.
7. Relay review findings in full, never a subset.
8. Every constant traces to evidence: a corpus entry, a report, or an investigation. A `[designed]`
   value says what was searched and came up empty.
9. A test proves behaviour only if it fails when the behaviour is removed (mutation, clean rebuild).
10. A comment asserting behaviour at an edge arrives with the test that visits that edge.
11. Until the first playable build, schedule only bugs that break play. Everything else is labelled
    `post-playable`.
12. A bug whose fix stays in the files it names and changes no rule's outcome is a `fix`; a
    blocking one-file mechanical fix may ride the PR that found it, declared under Scope. A bug that
    changes a rule's outcome becomes a task.
13. Design decisions go to the user; nothing else waits for the user.
14. At session start: `gh issue list --label triage:needed --state open` and any task in flight.

## Token economy
15. A brief carries the extracted task file, never a pointer to it.
16. Keep every file an agent must read under ~20k tokens; split data files and move blobs to
    sidecars. Scope searches to `src tests`; quiet terminal output; show diffs, not files.

## Conditional (delete what doesn't apply)
17. [research repo] Commit and push research-repo work without asking.
18. [seeded engine] No wall clock or unseeded random in gameplay paths; a seeded test proves it.
19. [original files outside CI] Tests needing them skip explicitly; CI fetches the fixtures repo.
20. [>1 machine] Claim a task with `machine:<name>` before dispatching; one primary triages.
```

### 4.2 process.md outline (under 150 lines)

```markdown
# Process

## 0. Order of work                                         (~12 lines)
- Phase A, evidence: decode the formats, decompile the core loop, write design.md and audit.md
  with [confirmed]/[derived]/[designed] tags. The research repo takes the reports.
- Phase B, walking skeleton: scaffolding + seams + ONE rule end to end + a CLI + a thin screen.
  Before any wide engine fan-out. (Here: T01–T03, T41, T47.)
- Phase C, rules fan-out behind the seams, highest-evidence first.
- Phase D, first playable: the UI chain goes first once the skeleton exists; rule 11 applies.
- Release tags mark capability jumps, not phases. Notes come from GitHub at cut time.

## 1. Roles                                                  (~12 lines)
Main session (plans, dispatches, triages, merges, talks to the user); implementer; reviewer
(the other model of Opus/Sonnet; Opus on one side for fidelity-critical or shared-model work);
researcher (evidence passes, research repo).

## 2. Task files                                             (~20 lines)
docs/tasks/T<nn>.md, 150–400 words: Kind (feature | correction | slice), Evidence links,
Owns (directories or files; finer only when two tasks run at once in one file), Scope,
Done when (each line one runnable check), Hazards, Model/effort, Reviewer. The main session
edits task files on main with a reason; the index is one table.

## 3. The loop (/run-task)                                  (~25 lines)
ready → implementer (worktree, pushes WIP, PR with DoD evidence) → reviewer (worktree at the
PR head) → approved + green CI → squash merge → follow-up issue if non-blocking findings →
unblock → report. Rework: full findings, same branch, at most two rounds.

## 4. What the reviewer checks                               (~30 lines)
0. HEAD and file list equal the PR's (paste them).
1. Re-run every DoD line.
2. Provenance: every constant traces; [designed] says what was searched.
3. [seeded] Determinism.
4. Every changed file is needed for the task; docs the change makes wrong are updated.
5. Sweep: tests that pass with the behaviour deleted (mutate, clean rebuild, re-take negatives);
   unreachable branches; edge comments without a test; project-grown classes (start with:
   delete-leaves-reference, integer truncation, off-by-one at caps).
Findings are proved or labelled unverified.

## 5. Bugs, follow-ups, triage                              (~15 lines)
Filed with triage:needed. Triage picks: fix | correction task | fold into a not-yet-ready task
| close with reason. The fix lane test: no ruleset key, corpus value, seeded measurement or golden
line changes. One review round for a fix.

## 6. Escalate to the user when                             (~8 lines)
A design question; a DoD would weaken; a third rework round; anything destructive; a new
external dependency.

## 7. Labels                                                 (~8 lines)
task, bug, fix, triage:needed, status:{ready,in-progress,in-review,rework,merged,blocked},
review-round:{1,2}, post-playable.

## 8. Evidence pipeline                                      (~10 lines)
Stage 1 writes the report to the research repo; stage 2 checks every claim the report touches
and routes each finding: fact → doc edit; defect → bug; undispatched task → task file edit;
design choice → the user.

## 9. Measure                                                (~5 lines)
Each PR body records the Agent results' token totals for its implementer and reviewer runs.
```

### 4.3 Not on day one, and what would justify each later

| Mechanism | Add it when |
| --- | --- |
| Line- or function-level Owns, never-in-flight constraints | Two tasks will run concurrently in the same file. |
| Plan PRs and their tiers | More than one person or session edits task contracts and a direct commit caused a conflict or a wrong DoD reached an implementer. |
| Cross-session or external reviewer | A defect class escaped the Claude reviewer twice, and a trial shows the other model catches it. |
| Two-machine claims | A second machine will actually run tasks this week. |
| Docs-only-after-merge rule | Concurrent tasks start conflicting in the same documents. |
| Wiki for living reference | A living page read by agents went stale twice. |
| Milestone review with freeze branches | The first public or packaged release. |
| A release-plan document | Packaging (1.0) needs gates beyond "a person can do X". |
| `phase:`, `lane:`, `model:`, `effort:`, `release:` labels | A second lane runs concurrently and a query needs them. |
| External implementer on cheap models (#482) | Its open points are settled and a trial matches Sonnet's review pass rate. |
| An orchestrator agent | Never, while execution is serial. |

---

## 5. Cost

### 5.1 What was measured

No per-task, per-agent or per-round token spend was ever measured. #264 and #269 are file sizes divided
by four:
- the catalogue at 84,809 tokens;
- the world at 99,897;
- one entry (T31) at 1,459;
- "~85,000 tokens per agent, per round" to reach the entry through a pointer;
- "~102,900 tokens" for the documented orientation chain, "past 220,000" with a ruleset and the world.

The operating guide adds one measured figure: a probe agent "cost about 54,000 tokens of startup
context". For scale, the read-only data-gathering agent this audit dispatched reported 180,069 tokens
over 37 tool calls.

### 5.2 A model of a task under this process

The ranges below are an estimate, not a measurement. They are built from those anchors and the PR
counts. The unit is tokens of context processed per agent run, summed. Billed input with caching is a
different number, and it is not measured either.

- **Agent runs per task: 3.5–5.** An implementer and a reviewer, plus about 0.82 × 2 for round 1 and
  about 0.4 × 2 for round 2. That gives ~4.4.
- **Fixed cost per run.**
  - After T61: ~55k for startup plus a 3–6k brief, so **~60k**.
  - Before T61: add up to ~85k for a pointer brief, so **~140–160k**.
- **Working cost per run: 100k–450k.** Reading code, building, running tests, writing. This is not
  measured; the range brackets the 180k calibration point.
- **Plan PRs.**
  - 127 over 84 tasks is ~1.5 per task.
  - Each costs the main session 20–60k.
  - About 40% of them, the contract tier after #479 plus the Opus-reviewed ones, add a reviewer run of
    80–150k.
  - That makes 60k–180k per task.
- **Main session per task: 40k–120k.** Dispatching, relaying, merging, the `Docs:` commit and the
  report.

| | Per task |
| --- | --- |
| This process, after T61 | **~0.7M–2.8M** tokens |
| This process, before T61 | **~1.1M–3.4M** |
| **Starter kit** | **~0.5M–2.2M** |

The starter kit's figure assumes:
- the same rework rate, which is the defect rate and should not be assumed away;
- a fixed cost of ~50k, from a shorter `CLAUDE.md` and brief;
- ~0.3 direct task-file commits per task, at 10–30k each;
- 30k–90k for the main session, with no `Docs:` sync.

### 5.3 What the estimate says

The per-task saving is modest, **about 15–30%**. Working tokens dominate, and they are the part that
buys defects caught. The larger saving is in **how many task-shaped units there are**:
- 42 of the 84 merged tasks were corrections by the catalogue's own list.
- Run as fixes, at one implementer and one reviewer round on Medium, a correction costs roughly half a
  task.
- If about 30 of them had qualified, that saves another **~15–20% of the total**.

The biggest saving is not a token saving at all. It is the order of work (§1.4).

**Totals to `v0.4.0`:**
- this process: 84 tasks × 0.7–2.8M, or **~60M–235M tokens**, plus the plan and triage overhead of
  sessions not tied to a task;
- the starter kit: roughly **~40M–150M** for the same scope.

### 5.4 What I could not measure

- **Actual agent token totals.** The transcripts are on the user's machine
  (`%USERPROFILE%\.claude\projects\…\subagents\agent-*.jsonl`), and the operating guide's `grep` recipe
  applies there.
- **Main-session token totals.**
- **Cached versus uncached input.**
- **The cost of the external-reviewer runs on OpenCode.**

The starter kit's §9 fixes this for the next project: record each Agent result's token total in the PR
body from the first task.

---

## 6. What is reusable as files

| File(s) | Verdict | What to change |
| --- | --- | --- |
| `/run-task` skill (build-process Appendix C, ~70 lines) | **Copy and adapt** | Keep steps 1–5 and the "Never" list verbatim; they are generic. Replace hand-built worktree paths with `isolation: "worktree"`. Remove the machine claim (step 0), the T16/T22 stop and the fix-lane substitution paragraph (move it to `process.md` §5). Keep "paste the extracted entry" and "check the where-I-worked block". |
| Implementer and reviewer templates (Appendices A and B, ~90 lines each) | **Rewrite, about 30 lines each** | Most of their length is Windows paths, worktree creation and this project's DoD shape. Keep: stop and report, no `git stash`, gate 0's SHA and file-list check, the mutation admissibility paragraph, and "paste the entry at the end". |
| `scripts/Invoke-OpenCodeWatched.ps1` (236 lines) | **Copy as is**, if OpenCode is used | Generic: stdin closed at EOF, a session-creation watchdog, a total timeout, and a UTF-8 read-back. The stdin discovery (#490) is worth keeping on its own. |
| `scripts/external-review.ps1` (302 lines) and `.opencode/agents/external-reviewer.md` | **Copy and adapt**, conditional (§4.3) | Paths, the model chain, label names. Keep the agent file's deny-list permission block exactly, and the guard against OpenCode's silent fallback to the full-permission agent found on #480. |
| `scripts/external-implement.ps1` (#482) | **Do not copy yet** | Not merged. Its open points, including the review chain reusing the implementer's model, are unresolved. |
| CI (`.github/workflows/ci.yml`, 177 lines) | **Copy and adapt** | The shape is reusable: build and test; fetch a private fixtures repository through a secret and set `IC2_FIXTURES_DIR`; a separate headless job for the engine host. Keep #477's fix: grep each scene's own exit line under `pipefail`, with a watchdog timeout, rather than trusting `--quit-after`. |
| `scripts/check-godot-churn.ps1` | **Copy as is**, if Godot | |
| Evidence pipeline (`evidence-pipeline.md`'s skill block) and `/parse-recording` (`recording-analysis.md`) | **Copy and adapt** | The two stages, the four routes, and "a recording needs no note" are generic. The prerequisites table and the paths are this project's. |
| The fixtures-release pattern | **Copy as is** (the pattern) | Four parts: GitHub Releases per play-through in a private repository; an evidence index mapping bare filenames to releases; a disposable `releases/<tag>/` cache with a fetch script; and a by-name resolver that CI points at a private fixtures repository. Carry #207's lesson: that repository holds the **whole** corpus, never a named subset, because sweep tests read by directory. |
| `docs/milestone-review.md` (128 lines) | **Copy as is** | Already written as a portable process. Use it at the first public release. |
| `tests/fixtures/corpus.json` and its provenance convention; the `[confirmed]`/`[derived]`/`[designed]` discipline | **Copy the convention; rewrite the content** | |
| PR template (82 lines), `build-task.yml` issue form | **Rewrite shorter** | Keep the fenced DoD-evidence block and a "Docs changed" list; drop the rest. |
| `build-process.md`, `operating-guide.md`, `release-plan.md`, the catalogue index | **Rewrite** as the §4 starter kit | Mine them for wording only. |
