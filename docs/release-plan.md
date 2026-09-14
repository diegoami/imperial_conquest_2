# Release plan: versioning, tags, and release notes for the 31-task build

This document says **when a version number changes, what it is called, who creates it, and what has to be true before it exists**. It sits alongside [game-design.md](game-design.md) (*what* is being built — 20 design milestones), [task-catalogue.md](task-catalogue.md) (the 31 tasks that build it, in 4 phases, with a critical path) and [build-process.md](build-process.md) (*how* — an implementer/reviewer/orchestrator pipeline over GitHub issues).

It **invents no new structure**. Every release gate below is a set of task issues from the [task index](task-catalogue.md#3-task-index); every human sign-off is one the [standing governance decisions](build-process.md#9-standing-governance-decisions) already reserve to the user; the reviewer of a release note is the pipeline's existing reviewer role, not a new one. Where this document *decides* something, it says so; where it only *recommends*, it says that too.

> **Two different things both called "milestone".** `game-design.md` has **design milestones** M1–M20 (subsystems). The build has four **GitHub build-phase milestones** (Phase 0 Foundation, Phase 1 Pure rules, Phase 2 Systems, Phase 3 Delivery). This document adds a third axis — **release versions** — and deliberately does **not** turn them into GitHub milestones; see [§6](#6-what-was-created-in-github-and-what-was-not).

Related reading, in order: [operating-guide.md](operating-guide.md) → [game-design.md](game-design.md) → [design-audit.md](design-audit.md) → [task-catalogue.md](task-catalogue.md) → [build-process.md](build-process.md) → this document. Live pipeline state: [tracking issue #29](https://github.com/diegoami/imperial_conquest_2/issues/29).

---

## 1. The versioning scheme

**SemVer 2.0.0, `v`-prefixed tags, pre-1.0 until the packaged build is playable end to end.**

### 1.1 What each position means — **DECIDED**

| Position | Pre-1.0 (now) | Post-1.0 |
| --- | --- | --- |
| **MAJOR** `0` → `1` | Bumps exactly once: the first packaged build a player can install and finish a game in. | A breaking change to the `classical-faithful` ruleset's *confirmed* behaviour, or a save file the previous MAJOR cannot load. |
| **MINOR** `0.x` | A new **user-visible capability** — something a person can do that they could not do at the previous tag. Also any change to a shipped preset's flag values or defaults. | New capability or preset, backwards-compatible saves. |
| **PATCH** `0.x.y` | Fixes only, to a defect the tag above it shipped. No new capability, no preset change. | Same. |

**No `-alpha.N` on the `0.x` tags.** SemVer §4 already says `0.y.z` means "anything may change at any time"; an `-alpha` suffix on top of a `0.x` tag repeats that and makes tags harder to sort and to read at a glance. The pre-release axis is reserved for the one place it earns its keep: **`-rc.N` candidates of `v1.0.0`**, cut when the packaging task has merged but a gate that is *not* a test — the human visual sign-off from [Q-B](build-process.md#9-standing-governance-decisions) — is still outstanding. That is a genuinely different state from "released", and it deserves a name.

### 1.2 What crosses each boundary — **DECIDED**

Stated explicitly, because "what makes this a MINOR rather than a PATCH" is exactly the question an autonomous orchestrator will get wrong:

- **A MINOR bump requires a new capability *reachable by a person*, or a preset change.** A merged task that only adds internal test coverage, refactors behind a seam, or fixes a constant is a PATCH, however large its diff. Conversely a one-line change to `data/rulesets/improved.json`'s `combat.onDefeat` scatter range is a **MINOR**, because it changes what a player experiences — see [§4.2](#42-what-a-release-note-must-contain).
- **`1.0.0` is crossed by packaging, not by feature count.** The engine is feature-complete at `v0.3.0`; `v1.0.0` is reached when [T27](task-catalogue.md#t27-packaging)'s export launches on a machine with no Godot and no .NET SDK on `PATH` and [T28](task-catalogue.md#t28-nightly-regression-and-soak-gate)'s nightly gate is green. "Playable" means *installable and finishable*, not *implemented*.
- **Pre-1.0, the save format and the ruleset schema may break at any MINOR.** This is safe rather than reckless only because [T20](task-catalogue.md#t20-new-format-saveload-and-versioning)'s DoD requires an unknown *future* save version to be rejected with a typed error and an older one to load through a migration — a broken save fails loudly, never silently best-effort parses. Every release note must state whether the previous tag's saves still load ([§4.2](#42-what-a-release-note-must-contain) item 6).

### 1.3 The save-format version is a separate axis

`SaveGame.schemaVersion` (T20) and the game version are **not** the same number and must never be tied together. A PATCH can never bump the schema; a MINOR may. The release note reports both.

### 1.4 Do phase completions get tags? — **DECIDED: no. Capability jumps only.**

Only the five cut points in [§2](#2-the-release-ladder) get a tag, and every one of them gets a GitHub Release. A phase boundary that is not also a capability jump gets nothing.

The justification is the build process's own rule ([build-process.md](build-process.md)): *"These documents are the intent. GitHub is the state."* Phase completion **already has a representation** — the phase's GitHub milestone closes and its issues all read `status:merged`. A `v0.0.x-phase0` tag would duplicate state GitHub already holds, on the one axis (git history) the process deliberately keeps free of progress tracking. Tags are reserved for the thing GitHub milestones *cannot* express: "here is a tree someone can go and use."

The two schemes only differ in three places, which is worth knowing before disagreeing with the decision:

| Phase boundary | Tagged? | Why |
| --- | --- | --- |
| Phase 0 complete (T01–T05, T29–T31) | **No** | Nothing runs. A placeholder test passing is not a release. |
| Phase 1 complete (T06–T12) | **Yes** — `v0.1.0` | Coincides with a real jump: the confirmed rules become executable. |
| Phase 2 complete (T13–T22) | **Yes** — `v0.3.0`, *plus* T23 | Phase 2 alone still has no runnable program; the CLI (T23, Phase 3) is what makes it usable, so the tag waits one task. |
| Phase 3 complete (T23–T28) | **Yes** — `v1.0.0` | The packaged build. |
| *Mid-Phase 2* (T13–T17 merged) | **Yes** — `v0.2.0` | Not a phase boundary at all, but the largest fidelity jump in the build: naval + battle + capture. Waiting for all of Phase 2 would hide it behind the AI, which has the most uncertain duration of any task. |

So: three of five releases land on a phase boundary, one lands a task past one, and one lands mid-phase — which is the argument for tying tags to capability rather than to phase in the first place.

---

## 2. The release ladder

Five releases, anchored to merged task issues, not to dates — this project has no calendar, only dependency order ([task-catalogue.md §1.1](task-catalogue.md#11-waves-and-the-critical-path)).

| Tag | Gate: all of these `status:merged` | Design milestones complete | What a user can actually do |
| --- | --- | --- | --- |
| **`v0.1.0`** *The rules run* | #1–#12, #32, #37, #45 (T01–T12, T29, T30, T31) — Phase 0 + Phase 1 | M1, M2, M3, M5, M6, M13; M17's buffer | **Nothing a player can do.** A developer clones, runs `dotnet test`, and watches the original's confirmed economy, calendar, movement, strength and victory numbers reproduce from the fixtures corpus. |
| **`v0.2.0`** *A war is simulable* | + #13–#17 (T13–T17) | + M4, M7, M8, M9, M14 | Still no runnable program. A developer can script a fixture in which an army is recruited, sails, fights a field/naval/siege battle under either preset, and takes a city with the defection cascade firing. |
| **`v0.3.0`** *Headless playable* | + #18–#23 (T18–T23) — Phase 2 complete + the CLI | + M10, M11, M12, M15, M16, M18 (headless half), M17 | **First downloadable thing anyone can run.** `IC2.Cli` loads a scenario, issues one order of every type, ends turns, and an all-AI toy scenario runs to a victory condition. Native saves round-trip; an original `.sav` imports (locally, with `assets.local.ini`). No graphics. |
| **`v0.4.0`** *Playable with a UI, from source* | + #24, #25, #26 (T24–T26) | + M18 (UI half), M19 | Launch the Godot project **from source** (needs Godot 4.7.2 + .NET 10 SDK), pick `Classical Faithful` or `Improved` at New Game, play the map with the contextual panel, news log, battle-result, diplomacy and hotseat-handoff screens. |
| **`v1.0.0`** *First packaged playable release* | + #27, #28 (T27, T28) — everything | **All 20** (M1–M20) | Download an export, launch it on a machine with no dev toolchain, and play a scenario end to end to a victory condition. |

Notes on the gates:

- **`v0.2.0` deliberately stops at T17.** T16 (battle resolution) and T14 (naval) are on the critical path and gate six downstream tasks between them; T17 is the first task that consumes T16 and proves it against a real recorded event (the Galatia elimination, city by city). That is the natural place to stop and write down what fidelity is now proven.
- **`v0.3.0` includes T23 from Phase 3** because Phase 2's close leaves the engine complete but unreachable. T23's `merge-after` is only T17 and T19, so it is available well before T22 (AI) lands; the tag waits for both.
- **T21 (original-save import) is `local-only`.** Its tests skip explicitly on a machine without the user's files (T21 DoD 4), so `v0.3.0` can be cut from a clean CI-green tree; the release note must say the import path was verified locally, by whom, and against which saves.
- **`v1.0.0` needs a green *scheduled* nightly run**, not just a green manual dispatch — a workflow that only ever ran on demand has not demonstrated it runs.

### 2.1 Gate progress

A snapshot written by the documentation step ([build-process.md §4.8](build-process.md#48-documentation-update-after-every-merge), location A4), using the same status values as the [task index](task-catalogue.md#3-task-index); GitHub's `release:*` labels are authoritative.

| Tag | Gate tasks | Status (as of `acd4098`) |
| --- | --- | --- |
| `v0.1.0` | T01–T12, T29, T30, T31 (15) | **9 merged**: T01, T02, T03, T04, T05, T06, T07, T30, T31. Blocked: T08 — suspended on #50. Ready: T09, T10, T11, T12, T29. |
| `v0.2.0` | T13–T17 (5) | 0 merged. Blocked: T13, T14, T15, T16, T17. |
| `v0.3.0` | T18–T23 (6) | 0 merged. Blocked: T18, T19, T20, T21, T22, T23. |
| `v0.4.0` | T24, T25, T26 (3) | 0 merged. Blocked: T24, T25, T26. |
| `v1.0.0` | T27, T28 (2) | 0 merged. Blocked: T27, T28. |

### 2.2 Patch releases

Cut a `v0.x.y` only when something merged after a tag **fixes a defect that tag shipped** — a wrong constant reaching `main` is the failure mode this project has already hit ([design-audit.md §2](design-audit.md), T31), and it deserves its own tag so "which build had the bad number" is answerable. Ordinary forward progress toward the next capability jump is **not** a patch release; it is untagged commits on `main`.

### 2.3 What is explicitly *not* in this ladder

Nothing here commits to scope beyond `game-design.md`. The tactical/animated battle screen, the in-game scenario editor, network multiplayer and further asset packs are all in that document's "Open questions genuinely left for later" — they are **post-1.0 MINOR candidates**, not gates on any tag above, and no release note should imply otherwise.

---

## 3. Tag and GitHub Release conventions

### 3.1 Naming and placement — **DECIDED**

| Thing | Convention |
| --- | --- |
| Tag name | `v<MAJOR>.<MINOR>.<PATCH>` with optional `-rc.<N>`; must match `^v\d+\.\d+\.\d+(-rc\.\d+)?$` |
| Tag kind | **Annotated** (`git tag -a`), never lightweight — the message carries the gate (the issue numbers) so `git show <tag>` explains itself |
| Tag message subject | `v0.3.0 — Headless playable (closes #18–#23)` |
| Where | **`main` only**, always on a squash-merge commit — never on a task branch, never on a rebase artifact |
| GitHub Release | One per tag, always; title = the tag plus the ladder's short name (`v0.3.0 — Headless playable`); body = [§4](#4-release-notes) |
| Pre-1.0 marker | Every `0.x` release and every `-rc` is published with GitHub's **pre-release** flag set; only `v1.0.0` is a full release |
| Moving a tag | **Never.** Re-pointing a published tag is a destructive git operation under [build-process.md §4.6](build-process.md#46-when-to-escalate-to-the-human) case 10. A mistake is corrected by a new PATCH tag. |

### 3.2 Who cuts the tag — **RECOMMENDED, consistent with Q-A**

[Q-A](build-process.md#9-standing-governance-decisions) already granted the orchestrator full merge autonomy **except** the four architecture PRs (T02, T03, T16, T22), and [Q-B](build-process.md#9-standing-governance-decisions) reserved *visual* judgment to the user. The consistent extension:

| Release | Tag + draft Release | Publish |
| --- | --- | --- |
| `v0.1.0`, `v0.2.0`, `v0.3.0` | Orchestrator, autonomously | Orchestrator, autonomously |
| `v0.4.0`, `v1.0.0` (and any `-rc`) | Orchestrator, autonomously, as a **draft** | **Human**, after the visual sign-off Q-B already requires |

The reasoning is that a tag is a *consequence* of merges, not a new decision. Every merge in a `0.1`–`0.3` gate was one the orchestrator was already authorized to make; refusing it the tag would add a human gate without adding a human judgment. `v0.4.0` and `v1.0.0` are different in kind: their gating tasks (T24, T25, T27) each carry "**+ human visual review**" in the task catalogue, so a person is in the loop *anyway* — the release simply inherits that gate rather than inventing a second one.

Worth noticing: **every release in the ladder already has a human touchpoint upstream of it**, with no new gate invented. `v0.1.0` inherits the T02/T03 architecture thumbs-up; `v0.2.0` inherits T16's; `v0.3.0` inherits T22's; `v0.4.0` and `v1.0.0` inherit the Q-B screenshot reviews.

### 3.3 How this interacts with branch-per-task and squash-merge

[build-process.md §6](build-process.md#6-git-and-github-conventions) gives `main` a linear history of squash-merges, one per task. Tagging slots into that cleanly:

1. **The tag is cut inside the same `/build-tick` that merges the last gating task**, on that task's squash-merge commit, before the tick's report step ([build-process.md Appendix C](build-process.md#appendix-c-the-build-tick-skill) step 7) — *not* as a separate later pass. The documentation update that follows the merge (step 6) is docs-only and lands after the tag.
2. **Dispatch (step 5) is skipped for that tick.** No other PR is merged between the last gating merge and the tag, so the tagged tree is exactly the tree the release checklist was run against. This costs at most one tick of idle time and removes the entire class of "the tag has a commit nobody tested" bug.
3. **No release branches.** Pre-1.0 with one machine and one orchestrator, a release branch would only create a second place for a fix to land and a merge-back to forget. If a `v1.0.x` line ever has to be maintained while `v1.1` develops, that is the moment to add one — not before.
4. **A tag is never cut while a task branch is mid-rebase or a conflict is unresolved** ([build-process.md §5.4](build-process.md#54-merge-conflicts)), because merge order is dependency order and a conflicting branch means `main` is about to move for a reason the checklist did not see.

---

## 4. Release notes

### 4.1 Where the note comes from — **DECIDED: generated at cut time from GitHub. No `CHANGELOG.md`.**

A running `CHANGELOG.md` on `main` is rejected for the same reason [build-process.md §2.3](build-process.md#2-how-the-build-avoids-conflicts) rejects every other shared registry file: a file every task wants to append to conflicts with every task branch, and working around that by making the orchestrator the only writer just moves the same information into a second place that can drift from GitHub. GitHub is already the state.

The note is **generated at cut time** from four sources, in this order:

| Section | Source | Command shape |
| --- | --- | --- |
| What landed | Merged PR titles since the previous tag — already canonical, since build-process.md §6 mandates `T09 Movement and terrain` | `gh pr list --state merged --base main --json number,title,closingIssuesReferences` |
| Which tasks closed | Issues closed in the range, with their `phase:`/`lane:`/`release:` labels | `gh issue list --state closed --label task --label release:v0.3.0` |
| Preset table | The shipped ruleset JSON itself, plus each field's `_provenance` | read `data/rulesets/*.json` — **generated, never hand-written** |
| Known gaps | Design milestones whose tasks are not all `status:merged` | derived from the [task index](task-catalogue.md#3-task-index)'s Design-M column crossed with issue state |

The commit range (`git log <prev-tag>..HEAD --oneline`) is the cross-check, not the source: The `T09: <subject>` commit subjects make every commit traceable to a task, so a commit on `main` with no task prefix in a release range is itself a finding.

### 4.2 What a release note must contain

Eight required sections. An agent drafting a note that omits one has not finished.

1. **Header** — tag, previous tag, the gate (issue numbers), and the ladder's one-line "what a user can actually do" statement, verbatim from [§2](#2-the-release-ladder).
2. **Rulesets and presets** — the whole point of this section is that `classical-faithful` vs `improved` is a **player-facing choice that will keep evolving** (`game-design.md` §"Two shipped presets"). Required: which presets ship; **which is the New Game default** (`classical-faithful`, per that section); and a table of every ruleset flag with its value under each preset. Regenerated from `data/rulesets/*.json` at cut time, so it cannot go stale. As of this writing that table is `diplomacy.model`, `economy.purses`, `victory.default`, `seatAsymmetry`, `bugPolicy.diplomaticThaw`, `combat.onDefeat`, plus the per-task flags the catalogue adds (`faithfulThawColumnBug`, T18's `cityOrders` table) — but the note reports what the files say, never this list.
3. **Newly playable `[designed]` mechanics** — anything tagged `[designed, no original analogue]` that a player can now encounter, named as such, with its placeholder-constant status. The first of these is the `improved` preset's **`combat.onDefeat` scatter** (T16): the loser survives at a mirrored casualty ratio and relocates 2–4 tiles away with its moves zeroed. `game-design.md` is explicit that its survivor fraction and scatter range are documented placeholders meant to be retuned from play, not derived from the original — the release note is where a player is told that, so feedback comes back as tuning rather than as a bug report.
4. **Known gaps vs `game-design.md`** — which of M1–M20 are not complete, each with the issue number that will close it. Plus the still-open evidence items the shipped rulesets carry as `[open]` `_provenance`: `design-audit.md` Q9's paid case (where a foreign supply purchase's talents go), and anything else [operating-guide.md §7](operating-guide.md#7-whats-still-open) lists that a shipped rule depends on.
5. **Fidelity statement** — which golden fixtures from real play pass ([`full-battle-resolution-rome-vs-gaul.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/full-battle-resolution-rome-vs-gaul.md), [`battle-recording-melee-cap-confirmed.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/battle-recording-melee-cap-confirmed.md), [`army-to-army-transfer-confirmed.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/army-to-army-transfer-confirmed.md), [`galatia-elimination-and-city-resupply-confirmed.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/galatia-elimination-and-city-resupply-confirmed.md), as each becomes applicable), the determinism guard's status, and from `v0.3.0` the 50-seed AI soak result. This is the section that distinguishes this project from a generic reimplementation and it should read as evidence, not as a claim.
6. **Compatibility** — `SaveGame.schemaVersion`; whether the previous tag's saves load; whether original-`.sav` import still targets `classical-mediterranean` + `classical-faithful` only (it does, by policy).
7. **Artifacts and how to run them** — what is attached, the runtime prerequisites, and the local-only caveats (original-save import needs `assets.local.ini`; before `v1.0.0`, a Godot install).
8. **Open escalations** — any issue still labelled `status:escalated` or `needs-human` at cut time. A release that silently omits a known escalation is the same failure mode [build-process.md §4.4](build-process.md#44-the-dod-is-not-negotiable-by-an-agent) exists to prevent.

Two standing prohibitions, inherited from the project's evidence rules: **no number in a release note that is not in the fixtures corpus or a cited report**, and **no claim that a mechanic is faithful unless the note can name the report**. `[designed]` stays visibly labelled all the way out to the player.

### 4.3 Who reviews it — **the existing reviewer role, on the draft Release body**

No new role. The pipeline's roles ([build-process.md §3.1](build-process.md#31-the-roles)) already cover it, and the review contract in [§4.2](build-process.md#42-what-the-reviewer-checks) transfers to a release note almost unchanged:

| Review gate | Applied to a release note |
| --- | --- |
| 1. DoD independently reproduced | The reviewer re-runs the [§5](#5-release-checklist) checklist commands itself, in the main checkout at the tag (nothing else is dispatched that tick). The draft's claims are a convenience, never the proof. |
| 2. Provenance | Every number and fidelity claim in the note traces to the fixtures corpus or a cited report; every `[designed]` mechanic is labelled as one. |
| 3. Determinism | The determinism guard and the seeded-soak results are quoted from an actual run, not asserted. |
| 4. Scope | The note describes only what merged in the range — no forward promises, nothing from `game-design.md`'s "left for later" list. |
| 5. Correctness sweep | Not applicable; a release note has no code. |

**Model for the seat**: the same rule the catalogue uses for fidelity-critical PRs — **Opus / Medium**, because the failure mode here (a wrong fidelity claim shipped to a player) is the one this project has already paid for. The reviewer approves by commenting on the draft Release or on the tracking issue; the orchestrator publishes (or, for `v0.4.0`/`v1.0.0`, hands to the human). This is the same *judge separate from executor* separation [build-process.md §4.3](build-process.md#43-is-a-third-agent-needed-to-merge) already argues for on merges, for the same audit-trail reason.

---

## 5. Release checklist

The concrete "done when" for cutting a release, in the task catalogue's style: **one line, one runnable check**, with human sign-off only where a machine genuinely cannot decide. Lines marked *(from vX)* apply only from that tag onward.

**Done when:**

1. Every issue in the tag's gate set ([§2](#2-the-release-ladder)) is closed and labelled `status:merged` — `gh issue list --label release:<tag> --json number,state,labels` shows no exception.
2. No issue anywhere is labelled `status:escalated`, or the note's §8 lists each one with a reason it does not block.
3. No PR is open against `main` with `status:approved` (nothing merge-ready is being left out of the tag by accident).
4. The latest CI run on `main`'s tip is `success` — `gh run list --branch main --limit 1 --json conclusion`.
5. A **fresh clone** of `main`'s tip builds with zero warnings in the new projects and all tests pass: `dotnet build IC2.sln` then `dotnet test IC2.sln`. The working tree is not evidence; it has local state.
6. The determinism guard (T03 DoD 4) and the fixtures-corpus tests (T04 DoD 1–4) are green in that run, named individually in the output.
7. *(from v0.3.0)* The committed CLI golden transcript (T23 DoD) reproduces **byte-exactly** from the fresh clone.
8. *(from v0.3.0)* The 50-seed AI soak (T22 DoD 1–2) completes inside its stated wall-clock budget with zero stalls, zero rejected commands, zero exceptions.
9. *(from v0.3.0)* The original-save import (T21) was run **locally** against the 3–4 representative saves, or the note states it was skipped and why — a CI skip is not a pass.
10. *(from v0.4.0)* The headless Godot script exits 0 and `scripts/check-godot-churn.ps1` reports a clean tree afterwards (T24 DoD 1, 3).
11. *(v1.0.0 only)* The packaging script's export launches and loads a scenario from a shell with scrubbed `PATH`/`DOTNET_ROOT`, exit 0 (T27 DoD).
12. *(v1.0.0 only)* The nightly workflow (T28) has a green **scheduled** run within the last 24 hours, not merely a green manual dispatch.
13. *(v1.0.0 only)* The packaged artifact contains **no file originating from the user's original installation** — no `.exe`/`.dat`/`.hlp`/`.cnt`/`.wav`/`.sav` from `imp_conq_original`, asserted by a manifest scan of the export, not by inspection. This is the project's oldest standing constraint and the one release step where a mistake is public and irreversible.
14. The release note's preset table was **regenerated** from `data/rulesets/*.json` and diffs clean against the draft — the table is never typed by hand.
15. The known-gaps section lists exactly the design milestones whose tasks are not all `status:merged`, derived mechanically from GitHub, not written from memory.
16. The reviewer agent ([§4.3](#43-who-reviews-it--the-existing-reviewer-role-on-the-draft-release-body)) has re-run items 4–13 itself and approved the draft.
17. The tag is annotated, on `main`, on a squash-merge commit, matches the name pattern, and does not already exist — `git tag -l <tag>` is empty before `git tag -a`.
18. **Human** *(v0.4.0, v1.0.0 only)*: visual sign-off given on every screenshot posted by T24/T25/T27, per [Q-B](build-process.md#9-standing-governance-decisions).
19. **Human** *(v0.4.0, v1.0.0 only)*: the draft Release is published by the user. For `v0.1.0`–`v0.3.0`, the orchestrator publishes (see [§3.2](#32-who-cuts-the-tag--recommended-consistent-with-q-a)).

Items 1–17 are checkable by an agent. Items 18–19 are the only human steps, and neither is new — both are [Q-B](build-process.md#9-standing-governance-decisions)'s existing answer applied at the release boundary.

**If a check fails**, the release does not get cut and nothing is tagged. Fix forward on `main` and re-run the checklist; a failed checklist is never worked around by weakening a line, for exactly the reason [build-process.md §4.4](build-process.md#44-the-dod-is-not-negotiable-by-an-agent) gives about DoDs.

---

## 6. What was created in GitHub, and what was not

**Created: five `release:*` labels**, applied one per task issue — the first release whose gate includes it.

| Label | Applied to |
| --- | --- |
| `release:v0.1.0` | #1–#12, #32 (T29), #37 (T30), #45 (T31) |
| `release:v0.2.0` | #13–#17 |
| `release:v0.3.0` | #18–#23 |
| `release:v0.4.0` | #24, #25, #26 |
| `release:v1.0.0` | #27, #28 |

This makes checklist items 1 and 15 a single `gh issue list` query instead of a hand-maintained list, it is additive and reversible like every other label, and it does not disturb anything the orchestrator relies on.

**Deliberately *not* created: GitHub milestones for the version tags.** A GitHub issue carries **exactly one** milestone, and task issues use theirs for the build **phase**, which [build-process.md §6](build-process.md#6-git-and-github-conventions) names as a convention. A version milestone could therefore only be empty — permanently 0/0, sitting in the milestone list next to the four meaningful phase ones and inviting exactly the phase-vs-version confusion this document opens by warning about — or it could displace a phase milestone, which would break a documented convention to gain nothing. Labels are multi-valued; milestones are not; the gate is a set, so it is a label. If a future release ever needs its own *new* issues (a `v1.0.1` bugfix batch, say), a milestone for that batch is the right tool at that time.

**No GitHub Release exists yet.** The first is drafted when [§2](#2-the-release-ladder)'s `v0.1.0` gate is met.

---

## 7. Summary

| | |
| --- | --- |
| Scheme | SemVer 2.0.0, `v`-prefixed, annotated tags on `main` only; `0.x` through the build, `-rc.N` only ahead of `v1.0.0` |
| Tags | Five: `v0.1.0`, `v0.2.0`, `v0.3.0`, `v0.4.0`, `v1.0.0` — at capability jumps, **not** at phase boundaries |
| Gate | A set of merged task issues, tracked by a `release:*` label; never a date |
| Cut by | The orchestrator, inside the tick that merges the last gating task, with dispatch paused for that tick |
| Published by | The orchestrator for `v0.1.0`–`v0.3.0`; the **human** for `v0.4.0` and `v1.0.0`, inheriting Q-B's visual sign-off |
| Notes | Generated at cut time from GitHub + the shipped ruleset JSON; no `CHANGELOG.md`; eight required sections, presets and `[designed]` mechanics among them |
| Reviewed by | The existing reviewer role (Opus / Medium), applying build-process.md §4.2's gates to the draft Release body |
| Blocking constraint | 19 checklist lines; 17 agent-checkable, 2 human, none of them new |
