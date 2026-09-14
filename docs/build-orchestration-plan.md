# Build orchestration plan (superseded)

This page holds no content of its own. It stays so that old references to it still resolve: in code comments, `_provenance` strings, closed issues and merged pull requests.

- **The process** (roles, the task loop, review, bugs, templates) is [build-process.md](build-process.md). The orchestrator layer this plan described was retired on 2026-09-14. The main session now runs tasks directly with `/run-task`.
- **The tasks, their dependency graph and index** are in [task-catalogue.md](task-catalogue.md). Each task's entry anchor is unchanged (below).
- **Where the build stands** is on GitHub's labels ([operating-guide.md §1](operating-guide.md#1-where-the-build-stands)).

A reference to "`build-orchestration-plan.md` T<nn> … Done-when N" means the same Done-when line in that task's catalogue entry, unless the catalogue has since amended it.

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
