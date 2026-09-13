<!--
PR body contract for a build task (docs/build-process.md §4.1, §4.2 and §6).
Keep every heading below: the orchestrator and the reviewer read them by name.
Title convention: "T<nn> <Task name>", e.g. "T09 Movement and terrain".
-->

Closes #<issue>

## Task

- **Task**: T<nn> <name> — [catalogue entry](https://github.com/diegoami/imperial_conquest_2/blob/main/docs/task-catalogue.md#t<nn>-<slug>)
- **Branch**: `task/T<nn>-<slug>`

## Scope: files touched vs the Owns list

<!-- Gate 4 (build-process.md §4.2). List every changed path and the Owns pattern from the task entry
     that covers it. A file outside the Owns list is a review finding even if the change
     is good; declare it here rather than letting the reviewer discover it. -->

| Changed path | Owns pattern that covers it |
| --- | --- |
| `<path>` | `<pattern>` |

- [ ] Every changed file above is inside the task's declared **Owns** list.
- [ ] No change to `docs/task-catalogue.md`, `docs/build-process.md` or any other document (documentation is updated on `main` after the merge, build-process.md §4.8), original game files, `assets.local.ini`, `.gitignore`'s exclusion policy, or the research repository's reports (each of those is an escalation, build-process.md §4.6).

## DoD evidence

<!-- Gate 1 (build-process.md §4.2). One entry per "Done when" line of the task entry, in order: the exact
     command run and the tail of its output. The reviewer re-runs every command itself;
     this block is a convenience, never the proof. A DoD line with no runnable check is
     itself a finding — say so here instead of inventing one. -->

```text
DoD 1: <quote the "Done when" line>
$ <command>
<output tail>

DoD 2: <quote the "Done when" line>
$ <command>
<output tail>
```

## Provenance checklist

<!-- Gate 2 (build-process.md §4.2). -->

- [ ] Every constant introduced or changed traces to a `tests/fixtures` entry, a cited research-repo report, or a `docs/investigations/` document.
- [ ] Every `[designed]` value states **what was searched and came up empty** before it was designed (design-audit.md §4.5, promoted to a merge gate).
- [ ] Nothing was invented to fill a gap: a value that could not be found in a report is omitted and listed below, not guessed.

Values omitted because no source was found (or "none"):

- none

## Determinism

<!-- Gate 3 (build-process.md §4.2). -->

- [ ] No `System.Random`, wall-clock reads, `Guid.NewGuid`, or order-dependent iteration in gameplay paths; every random draw goes through `IRng`; a seeded test proves reproducibility (or this PR touches no gameplay path — say which).

## Docs affected

<!-- Input to the post-merge documentation update (build-process.md §4.8); the reviewer checks
     it is plausible for the diff (gate 4). Do NOT edit these documents in this PR. For each,
     say what should change after merge and why, or delete the line. Status (the catalogue's
     Status lines, operating-guide §1, README "Current state", release-plan §2.1) is
     synced automatically and need not be listed. -->

- `docs/design-audit.md` / `docs/game-design.md`: <claim confirmed, corrected or closed>
- `docs/task-catalogue.md`: <dependency edge or wave note changed>
- `docs/investigations/README.md`: <new investigation added>
- `docs/release-plan.md`: <release gate changed>
- `docs/operating-guide.md` §7: <research item opened or closed>
- `README.md`: <build/test instructions or runnable surface changed>

Or: none.

## Notes for the reviewer

<!-- Anything you could not verify, any deviation from the task entry, any open question
     for the human. An honest "could not verify X, checked Y instead" beats a silent claim. -->
