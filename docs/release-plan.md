# Release plan: versioning, tags, and release notes

This document says **when a version number changes, what it is called, who creates it, and what has to be true before it exists**. It sits alongside [game-design.md](game-design.md) (*what* is being built — 20 design milestones), [task-catalogue.md](task-catalogue.md) (the tasks that build it, in 4 phases, with a critical path) and [build-process.md](build-process.md) (*how* — an implementer/reviewer pipeline over GitHub issues).

It **invents no new structure**. Every release gate below is a set of task issues from the [task index](task-catalogue.md#3-task-index); every human sign-off is one the [standing governance decisions](build-process.md#9-standing-governance-decisions) already reserve to the user; the reviewer of a release note is the pipeline's existing reviewer role, not a new one. Where this document *decides* something, it says so; where it only *recommends*, it says that too.

> **Two different things both called "milestone".** `game-design.md` has **design milestones** M1–M21 (subsystems). The build has four **GitHub build-phase milestones** (Phase 0 Foundation, Phase 1 Pure rules, Phase 2 Systems, Phase 3 Delivery). This document adds a third axis — **release versions** — and deliberately does **not** turn them into GitHub milestones; see [§6](#6-what-was-created-in-github-and-what-was-not).

Related reading, in order: [operating-guide.md](operating-guide.md) → [game-design.md](game-design.md) → [design-audit.md](design-audit.md) → [task-catalogue.md](task-catalogue.md) → [build-process.md](build-process.md) → this document. Live pipeline state: the task issues' labels ([operating-guide.md §1](https://github.com/diegoami/imperial_conquest_2/wiki/Where-the-build-stands)).

---

## 1. The versioning scheme

**SemVer 2.0.0, `v`-prefixed tags, pre-1.0 until the packaged build is playable end to end.**

### 1.1 What each position means — **DECIDED**

| Position | Pre-1.0 (now) | Post-1.0 |
| --- | --- | --- |
| **MAJOR** `0` → `1` | Bumps exactly once: the first packaged build a player can install and finish a game in. | A breaking change to the `classical-faithful` ruleset's *confirmed* behaviour, or a save file the previous MAJOR cannot load. |
| **MINOR** `0.x` | A new **user-visible capability** — something a person can do that they could not do at the previous tag. Also any change to a shipped preset's flag values or defaults. | New capability or preset, backwards-compatible saves. |
| **PATCH** `0.x.y` | Fixes to a defect the tag above it shipped, **and small changes the user asks for after playing it**: UI tweaks, wording, or how an existing rule is presented (the user's decision of 2026-10-04). No new capability, no change to a shipped preset's rules. | The same definition, unchanged after `1.0`: defect fixes and small changes the user asks for after playing, no new capability, no change to a shipped preset's rules (the user's decision of 2026-10-04 is not limited to pre-1.0). |

**No `-alpha.N` on the `0.x` tags.** SemVer §4 already says `0.y.z` means "anything may change at any time"; an `-alpha` suffix on top of a `0.x` tag repeats that and makes tags harder to sort and to read at a glance. The pre-release axis is reserved for the one place it earns its keep: **`-rc.N` candidates of `v1.0.0`**, cut when the packaging task has merged but a gate that is *not* a test — the human visual sign-off from [Q-B](build-process.md#9-standing-governance-decisions) — is still outstanding. That is a genuinely different state from "released", and it deserves a name.

### 1.2 What crosses each boundary — **DECIDED**

Stated explicitly, because "what makes this a MINOR rather than a PATCH" is exactly the question an agent running the build autonomously will get wrong:

- **A MINOR bump requires a new capability *reachable by a person*, or a preset change.** A merged task that only adds internal test coverage, refactors behind a seam, or fixes a constant is a PATCH, however large its diff. So is a small change the user asks for after playing a release, such as a UI tweak, a wording change, or a change to how an existing rule is presented, as long as it adds no capability and leaves every shipped preset's rules as they were (the user's decision of 2026-10-04). Conversely a one-line change to `data/rulesets/improved.json`'s `combat.onDefeat` scatter range is a **MINOR**, because it changes what a player experiences — see [§4.2](#42-what-a-release-note-must-contain).
- **`1.0.0` is crossed by packaging, not by feature count.** The strategic engine was complete at `v0.3.0`, and `v0.5.0` adds the tactical battle ([#496](https://github.com/diegoami/imperial_conquest_2/issues/496)); `v1.0.0` is reached when [T27](task-catalogue.md#t27-packaging)'s export launches on a machine with no Godot and no .NET SDK on `PATH` and [T28](task-catalogue.md#t28-nightly-regression-and-soak-gate)'s nightly gate is green. "Playable" means *installable and finishable*, not *implemented*.
- **Pre-1.0, the save format and the ruleset schema may break at any MINOR.** This is safe rather than reckless only because [T20](task-catalogue.md#t20-new-format-saveload-and-versioning)'s DoD requires an unknown *future* save version to be rejected with a typed error and an older one to load through a migration — a broken save fails loudly, never silently best-effort parses. Every release note must state whether the previous tag's saves still load ([§4.2](#42-what-a-release-note-must-contain) item 6).

### 1.3 The save-format version is a separate axis

`SaveGame.schemaVersion` (T20) and the game version are **not** the same number and must never be tied together. A PATCH can never bump the schema; a MINOR may. The release note reports both.

### 1.4 Do phase completions get tags? — **DECIDED: no. Capability jumps only.**

Only the six cut points in [§2](#2-the-release-ladder), and the patch releases between them ([§2.2](#22-patch-releases)), get a tag, and every one of them gets a GitHub Release. A phase boundary that is not also a capability jump gets nothing.

The justification is the build process's own rule ([build-process.md](build-process.md)): *"These documents are the intent. GitHub is the state."* Phase completion **already has a representation** — the phase's GitHub milestone closes and its issues all read `status:merged`. A `v0.0.x-phase0` tag would duplicate state GitHub already holds, on the one axis (git history) the process deliberately keeps free of progress tracking. Tags are reserved for the thing GitHub milestones *cannot* express: "here is a tree someone can go and use."

The two schemes only differ in three places, which is worth knowing before disagreeing with the decision:

| Phase boundary | Tagged? | Why |
| --- | --- | --- |
| Phase 0 complete (T01–T05, T30, T31) | **No** | Nothing runs. A placeholder test passing is not a release. |
| Phase 1 complete (T06–T12, T32–T34) | **Yes** — `v0.1.0` | Coincides with a real jump: the confirmed rules become executable. |
| Phase 2 complete (T13–T22, T29, T35, T36, T37) | **Yes** — `v0.3.0`, *plus* T23 | Phase 2 alone still has no runnable program; the CLI (T23, Phase 3) is what makes it usable, so the tag waits one task. |
| Phase 3 complete (T23–T28) | **Yes** — `v1.0.0` | The packaged build. |
| *Mid-Phase 2* (T13–T17, T35, T38 and T39 merged) | **Yes** — `v0.2.0` | Not a phase boundary at all, but the largest fidelity jump in the build: naval + battle + capture. Waiting for all of Phase 2 would hide it behind the AI, which has the most uncertain duration of any task. |

So: three of six releases land on a phase boundary, one lands a task past one, one lands mid-phase, and one (`v0.5.0`, the tactical battle) is a capability added after the phases — which is the argument for tying tags to capability rather than to phase in the first place.

---

## 2. The release ladder

Six releases, anchored to merged task issues, not to dates — this project has no calendar, only dependency order ([task-catalogue.md §1.1](task-catalogue.md#11-waves-and-the-critical-path)).

| Tag | Gate: all of these `status:merged` | Design milestones complete | What a user can actually do |
| --- | --- | --- | --- |
| **`v0.1.0`** *The rules run* | #1–#12, #37, #45 and the issues of T32–T34 and T40–T43 (T01–T12, T30–T34, T40–T43) — Phase 0 + Phase 1 | M1, M2, M3, M5, M6, M13; M17's buffer; an early slice of M18 | **A text demo on the toy world.** A developer clones and runs `dotnet test`, which reproduces the original's confirmed economy, calendar, movement, strength and victory numbers from the fixtures corpus. They can also run T41's `IC2.Cli` demo: move armies, buy supply, end turns across a season, and read the news log. There are no battles, capture, recruitment, diplomacy or AI yet. |
| **`v0.2.0`** *A war is simulable* | + #13–#17, #63, #78 and #81 (T13–T17, T35, T38, T39) | + M4, M7, M8, M9, M14 | Still no runnable program. A developer can script a fixture in which an army is recruited, sails, fights a field/naval/siege battle under either preset, and takes a city with the defection cascade firing. |
| **`v0.3.0`** *Headless playable* | + #18–#23, #32, and T36's and T37's issues (T18–T23, T29, T36, T37) — Phase 2 complete + the CLI | + M10, M11, M12, M15, M16, M18 (headless half), M17 | **First downloadable thing anyone can run.** `IC2.Cli` loads a scenario, issues one order of every type, ends turns, and an all-AI toy scenario runs to a victory condition **or its stated turn cap**, matching T22's settled Done-when 2 (reworded 2026-09-23 on the user's decision on [#267](https://github.com/diegoami/imperial_conquest_2/issues/267): a siege-scaled soak world comes after `v0.3.0`, and #267 stays open for it). Native saves round-trip; an original `.sav` imports (locally, with `assets.local.ini`); the exported `classical-mediterranean` world and both preset rulesets ship. No graphics. |
| **`v0.4.0`** *Playable with a UI, from source* | + #24, #25, #26, #469 (T24–T26, T95; T24 without the follow-ups split into T94 on 2026-09-28) | + M18 (UI half), M19 | Launch the Godot project **from source** (needs Godot 4.7.2 + .NET 10 SDK), pick `Classical Faithful` or `Improved` at New Game, play the map with the contextual panel, news log, battle-result, diplomacy and hotseat-handoff screens. |
| **`v0.5.0`** *Battles* | + #635 (T116) and the issues of T122–T130 (#663 … #670, #674) | + M21 | A field battle with a human side opens the original's **tactical battle**: place the army on a 14 × 12 board, move, shoot and fight half-round by half-round against the original's own computer general (or let *Computer general* play a side), surrender, and read the original's *Battle ended* window; the survivors, promotions, money, supplies and unity are written back as the original does. An AI seat's attack on a human's army opens the battle during the AI phase, as in the original. A battle with no human side still resolves at once, unchanged, and a player can choose instant resolve for its own battles too. A battle can be saved and reloaded: under `classical-faithful` at any point, resuming at the side to move's half-round setup as the original does; under `improved` at the start of a half-round, resuming exactly. `improved` also scatters a beaten army after retreat losses, fixes the original's code quirks, caps the battle's length and drops the slow advance. Pauses between exchanges are a display setting. The port is checked against the original's recorded battles half-round by half-round (the user's decisions of 2026-10-04 on [#496](https://github.com/diegoami/imperial_conquest_2/issues/496)). |
| **`v1.0.0`** *First packaged playable release* | + #27, #28, #464 (T27, T28, T94) — everything in the gates above, `v0.5.0`'s included | **All 21** (M1–M21) | Download an export, launch it on a machine with no dev toolchain, and play a scenario end to end to a victory condition. |

Notes on the gates:

- **`v0.2.0` deliberately stops at T17.** T16 (battle resolution) and T14 (naval) are on the critical path and gate six downstream tasks between them; T17 is the first task that consumes T16 and proves it against a real recorded event (the Galatia elimination, city by city). That is the natural place to stop and write down what fidelity is now proven.
- **`v0.3.0` includes T23 from Phase 3** because Phase 2's close leaves the engine complete but unreachable. T23's `merge-after` is only T17 and T19, so it is available well before T22 (AI) lands; the tag waits for both.
- **T21 (original-save import) is `local-only`.** Its tests skip explicitly on a machine without the user's files (T21 DoD 4), so `v0.3.0` can be cut from a clean CI-green tree; the release note must say the import path was verified locally, by whom, and against which saves.
- **`v0.5.0` waits for T116 as well as the battle's own tasks** (the user's decision of 2026-10-04 on [#496](https://github.com/diegoami/imperial_conquest_2/issues/496): T116 ships first, unchanged). Its gate includes the golden master (T129), by the same decisions; its recorded battles are the probe's and the sweep's (research `dee8150`). T130, `improved`'s tactical rules, is in the gate too: a preset change after the tag would need a MINOR of its own (§1.2), and the user has decided what `improved` changes.
- **`v1.0.0` needs a green *scheduled* nightly run**, not just a green manual dispatch — a workflow that only ever ran on demand has not demonstrated it runs.

### 2.1 Gate progress

Not kept in this document; GitHub's `release:*` labels are the record. To see a gate:

```bash
gh issue list --label task --label release:v0.2.0 --state all --json number,title,labels --jq '.[] | "\(.number)\t\(.title)\t\([.labels[].name | select(startswith("status:"))] | join(","))"'
```

A gate is met when every issue it lists is closed as `status:merged`.

### 2.2 Patch releases

Patch releases are **regular**, not exceptional (the user's decision of 2026-10-04): the user plays each release, finds things to fix or change, and expects them back as `v0.4.1`, `v0.4.2` and so on, each a GitHub Release with the Windows assets attached ([§5](#5-release-checklist) item 20). So cut a `v0.x.y` whenever fixes or changes merged since the last tag are ready for the user to play: a defect that tag shipped (a wrong constant reaching `main` is the failure mode this project has already hit, [design-audit.md §2](design-audit.md), T31, and it deserves its own tag so "which build had the bad number" is answerable), or a small change the user asked for after playing it (a UI tweak, wording, how an existing rule is presented; [§1.1](#11-what-each-position-means--decided)'s PATCH row, the user's decision of 2026-10-04). [§1.2](#12-what-crosses-each-boundary--decided) still decides the number: a change that adds a capability or changes a shipped preset's rules is a MINOR, not a patch. Ordinary forward progress toward the next capability jump is **not** a patch release; it is untagged commits on `main` until its gate is met.

### 2.2.1 v0.4.x patches

The user's decisions of 2026-10-04, recorded on [#690](https://github.com/diegoami/imperial_conquest_2/issues/690):

- **`v0.4.1` was cut from `main`, guarded; every later v0.4.x is cut from the maintenance branch `release/0.4`** ([§2.2.2](#222-two-release-lines-a-maintenance-branch-per-patched-minor), the user's decision of 2026-10-04 after `v0.4.1`). It ships everything merged on its line since the last tag plus the items on its list. Once `release/0.4` exists, it replaces the earlier "no maintenance branch" and "hold T125" ([PR 691](https://github.com/diegoami/imperial_conquest_2/pull/691)). The v0.5.0 work stays invisible in every v0.4.x release: none of it is on `release/0.4` (below).
- **An item the user puts on a v0.4.x list bypasses the playability gate** ([build-process.md §4.8](build-process.md#48-the-playability-gate-until-v050)). It becomes a task or a `fix` at once, labelled with its patch's `release:v0.4.x` label (`release:v0.4.1`). The gate still applies to every other finding.
- **`v0.4.1` is cut as soon as its first item, [T132](tasks/T132.md) (the hideable right-hand panel, [#690](https://github.com/diegoami/imperial_conquest_2/issues/690)), merges.** Items the user lists after that go to `v0.4.2`. `v0.4.1` ships T132 and everything merged since `v0.4.0`, notably the generated *authored* icon pack (T51, [#503](https://github.com/diegoami/imperial_conquest_2/pull/503); the default pack since fix 517, [#524](https://github.com/diegoami/imperial_conquest_2/pull/524); T101's toolbar keys, [#588](https://github.com/diegoami/imperial_conquest_2/pull/588)) and the game screen like the original's (T99–T113). The release note lists the rest from GitHub ([§4.1](#41-where-the-note-comes-from--decided-generated-at-cut-time-from-github-no-changelogmd)).
- **Every v0.4.x carries the Windows zip and installer**, built by T131's release workflow. If T131 has not merged when a v0.4.x is cut, the main session builds and attaches them by hand, as it did for `v0.4.0` on 2026-10-04.
- **The number is the user's** (the user's decision of 2026-10-04). A v0.4.x is numbered by the user, even when it carries what §1.2 would call a MINOR's content. `v0.4.1` does: T99–T113 add orders and dialogs a player could not reach before, and T120 changed both presets' melee matrix ([#657](https://github.com/diegoami/imperial_conquest_2/pull/657)). Its release note still lists those capability and preset changes, T120's matrix under [§4.2](#42-what-a-release-note-must-contain) item 2.
- **T132 runs before T131** (the user's decision of 2026-10-04), so that `v0.4.1` can be cut soon, with its assets built by hand if T131 has not merged; T131 runs right after T132. T132's Merge-after stays none.

**What these decisions supersede, for every v0.4.x until the `v0.5.0` tag** (`v0.4.1`, `v0.4.2`, …; the rules apply unchanged to every other release):

- [§1.1](#11-what-each-position-means--decided)'s PATCH row ("no new capability, no preset change") and [§1.2](#12-what-crosses-each-boundary--decided)'s MINOR test: a v0.4.x may carry new capabilities and preset changes merged on `main`, and is still a PATCH, numbered by the user.
- [§1.4](#14-do-phase-completions-get-tags--decided-no-capability-jumps-only)'s "only the six cut points get a tag": every v0.4.x gets an annotated tag and a GitHub Release.
- [§2.2](#22-patch-releases)'s "a patch only for a defect the tag shipped": a v0.4.x also ships the small changes the user asked for after playing, and everything merged on `main` before it.
- [§7](#7-summary)'s *Tags* row (six tags) and *Published by* row (the human for `v0.4.0`, `v0.5.0` and `v1.0.0`): every v0.4.x is tagged too, and the main session publishes it (below).

`v0.4.1` is tagged on `main`; every later v0.4.x is tagged on `release/0.4` ([§2.2.2](#222-two-release-lines-a-maintenance-branch-per-patched-minor), which amends [§3.3](#33-how-this-interacts-with-branch-per-task-and-squash-merge) item 3 and [§5](#5-release-checklist) item 17).

[§1.3](#13-the-save-format-version-is-a-separate-axis) is **not** superseded: no v0.4.x bumps the save schema. Every v0.4.x keeps `SaveFormat.CurrentVersion = 3`, and the guard below enforces it.

**How a v0.4.x stays guarded.** No v0.5.0 work may be reachable from a v0.4.x, whether by a new game, an attack or a loaded save. T125 is where the tactical battle enters the session: besides the `HumanBattles` switch (default `Instant`), it makes a save carry an open battle behind save schema 4, and **a loaded save holding a battle reopens it whatever the switch says** ([T125](tasks/T125.md), Hazards). A default switch alone is therefore not a guard. The guard is **the absence of T125 from `main`**:

1. **For `v0.4.1`, T125 was held** until `v0.4.1` was tagged on `main`. Nothing tactical was reachable there: T122's ruleset keys are read by no command, T123's and T124's engine is called by no session, and T128 adds asset files that no screen loads before T127.
2. **From `v0.4.1` on, the guard is the branch.** `release/0.4` starts at `v0.4.1`'s commit and receives only v0.4.x items ([§2.2.2](#222-two-release-lines-a-maintenance-branch-per-patched-minor)), never a v0.5.0 task, so T125 and everything after it merge on `main` freely. A v0.4.x item that would need a v0.5.0 change is brought to the user.
3. **The check at every v0.4.x cut**, run in PowerShell by the main session on the commit it is about to tag (`$cut`: on `main` for `v0.4.1`, on `release/0.4` after it), before `git tag -a`. These checks read the cut's tree, not commit subjects. Each one reads git's exit code, because `git grep` exits 1 when nothing matches and 128 or more when the command itself failed. All three must hold:
   - `git grep -n "HumanBattles" $cut -- src godot` prints nothing and `$LASTEXITCODE` is `1`. That means no match: the tree has no switch, so no session path to a tactical battle. Exit 0 (a match) or any other code (the command failed) fails the check.
   - `git grep -c "CurrentVersion = 3;" $cut -- src/IC2.Engine/Persistence/SaveFormat.cs` prints exactly one line ending in `:1`, and `$LASTEXITCODE` is `0`. The save schema is still 3, so no save can carry a battle, and §1.3 holds. Exit 1 means the line is gone; any other code means the command failed. Either fails the check.
   - The cut holds no v0.5.0 task. For `v0.4.1`, cut from `main`: `gh issue view 666 --json state,labels --jq '[.state, ([.labels[].name] | index("status:merged"))]'` prints `["OPEN",null]` and `$LASTEXITCODE` is `0`, so T125 had not merged. After `v0.4.1`: `git merge-base --is-ancestor $cut origin/release/0.4` exits `0`, so the cut is on the maintenance line. The two tree checks above still hold there, whatever T125 did on `main`.

   If any check fails, nothing is tagged and the user is asked.

**The main session publishes a v0.4.x without asking** (the user's decision of 2026-10-04; [§3.2](#32-who-cuts-the-tag--recommended-consistent-with-q-a)'s v0.4.x row). When the patch's listed tasks and fixes are merged and CI is green at the commit it cuts (on `main` for `v0.4.1`, on `release/0.4` after it), it runs the guard check above and the [§5](#5-release-checklist) checklist's agent lines, then: tags the release; creates the GitHub Release with its notes and the Windows zip and installer; publishes it; and installs the update on the user's machine by running the Release's `setup.exe` silently (`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`), which upgrades the existing install in place (the user's standing instruction). It reports the tag, the Release's link and the install's result to the user.

### 2.2.2 Two release lines: a maintenance branch per patched MINOR

The user's decision of 2026-10-04, after `v0.4.1`: *"we need separate paths for 0.4.x and 0.5.x"*. It is the standard gitflow case: patch releases for the shipped version, with fixes, while the next version is developed, and the next version needs those fixes too. It holds for every later line in the same way (`release/0.5` while `v0.6.0` develops, `release/1.0` after 1.0), opened when the user asks for patches on a line whose successor is already on `main`.

- **`release/0.4`** is created once by the main session from `v0.4.1`'s tagged commit (`git branch release/0.4 v0.4.1^{commit}`, then `git push origin release/0.4`). It is a long-lived branch: no force-push, and no deletion while the line lives. Every later v0.4.x is cut and tagged on it. **`main` is the v0.5 line**: T125 and the rest of v0.5.0 merge there as usual.
- **A v0.4.x item lands on `release/0.4` first.** That is a fix, or a small change the user lists after playing, carrying its `release:v0.4.x` label. Its branch (`task/T<nn>-<slug>` or `fix/<issue>-<slug>`) starts from `origin/release/0.4`, and its PR targets it (`gh pr create --base release/0.4`). It is reviewed and squash-merged under the same rules as any PR. In every brief, every `origin/main` reads `origin/release/0.4`, and the brief says so in its first lines. Its PR body says `Refs #<issue>`, not `Closes`: GitHub closes an issue only from a merge to the default branch.
- **Then it is ported forward to `main`.** Right after the release-branch merge, the main session opens a port PR against `main`. Its branch is `port/<issue>-<slug>`, made from `origin/main` with `git cherry-pick -x <the release-branch squash commit>`, and its body says `Closes #<issue>` and links the release-branch PR. How it is reviewed depends on the port:
  - a clean cherry-pick, whose diff is identical to the release-branch PR's, gets a re-check: the reviewer confirms that identity and that CI is green on `main`'s side;
  - an adapted port, where v0.5 code has moved the files, is reviewed in full at the original's tier;
  - when `main` no longer needs the item, because v0.5 code replaced what it fixes, the issue gets a comment saying why, the user is told, and the main session closes the issue by hand.

  The issue is `status:merged` only when both merges are done. A port is never skipped silently: it is how the next version gets the fixes too.
- **A defect found on `main` that a shipped v0.4.x also has** is fixed on `main` as usual. It is back-ported to `release/0.4` the same way, by a cherry-pick PR with `--base release/0.4`, but only if the user lists it for a v0.4.x.
- **`release/0.4` is never merged into `main`, and `main` is never merged into `release/0.4`.** Both histories are squash-merges, so a branch merge would replay every commit as a conflict. Commits cross between the lines only as cherry-pick PRs.
- **Concurrency** ([build-process.md §7](build-process.md#7-concurrency-single-instance-and-local-only), [§8](build-process.md#8-two-machines)) counts both lines. A v0.4.x item and a v0.5.0 task may be in flight together only if their files are disjoint, which the port will need anyway. `single-instance` still means one Godot process on the machine.
- **CI on the line.** `ci.yml` runs on every pull request, whatever branch it targets, but its `push` trigger lists `main` only, so a push to `release/0.4` runs nothing. Adding `release/**` to that trigger is the line's first fix. It lands on `release/0.4` and is ported to `main`, because a push runs the workflow file of the branch that was pushed. No v0.4.x after `v0.4.1` is cut until a push run on `release/0.4`'s tip is green.
- **The release workflow** ([T131](tasks/T131.md)) merges on `main` only, so a v0.4.x tag on `release/0.4` carries no `release.yml`. Its assets come from `gh workflow run release.yml -f tag=<tag>`, which runs `main`'s workflow and script on the tag's tree; [§5](#5-release-checklist) item 20 already accepts that dispatch. Until T131 merges, the main session builds the assets by hand.
- **The scripts.** `scripts/external-implement.ps1` starts a new branch from `origin/main` but resumes a pushed one. So for a v0.4.x item implemented by an OpenCode model, the main session first creates and pushes the branch from `origin/release/0.4`. `scripts/external-review.ps1` makes its worktree at the PR head, but its gate 0 diffs against `origin/main`, so the brief's first lines tell the reviewer to diff against `origin/release/0.4` instead. A `-Base` parameter for both scripts is a follow-up.
- **The line ends when the user says so.** `release/0.4` then takes no more items, and it stays, with its tags, as the record of what shipped.

### 2.3 What is explicitly *not* in this ladder

Nothing here commits to scope beyond `game-design.md`. The in-game scenario editor, network multiplayer and further asset packs are all in that document's "Open questions genuinely left for later" — they are **post-1.0 MINOR candidates**, not gates on any tag above, and no release note should imply otherwise.

The **tactical battle** is no longer one of them: it belongs to **`v0.5.0` *Battles*** (the user's decision of 2026-10-03, revised 2026-10-04; [#496](https://github.com/diegoami/imperial_conquest_2/issues/496)). Its gate row is in the ladder above, and `game-design.md`'s design principle 3 and its Combat section's "The tactical battle" carry the design.

---

## 3. Tag and GitHub Release conventions

### 3.1 Naming and placement — **DECIDED**

| Thing | Convention |
| --- | --- |
| Tag name | `v<MAJOR>.<MINOR>.<PATCH>` with optional `-rc.<N>`; must match `^v\d+\.\d+\.\d+(-rc\.\d+)?$` |
| Tag kind | **Annotated** (`git tag -a`), never lightweight — the message carries the gate (the issue numbers) so `git show <tag>` explains itself |
| Tag message subject | `v0.3.0 — Headless playable (closes #18–#23)` |
| Where | **`main` only**, always on a squash-merge commit — never on a task branch, never on a rebase artifact |
| GitHub Release | One per tag, always; title = the tag plus the ladder's short name (`v0.3.0 — Headless playable`); body = [§4](#4-release-notes); assets = the Windows zip and installer, from v0.4.0 on ([§5](#5-release-checklist) item 20) |
| Pre-1.0 marker | Every `0.x` release and every `-rc` is published with GitHub's **pre-release** flag set; only `v1.0.0` is a full release |
| Moving a tag | **Never.** Re-pointing a published tag is a destructive git operation under [build-process.md §4.5](build-process.md#45-when-to-escalate-to-the-user) case 10. A mistake is corrected by a new PATCH tag. |

### 3.2 Who cuts the tag — **RECOMMENDED, consistent with Q-A**

[Q-A](build-process.md#9-standing-governance-decisions) already granted the main session full merge autonomy **except** the four architecture PRs (T02, T03, T16, T22), and [Q-B](build-process.md#9-standing-governance-decisions) reserved *visual* judgment to the user. The consistent extension:

| Release | Tag + draft Release | Publish |
| --- | --- | --- |
| `v0.1.0`, `v0.2.0`, `v0.3.0` | The main session, autonomously | The main session, autonomously |
| `v0.4.0`, `v0.5.0`, `v1.0.0` (and any `-rc`) | The main session, autonomously, as a **draft** | **Human**, after the visual sign-off Q-B already requires |
| `v0.4.x` patches ([§2.2.1](#221-v04x-patches)) | The main session, autonomously, once the patch's listed items are merged and CI is green | The main session, autonomously, without asking, and it then installs the update on the user's machine (the user's decision of 2026-10-04) |

The reasoning is that a tag is a *consequence* of merges, not a new decision. Every merge in a `0.1`–`0.3` gate was one the main session was already authorized to make; refusing it the tag would add a human gate without adding a human judgment. `v0.4.0`, `v0.5.0` and `v1.0.0` are different in kind: their gating tasks (T24, T25, T27; T116 and T127 for `v0.5.0`) each carry "**+ human visual review**" in the task catalogue, so a person is in the loop *anyway* — the release simply inherits that gate rather than inventing a second one.

Worth noticing: **every release in the ladder already has a human touchpoint upstream of it**, with no new gate invented. `v0.1.0` inherits the T02/T03 architecture thumbs-up; `v0.2.0` inherits T16's; `v0.3.0` inherits T22's; `v0.4.0`, `v0.5.0` and `v1.0.0` inherit the Q-B screenshot reviews.

### 3.3 How this interacts with branch-per-task and squash-merge

[build-process.md §6](build-process.md#6-git-and-github-conventions) gives `main` a linear history of squash-merges, one per task. Tagging slots into that cleanly:

1. **The tag is cut right after the `/run-task` merge of the last gating task**, on that task's squash-merge commit, before its report ([build-process.md Appendix C](build-process.md#appendix-c-the-run-task-skill) step 4). It is *not* a separate later pass. The doc claims applied after the merge are docs-only and land after the tag.
2. **Nothing else is dispatched until the tag exists.** No other PR is merged between the last gating merge and the tag, so the tagged tree is exactly the tree the release checklist was run against. That removes the entire class of "the tag has a commit nobody tested" bug.
3. **No release branches, except a maintenance line the user asks for** ([§2.2.2](#222-two-release-lines-a-maintenance-branch-per-patched-minor)). With one machine and one task in flight at a time, a release branch would only create a second place for a fix to land and a merge-back to forget. One is opened only when patches are wanted for a line whose successor is already on `main`: `release/0.4`, from `v0.4.1` on (the user's decision of 2026-10-04). Its fixes reach `main` by cherry-pick PRs, never by a branch merge.
4. **A tag is never cut while a task branch is mid-rebase or a conflict is unresolved** ([build-process.md §4.5](build-process.md#45-when-to-escalate-to-the-user)), because merge order is dependency order and a conflicting branch means `main` is about to move for a reason the checklist did not see.

---

## 4. Release notes

### 4.1 Where the note comes from — **DECIDED: generated at cut time from GitHub. No `CHANGELOG.md`.**

A running `CHANGELOG.md` on `main` is rejected for the same reason [build-process.md §2.3](build-process.md#2-how-the-build-avoids-conflicts) rejects every other shared registry file: a file every task wants to append to conflicts with every task branch, and working around that by making one agent the only writer just moves the same information into a second place that can drift from GitHub. GitHub is already the state.

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
4. **Known gaps vs `game-design.md`** — which of M1–M21 are not complete, each with the issue number that will close it. Plus the still-open evidence items the shipped rulesets carry as `[open]` `_provenance`: `design-audit.md` Q9's paid case (where a foreign supply purchase's talents go), and anything else [operating-guide.md §7](https://github.com/diegoami/imperial_conquest_2/wiki/Open-questions) lists that a shipped rule depends on.
5. **Fidelity statement** — which golden fixtures from real play pass ([`full-battle-resolution-rome-vs-gaul.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/full-battle-resolution-rome-vs-gaul.md), [`battle-recording-melee-cap-confirmed.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/battle-recording-melee-cap-confirmed.md), [`army-to-army-transfer-confirmed.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/army-to-army-transfer-confirmed.md), [`galatia-elimination-and-city-resupply-confirmed.md`](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/reports/galatia-elimination-and-city-resupply-confirmed.md), as each becomes applicable), the determinism guard's status, and from `v0.3.0` the 50-seed AI soak result. This is the section that distinguishes this project from a generic reimplementation and it should read as evidence, not as a claim.
6. **Compatibility** — `SaveGame.schemaVersion`; whether the previous tag's saves load; whether original-`.sav` import still targets `classical-mediterranean` + `classical-faithful` only (it does, by policy).
7. **Artifacts and how to run them** — what is attached, the runtime prerequisites, and the local-only caveats (original-save import needs `assets.local.ini`; before `v1.0.0`, a Godot install).
8. **Open escalations** — any issue still labelled `status:escalated` or `needs-human` at cut time. A release that silently omits a known escalation is the same failure mode [build-process.md §4.3](build-process.md#43-the-dod-is-not-negotiable-by-an-agent) exists to prevent.

Two standing prohibitions, inherited from the project's evidence rules: **no number in a release note that is not in the fixtures corpus or a cited report**, and **no claim that a mechanic is faithful unless the note can name the report**. `[designed]` stays visibly labelled all the way out to the player.

### 4.3 Who reviews it — **the existing reviewer role, on the draft Release body**

No new role. The pipeline's roles ([build-process.md §3.1](build-process.md#31-the-roles)) already cover it, and the review contract in [§4.2](build-process.md#42-what-the-reviewer-checks) transfers to a release note almost unchanged:

| Review gate | Applied to a release note |
| --- | --- |
| 1. DoD independently reproduced | The reviewer re-runs the [§5](#5-release-checklist) checklist commands itself, in its own worktree at the tag (nothing else is dispatched meanwhile). The draft's claims are a convenience, never the proof. |
| 2. Provenance | Every number and fidelity claim in the note traces to the fixtures corpus or a cited report; every `[designed]` mechanic is labelled as one. |
| 3. Determinism | The determinism guard and the seeded-soak results are quoted from an actual run, not asserted. |
| 4. Scope | The note describes only what merged in the range — no forward promises, nothing from `game-design.md`'s "left for later" list. |
| 5. Correctness sweep | Not applicable; a release note has no code. |

**Model for the seat**: the same rule the catalogue uses for fidelity-critical PRs — **Opus / Medium**, because the failure mode here (a wrong fidelity claim shipped to a player) is the one this project has already paid for. The reviewer approves by commenting on the draft Release or on the gate's issues; the main session publishes (or, for `v0.4.0`/`v1.0.0`, hands to the human). This is the same *judge separate from executor* separation [build-process.md §4.1](build-process.md#41-the-path-a-task-takes) already argues for on merges, for the same audit-trail reason.

---

## 5. Release checklist

The concrete "done when" for cutting a release, in the task catalogue's style: **one line, one runnable check**, with human sign-off only where a machine genuinely cannot decide. Lines marked *(from vX)* apply only from that tag onward.

**Done when:**

0. **The docs pass.** Every document claim the gate's merges made stale is corrected in one `Docs:` commit before the tag: the design documents' formulas and `[open]` tags, the investigations index, and the wiki's living pages. Per-merge prose syncing was retired on 2026-09-18 ([build-process.md §4.7](build-process.md#47-after-a-merge)), so this is where it is collected.
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
11. *(v1.0.0 only)* The packaging script's export launches and loads a scenario from a shell with scrubbed `PATH`/`DOTNET_ROOT`, exit 0 (T27 DoD): `pwsh scripts/package.ps1 -Verify`.
12. *(v1.0.0 only)* The nightly workflow (T28) has a green **scheduled** run within the last 24 hours, not merely a green manual dispatch.
13. *(v1.0.0 only)* The packaged artifact contains **no file originating from the user's original installation** — no `.exe`/`.dat`/`.hlp`/`.cnt`/`.wav`/`.sav` from `imp_conq_original`, asserted by a manifest scan of the export, not by inspection. This is the project's oldest standing constraint and the one release step where a mistake is public and irreversible.
14. The release note's preset table was **regenerated** from `data/rulesets/*.json` and diffs clean against the draft — the table is never typed by hand.
15. The known-gaps section lists exactly the design milestones whose tasks are not all `status:merged`, derived mechanically from GitHub, not written from memory.
16. The reviewer agent ([§4.3](#43-who-reviews-it--the-existing-reviewer-role-on-the-draft-release-body)) has re-run items 4–13 itself and approved the draft.
17. The tag is annotated, on `main` (or, for a patch on a maintenance line, on its `release/X.Y` branch, [§2.2.2](#222-two-release-lines-a-maintenance-branch-per-patched-minor)), on a squash-merge commit, matches the name pattern, and does not already exist — `git tag -l <tag>` is empty before `git tag -a`.
18. **Human** *(v0.4.0, v0.5.0, v1.0.0 only)*: visual sign-off given on every screenshot posted by T24/T25/T27 (and, for `v0.5.0`, by T127), per [Q-B](build-process.md#9-standing-governance-decisions).
19. **Human** *(v0.4.0, v0.5.0, v1.0.0 only)*: the draft Release is published by the user. For `v0.1.0`–`v0.3.0`, the main session publishes (see [§3.2](#32-who-cuts-the-tag--recommended-consistent-with-q-a)).
20. *(from v0.4.1; every tag, milestone or patch)* **The Windows assets are on the Release before it is published.** Pushing the tag runs the release workflow ([T131](tasks/T131.md), `.github/workflows/release.yml`), which builds the tag with `scripts/release-assets.ps1` and attaches `ImperialConquest2-<tag>-windows-x64.zip` and `ImperialConquest2-<tag>-windows-x64-setup.exe` to the tag's Release, creating it as a draft when none exists. Checked by `gh release view <tag> --json assets` listing both files, which holds whichever run attached them, the tag push or a later dispatch, and by the run that did: `gh run list --workflow release.yml --json event,headBranch,displayTitle,conclusion,url --limit 20` shows a `success` run for the tag (event `push` with `headBranch` `<tag>`, or event `workflow_dispatch` whose title names `<tag>`; the workflow's `run-name` puts the tag in the title). It runs after item 17 and before item 19. The workflow never replaces an asset a Release already holds; a re-upload is the user's, by hand.

Items 1–17 and 20 are checkable by an agent. Items 18–19 are the only human steps, and neither is new — both are [Q-B](build-process.md#9-standing-governance-decisions)'s existing answer applied at the release boundary.

**If a check fails**, the release does not get cut and nothing is tagged. Fix forward on `main` and re-run the checklist; a failed checklist is never worked around by weakening a line, for exactly the reason [build-process.md §4.3](build-process.md#43-the-dod-is-not-negotiable-by-an-agent) gives about DoDs.

**Item 20 is the exception, because it runs after the tag exists.** If the release workflow fails, the tag stays: it is never moved or deleted (§3.1). The Release stays a draft and is not published (item 19) until it carries both assets. When the cause is the runner or the workflow and not the tagged tree, the main session fixes it on `main` and re-runs the workflow with `gh workflow run release.yml -f tag=<tag>` (a dispatch runs `main`'s workflow and script on the tag's tree). When the tagged tree itself cannot be packaged, the fix merges on `main` and ships in the next patch release (§2.2), which gets its own assets; the failed tag's draft stays unpublished, and the user decides what becomes of it.

---

## 6. What was created in GitHub, and what was not

**Created: `release:*` labels**, applied one per task issue — the first release whose gate includes it. Five were created with this document; `release:v0.5.0` is created when [#496](https://github.com/diegoami/imperial_conquest_2/issues/496)'s tasks are filed.

| Label | Applied to |
| --- | --- |
| `release:v0.1.0` | #1–#12, #37 (T30), #45 (T31), #60–#62 (T32–T34), #84 (T40), #89 (T41), #92 (T42), #110 (T43) |
| `release:v0.2.0` | #13–#17, #63 (T35), #78 (T38), #81 (T39) |
| `release:v0.3.0` | #18–#23, #32 (T29), #64 (T36), #70 (T37) |
| `release:v0.4.0` | #24, #25, #26 |
| `release:v0.5.0` | #635 (T116), and T122–T130's issues |
| `release:v1.0.0` | #27, #28 |

This makes checklist items 1 and 15 a single `gh issue list` query instead of a hand-maintained list, it is additive and reversible like every other label, and it does not disturb anything the task loop relies on.

**Deliberately *not* created: GitHub milestones for the version tags.** A GitHub issue carries **exactly one** milestone, and task issues use theirs for the build **phase**, which [build-process.md §6](build-process.md#6-git-and-github-conventions) names as a convention. A version milestone could therefore only be empty — permanently 0/0, sitting in the milestone list next to the four meaningful phase ones and inviting exactly the phase-vs-version confusion this document opens by warning about — or it could displace a phase milestone, which would break a documented convention to gain nothing. Labels are multi-valued; milestones are not; the gate is a set, so it is a label. If a future release ever needs its own *new* issues (a `v1.0.1` bugfix batch, say), a milestone for that batch is the right tool at that time.

**`v0.1.0` was tagged on 2026-09-18**, with its checklist run at `2456cd8` (T37's squash merge). *(Corrected 2026-09-24: the annotated tag object itself points to `b84635f`, the docs commit that records it, #140, not to `2456cd8` as first written here. `git rev-parse v0.1.0^{commit}` shows it. §3.1 asks for a squash-merge commit, and the tag cannot be moved (§3.1), so this stays as a recorded deviation.)* It was tagged once [§2](#2-the-release-ladder)'s gate set — #1–#12, #37, #45, #60–#62, #84, #89, #92, #110 — was closed and `status:merged` throughout. The annotated tag is the release; **no GitHub Release body was published**, because the user asked for the tag alone. Checklist [§5](#5-release-checklist) items 1–6 and 17 were run and passed:

| Item | Evidence |
| --- | --- |
| 0 | This commit. |
| 1 | `gh issue list --label release:v0.1.0` — every issue closed and `status:merged`. |
| 2 | No issue anywhere carries `status:escalated`. |
| 3 | No PR open against `main` (so none approved and left out). |
| 4 | CI on `main`'s tip `2456cd8`: `success`. |
| 5 | A **fresh clone** of `2456cd8` built with **0 warnings, 0 errors**; `dotnet test IC2.sln` gave **1,675 engine tests passed, 35 data tests passed, 0 failed**. The 90 skipped data tests are the ones needing the user's original files, which a clone does not have. |
| 6 | The determinism guard and fixtures-corpus tests were run by name in that clone: 107 engine and 6 data tests passed, 0 failed. |
| 17 | `git tag -l` was empty beforehand; the tag is annotated, on `main`, on a squash-merge commit, and matches the pattern. |

Items 14–16 concern a release-note body and its reviewer approval, and were **not** run — there is no note to regenerate a preset table for, derive known gaps for, or review. If a Release body is published for `v0.1.0` later, those three run then.

One thing the tag's history contains that its gate set does not: **T44** (#126, `e03608c`) merged after the gate was already met, so `v0.1.0` includes the signed army `moves` field — a developer running `IC2.Inspect` at this tag sees `moves -1`, not `65535`. The gate set was not reopened to add it; it is simply worth knowing when reading the tag's diff.

---

**`v0.3.0` was tagged and published on 2026-09-24**, annotated, on `fe0ff10` (T77's squash merge), as a pre-release: [release](https://github.com/diegoami/imperial_conquest_2/releases/tag/v0.3.0). The gate is the 15 issues labelled `release:v0.3.0`. It was the first release to go through [milestone-review.md](milestone-review.md): the review PR is [#343](https://github.com/diegoami/imperial_conquest_2/pull/343) (closed unmerged), with Fable 5.1 as the evidence checker and DeepSeek as the cold reader. There was no blocking finding and no claim was NOT MET. C9 was PARTLY MET, and its two literals are #344 and #345. C6's and C10's wording was corrected visibly in the milestone description. Checklist evidence:

| Item | Evidence |
| --- | --- |
| 0 | This commit, plus `b6e421b` and `e6c3069` earlier the same day. |
| 1 | `gh issue list --label release:v0.3.0`: 15 issues, all closed and `status:merged`. |
| 2 | No issue carries `status:escalated`. |
| 3 | The open PRs, #343 (the review PR) and #349 (a plan), carry no `status:approved`. |
| 4 | CI on `main`'s tip `fe0ff10`: `success`. |
| 5 | Fresh clone and worktree at `fe0ff10`: **0 warnings**. With the original files, 226 data and 2,660 engine tests passed, 0 skipped. Without them, 86 + 2,637 passed, 163 skipped (every skip named), 0 failed. |
| 6 | Determinism guard (`DeterminismGuardTests`, `TurnPipelineDeterminismTests`, `AiDeterminismTests`, `BattleDeterminismTests`, `RngGoldenVectorTests`) and fixtures-corpus tests (`FixturesCorpusTests`, `RulesetMatchesFixtureCorpusTests`, `CorpusSweepTests` ×100) all green, named individually in the release reviewer's run. |
| 7 | `demo.golden.txt` reproduced byte for byte (11,107 bytes, same sha256). |
| 8 | 50-seed soak: 0 rejected, 0 exceptions, worst stall run 1, 1.39 s (Fable) and 2.57 s (release reviewer) of a 300 s budget. |
| 9 | Import verified locally by the Fable 5.1 reviewer and the release reviewer. Three representative saves round-trip, three `IP*.sav` pin edge cases, and 20 real-save and 5 rejection tests pass. |
| 14 | The preset table was regenerated from both rulesets by the release reviewer and matches the note. |
| 15 | Known gaps derived from the task index against issue state: M3 (T72), M18 (T24, T25), M19 (T26), M20 (T27). |
| 16 | The release-note reviewer (Opus) re-ran items 4–9 and requested 8 edits (R1–R8, on #343). They were applied verbatim before publishing. |
| 17 | `git tag -l v0.3.0` was empty; the tag is annotated, on `main`, on a squash-merge commit. |

Items 10–13 and 18 are v0.4.0 or v1.0.0 only. Item 19: the main session published, per §3.2, after the user said to.

---

**`v0.4.0`'s Windows assets were attached by hand on 2026-10-04**, after the Release was published, by the user's decision of that day that every release carries a Windows build a player can start without Godot. The main session packaged the tag (`99ba0d3`) with `main`'s `scripts/package.ps1` and `godot/export_presets.cfg` copied onto it, since T27 merged after the tag; the tag lacks `-Verify`'s check scene, so a headless launch with a scrubbed `PATH` and `DOTNET_ROOT` stood in for it and exited 0. It attached `ImperialConquest2-v0.4.0-windows-x64.zip` (74.7 MB) and `ImperialConquest2-v0.4.0-windows-x64-setup.exe` (52 MB, Inno Setup 6.7.3, a per-user install that upgrades in place), the installer tested by a silent install, a headless launch and a silent uninstall that left no file. [T131](tasks/T131.md) automates the same steps for every later tag ([§5](#5-release-checklist) item 20).

---

## 7. Summary

| | |
| --- | --- |
| Scheme | SemVer 2.0.0, `v`-prefixed, annotated tags on `main`, or on a maintenance line's `release/X.Y` for its patches (§2.2.2); `0.x` through the build, `-rc.N` only ahead of `v1.0.0` |
| Tags | Six on the ladder: `v0.1.0`, `v0.2.0`, `v0.3.0`, `v0.4.0`, `v0.5.0`, `v1.0.0` — at capability jumps, **not** at phase boundaries — plus regular patch releases between them ([§2.2](#22-patch-releases)) |
| Gate | A set of merged task issues, tracked by a `release:*` label; never a date |
| Cut by | The main session, right after merging the last gating task, before dispatching the next |
| Published by | The main session for `v0.1.0`–`v0.3.0`; the **human** for `v0.4.0`, `v0.5.0` and `v1.0.0`, inheriting Q-B's visual sign-off |
| Notes | Generated at cut time from GitHub + the shipped ruleset JSON; no `CHANGELOG.md`; eight required sections, presets and `[designed]` mechanics among them |
| Reviewed by | The existing reviewer role (Opus / Medium), applying build-process.md §4.2's gates to the draft Release body |
| Blocking constraint | 20 checklist lines; 18 agent-checkable, 2 human; item 20, the Windows assets, added 2026-10-04 by the user's decision |
