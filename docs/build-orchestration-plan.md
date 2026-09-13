# Build orchestration plan (split)

This document was split in two and holds no content of its own; follow the links. [build-process.md](build-process.md) is the process — roles, review and merge, the orchestrator, the bug list, the documentation step, the prompt templates and `/build-tick`. [task-catalogue.md](task-catalogue.md) is the tasks — the dependency graph, every task entry, the task index and status. [operating-guide.md](operating-guide.md) replaces the former `docs/HANDOVER.md` and `AGENTS.md`.

The page stays so that references to it — in code comments, `_provenance` strings, closed issues and merged pull requests — still resolve. A reference to `build-orchestration-plan.md §N` maps as follows; a reference to a task entry (`T07 DoD 4`) is the same entry, same numbering, in the task catalogue.

| Old section | Now |
| --- | --- |
| §0 Where things stand, and what you can test | [operating-guide.md §1](operating-guide.md#1-current-state) |
| §1 What the pipeline has to work around | [build-process.md §1](build-process.md#1-constraints-the-pipeline-works-around) |
| §2 What makes the parallelism possible (§2.1–§2.5) | [build-process.md §2](build-process.md#2-how-the-build-avoids-conflicts) (same 2.1–2.5 numbering) |
| §3 Roles, models, and the effort scale (§3.1–§3.5) | [build-process.md §3](build-process.md#3-roles-models-and-the-effort-scale) (same numbering) |
| §4 The dependency graph; §4.1 waves; §4.2 sequential vs parallel | [task-catalogue.md §1](task-catalogue.md#1-the-dependency-graph), §1.1, §1.2 |
| §5 The task catalogue, and every T-entry | [task-catalogue.md §2](task-catalogue.md#2-the-tasks) — entry anchors unchanged (below) |
| §6.1–§6.6 The review and merge pipeline | [build-process.md §4.1–§4.6](build-process.md#4-the-review-and-merge-pipeline) |
| §6.7 The bug list | [build-process.md §4.7](build-process.md#47-the-bug-list) |
| (new) Documentation update after every merge | [build-process.md §4.8](build-process.md#48-documentation-update-after-every-merge) |
| §7.1–§7.5 The orchestrator (who runs it, state, one tick, conflicts, pause) | [build-process.md §5.1–§5.5](build-process.md#5-the-orchestrator) |
| §8 Git and GitHub conventions | [build-process.md §6](build-process.md#6-git-and-github-conventions) |
| §9 Concurrency, single-instance, and local-only | [build-process.md §7](build-process.md#7-concurrency-single-instance-and-local-only) |
| §10 Adding a second machine later | [build-process.md §8](build-process.md#8-adding-a-second-machine-later) |
| §11 Open questions for the user (Q-A–Q-D) | [build-process.md §9](build-process.md#9-standing-governance-decisions) |
| §12 Task index | [task-catalogue.md §3](task-catalogue.md#3-task-index) |
| Appendices A, B, C (prompt templates, `/build-tick`) | [build-process.md Appendix A](build-process.md#appendix-a-implementer-prompt-template), [B](build-process.md#appendix-b-reviewer-prompt-template), [C](build-process.md#appendix-c-the-build-tick-skill) |

## Task entries

Old links to a task's anchor land here; each heading links to the entry.

#### T01 Build scaffolding and CI

[task-catalogue.md → T01](task-catalogue.md#t01-build-scaffolding-and-ci)

#### T02 Core domain model and JSON round-trip

[task-catalogue.md → T02](task-catalogue.md#t02-core-domain-model-and-json-round-trip)

#### T31 Correct `Ruleset.Siege`'s defender-strength field identities

[task-catalogue.md → T31](task-catalogue.md#t31-correct-rulesetsieges-defender-strength-field-identities)

#### T30 Harden `IC2.Data`: army tombstones, and the DAT's own file layout

[task-catalogue.md → T30](task-catalogue.md#t30-harden-ic2data-army-tombstones-and-the-dats-own-file-layout)

#### T29 Export the shipped classical-mediterranean world and ruleset

[task-catalogue.md → T29](task-catalogue.md#t29-export-the-shipped-classical-mediterranean-world-and-ruleset)

#### T03 Engine seams: RNG, turn pipeline, commands, events

[task-catalogue.md → T03](task-catalogue.md#t03-engine-seams-rng-turn-pipeline-commands-events)

#### T04 Fixtures corpus

[task-catalogue.md → T04](task-catalogue.md#t04-fixtures-corpus)

#### T05 GitHub hygiene: templates, labels, CODEOWNERS

[task-catalogue.md → T05](task-catalogue.md#t05-github-hygiene-templates-labels-codeowners)

#### T06 Calendar and turn sequencing

[task-catalogue.md → T06](task-catalogue.md#t06-calendar-and-turn-sequencing)

#### T07 Strength functions

[task-catalogue.md → T07](task-catalogue.md#t07-strength-functions)

#### T08 Economy, supply, and purses

[task-catalogue.md → T08](task-catalogue.md#t08-economy-supply-and-purses)

#### T09 Movement and terrain

[task-catalogue.md → T09](task-catalogue.md#t09-movement-and-terrain)

#### T10 News log ring buffer and message catalog

[task-catalogue.md → T10](task-catalogue.md#t10-news-log-ring-buffer-and-message-catalog)

#### T11 Asset pack loader and generated placeholder pack

[task-catalogue.md → T11](task-catalogue.md#t11-asset-pack-loader-and-generated-placeholder-pack)

#### T12 Victory conditions

[task-catalogue.md → T12](task-catalogue.md#t12-victory-conditions)

#### T13 Recruitment and mercenaries

[task-catalogue.md → T13](task-catalogue.md#t13-recruitment-and-mercenaries)

#### T14 Naval

[task-catalogue.md → T14](task-catalogue.md#t14-naval)

#### T15 Army and unit management

[task-catalogue.md → T15](task-catalogue.md#t15-army-and-unit-management)

#### T16 Battle resolution — all three variants

[task-catalogue.md → T16](task-catalogue.md#t16-battle-resolution--all-three-variants)

#### T17 City capture, siege, and the defection cascade

[task-catalogue.md → T17](task-catalogue.md#t17-city-capture-siege-and-the-defection-cascade)

#### T18 City orders (fortification)

[task-catalogue.md → T18](task-catalogue.md#t18-city-orders-fortification)

#### T19 Diplomacy

[task-catalogue.md → T19](task-catalogue.md#t19-diplomacy)

#### T20 New-format save/load and versioning

[task-catalogue.md → T20](task-catalogue.md#t20-new-format-saveload-and-versioning)

#### T21 Original-save import bridge

[task-catalogue.md → T21](task-catalogue.md#t21-original-save-import-bridge)

#### T22 AI

[task-catalogue.md → T22](task-catalogue.md#t22-ai)

#### T23 Command layer and headless CLI harness

[task-catalogue.md → T23](task-catalogue.md#t23-command-layer-and-headless-cli-harness)

#### T24 Godot main game screen

[task-catalogue.md → T24](task-catalogue.md#t24-godot-main-game-screen)

#### T25 Battle result, diplomacy, and hotseat handoff screens

[task-catalogue.md → T25](task-catalogue.md#t25-battle-result-diplomacy-and-hotseat-handoff-screens)

#### T26 Scenario authoring docs and example scenarios

[task-catalogue.md → T26](task-catalogue.md#t26-scenario-authoring-docs-and-example-scenarios)

#### T27 Packaging

[task-catalogue.md → T27](task-catalogue.md#t27-packaging)

#### T28 Nightly regression and soak gate

[task-catalogue.md → T28](task-catalogue.md#t28-nightly-regression-and-soak-gate)
