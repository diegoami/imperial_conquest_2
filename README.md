# Imperial Conquest 2 research and reimplementation

A modern, moddable reimplementation of *Imperial Conquest 2* (1996) — the game design, the build/release plans, and all code. Does **not** contain the game's executables, data, help files, sounds, or saves; supply files from your own installation when using the tools that read them.

**The reverse-engineering research lives in a separate repository: [diegoami/imperial-conquest-2-research](https://github.com/diegoami/imperial-conquest-2-research)** — save/DAT format writeups, decompiled formulas, evidence-based reports. This repo cites those reports directly.

## Current state

A snapshot, synced after every merge by the documentation step ([build-process.md §4.8](docs/build-process.md#48-documentation-update-after-every-merge)); the live per-tick status is [tracking issue #29](https://github.com/diegoami/imperial_conquest_2/issues/29).

- **As of** `a53eaa5`: Phase 0 (foundation) merged, Phase 1 (pure rules) under way — **12 of 37 build tasks merged**: T01–T09, T30, T31, T32. Per-task status is in the [task index](docs/task-catalogue.md#3-task-index).
- **What exists**: `IC2.Data` (the original `.sav`/`.dat` parsers), the domain model and toy world, the fixtures corpus (389 constants from the research reports), the engine seams (seeded RNG, turn pipeline, commands, events), calendar and turn sequencing, the strength functions, movement/terrain (the one-click Bresenham walker, terrain costs, blocking, abort rules), and economy, supply and purses (tax, quarterly upkeep, weather-event frequency, supply as a purchased economy with the dialog capacity cap, and the supply→army-morale rule). No gameplay loop yet.
- **Next**: T10 (news log) is **in progress**, dispatched. The ready set is T11, T12, T14, T33, T34, T35 — T08's merge unblocked T14 and T35. T29 is still blocked (waiting on T30, T34, T15, T17, T19, T37).
- **Nothing is playable yet.** The first runnable program is the `IC2.Cli` harness (T23); the first screen is T24. What becomes runnable when: [operating-guide.md §1.1](docs/operating-guide.md#11-what-becomes-runnable-and-when).
- **Build and test now**:
  ```bash
  dotnet build IC2.sln   # 0 warnings, 0 errors
  dotnet test IC2.sln    # 420 tests: 342 engine, 78 data
  ```
  65 of the data tests read your original game files and skip without `assets.local.ini` (below). CI: [Actions](https://github.com/diegoami/imperial_conquest_2/actions).

Operating the project — the build pipeline, the skills, where everything lives, pausing, bugs: [docs/operating-guide.md](docs/operating-guide.md).

## Building the reimplementation

`IC2.Engine` is the headless game engine; its rules are being filled in task by task ([task-catalogue.md](docs/task-catalogue.md)). `IC2.Cli` (a scriptable play harness) is still a scaffolded stub; once it lands (T23), this section will carry its usage.

The shipped data files are `data/worlds/toy-3city.json`, `data/rulesets/toy-ruleset.json` and `data/scenarios/toy-3city.json` — a deliberately small 3-city / 2-nation fixture for tests. The real 334-city `classical-mediterranean` world and the `classical-faithful` ruleset are exported later, by T29.

`tests/fixtures/corpus.json` is the evidence base the rules are built against: every exact number from the research reports, transcribed once, each with its source report and tag. `tests/fixtures/known-reports.json` pins the real report filenames so a typo'd or invented citation fails CI offline, without the research repo being cloned.

## The research-inspector tools (`IC2.Inspect`)

Predates the reimplementation — a read-only C#/.NET CLI and Godot viewer for original `.DAT`/`.sav` files, built during the reverse-engineering phase and still useful for inspecting real save data. Point it at your own files:

```ini
# assets.local.ini (copy from assets.example.ini; git-ignored, machine-specific)
[assets]
directory = C:\path\to\imp_conq_original
```

Saves go in a `saves` subfolder of that directory; screenshots (`<save-number>.<image-number>.png`) and recordings (`.mp4`) in sibling `screenshots`/`recordings` folders — both git-ignored, reference material only.

```text
dotnet build src/IC2.Inspect/IC2.Inspect.csproj
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --save "saves/1,sav.sav"
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --compare-saves saves/1.sav saves/4.sav
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --inspect-city saves/11_supply.sav Rome
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --inspect-army saves/11_supply.sav 100 42
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --inspect-nation saves/11_ptol.sav Ptolemaic
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --inspect-turn saves/12_ptol.sav
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --list-armies saves/11_supply.sav Rome
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --list-fleets saves/11_supply.sav
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --list-mercenaries saves/11_supply.sav
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --to-json saves/11_supply.sav rendered/11_supply.json
dotnet run --project src/IC2.Inspect/IC2.Inspect.csproj -- --render-map rendered/map.svg
```

Run from the repository root. `--compare-saves` reports hashes, map transitions, and city/army/fleet-record byte changes between two saves. `--inspect-city`/`--inspect-army`/`--inspect-nation`/`--inspect-turn` print one entity's decoded fields. `--list-armies`/`--list-fleets`/`--list-mercenaries` enumerate without needing coordinates. `--to-json` dumps everything known about a save (turn, all nations, all cities with garrisons, all armies, all fleets, all mercenary offers) to one file. `--render-map` writes an SVG from the DAT's terrain/cities. `--config <path>` selects a different INI for automation. Never executes or modifies the original game.

### Godot map viewer (part of the inspector, not the reimplementation's UI yet)

After setting `assets.local.ini`, open `godot/project.godot` with Godot .NET 4.7.2 and run the project — draws the 320×140 map and 334 cities, click a city/army/fleet marker for its decoded fields, a nation selector, and the save's calendar header. Read-only; no turns or commands yet (that's what `IC2.Cli`/the Godot screens in the build plan will add).

## Further reading

- [Operating guide](docs/operating-guide.md) — start here: current state, where everything lives, how the sessions, skills and pipeline are run, what's still open.
- [Game design](docs/game-design.md) and its [design audit](docs/design-audit.md) — what the reimplementation will be, and what the evidence actually supports.
- [Task catalogue](docs/task-catalogue.md) — the 37 build tasks, their dependency graph and status.
- [Build process](docs/build-process.md) — how tasks are dispatched, reviewed, merged and documented by the agent pipeline.
- [Evidence pipeline](docs/evidence-pipeline.md) — the `/process-evidence` skill that turns new saves/recordings/notes into research-repo findings and then game-design implications.
- [Investigations](docs/investigations/README.md) — this repository's own evidence write-ups.
- [Release plan](docs/release-plan.md) — how tasks turn into version tags and releases.
- Research repository ([diegoami/imperial-conquest-2-research](https://github.com/diegoami/imperial-conquest-2-research)): [roadmap](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/roadmap.md), [research notes](https://github.com/diegoami/imperial-conquest-2-research/blob/main/docs/research.md), and the full [reports index](https://github.com/diegoami/imperial-conquest-2-research/tree/main/docs/reports) — 49 evidence-based findings the design cites throughout.
